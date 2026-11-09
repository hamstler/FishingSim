#!/usr/bin/env python3
"""
Finds freely licensed side-view fish photographs on Wikimedia Commons for every species in data/fish and downloads
candidate thumbnails for picking. One API request per species (search + image info + licence metadata), spaced out
and retried politely (Wikimedia rate-limits shared addresses). Writes candidates.json next to the downloads.

Usage: python3 tools/fish_photos/fetch.py search <species.json> <out_dir> [ids...]
       python3 tools/fish_photos/fetch.py previews <out_dir> [per_species]   (330 px previews of the best-ranked)
       python3 tools/fish_photos/fetch.py download <out_dir> <picks.json>   (picks: {"species_id": [candidate indices]})
"""
import json
import os
import sys
import time
import urllib.parse
import urllib.request

UA = "FishingSimAssetFetcher/1.0 (https://github.com/hamstler/FishingSim; contact via GitHub issues)"
API = "https://commons.wikimedia.org/w/api.php"

# Licences we may ship in a game with attribution (no NonCommercial/NoDerivatives).
OK_LICENCES = ("cc0", "public domain", "pd", "cc by", "cc-by", "cc by-sa", "cc-by-sa")

# Better search terms for names that do not find photos on their own.
QUERIES = {
    "sazan": ["Cyprinus carpio wild", "Cyprinus carpio"],
    "karp_lustrzen": ["mirror carp", "Cyprinus carpio mirror"],
    "orfa": ["golden orfe", "Leuciscus idus orfe"],
    "troc": ["Salmo trutta lacustris", "Salmo trutta"],
    "troc_wedrowna": ["sea trout Salmo trutta", "Salmo trutta trutta"],
    "pstrag": ["Salmo trutta fario", "brown trout"],
    "jesiotr": ["Acipenser oxyrinchus", "Atlantic sturgeon"],
    "bolen": ["Aspius aspius", "Leuciscus aspius"],
    "sapa": ["Ballerus sapa", "Abramis sapa"],
    "rozpior": ["Ballerus ballerus", "Abramis ballerus"],
    "brzanka": ["Barbus carpathicus", "Barbus petenyi"],
    "kielb_bialoplety": ["Romanogobio albipinnatus", "Gobio albipinnatus"],
    "strzebla_blotna": ["Rhynchocypris percnurus", "Phoxinus percnurus"],
    "glowacz": ["Cottus gobio"],
    "glowacz_pregoplety": ["Cottus poecilopus"],
}


def get(url, tries=12):
    for attempt in range(tries):
        req = urllib.request.Request(url, headers={"User-Agent": UA, "Api-User-Agent": UA})
        try:
            with urllib.request.urlopen(req, timeout=40) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code in (429, 503):
                wait = int(e.headers.get("Retry-After", "20") or 20) + 5 + attempt * 5
                print(f"  rate limited, waiting {wait}s", flush=True)
                time.sleep(wait)
                continue
            raise
        except Exception as e:  # network hiccup
            print(f"  error {e}, retrying", flush=True)
            time.sleep(10)
    raise RuntimeError(f"gave up on {url}")


def thumb_url(c, width):
    """
    The picture at a given width through commons.wikimedia.org/w/thumb.php. upload.wikimedia.org answers shared cloud
    addresses with long rate-limit bans; thumb.php streams the same thumbnail from the wiki host.
    """
    name = c["title"].split(":", 1)[1].replace(" ", "_")
    w = min(width, c.get("width") or width)
    return "https://commons.wikimedia.org/w/thumb.php?" + urllib.parse.urlencode({"f": name, "w": str(w)})


def search(term):
    params = {
        "action": "query", "format": "json", "generator": "search", "gsrsearch": term, "gsrnamespace": "6",
        "gsrlimit": "40", "prop": "imageinfo", "iiprop": "url|size|mime|extmetadata", "iiurlwidth": "1600",
    }
    data = json.loads(get(API + "?" + urllib.parse.urlencode(params)))
    out = []
    for page in (data.get("query", {}).get("pages", {}) or {}).values():
        info = (page.get("imageinfo") or [{}])[0]
        meta = info.get("extmetadata", {})
        lic = (meta.get("LicenseShortName", {}).get("value") or "").lower()
        w, h = info.get("width", 0), info.get("height", 0)
        if info.get("mime") not in ("image/jpeg", "image/png") or w < 900 or h == 0:
            continue
        if not any(l in lic for l in OK_LICENCES) or "nc" in lic.split() or "nd" in lic.split():
            continue
        aspect = w / h
        if not (1.5 <= aspect <= 4.5):          # a fish seen from the side is long and low
            continue
        out.append({
            # Thumbnails are also served from upload.wikimedia.org (the host the environment allows).
            "title": page["title"], "thumb": (info.get("thumburl") or "").replace("://thumb.wikimedia.org/", "://upload.wikimedia.org/").split("?")[0],
            "width": w, "height": h,
            "licence": meta.get("LicenseShortName", {}).get("value"),
            "licence_url": meta.get("LicenseUrl", {}).get("value"),
            "artist": meta.get("Artist", {}).get("value"),
            "source": info.get("descriptionurl"),
        })
    return out


