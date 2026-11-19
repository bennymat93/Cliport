# Builds the Cliport user guide (Hebrew + English):
#   1. takes screenshots of the real program in demo mode (sample data, no network)
#   2. draws numbered call-outs on them
#   3. produces one self-contained HTML per language   -> docs\dist\guide.<lang>.html  (embedded in the Windows app and the iOS app)
#   4. prints them to PDF with Microsoft Edge           -> docs\Cliport-User-Guide-<lang>.pdf
# Run build.ps1 first (needs dist\Cliport.exe), and again afterwards to embed the new guide.
$ErrorActionPreference = 'Stop'
$docs = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $docs
$exe = Join-Path $root 'dist\Cliport.exe'
if (-not (Test-Path $exe)) { throw 'dist\Cliport.exe not found - run build.ps1 first' }
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System; using System.Runtime.InteropServices; using System.Text; using System.Drawing; using System.Drawing.Drawing2D;
public static class Shot {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  // main = the window titled exactly "Cliport"; otherwise the top-most other visible window of the process (a dialog)
  public static IntPtr Find(uint pid, bool main) {
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p); if (p != pid || !IsWindowVisible(h)) return true;
      var t = new StringBuilder(256); GetWindowText(h, t, 256); bool isMain = t.ToString() == "Cliport";
      if (isMain == main && t.Length > 0) { found = h; return false; } return true; }, IntPtr.Zero);
    return found; }
  // Captures the window (without the invisible resize border). marks: "x,y,w,h;..." in client pixels of a design-size client area.
  public static void Capture(IntPtr h, string path, string marks, int designW, bool mirror) {
    RECT w; GetWindowRect(h, out w); RECT f; if (DwmGetWindowAttribute(h, 9, out f, 16) != 0) f = w;
    RECT c; GetClientRect(h, out c); var o = new POINT(); ClientToScreen(h, ref o);
    using (var full = new Bitmap(w.R - w.L, w.B - w.T)) {
      using (var g = Graphics.FromImage(full)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
      var crop = new Rectangle(f.L - w.L, f.T - w.T, f.R - f.L, f.B - f.T);
      using (var b = full.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
        // PrintWindow returns a mirror image for windows laid out right-to-left by the system (message boxes): flip it back
        if ((GetWindowLong(h, -20) & 0x00400000) != 0) b.RotateFlip(RotateFlipType.RotateNoneFlipX);
        if (!string.IsNullOrEmpty(marks)) using (var g = Graphics.FromImage(b)) {
          g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
          float s = (float)(c.R - c.L) / designW; int ox = o.X - f.L, oy = o.Y - f.T; int n = 0;
          var red = Color.FromArgb(226, 59, 78);
          using (var pen = new Pen(red, 2.4f * s)) using (var fill = new SolidBrush(red)) using (var font = new Font("Segoe UI", 11f * s, FontStyle.Bold, GraphicsUnit.Pixel)) using (var sf = new StringFormat())
          foreach (string m in marks.Split(';')) {
            var v = m.Split(','); if (v.Length != 4) continue; n++;
            float x = float.Parse(v[0]), y = float.Parse(v[1]), ww = float.Parse(v[2]), hh = float.Parse(v[3]);
            if (mirror) x = designW - x - ww;
            var r = new RectangleF(ox + x * s, oy + y * s, ww * s, hh * s);
            float rad = 6 * s;
            using (var gp = new GraphicsPath()) {
              gp.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90); gp.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
              gp.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90); gp.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90); gp.CloseFigure();
              g.DrawPath(pen, gp); }
            // number badge on the corner where there is least content: left corner for right-to-left screens, right corner otherwise
            float br = 11 * s, cx = mirror ? r.Right : r.X, cy = r.Y;
            cx = Math.Max(br + 1, Math.Min(b.Width - br - 1, cx)); cy = Math.Max(br + 1, Math.Min(b.Height - br - 1, cy));
            g.FillEllipse(Brushes.White, cx - br - 1.5f * s, cy - br - 1.5f * s, (br + 1.5f * s) * 2, (br + 1.5f * s) * 2);
            g.FillEllipse(fill, cx - br, cy - br, br * 2, br * 2);
            sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
            g.DrawString(n.ToString(), font, Brushes.White, new RectangleF(cx - br, cy - br, br * 2, br * 2), sf);
          }
        }
        b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
      } } }
}
'@
[void][Shot]::SetProcessDPIAware()

