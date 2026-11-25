// Cliport v1.0 - user interface: themes (light / device / dark), two languages, menu, toolbar, pages, dialogs, entry point.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Cliport
{
    // ====================================================================================================
    //  Theme
    // ====================================================================================================
    static class Theme
    {
        public static string Mode = "system";      // light | dark | system (follow the device)
        public static bool Dark, NoHover;
        public static Color Bg, Side, Card, Text, Sub, Border, Accent, AccentText, Sel, Ok, Warn, Danger;
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

        public static bool SystemDark()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0; }
            catch { return false; }
        }
        static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        public static void Resolve()
        {
            Dark = Mode == "dark" || (Mode != "light" && SystemDark());
            if (Dark)
            {
                Bg = C(0x17181D); Side = C(0x1E2027); Card = C(0x262830); Text = C(0xECEDF2); Sub = C(0x9BA0B3); Border = C(0x383B47);
                Accent = C(0x8286FF); AccentText = C(0x10111A); Sel = C(0x33366B); Ok = C(0x4ADE80); Warn = C(0xFBBF24); Danger = C(0xF87171);
            }
            else
            {
                Bg = C(0xF6F7FB); Side = C(0xECEEF6); Card = C(0xFFFFFF); Text = C(0x1B1D27); Sub = C(0x676C80); Border = C(0xD8DBE6);
                Accent = C(0x5457E8); AccentText = C(0xFFFFFF); Sel = C(0xE2E4FF); Ok = C(0x16A34A); Warn = C(0xD97706); Danger = C(0xDC2626);
            }
        }
        /// <summary>Colours a control tree. Roles come from Control.Tag: side, card, line, primary, nav, nav+, sub, head, danger.</summary>
        public static void Apply(Control c)
        {
            string role = c.Tag as string ?? "";
            var form = c as Form;
            if (form != null)
            {
                form.BackColor = Bg; form.ForeColor = Text;
                try { int on = Dark ? 1 : 0; if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4); } catch { }
            }
            else if (c is ToolStrip)
            {
                var ts = (ToolStrip)c; ts.Renderer = new StripRenderer(); ts.BackColor = c is StatusStrip ? Side : Bg; ts.ForeColor = Text;
            }
            else if (c is Button)
            {
                var b = (Button)c; b.FlatStyle = FlatStyle.Flat; b.UseVisualStyleBackColor = false;
                if (role == "primary") { b.BackColor = Accent; b.ForeColor = AccentText; b.FlatAppearance.BorderColor = Accent; }
                else if (role.StartsWith("nav")) { b.BackColor = role == "nav+" ? Sel : Side; b.ForeColor = Text; b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = NoHover ? b.BackColor : Sel; }
                else { b.BackColor = Card; b.ForeColor = role == "danger" ? Danger : Text; b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = Sel; }
            }
            else if (c is TextBox || c is ComboBox)
            {
                c.BackColor = Card; c.ForeColor = Text;
                var tb = c as TextBox; if (tb != null) tb.BorderStyle = BorderStyle.FixedSingle;
                var cb = c as ComboBox; if (cb != null) cb.FlatStyle = FlatStyle.Flat;
            }
            else if (c is ListBox) { c.BackColor = Bg; c.ForeColor = Text; }
            else if (c is WebBrowser) { }
            else if (c is LinkLabel) { var l = (LinkLabel)c; l.LinkColor = Accent; l.ActiveLinkColor = Accent; l.VisitedLinkColor = Accent; l.BackColor = Color.Transparent; }
            else if (c is Label) { c.ForeColor = role == "sub" ? Sub : role == "danger" ? Danger : Text; c.BackColor = Color.Transparent; }
            else if (c is CheckBox) { c.ForeColor = Text; c.BackColor = Color.Transparent; }
            else if (c is PictureBox) { c.BackColor = Color.Transparent; }
            else { c.BackColor = role == "side" ? Side : role == "card" ? Card : role == "line" ? Border : Bg; c.ForeColor = Text; }

            if (c is ListBox || c is TextBox || c is FlowLayoutPanel)
                try { SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
            foreach (Control k in c.Controls) Apply(k);
            var cm = c.ContextMenuStrip; if (cm != null) Style(cm);
        }
        public static void Style(ToolStripDropDown cm) { cm.Renderer = new StripRenderer(); cm.BackColor = Card; cm.ForeColor = Text; }

        sealed class StripColors : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin { get { return Bg; } }
            public override Color MenuStripGradientEnd { get { return Bg; } }
            public override Color ToolStripGradientBegin { get { return Bg; } }
            public override Color ToolStripGradientMiddle { get { return Bg; } }
            public override Color ToolStripGradientEnd { get { return Bg; } }
            public override Color ToolStripBorder { get { return Border; } }
            public override Color ToolStripDropDownBackground { get { return Card; } }
            public override Color ImageMarginGradientBegin { get { return Card; } }
            public override Color ImageMarginGradientMiddle { get { return Card; } }
            public override Color ImageMarginGradientEnd { get { return Card; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Sel; } }
            public override Color MenuItemSelected { get { return Sel; } }
            public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
            public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
            public override Color MenuItemPressedGradientBegin { get { return Card; } }
            public override Color MenuItemPressedGradientMiddle { get { return Card; } }
            public override Color MenuItemPressedGradientEnd { get { return Card; } }
            public override Color ButtonSelectedHighlight { get { return Sel; } }
            public override Color ButtonSelectedBorder { get { return Sel; } }
            public override Color ButtonSelectedGradientBegin { get { return Sel; } }
            public override Color ButtonSelectedGradientMiddle { get { return Sel; } }
            public override Color ButtonSelectedGradientEnd { get { return Sel; } }
            public override Color ButtonPressedGradientBegin { get { return Sel; } }
            public override Color ButtonPressedGradientMiddle { get { return Sel; } }
            public override Color ButtonPressedGradientEnd { get { return Sel; } }
            public override Color ButtonCheckedGradientBegin { get { return Sel; } }
            public override Color ButtonCheckedGradientMiddle { get { return Sel; } }
            public override Color ButtonCheckedGradientEnd { get { return Sel; } }
            public override Color ButtonCheckedHighlight { get { return Sel; } }
            public override Color CheckBackground { get { return Sel; } }
            public override Color CheckSelectedBackground { get { return Sel; } }
            public override Color CheckPressedBackground { get { return Sel; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Border; } }
            public override Color StatusStripGradientBegin { get { return Side; } }
            public override Color StatusStripGradientEnd { get { return Side; } }
            public override Color GripDark { get { return Border; } }
            public override Color GripLight { get { return Bg; } }
            public override Color OverflowButtonGradientBegin { get { return Bg; } }
            public override Color OverflowButtonGradientMiddle { get { return Bg; } }
            public override Color OverflowButtonGradientEnd { get { return Bg; } }
        }
        sealed class StripRenderer : ToolStripProfessionalRenderer
        {
            public StripRenderer() : base(new StripColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (!(e.Item is ToolStripStatusLabel) || e.Item.Tag as string == "sub") e.TextColor = e.Item.Enabled ? (e.Item.Tag as string == "sub" ? Sub : Text) : Sub;
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Text; base.OnRenderArrow(e); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e); }
        }
    }

    // ====================================================================================================
    //  Shared UI helpers
    // ====================================================================================================
    static class Ux
    {
        public static Font F, FBold, FSmall, FHead, FMono; public static Icon AppIcon; public static Image Logo;
        static float scale = 1f; static string glyphFont = "Segoe MDL2 Assets";
        // Segoe icon glyphs
        public const int GCopy = 0xE8C8, GPin = 0xE718, GDelete = 0xE74D, GSend = 0xE724, GPause = 0xE769, GPlay = 0xE768, GRemote = 0xE8AF,
            GDevices = 0xE772, GSettings = 0xE713, GHelp = 0xE897, GInfo = 0xE946, GLink = 0xE71B, GDownload = 0xE896, GClip = 0xE77F, GSearch = 0xE721,
            GBackup = 0xE74E, GRestore = 0xE777, GImport = 0xE8B5, GExport = 0xEDE1, GPhone = 0xE8EA, GPc = 0xE7F8, GFile = 0xE8A5, GTheme = 0xE793, GGlobe = 0xE774, GFolder = 0xE8B7, GText = 0xE8C1, GClear = 0xE894, GExit = 0xE7E8, GKey = 0xE8D7;

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            F = new Font("Segoe UI", 10f); FBold = new Font("Segoe UI Semibold", 10.5f); FSmall = new Font("Segoe UI", 9f); FHead = new Font("Segoe UI Semibold", 14f); FMono = new Font("Consolas", 15f, FontStyle.Bold);
            using (var fc = new InstalledFontCollection()) foreach (var ff in fc.Families) if (ff.Name == "Segoe Fluent Icons") glyphFont = ff.Name;
            try { AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            try { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")) if (s != null) Logo = new Bitmap(Image.FromStream(s)); } catch { }
        }
        public static int S(int px) { return (int)Math.Round(px * scale); }
        public static DockStyle Lead { get { return L.Rtl ? DockStyle.Right : DockStyle.Left; } }
        public static DockStyle Trail { get { return L.Rtl ? DockStyle.Left : DockStyle.Right; } }
        public static void Setup(Form f)
        {
            f.AutoScaleMode = AutoScaleMode.None; f.Font = F; f.RightToLeft = L.Rtl ? RightToLeft.Yes : RightToLeft.No; if (AppIcon != null) f.Icon = AppIcon;
        }
        public static Bitmap Glyph(int code, int px, Color color)
        {
            int n = S(px); var b = new Bitmap(n, n);
            using (var g = Graphics.FromImage(b)) using (var f = new Font(glyphFont, n * 0.74f, GraphicsUnit.Pixel)) using (var br = new SolidBrush(color)) using (var sf = new StringFormat())
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString(((char)code).ToString(), f, br, new RectangleF(0, S(1), n, n), sf);
            }
            return b;
        }
        public static Button Btn(string text, EventHandler click, string role = "")
        {
            var b = new Button(); b.Text = text; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(S(10), S(4), S(10), S(4)); b.Margin = new Padding(S(4));
            b.Tag = role; b.Click += click; b.Cursor = Cursors.Hand; return b;
        }
        public static Label Lbl(string text, string role = "")
        {
            var l = new Label(); l.Text = text; l.AutoSize = true; l.Margin = new Padding(S(4), S(6), S(4), S(2)); l.Tag = role;
            if (role == "head") { l.Font = FBold; l.Margin = new Padding(S(4), S(18), S(4), S(4)); }
            if (role == "sub") { l.Font = FSmall; l.MaximumSize = new Size(S(640), 0); }
            return l;
        }
        public static FlowLayoutPanel Row(DockStyle dock, params Control[] cs)
        {
            var p = new FlowLayoutPanel(); p.Dock = dock; p.AutoSize = true; p.WrapContents = true; p.Padding = new Padding(0, S(6), 0, 0); p.Controls.AddRange(cs); return p;
        }
        public static string OneLine(string s)
        {
            if (s.Length > 220) s = s.Substring(0, 220) + "…";
            return s.Replace("\r\n", " ⏎ ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        public static void Cue(TextBox tb, string text) { tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, (IntPtr)1, text); }; if (tb.IsHandleCreated) SendMessage(tb.Handle, 0x1501, (IntPtr)1, text); }
    }

    /// <summary>One row of a CardList: icon, title, optional second line, optional badge on the far side.</summary>
    sealed class Card
    {
        public string Title = "", Sub = "", Badge = ""; public int Glyph; public Color? Tint; public Color? BadgeColor; public object Data; public bool Hint;
        public override string ToString() { return Title; }
    }

    /// <summary>Owner-drawn, theme-aware, direction-aware list.</summary>
    sealed class CardList : ListBox
    {
        public CardList()
        {
            DrawMode = DrawMode.OwnerDrawFixed; BorderStyle = BorderStyle.None; IntegralHeight = false; ItemHeight = Ux.S(52); Dock = DockStyle.Fill;
        }
        public Card Selected { get { var c = SelectedItem as Card; return c != null && !c.Hint ? c : null; } }
        // Screen readers: the rows are drawn by hand, so each one is described explicitly.
        protected override AccessibleObject CreateAccessibilityInstance() { return new ListAcc(this); }
        sealed class ListAcc : ControlAccessibleObject
        {
            readonly CardList o;
            public ListAcc(CardList owner) : base(owner) { o = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override int GetChildCount() { return o.Items.Count; }
            public override AccessibleObject GetChild(int index) { return index >= 0 && index < o.Items.Count ? new RowAcc(o, this, index) : null; }
            public override AccessibleObject GetSelected() { return GetChild(o.SelectedIndex); }
            public override AccessibleObject GetFocused() { return o.Focused ? GetChild(o.SelectedIndex) : null; }
            public override AccessibleObject HitTest(int x, int y) { int i = o.IndexFromPoint(o.PointToClient(new Point(x, y))); return i >= 0 ? GetChild(i) : this; }
        }
        sealed class RowAcc : AccessibleObject
        {
            readonly CardList o; readonly AccessibleObject parent; readonly int i;
            public RowAcc(CardList owner, AccessibleObject p, int index) { o = owner; parent = p; i = index; }
            Card C { get { return i < o.Items.Count ? o.Items[i] as Card : null; } }
            public override string Name { get { var c = C; return c == null ? "" : c.Title; } set { } }
            public override string Value { get { var c = C; return c == null ? "" : c.Sub; } set { } }
            public override string Description { get { var c = C; return c == null ? "" : c.Badge; } }
            public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
            public override AccessibleObject Parent { get { return parent; } }
            public override Rectangle Bounds { get { return i < o.Items.Count ? o.RectangleToScreen(o.GetItemRectangle(i)) : Rectangle.Empty; } }
            public override AccessibleStates State
            {
                get
                {
                    var s = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (o.SelectedIndex == i) { s |= AccessibleStates.Selected; if (o.Focused) s |= AccessibleStates.Focused; }
                    return s;
                }
            }
            public override string DefaultAction { get { return L.T("בחר", "Select"); } }
            public override void DoDefaultAction() { if (i < o.Items.Count) o.SelectedIndex = i; }
            public override void Select(AccessibleSelection flags) { if (i < o.Items.Count) { o.SelectedIndex = i; o.Focus(); } }
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var c = Items[e.Index] as Card; if (c == null) return;
            bool sel = (e.State & DrawItemState.Selected) != 0 && !c.Hint, rtl = L.Rtl;
            Rectangle r = e.Bounds; Graphics g = e.Graphics;
            using (var bg = new SolidBrush(sel ? Theme.Sel : Theme.Bg)) g.FillRectangle(bg, r);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, r.Left + Ux.S(10), r.Bottom - 1, r.Right - Ux.S(10), r.Bottom - 1);
            int pad = Ux.S(12), icon = Ux.S(22), x0 = r.Left + pad, x1 = r.Right - pad;
            if (c.Hint)
            {
                TextRenderer.DrawText(g, c.Title, Ux.F, r, Theme.Sub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | (rtl ? TextFormatFlags.RightToLeft : 0));
                return;
            }
            if (c.Glyph != 0)
            {
                using (var bmp = Ux.Glyph(c.Glyph, 22, c.Tint ?? Theme.Sub)) g.DrawImage(bmp, rtl ? x1 - icon : x0, r.Top + (r.Height - icon) / 2);
                if (rtl) x1 -= icon + pad; else x0 += icon + pad;
            }
            if (c.Badge != "")
            {
                Size bs = TextRenderer.MeasureText(g, c.Badge, Ux.FSmall, Size.Empty, TextFormatFlags.NoPrefix);
                var br = new Rectangle(rtl ? x0 : x1 - bs.Width, r.Top, bs.Width, r.Height);
                TextRenderer.DrawText(g, c.Badge, Ux.FSmall, br, c.BadgeColor ?? Theme.Sub, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | (rtl ? TextFormatFlags.RightToLeft : 0));
                if (rtl) x0 += bs.Width + pad; else x1 -= bs.Width + pad;
            }
            var baseFlags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | (rtl ? TextFormatFlags.Right : TextFormatFlags.Left);
            var tf = baseFlags | (rtl && HasRtl(c.Title) ? TextFormatFlags.RightToLeft : 0);
            var sf = baseFlags | (rtl && HasRtl(c.Sub) ? TextFormatFlags.RightToLeft : 0);
            if (c.Sub == "")
                TextRenderer.DrawText(g, c.Title, Ux.F, new Rectangle(x0, r.Top, x1 - x0, r.Height), Theme.Text, tf | TextFormatFlags.VerticalCenter);
            else
            {
                TextRenderer.DrawText(g, c.Title, Ux.F, new Rectangle(x0, r.Top + Ux.S(6), x1 - x0, Ux.S(22)), Theme.Text, tf);
                TextRenderer.DrawText(g, c.Sub, Ux.FSmall, new Rectangle(x0, r.Top + Ux.S(28), x1 - x0, Ux.S(20)), Theme.Sub, sf);
            }
        }
        static bool HasRtl(string s) { foreach (char ch in s) if (ch >= 0x0590 && ch <= 0x08FF) return true; return false; }
    }

    // ====================================================================================================
    //  Dialogs
    // ====================================================================================================
    sealed class PairForm : Form
    {
        public string Code; TextBox tb; public bool Restored;
        /// <summary>restore: runs the restore flow; returns true when the tool is paired afterwards.</summary>
        public PairForm(Func<bool> restore = null)
        {
            Ux.Setup(this); Text = L.T("Cliport - צימוד מכשירים", "Cliport - Pair devices"); ClientSize = new Size(Ux.S(540), Ux.S(336)); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
            var l = new Label(); l.Text = L.T("כדי לסנכרן, כל המכשירים חולקים קוד צימוד סודי אחד.\nזה המכשיר הראשון? צור קבוצה חדשה. אחרת, הזן את הקוד שמוצג במכשיר קיים.",
                "To sync, all your devices share one secret pairing code.\nFirst device? Create a new group. Otherwise enter the code shown on an existing device.");
            l.SetBounds(Ux.S(18), Ux.S(16), Ux.S(504), Ux.S(62));
            var bNew = Ux.Btn(L.T("צור קבוצה חדשה", "Create a new group"), delegate { Code = KeyRing.NewCode(); DialogResult = DialogResult.OK; }, "primary");
            bNew.AutoSize = false; bNew.SetBounds(Ux.S(18), Ux.S(86), Ux.S(504), Ux.S(42));
            var l2 = new Label(); l2.Text = L.T("או הצטרף עם קוד קיים:", "Or join with an existing code:"); l2.SetBounds(Ux.S(18), Ux.S(146), Ux.S(504), Ux.S(24));
            tb = new TextBox(); tb.SetBounds(Ux.S(18), Ux.S(174), Ux.S(504), Ux.S(30)); tb.RightToLeft = RightToLeft.No; tb.Font = new Font("Consolas", 11f); tb.CharacterCasing = CharacterCasing.Upper;
            var bJoin = Ux.Btn(L.T("הצטרף", "Join"), delegate
            {
                if (KeyRing.FromCode(tb.Text) == null) { MessageBox.Show(this, L.T("הקוד אינו תקין. הקוד מורכב מ-26 תווים.", "The code is not valid. A code has 26 characters."), "Cliport", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                Code = B32.Encode(B32.Decode(tb.Text)); DialogResult = DialogResult.OK;
            });
            bJoin.AutoSize = false; bJoin.SetBounds(L.Rtl ? Ux.S(372) : Ux.S(18), Ux.S(220), Ux.S(150), Ux.S(38));
            var l3 = new Label(); l3.Text = L.T("יש לך גיבוי? שחזור מחזיר את הצימוד, ההגדרות והנעוצים בבת אחת.", "Have a backup? Restoring brings back pairing, settings and pins at once."); l3.Tag = "sub"; l3.Font = Ux.FSmall; l3.SetBounds(Ux.S(18), Ux.S(270), Ux.S(504), Ux.S(20));
            var bRestore = Ux.Btn(L.T("שחזר מגיבוי...", "Restore from a backup..."), delegate { if (restore != null && restore()) { Restored = true; DialogResult = DialogResult.OK; } });
            bRestore.AutoSize = false; bRestore.SetBounds(L.Rtl ? Ux.S(322) : Ux.S(18), Ux.S(292), Ux.S(200), Ux.S(34)); bRestore.Enabled = restore != null;
            Controls.AddRange(new Control[] { l, bNew, l2, tb, bJoin, l3, bRestore }); AcceptButton = bJoin;
            Theme.Apply(this);
        }
    }

    sealed class CodeForm : Form
    {
        public CodeForm(string code)
        {
            Ux.Setup(this); Text = L.T("קוד הצימוד", "Pairing code"); ClientSize = new Size(Ux.S(580), Ux.S(210)); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            var l = new Label(); l.Text = L.T("הזן את הקוד הזה ב-Cliport במכשיר הנוסף (אייפון / אייפד / מחשב).\nהקוד הוא מפתח ההצפנה - אל תשתף אותו עם אחרים.",
                "Enter this code in Cliport on the other device (iPhone / iPad / PC).\nThe code is the encryption key - do not share it with anyone.");
            l.SetBounds(Ux.S(18), Ux.S(14), Ux.S(544), Ux.S(54));
            var tb = new TextBox(); tb.ReadOnly = true; tb.Text = KeyRing.Pretty(code); tb.Font = Ux.FMono; tb.TextAlign = HorizontalAlignment.Center; tb.RightToLeft = RightToLeft.No; tb.SetBounds(Ux.S(18), Ux.S(78), Ux.S(544), Ux.S(40));
            var b = Ux.Btn(L.T("העתק קוד", "Copy code"), delegate { try { Clipboard.SetText(code); } catch { } }, "primary"); b.AutoSize = false; b.SetBounds(L.Rtl ? Ux.S(412) : Ux.S(18), Ux.S(144), Ux.S(150), Ux.S(40));
            var c = Ux.Btn(L.T("סגור", "Close"), delegate { Close(); }); c.AutoSize = false; c.SetBounds(L.Rtl ? Ux.S(292) : Ux.S(178), Ux.S(144), Ux.S(110), Ux.S(40));
            Controls.AddRange(new Control[] { l, tb, b, c });
            Theme.Apply(this);
        }
    }

    sealed class ProgressForm : Form
    {
        ProgressBar bar; Label lbl; public bool Finished;
        public ProgressForm(string title)
        {
            Ux.Setup(this); Text = "Cliport"; ClientSize = new Size(Ux.S(440), Ux.S(100)); FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false; TopMost = true;
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false;
            lbl = new Label(); lbl.Text = title; lbl.SetBounds(Ux.S(16), Ux.S(14), Ux.S(408), Ux.S(26)); lbl.AutoEllipsis = true;
            bar = new ProgressBar(); bar.SetBounds(Ux.S(16), Ux.S(52), Ux.S(408), Ux.S(22)); bar.RightToLeftLayout = L.Rtl;
            Controls.Add(lbl); Controls.Add(bar);
            Theme.Apply(this);
        }
        public void Set(string text, int pc) { lbl.Text = text; bar.Value = Math.Max(0, Math.Min(100, pc)); }
        public void Finish() { Finished = true; if (Visible) Close(); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); if (Finished) Close(); }
    }

    sealed class PasswordForm : Form
    {
        TextBox tb, tb2; public string Password { get { return tb.Text; } }
        /// <summary>confirm: ask twice (when setting a password). An empty password is allowed there and means "no password".</summary>
        public PasswordForm(string title, string text, bool confirm)
        {
            Ux.Setup(this); Text = title; ClientSize = new Size(Ux.S(480), Ux.S(confirm ? 250 : 190)); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false;
            var l = new Label(); l.Text = text; l.SetBounds(Ux.S(18), Ux.S(14), Ux.S(444), Ux.S(62));
            tb = new TextBox(); tb.UseSystemPasswordChar = true; tb.RightToLeft = RightToLeft.No; tb.SetBounds(Ux.S(18), Ux.S(84), Ux.S(444), Ux.S(28)); Ux.Cue(tb, L.T("סיסמה", "Password"));
            tb2 = new TextBox(); tb2.UseSystemPasswordChar = true; tb2.RightToLeft = RightToLeft.No; tb2.SetBounds(Ux.S(18), Ux.S(124), Ux.S(444), Ux.S(28)); tb2.Visible = confirm; Ux.Cue(tb2, L.T("הקלד שוב את הסיסמה", "Type the password again"));
            int y = confirm ? 188 : 136;
            var ok = Ux.Btn(L.T("אישור", "OK"), delegate
            {
                if (confirm && tb.Text != tb2.Text) { MessageBox.Show(this, L.T("הסיסמאות אינן זהות.", "The passwords do not match."), "Cliport", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                DialogResult = DialogResult.OK;
            }, "primary");
            ok.AutoSize = false; ok.SetBounds(L.Rtl ? Ux.S(332) : Ux.S(18), Ux.S(y), Ux.S(130), Ux.S(38));
            var cancel = Ux.Btn(L.T("ביטול", "Cancel"), delegate { DialogResult = DialogResult.Cancel; }); cancel.AutoSize = false; cancel.SetBounds(L.Rtl ? Ux.S(212) : Ux.S(158), Ux.S(y), Ux.S(110), Ux.S(38));
            Controls.AddRange(new Control[] { l, tb, tb2, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
            Theme.Apply(this);
        }
    }

    sealed class AboutForm : Form
    {
        public AboutForm(Config cfg)
        {
            Ux.Setup(this); Text = L.T("אודות Cliport", "About Cliport"); ClientSize = new Size(Ux.S(520), Ux.S(404)); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            int w = ClientSize.Width;
            var pic = new PictureBox(); pic.SizeMode = PictureBoxSizeMode.Zoom; pic.Image = Ux.Logo; pic.SetBounds((w - Ux.S(112)) / 2, Ux.S(20), Ux.S(112), Ux.S(112));
            Func<string, Font, string, int, int, Label> mk = delegate (string t, Font f, string role, int y, int h)
            {
                var l = new Label(); l.Text = t; l.Font = f; l.Tag = role; l.TextAlign = ContentAlignment.MiddleCenter; l.SetBounds(Ux.S(20), Ux.S(y), w - Ux.S(40), Ux.S(h)); return l;
            };
            var name = mk("Cliport", new Font("Segoe UI Semibold", 20f), "", 138, 42);
            var ver = mk(L.T("גרסה ", "Version ") + App.Version, Ux.F, "sub", 180, 24);
            var desc = mk(L.T("לוח משותף בזמן אמת בין מחשבים, אייפון ואייפד.\nמוצפן מקצה לקצה, עובד מכל רשת, כולל שיתוף קבצים, נעוצים ושולחן עבודה מרוחק.",
                "A real-time shared clipboard for PCs, iPhone and iPad.\nEnd-to-end encrypted, works from any network, with file sharing, pins and Remote Desktop."), Ux.F, "", 210, 72);
            var dev = mk(L.T("מכשיר: ", "Device: ") + cfg.DeviceName + "   •   " + cfg.DeviceId, Ux.FSmall, "sub", 284, 22);
            var link = new LinkLabel(); link.Text = "github.com/bennymat93/Cliport"; link.TextAlign = ContentAlignment.MiddleCenter; link.SetBounds(Ux.S(20), Ux.S(308), w - Ux.S(40), Ux.S(24));
            link.LinkClicked += delegate { try { Process.Start("https://github.com/bennymat93/Cliport"); } catch { } };
            var ok = Ux.Btn(L.T("סגור", "Close"), delegate { Close(); }, "primary"); ok.AutoSize = false; ok.SetBounds((w - Ux.S(130)) / 2, Ux.S(348), Ux.S(130), Ux.S(38));
            Controls.AddRange(new Control[] { pic, name, ver, desc, dev, link, ok }); AcceptButton = ok; CancelButton = ok;
            Theme.Apply(this);
        }
    }

    /// <summary>The user guide ships inside the program (embedded HTML with inline pictures).</summary>
    sealed class HelpForm : Form
    {
        public static string Extract()
        {
            string name = L.En ? "guide.en.html" : "guide.he.html";
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                string dir = App.PathOf("help"); Directory.CreateDirectory(dir); string p = Path.Combine(dir, name);
                using (var f = new FileStream(p, FileMode.Create, FileAccess.Write)) s.CopyTo(f);
                return p;
            }
        }
        public HelpForm(string path)
        {
            Ux.Setup(this); Text = L.T("Cliport - מדריך למשתמש", "Cliport - User guide"); ClientSize = new Size(Ux.S(1000), Ux.S(740)); StartPosition = FormStartPosition.CenterScreen;
            var wb = new WebBrowser(); wb.Dock = DockStyle.Fill; wb.IsWebBrowserContextMenuEnabled = false; wb.AllowWebBrowserDrop = false; wb.ScriptErrorsSuppressed = true;
            var bar = Ux.Row(DockStyle.Top, Ux.Btn(L.T("פתח בדפדפן", "Open in browser"), delegate { try { Process.Start(path); } catch { } }),
                Ux.Btn(L.T("הדפס", "Print"), delegate { try { wb.ShowPrintDialog(); } catch { } }));
            bar.Padding = new Padding(Ux.S(8), Ux.S(4), Ux.S(8), Ux.S(4));
            Controls.Add(wb); Controls.Add(bar);
            Theme.Apply(this);
            wb.Navigate(path);
        }
    }

    sealed class PinPopup : Form
    {
        CardList lb; public Action<string> Chosen;
        public PinPopup()
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; KeyPreview = true;
            lb = new CardList(); Controls.Add(lb);
            lb.DoubleClick += delegate { Pick(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; Pick(); } else if (e.KeyCode == Keys.Escape) Hide(); };
            Deactivate += delegate { Hide(); };
        }
        void Pick() { var c = lb.Selected; if (c == null) return; Hide(); if (Chosen != null) Chosen((string)c.Data); }
        public void ShowAt(List<string> pins)
        {
            Ux.Setup(this); Text = L.T("נעוצים - Enter להדבקה", "Pins - Enter to paste"); ClientSize = new Size(Ux.S(440), Ux.S(320)); lb.ItemHeight = Ux.S(40);
            lb.Items.Clear();
            foreach (string p in pins) { var c = new Card(); c.Title = Ux.OneLine(p); c.Glyph = Ux.GPin; c.Tint = Theme.Accent; c.Data = p; lb.Items.Add(c); }
            if (pins.Count == 0) { var c = new Card(); c.Title = L.T("אין פריטים נעוצים עדיין", "No pinned items yet"); c.Hint = true; lb.Items.Add(c); }
            Theme.Apply(this);
            lb.SelectedIndex = 0;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea; var pt = Cursor.Position;
            Location = new Point(Math.Max(wa.Left, Math.Min(pt.X - Width / 2, wa.Right - Width)), Math.Max(wa.Top, Math.Min(pt.Y - Ux.S(40), wa.Bottom - Height)));
            Show(); Activate(); lb.Focus();
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
    }

    // ====================================================================================================
    //  Main window
    // ====================================================================================================
    sealed class MainForm : Form
    {
        readonly Config cfg; readonly Engine eng; readonly ClipWindow clip; readonly NotifyIcon tray; readonly PinPopup popup = new PinPopup();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer(), debounce = new System.Windows.Forms.Timer(), fast = new System.Windows.Forms.Timer();
        // rebuilt by BuildUi
        MenuStrip menu; ToolStrip tools; StatusStrip status; ToolStripStatusLabel stDot, stText, stInfo;
        Panel side, content, offerBar; Panel[] pages; Button[] nav; CardList lbClips, lbDev, lbRdp; TextBox search, tbToken, tbChat, tbRelay; Label lblOffer, lblRdp, lblTg;
        ToolStripButton tbPause, tbGet; ToolStripMenuItem miPause, miGet;
        CardList lbBackups; TextBox tbBkName, tbBkDesc; CheckBox cbBkHist;
        int page; string filter = "";
        Offer offer; string[] offerPaths; bool fetching, exiting, pairing; IntPtr prevFg; int tickN; long declinedAt;

        public MainForm(Config cfg, bool demo)
        {
            this.cfg = cfg;
            Ux.Setup(this); Text = "Cliport"; ClientSize = new Size(Ux.S(960), Ux.S(660)); MinimumSize = new Size(Ux.S(780), Ux.S(540)); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
            var h = Handle;   // force handle creation so BeginInvoke works while hidden
            var ctx = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            eng = new Engine(cfg, delegate (Action a) { ctx.Post(delegate { try { a(); } catch (Exception e) { Log.Write("ui post: " + e); } }, null); });
            eng.RdpInfo = Rdp.Info;
            if (demo) { cfg.Set("name", "MY-PC"); offer = eng.LoadDemo(); Theme.NoHover = true; }
            page = Math.Max(0, Math.Min(4, Program.StartPage));
            clip = new ClipWindow();
            tray = new NotifyIcon(); tray.Icon = Ux.AppIcon ?? SystemIcons.Application; tray.Text = "Cliport"; tray.Visible = !demo;
            tray.DoubleClick += delegate { ShowMain(); };
            tray.BalloonTipClicked += delegate { if (offer != null && offerPaths == null && !offer.Image) FetchOfferNow(); };
            tray.ContextMenuStrip = new ContextMenuStrip(); tray.ContextMenuStrip.Opening += delegate { BuildTrayMenu(); };
            BuildUi();

            eng.TextIn = delegate (string text, string from) { if (!clip.SetText(text)) Log.Write("clipboard busy - could not apply remote text"); SetOffer(null); };
            eng.OfferIn = OnOffer;
            eng.PinsChanged = RefreshClips; eng.HistoryChanged = RefreshClips; eng.DevicesChanged = delegate { RefreshDevices(); }; eng.StatusChanged = RefreshStatus;
            eng.Notify = Toast;
            clip.Changed = delegate { debounce.Stop(); debounce.Start(); };
            clip.HotKey = OnHotKey;
            popup.Chosen = PastePin;
            debounce.Interval = 120; debounce.Tick += delegate { debounce.Stop(); ReadLocalClipboard(0); };
            tick.Interval = 5000; tick.Tick += delegate { OnTick(); }; tick.Start();
            fast.Interval = 1000; fast.Tick += delegate { eng.FastTick(); }; fast.Start();
            SystemEvents.UserPreferenceChanged += delegate { if (Theme.Mode == "system" && Theme.Dark != Theme.SystemDark()) try { BeginInvoke((Action)Rebuild); } catch { } };

            if (!Recovery.SafeMode && !demo)
            {
                if (!Native.RegisterHotKey(clip.Handle, 1, 0x0001 | 0x0002, 0x56)) Log.Write("hotkey Ctrl+Alt+V unavailable");
                if (!Native.RegisterHotKey(clip.Handle, 2, 0x0001 | 0x0002, 0x52)) Log.Write("hotkey Ctrl+Alt+R unavailable");
            }
            CleanOldFiles();
            if (!demo) eng.Start();
            RefreshAll();
            if (Recovery.Notice != null) Toast(L.T("Cliport - התאוששות (דרגה ", "Cliport - recovery (level ") + Recovery.Level + ")", Recovery.Notice);
            if (demo && Program.OpenWhat != null)
            {
                var t = new System.Windows.Forms.Timer(); t.Interval = 900;
                t.Tick += delegate
                {
                    t.Stop(); t.Dispose();
                    switch (Program.OpenWhat)
                    {
                        case "about": ShowAbout(); break;
                        case "code": ShowCode(); break;
                        case "pair": using (var f = new PairForm(delegate { return false; })) f.ShowDialog(this); break;
                        case "confirm": Confirm(offer); break;
                        case "pins": popup.ShowAt(eng.LivePins()); break;
                        case "help": ShowHelp(); break;
                    }
                };
                t.Start();
            }
        }

        // ---------------------------------------------------------------- construction
        void Rebuild() { Theme.Resolve(); BuildUi(); RefreshAll(); }
        void RefreshAll() { RefreshClips(); RefreshDevices(); RefreshStatus(); SetOffer(offer, true); RefreshBackups(); }

        void BuildUi()
        {
            SuspendLayout();
            foreach (Control c in new ArrayList(Controls)) c.Dispose();
            Controls.Clear();
            RightToLeft = L.Rtl ? RightToLeft.Yes : RightToLeft.No;
            Color ic = Theme.Text;

            // ---- menu
            menu = new MenuStrip(); menu.Padding = new Padding(Ux.S(6), Ux.S(2), Ux.S(6), Ux.S(2));
            var mFile = new ToolStripMenuItem(L.T("&קובץ", "&File"));
            Item(mFile, L.T("שלח את הלוח עכשיו", "Send clipboard now"), Ux.GSend, Keys.Control | Keys.Shift | Keys.V, delegate { SendNow(); });
            miGet = Item(mFile, L.T("הורד את הקובץ הממתין", "Download the pending file"), Ux.GDownload, Keys.Control | Keys.D, delegate { FetchOfferNow(); });
            Item(mFile, L.T("פתח את תיקיית הקבצים שהתקבלו", "Open the received files folder"), Ux.GFolder, Keys.None, delegate { OpenFolder("incoming"); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            Item(mFile, L.T("צור גיבוי עכשיו", "Create a backup now"), Ux.GBackup, Keys.Control | Keys.B, delegate { ShowPage(4); CreateBackup(); });
            Item(mFile, L.T("ייבא גיבוי מקובץ...", "Import a backup file..."), Ux.GImport, Keys.Control | Keys.O, delegate { ShowPage(4); ImportBackup(); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            miPause = Item(mFile, L.T("השהה סנכרון", "Pause sync"), Ux.GPause, Keys.Control | Keys.Shift | Keys.P, delegate { TogglePause(); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            Item(mFile, L.T("סגור למגש המערכת", "Close to tray"), 0, Keys.Control | Keys.W, delegate { Hide(); });
            Item(mFile, L.T("יציאה", "Exit"), Ux.GExit, Keys.Control | Keys.Q, delegate { ExitApp(); });
            var mEdit = new ToolStripMenuItem(L.T("&עריכה", "&Edit"));
            Item(mEdit, L.T("העתק פריט", "Copy item"), Ux.GCopy, Keys.None, delegate { CopySelected(); }).ShortcutKeyDisplayString = "Enter";
            Item(mEdit, L.T("נעץ / בטל נעיצה", "Pin / Unpin"), Ux.GPin, Keys.Control | Keys.P, delegate { TogglePin(); });
            Item(mEdit, L.T("מחק מההיסטוריה", "Delete from history"), Ux.GDelete, Keys.None, delegate { DeleteSelected(); }).ShortcutKeyDisplayString = "Del";
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            Item(mEdit, L.T("חיפוש", "Search"), Ux.GSearch, Keys.Control | Keys.F, delegate { ShowPage(0); search.Focus(); search.SelectAll(); });
            Item(mEdit, L.T("נקה את כל ההיסטוריה", "Clear all history"), Ux.GClear, Keys.None, delegate
            {
                if (MessageBox.Show(this, L.T("למחוק את כל ההיסטוריה במחשב זה? (הנעוצים נשארים)", "Delete all history on this PC? (Pins are kept)"), "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) eng.ClearHistory();
            });
            var mView = new ToolStripMenuItem(L.T("&תצוגה", "&View"));
            Item(mView, L.T("לוח ונעוצים", "Clipboard & pins"), Ux.GClip, Keys.Control | Keys.D1, delegate { ShowPage(0); });
            Item(mView, L.T("מכשירים", "Devices"), Ux.GDevices, Keys.Control | Keys.D2, delegate { ShowPage(1); });
            Item(mView, L.T("שולחן עבודה מרוחק", "Remote Desktop"), Ux.GRemote, Keys.Control | Keys.D3, delegate { ShowPage(2); });
            Item(mView, L.T("הגדרות", "Settings"), Ux.GSettings, Keys.Control | Keys.D4, delegate { ShowPage(3); });
            Item(mView, L.T("גיבוי ושחזור", "Backup & restore"), Ux.GBackup, Keys.Control | Keys.D5, delegate { ShowPage(4); });
            mView.DropDownItems.Add(new ToolStripSeparator());
            ThemeItems(Item(mView, L.T("ערכת נושא", "Theme"), Ux.GTheme, Keys.None, null).DropDownItems);
            LangItems(Item(mView, L.T("שפה", "Language"), Ux.GGlobe, Keys.None, null).DropDownItems);
            var mTools = new ToolStripMenuItem(L.T("&כלים", "&Tools"));
            Item(mTools, L.T("הצג קוד צימוד", "Show pairing code"), Ux.GKey, Keys.Control | Keys.K, delegate { ShowCode(); });
            var mRdp = Item(mTools, L.T("התחבר למחשב", "Connect to a PC"), Ux.GRemote, Keys.None, null);
            mRdp.DropDownItems.Add("..."); mRdp.DropDownOpening += delegate { mRdp.DropDownItems.Clear(); AddRdpItems(mRdp.DropDownItems); };
            Item(mTools, L.T("הפעל Remote Desktop במחשב זה", "Enable Remote Desktop on this PC"), 0, Keys.None, delegate { EnableRdp(); });
            mTools.DropDownItems.Add(new ToolStripSeparator());
            Item(mTools, L.T("פתח את תיקיית הנתונים והיומן", "Open data folder and log"), Ux.GFolder, Keys.None, delegate { OpenFolder(""); });
            Item(mTools, L.T("אסוף יומני אבחון מכל המכשירים", "Collect diagnostic logs from all devices"), 0, Keys.None, delegate { CollectDiag(); });
            var mHelp = new ToolStripMenuItem(L.T("ע&זרה", "&Help"));
            Item(mHelp, L.T("מדריך למשתמש", "User guide"), Ux.GHelp, Keys.F1, delegate { ShowHelp(); });
            Item(mHelp, L.T("קיצורי מקשים", "Keyboard shortcuts"), 0, Keys.None, delegate { ShowShortcuts(); });
            Item(mHelp, L.T("דף הפרויקט ב-GitHub", "Project page on GitHub"), Ux.GLink, Keys.None, delegate { try { Process.Start("https://github.com/bennymat93/Cliport"); } catch { } });
            mHelp.DropDownItems.Add(new ToolStripSeparator());
            Item(mHelp, L.T("אודות Cliport", "About Cliport"), Ux.GInfo, Keys.None, delegate { ShowAbout(); });
            menu.Items.AddRange(new ToolStripItem[] { mFile, mEdit, mView, mTools, mHelp });

            // ---- toolbar
            tools = new ToolStrip(); tools.GripStyle = ToolStripGripStyle.Hidden; tools.ImageScalingSize = new Size(Ux.S(22), Ux.S(22)); tools.Padding = new Padding(Ux.S(8), Ux.S(4), Ux.S(8), Ux.S(4));
            Tool(L.T("שלח לוח", "Send"), L.T("שלח את תוכן הלוח לכל המכשירים עכשיו (Ctrl+Shift+V)", "Send the clipboard to all devices now (Ctrl+Shift+V)"), Ux.GSend, true, delegate { SendNow(); });
            tools.Items.Add(new ToolStripSeparator());
            Tool(L.T("העתק", "Copy"), L.T("העתק את הפריט הנבחר לכל המכשירים (Enter)", "Copy the selected item to all devices (Enter)"), Ux.GCopy, true, delegate { CopySelected(); });
            Tool(L.T("נעץ", "Pin"), L.T("נעץ / בטל נעיצה - נעוצים מופיעים בכל המכשירים (Ctrl+P)", "Pin / unpin - pins appear on every device (Ctrl+P)"), Ux.GPin, true, delegate { TogglePin(); });
            Tool(L.T("מחק", "Delete"), L.T("מחק מההיסטוריה (Del)", "Delete from history (Del)"), Ux.GDelete, true, delegate { DeleteSelected(); });
            tools.Items.Add(new ToolStripSeparator());
            tbGet = Tool(L.T("הורד קובץ", "Get file"), L.T("הורד את הקובץ הממתין (Ctrl+D)", "Download the pending file (Ctrl+D)"), Ux.GDownload, true, delegate { FetchOfferNow(); });
            tbPause = Tool(L.T("השהה", "Pause"), L.T("השהה / חדש את הסנכרון", "Pause / resume sync"), Ux.GPause, true, delegate { TogglePause(); });
            tools.Items.Add(new ToolStripSeparator());
            var tRdp = new ToolStripDropDownButton(L.T("התחבר", "Connect"), Ux.Glyph(Ux.GRemote, 22, ic)); tRdp.ToolTipText = L.T("חיבור שולחן עבודה מרוחק למחשב (Ctrl+Alt+R)", "Remote Desktop to a PC (Ctrl+Alt+R)");
            tRdp.TextImageRelation = TextImageRelation.ImageAboveText; tRdp.DropDownItems.Add("..."); tRdp.DropDownOpening += delegate { tRdp.DropDownItems.Clear(); AddRdpItems(tRdp.DropDownItems); }; tools.Items.Add(tRdp);
            Tool(L.T("קוד צימוד", "Pair"), L.T("הצג את קוד הצימוד כדי להוסיף מכשיר (Ctrl+K)", "Show the pairing code to add a device (Ctrl+K)"), Ux.GKey, true, delegate { ShowCode(); });
            // far side: about, help, language, theme
            Tool("", L.T("אודות Cliport", "About Cliport"), Ux.GInfo, false, delegate { ShowAbout(); }).Alignment = ToolStripItemAlignment.Right;
            Tool("", L.T("מדריך למשתמש (F1)", "User guide (F1)"), Ux.GHelp, false, delegate { ShowHelp(); }).Alignment = ToolStripItemAlignment.Right;
            var tLang = new ToolStripDropDownButton("", Ux.Glyph(Ux.GGlobe, 22, ic)); tLang.ToolTipText = L.T("שפה", "Language"); tLang.Alignment = ToolStripItemAlignment.Right; LangItems(tLang.DropDownItems); tools.Items.Add(tLang);
            var tTheme = new ToolStripDropDownButton("", Ux.Glyph(Ux.GTheme, 22, ic)); tTheme.ToolTipText = L.T("ערכת נושא: בהיר / מכשיר / כהה", "Theme: light / device / dark"); tTheme.Alignment = ToolStripItemAlignment.Right; ThemeItems(tTheme.DropDownItems); tools.Items.Add(tTheme);

            // ---- status bar
            status = new StatusStrip(); status.SizingGrip = false; status.Padding = new Padding(Ux.S(8), 0, Ux.S(8), 0);
            stDot = new ToolStripStatusLabel("●"); stText = new ToolStripStatusLabel("");
            stInfo = new ToolStripStatusLabel(""); stInfo.Spring = true; stInfo.TextAlign = ContentAlignment.MiddleRight; stInfo.Tag = "sub";     // mirrored automatically in RTL
            status.Items.AddRange(new ToolStripItem[] { stDot, stText, stInfo });

            // ---- side navigation
            side = new Panel(); side.Dock = Ux.Lead; side.Width = Ux.S(208); side.Tag = "side"; side.Padding = new Padding(Ux.S(8), Ux.S(10), Ux.S(8), Ux.S(8));
            string[] names = { L.T("לוח ונעוצים", "Clipboard & pins"), L.T("מכשירים", "Devices"), L.T("שולחן עבודה מרוחק", "Remote Desktop"), L.T("הגדרות", "Settings"), L.T("גיבוי ושחזור", "Backup & restore") };
            int[] glyphs = { Ux.GClip, Ux.GDevices, Ux.GRemote, Ux.GSettings, Ux.GBackup };
            nav = new Button[5];
            for (int i = 4; i >= 0; i--)
            {
                int idx = i; var b = new Button(); b.UseMnemonic = false; b.Text = "  " + names[i]; b.Dock = DockStyle.Top; b.Height = Ux.S(46); b.Tag = "nav"; b.Cursor = Cursors.Hand;
                b.Image = Ux.Glyph(glyphs[i], 20, ic); b.TextImageRelation = TextImageRelation.ImageBeforeText; b.ImageAlign = ContentAlignment.MiddleLeft; b.TextAlign = ContentAlignment.MiddleLeft;
                b.Padding = new Padding(Ux.S(10), 0, Ux.S(10), 0); b.Click += delegate { ShowPage(idx); };
                nav[i] = b; side.Controls.Add(b);
            }
            var brand = new Label(); brand.Dock = DockStyle.Bottom; brand.Height = Ux.S(26); brand.Text = "Cliport v" + App.Version; brand.Tag = "sub"; brand.Font = Ux.FSmall; brand.TextAlign = ContentAlignment.MiddleCenter; side.Controls.Add(brand);

            // ---- pages
            content = new Panel(); content.Dock = DockStyle.Fill;
            pages = new[] { BuildClips(), BuildDevices(), BuildRdp(), BuildSettings(), BuildBackup() };
            foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; content.Controls.Add(p); }
            var line = new Panel(); line.Dock = Ux.Lead; line.Width = 1; line.Tag = "line";

            Controls.Add(content); Controls.Add(line); Controls.Add(side); Controls.Add(status); Controls.Add(tools); Controls.Add(menu);
            MainMenuStrip = menu;
            Theme.Apply(this);
            ShowPage(page);
            ResumeLayout(true);
        }
        ToolStripMenuItem Item(ToolStripMenuItem parent, string text, int glyph, Keys keys, EventHandler click)
        {
            var it = new ToolStripMenuItem(text); if (glyph != 0) it.Image = Ux.Glyph(glyph, 16, Theme.Text);
            if (keys != Keys.None) it.ShortcutKeys = keys; if (click != null) it.Click += click;
            parent.DropDownItems.Add(it); return it;
        }
        ToolStripButton Tool(string text, string tip, int glyph, bool withText, EventHandler click)
        {
            var b = new ToolStripButton(text, Ux.Glyph(glyph, 22, Theme.Text)); b.ToolTipText = tip; b.DisplayStyle = withText ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image;
            b.TextImageRelation = TextImageRelation.ImageAboveText; b.Padding = new Padding(Ux.S(4), 0, Ux.S(4), 0); b.Click += click; tools.Items.Add(b); return b;
        }
        void ThemeItems(ToolStripItemCollection items)
        {
            string[] ids = { "light", "system", "dark" }; string[] names = { L.T("בהיר", "Light"), L.T("מכשיר (לפי Windows)", "Device (follow Windows)"), L.T("כהה", "Dark") };
            for (int i = 0; i < 3; i++)
            {
                string id = ids[i]; var it = new ToolStripMenuItem(names[i]); it.Checked = Theme.Mode == id;
                it.Click += delegate { SetTheme(id); }; items.Add(it);
            }
        }
        void LangItems(ToolStripItemCollection items)
        {
            var he = new ToolStripMenuItem("עברית"); he.Checked = !L.En; he.Click += delegate { SetLang(false); }; items.Add(he);
            var en = new ToolStripMenuItem("English"); en.Checked = L.En; en.Click += delegate { SetLang(true); }; items.Add(en);
        }
        void SetTheme(string id) { if (Theme.Mode == id) return; Theme.Mode = id; cfg.Set("theme", id); cfg.Save(); BeginInvoke((Action)Rebuild); }
        void SetLang(bool en) { if (L.En == en) return; L.En = en; cfg.Set("lang", en ? "en" : "he"); cfg.Save(); BeginInvoke((Action)Rebuild); }

        Panel Page(string title, string subtitle)
        {
            var p = new Panel(); p.Padding = new Padding(Ux.S(18), Ux.S(12), Ux.S(18), Ux.S(12));
            var s = new Label(); s.Text = subtitle; s.Dock = DockStyle.Top; s.Height = Ux.S(28); s.Tag = "sub"; s.Font = Ux.FSmall; s.AutoEllipsis = true;
            var t = new Label(); t.UseMnemonic = false; t.Text = title; t.Dock = DockStyle.Top; t.Height = Ux.S(36); t.Font = Ux.FHead;
            p.Controls.Add(s); p.Controls.Add(t);      // title is added last so it docks first (top)
            return p;
        }
        static void AddTop(Panel page, Control c) { c.Dock = DockStyle.Top; page.Controls.Add(c); c.BringToFront(); }     // docks below what is already there
        static void AddFill(Panel page, Control c) { c.Dock = DockStyle.Fill; page.Controls.Add(c); c.BringToFront(); }

        Panel BuildClips()
        {
            var p = Page(L.T("לוח ונעוצים", "Clipboard & pins"), L.T("כל מה שמועתק מסתנכרן אוטומטית. נעוצים זמינים תמיד בכל המכשירים.", "Everything you copy syncs automatically. Pins stay available on every device."));
            search = new TextBox(); search.Text = filter; Ux.Cue(search, L.T("חיפוש בהיסטוריה ובנעוצים (Ctrl+F)", "Search history and pins (Ctrl+F)"));
            search.TextChanged += delegate { filter = search.Text; RefreshClips(); };
            search.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Down && lbClips.Items.Count > 0) { lbClips.Focus(); lbClips.SelectedIndex = 0; e.Handled = true; } };
            AddTop(p, search);
            var gap = new Panel(); gap.Height = Ux.S(8); AddTop(p, gap);

            offerBar = new Panel(); offerBar.Height = Ux.S(60); offerBar.Tag = "card"; offerBar.Padding = new Padding(Ux.S(12), Ux.S(8), Ux.S(12), Ux.S(8)); offerBar.Visible = false;
            lblOffer = new Label(); lblOffer.Dock = DockStyle.Fill; lblOffer.TextAlign = ContentAlignment.MiddleLeft; lblOffer.AutoEllipsis = true;
            var bGet = Ux.Btn(L.T("הורד עכשיו", "Download now"), delegate { FetchOfferNow(); }, "primary"); bGet.Dock = Ux.Trail; bGet.AutoSize = false; bGet.Width = Ux.S(130); bGet.Margin = Padding.Empty;
            var bNo = Ux.Btn(L.T("התעלם", "Dismiss"), delegate { SetOffer(null); }); bNo.Dock = Ux.Trail; bNo.AutoSize = false; bNo.Width = Ux.S(96); bNo.Margin = Padding.Empty;
            var pic = new PictureBox(); pic.Dock = Ux.Lead; pic.Width = Ux.S(42); pic.SizeMode = PictureBoxSizeMode.CenterImage; pic.Image = Ux.Glyph(Ux.GFile, 26, Theme.Accent);
            offerBar.Controls.Add(lblOffer); offerBar.Controls.Add(bNo); offerBar.Controls.Add(bGet); offerBar.Controls.Add(pic);
            AddTop(p, offerBar);

            var hint = new Label(); hint.Dock = DockStyle.Bottom; hint.Height = Ux.S(28); hint.Tag = "sub"; hint.Font = Ux.FSmall; hint.TextAlign = ContentAlignment.MiddleLeft; hint.AutoEllipsis = true;
            hint.Text = L.T("Enter - העתק  •  Ctrl+P - נעץ  •  Del - מחק  •  Ctrl+Alt+V בכל מקום - הדבקה מהירה מהנעוצים", "Enter - copy  •  Ctrl+P - pin  •  Del - delete  •  Ctrl+Alt+V anywhere - quick paste from pins");
            p.Controls.Add(hint);

            lbClips = new CardList();
            lbClips.DoubleClick += delegate { CopySelected(); };
            lbClips.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { CopySelected(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            };
            var cm = new ContextMenuStrip();
            cm.Items.Add(L.T("העתק", "Copy"), Ux.Glyph(Ux.GCopy, 16, Theme.Text), delegate { CopySelected(); });
            cm.Items.Add(L.T("נעץ / בטל נעיצה", "Pin / Unpin"), Ux.Glyph(Ux.GPin, 16, Theme.Text), delegate { TogglePin(); });
            cm.Items.Add(L.T("מחק", "Delete"), Ux.Glyph(Ux.GDelete, 16, Theme.Text), delegate { DeleteSelected(); });
            lbClips.ContextMenuStrip = cm;
            lbClips.MouseDown += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) { int i = lbClips.IndexFromPoint(e.Location); if (i >= 0) lbClips.SelectedIndex = i; } };
            AddFill(p, lbClips);
            return p;
        }
        Panel BuildDevices()
        {
            var p = Page(L.T("מכשירים", "Devices"), L.T("כל המכשירים שחולקים את קוד הצימוד. להוספת מכשיר, הזן בו את הקוד.", "All devices that share the pairing code. To add a device, enter the code on it."));
            p.Controls.Add(Ux.Row(DockStyle.Bottom,
                Ux.Btn(L.T("הצג קוד צימוד", "Show pairing code"), delegate { ShowCode(); }, "primary"),
                Ux.Btn(L.T("שכח מכשיר", "Forget device"), delegate { var c = lbDev.Selected; if (c != null && c.Data != null) eng.ForgetDevice((string)c.Data); }),
                Ux.Btn(L.T("התנתק מהקבוצה", "Leave the group"), delegate
                {
                    if (MessageBox.Show(this, L.T("לנתק את המחשב הזה מקבוצת הסנכרון?", "Disconnect this PC from the sync group?"), "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    eng.Unpair(); RefreshAll(); EnsurePaired();
                }, "danger")));
            lbDev = new CardList(); AddFill(p, lbDev);
            return p;
        }
        Panel BuildRdp()
        {
            var p = Page(L.T("שולחן עבודה מרוחק", "Remote Desktop"), L.T("התחברות בלחיצה אחת לכל מחשב בקבוצה, באיכות גבוהה ועם התחברות מחדש אוטומטית.", "One-click connection to any PC in the group, high quality with automatic reconnect."));
            lblRdp = new Label(); lblRdp.Height = Ux.S(66); lblRdp.Tag = "sub"; AddTop(p, lblRdp);
            p.Controls.Add(Ux.Row(DockStyle.Bottom,
                Ux.Btn(L.T("התחבר", "Connect"), delegate { ConnectSelected(); }, "primary"),
                Ux.Btn(L.T("הפעל Remote Desktop במחשב זה", "Enable Remote Desktop on this PC"), delegate { EnableRdp(); }),
                Ux.Lbl(L.T("Ctrl+Alt+R בכל מקום - חיבור מהיר", "Ctrl+Alt+R anywhere - quick connect"), "sub")));
            lbRdp = new CardList(); lbRdp.DoubleClick += delegate { ConnectSelected(); };
            lbRdp.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { ConnectSelected(); e.Handled = true; e.SuppressKeyPress = true; } };
            AddFill(p, lbRdp);
            return p;
        }
        Panel BuildSettings()
        {
            var p = Page(L.T("הגדרות", "Settings"), L.T("העדפות למחשב זה. הגדרות Telegram והממסר משותפות לכל המכשירים.", "Preferences for this PC. Telegram and relay settings are shared with all devices."));
            var fl = new FlowLayoutPanel(); fl.FlowDirection = FlowDirection.TopDown; fl.WrapContents = false; fl.AutoScroll = true;
            var cbLang = new ComboBox(); cbLang.DropDownStyle = ComboBoxStyle.DropDownList; cbLang.Width = Ux.S(260); cbLang.Items.AddRange(new object[] { "עברית", "English" }); cbLang.SelectedIndex = L.En ? 1 : 0;
            cbLang.SelectedIndexChanged += delegate { SetLang(cbLang.SelectedIndex == 1); };
            var cbTheme = new ComboBox(); cbTheme.DropDownStyle = ComboBoxStyle.DropDownList; cbTheme.Width = Ux.S(260);
            cbTheme.Items.AddRange(new object[] { L.T("בהיר", "Light"), L.T("מכשיר (לפי Windows)", "Device (follow Windows)"), L.T("כהה", "Dark") });
            cbTheme.SelectedIndex = Theme.Mode == "light" ? 0 : Theme.Mode == "dark" ? 2 : 1;
            cbTheme.SelectedIndexChanged += delegate { SetTheme(new[] { "light", "system", "dark" }[cbTheme.SelectedIndex]); };
            var cbAuto = new CheckBox(); cbAuto.Text = L.T("הפעל את Cliport עם עליית Windows (מומלץ)", "Start Cliport with Windows (recommended)"); cbAuto.AutoSize = true; cbAuto.Checked = Autostart.Enabled; cbAuto.Margin = new Padding(Ux.S(4), Ux.S(10), Ux.S(4), Ux.S(2));
            cbAuto.CheckedChanged += delegate { Autostart.Enabled = cbAuto.Checked; };
            var cbAccept = new CheckBox(); cbAccept.Text = L.T("אשר הדבקת קבצים אוטומטית, ללא חלון אישור", "Accept pasted files automatically, without the confirmation"); cbAccept.AutoSize = true; cbAccept.Checked = cfg.B("autoAccept", false);
            cbAccept.CheckedChanged += delegate { cfg.Set("autoAccept", cbAccept.Checked); cfg.Save(); };
            var cbBig = new ComboBox(); cbBig.DropDownStyle = ComboBoxStyle.DropDownList; cbBig.Width = Ux.S(420);
            cbBig.Items.AddRange(new object[] { L.T("טקסט וגם קובץ - לפי המקום שבו מדביקים", "Text and file - depends on where you paste"), L.T("טקסט בלבד - הדבקה רגילה", "Text only - regular paste"), L.T("קובץ טקסט בלבד", "Text file only") });
            cbBig.SelectedIndex = cfg.S("bigText") == "text" ? 1 : cfg.S("bigText") == "file" ? 2 : 0;
            cbBig.SelectedIndexChanged += delegate { cfg.Set("bigText", new[] { "both", "text", "file" }[cbBig.SelectedIndex]); cfg.Save(); };
            tbToken = new TextBox(); tbToken.Width = Ux.S(520); tbToken.RightToLeft = RightToLeft.No; tbToken.UseSystemPasswordChar = true; tbToken.Text = eng.TgToken;
            tbChat = new TextBox(); tbChat.Width = Ux.S(260); tbChat.RightToLeft = RightToLeft.No; tbChat.Text = eng.TgChat;
            lblTg = Ux.Lbl("", "sub");
            tbRelay = new TextBox(); tbRelay.Width = Ux.S(520); tbRelay.RightToLeft = RightToLeft.No; tbRelay.Text = cfg.S("relays");
            fl.Controls.AddRange(new Control[] {
                Ux.Lbl(L.T("כללי", "General"), "head"),
                Ux.Lbl(L.T("שפה", "Language")), cbLang, Ux.Lbl(L.T("ערכת נושא", "Theme")), cbTheme, cbAuto, cbAccept,
                Ux.Lbl(L.T("טקסט גדול מאוד (מעל 180KB) מגיע כ:", "Very large text (over 180 KB) arrives as:")), cbBig,
                Ux.Lbl(L.T("אחסון Telegram לקבצים גדולים (אופציונלי)", "Telegram storage for large files (optional)"), "head"),
                Ux.Lbl(L.T("ללא Telegram, קבצים עד 25MB עוברים ישירות. עם Telegram אין מגבלת גודל, והקבצים נשארים מוצפנים מקצה לקצה.",
                    "Without Telegram, files up to 25 MB travel directly. With Telegram there is no size limit, and files stay end-to-end encrypted."), "sub"),
                Ux.Lbl(L.T("שלב 1: צור בוט חדש בטלגרם אצל BotFather והדבק כאן את ה-Token שקיבלת.", "Step 1: create a new bot in Telegram with BotFather and paste its token here."), "sub"),
                Ux.Lbl(L.T("שלב 2: שלח לבוט החדש הודעה כלשהי (למשל start).", "Step 2: send any message to the new bot (for example start)."), "sub"),
                Ux.Lbl(L.T("שלב 3: לחץ על \"זהה צ'אט ושמור\".", "Step 3: click \"Detect chat and save\"."), "sub"),
                Ux.Lbl("Bot Token"), tbToken, Ux.Lbl("Chat ID"), tbChat,
                Ux.Btn(L.T("זהה צ'אט ושמור", "Detect chat and save"), delegate { SaveTelegram(); }), lblTg,
                Ux.Lbl(L.T("שרת ממסר פרטי (אופציונלי)", "Private relay server (optional)"), "head"),
                Ux.Lbl(L.T("MQTT על TLS בפורמט ‎host:port‎ או ‎user:pass@host:port‎. נוסף לשרתים הציבוריים ומופץ לכל המכשירים.", "MQTT over TLS as host:port or user:pass@host:port. Added to the public relays and shared with all devices."), "sub"),
                tbRelay,
                Ux.Btn(L.T("שמור ממסר", "Save relay"), delegate { eng.SetRelays(tbRelay.Text); Toast("Cliport", L.T("רשימת שרתי הממסר עודכנה.", "The relay list was updated.")); }),
                Ux.Lbl(L.T("נתונים", "Data"), "head"),
                Ux.Lbl(L.T("מזהה מכשיר: ", "Device ID: ") + cfg.DeviceId + "\n" + L.T("תיקיית נתונים: ", "Data folder: ") + App.DataDir, "sub"),
                Ux.Btn(L.T("פתח את תיקיית הנתונים והיומן", "Open data folder and log"), delegate { OpenFolder(""); }),
                Ux.Lbl(" ")
            });
            AddFill(p, fl);
            return p;
        }
        // ---------------------------------------------------------------- backup and restore
        Panel BuildBackup()
        {
            var p = Page(L.T("גיבוי ושחזור", "Backup & restore"), L.T("גיבוי כולל את הצימוד, ההגדרות המשותפות, הנעוצים, רשימת המכשירים וההעדפות. שחזור מחזיר הכול בלי פעולה נוספת.", "A backup holds the pairing, shared settings, pins, device list and preferences. Restoring brings everything back with no further steps."));
            var form = new Panel(); form.Height = Ux.S(128); form.Tag = "card"; form.Padding = new Padding(Ux.S(12), Ux.S(10), Ux.S(12), Ux.S(8));
            tbBkName = new TextBox(); tbBkName.Dock = DockStyle.Top; tbBkName.MaxLength = 80; Ux.Cue(tbBkName, L.T("שם הגיבוי (למשל: לפני החלפת מחשב)", "Backup name (for example: before replacing the PC)"));
            var g1 = new Panel(); g1.Dock = DockStyle.Top; g1.Height = Ux.S(6); g1.Tag = "card";
            tbBkDesc = new TextBox(); tbBkDesc.Dock = DockStyle.Top; tbBkDesc.MaxLength = 300; Ux.Cue(tbBkDesc, L.T("תיאור (אופציונלי)", "Description (optional)"));
            cbBkHist = new CheckBox(); cbBkHist.Text = L.T("כלול גם את היסטוריית הלוח", "Include the clipboard history too"); cbBkHist.AutoSize = true; cbBkHist.Margin = new Padding(Ux.S(8), Ux.S(8), Ux.S(8), 0);
            var row = Ux.Row(DockStyle.Top, Ux.Btn(L.T("צור גיבוי", "Create backup"), delegate { CreateBackup(); }, "primary"), cbBkHist); row.Tag = "card";
            form.Controls.Add(row); form.Controls.Add(tbBkDesc); form.Controls.Add(g1); form.Controls.Add(tbBkName);
            AddTop(p, form);
            var gap = new Panel(); gap.Height = Ux.S(8); AddTop(p, gap);
            p.Controls.Add(Ux.Row(DockStyle.Bottom,
                Ux.Btn(L.T("שחזר", "Restore"), delegate { RestoreSelected(); }, "primary"),
                Ux.Btn(L.T("הורד קובץ גיבוי...", "Download backup file..."), delegate { ExportSelected(); }),
                Ux.Btn(L.T("ייבא מקובץ...", "Import from file..."), delegate { ImportBackup(); }),
                Ux.Btn(L.T("מחק", "Delete"), delegate { DeleteBackup(); }, "danger")));
            lbBackups = new CardList(); lbBackups.ItemHeight = Ux.S(58);
            lbBackups.DoubleClick += delegate { RestoreSelected(); };
            lbBackups.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) { DeleteBackup(); e.Handled = true; } };
            AddFill(p, lbBackups);
            return p;
        }
        void RefreshBackups()
        {
            if (lbBackups == null) return;
            lbBackups.BeginUpdate(); lbBackups.Items.Clear();
            foreach (var b in eng.LocalBackups())
            {
                var c = new Card(); c.Glyph = Ux.GBackup; c.Tint = Theme.Accent; c.Data = b; c.Title = b.Name;
                string when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(b.Created).ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                c.Sub = (b.Desc != "" ? b.Desc + "   •   " : "") + b.Device;
                c.Badge = when + "   •   " + b.PinCount + L.T(" נעוצים", " pins") + "   •   " + (b.DeviceCount + 1) + L.T(" מכשירים", " devices") + (b.HasHistory ? L.T("   •   היסטוריה", "   •   history") : "");
                lbBackups.Items.Add(c);
            }
            if (lbBackups.Items.Count == 0) { var c = new Card(); c.Hint = true; c.Title = L.T("אין גיבויים מקומיים עדיין - תן שם ולחץ \"צור גיבוי\", או ייבא קובץ", "No local backups yet - enter a name and click \"Create backup\", or import a file"); lbBackups.Items.Add(c); }
            lbBackups.EndUpdate();
        }
        BackupInfo SelBackup() { var c = lbBackups == null ? null : lbBackups.Selected; return c == null ? null : c.Data as BackupInfo; }
        void CreateBackup()
        {
            if (!eng.Paired) return;
            string name = tbBkName.Text.Trim(); if (name == "") name = L.T("גיבוי ", "Backup ") + DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            eng.SaveLocal(name, tbBkDesc.Text.Trim(), cbBkHist.Checked);
            tbBkName.Text = ""; tbBkDesc.Text = ""; RefreshBackups(); if (lbBackups.Items.Count > 0) lbBackups.SelectedIndex = 0;
            stInfo.Text = L.T("הגיבוי נשמר: ", "Backup saved: ") + name;
        }
        void DeleteBackup()
        {
            var b = SelBackup(); if (b == null) return;
            if (MessageBox.Show(this, L.T("למחוק את הגיבוי \"", "Delete the backup \"") + b.Name + "\"?", "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            eng.DeleteLocal(b); RefreshBackups();
        }
        void ExportSelected()
        {
            var b = SelBackup(); if (b == null) { stInfo.Text = L.T("בחר גיבוי מהרשימה", "Select a backup from the list"); return; }
            string pwd;
            using (var pf = new PasswordForm(L.T("סיסמה לקובץ הגיבוי", "Password for the backup file"),
                L.T("הקובץ מכיל את מפתח ההצפנה של הקבוצה. מומלץ להגן עליו בסיסמה.\nהשאר ריק כדי לשמור קובץ ללא סיסמה.", "The file contains your group's encryption key. Protecting it with a password is recommended.\nLeave empty to save the file without a password."), true))
            { if (pf.ShowDialog(this) != DialogResult.OK) return; pwd = pf.Password; }
            using (var sd = new SaveFileDialog())
            {
                sd.Filter = L.T("גיבוי Cliport", "Cliport backup") + " (*" + BackupFile.Ext + ")|*" + BackupFile.Ext; sd.FileName = "Cliport-" + string.Join("_", b.Name.Split(Path.GetInvalidFileNameChars())) + BackupFile.Ext;
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                string text = b.Text;
                if (pwd != "") { var h = BackupFile.Header(b.Text); text = BackupFile.Create(b.Name, b.Desc, b.Device, b.Created, BackupFile.Payload(h, null), pwd); }
                File.WriteAllText(sd.FileName, text, new UTF8Encoding(false));
                stInfo.Text = L.T("קובץ הגיבוי נשמר", "The backup file was saved");
            }
        }
        /// <summary>Opens a backup file, asking for its password when needed. Returns the header and payload, or null.</summary>
        bool LoadBackupFile(out Dictionary<string, object> header, out Dictionary<string, object> payload)
        {
            header = null; payload = null;
            using (var od = new OpenFileDialog())
            {
                od.Filter = L.T("גיבוי Cliport", "Cliport backup") + " (*" + BackupFile.Ext + ")|*" + BackupFile.Ext + "|" + L.T("כל הקבצים", "All files") + "|*.*";
                if (od.ShowDialog() != DialogResult.OK) return false;
                string text; try { text = File.ReadAllText(od.FileName, Encoding.UTF8); } catch (Exception e) { MessageBox.Show(e.Message, "Cliport"); return false; }
                header = BackupFile.Header(text);
                if (header == null) { MessageBox.Show(L.T("זה אינו קובץ גיבוי של Cliport.", "This is not a Cliport backup file."), "Cliport", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            }
            if (!Json.B(header, "protected")) { payload = BackupFile.Payload(header, null); }
            else
                while (payload == null)
                {
                    using (var pf = new PasswordForm(L.T("סיסמת הגיבוי", "Backup password"), L.T("הגיבוי \"", "The backup \"") + Json.S(header, "name") + L.T("\" מוגן בסיסמה.", "\" is protected with a password."), false))
                    {
                        if (pf.ShowDialog() != DialogResult.OK) return false;
                        payload = BackupFile.Payload(header, pf.Password);
                        if (payload == null) MessageBox.Show(L.T("הסיסמה שגויה.", "Wrong password."), "Cliport", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            if (payload == null) { MessageBox.Show(L.T("קובץ הגיבוי פגום.", "The backup file is damaged."), "Cliport", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            return true;
        }
        void ImportBackup()
        {
            Dictionary<string, object> h, p; if (!LoadBackupFile(out h, out p)) return;
            // keep a copy inside the tool, then offer to restore it right away
            eng.StoreLocal(BackupFile.Create(Json.S(h, "name"), Json.S(h, "description"), Json.S(h, "device"), Json.L(h, "created"), p, null));
            RefreshBackups();
            if (MessageBox.Show(this, L.T("הגיבוי \"", "The backup \"") + Json.S(h, "name") + L.T("\" יובא ונשמר בכלי.\nלשחזר אותו עכשיו?", "\" was imported and saved in the tool.\nRestore it now?"), "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                ApplyRestore(p, Json.S(h, "name"), true);
        }
        void RestoreSelected()
        {
            var b = SelBackup(); if (b == null) { stInfo.Text = L.T("בחר גיבוי מהרשימה", "Select a backup from the list"); return; }
            if (MessageBox.Show(this, L.T("לשחזר את הגיבוי \"", "Restore the backup \"") + b.Name + L.T("\"?\nהצימוד, ההגדרות, הנעוצים וההעדפות יוחלפו בתוכן הגיבוי. המצב הנוכחי יישמר קודם כגיבוי אוטומטי.", "\"?\nPairing, settings, pins and preferences will be replaced by the backup. The current state is saved first as an automatic backup."),
                "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ApplyRestore(BackupFile.Payload(BackupFile.Header(b.Text), null), b.Name, true);
        }
        /// <summary>Restores a payload: safety backup of the current state, apply, re-apply language / theme / start-up, rebuild the window.</summary>
        void ApplyRestore(Dictionary<string, object> payload, string name, bool safetyBackup)
        {
            if (payload == null) return;
            try
            {
                if (safetyBackup && eng.Paired && !eng.Demo) eng.SaveLocal(L.T("אוטומטי - לפני שחזור", "Automatic - before restore"), L.T("נוצר אוטומטית לפני שחזור של: ", "Created automatically before restoring: ") + name, true);
                eng.Restore(payload);
                var prefs = Json.D(payload, "prefs");
                if (prefs != null && prefs.ContainsKey("autostart") && !eng.Demo) { try { Autostart.Enabled = Json.B(prefs, "autostart"); } catch { } }
                string lg = cfg.S("lang"); if (lg != "") L.En = lg == "en";
                if (cfg.S("theme") != "") Theme.Mode = cfg.S("theme");
                Rebuild();
                Toast("Cliport", L.T("השחזור הושלם: ", "Restore completed: ") + name);
                stInfo.Text = L.T("השחזור הושלם: ", "Restore completed: ") + name;
            }
            catch (Exception e) { Log.Write("restore failed: " + e); MessageBox.Show(L.T("השחזור נכשל: ", "Restore failed: ") + e.Message, "Cliport", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        public bool CliRestore(string path, string password)
        {
            try
            {
                var h = BackupFile.Header(File.ReadAllText(path, Encoding.UTF8));
                var p = h == null ? null : BackupFile.Payload(h, password);
                if (p == null) { Log.Write("--restore: not a valid backup, or wrong password: " + path); return false; }
                eng.StoreLocal(BackupFile.Create(Json.S(h, "name"), Json.S(h, "description"), Json.S(h, "device"), Json.L(h, "created"), p, null));
                ApplyRestore(p, Json.S(h, "name"), false);
                return eng.Paired;
            }
            catch (Exception e) { Log.Write("--restore failed: " + e.Message); return false; }
        }
        public void CliBackup(string path, string password)
        {
            try
            {
                File.WriteAllText(path, BackupFile.Create(L.T("גיבוי ", "Backup ") + DateTime.Now.ToString("dd/MM/yyyy HH:mm"), "", cfg.DeviceName, App.Now(), eng.BackupPayload(true), password), new UTF8Encoding(false));
                Log.Write("--backup written: " + path);
            }
            catch (Exception e) { Log.Write("--backup failed: " + e.Message); }
        }
        /// <summary>Used by the pairing window on a fresh installation: restore from the newest local backup or from a file.</summary>
        bool RestoreInteractive()
        {
            var local = eng.LocalBackups();
            if (local.Count > 0)
            {
                var b = local[0];
                var r = MessageBox.Show(L.T("נמצא גיבוי מקומי: \"", "A local backup was found: \"") + b.Name + L.T("\".\nלשחזר ממנו? (\"לא\" - בחירת קובץ גיבוי)", "\".\nRestore from it? (\"No\" - choose a backup file)"), "Cliport", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return false;
                if (r == DialogResult.Yes) { ApplyRestore(BackupFile.Payload(BackupFile.Header(b.Text), null), b.Name, false); return eng.Paired; }
            }
            Dictionary<string, object> h, p; if (!LoadBackupFile(out h, out p)) return false;
            eng.StoreLocal(BackupFile.Create(Json.S(h, "name"), Json.S(h, "description"), Json.S(h, "device"), Json.L(h, "created"), p, null));
            ApplyRestore(p, Json.S(h, "name"), false);
            return eng.Paired;
        }

        void ShowPage(int i)
        {
            page = i;
            for (int k = 0; k < pages.Length; k++) { pages[k].Visible = k == i; nav[k].Tag = k == i ? "nav+" : "nav"; nav[k].Font = k == i ? Ux.FBold : Ux.F; Theme.Apply(nav[k]); }
            if (i == 1 || i == 2) RefreshDevices();
            if (i == 4) RefreshBackups();
            if (i != 0 && Visible) nav[i].Focus();
            if (i == 0 && lbClips != null && Visible) lbClips.Focus();
        }

        // ---------------------------------------------------------------- refresh
        void RefreshClips()
        {
            if (lbClips == null) return;
            var keep = lbClips.Selected; string keepText = keep == null ? null : (string)keep.Data;
            lbClips.BeginUpdate(); lbClips.Items.Clear();
            var pins = eng.LivePins(); string f = filter.Trim();
            foreach (string t in pins) if (Match(t, f)) { var c = new Card(); c.Title = Ux.OneLine(t); c.Glyph = Ux.GPin; c.Tint = Theme.Accent; c.Badge = L.T("נעוץ", "Pinned"); c.BadgeColor = Theme.Accent; c.Data = t; lbClips.Items.Add(c); }
            foreach (string t in eng.History) if (!pins.Contains(t) && Match(t, f)) { var c = new Card(); c.Title = Ux.OneLine(t); c.Glyph = Ux.GText; c.Data = t; c.Badge = t.Length > 400 ? t.Length.ToString("N0") + L.T(" תווים", " chars") : ""; lbClips.Items.Add(c); }
            if (lbClips.Items.Count == 0)
            {
                var c = new Card(); c.Hint = true; c.Title = f != "" ? L.T("לא נמצאו תוצאות", "No results") : L.T("עדיין אין פריטים - העתק משהו בכל מכשיר והוא יופיע כאן", "Nothing here yet - copy something on any device and it will appear");
                lbClips.Items.Add(c);
            }
            lbClips.EndUpdate();
            if (keepText != null) for (int i = 0; i < lbClips.Items.Count; i++) { var c = (Card)lbClips.Items[i]; if ((string)c.Data == keepText) { lbClips.SelectedIndex = i; break; } }
        }
        static bool Match(string text, string f) { return f == "" || text.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0; }
        void RefreshDevices()
        {
            if (lbDev == null) return;
            lbDev.BeginUpdate(); lbRdp.BeginUpdate(); lbDev.Items.Clear(); lbRdp.Items.Clear();
            bool up = eng.UpCount > 0;
            var me = new Card(); me.Title = cfg.DeviceName + L.T("  (מחשב זה)", "  (this PC)"); me.Sub = "Windows  •  Cliport " + App.Version; me.Glyph = Ux.GPc; me.Tint = Theme.Accent;
            me.Badge = up ? L.T("מחובר", "Online") : L.T("מנותק", "Offline"); me.BadgeColor = up ? Theme.Ok : Theme.Warn; lbDev.Items.Add(me);
            var list = new List<DeviceInfo>(eng.Devices.Values); list.Sort(delegate (DeviceInfo a, DeviceInfo b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            foreach (var d in list)
            {
                bool on = d.IsOnline;
                var c = new Card(); c.Title = d.Name; c.Sub = (d.Plat == "ios" ? "iPhone / iPad" : "Windows") + "  •  Cliport " + d.Ver; c.Glyph = d.Plat == "ios" ? Ux.GPhone : Ux.GPc;
                c.Badge = on ? L.T("מחובר", "Online") : L.T("לא מחובר", "Offline"); c.BadgeColor = on ? Theme.Ok : Theme.Sub; c.Data = d.Id; lbDev.Items.Add(c);
                if (d.Plat != "win") continue;
                var r = new Card(); r.Title = d.Name; r.Glyph = Ux.GRemote; r.Data = d.Id; r.Sub = string.Join("   •   ", d.Addrs.ToArray());
                r.Badge = !on ? L.T("לא מחובר", "Offline") : d.RdpOn ? L.T("זמין לחיבור", "Ready") : L.T("Remote Desktop כבוי", "Remote Desktop is off");
                r.BadgeColor = !on ? Theme.Sub : d.RdpOn ? Theme.Ok : Theme.Warn; if (on && d.RdpOn) r.Tint = Theme.Accent; lbRdp.Items.Add(r);
            }
            if (lbRdp.Items.Count == 0) { var c = new Card(); c.Hint = true; c.Title = L.T("אין מחשבים נוספים בקבוצה - התקן את Cliport במחשב נוסף וצמד אותו", "No other PCs in the group - install Cliport on another PC and pair it"); lbRdp.Items.Add(c); }
            lbDev.EndUpdate(); lbRdp.EndUpdate();
            bool rdpOn = eng.Demo || (Rdp.Allowed() && Rdp.Listening()), ov = eng.Demo || Rdp.HasOverlay();
            lblRdp.Text = L.T("מחשב זה: Remote Desktop ", "This PC: Remote Desktop ") + (rdpOn ? L.T("פעיל וזמין", "is on and reachable") : L.T("כבוי", "is off")) +
                "   •   Tailscale: " + (ov ? L.T("פעיל (חיבור מכל רשת)", "active (reachable from any network)") : L.T("לא זוהה", "not detected")) +
                (ov ? "" : "\n" + L.T("לחיבור מרחוק בין רשתות שונות התקן Tailscale (חינם) בכל המכשירים והתחבר לאותו חשבון.", "To connect across different networks, install Tailscale (free) on all devices and sign in to the same account.")) +
                "\n" + L.T("מאייפון / אייפד: פתח את Cliport, עבור אל \"מחשבים\" ובחר מחשב.", "From iPhone / iPad: open Cliport, go to Computers and choose a PC.");
        }
        void RefreshStatus()
        {
            if (status == null) return;
            int up = eng.UpCount, total = eng.RelayCount;
            string s; Color col;
            if (!eng.Paired) { s = L.T("לא מצומד", "Not paired"); col = Theme.Warn; }
            else if (eng.Paused) { s = L.T("הסנכרון מושהה", "Sync is paused"); col = Theme.Warn; }
            else if (up == 0) { s = L.T("מתחבר מחדש...", "Reconnecting..."); col = Theme.Warn; }
            else { s = L.T("מחובר ומסנכרן", "Connected and syncing"); col = Theme.Ok; }
            stDot.ForeColor = col; stText.Text = s;
            stInfo.Text = (Recovery.SafeMode ? L.T("מצב בטוח  •  ", "Safe mode  •  ") : "") + (eng.TgReady ? L.T("אחסון Telegram פעיל  •  ", "Telegram storage on  •  ") : "") +
                (eng.Paired ? L.T("שרתי ממסר: ", "Relays: ") + up + "/" + total + "  •  " : "") + L.T("מכשירים: ", "Devices: ") + (eng.Devices.Count + 1);
            try { string t = "Cliport - " + s; tray.Text = t.Length > 62 ? t.Substring(0, 62) : t; } catch { }
            if (lbDev != null && lbDev.Items.Count > 0) { var me = (Card)lbDev.Items[0]; me.Badge = up > 0 ? L.T("מחובר", "Online") : L.T("מנותק", "Offline"); me.BadgeColor = up > 0 ? Theme.Ok : Theme.Warn; lbDev.Invalidate(); }
            if (lblTg != null) lblTg.Text = eng.TgReady ? L.T("אחסון Telegram מוגדר ומשותף לכל המכשירים.", "Telegram storage is set up and shared with all devices.") : L.T("אחסון Telegram אינו מוגדר.", "Telegram storage is not set up.");
            bool paused = eng.Paused;
            tbPause.Text = paused ? L.T("חדש", "Resume") : L.T("השהה", "Pause"); tbPause.Image = Ux.Glyph(paused ? Ux.GPlay : Ux.GPause, 22, Theme.Text); miPause.Text = paused ? L.T("חדש סנכרון", "Resume sync") : L.T("השהה סנכרון", "Pause sync");
        }
        void SetOffer(Offer o, bool keepPaths = false)
        {
            offer = o; if (!keepPaths) offerPaths = null;
            bool show = o != null && !o.Image;
            if (offerBar != null)
            {
                offerBar.Visible = show;
                if (show)
                {
                    var f0 = o.Files[0];
                    lblOffer.Text = L.T("קובץ ממתין מ-", "File waiting from ") + o.FromName + ":  " + f0.Name + (o.Files.Count > 1 ? " (+" + (o.Files.Count - 1) + ")" : "") + "\n" + App.SizeText(o.TotalSize) + "  •  " + Mime.Label(f0.Name) +
                        (offerPaths != null ? L.T("  •  התקבל, אפשר להדביק", "  •  received, ready to paste") : L.T("  •  הדבק (Ctrl+V) בתיקייה, או הורד עכשיו", "  •  paste (Ctrl+V) into a folder, or download now"));
                }
            }
            if (tbGet != null) { tbGet.Enabled = show; miGet.Enabled = show; }
        }
        void OnTick()
        {
            Recovery.Heartbeat(); Recovery.EnsureWatchdog();
            eng.Tick();
            if (++tickN % 6 == 0) { clip.Rearm(); if (Visible) RefreshDevices(); }
        }
        void Toast(string title, string text) { try { if (tray.Visible) tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Info); } catch { } Log.Write("notify: " + title + " | " + text); }

        // ---------------------------------------------------------------- local clipboard -> network
        void ReadLocalClipboard(int attempt)
        {
            if (!eng.Paired || eng.Paused || eng.Demo) return;
            try
            {
                IDataObject data = Clipboard.GetDataObject(); if (data == null) return;
                if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return;   // password managers etc.
                if (Clipboard.ContainsFileDropList() && !Clipboard.ContainsText())
                {
                    var l = new List<string>(); var dirs = new List<string>();
                    foreach (string s in Clipboard.GetFileDropList()) { if (File.Exists(s)) l.Add(s); else if (Directory.Exists(s)) dirs.Add(s); }
                    if (dirs.Count > 0) ZipFolders(l, dirs);
                    else if (l.Count > 0) { SetOffer(null); eng.LocalFiles(l.ToArray(), false); }
                }
                else if (Clipboard.ContainsText()) { string t = Clipboard.GetText(TextDataFormat.UnicodeText); SetOffer(null); eng.LocalText(t); }
                else if (Clipboard.ContainsImage())
                {
                    using (Image img = Clipboard.GetImage())
                    {
                        if (img == null) return;
                        string dir = App.PathOf("outgoing"); Directory.CreateDirectory(dir);
                        string p = Path.Combine(dir, "image-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                        img.Save(p, System.Drawing.Imaging.ImageFormat.Png);
                        SetOffer(null); eng.LocalFiles(new[] { p }, true);
                    }
                }
            }
            catch (ExternalException)
            {
                if (attempt < 6) { var t = new System.Windows.Forms.Timer(); t.Interval = 80; t.Tick += delegate { t.Stop(); t.Dispose(); ReadLocalClipboard(attempt + 1); }; t.Start(); }
            }
            catch (Exception e) { Log.Write("read clipboard: " + e.Message); }
        }
        /// <summary>Copied folders travel as zip files (compressed in the background; up to 500 MB each).</summary>
        void ZipFolders(List<string> files, List<string> dirs)
        {
            SetOffer(null);
            if (stInfo != null) stInfo.Text = L.T("דוחס תיקייה לשליחה...", "Compressing a folder to send...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                var all = new List<string>(files); string skipped = null;
                foreach (string dir in dirs)
                {
                    try
                    {
                        long size = 0; foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) { size += new FileInfo(f).Length; if (size > 500L * 1024 * 1024) break; }
                        string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar)); if (name == "") name = "folder";
                        if (size > 500L * 1024 * 1024) { skipped = name; continue; }
                        string outDir = App.PathOf("outgoing"); Directory.CreateDirectory(outDir);
                        string zip = Path.Combine(outDir, name + ".zip"); if (File.Exists(zip)) File.Delete(zip);
                        System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip, System.IO.Compression.CompressionLevel.Fastest, true);
                        all.Add(zip);
                    }
                    catch (Exception e) { Log.Write("zip folder: " + e.Message); }
                }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (skipped != null) Toast("Cliport", L.T("התיקייה גדולה מ-500MB ולא נשלחה: ", "The folder is larger than 500 MB and was not sent: ") + skipped);
                        if (all.Count > 0) { eng.LocalFiles(all.ToArray(), false); if (stInfo != null) stInfo.Text = L.T("התיקייה זמינה להדבקה בכל המכשירים (כקובץ zip)", "The folder is ready to paste on all devices (as a zip file)"); }
                    });
                }
                catch { }
            });
        }
        /// <summary>Toolbar "Send": push whatever is on the clipboard right now, even if it was already synced.</summary>
        void SendNow()
        {
            if (eng.Demo) return;
            try
            {
                if (Clipboard.ContainsText()) { string t = Clipboard.GetText(TextDataFormat.UnicodeText); eng.ForgetLast(); eng.LocalText(t); stInfo.Text = L.T("הלוח נשלח לכל המכשירים", "The clipboard was sent to all devices"); }
                else ReadLocalClipboard(0);
            }
            catch (Exception e) { Log.Write("send now: " + e.Message); }
        }
        string SelText() { var c = lbClips == null ? null : lbClips.Selected; return c == null ? null : (string)c.Data; }
        void CopySelected() { string s = SelText(); if (s == null) return; if (clip.SetText(s)) { eng.LocalText(s); stInfo.Text = L.T("הועתק לכל המכשירים", "Copied to all devices"); } }
        void TogglePin() { string s = SelText(); if (s == null) return; if (eng.IsPinned(s)) eng.Unpin(s); else eng.Pin(s); }
        void DeleteSelected() { string s = SelText(); if (s == null) return; if (eng.IsPinned(s)) eng.Unpin(s); else eng.RemoveHistory(s); }
        void TogglePause() { eng.Paused = !eng.Paused; RefreshStatus(); }
        void OpenFolder(string sub) { try { string d = sub == "" ? App.DataDir : App.PathOf(sub); Directory.CreateDirectory(d); Process.Start(d); } catch { } }
        void ShowCode() { if (eng.Paired) using (var f = new CodeForm(eng.Keys.Code)) f.ShowDialog(this); }
        /// <summary>Asks every connected device for the end of its log and saves the answers in the "diag" folder.</summary>
        void CollectDiag()
        {
            if (!eng.Paired || eng.RequestDiag() == 0) { Toast("Cliport", L.T("אין חיבור לשרתי הממסר.", "No connection to the relay servers.")); return; }
            int got = 0;
            eng.DiagReceived = delegate { got++; };
            var t = new System.Windows.Forms.Timer(); t.Interval = 6000;
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                Toast("Cliport", got == 0 ? L.T("אף מכשיר אחר לא ענה. ודא ש-Cliport פתוח במכשיר השני ונסה שוב.", "No other device answered. Make sure Cliport is open on the other device and try again.")
                                          : L.T("התקבלו יומנים מ-" + got + " מכשירים.", "Logs received from " + got + " device(s)."));
                if (got > 0) OpenFolder("diag");
            };
            t.Start();
        }
        void ShowAbout() { using (var a = new AboutForm(cfg)) a.ShowDialog(this); }
        void ShowHelp()
        {
            string p = HelpForm.Extract();
            if (p == null) { MessageBox.Show(this, L.T("המדריך אינו כלול בבנייה זו.", "The guide is not included in this build."), "Cliport"); return; }
            new HelpForm(p).Show(this);
        }
        void ShowShortcuts()
        {
            MessageBox.Show(this, L.T(
                "בכל מקום ב-Windows:\n  Ctrl+Alt+V   הדבקה מהירה מהנעוצים\n  Ctrl+Alt+R   חיבור מהיר לשולחן עבודה מרוחק\n\nבחלון Cliport:\n  Enter   העתק פריט לכל המכשירים\n  Ctrl+P   נעץ / בטל נעיצה\n  Del   מחק מההיסטוריה\n  Ctrl+F   חיפוש\n  Ctrl+Shift+V   שלח את הלוח עכשיו\n  Ctrl+D   הורד את הקובץ הממתין\n  Ctrl+K   הצג קוד צימוד\n  Ctrl+1..4   מעבר בין המסכים\n  F1   מדריך למשתמש",
                "Anywhere in Windows:\n  Ctrl+Alt+V   Quick paste from pins\n  Ctrl+Alt+R   Quick Remote Desktop connect\n\nIn the Cliport window:\n  Enter   Copy the item to all devices\n  Ctrl+P   Pin / unpin\n  Del   Delete from history\n  Ctrl+F   Search\n  Ctrl+Shift+V   Send the clipboard now\n  Ctrl+D   Download the pending file\n  Ctrl+K   Show the pairing code\n  Ctrl+1..4   Switch pages\n  F1   User guide"),
                L.T("קיצורי מקשים", "Keyboard shortcuts"), MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, L.Rtl ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading : 0);
        }

        // ---------------------------------------------------------------- remote files
        static string Describe(Offer o)
        {
            var sb = new StringBuilder();
            foreach (var f in o.Files) sb.AppendLine(L.T("שם: ", "Name: ") + f.Name + "\n" + L.T("גודל: ", "Size: ") + App.SizeText(f.Size) + "\n" + L.T("סוג: ", "Type: ") + Mime.Label(f.Name) + (f.Mime != "" ? " (" + f.Mime + ")" : "") + "\n");
            return sb.ToString().TrimEnd();
        }
        void OnOffer(Offer o)
        {
            if (o.Text && o.TotalSize <= 30L * 1024 * 1024)
            {
                SetOffer(null);
                stInfo.Text = L.T("מקבל טקסט גדול מ-", "Receiving a large text from ") + o.FromName + "...";
                eng.RequestFile(o, 0, null, delegate (string path, string err)
                {
                    if (err != null) { Toast("Cliport", L.T("קבלת הטקסט נכשלה: ", "Receiving the text failed: ") + err); return; }
                    try
                    {
                        string text = File.ReadAllText(path, Encoding.UTF8), mode = cfg.S("bigText");
                        bool ok = mode == "text" ? clip.SetText(text) : mode == "file" ? clip.SetFiles(new[] { path }) : clip.SetTextAndFile(text, path);
                        if (!ok) { Log.Write("clipboard busy - could not apply large text"); return; }
                        eng.NoteRemoteText(text);
                        string how = mode == "text" ? L.T("הדבק כרגיל.", "Paste as usual.") : mode == "file" ? L.T("הדבק בתיקייה כדי לקבל קובץ טקסט.", "Paste into a folder to get a text file.")
                            : L.T("הדבק בתוכנה כדי לקבל טקסט, או בתיקייה כדי לקבל קובץ.", "Paste into a program for the text, or into a folder for a file.");
                        Toast(L.T("טקסט גדול התקבל מ-", "Large text received from ") + o.FromName, App.SizeText(o.TotalSize) + " • " + text.Length.ToString("N0") + L.T(" תווים", " characters") + "\n" + how);
                        stInfo.Text = L.T("טקסט גדול התקבל", "Large text received");
                    }
                    catch (Exception e) { Log.Write("large text: " + e.Message); }
                });
                return;
            }
            SetOffer(o);
            if (o.Image && o.TotalSize <= 15L * 1024 * 1024)
            {
                eng.RequestFile(o, 0, null, delegate (string path, string err)
                {
                    if (err != null) { Toast("Cliport", L.T("קבלת התמונה נכשלה: ", "Receiving the image failed: ") + err); return; }
                    try { using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read)) using (var img = Image.FromStream(fs)) using (var bmp = new Bitmap(img)) clip.SetImage(bmp); }
                    catch (Exception e) { Log.Write("set image: " + e.Message); }
                });
                return;
            }
            bool armed = !Recovery.SafeMode && clip.OfferVirtualFiles(new VirtualFiles(o, delegate (int i) { return Materialize(o, i); }));
            var f0 = o.Files[0];
            Toast(L.T("קובץ זמין להדבקה מ-", "File ready to paste from ") + o.FromName, f0.Name + (o.Files.Count > 1 ? " (+" + (o.Files.Count - 1) + ")" : "") + " • " + App.SizeText(o.TotalSize) + " • " + Mime.Label(f0.Name) +
                (armed ? L.T("\nהדבק (Ctrl+V) בכל תיקייה כדי לקבל אותו.", "\nPaste (Ctrl+V) into any folder to receive it.") : L.T("\nלחץ כאן כדי להוריד.", "\nClick here to download.")));
        }
        bool Confirm(Offer o)
        {
            if (cfg.B("autoAccept", false) && !eng.Demo) return true;
            // MB_TOPMOST | MB_SETFOREGROUND: keep the question above the application that is pasting
            var opt = (MessageBoxOptions)0x50000 | (L.Rtl ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading : 0);
            return MessageBox.Show(L.T("לאשר הדבקת קובץ מ-", "Allow pasting a file from ") + o.FromName + "?\n\n" + Describe(o), L.T("Cliport - אישור הדבקת קובץ", "Cliport - confirm file paste"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, opt) == DialogResult.Yes;
        }
        /// <summary>Downloads every file of the offer behind a modal progress window. Returns local paths or null.</summary>
        string[] Download(Offer o)
        {
            if (fetching || eng.Demo) return null;
            fetching = true;
            try
            {
                var got = new List<string>(); string error = null; int idx = 0;
                var pf = new ProgressForm(L.T("מקבל: ", "Receiving: ") + o.Files[0].Name);
                Action next = null;
                next = delegate
                {
                    int cur = idx;
                    eng.RequestFile(o, cur, delegate (int pc) { pf.Set(L.T("מקבל: ", "Receiving: ") + o.Files[cur].Name + " (" + (cur + 1) + "/" + o.Files.Count + ")", pc); },
                        delegate (string path, string err)
                        {
                            if (err != null) { error = err; pf.Finish(); return; }
                            got.Add(path); idx++;
                            if (idx < o.Files.Count) next(); else pf.Finish();
                        });
                };
                next();
                pf.ShowDialog(); pf.Dispose();
                if (error != null) { Toast(L.T("Cliport - העברת הקובץ נכשלה", "Cliport - file transfer failed"), error); return null; }
                return got.ToArray();
            }
            finally { fetching = false; }
        }
        /// <summary>An application is pasting file #idx of the promised files right now: confirm once, download, hand over.</summary>
        string Materialize(Offer o, int idx)
        {
            if (offer != o) return null;
            if (offerPaths == null)
            {
                if (App.Now() - declinedAt < 4000) return null;          // one "no" covers every file of this paste
                Log.Write("paste of remote file requested");
                if (!Confirm(o)) { declinedAt = App.Now(); Log.Write("paste declined"); return null; }
                var paths = Download(o); if (paths == null) return null;
                offerPaths = paths; SetOffer(o, true);
                // afterwards put the real files on the clipboard, so every application (not only Explorer) can paste them
                var t = new System.Windows.Forms.Timer(); t.Interval = 4000;
                t.Tick += delegate { t.Stop(); t.Dispose(); if (offer == o && !fetching) clip.SetFiles(paths); };
                t.Start();
            }
            if (idx < 0) idx = 0;                                        // some consumers pass -1 for a single file
            return idx < offerPaths.Length ? offerPaths[idx] : null;
        }
        void FetchOfferNow()
        {
            var o = offer; if (o == null) { Toast("Cliport", L.T("אין קובץ ממתין.", "No file is waiting.")); return; }
            if (offerPaths == null) { if (!Confirm(o)) return; var p = Download(o); if (p == null) return; offerPaths = p; SetOffer(o, true); }
            clip.SetFiles(offerPaths);
            Toast("Cliport", L.T("הקובץ מוכן - הדבק (Ctrl+V) בכל תיקייה.", "The file is ready - paste (Ctrl+V) into any folder."));
        }

        // ---------------------------------------------------------------- hotkeys, pins, rdp
        void OnHotKey(int id)
        {
            if (id == 1) { prevFg = Native.GetForegroundWindow(); popup.ShowAt(eng.LivePins()); }
            else if (id == 2)
            {
                var m = new ContextMenuStrip(); m.RightToLeft = L.Rtl ? RightToLeft.Yes : RightToLeft.No; AddRdpItems(m.Items); Theme.Style(m);
                Native.SetForegroundWindow(clip.Handle); m.Show(Cursor.Position);
            }
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
                var dev = d; var it = new ToolStripMenuItem(d.Name + (d.IsOnline && d.RdpOn ? "" : L.T("  (לא זמין כרגע)", "  (not available now)")), Ux.Glyph(Ux.GRemote, 16, Theme.Text));
                it.Click += delegate { Connect(dev); };
                items.Add(it);
            }
            if (n == 0) { var e = new ToolStripMenuItem(L.T("אין מחשבים נוספים בקבוצה", "No other PCs in the group")); e.Enabled = false; items.Add(e); }
        }
        void Connect(DeviceInfo d)
        {
            if (eng.Demo) return;
            if (stInfo != null) stInfo.Text = L.T("מתחבר אל ", "Connecting to ") + d.Name + "...";
            Rdp.Connect(d, delegate (string err) { BeginInvoke((Action)delegate { Toast("Cliport - Remote Desktop", err); if (Visible) MessageBox.Show(this, err, "Cliport - Remote Desktop", MessageBoxButtons.OK, MessageBoxIcon.Warning); }); });
        }
        void ConnectSelected()
        {
            var c = lbRdp.Selected; if (c == null) return;
            DeviceInfo d; if (eng.Devices.TryGetValue((string)c.Data, out d)) Connect(d);
        }
        void EnableRdp()
        {
            if (MessageBox.Show(this, L.T("הפעולה תפעיל את Remote Desktop של Windows במחשב זה (נדרש אישור מנהל):\n• התרת חיבורי RDP עם אימות NLA\n• פתיחת כלל חומת האש של Remote Desktop\n• הפעלה אוטומטית של השירות\n\nלהמשיך?",
                "This turns on Windows Remote Desktop on this PC (administrator approval required):\n• Allow RDP connections with NLA authentication\n• Open the Remote Desktop firewall rule\n• Start the service automatically\n\nContinue?"),
                "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Rdp.EnableHost(); eng.PublishDev(); RefreshDevices();
        }
        void SaveTelegram()
        {
            string token = tbToken.Text.Trim(), chat = tbChat.Text.Trim();
            if (token == "") { eng.SetTelegram("", ""); RefreshStatus(); return; }
            try
            {
                Cursor = Cursors.WaitCursor;
                if (chat == "") { chat = Telegram.FindChat(token); if (chat == null) { MessageBox.Show(this, L.T("לא נמצאה הודעה לבוט. שלח לבוט הודעה כלשהי בטלגרם ונסה שוב.", "No message to the bot was found. Send any message to the bot in Telegram and try again."), "Cliport"); return; } tbChat.Text = chat; }
                eng.SetTelegram(token, chat); RefreshStatus(); Toast("Cliport", L.T("אחסון Telegram הוגדר ויופץ לכל המכשירים.", "Telegram storage was set up and will be shared with all devices."));
            }
            catch (Exception e) { MessageBox.Show(this, L.T("הפנייה ל-Telegram נכשלה: ", "The request to Telegram failed: ") + e.Message, "Cliport"); }
            finally { Cursor = Cursors.Default; }
        }

        // ---------------------------------------------------------------- tray / window
        void BuildTrayMenu()
        {
            var cms = tray.ContextMenuStrip; cms.RightToLeft = L.Rtl ? RightToLeft.Yes : RightToLeft.No;
            var m = cms.Items; m.Clear();
            m.Add(L.T("פתח את Cliport", "Open Cliport"), null, delegate { ShowMain(); });
            m.Add(L.T("שלח את הלוח עכשיו", "Send clipboard now"), Ux.Glyph(Ux.GSend, 16, Theme.Text), delegate { SendNow(); });
            var pins = new ToolStripMenuItem(L.T("נעוצים", "Pins"), Ux.Glyph(Ux.GPin, 16, Theme.Text));
            foreach (string p in eng.LivePins()) { string t = p; string one = Ux.OneLine(p); pins.DropDownItems.Add(one.Length > 60 ? one.Substring(0, 60) + "…" : one, null, delegate { if (clip.SetText(t)) eng.LocalText(t); }); }
            if (pins.DropDownItems.Count == 0) pins.Enabled = false;
            m.Add(pins);
            var rdp = new ToolStripMenuItem(L.T("שולחן עבודה מרוחק", "Remote Desktop"), Ux.Glyph(Ux.GRemote, 16, Theme.Text)); AddRdpItems(rdp.DropDownItems); m.Add(rdp);
            if (offer != null && !offer.Image) m.Add(L.T("הורד את הקובץ הממתין (", "Download the pending file (") + offer.Files[0].Name + ")", Ux.Glyph(Ux.GDownload, 16, Theme.Text), delegate { FetchOfferNow(); });
            var pause = new ToolStripMenuItem(L.T("השהה סנכרון", "Pause sync")); pause.Checked = eng.Paused; pause.Click += delegate { TogglePause(); }; m.Add(pause);
            m.Add(new ToolStripSeparator());
            m.Add(L.T("מדריך למשתמש", "User guide"), Ux.Glyph(Ux.GHelp, 16, Theme.Text), delegate { ShowMain(); ShowHelp(); });
            m.Add(L.T("יציאה", "Exit"), null, delegate { ExitApp(); });
            Theme.Style(cms);
        }
        public void ShowMain()
        {
            if (pairing) return;
            if (!eng.Paired && !EnsurePaired()) return;
            RefreshDevices(); Show(); WindowState = FormWindowState.Normal; Activate();
            if (page == 0 && lbClips != null) lbClips.Focus(); else if (nav != null) nav[page].Focus();
        }
        public bool EnsurePaired()
        {
            if (eng.Paired) return true;
            if (pairing) return false;
            pairing = true;
            try
            {
                using (var pf = new PairForm(RestoreInteractive))
                {
                    if (pf.ShowDialog() != DialogResult.OK) return false;
                    if (pf.Restored) return true;            // a backup was restored: paired, configured and connected already
                    cfg.Code = pf.Code; cfg.Save(); eng.Start(); RefreshAll();
                    if (!cfg.B("askedAutostart", false))
                    {
                        cfg.Set("askedAutostart", true); cfg.Save();
                        if (MessageBox.Show(L.T("להפעיל את Cliport אוטומטית עם עליית Windows? (מומלץ - כך הסנכרון תמיד פעיל)", "Start Cliport automatically with Windows? (Recommended - sync is then always on)"), "Cliport", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        { Autostart.Enabled = true; Rebuild(); }
                    }
                    using (var cf = new CodeForm(eng.Keys.Code)) cf.ShowDialog();
                    return true;
                }
            }
            finally { pairing = false; }
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
            if (!exiting && e.CloseReason == CloseReason.UserClosing && !eng.Demo) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { base.OnFormClosed(e); if (eng.Demo && !exiting) ExitApp(); }
        public void ExitApp()
        {
            exiting = true; Recovery.Exiting = true; Recovery.CleanExit();
            try { tick.Stop(); fast.Stop(); eng.Stop(); Native.UnregisterHotKey(clip.Handle, 1); Native.UnregisterHotKey(clip.Handle, 2); clip.Dispose2(); tray.Visible = false; tray.Dispose(); } catch { }
            Application.Exit();
        }
    }

    static class Program
    {
        public static int StartPage; public static string OpenWhat;
        [STAThread]
        static int Main(string[] args)
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.Name);
            string join = null, lang = null, theme = null, backupTo = null, restoreFrom = null, password = null; bool initNew = false, minimized = false, demo = false; int wdPid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--data": if (i + 1 < args.Length) data = args[++i]; break;
                    case "--join": if (i + 1 < args.Length) join = args[++i]; break;
                    case "--watchdog": if (i + 1 < args.Length) int.TryParse(args[++i], out wdPid); break;
                    case "--lang": if (i + 1 < args.Length) lang = args[++i]; break;
                    case "--theme": if (i + 1 < args.Length) theme = args[++i]; break;
                    case "--init-new": initNew = true; break;
                    case "--minimized": case "--recovered": minimized = true; break;
                    case "--no-watchdog": Recovery.WatchdogEnabled = false; break;
                    case "--demo": demo = true; Recovery.WatchdogEnabled = false; break;      // sample data, no network (documentation screenshots)
                    case "--page": if (i + 1 < args.Length) int.TryParse(args[++i], out StartPage); break;
                    case "--open": if (i + 1 < args.Length) OpenWhat = args[++i]; break;      // demo only: about | code | pair | confirm | pins
                    case "--backup": if (i + 1 < args.Length) backupTo = args[++i]; break;      // write a backup file at start-up
                    case "--restore": if (i + 1 < args.Length) restoreFrom = args[++i]; break;  // unattended restore (fresh installation)
                    case "--password": if (i + 1 < args.Length) password = args[++i]; break;    // for --backup / --restore
                }
            }
            try { Native.SetProcessDPIAware(); } catch { }
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
            // anything else is fatal: log it and let the watchdog restart us (exit at once instead of sitting behind a Windows error dialog)
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.Write("FATAL: " + e.ExceptionObject); Environment.Exit(1); };

            Log.Write("---- Cliport v" + App.Version + " starting (pid " + Process.GetCurrentProcess().Id + ") ----");
            var cfg = new Config();
            if (lang != null) cfg.Set("lang", lang);
            if (theme != null) cfg.Set("theme", theme);
            string lg = cfg.S("lang");
            L.En = lg == "en" || (lg == "" && System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "he");
            Theme.Mode = cfg.S("theme") == "" ? "system" : cfg.S("theme"); Theme.Resolve();
            if (!demo) { Recovery.OnStart(); Recovery.EnsureWatchdog(); }     // before any UI exists, so a crash during start-up is covered too
            SystemEvents.SessionEnding += delegate { Recovery.SessionEnding(); };
            Ux.Init();
            if (join != null && KeyRing.FromCode(join) != null) cfg.Code = B32.Encode(B32.Decode(join));
            else if (initNew && cfg.Code == "") cfg.Code = KeyRing.NewCode();
            cfg.Save();

            var form = new MainForm(cfg, demo);
            var waiter = new Thread(delegate () { while (true) { showEvt.WaitOne(); try { form.BeginInvoke((Action)form.ShowMain); } catch { } } });
            waiter.IsBackground = true; waiter.Start();
            if (restoreFrom != null) form.CliRestore(restoreFrom, password);
            if (!demo && !form.EnsurePaired()) { form.ExitApp(); return 0; }
            if (backupTo != null) form.CliBackup(backupTo, password);
            if (!minimized) form.ShowMain();
            Application.Run();
            Recovery.CleanExit();
            GC.KeepAlive(mutex);
            return 0;
        }
    }
}