def search_all(species_file, out_dir, only):
    species = json.load(open(species_file, encoding="utf-8"))
    species.sort(key=lambda r: PRIORITY.index(r[0]) if r[0] in PRIORITY else 999)
    os.makedirs(out_dir, exist_ok=True)
    cand_path = os.path.join(out_dir, "candidates.json")
    found = json.load(open(cand_path, encoding="utf-8")) if os.path.exists(cand_path) else {}
    for sid, name, latin in species:
        if (only and sid not in only) or (sid in found and found[sid]):
            continue
        terms = QUERIES.get(sid, [latin.split(" var.")[0].split(" (")[0]])
        cands = []
        for term in terms:
            print(f"{sid}: searching '{term}'", flush=True)
            cands += search(term)
            time.sleep(8)
            if len(cands) >= 6:
                break
        seen, unique = set(), []
        for c in cands:
            if c["title"] not in seen:
                seen.add(c["title"])
                unique.append(c)
        found[sid] = unique[:16]
        json.dump(found, open(cand_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"{sid}: {len(found[sid])} candidates", flush=True)


def download(out_dir, picks_file):
    found = json.load(open(os.path.join(out_dir, "candidates.json"), encoding="utf-8"))
    picks = json.load(open(picks_file, encoding="utf-8"))
    for sid, indices in picks.items():
        for i in indices:
            c = found[sid][i]
            url = thumb_url(c, 1920)
            path = os.path.join(out_dir, f"{sid}_{i}.jpg")
            if os.path.exists(path) and os.path.getsize(path) > 0:
                continue
            try:
                open(path, "wb").write(get(url))
                print(f"{sid}_{i}: ok", flush=True)
            except Exception as e:
                print(f"{sid}_{i}: failed {e}", flush=True)
            time.sleep(3)


PRIORITY = ["ploc", "leszcz", "karp", "szczupak", "okon", "sandacz", "sum", "lin", "karas", "karas_srebrzysty", "krap",
            "wzdrega", "pstrag", "jaz", "klen", "bolen", "wegorz", "ukleja", "pstrag_teczowy", "brzana", "lipien", "jazgarz",
            "mietus", "sielawa", "sieja", "losos", "troc", "kielb", "certa", "karp_lustrzen", "amur", "sazan"]


def score(c):
    """Rank candidates: specimen shots on a plain background first, public domain and large images preferred."""
    t = (c["title"] + " " + (c.get("artist") or "")).lower()
    s = 0
    for good in ("white background", "isolated", "specimen", "usfws", "juvenile", "adult", "side", "lateral", "hillewaert"):
        if good in t:
            s += 2
    for bad in ("aquarium", "underwater", "swimming", "market", "stamp", "drawing", "illustration", "dish", "fried", "map", "skeleton", "head"):
        if bad in t:
            s -= 3
    lic = (c.get("licence") or "").lower()
    if "public domain" in lic or "cc0" in lic:
        s += 1
    return s


def previews(out_dir, per_species=5):
    """Small (330 px) previews of the best-ranked candidates, for picking by eye."""
    found = json.load(open(os.path.join(out_dir, "candidates.json"), encoding="utf-8"))
    order = [s for s in PRIORITY if s in found] + [s for s in found if s not in PRIORITY]
    for sid in order:
        ranked = sorted(range(len(found[sid])), key=lambda i: -score(found[sid][i]))[:per_species]
        for i in ranked:
            path = os.path.join(out_dir, "prev", f"{sid}_{i}.jpg")
            if os.path.exists(path) and os.path.getsize(path) > 0:
                continue
            os.makedirs(os.path.dirname(path), exist_ok=True)
            url = thumb_url(found[sid][i], 330)
            try:
                open(path, "wb").write(get(url))
                print(f"preview {sid}_{i}", flush=True)
            except Exception as e:
                print(f"preview {sid}_{i} failed {e}", flush=True)
            time.sleep(3)


def main():
    if sys.argv[1] == "previews":
        previews(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 5)
        return
    if sys.argv[1] == "search":
        search_all(sys.argv[2], sys.argv[3], set(sys.argv[4:]))
    else:
        download(sys.argv[2], sys.argv[3])


if __name__ == "__main__":
    main()
