#!/usr/bin/env python3
"""Writes the game's catalogue of spinning lures and lines (data/tackle/baits/l_*.tres, data/tackle/lines/g_*.tres)
and src/Progress/GeneratedTackle.cs with their ids, from the tables below. Run from the repository root:

    python3 -I tools/tackle/generate_tackle.py

Every lure is a BaitData (Kind = Lure, Reusable) with its family (AffinityAs), model (Shape), colours, pattern, size,
weight and how it runs (SinkScale/SinkOffset/DepthScale/DepthOffset); every line a LineData with its kind, diameter,
colour, strength, visibility and stretch. The hand-made items already in the game keep their files; this script
also gives them colours and diameters (KEEP tables)."""
import os
import re
import sys

ROOT = os.getcwd()
BAITS = os.path.join(ROOT, "data", "tackle", "baits")
LINES = os.path.join(ROOT, "data", "tackle", "lines")
OUT_CS = os.path.join(ROOT, "src", "Progress", "GeneratedTackle.cs")

# ---------------------------------------------------------------- colours: (name, main, accent, pattern, metallic)
SILVER = ("srebrna", (0.86, 0.87, 0.9), (0.85, 0.12, 0.1), "plain", True)
GOLD = ("złota", (0.92, 0.74, 0.32), (0.85, 0.12, 0.1), "plain", True)
COPPER = ("miedziana", (0.82, 0.48, 0.28), (0.15, 0.1, 0.08), "plain", True)
BLACK_DOTS = ("czarna w żółte kropki", (0.07, 0.07, 0.08), (1.0, 0.85, 0.1), "dots", False)
SILVER_DOTS = ("srebrna w czerwone kropki", (0.86, 0.87, 0.9), (0.85, 0.12, 0.1), "dots", True)
TIGER = ("tygrysia fluo", (0.45, 0.9, 0.15), (1.0, 0.55, 0.08), "bars", False)
SILVER_RED = ("srebrno-czerwona", (0.86, 0.87, 0.9), (0.85, 0.12, 0.1), "halves", True)
PERCH_SPOON = ("okoniowa", (0.55, 0.62, 0.2), (0.95, 0.75, 0.15), "bars", False)
BLUE_HERRING = ("niebieska śledziowa", (0.25, 0.45, 0.75), (0.86, 0.87, 0.9), "halves", True)
GOLD_RED = ("złoto-czerwona", (0.92, 0.74, 0.32), (0.85, 0.12, 0.1), "stripes", True)

PERCH = ("okoń", (0.42, 0.55, 0.18), (0.95, 0.78, 0.2), "bars", False)
ROACH = ("płoć", (0.3, 0.38, 0.5), (0.86, 0.88, 0.9), "plain", False)
FIRETIGER = ("tygrys fluo", (0.4, 0.85, 0.2), (1.0, 0.7, 0.1), "bars", False)
TROUT = ("pstrąg", (0.45, 0.42, 0.25), (0.95, 0.72, 0.72), "dots", False)
BLACK_GOLD = ("czarno-złoty", (0.08, 0.08, 0.08), (0.95, 0.75, 0.25), "plain", False)
PIKE = ("szczupaczek", (0.35, 0.45, 0.2), (0.88, 0.86, 0.6), "dots", False)
RED_WOB = ("czerwony", (0.8, 0.12, 0.1), (0.95, 0.9, 0.85), "plain", False)
WHITE_RED = ("biały z czerwoną głową", (0.95, 0.95, 0.93), (0.85, 0.1, 0.08), "redhead", False)
FROG = ("żaba", (0.3, 0.6, 0.2), (0.95, 0.9, 0.4), "dots", False)
BLACK = ("czarny", (0.08, 0.08, 0.09), (0.4, 0.4, 0.45), "plain", False)

