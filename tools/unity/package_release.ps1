# Packs the local Unity build (tools\unity\build_local.ps1) into a release zip:
#   powershell -ExecutionPolicy Bypass -File tools\unity\package_release.ps1
# -> build\release\FishingSim-<version>-Windows.zip (the player, its data and StreamingAssets; Unity's
#    "BackUpThisFolder_ButDontShipItWithYourGame" debug folder left out)
$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$Build = Join-Path $Root "build\local\Windows"
if (-not (Test-Path (Join-Path $Build "FishingSim.exe"))) { throw "No build at $Build (run tools\unity\build_local.ps1 first)" }
$version = (Select-String -Path (Join-Path $Root "unity\ProjectSettings\ProjectSettings.asset") -Pattern "^\s*bundleVersion: (.+)").Matches[0].Groups[1].Value.Trim()
$Out = Join-Path $Root "build\release"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Stage = Join-Path $Out "FishingSim-$version"
if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null
Get-ChildItem $Build | Where-Object { $_.Name -notlike "*BackUpThisFolder*" } | ForEach-Object { Copy-Item $_.FullName -Destination $Stage -Recurse }
$Zip = Join-Path $Out "FishingSim-$version-Windows.zip"
if (Test-Path $Zip) { Remove-Item -Force $Zip }
Compress-Archive -Path (Join-Path $Stage "*") -DestinationPath $Zip -CompressionLevel Optimal
Write-Host "== release: $Zip ($([math]::Round((Get-Item $Zip).Length / 1MB)) MB)"
