# Fish reference measurements

`<id>.json` holds what was measured on a real fish of the species in a freely licensed photograph on Wikimedia
Commons: the side-view body outline without the fins (`body_start`, `body_end`, `outline`, `aspect`, from
`tools/fish_photos/cutout.py`) and the colours of the back, flank and belly (`palette`, from `measure.py`), with the
photographer's credit. The game lofts the painted model of the species to this outline and paints its skin in these
colours (`src/Fishing/FishReference.cs`), so it keeps the game's look while matching the species. The photographs are
not part of the game; `CREDITS.md` lists them.

The folder has a `.gdignore`, so the editor does not import the files; the game reads them at run time. When
exporting the game, add `assets/fish/*.json` to the export's non-resource files.
