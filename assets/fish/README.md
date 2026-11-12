# Fish reference measurements

`<id>.json` holds what was measured on a real fish of the species in a freely licensed photograph on Wikimedia
Commons: the side-view body outline without the fins (`body_start`, `body_end`, `outline`, `aspect`, from
`tools/fish_photos/cutout.py`) and the colours of the back, flank and belly (`palette`, from `measure.py`), with the
photographer's credit. The game lofts the painted model of the species to this outline and paints its skin in these
colours (`src/Fishing/FishReference.cs`), so it keeps the game's look while matching the species. The photographs are
not part of the game; `CREDITS.md` lists them.

The game reads these files and the species models (`models/<id>.glb`) at run time. The models have `.import` files
with `importer="keep"` (written by `tools/fish_model/fish_blender.py`), so the editor does not import them as scenes,
and the export presets (`export_presets.cfg`) include `assets/fish/*`, so exports carry them as they are. Do not put a
`.gdignore` here: an export skips folders that have one, and the exported game would have no species models.
