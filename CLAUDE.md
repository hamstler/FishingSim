# FishingSim

Original RF4-style fishing simulator, Godot 4.3 .NET (C#). Polish README in README.md.

## Build / test
- `dotnet build` (Godot.NET.Sdk 4.3.0; in offline sandboxes restore via the bundled GodotSharp/Tools/nupkgs source).
- Tests: `FISHSIM_NO_SAVE=1 godot --headless --path . res://tests/run_tests.tscn -- <suite>` (core, accounts, progress, world, game, tournaments, village, gear).
- Screenshots: `tests/screenshot.tscn` (use xvfb-run + `--rendering-driver vulkan` with lavapipe, ~30 s per shot; volumetric fog needs Forward+).

## Architecture
Pure logic in src/Core (injected IRng); data as .tres resources; saves via System.Text.Json.Nodes; shaders in shaders/.

## Art direction
- One look only: Nordic (Valheim-like). Native resolution in the root viewport, crisp procedural textures, volumetric fog with sun shafts, AO, bloom, teal-shadow/warm-light grade (GraphicsQuality.ApplyNordic), moonlit ambient at night (SkyController). Pixel art was removed.
- Textures: 1024px per 2 m tile (QHD-sharp), painted on worker threads and cached in user://texcache (World/Props/Textures, bump CacheVersion when a design changes), trilinear anisotropic filtering (BuildingFactory.Pixel). Window starts maximised, canvas_items stretch, 3D at native resolution. Fish skins: src/Graphics/FishSkin paints them per pixel on worker threads (512×256 for fish in the lake, 4096×2048 + normal map for hooked/held/atlas fish via FishModel.Create(..., detailed: true)); screenshot mode 20 = skin sheet, 19 = atlas. Decor: pier festoon lights (Scenery.BuildStringLights), golden fish signs, Graphics/Gulls.
- Performance: never set sky material params or move the sun every frame (re-bakes radiance); cloud drift is the global shader param `cloud_drift`. Profile with FISHSIM_PERF=1 and screenshot mode 13.
- UI pictures: src/Graphics/Icons (items, fish), src/Graphics/Postcard (one composition per map). Screenshot modes 16 (icon sheet), 17 (postcards) and 18 (top-down map shapes) run headless.
- Maps are unique via LocationData Squareness/Lobes/Islands (LakeBed.InsideMetres) and Landmarks (World/Props/Landmarks.cs).
- No GPUParticles: rain is a MultiMesh + shaders/rain.gdshader (GPU particles with motion vectors crashed Godot 4.3 when two viewports drew them).
- Rod: src/Core/Physics/RodBendModel drives RodRig.SetJointAngles (RodData.Action).
- Quality presets in src/Graphics/GraphicsQuality (pause menu button): High (default) = SSAO + volumetric fog + TAA; SSIL, SSR, MSAA and 4 shadow splits only on Ultra; SDFGI off.
- Fish models: src/Fishing/FishBody (per-species build table, iris colours, mouth size) + FishModel (lofted with a rounded nose cap, cached per species at unit length, scaled). Close-up (detailed) fish use shaders/fish_body + fish_fin (one swimming wave from fish_bend.gdshaderinc, instance uniforms wiggle_amount/swim_phase), painted fins (Graphics/FinPainter: rays, membrane, markings), iris discs (EyePainter) under a cornea; head details (mouth, nostrils, gill cover, eye socket) are painted into FishSkin. Screenshot mode 22 = studio close-up (arg = species index, also writes *_head.png). Screenshot mode 21 = side views of 20 species per page (arg = page). Light skins are pre-painted in parallel when a lake is stocked.
- Fish references: the user rejected photo-textured fish ("too photographic"); fish stay painted and game-like, but species with assets/fish/<id>.json (FishReference) are lofted to the side-view profile and painted in the back/flank/belly colours measured from a freely licensed Wikimedia Commons photo (tools/fish_photos: fetch.py search/previews/more via commons thumb.php, cutout.py with --isnet [ISNet ONNX model, tool-only], measure.py, finalize.py; photos are not shipped, CREDITS.md lists them). FishModel.Growth/Proportions: heavier fish are deeper and thicker, young ones slimmer with bigger eyes; the held fish is shown at its real size (FISHSIM_SHOT_WEIGHT sets the weight in screenshot mode 2).
- Hand-built species models: tools/fish_model/<species>.py holds the shapes traced from reference photos (body, head: eye, gape, gill cover; every fin) and paints the textures (`--textures <dir>`: skin unwrapped once round the body + normal map, fins, iris; also rewrites assets/fish/<id>.json for the lake model). Blender builds the model: `blender -b --factory-startup --python tools/fish_model/perch_blender.py -- assets/fish/models/okon.glb [preview_dir]` (quad body, Solidify fins with ray tubes, Mirror for paired fins, eyes under corneas, glTF export with tangents, Cycles previews). Blender 4.0 + python3-numpy come from apt (the environment's setup script installs them). FishAsset loads the glb at run time with GltfDocument; FishModel uses it for close-up fish (BuildAsset, fish_body/fish_fin shaders, CorneaMaterial, growth scaling). perch.py alone still writes a simpler glb without Blender (glb.py). So far: okon.

## Game rules worth knowing
- Day 06-18 lasts 40 real minutes, night 20 (src/Core/DayClock); WorldState.TimeScale is the stool speed-up.
- 66 species in SpeciesCatalog (each needs LatinName, Description, a FishSkin style and an Abundance entry; GearSuite checks they are catchable).
- FightModel: the fish has position/heading/speed (burst speed from body length, limited turn rate), the taut line holds it at LineOut, it takes line only when it beats the drag; fights start with a strike run; drag-load drains stamina. Starter tackle is weak on purpose (junior rod 2.4 kg, reel drag 2 kg).
- Broken tackle is destroyed (Profile.Destroy: snapped rod, hook lost with a broken line, line lost when spooled). The tackle counter hands out the starter kit only when the missing pieces cost more than the purse (Profile.ClaimFreeKit, Shop free-kit button).
- HUD: minimap + clock top-right (UI/Minimap), fight bar under the status line (UI/TensionGauge). Fish atlas: UI/FishAtlas (menu and pause menu).

## Known issues
- Intermittent exit 134 when running several suites in one process.
