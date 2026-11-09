# Fishing Sim (RF4-like), wersja C#

**Styl graficzny: ilustracyjny pixel art 3D** (domyślny; w menu pauzy można przełączyć na „Ostry”): ciepła, ograniczona paleta, brązowe kontury brył, płaskie chmury, lampki nad pomostem, złote ryby na dachach skupu i targu, mewy.

**Wcześniej: medieval pixel art 3D.** Świat 3D jest renderowany w niskiej rozdzielczości i powiększany bez wygładzania, z ograniczoną paletą, ditheringiem i konturami (`src/Graphics/PixelArtView.cs`, `shaders/pixel_post.gdshader`), a HUD zostaje ostry. Wioska jest średniowieczna: domy z szachulcem i strzechą, latarnie na drewnianych słupach, drewniana wędka. Tekstury to proceduralny pixel art 32×32 z filtrowaniem nearest, a oświetlenie jest pasmowe (toon). Jakość grafiki (wielkość piksela, cienie, trawa, efekty) zmienia się w menu pauzy. Waluta to grosze (gr), sklepy to kramy, turnieje prowadzi gildia wędkarzy, a na inne łowisko jedzie się wozem.

**Minimalistyczny interfejs:** podczas zwykłego rozglądania na ekranie jest tylko mała kropka celownika. Stan wędki i cienki pasek napięcia pojawiają się w trakcie łowienia, sakiewka i siatka na chwilę po zmianie, a sprzęt na chwilę po przełączeniu. Tab pokazuje wszystko naraz, H sterowanie.

Symulator wędkarski w Godot 4.3 (.NET) napisany w całości w C#. Poza kodem są shadery (`shaders/*.gdshader`: niebo, woda, teren, liście, trawa, gwiazdy, efekt pikseli), bo shadery w Godocie piszą się w jego własnym języku, oraz pliki scen i danych (`.tscn`, `.tres`).

## Wymagania i uruchomienie
- **Godot 4.3 w wersji .NET (Mono)** i **.NET SDK 8**.
- Otwórz `project.godot` w Godocie .NET, zbuduj (Alt+B albo przycisk Build), potem F5.
- Logowanie: `admin` / `admin` (jedyne konto, tworzone przy pierwszym uruchomieniu). Hasła są zapisywane jako hasz z solą (`src/Accounts/UserStore.cs`).
- Bez edytora: `dotnet build`, potem `godot --path .`.

## Sterowanie
| Klawisz | Działanie |
|---|---|
| WASD, mysz | ruch po całej mapie, rozglądanie |
| LPM (przytrzymaj / puść) | ładowanie i rzut; potem zwijanie linki |
| PPM (przytrzymaj) | podniesienie wędki: zacięcie przy braniu, pompowanie w holu, a gdy ryba jest przy brzegu, podciąganie jej do góry. Ryba jest złowiona, gdy dotknie wędkarza; ciężka zerwie przy tym linkę |
| Spacja | kasarek (podbierak): wyjmuje rybę przy brzegu. Trzeba go kupić; bez kasarka Spacja nic nie robi |
| Kółko myszy | hamulec (wartość w procentach w prawym dolnym rogu) |
| R (zaraz po złowieniu) | wypuszczenie ryby (zwalnia miejsce w siatce, +3 XP) |
| 1-5 (Shift = wstecz) | wędka, kołowrotek, żyłka, hak, przynęta (tylko przed rzutem) |
| I / M / J / T | ekwipunek, mapa, dziennik, turnieje |
| E | wejście do kramu, gildii, na targ lub na postój wozów (przy drzwiach budynku) |
| Shift | bieg |
| Esc | menu pauzy (wznów, zmień mapę, jakość grafiki, wyloguj, wyjdź) |
| Tab (przytrzymaj) | stan: godzina, pogoda, łowisko, sakiewka, siatka, sprzęt |
| H | sterowanie |
| F1 | konsola deweloperska: F2 natychmiastowe branie, F3 nieśmiertelna żyłka, F4 gatunek, F5 reset rzutu, F6 przynęty, F7 +3 godz., F8 pogoda, F9 reset postępu, F10 +1000 gr |

## Świat
- **Cała mapa jest do chodzenia:** brzeg, wzgórza, wioska. Wchodzić można tylko w płytką wodę (do 0,5 m), a pomost jest jednym z miejsc do łowienia. Łowić można z dowolnego miejsca na brzegu.
- **Cztery zróżnicowane mapy** (`data/locations/*.tres`), każda z własnym biomem (kolory terenu i wody, mgła, wysokość wzgórz, gatunki i gęstość drzew, krzewy, kwiaty, trzciny, skały) i własną wioską:
  - *Jezioro Zielone*: las mieszany (dąb, brzoza, świerk), wioska z kramem wędkarskim, kramem z przynętami, skupem ryb, targiem rybnym, gildią wędkarzy i postojem wozów.
  - *Zatoka Szuwarowa*: mokradło, wierzby, gęste trzciny, mgła, chata wędkarza, skup ryb, łodzie na brzegu.
  - *Staw Karpiowy* (poziom 3): łąka z kwiatami, gospodarstwo z wiatrakiem, stodołą i stogami siana.
  - *Górskie Jezioro* (poziom 4): sosny, skały, wysokie wzgórza ze śniegiem, górska chata.
