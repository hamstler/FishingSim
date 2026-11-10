#!/bin/bash
# Rebuilds every species model and the lake models' outlines:
#   tools/fish_model/build_all.sh [ids...]
# Needs Blender 4.0 with numpy (apt: blender python3-numpy). Each species: tools/fish_model/species/<id>.json ->
# assets/fish/models/<id>.glb and assets/fish/<id>.json; then assets/fish/CREDITS.md.
set -e
cd "$(dirname "$0")/../.."
ids="$*"
[ -z "$ids" ] && ids=$(ls tools/fish_model/species | sed 's/\.json$//')
for id in $ids; do
  blender -b --factory-startup --python-exit-code 1 --python tools/fish_model/fish_blender.py -- \
    tools/fish_model/species/$id.json assets/fish/models/$id.glb > /dev/null 2>&1 || { echo "$id failed"; exit 1; }
  python3 tools/fish_model/fishgen.py --reference tools/fish_model/species/$id.json assets/fish/$id.json
  echo "$id: $(du -k assets/fish/models/$id.glb | cut -f1) KB"
done
python3 tools/fish_model/credits.py
