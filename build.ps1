# Builds dist\Cliport.exe with the C# compiler that ships with Windows (.NET Framework 4.8). No SDK needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $out | Out-Null
$icon = Join-Path $root 'assets\Cliport.ico'
$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/codepage:65001', '/warn:3',
    "/out:$out\Cliport.exe",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll', '/r:System.Security.dll')
if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }
$cscArgs += (Get-ChildItem (Join-Path $root 'windows\src\*.cs') | ForEach-Object { $_.FullName })
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
"Built: $out\Cliport.exe ($([math]::Round((Get-Item "$out\Cliport.exe").Length / 1KB)) KB)"
