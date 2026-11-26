#!/usr/bin/env bash
# Local Unity build and test on Linux (Fedora, Ubuntu...): the same steps as .github/workflows/unity.yml.
#   tools/unity/build_local.sh                 # convert + build
#   tools/unity/build_local.sh --smoke         # + automatic test with full-size screenshots (build/local/shots)
#   tools/unity/build_local.sh --skip-convert --run
# UNITY=/path/to/Unity overrides the editor (default: Unity Hub's 6000.0.64f1); QUALITY=0..4 for the smoke test.
set -euo pipefail
cd "$(dirname "$0")/../.."
ROOT="$PWD"
OUT="$ROOT/build/local"
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.0.64f1/Editor/Unity}"
QUALITY="${QUALITY:-3}"
CONVERT=1 SMOKE=0 RUN=0
for a in "$@"; do
  case "$a" in
    --skip-convert) CONVERT=0 ;;
    --smoke) SMOKE=1 ;;
    --run) RUN=1 ;;
  esac
done
mkdir -p "$OUT"
[ -x "$UNITY" ] || { echo "Unity 6000.0.64f1 not found at $UNITY (install it in Unity Hub or set UNITY=...)"; exit 1; }

if [ "$CONVERT" = 1 ]; then
  echo "== converting models and pictures"
  if [ ! -x build/venv/bin/python ]; then
    python3 -m venv build/venv
    build/venv/bin/pip install --quiet pillow numpy
  fi
  PY=build/venv/bin/python
  SA=unity/Assets/StreamingAssets
  $PY -I tools/unity/convert_glb.py assets/fish/meshy "$SA/Models/fish" --fish --max 2048
  $PY -I tools/unity/convert_glb.py assets/models "$SA/Models" --max 4096
  $PY -I tools/unity/convert_glb.py assets/characters "$SA/Models/characters" --max 2048
  $PY -I tools/unity/convert_textures.py assets/characters/textures "$SA/Models/characters/textures" --max 2048
  for d in assets/textures/*/; do
    id=$(basename "$d")
    $PY -I tools/unity/convert_textures.py "$d" "$SA/Textures/$id" --max 4096 > /dev/null
    cp "$d"/*.json "$SA/Textures/$id/" 2>/dev/null || true
  done
fi

echo "== building (Unity batch mode; the log: build/local/unity.log)"
EXE="$OUT/Linux/FishingSim.x86_64"
if ! "$UNITY" -batchmode -quit -projectPath "$ROOT/unity" -executeMethod FishingSim.EditorTools.BuildGame.Build \
     -buildTarget StandaloneLinux64 -customBuildPath "$EXE" -logFile "$OUT/unity.log" || [ ! -x "$EXE" ]; then
  grep -E "error|Exception|Shader error" "$OUT/unity.log" | tail -40
  echo "The build failed; see build/local/unity.log"
  exit 1
fi
echo "== built: $EXE"

if [ "$SMOKE" = 1 ]; then
  echo "== smoke test (screenshots in build/local/shots)"
  FISHSIM_SMOKE=1 FISHSIM_NO_SAVE=1 FISHSIM_SMOKE_DIR="$OUT/shots" FISHSIM_SMOKE_QUALITY="$QUALITY" FISHSIM_SMOKE_VIEW=2 \
    "$EXE" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "$OUT/smoke.log" || true
  grep -E "SMOKE|Exception" "$OUT/smoke.log" | grep -v "SMOKE JPEG" | cut -c1-300
fi
if [ "$RUN" = 1 ]; then "$EXE" & fi
