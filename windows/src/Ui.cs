// Cliport v1.0 - tray application, main window, dialogs, entry point.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Cliport
{
    static class Ux
    {
        public static readonly Font F = new Font("Segoe UI", 10f);
        public static readonly Font FBig = new Font("Consolas", 15f, FontStyle.Bold);
        public static Icon AppIcon;
        public static void Rtl(Form f)
        {
            f.RightToLeft = RightToLeft.Yes; f.RightToLeftLayout = true; f.Font = F; if (AppIcon != null) f.Icon = AppIcon;
        }
        public static Button Btn(string text, EventHandler click)
        {
            var b = new Button(); b.Text = text; b.AutoSize = true; b.Padding = new Padding(8, 3, 8, 3); b.Margin = new Padding(4); b.Click += click; return b;
        }
        public static FlowLayoutPanel Bar(DockStyle dock, params Control[] cs)
        {
            var p = new FlowLayoutPanel(); p.Dock = dock; p.AutoSize = true; p.Padding = new Padding(6); p.Controls.AddRange(cs); return p;
        }
        public static Label Lbl(string text)
        {
            var l = new Label(); l.Text = text; l.AutoSize = true; l.Margin = new Padding(4, 8, 4, 4); return l;
        }
        public static string OneLine(string s)
        {
            if (s.Length > 140) s = s.Substring(0, 140) + "…";
            return s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        }
    }

    sealed class PairForm : Form
    {
        public string Code; TextBox tb;
        public PairForm()
        {
            Ux.Rtl(this); Text = "Cliport - צימוד מכשירים"; ClientSize = new Size(520, 250); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
            var l = new Label(); l.Text = "כדי לסנכרן, כל המכשירים חייבים לחלוק אותו קוד צימוד סודי.\nזה המכשיר הראשון? צור קבוצה חדשה. אחרת - הזן את הקוד שמוצג במכשיר קיים.";
            l.SetBounds(16, 14, 488, 60);
            var bNew = Ux.Btn("צור קבוצה חדשה", delegate { Code = KeyRing.NewCode(); DialogResult = DialogResult.OK; }); bNew.SetBounds(16, 80, 488, 40); bNew.AutoSize = false;
            var l2 = new Label(); l2.Text = "או הצטרף עם קוד קיים:"; l2.SetBounds(16, 136, 488, 24);
            tb = new TextBox(); tb.SetBounds(16, 162, 488, 28); tb.RightToLeft = RightToLeft.No; tb.Font = new Font("Consolas", 11f); tb.CharacterCasing = CharacterCasing.Upper;
            var bJoin = Ux.Btn("הצטרף", delegate
            {
                if (KeyRing.FromCode(tb.Text) == null) { MessageBox.Show(this, "הקוד אינו תקין. הקוד מורכב מ-26 תווים.", "Cliport", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                Code = B32.Encode(B32.Decode(tb.Text)); DialogResult = DialogResult.OK;
            });
            bJoin.SetBounds(16, 200, 150, 34); bJoin.AutoSize = false;
            Controls.AddRange(new Control[] { l, bNew, l2, tb, bJoin }); AcceptButton = bJoin;
        }
    }

    sealed class CodeForm : Form
    {
        public CodeForm(string code)
        {
            Ux.Rtl(this); Text = "קוד הצימוד"; ClientSize = new Size(560, 190); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            var l = new Label(); l.Text = "הזן את הקוד הזה באפליקציה במכשיר הנוסף (אייפון / אייפד / מחשב).\nהקוד הוא מפתח ההצפנה - אל תשתף אותו עם אחרים."; l.SetBounds(16, 12, 528, 50);
            var tb = new TextBox(); tb.ReadOnly = true; tb.Text = KeyRing.Pretty(code); tb.Font = Ux.FBig; tb.TextAlign = HorizontalAlignment.Center; tb.RightToLeft = RightToLeft.No; tb.SetBounds(16, 70, 528, 36);
            var b = Ux.Btn("העתק קוד", delegate { try { Clipboard.SetText(code); } catch { } }); b.SetBounds(16, 124, 140, 36); b.AutoSize = false;
            var c = Ux.Btn("סגור", delegate { Close(); }); c.SetBounds(166, 124, 100, 36); c.AutoSize = false;
            Controls.AddRange(new Control[] { l, tb, b, c });
        }
    }

    sealed class ProgressForm : Form
    {
        ProgressBar bar; Label lbl; public bool Finished;
        public ProgressForm(string title)
        {
            Ux.Rtl(this); Text = "Cliport"; ClientSize = new Size(420, 96); FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false; TopMost = true;
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false;
            lbl = new Label(); lbl.Text = title; lbl.SetBounds(14, 12, 392, 26); lbl.AutoEllipsis = true;
            bar = new ProgressBar(); bar.SetBounds(14, 48, 392, 24); bar.RightToLeftLayout = true;
            Controls.Add(lbl); Controls.Add(bar);
        }
        public void Set(string text, int pc) { lbl.Text = text; bar.Value = Math.Max(0, Math.Min(100, pc)); }
        public void Finish() { Finished = true; if (Visible) Close(); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); if (Finished) Close(); }
    }

    sealed class PinPopup : Form
    {
        ListBox lb; List<string> items = new List<string>(); public Action<string> Chosen;
        public PinPopup()
        {
            Ux.Rtl(this); FormBorderStyle = FormBorderStyle.FixedToolWindow; Text = "נעוצים - Enter להדבקה"; ShowInTaskbar = false; TopMost = true;
            ClientSize = new Size(420, 300); StartPosition = FormStartPosition.Manual; KeyPreview = true;
            lb = new ListBox(); lb.Dock = DockStyle.Fill; lb.IntegralHeight = false; lb.Font = Ux.F; Controls.Add(lb);
            lb.DoubleClick += delegate { Pick(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; Pick(); } else if (e.KeyCode == Keys.Escape) Hide(); };
            Deactivate += delegate { Hide(); };
        }
        void Pick() { int i = lb.SelectedIndex; if (i < 0 || i >= items.Count) return; string t = items[i]; Hide(); if (Chosen != null) Chosen(t); }
        public void ShowAt(List<string> pins)
        {
            items = pins; lb.Items.Clear();
            foreach (string p in pins) lb.Items.Add(Ux.OneLine(p));
            if (pins.Count == 0) lb.Items.Add("(אין פריטים נעוצים - נעץ פריט מהחלון הראשי)");
            lb.SelectedIndex = 0;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea; var pt = Cursor.Position;
            Location = new Point(Math.Max(wa.Left, Math.Min(pt.X - Width / 2, wa.Right - Width)), Math.Max(wa.Top, Math.Min(pt.Y - 40, wa.Bottom - Height)));
            Show(); Activate(); lb.Focus();
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
    }

    sealed class MainForm : Form
    {
        readonly Config cfg; readonly Engine eng; readonly ClipWindow clip; readonly NotifyIcon tray; readonly PinPopup popup = new PinPopup();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer(), debounce = new System.Windows.Forms.Timer();
        ListView lvClips, lvDev, lvRdp; Label lblStatus, lblRdp, lblTg; TextBox tbToken, tbChat, tbRelay; CheckBox cbAuto, cbAccept;
        Offer offer; string[] offerPaths; bool fetching; IntPtr prevFg; int tickN; bool exiting;
        readonly List<string> rowText = new List<string>();

        public MainForm(Config cfg)
        {
            this.cfg = cfg;
            Ux.Rtl(this); Text = "Cliport v" + App.Version; ClientSize = new Size(720, 520); MinimumSize = new Size(600, 420); StartPosition = FormStartPosition.CenterScreen;
            var h = Handle;   // force handle creation so BeginInvoke works while hidden
            var ctx = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            eng = new Engine(cfg, delegate (Action a) { ctx.Post(delegate { try { a(); } catch (Exception e) { Log.Write("ui post: " + e); } }, null); });
            eng.RdpInfo = Rdp.Info;
            clip = new ClipWindow();
            tray = new NotifyIcon(); tray.Icon = Ux.AppIcon ?? SystemIcons.Application; tray.Text = "Cliport"; tray.Visible = true;
            tray.DoubleClick += delegate { ShowMain(); };
            tray.BalloonTipClicked += delegate { if (offer != null && offerPaths == null && !offer.Image) FetchOfferNow(); };
            tray.ContextMenuStrip = new ContextMenuStrip(); tray.ContextMenuStrip.RightToLeft = RightToLeft.Yes; tray.ContextMenuStrip.Opening += delegate { BuildTrayMenu(); };
            BuildUi();

            eng.TextIn = delegate (string text, string from) { if (!clip.SetText(text)) Log.Write("clipboard busy - could not apply remote text"); offer = null; offerPaths = null; };
            eng.OfferIn = OnOffer;
            eng.PinsChanged = RefreshClips; eng.HistoryChanged = RefreshClips; eng.DevicesChanged = delegate { RefreshDevices(); }; eng.StatusChanged = RefreshStatus;
            eng.Notify = Toast;
            clip.Changed = delegate { debounce.Stop(); debounce.Start(); };
            clip.RenderFiles = RenderOffer;
            clip.HotKey = OnHotKey;
            popup.Chosen = PastePin;
            debounce.Interval = 120; debounce.Tick += delegate { debounce.Stop(); ReadLocalClipboard(0); };
            tick.Interval = 5000; tick.Tick += delegate { OnTick(); }; tick.Start();

            if (!Recovery.SafeMode)
            {
                if (!Native.RegisterHotKey(clip.Handle, 1, 0x0001 | 0x0002, 0x56)) Log.Write("hotkey Ctrl+Alt+V unavailable");
                if (!Native.RegisterHotKey(clip.Handle, 2, 0x0001 | 0x0002, 0x52)) Log.Write("hotkey Ctrl+Alt+R unavailable");
            }
            CleanOldFiles();
            eng.Start();
            RefreshClips(); RefreshDevices(); RefreshStatus();
            Recovery.EnsureWatchdog();
            if (Recovery.Notice != null) Toast("Cliport - התאוששות (דרגה " + Recovery.Level + ")", Recovery.Notice);
        }

        // ---------- UI construction ----------
        void BuildUi()
        {
            var tabs = new TabControl(); tabs.Dock = DockStyle.Fill; tabs.RightToLeftLayout = true;
            lblStatus = new Label(); lblStatus.Dock = DockStyle.Bottom; lblStatus.Height = 28; lblStatus.Padding = new Padding(8, 5, 8, 0);
            Controls.Add(tabs); Controls.Add(lblStatus);

            // clips
            var t1 = new TabPage("לוח ונעוצים");
            lvClips = NewList("תוכן", 560, "נעוץ", 70);
            lvClips.DoubleClick += delegate { CopySelected(); };
            t1.Controls.Add(lvClips);
            t1.Controls.Add(Ux.Bar(DockStyle.Bottom,
                Ux.Btn("העתק", delegate { CopySelected(); }),
                Ux.Btn("נעץ / בטל נעיצה", delegate { string s = SelText(); if (s == null) return; if (eng.IsPinned(s)) eng.Unpin(s); else eng.Pin(s); }),
                Ux.Btn("מחק מההיסטוריה", delegate { string s = SelText(); if (s != null) eng.RemoveHistory(s); }),
                Ux.Lbl("Ctrl+Alt+V - הדבקה מהירה מהנעוצים")));
            tabs.TabPages.Add(t1);

            // devices
            var t2 = new TabPage("מכשירים");
            lvDev = NewList("שם", 260, "סוג", 110, "מצב", 110, "גרסה", 80);
            t2.Controls.Add(lvDev);
            t2.Controls.Add(Ux.Bar(DockStyle.Bottom,
                Ux.Btn("הצג קוד צימוד", delegate { if (eng.Paired) new CodeForm(eng.Keys.Code).ShowDialog(this); }),
                Ux.Btn("שכח מכשיר", delegate { if (lvDev.SelectedItems.Count > 0 && lvDev.SelectedItems[0].Tag != null) eng.ForgetDevice((string)lvDev.SelectedItems[0].Tag); }),
                Ux.Btn("התנתק מהקבוצה", delegate
                {
                    if (MessageBox.Show(this, "לנתק את המחשב הזה מקבוצת הסנכרון?", "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    eng.Unpair(); EnsurePaired();
                })));
            tabs.TabPages.Add(t2);

            // rdp
            var t3 = new TabPage("שולחן עבודה מרוחק");
            lvRdp = NewList("מחשב", 220, "זמינות", 150, "כתובות", 300);
            lvRdp.DoubleClick += delegate { ConnectSelected(); };
            lblRdp = new Label(); lblRdp.Dock = DockStyle.Top; lblRdp.Height = 66; lblRdp.Padding = new Padding(8);
            t3.Controls.Add(lvRdp); t3.Controls.Add(lblRdp);
            t3.Controls.Add(Ux.Bar(DockStyle.Bottom,
                Ux.Btn("התחבר", delegate { ConnectSelected(); }),
                Ux.Btn("הפעל Remote Desktop במחשב זה", delegate
                {
                    if (MessageBox.Show(this, "הפעולה תפעיל את Remote Desktop של Windows במחשב זה (נדרש אישור מנהל):\n• התרת חיבורי RDP עם אימות NLA\n• פתיחת כלל חומת האש של Remote Desktop\n• הפעלה אוטומטית של השירות\n\nלהמשיך?", "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    Rdp.EnableHost(); eng.PublishDev(); RefreshDevices();
                }),
                Ux.Lbl("Ctrl+Alt+R - חיבור מהיר")));
            tabs.TabPages.Add(t3);

            // settings
            var t4 = new TabPage("הגדרות"); t4.AutoScroll = true;
            var fl = new FlowLayoutPanel(); fl.Dock = DockStyle.Fill; fl.FlowDirection = FlowDirection.TopDown; fl.WrapContents = false; fl.Padding = new Padding(10); fl.AutoScroll = true;
            cbAuto = new CheckBox(); cbAuto.Text = "הפעל את Cliport עם עליית Windows"; cbAuto.AutoSize = true; cbAuto.Checked = Autostart.Enabled;
            cbAuto.CheckedChanged += delegate { Autostart.Enabled = cbAuto.Checked; };
            cbAccept = new CheckBox(); cbAccept.Text = "אשר הדבקת קבצים אוטומטית (ללא חלון אישור)"; cbAccept.AutoSize = true; cbAccept.Checked = cfg.B("autoAccept", false);
            cbAccept.CheckedChanged += delegate { cfg.Set("autoAccept", cbAccept.Checked); cfg.Save(); };
            tbToken = new TextBox(); tbToken.Width = 520; tbToken.RightToLeft = RightToLeft.No; tbToken.UseSystemPasswordChar = true; tbToken.Text = eng.TgToken;
            tbChat = new TextBox(); tbChat.Width = 200; tbChat.RightToLeft = RightToLeft.No; tbChat.Text = eng.TgChat;
            lblTg = Ux.Lbl("");
            tbRelay = new TextBox(); tbRelay.Width = 520; tbRelay.RightToLeft = RightToLeft.No; tbRelay.Text = cfg.S("relays");
            fl.Controls.AddRange(new Control[] {
                cbAuto, cbAccept,
                Ux.Lbl("—— אחסון Telegram לקבצים גדולים (אופציונלי, מוצפן מקצה לקצה) ——"),
                Ux.Lbl("1. צור בוט אצל ‎@BotFather‎ והדבק כאן את ה-Token.  2. שלח לבוט ‎/start‎.  3. לחץ \"זהה צ'אט ושמור\"."),
                Ux.Lbl("Bot Token:"), tbToken, Ux.Lbl("Chat ID:"), tbChat,
                Ux.Btn("זהה צ'אט ושמור", delegate { SaveTelegram(); }), lblTg,
                Ux.Lbl("—— שרת ממסר פרטי (אופציונלי) ——"),
                Ux.Lbl("MQTT על TLS בפורמט host:port או user:pass@host:port. ההגדרה מופצת לכל המכשירים."),
                tbRelay,
                Ux.Btn("שמור ממסר", delegate { eng.SetRelays(tbRelay.Text); Toast("Cliport", "רשימת שרתי הממסר עודכנה."); }),
                Ux.Lbl("—— אודות ——"),
                Ux.Lbl("Cliport v" + App.Version + "  |  מזהה מכשיר: " + cfg.DeviceId + "  |  נתונים: " + App.DataDir),
                Ux.Btn("פתח את תיקיית הנתונים והיומן", delegate { try { Process.Start(App.DataDir); } catch { } })
            });
            t4.Controls.Add(fl); tabs.TabPages.Add(t4);
        }
        ListView NewList(params object[] cols)
        {
            var lv = new ListView(); lv.Dock = DockStyle.Fill; lv.View = View.Details; lv.FullRowSelect = true; lv.HideSelection = false; lv.MultiSelect = false;
            lv.RightToLeftLayout = true; lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            for (int i = 0; i < cols.Length; i += 2) lv.Columns.Add((string)cols[i], (int)cols[i + 1]);
            return lv;
        }

        // ---------- refresh ----------
        void RefreshClips()
        {
            lvClips.BeginUpdate(); lvClips.Items.Clear(); rowText.Clear();
            var pins = eng.LivePins();
            foreach (string p in pins) { lvClips.Items.Add(new ListViewItem(new[] { Ux.OneLine(p), "📌" })); rowText.Add(p); }
            foreach (string hsty in eng.History) if (!pins.Contains(hsty)) { lvClips.Items.Add(new ListViewItem(new[] { Ux.OneLine(hsty), "" })); rowText.Add(hsty); }
            lvClips.EndUpdate();
        }
        void RefreshDevices()
        {
            lvDev.Items.Clear(); lvRdp.Items.Clear();
            var me = new ListViewItem(new[] { cfg.DeviceName + " (מחשב זה)", "Windows", eng.UpCount > 0 ? "מחובר" : "מנותק", App.Version }); lvDev.Items.Add(me);
            foreach (var d in eng.Devices.Values)
            {
                var it = new ListViewItem(new[] { d.Name, d.Plat == "ios" ? "iPhone / iPad" : "Windows", d.IsOnline ? "מחובר" : "לא מחובר", d.Ver }); it.Tag = d.Id; lvDev.Items.Add(it);
                if (d.Plat == "win")
                {
                    var r = new ListViewItem(new[] { d.Name, !d.IsOnline ? "לא מחובר" : d.RdpOn ? "זמין" : "RDP כבוי", string.Join(", ", d.Addrs.ToArray()) }); r.Tag = d.Id; lvRdp.Items.Add(r);
                }
            }
            bool on = Rdp.Allowed() && Rdp.Listening(), ov = Rdp.HasOverlay();
            lblRdp.Text = "מחשב זה: Remote Desktop " + (on ? "פעיל וזמין" : "כבוי") + "  |  Tailscale (חיבור בין רשתות שונות): " + (ov ? "פעיל" : "לא זוהה") +
                (ov ? "" : "\nלחיבור מרחוק ללא תלות ברשת התקן Tailscale (חינם) בכל המכשירים והתחבר לאותו חשבון.") +
                "\nמאייפון / אייפד: פתח את Cliport ← שולחן עבודה מרוחק ← בחר מחשב.";
        }
        void RefreshStatus()
        {
            int up = eng.UpCount, total = eng.Hub == null ? 0 : eng.Hub.Links.Count;
            string s = !eng.Paired ? "לא מצומד" : eng.Paused ? "הסנכרון מושהה" : up == 0 ? "מתחבר מחדש..." : "מחובר (" + up + "/" + total + " שרתי ממסר)";
            lblStatus.Text = "מצב: " + s + (Recovery.SafeMode ? "  |  מצב בטוח" : "") + (eng.TgReady ? "  |  אחסון Telegram פעיל" : "");
            tray.Text = "Cliport - " + s;
            if (lvDev.Items.Count > 0) lvDev.Items[0].SubItems[2].Text = up > 0 ? "מחובר" : "מנותק";
            if (lblTg != null) lblTg.Text = eng.TgReady ? "אחסון Telegram מוגדר (משותף לכל המכשירים)." : "ללא Telegram: קבצים עד 25MB עוברים ישירות דרך הממסר.";
        }
        void OnTick()
        {
            Recovery.Heartbeat(); Recovery.EnsureWatchdog();
            eng.Tick();
            if (++tickN % 6 == 0) { clip.Rearm(); if (Visible) RefreshDevices(); }
        }
        void Toast(string title, string text) { try { tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Info); } catch { } Log.Write("notify: " + title + " | " + text); }

        // ---------- local clipboard -> network ----------
        void ReadLocalClipboard(int attempt)
        {
            if (!eng.Paired || eng.Paused) return;
            try
            {
                IDataObject data = Clipboard.GetDataObject(); if (data == null) return;
                if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return;   // password managers etc.
                if (Clipboard.ContainsFileDropList())
                {
                    var l = new List<string>(); foreach (string s in Clipboard.GetFileDropList()) if (File.Exists(s)) l.Add(s);
                    if (l.Count > 0) { offer = null; offerPaths = null; eng.LocalFiles(l.ToArray(), false); }
                }
                else if (Clipboard.ContainsText()) { string t = Clipboard.GetText(TextDataFormat.UnicodeText); offer = null; offerPaths = null; eng.LocalText(t); }
                else if (Clipboard.ContainsImage())
                {
                    using (Image img = Clipboard.GetImage())
                    {
                        if (img == null) return;
                        string dir = App.PathOf("outgoing"); Directory.CreateDirectory(dir);
                        string p = Path.Combine(dir, "image-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                        img.Save(p, System.Drawing.Imaging.ImageFormat.Png);
                        offer = null; offerPaths = null; eng.LocalFiles(new[] { p }, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                if (attempt < 6) { var t = new System.Windows.Forms.Timer(); t.Interval = 80; t.Tick += delegate { t.Stop(); t.Dispose(); ReadLocalClipboard(attempt + 1); }; t.Start(); }
            }
            catch (Exception e) { Log.Write("read clipboard: " + e.Message); }
        }
        string SelText() { if (lvClips.SelectedIndices.Count == 0) return null; int i = lvClips.SelectedIndices[0]; return i < rowText.Count ? rowText[i] : null; }
        void CopySelected() { string s = SelText(); if (s == null) return; if (clip.SetText(s)) { eng.LocalText(s); } }

        // ---------- remote files ----------
        static string Describe(Offer o)
        {
            var sb = new StringBuilder();
            foreach (var f in o.Files) sb.AppendLine("שם: " + f.Name + "\nגודל: " + App.SizeText(f.Size) + "\nסוג: " + Mime.Label(f.Name) + (f.Mime != "" ? " (" + f.Mime + ")" : "") + "\n");
            return sb.ToString().TrimEnd();
        }
        void OnOffer(Offer o)
        {
            offer = o; offerPaths = null;
            if (o.Image && o.TotalSize <= 15L * 1024 * 1024)
            {
                eng.RequestFile(o, 0, null, delegate (string path, string err)
                {
                    if (err != null) { Toast("Cliport", "קבלת התמונה נכשלה: " + err); return; }
                    try { using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read)) using (var img = Image.FromStream(fs)) using (var bmp = new Bitmap(img)) clip.SetImage(bmp); }
                    catch (Exception e) { Log.Write("set image: " + e.Message); }
                });
                return;
            }
            bool armed = !Recovery.SafeMode && clip.OfferDelayedFiles();
            var f0 = o.Files[0];
            Toast("קובץ זמין להדבקה מ-" + o.FromName, f0.Name + (o.Files.Count > 1 ? " (+" + (o.Files.Count - 1) + ")" : "") + " • " + App.SizeText(o.TotalSize) + " • " + Mime.Label(f0.Name) +
                (armed ? "\nהדבק (Ctrl+V) בכל תיקייה כדי לקבל אותו." : "\nלחץ כאן כדי להוריד."));
        }
        bool Confirm(Offer o)
        {
            if (cfg.B("autoAccept", false)) return true;
            var owner = new Form(); owner.TopMost = true;   // keep the question above the app that is pasting
            try
            {
                return MessageBox.Show(owner, "לאשר הדבקת קובץ מ-" + o.FromName + "?\n\n" + Describe(o), "Cliport - אישור הדבקת קובץ",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading) == DialogResult.Yes;
            }
            finally { owner.Dispose(); }
        }
        /// <summary>Downloads every file of the offer behind a modal progress window. Returns local paths or null.</summary>
        string[] Download(Offer o)
        {
            if (fetching) return null;
            fetching = true;
            try
            {
                var got = new List<string>(); string error = null; int idx = 0;
                var pf = new ProgressForm("מקבל: " + o.Files[0].Name);
                Action next = null;
                next = delegate
                {
                    int cur = idx;
                    eng.RequestFile(o, cur, delegate (int pc) { pf.Set("מקבל: " + o.Files[cur].Name + " (" + (cur + 1) + "/" + o.Files.Count + ")", pc); },
                        delegate (string path, string err)
                        {
                            if (err != null) { error = err; pf.Finish(); return; }
                            got.Add(path); idx++;
                            if (idx < o.Files.Count) next(); else pf.Finish();
                        });
                };
                next();
                pf.ShowDialog(); pf.Dispose();
                if (error != null) { Toast("Cliport - העברת הקובץ נכשלה", error); return null; }
                return got.ToArray();
            }
            finally { fetching = false; }
        }
        /// <summary>WM_RENDERFORMAT: an application is pasting the promised files right now.</summary>
        string[] RenderOffer()
        {
            var o = offer; if (o == null) return null;
            if (offerPaths != null) return offerPaths;
            if (!Confirm(o)) return null;
            var paths = Download(o); if (paths == null) return null;
            offerPaths = paths;
            // also place the real files on the clipboard afterwards, in case the pasting app gave up waiting
            BeginInvoke((Action)delegate { if (offer == o) clip.SetFiles(paths); });
            return paths;
        }
        void FetchOfferNow()
        {
            var o = offer; if (o == null) { Toast("Cliport", "אין קובץ ממתין."); return; }
            if (offerPaths == null) { if (!Confirm(o)) return; var p = Download(o); if (p == null) return; offerPaths = p; }
            clip.SetFiles(offerPaths);
            Toast("Cliport", "הקובץ מוכן - הדבק (Ctrl+V) בכל תיקייה.");
        }

        // ---------- hotkeys, pins, rdp ----------
        void OnHotKey(int id)
        {
            if (id == 1) { prevFg = Native.GetForegroundWindow(); popup.ShowAt(eng.LivePins()); }
            else if (id == 2) { var m = new ContextMenuStrip(); m.RightToLeft = RightToLeft.Yes; AddRdpItems(m.Items); Native.SetForegroundWindow(clip.Handle); m.Show(Cursor.Position); }
        }
        void PastePin(string text)
        {
            if (!clip.SetText(text)) return;
            eng.LocalText(text);     // the pinned item becomes the current clipboard on every device
            if (prevFg != IntPtr.Zero) { Native.SetForegroundWindow(prevFg); Thread.Sleep(120); try { SendKeys.SendWait("^v"); } catch { } }
        }
        void AddRdpItems(ToolStripItemCollection items)
        {
            int n = 0;
            foreach (var d in eng.Devices.Values)
            {
                if (d.Plat != "win") continue; n++;
                var dev = d; var it = new ToolStripMenuItem(d.Name + (d.IsOnline && d.RdpOn ? "" : "  (לא זמין כרגע)"));
                it.Click += delegate { Rdp.Connect(dev, delegate (string err) { BeginInvoke((Action)delegate { Toast("Cliport - RDP", err); }); }); };
                items.Add(it);
            }
            if (n == 0) { var e = new ToolStripMenuItem("אין מחשבים נוספים בקבוצה"); e.Enabled = false; items.Add(e); }
        }
        void ConnectSelected()
        {
            if (lvRdp.SelectedItems.Count == 0) return;
            DeviceInfo d; if (!eng.Devices.TryGetValue((string)lvRdp.SelectedItems[0].Tag, out d)) return;
            Rdp.Connect(d, delegate (string err) { BeginInvoke((Action)delegate { Toast("Cliport - RDP", err); }); });
        }
        void SaveTelegram()
        {
            string token = tbToken.Text.Trim(), chat = tbChat.Text.Trim();
            if (token == "") { eng.SetTelegram("", ""); RefreshStatus(); return; }
            try
            {
                if (chat == "") { chat = Telegram.FindChat(token); if (chat == null) { MessageBox.Show(this, "לא נמצאה הודעה לבוט. שלח לבוט ‎/start‎ בטלגרם ונסה שוב.", "Cliport"); return; } tbChat.Text = chat; }
                eng.SetTelegram(token, chat); RefreshStatus(); Toast("Cliport", "אחסון Telegram הוגדר ויופץ לכל המכשירים.");
            }
            catch (Exception e) { MessageBox.Show(this, "הפנייה ל-Telegram נכשלה: " + e.Message, "Cliport"); }
        }

        // ---------- tray / window ----------
        void BuildTrayMenu()
        {
            var m = tray.ContextMenuStrip.Items; m.Clear();
            m.Add("פתח את Cliport", null, delegate { ShowMain(); });
            var pins = new ToolStripMenuItem("נעוצים");
            foreach (string p in eng.LivePins()) { string t = p; pins.DropDownItems.Add(Ux.OneLine(p).Length > 60 ? Ux.OneLine(p).Substring(0, 60) + "…" : Ux.OneLine(p), null, delegate { if (clip.SetText(t)) eng.LocalText(t); }); }
            if (pins.DropDownItems.Count == 0) pins.Enabled = false;
            m.Add(pins);
            var rdp = new ToolStripMenuItem("שולחן עבודה מרוחק"); AddRdpItems(rdp.DropDownItems); m.Add(rdp);
            if (offer != null && !offer.Image) m.Add("הורד את הקובץ הממתין (" + offer.Files[0].Name + ")", null, delegate { FetchOfferNow(); });
            var pause = new ToolStripMenuItem("השהה סנכרון"); pause.Checked = eng.Paused; pause.Click += delegate { eng.Paused = !eng.Paused; RefreshStatus(); }; m.Add(pause);
            m.Add(new ToolStripSeparator());
            m.Add("יציאה", null, delegate { ExitApp(); });
        }
        public void ShowMain() { RefreshDevices(); Show(); WindowState = FormWindowState.Normal; Activate(); }
        public bool EnsurePaired()
        {
            if (eng.Paired) return true;
            using (var pf = new PairForm())
            {
                if (pf.ShowDialog() != DialogResult.OK) return false;
                cfg.Code = pf.Code; cfg.Save(); eng.Start(); RefreshStatus(); RefreshDevices();
                if (!cfg.B("askedAutostart", false))
                {
                    cfg.Set("askedAutostart", true); cfg.Save();
                    if (MessageBox.Show("להפעיל את Cliport אוטומטית עם עליית Windows? (מומלץ - כך הסנכרון תמיד פעיל)", "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { Autostart.Enabled = true; cbAuto.Checked = true; }
                }
                new CodeForm(eng.Keys.Code).ShowDialog();
                return true;
            }
        }
        void CleanOldFiles()
        {
            foreach (string sub in new[] { "incoming", "outgoing" })
                try
                {
                    string dir = App.PathOf(sub); if (!Directory.Exists(dir)) continue;
                    foreach (string e in Directory.GetFileSystemEntries(dir))
                        if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(e) > TimeSpan.FromDays(2)) { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); }
                }
                catch { }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
        public void ExitApp()
        {
            exiting = true; Recovery.CleanExit();
            try { tick.Stop(); eng.Stop(); Native.UnregisterHotKey(clip.Handle, 1); Native.UnregisterHotKey(clip.Handle, 2); clip.Dispose2(); tray.Visible = false; tray.Dispose(); } catch { }
            Application.Exit();
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.Name);
            string join = null; bool initNew = false, minimized = false; int wdPid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--data": if (i + 1 < args.Length) data = args[++i]; break;
                    case "--join": if (i + 1 < args.Length) join = args[++i]; break;
                    case "--watchdog": if (i + 1 < args.Length) int.TryParse(args[++i], out wdPid); break;
                    case "--init-new": initNew = true; break;
                    case "--minimized": case "--recovered": minimized = true; break;
                    case "--no-watchdog": Recovery.WatchdogEnabled = false; break;
                }
            }
            App.DataDir = Path.GetFullPath(data); Directory.CreateDirectory(App.DataDir);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (wdPid != 0) { Log.Tag = "watchdog"; return Recovery.RunWatchdog(wdPid); }

            string key = App.Hex(System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(App.DataDir.ToLowerInvariant()))).Substring(0, 16);
            bool created; var mutex = new Mutex(true, "Cliport-" + key, out created);
            var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Cliport-show-" + key);
            if (!created) { showEvt.Set(); return 0; }       // already running: just bring its window up

            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            // level 0: a failing UI callback is logged and contained; the app keeps running
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Write("contained UI exception: " + e.Exception); };
            // anything else is fatal: log it and let the watchdog restart us
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.Write("FATAL: " + e.ExceptionObject); };

            Log.Write("---- Cliport v" + App.Version + " starting (pid " + Process.GetCurrentProcess().Id + ") ----");
            Recovery.OnStart();
            try { Ux.AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var cfg = new Config();
            if (join != null && KeyRing.FromCode(join) != null) cfg.Code = B32.Encode(B32.Decode(join));
            else if (initNew && cfg.Code == "") cfg.Code = KeyRing.NewCode();
            cfg.Save();

            var form = new MainForm(cfg);
            var waiter = new Thread(delegate () { while (true) { showEvt.WaitOne(); try { form.BeginInvoke((Action)form.ShowMain); } catch { } } });
            waiter.IsBackground = true; waiter.Start();
            if (!form.EnsurePaired()) { form.ExitApp(); return 0; }
            if (!minimized) form.ShowMain();
            Application.Run();
            Recovery.CleanExit();
            GC.KeepAlive(mutex);
            return 0;
        }
    }
}
