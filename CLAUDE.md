# FishingSim

Original RF4-style fishing simulator, Godot 4.3 .NET (C#). Polish README in README.md.

## Build / test
- `dotnet build` (Godot.NET.Sdk 4.3.0; in offline sandboxes restore via the bundled GodotSharp/Tools/nupkgs source).
- Tests: `FISHSIM_NO_SAVE=1 godot --headless --path . res://tests/run_tests.tscn -- <suite>` (core, accounts, progress, world, game, tournaments, village).
- Screenshots: `tests/screenshot.tscn` (use xvfb-run + vulkan/lavapipe).

## Architecture
Pure logic in src/Core (injected IRng); data as .tres resources; saves via System.Text.Json.Nodes; shaders in shaders/.

## Known issues
- This tree is the v2 snapshot: the realism/graphics pass (src/Graphics, terrain/sky/water shaders) was lost in a sandbox reset and is NOT included.
- Intermittent exit 134 when running several suites in one process.
