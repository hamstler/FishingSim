# Meshy AI fish models

The `<id>.glb` models in this folder (all 66 species) were generated with Meshy AI (https://www.meshy.ai, image to
3D) by the project's author on a paid (Premium) plan, from their own reference pictures, then decimated and
re-encoded with tools/assets/meshy_fish.py. Under Meshy's terms of service, assets generated on a paid plan belong
to the user who generated them; the author releases them with the project under GPL-3.0-or-later.

The same holds for assets/models/meshy_fisherman.glb and assets/models/meshy_spinning_rod.glb (the standing
anglers on the bank), converted with `tools/assets/meshy_fish.py --file IN OUT --triangles N [--keep-mr]`.
And for the player's rods and reels: assets/models/rods/*.glb, assets/models/reels/*.glb (+ .json frames),
assets/models/meshy_rod2_player.glb and assets/models/meshy_reel.glb (converted the same way, then laid into the
rod rig and cut by tools/assets/meshy_rod_split.py).
