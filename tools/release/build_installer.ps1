# Builds FishingSim's own installer (tools/release/installer/Setup.cs) with the C# compiler built into Windows:
#   powershell -File tools/release/build_installer.ps1 [-Version 0.12.0]
# Writes build/release/setup/FishingSim-<ver>-Setup.exe next to the data parts it unpacks (hard links to
# build/release/FishingSim-<ver>-Windows-czesc*.zip, made by the split step) and LICENSE.txt.
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if (-not $Version) { $Version = (Get-Content (Join-Path $root "tools\release\VERSION") -Raw).Trim() }
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = Join-Path $root "tools\release\installer"
$out = Join-Path $root "build\release\setup"
New-Item -ItemType Directory -Force $out | Out-Null
$ver = Join-Path $env:TEMP "FishingSimSetupVersion.cs"
Set-Content $ver "[assembly: System.Reflection.AssemblyVersion(`"$Version.0`")] [assembly: System.Reflection.AssemblyFileVersion(`"$Version.0`")]" -Encoding utf8
$exe = Join-Path $out "FishingSim-$Version-Setup.exe"
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$exe" "/win32icon:$(Join-Path $root 'tools\release\icon.ico')" `
    "/win32manifest:$(Join-Path $src 'setup.manifest')" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $src "Setup.cs") $ver
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
foreach ($part in Get-ChildItem (Join-Path $root "build\release") -Filter "FishingSim-$Version-Windows-czesc*.zip") {
    $link = Join-Path $out $part.Name
    if (-not (Test-Path $link)) { New-Item -ItemType HardLink -Path $link -Target $part.FullName | Out-Null }
}
Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE.txt") -Force
Get-ChildItem $out | Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB) } }