PEARL = ("perłowa", (0.93, 0.92, 0.88), (0.75, 0.75, 0.8), "glitter", False)
CHARTREUSE = ("seledynowa fluo", (0.75, 1.0, 0.15), (0.2, 0.4, 0.1), "glitter", False)
MOTOR_OIL = ("motor oil", (0.35, 0.3, 0.12), (0.9, 0.7, 0.2), "glitter", False)
PINK = ("różowa fluo", (1.0, 0.4, 0.65), (1.0, 0.95, 0.95), "plain", False)
SILVER_GLITTER = ("srebrny brokat", (0.8, 0.82, 0.86), (0.3, 0.35, 0.45), "glitter", False)
YELLOW = ("żółty", (1.0, 0.85, 0.1), (0.85, 0.3, 0.1), "plain", False)
WHITE = ("biały", (0.95, 0.95, 0.93), (0.8, 0.8, 0.8), "plain", False)
GREEN_FLUO = ("zielony fluo", (0.3, 0.95, 0.3), (0.1, 0.4, 0.1), "plain", False)
BLACK_SOFT = ("czarny", (0.08, 0.08, 0.08), (0.3, 0.1, 0.4), "glitter", False)
RED = ("czerwony", (0.8, 0.1, 0.08), (0.3, 0.05, 0.05), "plain", False)
BROWN = ("brązowy", (0.45, 0.3, 0.18), (0.2, 0.12, 0.06), "plain", False)
GREEN = ("zielony", (0.3, 0.42, 0.18), (0.15, 0.2, 0.08), "plain", False)


def slug(text):
    table = str.maketrans("ąćęłńóśźżĄĆĘŁŃÓŚŹŻ ,-", "acelnoszzACELNOSZZ___")
    s = text.translate(table).lower()
    s = re.sub(r"[^a-z0-9_]", "", s)
    return re.sub(r"_+", "_", s).strip("_")


def num(x):
    return f"{x:g}".replace(".", ",")


def col(c):
    return f"Color({c[0]:.3g}, {c[1]:.3g}, {c[2]:.3g}, 1)"


LURES = []


def lure(family, shape, title, colour, size_cm, weight_g, price, level, desc, sink_scale=1.0, sink_offset=0.0,
         depth_scale=1.0, depth_offset=0.0, affinity=1.0):
    name, main, accent, pattern, metal = colour
    ident = "l_" + slug(f"{family}_{shape}_{num(size_cm if family != 'spinner' else weight_g)}_{name}")
    LURES.append(dict(id=ident, family=family, shape=shape, name=f"{title}, {name}", main=main, accent=accent,
                      pattern=pattern, metal=metal, size=size_cm, weight=weight_g, price=price, level=level,
                      desc=desc, sink_scale=sink_scale, sink_offset=sink_offset, depth_scale=depth_scale,
                      depth_offset=depth_offset, affinity=affinity))


# ---------------------------------------------------------------- spinners (obrotówki)
for n, g, cm, price, lvl in [(1, 3, 3.5, 25, 1), (2, 5, 4.5, 32, 1), (3, 8, 5.5, 40, 2)]:
    for c in [SILVER, GOLD, COPPER, BLACK_DOTS, SILVER_DOTS, TIGER]:
        lure("spinner", "round", f"Obrotówka okrągła nr {n}", c, cm, g, price, lvl,
             f"Okrągłe skrzydełko wiruje szeroko i pracuje już przy wolnym zwijaniu. {num(g)} g, {num(cm)} cm.")
for n, g, cm, price, lvl in [(2, 6, 5.0, 36, 2), (3, 9, 6.0, 45, 2)]:
    for c in [SILVER, GOLD, COPPER]:
        lure("spinner", "long", f"Obrotówka wydłużona nr {n}", c, cm, g, price, lvl,
             f"Wąskie skrzydełko wiruje blisko drutu: lubi szybsze prowadzenie i nurt. {num(g)} g.",
             depth_scale=1.2)
for n, g, cm, price, lvl in [(2, 6, 5.5, 42, 2), (3, 10, 7.0, 52, 3)]:
    for c in [("srebrna z czerwonym chwostem", (0.86, 0.87, 0.9), (0.85, 0.12, 0.1), "plain", True),
              ("czarna z żółtym chwostem", (0.07, 0.07, 0.08), (1.0, 0.85, 0.1), "plain", False)]:
        lure("spinner", "tail", f"Obrotówka z chwostem nr {n}", c, cm, g, price, lvl,
             f"Chwost z włosia za kotwiczką faluje nawet przy wolnym prowadzeniu: szczupak i okoń. {num(g)} g.",
             affinity=1.1)
