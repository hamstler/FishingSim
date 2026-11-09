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
- Materials: nearest-filtered 32px procedural textures, toon diffuse (BuildingFactory.Pixel); shaders sky/terrain/water/foliage/grass in shaders/.
- Quality presets in src/Graphics/GraphicsQuality (pause menu button); PixelArtView is skipped headless, so tests do not cover it.

## Known issues
- Intermittent exit 134 when running several suites in one process.
