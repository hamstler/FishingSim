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

# Invented makers (no real trademarks): each evokes a style and an origin, not a real company's name.
BRANDS = {
    ("spinner", "round"): "Loirette Ronde", ("spinner", "long"): "Loirette Saule", ("spinner", "tail"): "Loirette Plumet",
    ("spinner", "headed"): "Loirette Plombée", ("spinner", "tailspinner"): "Loirette Comète",
    ("spinner", "spinnerbait"): "Bayou Bill Reed Blade",
    ("spoon", "classic"): "Nordvik Fjell", ("spoon", "narrow"): "Nordvik Sild", ("spoon", "wide"): "Nordvik Brede",
    ("spoon", "jig"): "Nordvik Stål Jig",
    ("wobbler", "minnow"): "Saimaa Craft Salmi", ("wobbler", "crank"): "Saimaa Craft Syvä",
    ("wobbler", "jerk"): "Saimaa Craft Hauki Jerk", ("wobbler", "shad"): "Saimaa Craft Kuha Shad",
    ("wobbler", "popper"): "Bayou Bill Chug", ("wobbler", "vib"): "Kawase Shindō Vib",
    ("wobbler", "pencil"): "Kawase Kaze Walker", ("wobbler", "swimbait"): "Kawase Nami Swim",
    ("softbait", "ripper"): "Bayou Bill Paddle", ("softbait", "twister"): "Bayou Bill Curly",
    ("softbait", "worm"): "Bayou Bill Crawler", ("softbait", "craw"): "Bayou Bill Mud Craw",
    ("softbait", "stick"): "Bayou Bill Stik", ("softbait", "frog"): "Bayou Bill Lily Frog",
    ("fly", "dry"): "Highland Fly Co.", ("fly", "nymph"): "Highland Fly Co.", ("fly", "wet"): "Highland Fly Co.",
    ("fly", "streamer"): "Highland Fly Co.",
}


