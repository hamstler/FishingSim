#!/usr/bin/env python3
"""Writes data/tackle/hooks/*.tres: the real hook scale from #24 (smallest) to 4/0 (largest).

RealSize > 0 is #n (bigger number = smaller hook), RealSize < 0 is n/0. Size is the game's internal 0.5..10 scale used
by the bite model (hook vs mouth); the old ids keep their old internal size (saves and tests use them). Strength (kg
the hook takes before it opens) and weight grow smoothly with the size. Run from the repository root."""
import math, os

#        real, internal size, id (old ids kept), model
SIZES = [
    (24, 0.5, "hak_24", "Sode"), (22, 0.7, "hak_22", "Sode"), (20, 1.0, "hak1", "Sode"), (18, 1.5, "hak_18", "Sode"),
    (16, 2.0, "hak2", "Sode"), (14, 3.0, "hak3", "Chinu"), (12, 4.0, "hak_12", "Chinu"), (10, 5.0, "hak5", "Chinu"),
    (8, 6.0, "hak6", "Chinu"), (6, 7.0, "hak_6", "Iseama"), (4, 8.0, "hak8", "Iseama"), (2, 8.6, "hak_2", "Iseama"),
    (1, 9.0, "hak_1", "Iseama"), (-1, 9.4, "hak_1_0", "Offset"), (-2, 9.7, "hak_2_0", "Offset"),
    (-3, 9.85, "hak_3_0", "Offset"), (-4, 10.0, "hak10", "Offset"),
]
USE = {
    "Sode": "cienki drut na ochotkę, białe robaki i drobnicę",
    "Chinu": "uniwersalny na płoć, leszcza, karasia i lina",
    "Iseama": "mocny na karpia, lina i większe ryby z dna",
    "Offset": "kuty hak na żywca, szczupaka i suma",
}
BRAND = {"Sode": "Kaminari", "Chinu": "Kaminari", "Iseama": "Kaminari", "Offset": "Ironwood"}

def label(real): return f"#{real}" if real > 0 else f"{-real}/0"

def main():
    n = len(SIZES)
    os.makedirs("data/tackle/hooks", exist_ok=True)
    for i, (real, size, hid, model) in enumerate(SIZES):
        t = i / (n - 1)
        strength = 0.8 * (31.25 ** t)            # 0.8 kg (#24) .. 25 kg (4/0)
        weight = 0.012 * (170 ** t)              # 0.012 g .. 2 g
        wire = 0.16 + 0.84 * t ** 1.3            # mm
        price = int(round(3 + 22 * t ** 1.6))
        level = 1 if i <= 9 else 2 if i <= 12 else 3 if i <= 14 else 4
        name = f"{BRAND[model]} {model} {label(real)} · haczyk"
        desc = (f"Rozmiar {label(real)}, drut {wire:.2f} mm, wytrzymałość {strength:.1f} kg, {weight:.2f} g. "
                f"{USE[model].capitalize()}.").replace(".", ",", 0)
        body = f"""[gd_resource type="Resource" script_class="HookData" load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Data/HookData.cs" id="1"]

[resource]
script = ExtResource("1")
DisplayName = "{name}"
Size = {size}
RealSize = {real}
Strength = {strength:.2f}
WeightG = {weight:.3f}
Price = {price}
RequiredLevel = {level}
Description = "{desc}"
"""
        with open(f"data/tackle/hooks/{hid}.tres", "w", encoding="utf-8") as f:
            f.write(body)
    print(", ".join(f'"{h}"' for _, _, h, _ in SIZES))

if __name__ == "__main__":
    main()
