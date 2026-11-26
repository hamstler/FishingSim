"""Pack the original icons and Blender renders into the Unity inventory atlas."""
import re
from pathlib import Path

from PIL import Image

root = Path(__file__).resolve().parents[2]
icons = {}
for path in sorted((root / "assets/icons").glob("*.png")):
    match = re.fullmatch(r"(?:rod|reel|lure)_(.+)_[0-9a-f]{8}_v\d+", path.stem)
    if match:
        icons[match[1]] = path

renders = root / "build/local/inventory_thumbnails"
required = {f"{folder}/{p.stem}" for folder in ("rods", "reels")
            for p in (root / "assets/models" / folder).glob("*.glb")}
required.update(("line_mono", "line_braid", "line_fluoro", "hook", "bait_worm", "bait_corn", "bait_carp",
                 "wiadro", "kretlik", "swietlik", "oselka"))
for key in required:
    path = renders / (key.replace("/", "_") + ".png")
    if not path.is_file():
        raise SystemExit(f"Missing render: {path}")
    icons[key] = path

keys = sorted(icons)
cell, columns = 96, 16
atlas = Image.new("RGBA", (columns * cell, ((len(keys) + columns - 1) // columns) * cell))
for i, key in enumerate(keys):
    with Image.open(icons[key]) as source:
        image = source.convert("RGBA")
    if key in required:
        box = image.getbbox()
        if box is None:
            raise SystemExit(f"Empty render: {icons[key]}")
        image = image.crop(box)
    image.thumbnail((88, 88), Image.Resampling.LANCZOS)
    at = (i % columns * cell + (cell - image.width) // 2, i // columns * cell + (cell - image.height) // 2)
    atlas.alpha_composite(image, at)

out = root / "unity/Assets/StreamingAssets/Inventory"
atlas.save(out / "gear.png", optimize=True)
(out / "gear.txt").write_text("\n".join(keys) + "\n", encoding="utf8")
print(f"{len(required)} new renders, {len(keys)} thumbnails total")
