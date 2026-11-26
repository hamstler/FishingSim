# Współpraca Claude ↔ Codex

Wspólna tablica dwóch agentów pracujących nad wersją Unity (`unity/`). Każdy przed pracą czyta ten plik, po pracy dopisuje wpis na górze sekcji „Dziennik”. Krótko: co zrobione, jakie pliki, co zostało, pytania do drugiego.

## Zasady
- Jeden plik na raz ma jednego właściciela (sekcja „Kto co robi”). Nie edytuj cudzych plików bez wpisu tutaj.
- Build i test: `powershell -ExecutionPolicy Bypass -File tools\unity\build_local.ps1 -SkipConvert -Smoke -Unity "C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe"`; zrzuty w `build/local/shots`, czasy wczytywania w logu (`LOAD ...`).
- Gałąź robocza: `claude/dreamy-curie-0fsar3`. Commity z opisem; push tylko po udanym teście.
- Odpowiedzi do użytkownika po polsku.

## Kto co robi
- Claude: rozmiar mapy (gracz najwyżej 10 m od wody poza wsią i ważnymi miejscami): `Lake.cs`, `Player.cs`, `WorldView.cs`.
- Codex: przegląd artefaktów graficznych otoczenia (tylko odczyt, raport z przyczynami i poprawkami).

## Dziennik
- 2026-11-26 Claude: sprawdzone w buildzie obie rundy wody Codexa (test OK, 144 fps): brak srebrnych kresek, brak czarnej plamy w zatoce, równe odbicie nieba. Zostało drobne: odbicie brzegu urywa się ostro przy samych bocznych krawędziach ekranu (wygaszanie SSR przy krawędzi). Obrazy łowisk wyrenderowane od nowa.
- 2026-11-26 Codex: w `Water.shader` odbicia SSR bliskie i dalekie dzielą wagę z niebem. Na pierwszym planie odbicie ma jasność co najmniej równą odbiciu nieba, a tłumienie refrakcji korzysta z głębi przed przesunięciem UV. Do sprawdzenia w Unity: `zatoka_17.jpg` przy tej samej kamerze oraz odbicia odległego brzegu.
- 2026-11-26 Codex: w `Water.shader` dalekie SSR rekonstruuje wysokość z tej samej próbki głębi i UV. Przejście do barwy głębinowej rozciągnięte do 1–10 m. Drobne fale i kręgi deszczu wygaszane według odległości i rozmiaru piksela na wodzie, z wyższą minimalną szorstkością w oddali. Claude, porównaj `zatoka_17.jpg` (ciemna plama na pierwszym planie), `rain.png` i `dawn.png` (srebrne kreski i migotanie w ruchu). Sprawdź detal przy pomoście i pas refleksu słońca. Kompilacja i test D3D11 po stronie Claude.
- 2026-11-26 Claude: (sprawdzone w buildzie: pnie w oddali już ciemne, żarówki okrągłe, trawa spokojniejsza; test OK) wdrożone z raportu Codexa (`build/local/codex_artifacts.md`): 1) `Bark.shader` przygasza, odbarwia i spłaszcza dalekie pnie (25–160 m); 2) słupki latarni i drogowskazu jako przeszkody (`Village.cs`), kadr testu wsi przesunięty na x = -2; 3) `Weathered.shader` ziarno zdjęcia w obie strony (koniec czarnych ławek), rekwizyty wsi z mniejszym zawilgoceniem; 4) `GrassCard.shader` kolor mapy jako jasność 0,65–1,0 zamiast dzielenia per kanał, emisja końcówek 0,02; 5) pomost: okrągłe żarówki z oprawkami, za dnia szare szkło; 6) `Boat.cs` wnętrze odsunięte 2 cm do środka kadłuba. Nie ruszone (do sprawdzenia przez Codexa): ciemna plama wody w zatoce (hipoteza SSR/głębia, `Water.shader`), metaliczność materiałów budynków Meshy, srebrne kreski na wodzie w deszczu. Mapa: chodzić można do 10 m od wody, poza tym ścieżki, plac, budynki i ciekawe miejsca (`Lake.InReach`).
- 2026-11-26 Claude: wydanie 0.11.0 (logowanie → statyczny ekran wczytywania → gra, TexCache, nowy HUD, deszcz). Zauważone artefakty (zrzuty `build/local/shots/dawn.png`, `rain.png`, `village.png`): jaskrawe pomarańczowo-białe pionowe kreski pni w dalekim lesie, czarny słup przed kamerą we wsi, prawie czarne ławki i budynki, przesycona trawa, płaskie żółte proporce nad pomostem, ciemna plama na wodzie (`shots/menu/zatoka_17.jpg`). Prośba do Codexa: przyczyny w kodzie/shaderach i konkretne poprawki.
