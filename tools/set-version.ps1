# Sets the Cliport version everywhere (program, installer, iOS app, release workflow, README, guide).
#   powershell -ExecutionPolicy Bypass -File tools\set-version.ps1 1.6
# The current version is kept in the VERSION file at the repository root. Rebuild afterwards (build.ps1, docs\build-guide.ps1).
param([Parameter(Mandatory = $true)][string]$New)
$root = Split-Path -Parent $PSScriptRoot
if ($New -notmatch '^\d+\.\d+$') { throw 'Version must look like 1.6' }
$old = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if ($old -eq $New) { "Already $New"; return }
$files = @('.github\workflows\build-ipa.yml', 'README.md', 'docs\guide\guide.he.html', 'docs\guide\guide.en.html', 'ios\project.yml', 'tools\Setup.cs', 'web\index.html') +
         @(Get-ChildItem (Join-Path $root 'ios\Cliport') -Filter *.swift | ForEach-Object { 'ios\Cliport\' + $_.Name }) +
         @(Get-ChildItem (Join-Path $root 'windows\src') -Filter *.cs | ForEach-Object { 'windows\src\' + $_.Name })
$o = [regex]::Escape($old)
foreach ($f in $files) {
    $p = Join-Path $root $f
    $bytes = [IO.File]::ReadAllBytes($p)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $t = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
    $n = [regex]::Replace($t, "v$o(?![\d.])", "v$New")              # v1.0 (titles, file names, release tag)
    $n = [regex]::Replace($n, "`"$o`"", "`"$New`"")                  # "1.0" (version constants, Info.plist values)
    $n = [regex]::Replace($n, "(?<![\d.])$o\.0\.0(?![\d.])", "$New.0.0")   # 1.0.0.0 (assembly versions)
    if ($n -ne $t) { [IO.File]::WriteAllText($p, $n, (New-Object Text.UTF8Encoding $bom)); "updated $f" }
}
[IO.File]::WriteAllText((Join-Path $root 'VERSION'), "$New`n", (New-Object Text.UTF8Encoding $false))
"Version: $old -> $New"