for g, cm, c, price, lvl in [(7, 5.0, COPPER, 45, 2), (12, 6.0, SILVER, 55, 3)]:
    lure("spinner", "headed", "Obrotówka z ołowianą główką", c, cm, g, price, lvl,
         f"Ołowiana główka przed skrzydełkiem: daleki rzut i prowadzenie przy dnie. {num(g)} g.",
         sink_scale=1.8, depth_scale=1.6, affinity=1.05)

# ---------------------------------------------------------------- spoons (wahadłówki)
for g, cm, price, lvl in [(10, 5.0, 30, 1), (18, 6.5, 42, 2), (25, 8.0, 55, 3)]:
    for c in [SILVER, GOLD, COPPER, SILVER_RED, PERCH_SPOON]:
        lure("spoon", "classic", f"Wahadłówka klasyczna {num(g)} g", c, cm, g, price, lvl,
             f"Wygięta blacha kołysze się na boki i błyska: szczupak, okoń, boleń. {num(cm)} cm.",
             sink_scale=0.8 + g / 50.0)
for g, cm, price, lvl in [(14, 6.0, 40, 2), (22, 7.5, 50, 3)]:
    for c in [SILVER, BLUE_HERRING]:
        lure("spoon", "narrow", f"Wahadłówka wąska {num(g)} g", c, cm, g, price, lvl,
             "Smukła i ciężka: rzuca się nią najdalej, opada szybko, pracuje drobno. Na bolenia i pstrągi.",
             sink_scale=1.3, depth_scale=1.1)
for g, cm, c, price, lvl in [(8, 5.5, GOLD, 32, 1), (15, 7.0, GOLD_RED, 45, 2), (15, 7.0, COPPER, 45, 2)]:
    lure("spoon", "wide", f"Wahadłówka szeroka {num(g)} g", c, cm, g, price, lvl,
         "Szeroka blacha chodzi płytko i wolno, szerokim łukiem: dobra nad zielskiem.",
         sink_scale=0.6, depth_scale=0.6)

# ---------------------------------------------------------------- wobblers (woblery)
for cm, g, depth, price, lvl in [(5, 4, 0.6, 60, 1), (7, 7, 1.0, 80, 2), (9, 11, 1.2, 100, 3)]:
    for c in [PERCH, ROACH, FIRETIGER, TROUT, BLACK_GOLD]:
        lure("wobbler", "minnow", f"Wobler pływający {num(cm)} cm", c, cm, g, price, lvl,
             f"Smukła rybka ze sterem: wypływa, gdy stoi, schodzi na ok. {num(round(0.8 * depth, 1))} m przy zwijaniu.",
             depth_scale=depth)
lure("wobbler", "minnow", "Wobler szczupakowy 11 cm", PIKE, 11, 16, 140, 4,
     "Duża rybka na szczupaki i sandacze: schodzi do 2 m.", depth_scale=1.6)
for cm, g, depth, price, lvl in [(5, 8, 2.0, 85, 2), (7, 14, 2.4, 110, 3)]:
    for c in [PERCH, FIRETIGER, RED_WOB, ROACH]:
        lure("wobbler", "crank", f"Wobler głęboko nurkujący {num(cm)} cm", c, cm, g, price, lvl,
             "Krępy, z dużym sterem: szybko schodzi głęboko i mocno pracuje. Na sandacze i okonie przy dnie.",
             depth_scale=depth)
for c in [PERCH, ROACH, FIRETIGER]:
    lure("wobbler", "jerk", "Jerk tonący 10 cm", c, 10, 22, 130, 4,
         "Bez steru: tonie i ucieka w bok przy każdym szarpnięciu (PPM). Na szczupaki.",
         sink_offset=0.55, depth_scale=0.4)
for c in [WHITE_RED, FROG, BLACK]:
    lure("wobbler", "popper", "Popper powierzchniowy 6 cm", c, 6, 9, 70, 2,
         "Wklęsły pyszczek chlapie po powierzchni przy podszarpnięciach: bolenie, klenie i szczupaki o zmierzchu.",
         depth_scale=0.0)

