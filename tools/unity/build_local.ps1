# Local Unity build and test on Windows (the same steps as .github/workflows/unity.yml):
#   1. converts the models and pictures for Unity (Python venv with Pillow and NumPy),
#   2. builds the game with Unity 6000.0.64f1 in batch mode (BuildGame.Build),
#   3. optionally runs the smoke test and saves full-size screenshots.
#
#   powershell -ExecutionPolicy Bypass -File tools\unity\build_local.ps1            # convert + build
#   powershell -ExecutionPolicy Bypass -File tools\unity\build_local.ps1 -Smoke     # + automatic test with screenshots
#   powershell -ExecutionPolicy Bypass -File tools\unity\build_local.ps1 -SkipConvert -Run   # build and start the game
param(
    [switch]$SkipConvert,
    [switch]$Smoke,
    [switch]$Run,
    [int]$Quality = 3,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Unity.exe"
)
$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $Root
$Out = Join-Path $Root "build\local"
New-Item -ItemType Directory -Force -Path $Out | Out-Null

if (-not (Test-Path $Unity)) { throw "Unity 6000.0.64f1 not found at $Unity (install it in Unity Hub, or pass -Unity <path to Unity.exe>)" }

if (-not $SkipConvert) {
    Write-Host "== converting models and pictures"
    $venv = Join-Path $Root "build\venv"
    if (-not (Test-Path "$venv\Scripts\python.exe")) {
        py -3 -m venv $venv
        & "$venv\Scripts\python.exe" -m pip install --quiet pillow numpy
    }
    $py = "$venv\Scripts\python.exe"
    $sa = Join-Path $Root "unity\Assets\StreamingAssets"
    & $py -I tools\unity\convert_glb.py assets\fish\meshy "$sa\Models\fish" --fish --max 2048
    & $py -I tools\unity\convert_glb.py assets\models "$sa\Models" --max 4096
    & $py -I tools\unity\convert_glb.py assets\characters "$sa\Models\characters" --max 2048
    & $py -I tools\unity\convert_textures.py assets\characters\textures "$sa\Models\characters\textures" --max 2048
    Get-ChildItem assets\textures -Directory | ForEach-Object {
        $dst = Join-Path "$sa\Textures" $_.Name
        & $py -I tools\unity\convert_textures.py $_.FullName $dst --max 4096 | Out-Null
        Copy-Item (Join-Path $_.FullName "*.json") $dst -ErrorAction SilentlyContinue
    }
}

Write-Host "== building (Unity batch mode; the log: build\local\unity.log)"
$exe = Join-Path $Out "Windows\FishingSim.exe"
$p = Start-Process -FilePath $Unity -Wait -PassThru -ArgumentList @(
    "-batchmode", "-quit", "-projectPath", "`"$Root\unity`"",
    "-executeMethod", "FishingSim.EditorTools.BuildGame.Build",
    "-buildTarget", "StandaloneWindows64", "-customBuildPath", "`"$exe`"",
    "-logFile", "`"$Out\unity.log`"")
if ($p.ExitCode -ne 0 -or -not (Test-Path $exe)) {
    Select-String -Path "$Out\unity.log" -Pattern "error|Exception|Shader error" | Select-Object -Last 40 | ForEach-Object { $_.Line }
    throw "The build failed (exit $($p.ExitCode)); see build\local\unity.log"
}
Write-Host "== built: $exe"

if ($Smoke) {
    Write-Host "== smoke test (screenshots in build\local\shots)"
    $env:FISHSIM_SMOKE = "1"; $env:FISHSIM_NO_SAVE = "1"
    $env:FISHSIM_SMOKE_DIR = Join-Path $Out "shots"; $env:FISHSIM_SMOKE_QUALITY = "$Quality"; $env:FISHSIM_SMOKE_VIEW = "2"
    $g = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList @("-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900", "-logFile", "`"$Out\smoke.log`"")
    Remove-Item Env:FISHSIM_SMOKE, Env:FISHSIM_NO_SAVE, Env:FISHSIM_SMOKE_DIR, Env:FISHSIM_SMOKE_QUALITY, Env:FISHSIM_SMOKE_VIEW
    Select-String -Path "$Out\smoke.log" -Pattern "SMOKE|Exception" | Where-Object { $_.Line -notmatch "SMOKE JPEG" } | ForEach-Object { $_.Line }
    Write-Host "== smoke exit $($g.ExitCode); screenshots: $Out\shots"
}
if ($Run) { Start-Process -FilePath $exe }
