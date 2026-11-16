# Modele ryb z zewnątrz (np. Meshy AI)

Plik `<id>.glb` w tym katalogu zastępuje w grze zbudowany model gatunku z `assets/fish/models/<id>.glb`
(ten sam `<id>`, np. `okon.glb`, `ploc.glb`, `szczupak.glb`: nazwy plików z `data/fish`).

- glTF 2.0 binarny (.glb) z teksturami w środku; pierwszy obraz koloru to skóra, obraz z "normal" w nazwie
  to mapa normalnych (OpenGL), bez wypalonego światła.
- Dowolna orientacja i skala: gra układa rybę wzdłuż, nos wykrywa z kształtu (głowa grubsza niż płetwa
  ogonowa). Gdy się pomyli, obok połóż `<id>.json`: `{"nose": "+x", "up": "+y"}`.
- Oczy namalowane w teksturze; płetwy mogą być częścią jednej siatki.
- Licencja: tylko modele, które wolno użyć komercyjnie; wpisz autora do CREDITS.md.