# ---------------------------------------------------------------- soft baits (gumy na główkach)
for cm, g, price, lvl in [(5, 5, 15, 1), (7, 10, 20, 1), (10, 15, 28, 2)]:
    for c in [PEARL, CHARTREUSE, MOTOR_OIL, PINK, SILVER_GLITTER]:
        lure("softbait", "ripper", f"Ripper {num(cm)} cm, główka {num(g)} g", c, cm, g, price, lvl,
             "Guma z ogonkiem-kopytkiem na jigowej główce: spada do dna i bierze na opadzie (metoda stop and go).",
             sink_scale=0.6 + g / 15.0)
for cm, g, price, lvl in [(4, 3, 12, 1), (6, 7, 16, 1)]:
    for c in [YELLOW, WHITE, GREEN_FLUO, BLACK_SOFT]:
        lure("softbait", "twister", f"Twister {num(cm)} cm, główka {num(g)} g", c, cm, g, price, lvl,
             "Sierpowaty ogonek wije się przy najwolniejszym prowadzeniu: okonie i sandacze.",
             sink_scale=0.6 + g / 15.0)
for c in [RED, BROWN]:
    lure("softbait", "worm", "Gumowy robak 8 cm, główka 5 g", c, 8, 5, 18, 1,
         "Wije się przy dnie jak prawdziwy robak: leszcze, okonie, klenie.", sink_scale=0.9)
for c in [BROWN, GREEN]:
    lure("softbait", "craw", "Gumowy rak 6 cm, główka 8 g", c, 6, 8, 22, 2,
         "Rak z szczypcami podskakujący po dnie: duże okonie, klenie i sandacze.", sink_scale=1.1)
lure("softbait", "ripper", "Ripper szczupakowy 14 cm, główka 25 g", PEARL, 14, 25, 45, 4,
     "Duża guma na szczupaki i sandacze z głębokiej wody.", sink_scale=1.8)

# ---------------------------------------------------------------- flies (muchy)
for title, c in [("jętka", ("jasna", (0.85, 0.82, 0.6), (0.7, 0.65, 0.5), "plain", False)),
                 ("chruścik", ("brązowy", (0.45, 0.3, 0.15), (0.6, 0.45, 0.25), "plain", False)),
                 ("komar", ("szary", (0.45, 0.45, 0.45), (0.75, 0.75, 0.78), "plain", False)),
                 ("mrówka", ("czarna", (0.06, 0.05, 0.05), (0.3, 0.25, 0.2), "plain", False))]:
    lure("fly", "dry", f"Mucha sucha: {title}", c, 1.5, 0.3, 12, 1,
         "Unosi się na powierzchni jak owad, który wpadł do wody: pstrągi, klenie, jelce.")
for title, c in [("złotogłówka", ("zajęcza", (0.55, 0.45, 0.32), (0.95, 0.75, 0.25), "redhead", False)),
                 ("zielona larwa", ("zielona", (0.35, 0.6, 0.25), (0.2, 0.3, 0.15), "plain", False)),
                 ("czarna", ("czarna", (0.08, 0.08, 0.08), (0.95, 0.75, 0.25), "redhead", False))]:
    lure("fly", "nymph", f"Nimfa: {title}", c, 1.8, 0.8, 14, 1,
         "Tonie powoli i płynie pół metra pod powierzchnią jak larwa owada.", sink_offset=0.25, depth_offset=0.5)
for title, c in [("rudy", ("ruda", (0.7, 0.35, 0.15), (0.9, 0.7, 0.3), "plain", False)),
                 ("szary", ("szara", (0.5, 0.5, 0.5), (0.8, 0.8, 0.75), "plain", False)),
                 ("zielony", ("zielona", (0.35, 0.55, 0.25), (0.9, 0.85, 0.5), "plain", False))]:
    lure("fly", "wet", f"Mucha mokra: {title}", c, 2.0, 0.6, 14, 2,
         "Tonie tuż pod powierzchnię i płynie z nurtem: lipienie i pstrągi.", sink_offset=0.15, depth_offset=0.3)
