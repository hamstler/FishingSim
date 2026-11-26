"""Opens the fisherman's posing (untex_grip.py) live in Blender's window: started once the window is up (at start-up
the glTF importer has no context), the posing then runs step by step with the view redrawn between the steps.

    blender --python tools/assets/untex_grip_live.py -- --live
"""
import os

import bpy

PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "untex_grip.py")


def start():
    win = bpy.context.window_manager.windows[0]
    area = next((a for a in win.screen.areas if a.type == "VIEW_3D"), win.screen.areas[0])
    region = next((r for r in area.regions if r.type == "WINDOW"), None)
    with bpy.context.temp_override(window=win, screen=win.screen, area=area, region=region):
        code = compile(open(PATH, encoding="utf-8").read(), PATH, "exec")
        exec(code, {"__name__": "__main__", "__file__": PATH})
    return None


bpy.app.timers.register(start, first_interval=1.5)