def lure(family, shape, title, colour, size_cm, weight_g, price, level, desc, sink_scale=1.0, sink_offset=0.0,
         depth_scale=1.0, depth_offset=0.0, affinity=1.0):
    name, main, accent, pattern, metal = colour
    ident = "l_" + slug(f"{family}_{shape}_{num(size_cm if family != 'spinner' else weight_g)}_{name}")
    LURES.append(dict(id=ident, family=family, shape=shape, name=f"{BRANDS[(family, shape)]} · {title}, {name}", main=main, accent=accent,
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

# ---------------------------------------------------------------- newer shapes
for g, cm, price, lvl in [(10, 6.0, 48, 2), (20, 7.5, 62, 3)]:
    for c in [SILVER, GOLD, BLUE_HERRING]:
        lure("spinner", "tailspinner", f"Obrotówka z ołowianym korpusem {num(g)} g", c, cm, g, price, lvl,
             "Ciężki ołowiany korpus i skrzydełko na ogonie: rzuca się jak wahadłówką, tonie szybko, na bolenie i okonie z głębi.",
             sink_scale=2.0, depth_scale=1.8, affinity=1.05)
for g, cm, c, price, lvl in [(14, 9.0, WHITE_RED, 70, 3), (14, 9.0, CHARTREUSE, 70, 3)]:
    lure("spinner", "spinnerbait", f"Spinnerbait {num(g)} g", c, cm, g, price, lvl,
         "Ramię z dwoma skrzydełkami nad główką ze spódniczką: przechodzi przez zielsko bez zaczepów. Na szczupaki.",
         sink_scale=1.2, depth_scale=1.0, affinity=1.1)
for g, cm, price, lvl in [(20, 6.0, 40, 2), (40, 8.0, 55, 3)]:
    for c in [SILVER, PERCH_SPOON]:
        lure("spoon", "jig", f"Pilker {num(g)} g", c, cm, g, price, lvl,
             "Ciężka, smukła blacha do łowienia pionowego i dalekich rzutów: opada jak kamień, podrywana błyska przy dnie.",
             sink_scale=2.2, depth_scale=1.4)
for cm, g, price, lvl in [(5, 9, 75, 2), (7, 18, 95, 3)]:
    for c in [SILVER, FIRETIGER, PERCH]:
        lure("wobbler", "vib", f"Cykada-wibrator {num(cm)} cm", c, cm, g, price, lvl,
             "Bezsterowy, tonący wibrator: drży przy każdym zwijaniu i rzuca się bardzo daleko. Sandacze i okonie w toni.",
             sink_offset=0.6, depth_scale=0.9)
for c in [WHITE_RED, ROACH, BLACK_GOLD]:
    lure("wobbler", "pencil", "Pencil walker 9 cm", c, 9, 11, 85, 3,
         "Ołówek bez steru prowadzony zygzakiem po powierzchni (podszarpnięcia PPM): bolenie i szczupaki o świcie.",
         depth_scale=0.0)
for cm, g, depth, price, lvl in [(8, 12, 1.4, 95, 3), (12, 25, 2.0, 135, 4)]:
    for c in [PERCH, ROACH, TROUT]:
        lure("wobbler", "shad", f"Wobler shad {num(cm)} cm", c, cm, g, price, lvl,
             "Wysoka, krępa rybka ze średnim sterem: mocna praca, schodzi do ok. " + num(round(0.8 * depth, 1)) + " m.",
             depth_scale=depth)
for c in [TROUT, PIKE, ROACH]:
    lure("wobbler", "swimbait", "Swimbait łamany 16 cm", c, 16, 45, 180, 5,
         "Trzyczęściowa rybka na zawiasach płynie esowato jak prawdziwa: duże szczupaki i sumy.",
         sink_offset=0.2, depth_scale=1.2, affinity=1.15)
for cm, g, price, lvl in [(10, 5, 20, 2), (13, 7, 26, 3)]:
    for c in [MOTOR_OIL, GREEN, BLACK_SOFT]:
        lure("softbait", "stick", f"Gumowy stick {num(cm)} cm, obciążenie {num(g)} g", c, cm, g, price, lvl,
             "Prosty, ciężki wałek bez ogonka: opada drżąc, na zestawie bez zaczepów. Okonie i klenie przy zielsku.",
             sink_scale=0.5 + g / 15.0)
for c in [FROG, BLACK, YELLOW]:
    lure("softbait", "frog", "Żaba pływająca 6 cm", c, 6, 14, 48, 3,
         "Pusta guma z ukrytymi hakami ślizga się po liściach grążeli: szczupaki w płyciznach z zielskiem.",
         sink_scale=0.0, depth_scale=0.0, affinity=1.1)

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
        title = f"Vistula Mono {num(d)} mm, {colour_name}"
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
        title = f"Kraken Braid X{8 if d <= 0.12 else 4} {num(d)} mm, {colour_name}"
        price = int(20 + strength * 6)
        desc = "Bardzo mocna jak na swoją grubość i nierozciągliwa: daleki rzut i pewne zacięcie, ale zrywy idą prosto w wędkę."
        ident = f"g_plecionka_{int(round(d * 100)):03d}_{slug(colour_name)}"
        k = 1
    else:
        strength = 90 * d * d
        visibility = 0.62 + d * 0.4
        stretch = 0.4
        title = f"Glasswater Fluorocarbon {num(d)} mm"
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


# ---------------------------------------------------------------- rods and reels on the user's Meshy models
RODS = os.path.join(ROOT, "data", "tackle", "rods")
REELS = os.path.join(ROOT, "data", "tackle", "reels")

# One series = one model (assets/models/rods/<model>.glb) in three lengths. Stats are the series' middle length; the
# short and long versions follow length_stats.
#   id, brand + name, kind (0 float, 1 spinning, 2 bottom, 3 carp), model, lengths (m), max load kg, action,
#   shock absorb, cast mult, price, level, blank, whip, foam grip, Polish kind word, description
ROD_SERIES = [
    ("raiden", "Kaminari Raiden", 1, "rods/spin_kaminari", (2.1, 2.4, 2.7), 7.0, 0.25, 0.12, 1.25, 340, 3,
     (0.06, 0.08, 0.18), (0.8, 0.82, 0.86), True, "spinning",
     "Szybki, uniwersalny spinning na okonie, sandacze i średnie szczupaki."),
    ("hikari", "Hokuto Hikari UL", 1, "rods/spin_hokuto", (1.8, 2.0, 2.2), 3.0, 0.35, 0.24, 1.0, 240, 2,
     (0.55, 0.58, 0.6), (0.92, 0.92, 0.9), False, "ultralekki spinning",
     "Delikatny ultralight z cienką szczytówką: pstrągi, okonie i drobne obrotówki."),
    ("hammerhead", "Ironwood Hammerhead", 1, "rods/spin_ironwood", (1.9, 2.2, 2.5), 15.0, 0.2, 0.07, 1.15, 760, 6,
     (0.3, 0.07, 0.08), (0.08, 0.08, 0.08), True, "ciężki spinning",
     "Sztywny kij na duże woblery, jerki i grube szczupaki."),
    ("nordlys", "Borealis Nordlys Feeder", 2, "rods/feeder_borealis", (3.0, 3.6, 3.9), 8.0, 0.6, 0.2, 1.3, 380, 3,
     (0.05, 0.05, 0.06), (0.85, 0.12, 0.1), True, "feeder",
     "Feeder z pomarańczową szczytówką na leszcze, karasie i liny z koszyka."),
    ("picker_plus", "Ostoja Picker Plus", 2, "rods/feeder_ostoja", (2.4, 2.7, 3.0), 4.5, 0.7, 0.27, 1.05, 170, 1,
     (0.18, 0.22, 0.14), (0.78, 0.62, 0.3), False, "lekki gruntowy",
     "Lekki picker na płocie i krąpie blisko brzegu: czuła biała szczytówka."),
    ("silurus_pro", "Ironwood Silurus Pro", 2, "rods/feeder_ironwood", (2.7, 3.0, 3.3), 20.0, 0.9, 0.1, 1.05, 1050, 7,
     (0.3, 0.3, 0.32), (0.06, 0.06, 0.06), True, "sumowy",
     "Gruby kij z mocnymi przelotkami na sumy i największe karpie."),
    ("fjell_match", "Borealis Fjell Match", 0, "rods/float_borealis", (3.9, 4.2, 4.5), 4.5, 0.4, 0.3, 1.35, 340, 3,
     (0.06, 0.08, 0.2), (0.78, 0.62, 0.3), False, "odległościówka",
     "Długa odległościówka na daleki spławik: płocie, leszcze i jazie."),
    ("bologna", "Fjordline Bologna", 0, "rods/float_fjordline", (3.6, 4.0, 4.5), 4.0, 0.3, 0.3, 1.1, 200, 2,
     (0.6, 0.54, 0.42), (0.42, 0.26, 0.14), False, "bolonka",
     "Bolonka na spławik z przelotkami na wysokich stopkach: rzeka i głębsze stanowiska."),
    ("jesion", "Ostoja Jesion", 0, "rods/float_ostoja", (2.4, 3.0, 3.6), 3.5, 0.5, 0.3, 0.9, 70, 1,
     (0.42, 0.27, 0.14), (0.6, 0.5, 0.35), False, "drewniana spławikówka",
     "Prosta wędka z jesionu ze sznurkowymi owijkami: dobra na początek."),
    ("karp_classic", "Ostoja Karp Classic", 3, "rods/carp_ostoja", (3.0, 3.6, 3.9), 10.0, 0.85, 0.14, 1.1, 420, 4,
     (0.08, 0.16, 0.1), (0.78, 0.62, 0.3), True, "karpiówka",
     "Klasyczna, paraboliczna karpiówka z pełną pianką."),
    ("koi", "Kaminari Koi", 3, "rods/carp_kaminari", (3.0, 3.6, 3.9), 12.0, 0.8, 0.12, 1.2, 650, 5,
     (0.05, 0.05, 0.05), (0.85, 0.12, 0.1), True, "karpiówka",
     "Nowoczesna karpiówka z krótką rękojeścią: dalekie rzuty ciężkimi zestawami."),
    ("polar_carp", "Borealis Polar Carp", 3, "rods/carp_borealis", (3.0, 3.6, 3.9), 14.0, 0.75, 0.12, 1.3, 950, 6,
     (0.04, 0.07, 0.2), (0.82, 0.84, 0.88), False, "karpiówka",
     "Karpiówka z najwyższej półki: korek, lakierowany blank, rzuty na 100 m."),
]

# More series on the same Meshy models, each with its own finish (ModelTint: the model's pictures multiplied by it in
# the game) and its own figures: 27 series x 3 lengths = 81 rods.
#   same fields as ROD_SERIES, then the finish's tint
EXTRA_ROD_SERIES = [
    ("kage", "Kaminari Kage Finesse", 1, "rods/spin_kaminari", (1.9, 2.1, 2.3), 4.0, 0.3, 0.2, 1.1, 290, 2,
     (0.1, 0.11, 0.13), (0.7, 0.72, 0.76), False, "finesse", "Lekki kij do gumek i drop shota na okonie.", (0.55, 0.58, 0.62)),
    ("tsunami", "Kaminari Tsunami Jerk", 1, "rods/spin_kaminari", (1.8, 2.0, 2.2), 25.0, 0.15, 0.06, 1.0, 890, 7,
     (0.06, 0.1, 0.3), (0.85, 0.85, 0.88), True, "jerkowy", "Krótki, twardy kij do ciężkich jerków i wielkich szczupaków.", (0.35, 0.45, 0.85)),
    ("yuki", "Hokuto Yuki Trout", 1, "rods/spin_hokuto", (1.7, 1.9, 2.1), 2.0, 0.4, 0.28, 0.95, 190, 1,
     (0.8, 0.78, 0.72), (0.4, 0.3, 0.2), False, "pstrągowy", "Bardzo lekka wędka na pstrągi i klenie na małych rzekach.", (1.0, 0.95, 0.88)),
    ("sakura", "Hokuto Sakura ML", 1, "rods/spin_hokuto", (2.1, 2.3, 2.5), 6.0, 0.32, 0.2, 1.15, 330, 3,
     (0.55, 0.28, 0.32), (0.9, 0.85, 0.85), False, "spinning średni", "Średni spinning na bolenie i okonie z mocnym dolnikiem.", (1.0, 0.72, 0.75)),
    ("grizzly", "Ironwood Grizzly", 1, "rods/spin_ironwood", (2.4, 2.7, 3.0), 30.0, 0.25, 0.08, 1.25, 980, 8,
     (0.12, 0.2, 0.1), (0.08, 0.08, 0.08), True, "bardzo ciężki spinning", "Pałka na sumy na spinning i wielkie gumy.", (0.45, 0.62, 0.4)),
    ("method", "Borealis Method Feeder", 2, "rods/feeder_borealis", (3.0, 3.3, 3.6), 9.0, 0.65, 0.22, 1.2, 420, 4,
     (0.35, 0.25, 0.12), (0.85, 0.12, 0.1), True, "method feeder", "Feeder do koszyczków method: krótkie rzuty, pewne zacięcia.", (0.95, 0.75, 0.5)),
    ("river_feeder", "Ostoja Rzeczny Feeder", 2, "rods/feeder_ostoja", (3.6, 3.9, 4.2), 12.0, 0.5, 0.18, 1.3, 520, 5,
     (0.12, 0.25, 0.12), (0.78, 0.62, 0.3), True, "feeder rzeczny", "Mocny feeder na prąd i ciężkie koszyki: brzany i klenie.", (0.55, 0.8, 0.55)),
    ("barbel", "Ironwood Barbel Specimen", 2, "rods/feeder_ironwood", (3.3, 3.6, 3.9), 16.0, 0.8, 0.14, 1.15, 780, 6,
     (0.45, 0.35, 0.22), (0.06, 0.06, 0.06), True, "specimen", "Specjalistyczny kij na duże brzany i liny.", (1.0, 0.85, 0.65)),
    ("telescope", "Ostoja Teleskop", 0, "rods/float_ostoja", (3.0, 4.0, 5.0), 3.0, 0.45, 0.32, 0.95, 90, 1,
     (0.25, 0.2, 0.15), (0.6, 0.5, 0.35), False, "teleskop", "Tani teleskop: tyczka na płocie i ukleje z pomostu.", (0.65, 0.6, 0.55)),
    ("waggler", "Borealis Waggler Pro", 0, "rods/float_borealis", (3.6, 3.9, 4.2), 5.0, 0.42, 0.3, 1.3, 410, 4,
     (0.12, 0.18, 0.4), (0.78, 0.62, 0.3), False, "waggler", "Szybki match do wagglera na dalekie łowisko.", (0.6, 0.7, 1.0)),
    ("lagoon", "Fjordline Lagoon", 0, "rods/float_fjordline", (3.3, 3.9, 4.5), 4.5, 0.35, 0.32, 1.15, 260, 2,
     (0.15, 0.35, 0.35), (0.42, 0.26, 0.14), False, "spławikowa", "Lekka spławikówka na staw i jezioro.", (0.55, 0.85, 0.85)),
    ("sztorm", "Fjordline Sztorm Bolo", 0, "rods/float_fjordline", (4.5, 5.0, 6.0), 6.0, 0.28, 0.28, 1.1, 480, 5,
     (0.4, 0.12, 0.1), (0.42, 0.26, 0.14), False, "długa bolonka", "Długa bolonka na rzekę z mocnym dolnikiem.", (0.85, 0.5, 0.45)),
    ("stalker", "Ostoja Stalker", 3, "rods/carp_ostoja", (2.7, 3.0, 3.3), 9.0, 0.8, 0.16, 1.0, 360, 3,
     (0.2, 0.28, 0.12), (0.78, 0.62, 0.3), True, "podchodówka", "Krótka karpiówka do podchodu w zarośniętych łowiskach.", (0.6, 0.7, 0.45)),
    ("dragon", "Kaminari Dragon Spod", 3, "rods/carp_kaminari", (3.6, 3.9, 4.2), 16.0, 0.65, 0.1, 1.35, 820, 7,
     (0.4, 0.06, 0.05), (0.85, 0.85, 0.88), True, "spodówka", "Mocny kij do rakiet zanętowych i bardzo dalekich rzutów.", (0.95, 0.45, 0.4)),
    ("aurora", "Borealis Aurora", 3, "rods/carp_borealis", (3.3, 3.6, 3.9), 12.0, 0.72, 0.13, 1.25, 720, 6,
     (0.3, 0.2, 0.45), (0.82, 0.84, 0.88), False, "karpiówka", "Smukła karpiówka z lekkim blankiem: długi hol bez spinki.", (0.75, 0.62, 0.95)),
]
ROD_TINT = {se[0]: se[16] for se in EXTRA_ROD_SERIES}
ROD_SERIES = ROD_SERIES + [se[:16] for se in EXTRA_ROD_SERIES]
LENGTH_WORD = ("krótka", "średnia", "długa")


def length_stats(series, i):
    """The series' stats for its i-th length (0 short, 1 middle, 2 long): a longer rod casts further (cast x (L/Lm)^0.8)
    and cushions a lunge better (shock x (L/Lm)^0.7), its leverage takes a little off what it lifts
    (max load x (Lm/L)^0.25) and it costs more."""
    lengths = series[4]
    L, Lm = lengths[i], lengths[1]
    return dict(length=L, cast=series[8] * (L / Lm) ** 0.8, shock=min(0.5, series[7] * (L / Lm) ** 0.7),
                load=series[5] * (Lm / L) ** 0.25, price=int(round(series[9] * (0.88, 1.0, 1.15)[i] / 5) * 5))


def write_rod_series():
    ids = []
    for se in ROD_SERIES:
        for i, L in enumerate(se[4]):
            st = length_stats(se, i)
            ident = f"s_{se[0]}_{int(round(L * 100))}"
            ids.append(ident)
            body = f"""[gd_resource type="Resource" script_class="RodData" load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Data/RodData.cs" id="1"]

[resource]
script = ExtResource("1")
DisplayName = {q(f"{se[1]} {num(L)} m · {se[14]} ({LENGTH_WORD[i]})")}
Kind = {se[2]}
CastMult = {st['cast']:.3g}
MaxLoad = {st['load']:.3g}
ShockAbsorb = {st['shock']:.3g}
Action = {se[6]:g}
Price = {st['price']}
RequiredLevel = {se[10]}
Description = {q(se[15] + " Krótsza rzuca bliżej, ale jest mocniejsza; dłuższa rzuca dalej i lepiej amortyzuje zrywy.")}
BlankColor = {col(se[11])}
WhipColor = {col(se[12])}
FoamGrip = {"true" if se[13] else "false"}
Length = {L:g}
Model = {q(se[3])}
"""
            if se[0] in ROD_TINT:
                body += f"ModelTint = {col(ROD_TINT[se[0]])}\n"
            with open(os.path.join(RODS, ident + ".tres"), "w", encoding="utf-8") as f:
                f.write(body)
    return ids


# The hand-made reels kept in the shop get their model (the hand-made rods and the other reels were retired:
# Profile.RetiredRods/RetiredReels).
KEEP_REELS = {
    "mlynek1000": "reels/small_hokuto", "fjordline_freespool6000": "reels/baitrunner_fjordline",
    "beben_sumowy": "reels/big_ironwood",
}
#   id, name, model, drag max, speed, capacity, price, level, body, trim, description
NEW_REELS = [
    ("r_hokuto_kaiten4000", "Hokuto Kaiten 4000 FD · uniwersalny", "reels/medium_hokuto", 7.0, 3.6, 160, 420, 4,
     (0.22, 0.23, 0.25), (0.75, 0.15, 0.1), "Średni kołowrotek z przednim hamulcem: spinning, feeder i spławik."),
    ("r_kaminari_longcast5000", "Kaminari Long Cast 5000 · wolny bieg", "reels/baitrunner_kaminari", 10.0, 3.4, 260, 680, 5,
     (0.06, 0.06, 0.07), (0.78, 0.78, 0.8), "Długa stożkowa szpula do dalekich rzutów i wolny bieg na karpie."),
    ("r_fjordline_lett2500", "Fjordline Lett 2500 · lekki spinningowy", "reels/small_fjordline", 4.0, 3.7, 100, 220, 2,
     (0.12, 0.18, 0.12), (0.78, 0.6, 0.3), "Lekki kołowrotek z drewnianą gałką: ultralight i lekki spławik."),
]


# The reel catalogue on the six Meshy reels: each family in its sizes, each size in three finishes (the model's
# pictures tinted, the model scaled by the size), the figures from the size: line capacity, drag, retrieve, price.
#   model, brand + line, sizes, price factor, kind word
REEL_FAMILIES = [
    ("reels/small_hokuto", "Hokuto Mizu", (1000, 1500, 2000, 2500, 3000), 1.0, "lekki spinningowy"),
    ("reels/small_fjordline", "Fjordline Fjell", (1000, 1500, 2000, 2500, 3000), 0.9, "lekki uniwersalny"),
    ("reels/medium_hokuto", "Hokuto Kaiten", (3000, 3500, 4000, 4500, 5000), 1.2, "średni z przednim hamulcem"),
    ("reels/baitrunner_fjordline", "Fjordline Frispole", (5000, 6000, 8000, 10000), 1.4, "z wolnym biegiem"),
    ("reels/baitrunner_kaminari", "Kaminari Long Cast", (5000, 6000, 8000, 10000), 1.5, "dalekorzutowy z wolnym biegiem"),
    ("reels/big_ironwood", "Ironwood Titan", (8000, 10000, 12000, 14000), 1.6, "sumowy, mocny hamulec"),
]
#   id suffix, name suffix, tint, retrieve +, drag x, price x, level +
REEL_FINISHES = [
    ("", "", (1.0, 1.0, 1.0), 0.0, 1.0, 1.0, 0),
    ("hs", " HS", (0.55, 0.6, 0.68), 0.45, 0.95, 1.2, 1),
    ("pro", " Pro", (1.0, 0.82, 0.55), 0.2, 1.15, 1.5, 2),
]


def reel_catalogue():
    out = []
    for model, name, sizes, pf, word in REEL_FAMILIES:
        mid = sizes[len(sizes) // 2]
        for size in sizes:
            k = size / 1000.0
            for suffix, fword, tint, speed, dragx, pricex, lvl in REEL_FINISHES:
                ident = f"r_{slug(name)}_{size}{suffix}"
                drag = min(2.4 + 1.25 * k, 22.0) * dragx
                capacity = round(35 * k + 25)
                retrieve = min(3.0 + 0.12 * k, 4.4) + speed
                price = int(round((55 + 48 * k) * pf * pricex / 5) * 5)
                level = max(1, min(10, int(round(1 + 0.55 * k + lvl))))
                scale = max(0.78, min(1.3, (size / mid) ** (1 / 3)))
                body = tuple(max(0.04, c * 0.25) for c in tint)
                desc = (f"{name} w rozmiarze {size}: {capacity} m żyłki 0,25 mm, hamulec do {drag:.1f} kg"
                        + (", szybkie przełożenie." if suffix == "hs" else ", wzmocniona przekładnia i hamulec." if suffix == "pro" else "."))
                out.append((ident, f"{name} {size}{fword} · {word}", model, drag, retrieve, capacity, price, level, body,
                            (0.75, 0.62, 0.3), desc, tint, scale))
    return out


def write_reels():
    ids = []
    catalogue = reel_catalogue()
    keep = {r[0] for r in NEW_REELS} | {r[0] for r in catalogue}
    for old in os.listdir(REELS):
        if old.startswith("r_") and old.endswith(".tres") and old[:-5] not in keep:
            os.remove(os.path.join(REELS, old))
    for r in catalogue:
        ids.append(r[0])
        with open(os.path.join(REELS, r[0] + ".tres"), "w", encoding="utf-8") as f:
            f.write(f"""[gd_resource type="Resource" script_class="ReelData" load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Data/ReelData.cs" id="1"]

[resource]
script = ExtResource("1")
DisplayName = {q(r[1])}
DragMax = {r[3]:.1f}
ReelSpeed = {r[4]:.2f}
LineCapacity = {r[5]:.1f}
Price = {r[6]}
RequiredLevel = {r[7]}
Description = {q(r[10])}
BodyColor = {col(r[8])}
TrimColor = {col(r[9])}
Model = {q(r[2])}
ModelTint = {col(r[11])}
ModelScale = {r[12]:.3f}
""")
    for r in NEW_REELS:
        ids.append(r[0])
        body = f"""[gd_resource type="Resource" script_class="ReelData" load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Data/ReelData.cs" id="1"]

[resource]
script = ExtResource("1")
DisplayName = {q(r[1])}
DragMax = {r[3]:.1f}
ReelSpeed = {r[4]:g}
LineCapacity = {r[5]:.1f}
Price = {r[6]}
RequiredLevel = {r[7]}
Description = {q(r[10])}
BodyColor = {col(r[8])}
TrimColor = {col(r[9])}
Model = {q(r[2])}
"""
        with open(os.path.join(REELS, r[0] + ".tres"), "w", encoding="utf-8") as f:
            f.write(body)
    for ident, model in KEEP_REELS.items():
        patch(os.path.join(REELS, ident + ".tres"), dict(Model=q(model)))
    return ids


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
    for old in os.listdir(RODS):
        if old.startswith("s_") and old.endswith(".tres"):
            os.remove(os.path.join(RODS, old))
    rod_ids = write_rod_series()
    reel_ids = write_reels()
    with open(OUT_CS, "w", encoding="utf-8") as f:
        f.write("// Generated by tools/tackle/generate_tackle.py: do not edit by hand.\n")
        f.write("namespace FishingSim.Progress;\n\n")
        f.write("/// <summary>The catalogue's spinning lures, lines, rod series and new reels (ids of data/tackle/baits/l_*.tres, lines/g_*.tres, rods/s_*.tres, reels/r_*.tres).</summary>\n")
        f.write("public static class GeneratedTackle\n{\n")
        f.write("    public static readonly string[] Lures =\n    {\n")
        for i in range(0, len(ids), 4):
            f.write("        " + " ".join(f'"{x}",' for x in ids[i:i + 4]) + "\n")
        f.write("    };\n\n    public static readonly string[] Lines =\n    {\n")
        for i in range(0, len(lids), 4):
            f.write("        " + " ".join(f'"{x}",' for x in lids[i:i + 4]) + "\n")
        f.write("    };\n\n    public static readonly string[] Rods =\n    {\n")
        for i in range(0, len(rod_ids), 4):
            f.write("        " + " ".join(f'"{x}",' for x in rod_ids[i:i + 4]) + "\n")
        f.write("    };\n\n    public static readonly string[] Reels =\n    {\n")
        for i in range(0, len(reel_ids), 4):
            f.write("        " + " ".join(f'"{x}",' for x in reel_ids[i:i + 4]) + "\n")
        f.write("    };\n}\n")
    fams = {}
    for l in LURES:
        fams[l["family"]] = fams.get(l["family"], 0) + 1
    print(f"{len(rod_ids)} series rods, {len(reel_ids)} new reels")
    print(f"{len(LURES)} lures ({', '.join(f'{k} {v}' for k, v in fams.items())}), {len(LINE_LIST)} lines")


if __name__ == "__main__":
    main()