for c in [("biały", (0.95, 0.95, 0.92), (0.75, 0.75, 0.8), "plain", False),
          ("oliwkowy", (0.45, 0.48, 0.2), (0.85, 0.8, 0.4), "plain", False),
          ("czarny", (0.08, 0.08, 0.08), (0.5, 0.2, 0.6), "plain", False),
          ("żółty", (1.0, 0.85, 0.15), (0.9, 0.3, 0.1), "plain", False)]:
    lure("fly", "streamer", "Streamer 6 cm", c, 6, 2.5, 22, 2,
         "Długie pióra udają małą rybkę: pstrągi, okonie i szczupaki.", sink_offset=0.4, depth_offset=0.9, affinity=1.1)

# ---------------------------------------------------------------- lines
LINE_LIST = []


def line(kind, diameter, colour_name, colour, level):
    d = diameter
    if kind == "mono":
        strength = 105 * d * d
        visibility = {"przezroczysta": 0.85, "zielona": 0.8, "brązowa": 0.82, "żółta fluo": 1.15}[colour_name] + d * 0.5
        stretch = 0.55
        title = f"Żyłka {num(d)} mm, {colour_name}"
        price = int(8 + strength * 4)
        desc = "Rozciągliwa żyłka pochłania zrywy ryby i unosi się lekko na wodzie."
        ident = f"g_zylka_{int(round(d * 100)):03d}_{slug(colour_name)}"
        k = 0
    elif kind == "braid":
        strength = 520 * d * d
        visibility = {"zielona": 1.05, "szara": 1.0, "żółta fluo": 1.3, "pomarańczowa fluo": 1.35,
                      "wielokolorowa": 1.25}[colour_name] + d * 0.5
        stretch = 0.05
        splot = "8-splotowa" if d <= 0.12 else "4-splotowa"
        title = f"Plecionka {splot} {num(d)} mm, {colour_name}"
        price = int(20 + strength * 6)
        desc = "Bardzo mocna jak na swoją grubość i nierozciągliwa: daleki rzut i pewne zacięcie, ale zrywy idą prosto w wędkę."
        ident = f"g_plecionka_{int(round(d * 100)):03d}_{slug(colour_name)}"
        k = 1
    else:
        strength = 90 * d * d
        visibility = 0.62 + d * 0.4
        stretch = 0.4
        title = f"Fluorocarbon {num(d)} mm"
        price = int(30 + strength * 9)
        desc = "Prawie niewidoczny w wodzie, twardy i tonący: dla płochliwych ryb."
        ident = f"g_fluorocarbon_{int(round(d * 100)):03d}"
        k = 2
    LINE_LIST.append(dict(id=ident, kind=k, name=title, strength=round(strength, 1), visibility=round(visibility, 2),
                          stretch=stretch, diameter=d, colour=colour, price=price, level=level, desc=desc))


CLEAR = (0.9, 0.92, 0.9)
for d in [0.12, 0.14, 0.16, 0.18, 0.20, 0.22, 0.25, 0.28, 0.30, 0.35, 0.40]:
    line("mono", d, "przezroczysta", CLEAR, 1 if d <= 0.25 else 2)
for d in [0.16, 0.20, 0.25, 0.30, 0.35]:
    line("mono", d, "zielona", (0.45, 0.62, 0.42), 1 if d <= 0.25 else 2)
for d in [0.18, 0.22, 0.28]:
    line("mono", d, "żółta fluo", (0.95, 0.95, 0.25), 2)
for d in [0.20, 0.25]:
    line("mono", d, "brązowa", (0.55, 0.42, 0.28), 1)
for d in [0.06, 0.08, 0.10, 0.12, 0.14, 0.16, 0.20, 0.25, 0.30]:
    line("braid", d, "zielona", (0.25, 0.42, 0.22), 2 if d <= 0.16 else 3)
for d in [0.08, 0.12, 0.16]:
    line("braid", d, "szara", (0.55, 0.57, 0.6), 2)
for d in [0.10, 0.14, 0.20]:
    line("braid", d, "żółta fluo", (0.95, 0.92, 0.15), 2)