- **Budynki** (`src/World/Props/BuildingFactory.cs`, wszystko z prymitywów): kramy i domy z szachulcem i strzechą, stodoła, wiatrak z obracającymi się skrzydłami, skup ryb ze stoiskiem i suszarnią, gildia z chorągwią, wóz z plandeką przy drogowskazie, latarnie na słupach (migoczą po zmroku), ławki, łodzie, płoty, skrzynie i beczki. Okna i latarnie świecą po zmroku.
- **Kramy to budynki:** podejdź do drzwi i naciśnij E. Kram wędkarski sprzedaje sprzęt (wędki, kołowrotki, linki, haczyki, siatki, kasarki, akcesoria), kram z przynętami przynęty, skup ryb kupuje po stałej cenie, targ rybny (Jezioro Zielone) płaci według cen dnia z premią za świeże i trofeowe ryby, na postoju wozów zmienia się łowisko, a gildia wędkarzy prowadzi turnieje (zakładka T działa też z daleka).
- **Siatka i podbierak:** siatka ma limit ryb i kilogramów (pełna = ryba wypuszczona), a ryby tracą w niej kondycję. Na start nie masz kasarka: małą rybę podciągasz wędką (PPM) aż do siebie, dużą trzeba wyjąć kasarkiem (Spacja), bo przy podnoszeniu zerwie linkę. Za mały kasarek też jej nie utrzyma.
- **40 gatunków słodkowodnych** (wyłącznie rodzime, europejskie): płoć, leszcz, karp, szczupak, okoń, sandacz, sum, lin, karaś złocisty i srebrzysty, krąp, wzdręga, jaź, kleń, boleń, brzana, świnka, ukleja, kiełb, jelec, certa, sapa, rozpiór, węgorz, miętus, pstrąg potokowy, palia, lipień, głowacica, troć jeziorowa, sielawa, sieja, jazgarz, łosoś, ciosa, strzebla potokowa, różanka, piskorz, głowacz białopłetwy i jesiotr ostronosy. Każdy ma swoje wagi, głębokości, porę żerowania, ulubione przynęty, siłę w holu, wygląd i cenę. Łowiska różnią się składem: jezioro nizinne, zarośnięta zatoka (lin, karaś, wzdręga, węgorz, piskorz), staw karpiowy i czyste górskie jezioro (pstrąg, palia, lipień, sielawa, sieja, głowacica, troć). Nowe przynęty: czerwie i koniki polne.
- **Przejrzystość wody zależy od łowiska** (`WaterClarity`): w górach widać dno na kilka metrów, w zatoce woda jest mętna.
- **Ryby widać w wodzie:** około 150 ryb pływa w ławicach i pojedynczo; ławice, które odpłyną daleko, wracają w okolice wędkarza, więc wokół zawsze coś pływa. Uciekają przed biegnącym wędkarzem. Kapelusz z szerokim rondem (mniej odblasków) pokazuje je dalej i głębiej.
- **Tylko widok z oczu wędkarza:** kołysanie głowy w rytm kroków, oddech, widoczne ręce na wędce i kołowrotku.
- **Dźwięk bez plików audio** (`src/Core/Audio/Synth.cs`, `src/Audio/SoundEngine.cs`): wiatr, chlupot wody przy brzegu, deszcz, ptaki w dzień, świerszcze nocą, kroki (inne na pomoście, inne na trawie), plusk spławika, terkot kołowrotka, jazgot hamulca, zacięcie, złowienie i zerwanie. Wszystko jest syntezowane w kodzie.
- **Fizyka wędki:** blank z 10 sprężystych segmentów gnie się w stronę żyłki (także w bok, gdy ryba odchodzi w bok), z bezwładnością i drganiami (podgryzanie, branie, zacięcie, rzut). Każda wędka ma akcję: leszczynowy kij gnie się szczytówką, dębowa tyka całym blankiem (`src/Core/Physics/RodBendModel.cs`).
- **Sklepy z obrazkami:** karty towarów z pikselowymi ikonami (każdy przedmiot i każda ryba ma swój obrazek, `src/Graphics/Icons.cs`), panel szczegółów z paskami parametrów porównanych z używanym sprzętem, galeria rekordów (niezłowione gatunki jako sylwetki), pocztówki łowisk na postoju wozów.
- **Ekran logowania i wyboru łowiska:** malowane pikselowe pocztówki łowisk (`src/Graphics/Postcard.cs`) jako przenikające się tło i jako zdjęcia na kartach map.
- **Wymiar ochronny:** najmniejsze ryby każdego gatunku są niewymiarowe. Przy złowieniu gra ostrzega, a za każdą niewymiarową rybę sprzedaną w skupie lub na targu strażnik pobiera 10 gr kary. Wypuść ją klawiszem R.
- **Brania od widocznych ryb:** ryby, które lubią przynętę, podpływają do spławika, a bierze konkretna ryba z okolicy. Spłoszone nie biorą, zerwana ryba ucieka.
- Plan dalszej rozbudowy: [`docs/ROZWOJ.md`](docs/ROZWOJ.md).
- **Dodawanie budynków:** wpis `"rodzaj,x,z[,obrót]"` w polu `Buildings` łowiska (np. `"tackle,-22,24"`). Budynki same obracają się twarzą do placu, teren pod nimi jest wyrównywany, a do drzwi prowadzi ścieżka.

