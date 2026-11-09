# FishingSim

Original RF4-style fishing simulator, Godot 4.3 .NET (C#). Polish README in README.md.

## Build / test
- `dotnet build` (Godot.NET.Sdk 4.3.0; in offline sandboxes restore via the bundled GodotSharp/Tools/nupkgs source).
- Tests: `FISHSIM_NO_SAVE=1 godot --headless --path . res://tests/run_tests.tscn -- <suite>` (core, accounts, progress, world, game, tournaments, village, gear).
- Screenshots: `tests/screenshot.tscn` (use xvfb-run + `--rendering-driver vulkan` with lavapipe, ~30 s per shot; the pixel-art view needs Forward+).

## Architecture
Pure logic in src/Core (injected IRng); data as .tres resources; saves via System.Text.Json.Nodes; shaders in shaders/.

## Art direction
- Medieval pixel art 3D: low-res SubViewport + nearest upscale + palette/dither/outline post (src/Graphics/PixelArtView, shaders/pixel_post).
- Three styles (pause menu "Styl", GraphicsQuality.Style, saved as `art_style`): Nordic (default, Valheim-like: native res, crisp textures, no palette/outlines, volumetric fog with sun shafts, AO, bloom, teal-shadow/warm-light grade in GraphicsQuality.ApplyNordic, moonlit ambient in SkyController), PixelArt (~360 lines, warm 61-colour palette in pixel_post, illustrated sky `stylize`, no volumetric fog) and Sharp (native res).
- Outlines: full-screen depth/normal pass shaders/outline.gdshader on the pixel camera (Forward+ only).
- Textures: nearest-filtered 256px/2m procedural, toon diffuse (BuildingFactory.Pixel). Fish skins: src/Graphics/FishSkin. Decor: pier festoon lights (Scenery.BuildStringLights), golden fish signs, Graphics/Gulls.
- Performance: never set sky material params or move the sun every frame (re-bakes radiance); cloud drift is the global shader param `cloud_drift`. Profile with FISHSIM_PERF=1 and screenshot mode 13.
- UI pictures: src/Graphics/Icons (items, fish), src/Graphics/Postcard (one composition per map). Screenshot modes 16 (icon sheet), 17 (postcards) and 18 (top-down map shapes) run headless.
- Maps are unique via LocationData Squareness/Lobes/Islands (LakeBed.InsideMetres) and Landmarks (World/Props/Landmarks.cs).
- No GPUParticles: rain is a MultiMesh + shaders/rain.gdshader (particles in two viewports with motion vectors crash Godot 4.3).
- Rod: src/Core/Physics/RodBendModel drives RodRig.SetJointAngles (RodData.Action).
- Quality presets in src/Graphics/GraphicsQuality (pause menu button); PixelArtView is skipped headless, so tests do not cover it.

## Known issues
- Intermittent exit 134 when running several suites in one process.
