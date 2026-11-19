# Builds dist\Cliport.exe with the C# compiler that ships with Windows (.NET Framework 4.8). No SDK needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $out | Out-Null
$icon = Join-Path $root 'assets\Cliport.ico'

# logo shown in the About dialog (scaled once from the 1024px icon)
$logo = Join-Path $root 'assets\logo.png'
if (-not (Test-Path $logo)) {
    Add-Type -AssemblyName System.Drawing
    $src = [Drawing.Image]::FromFile((Join-Path $root 'assets\icon-1024.png'))
    $cur = $src
    foreach ($n in 512, 256) {
        $b = New-Object Drawing.Bitmap($n, $n); $g = [Drawing.Graphics]::FromImage($b)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.DrawImage($cur, 0, 0, $n, $n); $g.Dispose()
        if ($cur -ne $src) { $cur.Dispose() }; $cur = $b
    }
    $cur.Save($logo, [Drawing.Imaging.ImageFormat]::Png); $cur.Dispose(); $src.Dispose()
}

$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/codepage:65001', '/warn:3',
    "/out:$out\Cliport.exe",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll', '/r:System.Security.dll',
    "/resource:$logo,logo.png")
if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }
# the user guide is embedded in the program (built by docs\build-guide.ps1)
foreach ($lang in 'he', 'en') {
    $guide = Join-Path $root "docs\dist\guide.$lang.html"
    if (Test-Path $guide) { $cscArgs += "/resource:$guide,guide.$lang.html" }
}
$cscArgs += (Get-ChildItem (Join-Path $root 'windows\src\*.cs') | ForEach-Object { $_.FullName })
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
"Built: $out\Cliport.exe ($([math]::Round((Get-Item "$out\Cliport.exe").Length / 1KB)) KB)"