for d in [0.12, 0.16]:
    line("braid", d, "pomarańczowa fluo", (1.0, 0.5, 0.1), 3)
for d in [0.14, 0.20, 0.25]:
    line("braid", d, "wielokolorowa", (0.9, 0.3, 0.3), 3)
for d in [0.18, 0.22, 0.26, 0.30, 0.35]:
    line("fluoro", d, "", (0.85, 0.9, 0.95), 3 if d <= 0.26 else 4)

# The hand-made items already in the game: their look (lures) and diameter and colour (lines).
KEEP_LURES = {
    "blystka_obrotowa": dict(shape="round", main=(0.92, 0.74, 0.32), accent=(0.85, 0.12, 0.1), pattern="plain", metal=True, size=4.5, weight=4),
    "blystka_mosiezna": dict(shape="classic", main=(0.9, 0.72, 0.35), accent=(0.85, 0.12, 0.1), pattern="plain", metal=True, size=6, weight=14),
    "blystka_srebrna": dict(shape="classic", main=(0.88, 0.89, 0.92), accent=(0.85, 0.12, 0.1), pattern="plain", metal=True, size=6, weight=14),
    "rybka_kora": dict(shape="minnow", main=(0.55, 0.38, 0.24), accent=(0.75, 0.6, 0.4), pattern="plain", metal=False, size=6, weight=6),
    "rybka_drewniana": dict(shape="minnow", main=(0.42, 0.55, 0.18), accent=(0.95, 0.78, 0.2), pattern="bars", metal=False, size=6, weight=6),
    "piorko_olowiane": dict(shape="ripper", main=(0.95, 0.95, 0.9), accent=(0.8, 0.3, 0.25), pattern="plain", metal=False, size=6, weight=8),
    "skorka_wegorza": dict(shape="worm", main=(0.3, 0.32, 0.2), accent=(0.2, 0.22, 0.13), pattern="plain", metal=False, size=8, weight=6),
    "mucha_kogut": dict(shape="dry", main=(0.25, 0.2, 0.15), accent=(0.75, 0.45, 0.2), pattern="plain", metal=False, size=1.8, weight=0.3),
    "mucha_zajac": dict(shape="nymph", main=(0.55, 0.45, 0.32), accent=(0.7, 0.6, 0.45), pattern="plain", metal=False, size=1.8, weight=0.8),
}
KEEP_NYMPH = {"mucha_zajac": dict(sink_offset=0.25, depth_offset=0.5)}
KEEP_LINES = {
    "zylka016": (0.16, (0.88, 0.86, 0.8)), "zylka025": (0.25, (0.86, 0.84, 0.76)), "plecionka030": (0.3, (0.82, 0.74, 0.55)),
    "jelito_cienkie": (0.2, (0.88, 0.7, 0.42)), "jelito_mocne": (0.32, (0.85, 0.66, 0.38)), "jedwab_skrecany": (0.22, (0.97, 0.95, 0.9)),
    "plecionka_konopna": (0.4, (0.6, 0.58, 0.4)), "plecionka_jedwabna": (0.28, (0.97, 0.95, 0.9)), "plecionka_smolowana": (0.35, (0.18, 0.16, 0.14)),
}


def q(text):
    return '"' + text.replace('"', '\\"') + '"'


def write_lure(l):
    body = f"""[gd_resource type="Resource" script_class="BaitData" load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Data/BaitData.cs" id="1"]

[resource]
script = ExtResource("1")
DisplayName = {q(l['name'])}
Kind = 3
AffinityAs = "{l['family']}"
AffinityScale = {l['affinity']:g}
Reusable = true
StartCount = 1
PackSize = 1
Price = {l['price']}
RequiredLevel = {l['level']}
Description = {q(l['desc'])}
Shape = "{l['shape']}"
LureColor = {col(l['main'])}
AccentColor = {col(l['accent'])}
Pattern = "{l['pattern']}"
Metallic = {'true' if l['metal'] else 'false'}
SizeCm = {l['size']:g}
WeightG = {l['weight']:g}
SinkScale = {l['sink_scale']:.3g}
SinkOffset = {l['sink_offset']:g}
DepthScale = {l['depth_scale']:g}
DepthOffset = {l['depth_offset']:g}
"""
    with open(os.path.join(BAITS, l["id"] + ".tres"), "w", encoding="utf-8") as f:
        f.write(body)