## Struktura kodu
```
src/
  Core/         logika bez zależności od sceny, w pełni testowalna
                FightModel (hol), BiteModel (szanse brań), Progression (XP, wartość ryb), IRng,
                Market/FishMarket (targ), Gear/ (siatka, podbierak), Animation/BodyMotion (ruch ciała), FishAI/FishSwimModel (ławice)
  Data/         zasoby danych (.tres): FishSpecies, LocationData, RodData, ReelData, LineData, HookData, BaitData,
                KeepnetData, LandingNetData, AccessoryData
  Accounts/     UserStore (konta, hasze), Session (kto jest zalogowany, wybrana mapa)
  Progress/     Profile (pieniądze, XP, sprzęt, siatka, rekordy, dziennik, turnieje, targ), ProfileSerializer (JSON), TackleLoadout,
                GearInventory + GearCatalog (siatki, podbieraki, akcesoria)
  World/        LakeBed (teren, głębokość, place pod budynki, ścieżki), Water, SkyController (niebo, deszcz, gwiazdy), WorldState (czas, pogoda),
                Scenery (pomost, wioska, roślinność wg biomu), InteractionManager (drzwi i klawisz E), Props/ (BuildingFactory, TreeFactory, tekstury),
                FishPopulation (ryby widoczne w wodzie)
  Fishing/      FishingRod (maszyna stanów połowu), Bobber, Player, FirstPersonBody (ręce), RodRig, FishModel (proceduralna ryba)
  Tournaments/  TournamentData, TournamentSession (boty i ranking), TournamentManager
  UI/           Menu (logowanie, wybór mapy), Hud, GameUI (ekwipunek, mapa, dziennik, turnieje), Shop, PauseMenu, UITheme
data/           ryby, łowiska, sprzęt, turnieje (.tres): nowa ryba, mapa lub turniej to nowy plik
tests/          zestawy testów i narzędzie do zrzutów ekranu
```
Przy dodawaniu: nowa ryba = plik w `data/fish/`, wpis w `SpeciesCatalog.Paths` i wpis `Abundance` w łowiskach, gdzie żyje; nowa mapa = plik w `data/locations/` i wpis w `LakeBed.LocationPaths`; nowy sprzęt = plik w `data/tackle/...` i wpis w `TackleLoadout` (siatki, podbieraki, akcesoria: w `GearCatalog`); nowy turniej = plik w `data/tournaments/` i wpis w `TournamentManager.TournamentPaths`.

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
Zestawy: `core` (hol, brania, postęp), `accounts`, `progress` (profil, sklep, zapis, zgodność ze starymi zapisami), `world` (teren, czas, pogoda, fale), `game` (logowanie, wybór mapy, pauza, ekran gracza, cały cykl połowu), `tournaments`, `village` (układ wiosek, wyrównany teren, ścieżki, biomy, chodzenie, drzwi i sklepy), `gear` (targ, siatki, podbieraki, akcesoria, animacja ciała, ryby w wodzie). Kod wyjścia to liczba nieudanych sprawdzeń. W trybie headless zapis na dysk jest wyłączony.

Zrzuty ekranu (potrzebny ekran i OpenGL, np. `xvfb-run`):
```
godot --rendering-driver opengl3 --path . res://tests/screenshot.tscn -- GODZINA POGODA ETYKIETA PLIK YAW PITCH TRYB ARG MAPA
```
Tryby: 0 widok, 1 hol, 2 złowiona ryba, 3 sklep, 4 pauza, 5 ekran gracza, 6 logowanie, 7 wybór mapy, 8 turniej w toku, 9 wyniki turnieju.
