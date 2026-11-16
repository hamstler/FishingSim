# Modele ryb z zewnątrz (np. Meshy AI)

Plik `<id>.glb` w tym katalogu zastępuje w grze zbudowany model gatunku z `assets/fish/models/<id>.glb`
(ten sam `<id>`, np. `okon.glb`, `ploc.glb`, `szczupak.glb`: nazwy plików z `data/fish`).

- glTF 2.0 binarny (.glb) z teksturami w środku; pierwszy obraz koloru to skóra, obraz z "normal" w nazwie
  to mapa normalnych (OpenGL), bez wypalonego światła.
- Dowolna orientacja i skala: gra układa rybę wzdłuż, nos wykrywa z kształtu (głowa grubsza niż płetwa
  ogonowa). Gdy się pomyli, obok połóż `<id>.json`: `{"nose": "+x", "up": "+y"}`.
- Oczy namalowane w teksturze; płetwy mogą być częścią jednej siatki.
- Licencja: tylko modele, które wolno użyć komercyjnie; wpisz autora do CREDITS.md.

## Pobieranie z Google Drive

`python3 -I tools/assets/meshy_fish.py <ID folderu>` czyta udostępniony folder ("każdy z linkiem"), w którym każdy
gatunek ma podfolder `Meshy_AI_<Nazwa>_3D_...` z jednym `.glb`. Nazwę dopasowuje do `SpeciesName` z `data/fish`
(kilka angielskich nazw w tabeli `ALIAS`), siatkę ponad 30 tys. trójkątów upraszcza w Blenderze
(`tools/assets/meshy_decimate.py`, UV zostają), tekstury koloru i normalnych zapisuje w WebP 2048 px, a nieużywaną
teksturę metaliczności/szorstkości pomija: z ~45 MB robi się ~2,5 MB na gatunek. Opcje: `--only okon,ploc`,
`--size 2048`, `--triangles 30000`.
