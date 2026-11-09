# Fishing Sim (RF4-like), wersja C#

Symulator wędkarski w Godot 4.3 (.NET) napisany w całości w C#. Poza kodem są tylko dwa shadery (`shaders/*.gdshader`: woda i gwiazdy), bo shadery w Godocie piszą się w jego własnym języku, oraz pliki scen i danych (`.tscn`, `.tres`).

## Wymagania i uruchomienie
- **Godot 4.3 w wersji .NET (Mono)** i **.NET SDK 8**.
- Otwórz `project.godot` w Godocie .NET, zbuduj (Alt+B albo przycisk Build), potem F5.
- Logowanie: `admin` / `admin` (jedyne konto, tworzone przy pierwszym uruchomieniu). Hasła są zapisywane jako hasz z solą (`src/Accounts/UserStore.cs`).
- Bez edytora: `dotnet build`, potem `godot --path .`.

## Sterowanie
| Klawisz | Działanie |
|---|---|
| WASD, mysz | ruch po całej mapie, rozglądanie |
| LPM (przytrzymaj / puść) | ładowanie i rzut; potem zwijanie |
| PPM lub Spacja | zacięcie, gdy spławik się zanurzy |
| Kółko myszy | hamulec |
| 1-5 (Shift = wstecz) | wędka, kołowrotek, żyłka, hak, przynęta (tylko przed rzutem) |
| I / M / J / T | ekwipunek, mapa, dziennik, turnieje |
| E | wejście do sklepu, biura zawodów lub przystanku (przy drzwiach budynku) |
| Shift | bieg |
| Esc | menu pauzy (wznów, zmień mapę, wyloguj, wyjdź) |
| H | ukryj pomoc |
| F1 | konsola deweloperska: F2 natychmiastowe branie, F3 nieśmiertelna żyłka, F4 gatunek, F5 reset rzutu, F6 przynęty, F7 +3 godz., F8 pogoda, F9 reset postępu, F10 +1000 zł |

## Świat
- **Cała mapa jest do chodzenia:** brzeg, wzgórza, wioska. Wchodzić można tylko w płytką wodę (do 0,5 m), a pomost jest jednym z miejsc do łowienia. Łowić można z dowolnego miejsca na brzegu.
- **Cztery zróżnicowane mapy** (`data/locations/*.tres`), każda z własnym biomem (kolory terenu i wody, mgła, wysokość wzgórz, gatunki i gęstość drzew, krzewy, kwiaty, trzciny, skały) i własną wioską:
  - *Jezioro Zielone*: las mieszany (dąb, brzoza, świerk), wioska z sklepem wędkarskim, sklepem z przynętami, skupem ryb, biurem zawodów i przystankiem.
  - *Zatoka Szuwarowa*: mokradło, wierzby, gęste trzciny, mgła, chata wędkarza, skup ryb, łodzie na brzegu.
  - *Staw Karpiowy* (poziom 3): łąka z kwiatami, gospodarstwo z wiatrakiem, stodołą i stogami siana.
  - *Górskie Jezioro* (poziom 4): sosny, skały, wysokie wzgórza ze śniegiem, górska chata.
- **Budynki** (`src/World/Props/BuildingFactory.cs`, wszystko z prymitywów): sklepy, domy, stodoła, wiatrak z obracającymi się skrzydłami, skup ryb ze stoiskiem i suszarnią, biuro z flagą, przystanek, latarnie, ławki, łodzie, płoty, skrzynie i beczki. Okna i latarnie świecą po zmroku.
- **Sklepy to budynki:** podejdź do drzwi i naciśnij E. Sklep wędkarski pokazuje sprzęt, sklep z przynętami przynęty, skup ryb sprzedaż i rekordy, przystanek zmianę łowiska, biuro zawodów turnieje (zakładka T działa też z daleka).
- **Dodawanie budynków:** wpis `"rodzaj,x,z[,obrót]"` w polu `Buildings` łowiska (np. `"tackle,-22,24"`). Budynki same obracają się twarzą do placu, teren pod nimi jest wyrównywany, a do drzwi prowadzi ścieżka.