def write_line(l):
    body = f"""[gd_resource type="Resource" script_class="LineData" load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Data/LineData.cs" id="1"]

[resource]
script = ExtResource("1")
DisplayName = {q(l['name'])}
Kind = {l['kind']}
Strength = {l['strength']:g}
Visibility = {l['visibility']:g}
Stretch = {l['stretch']:g}
Diameter = {l['diameter']:g}
LineColor = {col(l['colour'])}
Price = {l['price']}
RequiredLevel = {l['level']}
Description = {q(l['desc'])}
"""
    with open(os.path.join(LINES, l["id"] + ".tres"), "w", encoding="utf-8") as f:
        f.write(body)


def patch(path, fields):
    """Sets (or adds) properties in an existing .tres resource."""
    with open(path, encoding="utf-8") as f:
        text = f.read()
    for key, value in fields.items():
        line_ = f"{key} = {value}"
        if re.search(rf"^{key} = .*$", text, flags=re.M):
            text = re.sub(rf"^{key} = .*$", line_, text, flags=re.M)
        else:
            text = text.rstrip("\n") + "\n" + line_ + "\n"
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)


def main():
    ids = [l["id"] for l in LURES]
    if len(ids) != len(set(ids)):
        sys.exit("duplicate lure ids: " + ", ".join(sorted({i for i in ids if ids.count(i) > 1})))
    lids = [l["id"] for l in LINE_LIST]
    if len(lids) != len(set(lids)):
        sys.exit("duplicate line ids")
    for old in os.listdir(BAITS):
        if old.startswith("l_") and old.endswith(".tres") and old[:-5] not in ids:
            os.remove(os.path.join(BAITS, old))
    for old in os.listdir(LINES):
        if old.startswith("g_") and old.endswith(".tres") and old[:-5] not in lids:
            os.remove(os.path.join(LINES, old))
    for l in LURES:
        write_lure(l)
    for l in LINE_LIST:
        write_line(l)
    for ident, look in KEEP_LURES.items():
        fields = dict(Shape=f'"{look["shape"]}"', LureColor=col(look["main"]), AccentColor=col(look["accent"]),
                      Pattern=f'"{look["pattern"]}"', Metallic="true" if look["metal"] else "false",
                      SizeCm=f"{look['size']:g}", WeightG=f"{look['weight']:g}")
        for k, v in KEEP_NYMPH.get(ident, {}).items():
            fields["SinkOffset" if k == "sink_offset" else "DepthOffset"] = f"{v:g}"
        patch(os.path.join(BAITS, ident + ".tres"), fields)
    for ident, (d, c) in KEEP_LINES.items():
        patch(os.path.join(LINES, ident + ".tres"), dict(Diameter=f"{d:g}", LineColor=col(c)))
    with open(OUT_CS, "w", encoding="utf-8") as f:
        f.write("// Generated by tools/tackle/generate_tackle.py: do not edit by hand.\n")
        f.write("namespace FishingSim.Progress;\n\n")
        f.write("/// <summary>The catalogue's spinning lures and lines (ids of data/tackle/baits/l_*.tres and lines/g_*.tres).</summary>\n")
        f.write("public static class GeneratedTackle\n{\n")
        f.write("    public static readonly string[] Lures =\n    {\n")
        for i in range(0, len(ids), 4):
            f.write("        " + " ".join(f'"{x}",' for x in ids[i:i + 4]) + "\n")
        f.write("    };\n\n    public static readonly string[] Lines =\n    {\n")
        for i in range(0, len(lids), 4):
            f.write("        " + " ".join(f'"{x}",' for x in lids[i:i + 4]) + "\n")
        f.write("    };\n}\n")
    fams = {}
    for l in LURES:
        fams[l["family"]] = fams.get(l["family"], 0) + 1
    print(f"{len(LURES)} lures ({', '.join(f'{k} {v}' for k, v in fams.items())}), {len(LINE_LIST)} lines")


if __name__ == "__main__":
    main()
