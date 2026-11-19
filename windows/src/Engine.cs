// Cliport v1.0 - sync engine: protocol, pins (LWW set), device registry, file transfer.
// All state is touched on the UI thread only (network threads marshal through `post`).
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Cliport
{
    sealed class PinItem { public string Id, Text; public long Ts; public bool Del; }
    sealed class FileEntry { public string Name, Mime, Path; public long Size; }
    sealed class Offer
    {
        public string Fid, From, FromName; public bool Image, Text; public long Ts;      // Text: a very large text clip travelling as a .txt file
        public List<FileEntry> Files = new List<FileEntry>();
        public long TotalSize { get { long n = 0; foreach (var f in Files) n += f.Size; return n; } }
    }
    sealed class DeviceInfo
    {
        public string Id, Name, Plat, Ver, RdpHost, RdpUser; public bool Online, RdpOn; public long Ts; public int RdpPort = 3389;
        public List<string> Addrs = new List<string>();
        public bool IsOnline { get { return Online && Math.Abs(App.Now() - Ts) < 300000; } }
    }

    sealed class Engine
    {
        public const int MaxInlineText = 180000;            // bytes; larger text travels as a file
        public const long MaxRelayFile = 25L * 1024 * 1024; // without Telegram storage
        const int Chunk = 48000;
        const int TgPart = 18 * 1024 * 1024;
        const long TgFrom = 4L * 1024 * 1024;               // files above this go through Telegram storage when it is configured

        public readonly Config Cfg; public KeyRing Keys; public RelayHub Hub;
        readonly Action<Action> post;
        public readonly List<PinItem> Pins = new List<PinItem>();
        public readonly Dictionary<string, DeviceInfo> Devices = new Dictionary<string, DeviceInfo>();
        public readonly List<string> History = new List<string>();
        public bool Paused, Demo;

        public Action<string, string> TextIn;        // text, fromName
        public Action<Offer> OfferIn;
        public Action PinsChanged, DevicesChanged, StatusChanged, HistoryChanged;
        public Action<string, string> Notify;        // title, text
        public Func<Dictionary<string, object>> RdpInfo;

        long lastClipTs; string lastHash = "";
        Dictionary<string, object> pending, lastOut;
        readonly Dictionary<string, int> pace = new Dictionary<string, int>();     // per-transfer chunk interval (ms), grows when the receiver reports loss
        bool netHooked; long lastNetKick;
        readonly HashSet<string> seen = new HashSet<string>(); readonly Queue<string> seenQ = new Queue<string>();
        readonly Dictionary<string, Offer> outgoing = new Dictionary<string, Offer>(); readonly Queue<string> outQ = new Queue<string>();
        readonly Dictionary<string, Rx> rxs = new Dictionary<string, Rx>();
        long lastDevPub; int lastUp = -1;
        /// <summary>QA only: CLIPORT_TEST_DROP=N discards every Nth incoming file chunk once, to exercise re-transmission.</summary>
        static readonly int TestDrop = ParseEnv("CLIPORT_TEST_DROP"); readonly HashSet<string> testDropped = new HashSet<string>();
        static int ParseEnv(string n) { int v; return int.TryParse(Environment.GetEnvironmentVariable(n), out v) ? v : 0; }

        sealed class Rx
        {
            public Offer O; public int Idx; public string Path; public FileStream Fs; public int Total = -1, Next; public long Last;
            public Dictionary<int, byte[]> Buf = new Dictionary<int, byte[]>(); public long LastNak;
            public Action<int> Progress; public Action<string, string> Done;
        }

        public Engine(Config cfg, Action<Action> post)
        {
            Cfg = cfg; this.post = post;
            lastClipTs = cfg.L("lastClipTs");
            LoadLocal();
        }

        // ---------- lifecycle ----------
        public bool Paired { get { return Keys != null; } }
        public void Start()
        {
            Stop();
            Keys = KeyRing.FromCode(Cfg.Code);
            if (Keys == null) return;
            Hub = new RelayHub();
            string me = Cfg.DeviceId, b = Keys.Topic;
            var specs = new List<string>();
            foreach (string r in Cfg.S("relays").Split(new[] { ',', ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)) specs.Add(r);   // private relay(s) first
            specs.AddRange(RelayHub.DefaultRelays);
            foreach (string r in specs)
            {
                var l = MqttLink.Parse(r); if (l == null) continue;
                l.ClientIdBase = "sc" + me; l.Subs = new[] { b + "/ev", b + "/st/#", b + "/f/" + me };
                l.WillTopic = b + "/st/dev/" + me;
                l.WillPayload = delegate { var w = DevMsg(false); w["will"] = true; return Pack(w); };
                l.OnMessage = OnWire;
                l.OnUp = delegate { post(delegate { PublishDev(); ReannounceClip(); PublishPinsIfAny(); FireStatus(); }); };
                // one relay dropped while others still carry us: say so before that relay's last-will lands
                l.OnDown = delegate { post(delegate { FireStatus(); if (Hub != null && Hub.UpCount > 0) PublishDev(); }); };
                Hub.Links.Add(l);
            }
            Hub.Start();
            if (!netHooked)
            {
                netHooked = true;
                // Wi-Fi / cable / VPN changed: old sockets are dead even if Windows has not noticed yet - reconnect now
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += delegate
                {
                    post(delegate
                    {
                        if (Hub == null || App.Now() - lastNetKick < 3000) return;
                        lastNetKick = App.Now(); Log.Write("network changed - reconnecting relays"); Hub.KickAll();
                    });
                };
            }
            Log.Write("engine started, device " + me + ", topic " + b.Substring(0, 12) + "...");
        }
        public void Stop()
        {
            if (Hub != null)
            {
                try { Send("st/dev/" + Cfg.DeviceId, DevMsg(false), true); } catch { }
                Hub.Stop(); Hub = null;
            }
        }
        public void Unpair() { Stop(); Keys = null; Cfg.Set("secret", ""); Cfg.Save(); Devices.Clear(); Fire(DevicesChanged); FireStatus(); }

        /// <summary>Periodic maintenance (UI thread, every ~5s): heal relays, refresh presence, time out transfers.</summary>
        public void Tick()
        {
            if (Hub == null) return;
            Hub.Heal();
            long now = App.Now();
            if (now - lastDevPub > 60000) PublishDev();
            AutoBackupIfDue(now);
            foreach (var k in new List<string>(rxs.Keys))
            {
                var rx = rxs[k];
                if (now - rx.Last > 90000) FinishRx(k, L.T("תם הזמן - המכשיר המקורי לא הגיב", "Timed out - the source device did not respond"));
            }
            FireStatus();
        }
        /// <summary>Every second: relays are best-effort, so a stalled transfer asks again for exactly the chunks it is missing.</summary>
        public void FastTick()
        {
            if (Hub == null || rxs.Count == 0) return;
            long now = App.Now();
            foreach (var rx in new List<Rx>(rxs.Values))
            {
                if (rx.Total <= 0 || now - rx.LastNak < 2500) continue;
                bool stalled = now - rx.Last >= 2500;
                if (!stalled && rx.Buf.Count < 12) continue;            // still flowing and no significant gap
                int hi = rx.Total;                                      // stalled: everything not received is missing
                if (!stalled) { hi = 0; foreach (int k in rx.Buf.Keys) if (k > hi) hi = k; }   // flowing: only holes below what already arrived
                var need = new List<object>();
                for (int s = rx.Next; s < hi && need.Count < 48; s++) if (!rx.Buf.ContainsKey(s)) need.Add(s);
                if (need.Count == 0) continue;
                rx.LastNak = now;
                var m = new Dictionary<string, object>(); m["t"] = "fnak"; m["fid"] = rx.O.Fid; m["idx"] = rx.Idx; m["to"] = rx.O.From; m["seqs"] = need;
                Log.Write("transfer stalled at chunk " + rx.Next + "/" + rx.Total + " - re-requesting " + need.Count + " chunk(s)");
                Send("ev", m, false);
            }
        }
        void FireStatus() { int u = UpCount; if (u != lastUp) { lastUp = u; Fire(StatusChanged); } }
        public int UpCount { get { return Demo ? 3 : Hub == null ? 0 : Hub.UpCount; } }
        public int RelayCount { get { return Demo ? 3 : Hub == null ? 0 : Hub.Links.Count; } }
        static void Fire(Action a) { if (a != null) a(); }

        // ---------- wire ----------
        int Send(string sub, Dictionary<string, object> m, bool retain)
        {
            if (Hub == null || Keys == null) return 0;
            return Hub.Publish(Keys.Topic + "/" + sub, Pack(m), retain);
        }
        byte[] Pack(Dictionary<string, object> m)
        {
            if (!m.ContainsKey("id")) m["id"] = App.RandHex(8);
            m["from"] = Cfg.DeviceId;
            if (!m.ContainsKey("ts")) m["ts"] = App.Now();
            return Keys.Seal(Encoding.UTF8.GetBytes(Json.Ser(m)));
        }
        void OnWire(MqttLink link, string topic, byte[] payload)
        {
            var k = Keys; if (k == null || payload.Length == 0 || topic.Length <= k.Topic.Length + 1) return;
            byte[] plain = k.Open(payload); if (plain == null) return;       // not ours / tampered
            Dictionary<string, object> d;
            try { d = Json.Parse(Encoding.UTF8.GetString(plain)); } catch { return; }
            string sub = topic.Substring(k.Topic.Length + 1);
            post(delegate { try { Handle(sub, d); } catch (Exception e) { Log.Write("handle " + sub + ": " + e); } });
        }
        void Handle(string sub, Dictionary<string, object> d)
        {
            if (Keys == null) return;
            string from = Json.S(d, "from"), t = Json.S(d, "t");
            bool mine = from == Cfg.DeviceId;
            if (sub.StartsWith("st/dev/")) { OnDev(sub.Substring(7), d); return; }
            if (sub == "st/pins") { MergePins(d); return; }
            if (sub == "st/cfg") { MergeCfg(d); return; }
            if (mine) return;
            if (sub == "ev" || sub.StartsWith("f/"))
            {
                string id = Json.S(d, "id");
                if (!seen.Add(id)) return;
                seenQ.Enqueue(id); if (seenQ.Count > 1500) seen.Remove(seenQ.Dequeue());
            }
            switch (t)
            {
                case "clip": OnClip(d); break;
                case "offer": OnOffer(d); break;
                case "freq": if (Json.S(d, "to") == Cfg.DeviceId) ServeFile(d); break;
                case "fres": if (Json.S(d, "to") == Cfg.DeviceId) OnFres(d); break;
                case "fnak": if (Json.S(d, "to") == Cfg.DeviceId) ServeNak(d); break;
                case "fchunk": OnChunk(d); break;
            }
        }
        string NameOf(string id) { DeviceInfo di; return Devices.TryGetValue(id, out di) && di.Name != "" ? di.Name : L.T("מכשיר אחר", "another device"); }

        // ---------- clipboard text ----------
        static string Hash(string s)
        {
            using (var h = SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(s)), 0, 12);
        }
        bool Fresh(Dictionary<string, object> d)
        {
            long ts = Json.L(d, "ts");
            if (ts <= lastClipTs) return false;
            if (App.Now() - ts > 24L * 3600 * 1000) return false;      // stale retained state
            lastClipTs = ts; Cfg.Set("lastClipTs", ts); Cfg.Save();
            return true;
        }
        void OnClip(Dictionary<string, object> d)
        {
            if (Paused || !Fresh(d)) return;
            string text = Json.S(d, "text"); if (text == "") return;
            lastHash = Hash(text);
            AddHistory(text);
            Log.Write("clip in (" + text.Length + " chars) from " + NameOf(Json.S(d, "from")));
            if (TextIn != null) TextIn(text, NameOf(Json.S(d, "from")));
        }
        /// <summary>Called by the clipboard monitor when the local clipboard holds new text.</summary>
        public void LocalText(string text)
        {
            if (Paused || Keys == null || string.IsNullOrEmpty(text)) return;
            string h = Hash(text); if (h == lastHash) return;
            AddHistory(text);
            if (Encoding.UTF8.GetByteCount(text) > MaxInlineText)
            {
                string dir = App.PathOf("outgoing"); Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "Cliport-text-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"); File.WriteAllText(p, text, new UTF8Encoding(false));
                LocalFiles(new[] { p }, false, true); lastHash = h; return;
            }
            lastHash = h;
            var m = new Dictionary<string, object>(); m["t"] = "clip"; m["kind"] = "text"; m["text"] = text;
            Log.Write("clip out (" + text.Length + " chars)");
            PublishClip(m);
        }
        void PublishClip(Dictionary<string, object> m)
        {
            if (!m.ContainsKey("ts")) { lastClipTs = Math.Max(App.Now(), lastClipTs + 1); m["ts"] = lastClipTs; Cfg.Set("lastClipTs", lastClipTs); Cfg.Save(); }
            if (!m.ContainsKey("id")) m["id"] = App.RandHex(8);
            int n = Send("ev", m, false); Send("st/clip", m, true);
            pending = n == 0 ? m : null;           // re-sent automatically when a relay comes back
            lastOut = m;
        }
        /// <summary>
        /// A relay (re)connected. If the last clip never left, send it now. Otherwise re-announce our latest clip as
        /// retained state: a write into a socket that was already dead reports success, so this closes that gap.
        /// Receivers ignore it unless it is newer than what they have.
        /// </summary>
        /// <summary>Forget what was synced last, so the next LocalText is sent even if the content is unchanged ("send now").</summary>
        public void ForgetLast() { lastHash = ""; }
        void ReannounceClip()
        {
            if (pending != null) { PublishClip(pending); return; }
            if (lastOut != null && Json.L(lastOut, "ts") == lastClipTs) Send("st/clip", lastOut, true);
        }

        // ---------- history ----------
        void AddHistory(string text)
        {
            if (text.Length > 100000) text = text.Substring(0, 100000);
            History.Remove(text); History.Insert(0, text);
            while (History.Count > 60) History.RemoveAt(History.Count - 1);
            SaveHistory(); Fire(HistoryChanged);
        }
        public void RemoveHistory(string text) { History.Remove(text); SaveHistory(); Fire(HistoryChanged); }
        public void ClearHistory() { History.Clear(); SaveHistory(); Fire(HistoryChanged); }
        /// <summary>A large text clip that arrived as a file was placed on the clipboard: record it like any other received text.</summary>
        public void NoteRemoteText(string text) { lastHash = Hash(text); AddHistory(text); }
        /// <summary>--demo: sample content and devices, no network. Used to produce the user-guide screenshots.</summary>
        public Offer LoadDemo()
        {
            Demo = true; Keys = KeyRing.FromCode("CLIPORTDEMOCODEAAAAAAAAAAA"); History.Clear(); Pins.Clear(); Devices.Clear();
            string[] h = L.En
                ? new[] { "https://github.com/bennymat93/Cliport", "Meeting moved to Thursday 14:30, room 4B", "SELECT name, total FROM orders WHERE status = 'open';", "Flight LY315 TLV-LHR, seat 23A", "Thanks! I will send the signed contract today." }
                : new[] { "https://github.com/bennymat93/Cliport", "הפגישה עברה ליום חמישי ב-14:30, חדר 4B", "SELECT name, total FROM orders WHERE status = 'open';", "טיסה LY315 תל אביב-לונדון, מושב 23A", "תודה! אשלח את החוזה החתום עוד היום." };
            History.AddRange(h);
            string[] p = L.En ? new[] { "IBAN: IL62 0108 0000 0009 9999 999", "Office Wi-Fi: Guest-5G / welcome2026" } : new[] { "מספר חשבון: 12-345-678901", "Wi-Fi במשרד: Guest-5G / welcome2026" };
            long t = App.Now();
            foreach (string s in p) { var it = new PinItem(); it.Id = App.RandHex(6); it.Text = s; it.Ts = t--; Pins.Add(it); }
            AddDemoDevice("iPhone 15 Pro", "ios", true, false); AddDemoDevice("iPad Air", "ios", true, false);
            AddDemoDevice("OFFICE-PC", "win", true, true, "100.84.12.7", "192.168.1.20", "OFFICE-PC"); AddDemoDevice("LAPTOP-HOME", "win", false, true, "100.91.3.44", "LAPTOP-HOME");
            foreach (string f in Directory.GetFiles(BackupDir)) File.Delete(f);
            StoreLocal(BackupFile.Create(L.T("לפני החלפת מחשב", "Before replacing the PC"), L.T("כל ההגדרות והנעוצים, כולל Telegram", "All settings and pins, including Telegram"), "MY-PC", App.Now() - 86400000L * 9, BackupPayload(true), null));
            StoreLocal(BackupFile.Create(L.T("גיבוי שבועי", "Weekly backup"), L.T("מצב תקין אחרי הוספת האייפד", "Known-good state after adding the iPad"), "MY-PC", App.Now() - 86400000L * 2, BackupPayload(false), null));
            var o = new Offer(); o.Fid = "demo"; o.From = "demo0"; o.FromName = "iPhone 15 Pro"; o.Ts = t;
            var fe = new FileEntry(); fe.Name = L.T("דוח רבעוני.pdf", "Quarterly report.pdf"); fe.Size = 2516582; fe.Mime = "application/pdf"; o.Files.Add(fe);
            return o;
        }
        void AddDemoDevice(string name, string plat, bool online, bool rdp, params string[] addrs)
        {
            var d = new DeviceInfo(); d.Id = "demo" + Devices.Count; d.Name = name; d.Plat = plat; d.Ver = App.Version; d.Online = online; d.Ts = App.Now();
            d.RdpOn = rdp && online; d.RdpUser = "user"; d.Addrs.AddRange(addrs); Devices[d.Id] = d;
        }

        // ---------- pins (last-writer-wins element set with tombstones) ----------
        public bool IsPinned(string text) { foreach (var p in Pins) if (!p.Del && p.Text == text) return true; return false; }
        public void Pin(string text)
        {
            if (string.IsNullOrEmpty(text) || IsPinned(text)) return;
            if (text.Length > 16000) { if (Notify != null) Notify(L.T("נעיצה", "Pin"), L.T("הטקסט ארוך מדי לנעיצה (מעל 16,000 תווים).", "The text is too long to pin (over 16,000 characters).")); return; }
            int live = 0, total = 0; foreach (var p in Pins) if (!p.Del) { live++; total += p.Text.Length; }
            if (live >= 60 || total + text.Length > 60000) { if (Notify != null) Notify(L.T("נעיצה", "Pin"), L.T("הגעת למגבלת הנעוצים. בטל נעיצה של פריט אחר.", "Pin limit reached. Unpin another item first.")); return; }
            var it = new PinItem(); it.Id = App.RandHex(6); it.Text = text; it.Ts = App.Now(); Pins.Add(it);
            SavePins(); PublishPins(); Fire(PinsChanged);
        }
        public void Unpin(string text)
        {
            bool ch = false;
            foreach (var p in Pins) if (!p.Del && p.Text == text) { p.Del = true; p.Text = ""; p.Ts = App.Now(); ch = true; }
            if (ch) { SavePins(); PublishPins(); Fire(PinsChanged); }
        }
        public List<string> LivePins()
        {
            var l = new List<PinItem>(); foreach (var p in Pins) if (!p.Del) l.Add(p);
            l.Sort(delegate (PinItem a, PinItem b) { return b.Ts.CompareTo(a.Ts); });
            var r = new List<string>(); foreach (var p in l) r.Add(p.Text); return r;
        }
        Dictionary<string, object> PinsMsg()
        {
            var items = new List<object>();
            foreach (var p in Pins)
            {
                var o = new Dictionary<string, object>(); o["id"] = p.Id; o["text"] = p.Text; o["ts"] = p.Ts; o["del"] = p.Del; items.Add(o);
            }
            var m = new Dictionary<string, object>(); m["t"] = "pins"; m["items"] = items; return m;
        }
        void PublishPins() { Send("st/pins", PinsMsg(), true); }
        void PublishPinsIfAny() { if (Pins.Count > 0) PublishPins(); if (Cfg.L("cfgTs") > 0) PublishCfg(); }
        void MergePins(Dictionary<string, object> d)
        {
            bool changed = false, localNewer = false;
            var inc = new Dictionary<string, PinItem>();
            foreach (var o in Json.List(d, "items"))
            {
                var p = new PinItem(); p.Id = Json.S(o, "id"); p.Text = Json.S(o, "text"); p.Ts = Json.L(o, "ts"); p.Del = Json.B(o, "del");
                if (p.Id != "") inc[p.Id] = p;
            }
            foreach (var p in inc.Values)
            {
                PinItem loc = Pins.Find(delegate (PinItem x) { return x.Id == p.Id; });
                if (loc == null) { Pins.Add(p); changed = true; }
                else if (p.Ts > loc.Ts) { loc.Text = p.Text; loc.Del = p.Del; loc.Ts = p.Ts; changed = true; }
            }
            long cutoff = App.Now() - 30L * 24 * 3600 * 1000;
            Pins.RemoveAll(delegate (PinItem x) { return x.Del && x.Ts < cutoff; });
            foreach (var p in Pins) { PinItem i; if (!inc.TryGetValue(p.Id, out i) || p.Ts > i.Ts) { localNewer = true; break; } }
            if (changed) { SavePins(); Fire(PinsChanged); }
            if (localNewer) PublishPins();
        }

        // ---------- shared config (Telegram storage) ----------
        public string TgToken { get { return Cfg.S("tgToken"); } }
        public string TgChat { get { return Cfg.S("tgChat"); } }
        public bool TgReady { get { return TgToken != "" && TgChat != ""; } }
        public void SetTelegram(string token, string chat)
        {
            Cfg.Set("tgToken", token); Cfg.Set("tgChat", chat); Cfg.Set("cfgTs", App.Now()); Cfg.Save(); PublishCfg();
        }
        /// <summary>Private relay(s): "host:port" or "user:pass@host:port", comma separated. Shared with all devices.</summary>
        public void SetRelays(string relays)
        {
            Cfg.Set("relays", relays.Trim()); Cfg.Set("cfgTs", App.Now()); Cfg.Save();
            PublishCfg();                       // announce over the current relays, then reconnect with the new set
            System.Threading.Thread.Sleep(300);
            Start();
        }
        void PublishCfg()
        {
            var m = new Dictionary<string, object>(); m["t"] = "cfg"; m["tgToken"] = TgToken; m["tgChat"] = TgChat; m["relays"] = Cfg.S("relays"); m["cts"] = Cfg.L("cfgTs");
            Send("st/cfg", m, true);
        }
        void MergeCfg(Dictionary<string, object> d)
        {
            long cts = Json.L(d, "cts"), mine = Cfg.L("cfgTs");
            if (cts > mine)
            {
                bool relaysChanged = Json.S(d, "relays") != Cfg.S("relays");
                Cfg.Set("tgToken", Json.S(d, "tgToken")); Cfg.Set("tgChat", Json.S(d, "tgChat")); Cfg.Set("relays", Json.S(d, "relays")); Cfg.Set("cfgTs", cts); Cfg.Save();
                Fire(StatusChanged);
                if (relaysChanged) { Log.Write("relay list updated by another device"); Start(); }
            }
            else if (cts < mine) PublishCfg();
        }

        // ---------- devices ----------
        Dictionary<string, object> DevMsg(bool online)
        {
            var m = new Dictionary<string, object>();
            m["t"] = "dev"; m["name"] = Cfg.DeviceName; m["plat"] = "win"; m["ver"] = App.Version; m["online"] = online;
            if (online && RdpInfo != null) { try { m["rdp"] = RdpInfo(); } catch { } }
            return m;
        }
        public void PublishDev() { lastDevPub = App.Now(); Send("st/dev/" + Cfg.DeviceId, DevMsg(true), true); }
        readonly HashSet<System.Threading.Timer> willTimers = new HashSet<System.Threading.Timer>();
        void OnDev(string id, Dictionary<string, object> d)
        {
            if (Json.S(d, "from") != id) return;
            if (id == Cfg.DeviceId) { if (!Json.B(d, "online") && Hub != null && Hub.UpCount > 0) PublishDev(); return; }
            DeviceInfo di; if (!Devices.TryGetValue(id, out di)) { di = new DeviceInfo(); di.Id = id; Devices[id] = di; }
            if (Json.B(d, "will"))
            {
                // Last-will from a relay: the device dropped off that relay. Its timestamp is the connect time, so it
                // bypasses the ordering check; a device that is still alive elsewhere re-announces itself right away.
                if (di.Name == null || di.Name == "") { di.Name = Json.S(d, "name"); di.Plat = Json.S(d, "plat"); }
                // So wait a moment before believing it: one dropped relay out of several is not "offline".
                long seen = di.Ts; string wid = id; System.Threading.Timer t = null;
                t = new System.Threading.Timer(delegate
                {
                    post(delegate
                    {
                        lock (willTimers) willTimers.Remove(t);
                        t.Dispose();
                        DeviceInfo x; if (!Devices.TryGetValue(wid, out x) || x.Ts != seen || !x.Online) return;
                        x.Online = false; Log.Write("device offline (connection lost): " + x.Name); Fire(DevicesChanged);
                    });
                }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                lock (willTimers) willTimers.Add(t);
                t.Change(8000, System.Threading.Timeout.Infinite);
                return;
            }
            long ts = Json.L(d, "ts"); if (ts < di.Ts) return;
            bool was = di.Online;
            di.Ts = ts; di.Online = Json.B(d, "online");
            if (was != di.Online) Log.Write("device " + (di.Online ? "online: " : "offline: ") + Json.S(d, "name")); di.Name = Json.S(d, "name"); di.Plat = Json.S(d, "plat"); di.Ver = Json.S(d, "ver");
            var r = Json.D(d, "rdp");
            if (r != null)
            {
                di.RdpOn = Json.B(r, "on"); di.RdpHost = Json.S(r, "host"); di.RdpUser = Json.S(r, "user");
                di.RdpPort = (int)Json.L(r, "port"); if (di.RdpPort == 0) di.RdpPort = 3389;
                di.Addrs = Json.Strs(r, "addrs");
            }
            Fire(DevicesChanged);
        }
        public void ForgetDevice(string id)
        {
            Devices.Remove(id);
            if (Hub != null) Hub.Publish(Keys.Topic + "/st/dev/" + id, new byte[0], true);   // clears the retained record
            Fire(DevicesChanged);
        }

        // ---------- files: announce ----------
        public void LocalFiles(string[] paths, bool image, bool text = false)
        {
            if (Paused || Keys == null) return;
            var o = new Offer(); o.Fid = App.RandHex(8); o.From = Cfg.DeviceId; o.Image = image; o.Ts = App.Now();
            var sig = new StringBuilder("F:");
            foreach (string p in paths)
            {
                try
                {
                    var fi = new FileInfo(p); if (!fi.Exists) continue;
                    var fe = new FileEntry(); fe.Name = fi.Name; fe.Size = fi.Length; fe.Path = p; fe.Mime = Mime.Of(fi.Name);
                    o.Files.Add(fe); sig.Append(fe.Name).Append('|').Append(fe.Size).Append(';');
                }
                catch { }
            }
            if (o.Files.Count == 0) return;
            string h = Hash(sig.ToString()); if (h == lastHash) return; lastHash = h;
            outgoing[o.Fid] = o; outQ.Enqueue(o.Fid); if (outQ.Count > 8) outgoing.Remove(outQ.Dequeue());
            var files = new List<object>();
            foreach (var f in o.Files) { var x = new Dictionary<string, object>(); x["name"] = f.Name; x["size"] = f.Size; x["mime"] = f.Mime; files.Add(x); }
            var m = new Dictionary<string, object>(); m["t"] = "offer"; m["fid"] = o.Fid; m["image"] = image; m["text"] = text; m["files"] = files;
            Log.Write("offer out: " + o.Files.Count + " file(s), " + App.SizeText(o.TotalSize));
            PublishClip(m);
        }
        void OnOffer(Dictionary<string, object> d)
        {
            if (Paused || !Fresh(d)) return;
            var o = new Offer(); o.Fid = Json.S(d, "fid"); o.From = Json.S(d, "from"); o.FromName = NameOf(o.From); o.Image = Json.B(d, "image"); o.Text = Json.B(d, "text"); o.Ts = Json.L(d, "ts");
            foreach (var x in Json.List(d, "files"))
            {
                var fe = new FileEntry(); fe.Name = SafeName(Json.S(x, "name")); fe.Size = Json.L(x, "size"); fe.Mime = Json.S(x, "mime"); o.Files.Add(fe);
            }
            if (o.Fid == "" || o.Files.Count == 0) return;
            // same signature as LocalFiles: a received copy landing on a clipboard is never re-announced (no ping-pong)
            var sig = new StringBuilder("F:"); foreach (var f in o.Files) sig.Append(f.Name).Append('|').Append(f.Size).Append(';');
            lastHash = Hash(sig.ToString());
            Log.Write("offer in: " + o.Files[0].Name + " (" + App.SizeText(o.TotalSize) + ") from " + o.FromName);
            if (OfferIn != null) OfferIn(o);
        }
        static string SafeName(string n)
        {
            n = Path.GetFileName(n.Replace('/', '\\'));
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n == "" || n == "." || n == ".." ? "file" : n;
        }

        // ---------- files: receive ----------
        public void RequestFile(Offer o, int idx, Action<int> progress, Action<string, string> done)
        {
            string key = o.Fid + ":" + idx;
            if (rxs.ContainsKey(key)) { done(null, L.T("ההעברה כבר מתבצעת", "The transfer is already in progress")); return; }
            var rx = new Rx(); rx.O = o; rx.Idx = idx; rx.Progress = progress; rx.Done = done; rx.Last = App.Now();
            string dir = Path.Combine(App.PathOf("incoming"), o.Fid);
            string nm = o.Files[idx].Name;
            if (o.Files.FindIndex(delegate (FileEntry f) { return string.Equals(f.Name, nm, StringComparison.OrdinalIgnoreCase); }) != idx) dir = Path.Combine(dir, idx.ToString());   // same name twice in one copy
            Directory.CreateDirectory(dir);
            rx.Path = Path.Combine(dir, o.Files[idx].Name);
            rxs[key] = rx;
            var m = new Dictionary<string, object>(); m["t"] = "freq"; m["fid"] = o.Fid; m["idx"] = idx; m["to"] = o.From;
            if (Send("ev", m, false) == 0) FinishRx(key, L.T("אין חיבור לשרתי הממסר", "No connection to the relay servers"));
        }
        void FinishRx(string key, string err)
        {
            Rx rx; if (!rxs.TryGetValue(key, out rx)) return;
            rxs.Remove(key);
            try { if (rx.Fs != null) rx.Fs.Close(); } catch { }
            if (err != null) { Log.Write("file rx failed: " + err); try { File.Delete(rx.Path); } catch { } rx.Done(null, err); }
            else { Log.Write("file rx done: " + rx.Path); rx.Done(rx.Path, null); }
        }
        void OnFres(Dictionary<string, object> d)
        {
            string key = Json.S(d, "fid") + ":" + Json.L(d, "idx");
            Rx rx; if (!rxs.TryGetValue(key, out rx)) return;
            rx.Last = App.Now();
            string mode = Json.S(d, "mode");
            if (mode == "err") { FinishRx(key, Json.S(d, "err")); return; }
            if (mode == "mqtt")
            {
                rx.Total = (int)Json.L(d, "total");
                if (rx.Fs == null) rx.Fs = new FileStream(rx.Path, FileMode.Create, FileAccess.Write);
                if (rx.Total == 0) FinishRx(key, null);
                return;
            }
            if (mode == "tg")
            {
                var parts = Json.Strs(d, "parts"); string token = TgToken; var keys = Keys;
                rx.Last = App.Now() + 3600000;                    // worker owns the timeout now
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = null;
                    try
                    {
                        using (var fs = new FileStream(rx.Path, FileMode.Create, FileAccess.Write))
                            for (int i = 0; i < parts.Count; i++)
                            {
                                byte[] plain = keys.Open(Telegram.Download(token, parts[i]));
                                if (plain == null) throw new IOException(L.T("פענוח נכשל", "decryption failed"));
                                fs.Write(plain, 0, plain.Length);
                                int pc = (i + 1) * 100 / parts.Count; post(delegate { if (rx.Progress != null) rx.Progress(pc); });
                            }
                    }
                    catch (Exception e) { err = L.T("הורדה מ-Telegram נכשלה: ", "Download from Telegram failed: ") + e.Message; }
                    post(delegate { FinishRx(key, err); });
                });
            }
        }
        void OnChunk(Dictionary<string, object> d)
        {
            string key = Json.S(d, "fid") + ":" + Json.L(d, "idx");
            Rx rx; if (!rxs.TryGetValue(key, out rx)) return;
            int seq = (int)Json.L(d, "seq"); if (seq < rx.Next) return;
            if (TestDrop > 0 && seq % TestDrop == 1 && testDropped.Add(key + "#" + seq)) return;     // QA hook: simulate a lossy relay
            rx.Total = (int)Json.L(d, "total"); rx.Last = App.Now();
            rx.Buf[seq] = Convert.FromBase64String(Json.S(d, "data"));
            if (rx.Fs == null) rx.Fs = new FileStream(rx.Path, FileMode.Create, FileAccess.Write);
            byte[] b;
            while (rx.Buf.TryGetValue(rx.Next, out b)) { rx.Fs.Write(b, 0, b.Length); rx.Buf.Remove(rx.Next); rx.Next++; }
            if (rx.Progress != null && rx.Total > 0) rx.Progress(rx.Next * 100 / rx.Total);
            if (rx.Next >= rx.Total) FinishRx(key, null);
        }

        // ---------- files: serve (origin device) ----------
        void ServeFile(Dictionary<string, object> d)
        {
            string fid = Json.S(d, "fid"), to = Json.S(d, "from"); int idx = (int)Json.L(d, "idx");
            Offer o; outgoing.TryGetValue(fid, out o);
            var keys = Keys; var hub = Hub; string token = TgToken, chat = TgChat; bool tg = TgReady;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var res = new Dictionary<string, object>(); res["t"] = "fres"; res["fid"] = fid; res["idx"] = idx; res["to"] = to;
                try
                {
                    if (o == null || idx < 0 || idx >= o.Files.Count || !File.Exists(o.Files[idx].Path)) throw new IOException(L.T("הקובץ כבר לא זמין במכשיר המקורי", "The file is no longer available on the source device"));
                    string path = o.Files[idx].Path; long size = new FileInfo(path).Length;
                    res["size"] = size;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (tg && size > TgFrom)
                        {
                            var parts = new List<object>(); var buf = new byte[TgPart]; int n, k = 0;
                            while ((n = ReadFull(fs, buf)) > 0)
                            {
                                var piece = new byte[n]; Array.Copy(buf, piece, n);
                                parts.Add(Telegram.Upload(token, chat, keys.Seal(piece, false), "sc-" + fid + "-" + (k++) + ".bin"));
                            }
                            res["mode"] = "tg"; res["parts"] = parts;
                            hub.Publish(keys.Topic + "/ev", Pack(res), false);
                            Log.Write("served via telegram: " + path);
                            return;
                        }
                        if (size > MaxRelayFile) throw new IOException(L.T("הקובץ גדול מ-25MB. הגדר אחסון Telegram בהגדרות כדי להעביר קבצים גדולים.", "The file is larger than 25 MB. Set up Telegram storage in Settings to transfer large files."));
                        int total = (int)((size + Chunk - 1) / Chunk);
                        res["mode"] = "mqtt"; res["total"] = total;
                        hub.Publish(keys.Topic + "/ev", Pack(res), false);
                        var cb = new byte[Chunk]; string pk = fid + ":" + idx + ":" + to;
                        lock (pace) pace[pk] = 30;
                        for (int seq = 0; seq < total; seq++)
                        {
                            SendChunk(hub, keys, fs, cb, fid, idx, seq, total, to);
                            int wait; lock (pace) wait = pace[pk];
                            Thread.Sleep(wait);
                        }
                        lock (pace) pace.Remove(pk);
                        Log.Write("served via relay: " + path + " (" + total + " chunks)");
                    }
                }
                catch (Exception e)
                {
                    Log.Write("serve failed: " + e.Message);
                    res["mode"] = "err"; res["err"] = e.Message; res.Remove("id");
                    try { hub.Publish(keys.Topic + "/ev", Pack(res), false); } catch { }
                }
            });
        }
        void SendChunk(RelayHub hub, KeyRing keys, FileStream fs, byte[] cb, string fid, int idx, int seq, int total, string to)
        {
            int n = ReadFull(fs, cb);
            var m = new Dictionary<string, object>(); m["t"] = "fchunk"; m["fid"] = fid; m["idx"] = idx; m["seq"] = seq; m["total"] = total;
            m["data"] = Convert.ToBase64String(cb, 0, n);
            byte[] pkt = Pack(m); int tries = 0;
            while (!hub.PublishOne(keys.Topic + "/f/" + to, pkt)) { if (++tries > 40) throw new IOException(L.T("החיבור אבד במהלך ההעברה", "The connection was lost during the transfer")); Thread.Sleep(500); }
        }
        /// <summary>The receiver is missing specific chunks: send just those again.</summary>
        void ServeNak(Dictionary<string, object> d)
        {
            string fid = Json.S(d, "fid"), to = Json.S(d, "from"); int idx = (int)Json.L(d, "idx");
            var seqs = new List<int>(); foreach (string s in Json.Strs(d, "seqs")) { int v; if (int.TryParse(s, out v)) seqs.Add(v); }
            Offer o; if (!outgoing.TryGetValue(fid, out o) || idx < 0 || idx >= o.Files.Count || seqs.Count == 0 || seqs.Count > 64) return;
            string path = o.Files[idx].Path; var keys = Keys; var hub = Hub;
            // loss reported while we are still sending: the link is weaker than our pace - slow down
            lock (pace) { string pk = fid + ":" + idx + ":" + to; int cur; if (pace.TryGetValue(pk, out cur)) pace[pk] = Math.Min(cur * 2, 400); }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        int total = (int)((fs.Length + Chunk - 1) / Chunk); var cb = new byte[Chunk];
                        foreach (int seq in seqs)
                        {
                            if (seq < 0 || seq >= total) continue;
                            fs.Seek((long)seq * Chunk, SeekOrigin.Begin);
                            SendChunk(hub, keys, fs, cb, fid, idx, seq, total, to);
                            Thread.Sleep(30);
                        }
                    }
                    Log.Write("re-sent " + seqs.Count + " chunk(s) of " + Path.GetFileName(path));
                }
                catch (Exception e) { Log.Write("re-send failed: " + e.Message); }
            });
        }
        static int ReadFull(Stream s, byte[] b)
        {
            int got = 0; while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) break; got += n; }
            return got;
        }

        // ---------- backup and restore ----------
        /// <summary>Everything needed to bring a fresh installation back to full function: pairing, shared settings, pins, devices, preferences.</summary>
        public Dictionary<string, object> BackupPayload(bool withHistory)
        {
            var p = new Dictionary<string, object>();
            p["code"] = Keys != null ? Keys.Code : Cfg.Code;
            p["tgToken"] = TgToken; p["tgChat"] = TgChat; p["relays"] = Cfg.S("relays");
            p["pins"] = PinsMsg()["items"];
            var devs = new List<object>();
            foreach (var d in Devices.Values)
            {
                var o = new Dictionary<string, object>(); o["id"] = d.Id; o["name"] = d.Name; o["plat"] = d.Plat; o["ver"] = d.Ver; o["ts"] = d.Ts;
                o["rdpOn"] = d.RdpOn; o["rdpUser"] = d.RdpUser ?? ""; o["rdpPort"] = d.RdpPort; o["addrs"] = d.Addrs; devs.Add(o);
            }
            p["devices"] = devs;
            var prefs = new Dictionary<string, object>(); prefs["lang"] = Cfg.S("lang"); prefs["theme"] = Cfg.S("theme"); prefs["autoAccept"] = Cfg.B("autoAccept", false); prefs["autostart"] = Autostart.Enabled;
            p["prefs"] = prefs;
            if (withHistory) p["history"] = new List<string>(History);
            return p;
        }
        /// <summary>Applies a backup payload and reconnects. Nothing else is needed afterwards.</summary>
        public void Restore(Dictionary<string, object> p)
        {
            string code = Json.S(p, "code"); if (KeyRing.FromCode(code) == null) throw new InvalidDataException("bad backup");
            if (!Demo) Stop();
            Cfg.Code = B32.Encode(B32.Decode(code));
            Cfg.Set("tgToken", Json.S(p, "tgToken")); Cfg.Set("tgChat", Json.S(p, "tgChat")); Cfg.Set("relays", Json.S(p, "relays"));
            Cfg.Set("cfgTs", App.Now());                       // the restored shared settings win and spread to the other devices
            var prefs = Json.D(p, "prefs");
            if (prefs != null)
            {
                if (Json.S(prefs, "lang") != "") Cfg.Set("lang", Json.S(prefs, "lang"));
                if (Json.S(prefs, "theme") != "") Cfg.Set("theme", Json.S(prefs, "theme"));
                if (prefs.ContainsKey("autoAccept")) Cfg.Set("autoAccept", Json.B(prefs, "autoAccept"));
            }
            // pins: live items get a fresh timestamp so they come back on every device, in their original order
            Pins.Clear(); long now = App.Now(); int n = 0;
            var items = Json.List(p, "pins"); items.Sort(delegate (Dictionary<string, object> a, Dictionary<string, object> b) { return Json.L(b, "ts").CompareTo(Json.L(a, "ts")); });
            foreach (var o in items)
            {
                var it = new PinItem(); it.Id = Json.S(o, "id"); it.Text = Json.S(o, "text"); it.Del = Json.B(o, "del"); it.Ts = it.Del ? Json.L(o, "ts") : now - n++;
                if (it.Id != "") Pins.Add(it);
            }
            if (p.ContainsKey("history")) { History.Clear(); History.AddRange(Json.Strs(p, "history")); SaveHistory(); }
            Devices.Clear();
            foreach (var o in Json.List(p, "devices"))
            {
                string id = Json.S(o, "id"); if (id == "" || id == Cfg.DeviceId) continue;
                var d = new DeviceInfo(); d.Id = id; d.Name = Json.S(o, "name"); d.Plat = Json.S(o, "plat"); d.Ver = Json.S(o, "ver"); d.Ts = Json.L(o, "ts"); d.Online = false;
                d.RdpOn = Json.B(o, "rdpOn"); d.RdpUser = Json.S(o, "rdpUser"); d.RdpPort = (int)Json.L(o, "rdpPort"); if (d.RdpPort == 0) d.RdpPort = 3389; d.Addrs = Json.Strs(o, "addrs");
                Devices[id] = d;
            }
            Cfg.Save(); SavePins();
            Log.Write("backup restored: " + Pins.Count + " pin record(s), " + Devices.Count + " known device(s)");
            if (!Demo) Start(); else Keys = KeyRing.FromCode(Cfg.Code);
            Fire(PinsChanged); Fire(HistoryChanged); Fire(DevicesChanged); lastUp = -1; FireStatus();
        }
        static string BackupDir { get { string d = App.PathOf("backups"); Directory.CreateDirectory(d); return d; } }
        /// <summary>Backups kept inside the tool (protected with Windows DPAPI). Newest first.</summary>
        public List<BackupInfo> LocalBackups()
        {
            var list = new List<BackupInfo>();
            foreach (string f in Directory.GetFiles(BackupDir, "*" + BackupFile.Ext))
            {
                try
                {
                    string text = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(f), null, DataProtectionScope.CurrentUser));
                    var h = BackupFile.Header(text); if (h == null) continue;
                    var b = new BackupInfo(); b.Path = f; b.Text = text; b.Name = Json.S(h, "name"); b.Desc = Json.S(h, "description"); b.Device = Json.S(h, "device"); b.Created = Json.L(h, "created");
                    var p = BackupFile.Payload(h, null);
                    if (p != null) { b.HasHistory = p.ContainsKey("history"); b.DeviceCount = Json.List(p, "devices").Count; foreach (var o in Json.List(p, "pins")) if (!Json.B(o, "del")) b.PinCount++; }
                    list.Add(b);
                }
                catch (Exception e) { Log.Write("backup unreadable " + f + ": " + e.Message); }
            }
            list.Sort(delegate (BackupInfo a, BackupInfo b) { return b.Created.CompareTo(a.Created); });
            return list;
        }
        public void StoreLocal(string backupFileText)
        {
            string f = System.IO.Path.Combine(BackupDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + App.RandHex(3) + BackupFile.Ext);
            File.WriteAllBytes(f, ProtectedData.Protect(Encoding.UTF8.GetBytes(backupFileText), null, DataProtectionScope.CurrentUser));
        }
        public void SaveLocal(string name, string desc, bool withHistory)
        {
            StoreLocal(BackupFile.Create(name, desc, Cfg.DeviceName, App.Now(), BackupPayload(withHistory), null));
            Log.Write("local backup created: " + name);
        }
        public void DeleteLocal(BackupInfo b) { try { File.Delete(b.Path); } catch { } }
        /// <summary>One automatic backup a day, the newest five are kept. Costs nothing and makes any mishap recoverable.</summary>
        void AutoBackupIfDue(long now)
        {
            if (Demo || Keys == null || now - Cfg.L("autoBackupTs") < 24L * 3600 * 1000) return;
            Cfg.Set("autoBackupTs", now); Cfg.Save();
            try
            {
                SaveLocal(AutoName, L.T("נוצר אוטומטית פעם ביום", "Created automatically once a day"), false);
                int kept = 0;
                foreach (var b in LocalBackups()) if (b.Name == "אוטומטי - יומי" || b.Name == "Automatic - daily") { if (++kept > 5) DeleteLocal(b); }
            }
            catch (Exception e) { Log.Write("auto backup: " + e.Message); }
        }
        static string AutoName { get { return L.T("אוטומטי - יומי", "Automatic - daily"); } }

        // ---------- local persistence (DPAPI-protected) ----------
        void LoadLocal()
        {
            try
            {
                var d = Secure.Load("pins.bin");
                foreach (var o in Json.List(d, "items"))
                {
                    var p = new PinItem(); p.Id = Json.S(o, "id"); p.Text = Json.S(o, "text"); p.Ts = Json.L(o, "ts"); p.Del = Json.B(o, "del");
                    if (p.Id != "") Pins.Add(p);
                }
                History.AddRange(Json.Strs(Secure.Load("history.bin"), "items"));
            }
            catch (Exception e) { Log.Write("load local: " + e.Message); }
        }
        void SavePins() { Secure.Save("pins.bin", PinsMsg()); }
        void SaveHistory() { var d = new Dictionary<string, object>(); d["items"] = History; Secure.Save("history.bin", d); }
    }

    sealed class BackupInfo
    {
        public string Path, Name, Desc, Device, Text; public long Created; public bool HasHistory; public int PinCount, DeviceCount;
    }

    static class Secure
    {
        public static Dictionary<string, object> Load(string name)
        {
            foreach (string p in new[] { App.PathOf(name), App.PathOf(name + ".bak") })
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(p), null, DataProtectionScope.CurrentUser);
                    return Json.Parse(Encoding.UTF8.GetString(raw));
                }
                catch (Exception e) { Log.Write("secure load " + p + ": " + e.Message); }
            }
            return new Dictionary<string, object>();
        }
        public static void Save(string name, Dictionary<string, object> d)
        {
            try
            {
                string p = App.PathOf(name), tmp = p + ".tmp";
                File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.Ser(d)), null, DataProtectionScope.CurrentUser));
                if (File.Exists(p)) File.Replace(tmp, p, p + ".bak"); else File.Move(tmp, p);
            }
            catch (Exception e) { Log.Write("secure save " + name + ": " + e.Message); }
        }
    }

    static class Mime
    {
        public static string Of(string name)
        {
            switch (Path.GetExtension(name).ToLowerInvariant())
            {
                case ".pdf": return "application/pdf";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".heic": return "image/heic";
                case ".txt": return "text/plain";
                case ".doc": case ".docx": return "application/msword";
                case ".xls": case ".xlsx": return "application/vnd.ms-excel";
                case ".ppt": case ".pptx": return "application/vnd.ms-powerpoint";
                case ".zip": return "application/zip";
                case ".mp4": return "video/mp4";
                case ".mov": return "video/quicktime";
                case ".mp3": return "audio/mpeg";
                default: return "application/octet-stream";
            }
        }
        public static string Label(string name)
        {
            string e = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
            return e == "" ? L.T("קובץ", "File") : L.T("קובץ " + e, e + " file");
        }
    }
}