## Struktura kodu
```
src/
  Core/         logika bez zależności od sceny, w pełni testowalna
                FightModel (hol), BiteModel (szanse brań), Progression (XP, wartość ryb), IRng
  Data/         zasoby danych (.tres): FishSpecies, LocationData, RodData, ReelData, LineData, HookData, BaitData
  Accounts/     UserStore (konta, hasze), Session (kto jest zalogowany, wybrana mapa)
  Progress/     Profile (pieniądze, XP, sprzęt, siatka, rekordy, dziennik, turnieje), ProfileSerializer (JSON), TackleLoadout
  World/        LakeBed (teren, głębokość, place pod budynki, ścieżki), Water, SkyController (niebo, deszcz, gwiazdy), WorldState (czas, pogoda),
                Scenery (pomost, wioska, roślinność wg biomu), InteractionManager (drzwi i klawisz E), Props/ (BuildingFactory, TreeFactory, tekstury)
  Fishing/      FishingRod (maszyna stanów połowu), Bobber, Player, RodRig, FishModel (proceduralna ryba)
  Tournaments/  TournamentData, TournamentSession (boty i ranking), TournamentManager
  UI/           Menu (logowanie, wybór mapy), Hud, GameUI (ekwipunek, mapa, dziennik, turnieje), Shop, PauseMenu, UITheme
data/           ryby, łowiska, sprzęt, turnieje (.tres): nowa ryba, mapa lub turniej to nowy plik
tests/          zestawy testów i narzędzie do zrzutów ekranu
```
Przy dodawaniu: nowa ryba = plik w `data/fish/` i wpis w `Main.SpeciesPaths`; nowa mapa = plik w `data/locations/` i wpis w `LakeBed.LocationPaths`; nowy sprzęt = plik w `data/tackle/...` i wpis w `TackleLoadout`; nowy turniej = plik w `data/tournaments/` i wpis w `TournamentManager.TournamentPaths`.

## Jak działa hol i branie
- **Hol** (`FightModel`, w kg-sile): ryba ciągnie z siłą zależną od gatunku i wagi. Hamulec puszcza żyłkę tylko wtedy, gdy ciągnięcie przekracza jego ustawienie. Napięcie to limit hamulca plus 25% nadwyżki (pomniejszone o amortyzację wędki), więc zrywy mogą przeciążyć żyłkę. Zbyt wysoki hamulec zrywa żyłkę, zbyt niski oddaje rybie całą szpulę. Im dłużej trwa hol, tym szybciej ryba traci siły.
- **Branie** (`BiteModel`): szansa gatunku = rzadkość x przynęta x dopasowanie haka x liczebność na łowisku x głębokość x pora dnia x pogoda. Ekran ekwipunku (I) pokazuje te szanse dla każdej głębokości.
- **Turnieje** (T): wpisowe, dojazd na łowisko, wynik to suma N najcięższych ryb, rywale to boty o różnych umiejętnościach. Nagrody (pieniądze i XP) według miejsca, historia w zapisie.

## Zapis gry
`user://saves/<login>.json` (pieniądze, XP, sprzęt, przynęty, siatka, rekordy, dziennik, historia turniejów, łowisko, godzina). Zapisy z wersji w GDScript wczytują się bez zmian, a hasze w `users.json` pozostają ważne.

## Testy
```
godot --headless --path . res://tests/run_tests.tscn              # wszystkie zestawy
godot --headless --path . res://tests/run_tests.tscn -- core game # wybrane zestawy
```
Zestawy: `core` (hol, brania, postęp), `accounts`, `progress` (profil, sklep, zapis, zgodność ze starymi zapisami), `world` (teren, czas, pogoda, fale), `game` (logowanie, wybór mapy, pauza, ekran gracza, cały cykl połowu), `tournaments`, `village` (układ wiosek, wyrównany teren, ścieżki, biomy, chodzenie, drzwi i sklepy). Kod wyjścia to liczba nieudanych sprawdzeń. W trybie headless zapis na dysk jest wyłączony.

Zrzuty ekranu (potrzebny ekran i OpenGL, np. `xvfb-run`):
```
godot --rendering-driver opengl3 --path . res://tests/screenshot.tscn -- GODZINA POGODA ETYKIETA PLIK YAW PITCH TRYB ARG MAPA
```
Tryby: 0 widok, 1 hol, 2 złowiona ryba, 3 sklep, 4 pauza, 5 ekran gracza, 6 logowanie, 7 wybór mapy, 8 turniej w toku, 9 wyniki turnieju.
