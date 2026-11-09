# Fish photographs

Cut-out side views of the species (`<id>.webp`, 2048 px wide, RGBA) with their body outline and credit (`<id>.json`),
made with `tools/fish_photos` from freely licensed photographs on Wikimedia Commons. The game loads them at run time
(`src/Fishing/PhotoFish.cs`); the folder has a `.gdignore`, so the editor does not import them. When exporting the
game, add `assets/fish/*` to the export's non-resource files.

Every photo keeps its author and licence in its JSON file and in `CREDITS.md`; the atlas shows the credit.
Images under CC BY-SA remain under CC BY-SA (the cut-outs are adaptations).
