// Cliport v1.0 - Windows integration: clipboard window, Telegram storage, RDP, crash recovery.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Cliport
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr h);
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern bool EmptyClipboard();
        [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(uint fmt, IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetOpenClipboardWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        /// <summary>Name of the process that currently has the clipboard open (i.e. the one pasting).</summary>
        public static string ClipboardReader()
        {
            try
            {
                IntPtr w = GetOpenClipboardWindow(); if (w == IntPtr.Zero) return "";
                uint pid; GetWindowThreadProcessId(w, out pid); if (pid == 0) return "";
                using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) return p.ProcessName.ToLowerInvariant();
            }
            catch { return ""; }
        }
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterApplicationRestart(string cmd, int flags);
        public const uint CF_HDROP = 15;

        public static IntPtr GlobalFrom(byte[] data)
        {
            IntPtr h = GlobalAlloc(0x0042, (UIntPtr)(uint)data.Length);   // GMEM_MOVEABLE | ZEROINIT
            IntPtr p = GlobalLock(h); Marshal.Copy(data, 0, p, data.Length); GlobalUnlock(h);
            return h;
        }
        public static IntPtr MakeHDrop(string[] paths)
        {
            var sb = new StringBuilder(); foreach (string p in paths) sb.Append(p).Append('\0'); sb.Append('\0');
            byte[] names = Encoding.Unicode.GetBytes(sb.ToString());
            var b = new byte[20 + names.Length];
            b[0] = 20;          // DROPFILES.pFiles
            b[16] = 1;          // fWide
            Array.Copy(names, 0, b, 20, names.Length);
            return GlobalFrom(b);
        }
    }

    /// <summary>
    /// Hidden window: clipboard change listener, clipboard owner for delayed rendering of remote files
    /// (the file is requested only when the user actually pastes), and global hotkeys.
    /// </summary>
    sealed class ClipWindow : NativeWindow
    {
        public Action Changed, LostOwnership; public Func<string[]> RenderFiles; public Action<int> HotKey;
        uint ignoreSeq; public bool OwnsDelayed;

        public ClipWindow() { CreateHandle(new CreateParams()); Native.AddClipboardFormatListener(Handle); }
        public void Rearm() { Native.RemoveClipboardFormatListener(Handle); Native.AddClipboardFormatListener(Handle); }
        void Mark() { ignoreSeq = Native.GetClipboardSequenceNumber(); }

        protected override void WndProc(ref Message m)
        {
            try
            {
                switch (m.Msg)
                {
                    case 0x031D:   // WM_CLIPBOARDUPDATE
                        if (Native.GetClipboardSequenceNumber() != ignoreSeq && Changed != null) Changed();
                        break;
                    case 0x0305:   // WM_RENDERFORMAT - somebody is pasting our delayed file list
                        if ((uint)m.WParam.ToInt64() == Native.CF_HDROP && RenderFiles != null)
                        {
                            string[] paths = RenderFiles();
                            if (paths != null && paths.Length > 0) Native.SetClipboardData(Native.CF_HDROP, Native.MakeHDrop(paths));
                        }
                        break;
                    case 0x0307:   // WM_DESTROYCLIPBOARD
                        OwnsDelayed = false; if (LostOwnership != null) LostOwnership();
                        break;
                    case 0x0312:   // WM_HOTKEY
                        if (HotKey != null) HotKey(m.WParam.ToInt32());
                        break;
                }
            }
            catch (Exception e) { Log.Write("clip window: " + e); }
            base.WndProc(ref m);
        }

        static bool Retry(Action a)
        {
            for (int i = 0; i < 12; i++)
            {
                try { a(); return true; }
                catch (ExternalException) { Thread.Sleep(40); }
                catch (ThreadStateException) { return false; }
            }
            return false;
        }
        public bool SetText(string text) { bool ok = Retry(delegate { Clipboard.SetText(text, TextDataFormat.UnicodeText); }); Mark(); return ok; }
        public bool SetImage(Image img) { bool ok = Retry(delegate { Clipboard.SetImage(img); }); Mark(); return ok; }
        public bool SetFiles(string[] paths)
        {
            var c = new System.Collections.Specialized.StringCollection(); c.AddRange(paths);
            bool ok = Retry(delegate { Clipboard.SetFileDropList(c); }); Mark(); return ok;
        }
        /// <summary>Text and a file at once: pasting into an editor gives the text, pasting into a folder gives the file.</summary>
        public bool SetTextAndFile(string text, string path)
        {
            var d = new DataObject(); d.SetText(text, TextDataFormat.UnicodeText);
            var c = new System.Collections.Specialized.StringCollection(); c.Add(path); d.SetFileDropList(c);
            bool ok = Retry(delegate { Clipboard.SetDataObject(d, true, 5, 40); }); Mark(); return ok;
        }
        /// <summary>Puts a promise of files on the clipboard. Data is produced in WM_RENDERFORMAT, i.e. at paste time.</summary>
        public bool OfferDelayedFiles()
        {
            for (int i = 0; i < 12; i++)
            {
                if (Native.OpenClipboard(Handle))
                {
                    try
                    {
                        Native.EmptyClipboard();
                        Native.SetClipboardData(Native.CF_HDROP, IntPtr.Zero);
                        Native.SetClipboardData(Native.RegisterClipboardFormat("Preferred DropEffect"), Native.GlobalFrom(new byte[] { 1, 0, 0, 0 }));
                        // keep clipboard-history tools from triggering the paste prompt
                        Native.SetClipboardData(Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), Native.GlobalFrom(new byte[4]));
                        Native.SetClipboardData(Native.RegisterClipboardFormat("CanIncludeInClipboardHistory"), Native.GlobalFrom(new byte[4]));
                        Native.SetClipboardData(Native.RegisterClipboardFormat("CanUploadToCloudClipboard"), Native.GlobalFrom(new byte[4]));
                    }
                    finally { Native.CloseClipboard(); }
                    OwnsDelayed = true; Mark(); return true;
                }
                Thread.Sleep(40);
            }
            return false;
        }
        /// <summary>Places a promise of remote files (see VirtualFiles) on the clipboard without rendering anything.</summary>
        public bool OfferVirtualFiles(object comDataObject)
        {
            bool ok = Retry(delegate { Clipboard.SetDataObject(comDataObject, false, 5, 40); }); Mark(); return ok;
        }
        public void Dispose2() { try { Native.RemoveClipboardFormatListener(Handle); DestroyHandle(); } catch { } }
    }

    /// <summary>Optional encrypted blob storage for large files, through a private Telegram bot chat.</summary>
    static class Telegram
    {
        static string Api(string token, string method, string query)
        {
            var rq = (HttpWebRequest)WebRequest.Create("https://api.telegram.org/bot" + token + "/" + method + (query == null ? "" : "?" + query));
            rq.Timeout = 30000;
            using (var rs = rq.GetResponse()) using (var sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }
        /// <summary>Finds the chat of the most recent message sent to the bot (the user sends /start first).</summary>
        public static string FindChat(string token)
        {
            var d = Json.Parse(Api(token, "getUpdates", "limit=20"));
            string chat = null;
            foreach (var u in Json.List(d, "result"))
            {
                var msg = Json.D(u, "message") ?? Json.D(u, "channel_post"); if (msg == null) continue;
                var c = Json.D(msg, "chat"); if (c != null) chat = Json.S(c, "id");
            }
            return chat;
        }
        public static string Upload(string token, string chat, byte[] data, string name)
        {
            string bnd = "----sc" + App.RandHex(12);
            var head = new StringBuilder();
            head.Append("--" + bnd + "\r\nContent-Disposition: form-data; name=\"chat_id\"\r\n\r\n" + chat + "\r\n");
            head.Append("--" + bnd + "\r\nContent-Disposition: form-data; name=\"disable_notification\"\r\n\r\ntrue\r\n");
            head.Append("--" + bnd + "\r\nContent-Disposition: form-data; name=\"document\"; filename=\"" + name + "\"\r\nContent-Type: application/octet-stream\r\n\r\n");
            byte[] h = Encoding.UTF8.GetBytes(head.ToString()), t = Encoding.ASCII.GetBytes("\r\n--" + bnd + "--\r\n");
            var rq = (HttpWebRequest)WebRequest.Create("https://api.telegram.org/bot" + token + "/sendDocument");
            rq.Method = "POST"; rq.ContentType = "multipart/form-data; boundary=" + bnd; rq.Timeout = 600000; rq.ReadWriteTimeout = 600000;
            rq.AllowWriteStreamBuffering = false; rq.ContentLength = h.Length + data.Length + t.Length;
            using (var s = rq.GetRequestStream()) { s.Write(h, 0, h.Length); s.Write(data, 0, data.Length); s.Write(t, 0, t.Length); }
            string body;
            using (var rs = rq.GetResponse()) using (var sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
            var doc = Json.D(Json.D(Json.Parse(body), "result"), "document");
            string id = Json.S(doc, "file_id"); if (id == "") throw new IOException("Telegram: no file_id");
            return id;
        }
        public static byte[] Download(string token, string fileId)
        {
            var d = Json.Parse(Api(token, "getFile", "file_id=" + Uri.EscapeDataString(fileId)));
            string path = Json.S(Json.D(d, "result"), "file_path"); if (path == "") throw new IOException("Telegram: no file_path");
            using (var wc = new WebClient()) return wc.DownloadData("https://api.telegram.org/file/bot" + token + "/" + path);
        }
    }

    /// <summary>Remote Desktop: publishes how to reach this PC, and connects to the others with mstsc.</summary>
    static class Rdp
    {
        const string TsKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
        public static int Port()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(TsKey + @"\WinStations\RDP-Tcp")) return Convert.ToInt32(k.GetValue("PortNumber", 3389)); } catch { return 3389; }
        }
        public static bool Allowed()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(TsKey)) return Convert.ToInt32(k.GetValue("fDenyTSConnections", 1)) == 0; } catch { return false; }
        }
        public static bool Listening()
        {
            try { int p = Port(); foreach (var e in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()) if (e.Port == p) return true; } catch { }
            return false;
        }
        public static bool IsTailscale(string ip)
        {
            var p = ip.Split('.'); int a, b;
            return p.Length == 4 && int.TryParse(p[0], out a) && int.TryParse(p[1], out b) && a == 100 && b >= 64 && b <= 127;
        }
        public static List<string> Addresses()
        {
            var ts = new List<string>(); var lan = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString(); if (ip.StartsWith("169.254.")) continue;
                        if (IsTailscale(ip)) ts.Add(ip); else lan.Add(ip);
                    }
                }
            }
            catch { }
            ts.AddRange(lan); ts.Add(Environment.MachineName);
            return ts;      // overlay-network (works across networks) first, then LAN, then name
        }
        public static bool HasOverlay() { foreach (string a in Addresses()) if (IsTailscale(a)) return true; return false; }
        public static Dictionary<string, object> Info()
        {
            var d = new Dictionary<string, object>();
            d["on"] = Allowed() && Listening(); d["host"] = Environment.MachineName; d["user"] = Environment.UserName; d["port"] = Port(); d["addrs"] = Addresses();
            return d;
        }
        /// <summary>User-initiated (UAC prompt): allow RDP, open its firewall group, require NLA, autostart the service.</summary>
        public static bool EnableHost()
        {
            string cmd = "/c reg add \"HKLM\\" + TsKey + "\" /v fDenyTSConnections /t REG_DWORD /d 0 /f"
                + " & reg add \"HKLM\\" + TsKey + "\\WinStations\\RDP-Tcp\" /v UserAuthentication /t REG_DWORD /d 1 /f"
                + " & netsh advfirewall firewall set rule group=\"@FirewallAPI.dll,-28752\" new enable=Yes"
                + " & sc config TermService start= auto & net start TermService";
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", cmd); psi.Verb = "runas"; psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (var p = Process.Start(psi)) { p.WaitForExit(30000); }
                return true;
            }
            catch (Exception e) { Log.Write("rdp enable: " + e.Message); return false; }
        }
        static bool Probe(string host, int port)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(1800)) return false;
                    c.EndConnect(ar); return true;
                }
            }
            catch { return false; }
        }
        /// <summary>Finds the first reachable address of the target and launches a high-quality, auto-reconnecting session.</summary>
        public static void Connect(DeviceInfo d, Action<string> fail)
        {
            var addrs = new List<string>(d.Addrs); string name = d.Name, user = d.RdpUser; int port = d.RdpPort;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string hit = null;
                    foreach (string a in addrs) if (Probe(a, port)) { hit = a; break; }
                    if (hit == null) { fail(L.T("לא ניתן להגיע אל " + name + ". ודא שהמחשב דולק, ש-Remote Desktop מופעל בו, ושבשני המחשבים פועל Tailscale (לחיבור בין רשתות שונות).", "Cannot reach " + name + ". Make sure the PC is on, Remote Desktop is enabled on it, and Tailscale is running on both computers (needed across different networks).")); return; }
                    string dir = App.PathOf("rdp"); Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, string.Join("_", name.Split(Path.GetInvalidFileNameChars())) + ".rdp");
                    var sb = new StringBuilder();
                    sb.AppendLine("full address:s:" + hit + ":" + port);
                    if (!string.IsNullOrEmpty(user)) sb.AppendLine("username:s:" + user);
                    sb.AppendLine("screen mode id:i:2");
                    sb.AppendLine("session bpp:i:32");
                    sb.AppendLine("smart sizing:i:1");
                    sb.AppendLine("dynamic resolution:i:1");
                    sb.AppendLine("connection type:i:7");
                    sb.AppendLine("networkautodetect:i:1");
                    sb.AppendLine("bandwidthautodetect:i:1");
                    sb.AppendLine("compression:i:1");
                    sb.AppendLine("autoreconnection enabled:i:1");
                    sb.AppendLine("autoreconnect max retries:i:50");
                    sb.AppendLine("allow font smoothing:i:1");
                    sb.AppendLine("allow desktop composition:i:1");
                    sb.AppendLine("videoplaybackmode:i:1");
                    sb.AppendLine("audiomode:i:0");
                    sb.AppendLine("redirectclipboard:i:1");
                    sb.AppendLine("authentication level:i:2");
                    sb.AppendLine("enablecredsspsupport:i:1");
                    sb.AppendLine("keyboardhook:i:2");
                    File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
                    Process.Start("mstsc.exe", "\"" + file + "\"");
                    Log.Write("rdp connect to " + name + " via " + hit);
                }
                catch (Exception e) { fail(L.T("חיבור RDP נכשל: ", "Remote Desktop connection failed: ") + e.Message); }
            });
        }
    }

    /// <summary>
    /// Crash recovery with escalating levels:
    ///  0. in-process: every relay link self-reconnects, stale links are kicked, clipboard listener is re-armed, UI exceptions are contained.
    ///  1. watchdog process restarts the app after a crash or a hang (heartbeat).
    ///  2. repeated crashes: caches and history are reset.
    ///  3. still crashing: settings are rebuilt (pairing kept), local pins re-synced from the relay.
    ///  4. still crashing: safe mode (core sync only) + slowed restarts + a notification asking for attention.
    /// </summary>
    static class Recovery
    {
        public static int Level; public static string Notice; public static bool SafeMode;
        static Process wd; public static bool WatchdogEnabled = true;
        static string P(string n) { return App.PathOf(n); }

        public static void OnStart()
        {
            long now = App.Now();
            var st = Store.Load("recovery.json");
            var crashes = new List<long>();
            foreach (string s in Json.Strs(st, "crashes")) { long v; if (long.TryParse(s, out v) && now - v < 15 * 60000) crashes.Add(v); }
            bool dirty = File.Exists(P("running.flag"));
            try { File.Delete(P("clean-exit.flag")); } catch { }
            if (dirty)
            {
                crashes.Add(now); int n = crashes.Count;
                Level = n <= 1 ? 1 : n <= 3 ? 2 : n <= 5 ? 3 : 4;
                Log.Write("unclean shutdown detected, crash #" + n + " in 15 min -> recovery level " + Level);
                if (Level >= 2)
                {
                    foreach (string f in new[] { "history.bin", "history.bin.bak" }) try { File.Delete(P(f)); } catch { }
                    foreach (string dir in new[] { "incoming", "outgoing", "rdp" }) try { Directory.Delete(P(dir), true); } catch { }
                }
                if (Level >= 3)
                {
                    var old = Store.Load("config.json"); var fresh = new Dictionary<string, object>();
                    foreach (string k in new[] { "deviceId", "secret", "name", "autostart" }) if (old.ContainsKey(k)) fresh[k] = old[k];
                    Store.Save("config.json", fresh);
                    foreach (string f in new[] { "pins.bin", "pins.bin.bak" }) try { File.Delete(P(f)); } catch { }
                }
                if (Level >= 4) SafeMode = true;
                switch (Level)
                {
                    case 1: Notice = L.T("התוכנה נסגרה באופן לא תקין והופעלה מחדש אוטומטית. הסנכרון פעיל.", "Cliport stopped unexpectedly and was restarted automatically. Sync is active."); break;
                    case 2: Notice = L.T("זוהו קריסות חוזרות. המטמון וההיסטוריה אופסו והתוכנה הופעלה מחדש. הסנכרון פעיל.", "Repeated crashes were detected. The cache and history were reset and Cliport was restarted. Sync is active."); break;
                    case 3: Notice = L.T("הקריסות נמשכו. ההגדרות נבנו מחדש (הצימוד נשמר) והנעוצים יסונכרנו מחדש.", "Crashes continued. Settings were rebuilt (pairing kept) and pins will sync again."); break;
                    default: Notice = L.T("התוכנה פועלת במצב בטוח (סנכרון בסיסי בלבד) עקב קריסות חוזרות. פרטים בקובץ app.log בתיקיית הנתונים.", "Cliport is running in safe mode (basic sync only) after repeated crashes. See app.log in the data folder."); break;
                }
            }
            var o = new Dictionary<string, object>(); var cl = new List<object>(); foreach (long c in crashes) cl.Add(c.ToString());
            o["crashes"] = cl; o["level"] = Level; Store.Save("recovery.json", o);
            try { File.WriteAllText(P("running.flag"), Process.GetCurrentProcess().Id.ToString()); } catch { }
            Heartbeat();
            try { Native.RegisterApplicationRestart("--recovered --data \"" + App.DataDir + "\"", 0); } catch { }
        }
        public static bool Exiting; static long endingAt;
        public static void CleanExit()
        {
            try { File.WriteAllText(P("clean-exit.flag"), "1"); File.Delete(P("running.flag")); } catch { }
        }
        /// <summary>Windows is logging off / shutting down: that is a clean stop, not a crash.</summary>
        public static void SessionEnding() { endingAt = App.Now(); CleanExit(); Log.Write("session ending - marked as clean exit"); }
        public static void Heartbeat()
        {
            try
            {
                File.WriteAllText(P("heartbeat"), App.Now().ToString());
                // shutdown was cancelled and we are still running: go back to the "running" state
                if (!Exiting && endingAt != 0 && App.Now() - endingAt > 120000)
                {
                    endingAt = 0; File.Delete(P("clean-exit.flag"));
                    File.WriteAllText(P("running.flag"), Process.GetCurrentProcess().Id.ToString());
                }
            }
            catch { }
        }

        public static void EnsureWatchdog()
        {
            if (!WatchdogEnabled) return;
            try
            {
                if (wd != null && !wd.HasExited) return;
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--watchdog " + Process.GetCurrentProcess().Id + " --data \"" + App.DataDir + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                wd = Process.Start(psi);
            }
            catch (Exception e) { Log.Write("watchdog start: " + e.Message); }
        }

        public static int RunWatchdog(int pid)
        {
            Process main; try { main = Process.GetProcessById(pid); } catch { main = null; }
            Log.Write("watching pid " + pid);
            long last = App.Now(), graceUntil = App.Now() + 120000;
            while (true)
            {
                Thread.Sleep(3000);
                long now = App.Now();
                if (now - last > 20000) graceUntil = now + 120000;     // machine was asleep - don't mistake it for a hang
                last = now;
                if (File.Exists(P("clean-exit.flag"))) return 0;
                bool dead = main == null || main.HasExited;
                if (!dead && now > graceUntil)
                {
                    try
                    {
                        if (now - long.Parse(File.ReadAllText(P("heartbeat"))) > 90000)
                        {
                            Log.Write("main process is hung (no heartbeat for 90s) - killing it");
                            main.Kill(); main.WaitForExit(5000); dead = true;
                        }
                    }
                    catch { }
                }
                if (!dead) continue;
                Thread.Sleep(600);
                if (File.Exists(P("clean-exit.flag"))) return 0;
                int recent = Json.Strs(Store.Load("recovery.json"), "crashes").Count;
                int wait = recent >= 5 ? 60000 : 1500;
                Log.Write("main process died - restarting in " + wait + " ms");
                Thread.Sleep(wait);
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "--recovered --minimized --data \"" + App.DataDir + "\"");
                    psi.UseShellExecute = false; Process.Start(psi);
                }
                catch (Exception e) { Log.Write("restart failed: " + e.Message); }
                return 0;
            }
        }
    }

    /// <summary>
    /// Remote files on the clipboard as "virtual files" (FileGroupDescriptorW + FileContents), the same mechanism
    /// Remote Desktop uses. Explorer reads the descriptor (name, size) eagerly, but asks for the contents only when
    /// the user really pastes - which is when the confirmation and the download happen.
    /// </summary>
    [ComVisible(true)]
    sealed class VirtualFiles : System.Runtime.InteropServices.ComTypes.IDataObject
    {
        readonly System.Runtime.InteropServices.ComTypes.IDataObject inner;
        readonly Func<int, string> materialize;      // file index -> local path (null = declined / failed)
        static readonly short CfContents = (short)DataFormats.GetFormat("FileContents").Id;
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attrs, bool create, IntPtr template, out System.Runtime.InteropServices.ComTypes.IStream stream);

        public VirtualFiles(Offer o, Func<int, string> materialize)
        {
            this.materialize = materialize;
            var d = new DataObject();
            d.SetData("FileGroupDescriptorW", new MemoryStream(Descriptor(o)));
            d.SetData("FileContents", new MemoryStream(new byte[1]));              // placeholder so the format is listed
            d.SetData("Preferred DropEffect", new MemoryStream(new byte[] { 1, 0, 0, 0 }));
            d.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            d.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
            d.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
            inner = d;
        }
        static byte[] Descriptor(Offer o)
        {
            const int Size = 592;                                                  // sizeof(FILEDESCRIPTORW)
            var b = new byte[4 + Size * o.Files.Count];
            BitConverter.GetBytes(o.Files.Count).CopyTo(b, 0);
            for (int i = 0; i < o.Files.Count; i++)
            {
                int off = 4 + i * Size; var f = o.Files[i];
                BitConverter.GetBytes(unchecked((int)(0x00000004 | 0x00000040 | 0x00004000 | 0x80000000))).CopyTo(b, off);   // ATTRIBUTES | FILESIZE | PROGRESSUI | UNICODE
                BitConverter.GetBytes(0x80).CopyTo(b, off + 36);                   // FILE_ATTRIBUTE_NORMAL
                BitConverter.GetBytes((uint)(f.Size >> 32)).CopyTo(b, off + 64);
                BitConverter.GetBytes((uint)(f.Size & 0xFFFFFFFF)).CopyTo(b, off + 68);
                byte[] name = Encoding.Unicode.GetBytes(f.Name.Length > 259 ? f.Name.Substring(0, 259) : f.Name);
                Array.Copy(name, 0, b, off + 72, name.Length);
            }
            return b;
        }
        public void GetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format, out System.Runtime.InteropServices.ComTypes.STGMEDIUM medium)
        {
            if (format.cfFormat != CfContents) { inner.GetData(ref format, out medium); return; }
            medium = new System.Runtime.InteropServices.ComTypes.STGMEDIUM();
            if ((format.tymed & System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM) == 0) Marshal.ThrowExceptionForHR(unchecked((int)0x80040069));   // DV_E_TYMED
            string path = null;
            try { path = materialize(format.lindex); } catch (Exception e) { Log.Write("materialize: " + e); }
            if (path == null) throw new COMException("declined", unchecked((int)0x80004004));                                                              // E_ABORT
            System.Runtime.InteropServices.ComTypes.IStream s;
            int hr = SHCreateStreamOnFileEx(path, 0x20 /* STGM_READ | STGM_SHARE_DENY_WRITE */, 0, false, IntPtr.Zero, out s);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            medium.tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM;
            medium.unionmember = Marshal.GetComInterfaceForObject(s, typeof(System.Runtime.InteropServices.ComTypes.IStream));
            medium.pUnkForRelease = null;
        }
        public int QueryGetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format)
        {
            if (format.cfFormat == CfContents) return (format.tymed & System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM) != 0 ? 0 : unchecked((int)0x80040069);
            return inner.QueryGetData(ref format);
        }
        public void GetDataHere(ref System.Runtime.InteropServices.ComTypes.FORMATETC format, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium) { inner.GetDataHere(ref format, ref medium); }
        public int GetCanonicalFormatEtc(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatIn, out System.Runtime.InteropServices.ComTypes.FORMATETC formatOut) { return inner.GetCanonicalFormatEtc(ref formatIn, out formatOut); }
        public void SetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatIn, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium, bool release) { inner.SetData(ref formatIn, ref medium, release); }
        public System.Runtime.InteropServices.ComTypes.IEnumFORMATETC EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR direction) { return inner.EnumFormatEtc(direction); }
        public int DAdvise(ref System.Runtime.InteropServices.ComTypes.FORMATETC pFormatetc, System.Runtime.InteropServices.ComTypes.ADVF advf, System.Runtime.InteropServices.ComTypes.IAdviseSink adviseSink, out int connection) { return inner.DAdvise(ref pFormatetc, advf, adviseSink, out connection); }
        public void DUnadvise(int connection) { inner.DUnadvise(connection); }
        public int EnumDAdvise(out System.Runtime.InteropServices.ComTypes.IEnumSTATDATA enumAdvise) { return inner.EnumDAdvise(out enumAdvise); }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled
        {
            get { try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(App.Name) != null; } catch { return false; } }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue(App.Name, "\"" + Application.ExecutablePath + "\" --minimized");
                        else k.DeleteValue(App.Name, false);
                    }
                }
                catch (Exception e) { Log.Write("autostart: " + e.Message); }
            }
        }
    }
}
