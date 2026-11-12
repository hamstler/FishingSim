# Fishing Sim (RF4-like), wersja C#

Wolna gra na licencji **GNU GPL 3.0 lub nowszej**: możesz ją kopiować, zmieniać i rozpowszechniać, a zmienioną wersję udostępniasz na tej samej licencji razem z kodem źródłowym. Szczegóły w sekcji [Licencja](#licencja) i w pliku [`LICENSE`](LICENSE).

**Styl graficzny: nordycki, zbliżony do Valheima** (jedyny): pełna rozdzielczość z ostrymi, ręcznie malowanymi w kodzie teksturami, gęsta mgła wolumetryczna z promieniami słońca, cienie i okluzja otoczenia, poświata jasnych miejsc, chłodne cienie i ciepłe światło, jasne księżycowe noce. Wioska jest średniowieczna: domy z szachulcem i strzechą, latarnie na drewnianych słupach, drewniana wędka. Waluta to grosze (gr), sklepy to kramy, turnieje prowadzi gildia wędkarzy, a na inne łowisko jedzie się wozem.

**Doba:** dzień (06:00-18:00) trwa 40 minut, noc 20 minut czasu rzeczywistego (`src/Core/DayClock.cs`). W prawym górnym rogu jest minimapa (120 m wokół gracza) i zegar z tarczą dnia i nocy, pogodą i czasem do zmroku lub świtu.

**Ryby:** 66 gatunków (rodzime i zadomowione), każdy z nazwą łacińską i opisem. Skóry są malowane w kodzie piksel po pikselu: łuski z ciemnymi krawędziami, linia boczna, pokrywa skrzelowa, plamki z obwódkami, pręgi, marmurkowanie, lustra karpia, tarczki jesiotra, plus mapa normalnych. Ryba na haku, w dłoni i w atlasie dostaje skórę 4096×2048 (2048×1024 na niskiej jakości), ryby w wodzie 512×256. **Atlas ryb** jest w menu głównym i w menu pauzy: obracający się model 3D, opis, wagi, cena, przynęty, głębokość, pora i łowiska, wyszukiwarka i filtr łowiska.

**Hol:** ryba ma prawdziwą pozycję, kierunek i prędkość: płynie najwyżej z prędkością zrywu wynikającą z długości ciała (płoć ok. 1 m/s, duży karp czy szczupak 3 m/s), skręca z ograniczoną szybkością i kołysze się na napiętej lince jak wahadło, zamiast przeskakiwać. Linkę zabiera tylko wtedy, gdy przeciąga hamulec, a wędkarz zyskuje linkę powoli, póki ryba ciągnie. Walka zaczyna się od pierwszego odjazdu zaraz po zacięciu, wędka szarpie przy każdym zrywie, a zmęczenie zależy od tego, jak mocno ryba walczy z hamulcem. Płoć to ok. pół minuty, karp 12 kg ok. 2 minuty na sprzęcie karpiowym (na teleskopie wyciąga całą linkę). Startowy zestaw jest słaby (kij 2,4 kg, hamulec 2 kg).

**Sprzęt:** 10 wędek (spławikowe, spinningowe, gruntowe, karpiowe i sumowe), 7 kołowrotków, żyłki z włosia, jelita i jedwabiu oraz plecionki z lnu, konopi i jedwabiu, 6 przyponów, 7 haczyków, 26 przynęt i akcesoria. Żyłka rozciąga się i tłumi zrywy ryby, plecionka jest mocniejsza i pewniej zacina, ale zrywy idą prosto w wędkę. Przypon to część linki: cienki z włosia czy jelita jest mało widoczny (szybsze brania), ale słabszy, a drut chroni przed zębami: szczupak, sum i sandacze potrafią przegryźć linkę bez drucianego przyponu. Przynęty sztuczne (błystki, rybki, przynęty miękkie, muchy) nie zużywają się, giną tylko z haczykiem przy zerwaniu i biorą tylko wtedy, gdy się je zwija (LPM), szybciej na wędce spinningowej. Każdy gatunek ma swoje upodobania (szczupak: błystki wahadłowe i rybki, sandacz: przynęty miękkie, lipień i pstrąg: muchy). Wodery pozwalają wejść głębiej do wody, osełka poprawia zacięcie, a smalec przyspiesza kołowrotek.

**Zniszczony sprzęt:** złamana wędka znika z ekwipunku i z ręki, przy zerwanej żyłce przepada haczyk, a gdy ryba wyciągnie całą linkę, przepada żyłka. Bez kompletnego zestawu nie da się zarzucić. W kramie wędkarskim można odebrać darmowy zestaw, ale tylko wtedy, gdy brakujących części nie da się kupić za pieniądze z sakiewki.

**Modele ryb:** każdy gatunek ma własną budowę (`src/Fishing/FishBody.cs`): wysokość i szerokość ciała, garb, płaski brzuch, długość pyska, szeroka lub płaska głowa, trzon ogonowy, kształt ogona (od zaokrąglonego po głęboko wcięty i rekinowy u jesiotrów), rodzaj płetwy grzbietowej (miękka, kolczasta, żagiel lipienia, długa karpia, ciągła węgorza, kolce ciernika), płetwa tłuszczowa, wąsy i oko dopasowane do głowy.

**Ryby z bliska:** ryba na haku, w dłoni i w atlasie ma skórę 4096×2048 z mapą wypukłości łusek, płetwy malowane promień po promieniu (przezroczysta błona, postrzępiona krawędź, wzory gatunku: czarna plama okonia, cętki szczupaka, białe krawędzie płetw pstrąga, żagiel lipienia), szparę pyska z wargami, nozdrza, wypukłą pokrywę skrzelową, oczodół i oko z kolorową tęczówką pod błyszczącą rogówką. Całe ciało z płetwami wygina się jedną falą podczas pływania (shadery `fish_body`/`fish_fin`).

**Ryby według prawdziwych okazów:** kształt ciała i barwy (grzbiet, bok, brzuch) części gatunków są zmierzone na wolnych zdjęciach z Wikimedia Commons (`assets/fish`, narzędzia w `tools/fish_photos`), a ryba nadal jest malowana w stylu gry; same zdjęcia nie trafiają do gry, autorzy są w `assets/fish/CREDITS.md` i w atlasie. **Ryba rośnie z wagą:** cięższa jest dłuższa, wyższa i grubsza, młoda smukła z większymi oczami, a złowioną rybę trzymasz w prawdziwej wielkości.

**Modele ryb z Blendera:** każdy z 66 gatunków ma własny model 3D (`assets/fish/models/<id>.glb`). Proporcje ciała, każdej płetwy i oka są zmierzone na ilustracjach z AtlasRyb.online (bez kopiowania samych ilustracji; gatunki, których atlas nie ma, biorą proporcje od najbliższego krewnego albo z tabeli budowy gatunku), a głowa (pysk z wargami, kość szczęki, pokrywa skrzelowa, nozdrza) jest przeniesiona z dopracowanego modelu okonia. Skóra jest malowana w kodzie: nachodzące na siebie łuski albo gładka skóra, wzory gatunku (prążki, cętki, tęczowy pas, marmurkowanie, tarczki jesiotra, lustra karpia), mapa wypukłości; płetwy z promieniami, wąsiki, oczy pod rogówką. Grzbiet i brzuch pod płetwami biegną gładkim łukiem (bez garbów przy płetwach). Pysk jest rzeźbiony w proporcji do wielkości gęby: wargi i szpara gęby przechodząca przez czubek pyska, zaokrąglony nos; oczy siedzą płasko w oczodołach pod przezroczystą rogówką, a tęczówka jest metaliczna aż po brzeg. Tekstury 4K: skóra 4096×2048 z pełną mapą wypukłości, łuski z ostrym brzegiem, słojami przyrostu i pigmentem, płetwy 1024×512; zapisane raz w WebP z bezstratnych źródeł i wczytywane z mipmapami, więc ryba z bliska jest ostra, a z daleka nie migocze. Narzędzia: `tools/fish_model` (`build_all.sh` przebudowuje wszystko).

**Tekstury QHD:** tekstury świata mają 1024 px na 2 m i filtrowanie anizotropowe; malują się wielowątkowo i są zapisywane w pamięci podręcznej (`user://texcache`). Okno startuje zmaksymalizowane, a świat renderuje się w natywnej rozdzielczości.

**Interfejs:** rzeźbione dębowe ramy z mosiężnymi ćwiekami, przyciski z desek, pergaminowe karty i złocone paski, wszystko malowane w kodzie. W kramie boczny pasek kategorii z dużymi obrazkami, podkategorie (np. wędki spławikowe, spinningowe, gruntowe, karpiowe; przynęty naturalne, roślinne, karpiowe, sztuczne), wyszukiwarka, sortowanie i filtr „Dostępne”; karty mają wstążkę jakości (zwykły, dobry, wyborny, mistrzowski), pieczęć posiadanego i gwiazdkę używanego przedmiotu, kłódkę na zablokowanych, rosną pod kursorem, a zakup kończy się deszczem monet i brzękiem. Obrazki przedmiotów są gładkie (128 px), z cieniem.

**Minimalistyczny interfejs:** podczas zwykłego rozglądania na ekranie jest tylko mała kropka celownika. Stan wędki i cienki pasek napięcia pojawiają się w trakcie łowienia, sakiewka i siatka na chwilę po zmianie, a sprzęt na chwilę po przełączeniu. Tab pokazuje wszystko naraz, H sterowanie.

Symulator wędkarski w Godot 4.3 (.NET) napisany w całości w C#. Poza kodem są shadery (`shaders/*.gdshader`: niebo, woda, teren, liście, trawa, gwiazdy, deszcz, wodospad), bo shadery w Godocie piszą się w jego własnym języku, oraz pliki scen i danych (`.tscn`, `.tres`).

## Budowanie i uruchamianie

### Czego potrzebujesz
- **Godot 4.3 w wersji .NET** (na stronie Godota: „Godot Engine - .NET”, wersja 4.3-stable). Zwykły Godot bez .NET nie
  zbuduje gry, bo cały kod jest w C#.
- **.NET SDK 8.0** (`dotnet --version` powinno pokazać 8.x).
- **Git**, żeby pobrać repozytorium. Repozytorium z całą historią zajmuje ok. 750 MB, głównie przez modele ryb
  (`assets/fish/models`, ok. 350 MB w bieżącej wersji).

### Pobranie i pierwsze uruchomienie
Sama bieżąca wersja, bez historii (ok. 350 MB):
```bash
git clone --depth 1 https://github.com/hamstler/FishingSim.git
```
Z całą historią (potrzebna, jeśli chcesz zgłaszać zmiany):
```bash
git clone https://github.com/hamstler/FishingSim.git
```
1. Uruchom Godota .NET, wybierz **Import** i wskaż plik `project.godot` w pobranym katalogu.
2. Przy pierwszym otwarciu edytor importuje zasoby, co może potrwać kilka minut.
3. Zbuduj kod C#: przycisk **Build** w prawym górnym rogu albo **Alt+B**.
4. Uruchom grę klawiszem **F5**.

Logowanie: `admin` / `admin` (jedyne konto, tworzone przy pierwszym uruchomieniu). Hasła są zapisywane jako hasz z solą
(`src/Accounts/UserStore.cs`).

Bez edytora, z wiersza poleceń (`godot` to plik wykonywalny Godota .NET, u Ciebie może mieć dłuższą nazwę):
```bash
dotnet build
```
```bash
godot --path .
```

### Zbudowanie gry do pliku wykonywalnego (eksport)
1. Zainstaluj szablony eksportu dla tej samej wersji: w edytorze **Editor → Manage Export Templates → Download and
   Install** (szablony „4.3.stable.mono”).
2. **Project → Export**: projekt ma gotowe ustawienia dla **Windows** i **Linux** (`export_presets.cfg`). Wybierz jedno
   i kliknij **Export Project**. Gra trafi do `build/windows/` albo `build/linux/` (katalog `build/` nie jest w
   repozytorium).

To samo z wiersza poleceń (najpierw utwórz katalog docelowy):
```bash
mkdir -p build/windows build/linux
```
```bash
godot --headless --path . --export-release "Windows" build/windows/FishingSim.exe
```
```bash
godot --headless --path . --export-release "Linux" build/linux/FishingSim.x86_64
```
Zbudowana gra to plik wykonywalny, plik `.pck` z zasobami (ok. 350 MB, głównie modele ryb) i katalog
`data_FishingSim_*` z bibliotekami .NET. Rozpowszechniaj je razem.

Ustawienia eksportu dołączają katalog `assets/fish` (modele ryb i pomiary gatunków, gra wczytuje je dopiero w trakcie
działania) i pomijają `tools/`, `tests/` i `docs/`. Modele `.glb` mają pliki `.import` z `importer="keep"`, więc edytor
ich nie importuje, a eksport dołącza je bez zmian.

### Przebudowa modeli ryb (opcjonalnie)
Gotowe modele są w repozytorium, więc do zbudowania gry nie są potrzebne żadne dodatkowe narzędzia. Kto chce zmienić
wygląd ryb, potrzebuje Blendera 4.0 i Pythona 3 z `numpy` i `Pillow` (do ponownego mierzenia ilustracji także
`opencv-python`):
```bash
tools/fish_model/build_all.sh karp lin
```
Bez nazw gatunków skrypt przebudowuje wszystkie 66 po kolei, co trwa ponad godzinę i dokłada ok. 270 MB do historii
repozytorium, więc lepiej przebudowywać tylko zmienione gatunki. Opis całego procesu jest w `CLAUDE.md` (sekcja „Species models”).

## Sterowanie
| Klawisz | Działanie |
|---|---|
| WASD, mysz | ruch po całej mapie, rozglądanie |
| LPM (przytrzymaj / puść) | ładowanie i rzut; potem zwijanie linki |
| PPM (przytrzymaj) | podniesienie wędki: zacięcie przy braniu, pompowanie w holu, a gdy ryba jest przy brzegu, podciąganie jej do góry. Ryba jest złowiona, gdy dotknie wędkarza; ciężka zerwie przy tym linkę |
| Spacja | kasarek (podbierak): wyjmuje rybę przy brzegu. Trzeba go kupić; bez kasarka Spacja nic nie robi |
| Kółko myszy | hamulec (wartość w procentach w prawym dolnym rogu) |
| E / R (zaraz po złowieniu) | ryba do siatki / do wody (+3 XP) |
| 1-6 (Shift = wstecz) | wędka, kołowrotek, żyłka, hak, przynęta, przypon (tylko przed rzutem) |
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
- **66 gatunków słodkowodnych.** Pierwsze 40 to rodzime: płoć, leszcz, karp, szczupak, okoń, sandacz, sum, lin, karaś złocisty i srebrzysty, krąp, wzdręga, jaź, kleń, boleń, brzana, świnka, ukleja, kiełb, jelec, certa, sapa, rozpiór, węgorz, miętus, pstrąg potokowy, palia, lipień, głowacica, troć jeziorowa, sielawa, sieja, jazgarz, łosoś, ciosa, strzebla potokowa, różanka, piskorz, głowacz białopłetwy i jesiotr ostronosy. Każdy ma swoje wagi, głębokości, porę żerowania, ulubione przynęty, siłę w holu, wygląd i cenę. Łowiska różnią się składem: jezioro nizinne, zarośnięta zatoka (lin, karaś, wzdręga, węgorz, piskorz), staw karpiowy i czyste górskie jezioro (pstrąg, palia, lipień, sielawa, sieja, głowacica, troć). Nowe przynęty: czerwie i koniki polne. Kolejne 26: pstrąg tęczowy i źródlany, troć wędrowna, amur, tołpyga biała i pstra, sazan, karp lustrzeń, sumik karłowaty i kanałowy, bass wielkogębowy, sandacz wołżański, czop, brzanka, kiełb białopłetwy, śliz, koza, babka bycza, ciernik, cierniczek, słonecznica, strzebla błotna, głowacz pręgopłetwy, stynka, sterlet i orfa.
- **Przejrzystość wody zależy od łowiska** (`WaterClarity`): w górach widać dno na kilka metrów, w zatoce woda jest mętna.
- **Ryby widać w wodzie:** około 150 ryb pływa w ławicach i pojedynczo; ławice, które odpłyną daleko, wracają w okolice wędkarza, więc wokół zawsze coś pływa. Uciekają przed biegnącym wędkarzem. Kapelusz z szerokim rondem (mniej odblasków) pokazuje je dalej i głębiej.
- **Tylko widok z oczu wędkarza:** kołysanie głowy w rytm kroków, oddech, widoczne ręce na wędce i kołowrotku.
- **Dźwięk bez plików audio** (`src/Core/Audio/Synth.cs`, `src/Audio/SoundEngine.cs`): wiatr, chlupot wody przy brzegu, deszcz, ptaki w dzień, świerszcze nocą, kroki (inne na pomoście, inne na trawie), plusk spławika, terkot kołowrotka, jazgot hamulca, zacięcie, złowienie i zerwanie. Wszystko jest syntezowane w kodzie.
- **Fizyka wędki:** blank z 10 sprężystych segmentów gnie się w stronę żyłki (także w bok, gdy ryba odchodzi w bok), z bezwładnością i drganiami (podgryzanie, branie, zacięcie, rzut). Każda wędka ma akcję: leszczynowy kij gnie się szczytówką, dębowa tyka całym blankiem (`src/Core/Physics/RodBendModel.cs`).
- **Sklepy z obrazkami:** karty towarów z ikonami (każdy przedmiot i każda ryba ma swój obrazek, `src/Graphics/Icons.cs`), panel szczegółów z paskami parametrów porównanych z używanym sprzętem, galeria rekordów (niezłowione gatunki jako sylwetki), pocztówki łowisk na postoju wozów.
- **Ekran logowania i wyboru łowiska:** malowane pocztówki łowisk (`src/Graphics/Postcard.cs`) jako przenikające się tło i jako zdjęcia na kartach map.
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

Zrzuty ekranu (potrzebny ekran, bez monitora np. `xvfb-run`; renderer Vulkan, ok. 30 s na zrzut):
```
godot --rendering-driver vulkan --path . res://tests/screenshot.tscn -- GODZINA POGODA ETYKIETA PLIK YAW PITCH TRYB ARG MAPA
```
Tryby: 0 widok, 1 hol, 2 złowiona ryba, 3 sklep, 4 pauza, 5 ekran gracza, 6 logowanie, 7 wybór mapy, 8 turniej w toku, 9 wyniki turnieju.

## Licencja

Copyright (C) 2026 hamstler i współtwórcy FishingSim.

FishingSim jest wolnym oprogramowaniem: możesz go rozpowszechniać i zmieniać na warunkach **GNU General Public
License** w wersji 3 albo (według Twojego wyboru) dowolnej późniejszej wersji, opublikowanej przez Free Software
Foundation. Program jest udostępniany bez żadnej gwarancji. Pełny tekst licencji jest w pliku [`LICENSE`](LICENSE)
(oficjalny tekst angielski jest jedynym wiążącym).

Licencja obejmuje cały projekt: kod C#, shadery, sceny i dane (`.tscn`, `.tres`, `.json`), narzędzia w `tools/` oraz
modele i tekstury ryb wygenerowane tymi narzędziami.

**Co wolno:** używać gry w dowolnym celu (także komercyjnym), kopiować ją, badać kod, zmieniać go i rozpowszechniać,
odpłatnie lub za darmo.

**Co trzeba**, gdy rozpowszechniasz grę albo jej zmienioną wersję (udostępniasz plik, sprzedajesz, wrzucasz do
internetu):
- udostępnić całą wersję na tej samej licencji GPL 3.0 lub nowszej, razem z jej kodem źródłowym (albo pisemną ofertą
  jego udostępnienia);
- zachować informacje o prawach autorskich i licencji, a zmienione pliki oznaczyć jako zmienione, z datą zmiany;
- nie dodawać ograniczeń, których GPL nie przewiduje (np. zakazu dalszego kopiowania).

Zmian, których nie rozpowszechniasz (np. własna wersja do grania w domu), nie musisz nikomu udostępniać.

**Wkład w projekt:** wysyłając zmiany (pull request), zgadzasz się, że są one udostępniane na tej samej licencji
(GPL 3.0 lub nowszej).

### Elementy i materiały innych autorów
- **Silnik Godot** (licencja MIT, © Juan Linietsky, Ariel Manzur i współtwórcy Godot Engine) i **.NET** (licencja MIT,
  © .NET Foundation): wbudowane w zbudowaną grę. Licencja MIT jest zgodna z GPL. Rozpowszechniając zbudowaną grę,
  dołącz informację o licencji Godota (<https://godotengine.org/license>), np. w pliku obok gry lub w napisach.
- **Proporcje ryb** zmierzono na ilustracjach z [AtlasRyb.online](https://atlasryb.online) (licencja CC BY-NC-SA).
  Do projektu trafiły wyłącznie liczby (obrysy ciała, położenie płetw i oka, barwy), nie ilustracje. Źródła każdego
  gatunku są w [`assets/fish/CREDITS.md`](assets/fish/CREDITS.md).
- **Barwy i kształty części gatunków** zmierzono na zdjęciach z Wikimedia Commons na wolnych licencjach. Same zdjęcia
  nie są częścią projektu; autorzy i licencje są w `assets/fish/CREDITS.md` i w atlasie ryb w grze.
- Dźwięki są syntezowane w kodzie, tekstury malowane w kodzie, a czcionka to wbudowana czcionka Godota: projekt nie
  zawiera plików graficznych ani dźwiękowych innych autorów.