function Take($lang, $name, $page, $theme, $open, $marks, $designW) {
    $out = Join-Path $docs "guide\img\$lang\$name.png"
    $data = Join-Path $env:TEMP ("cliport-guide-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $a = "--demo --data `"$data`" --lang $lang --theme $theme --page $page"
    if ($open) { $a += " --open $open" }
    $p = Start-Process $exe -ArgumentList $a -PassThru
    try {
        $h = [IntPtr]::Zero
        for ($i = 0; $i -lt 40 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250; $h = [Shot]::Find([uint32]$p.Id, -not $open) }
        if ($h -eq [IntPtr]::Zero) { throw "window for '$name' ($lang) did not appear" }
        Start-Sleep -Milliseconds 900
        [Shot]::Capture($h, $out, $marks, $designW, ($lang -eq 'en'))
        "  $lang\$name.png"
    } finally { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 250; Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue }
}

# Call-out rectangles: client pixels of the Hebrew (right-to-left) layout at 100% scale; mirrored automatically for English.
$main     = '724,3,228,20;496,27,452,46;11,29,140,40;759,85,196,232;17,151,718,28;17,183,718,64;27,249,702,100;27,351,702,248;1,639,958,21'
$devices  = '27,153,702,262;327,579,406,48'
$rdp      = '267,149,468,48;27,217,702,108;195,579,538,48'
$settings = '465,197,270,110;305,309,430,118;123,430,612,196'
$pair     = '18,86,504,42;18,146,504,112;18,268,504,60'
$backup   = '17,151,718,130;27,286,702,122;213,579,522,48'

foreach ($lang in 'he', 'en') {
    New-Item -ItemType Directory -Force (Join-Path $docs "guide\img\$lang") | Out-Null
    "screenshots ($lang):"
    Take $lang 'main'     0 'light' $null     $main     960
    Take $lang 'devices'  1 'light' $null     $devices  960
    Take $lang 'rdp'      2 'light' $null     $rdp      960
    Take $lang 'settings' 3 'light' $null     $settings 960
    Take $lang 'backup'   4 'light' $null     $backup   960
    Take $lang 'dark'     0 'dark'  $null     $null     960
    Take $lang 'pair'     0 'light' 'pair'    $pair     540
    Take $lang 'code'     0 'light' 'code'    $null     580
    Take $lang 'pins'     0 'light' 'pins'    $null     440
    Take $lang 'confirm'  0 'light' 'confirm' $null     400
}
Copy-Item (Join-Path $root 'assets\logo.png') (Join-Path $docs 'guide\img\logo.png') -Force

# ---- one self-contained HTML per language (stylesheet and pictures inlined)
$dist = Join-Path $docs 'dist'; New-Item -ItemType Directory -Force $dist | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$css = [IO.File]::ReadAllText((Join-Path $docs 'guide\guide.css'), [Text.Encoding]::UTF8)
foreach ($lang in 'he', 'en') {
    $html = [IO.File]::ReadAllText((Join-Path $docs "guide\guide.$lang.html"), [Text.Encoding]::UTF8)
    $html = $html.Replace('<link rel="stylesheet" href="guide.css">', "<style>`n$css`n</style>")
    $html = [regex]::Replace($html, 'src="(img/[^"]+)"', {
        param($m)
        $file = Join-Path $docs ('guide\' + $m.Groups[1].Value.Replace('/', '\'))
        'src="data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) + '"'
    })
    $one = Join-Path $dist "guide.$lang.html"
    [IO.File]::WriteAllText($one, $html, $utf8)
    Copy-Item $one (Join-Path $root "ios\Cliport\guide.$lang.html") -Force
    "html: dist\guide.$lang.html ($([math]::Round((Get-Item $one).Length / 1KB)) KB)"
}

# ---- PDF
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { Write-Warning 'Microsoft Edge not found - PDF files were not produced'; return }
foreach ($lang in 'he', 'en') {
    $pdf = Join-Path $docs "Cliport-User-Guide-$lang.pdf"
    Remove-Item $pdf -ErrorAction SilentlyContinue
    $prof = Join-Path $env:TEMP ("cliport-edge-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $uri = (New-Object Uri((Join-Path $dist "guide.$lang.html"))).AbsoluteUri
    $p = Start-Process $edge -ArgumentList "--headless=new --disable-gpu --no-first-run --user-data-dir=`"$prof`" --no-pdf-header-footer --print-to-pdf=`"$pdf`" `"$uri`"" -PassThru
    if (-not $p.WaitForExit(90000)) { $p.Kill() }
    for ($i = 0; $i -lt 20 -and -not (Test-Path $pdf); $i++) { Start-Sleep -Milliseconds 500 }
    Remove-Item $prof -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $pdf) { "pdf:  Cliport-User-Guide-$lang.pdf ($([math]::Round((Get-Item $pdf).Length / 1KB)) KB)" } else { Write-Warning "PDF for $lang was not produced" }
}
