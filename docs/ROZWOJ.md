# Rozwój: pełnoprawny symulator wędkarski

Ten dokument opisuje strukturę rozbudowy gry do pełnego symulatora: co już działa, gdzie to leży w kodzie i co
jest następne w kolejce. Zasada pozostaje ta sama co w reszcie projektu: **reguły gry są czystą logiką w `src/Core`**
(bez typów Godota, z wstrzykiwanym `IRng`, testowalne), **dane to pliki `.tres`**, a węzły sceny tylko je wyświetlają.

## Filary

| Filar | Stan | Logika (`src/Core`) | Dane | Scena / UI |
|---|---|---|---|---|
| Sklep ze sprzętem | działa | `Progression` | `data/tackle/*` | `UI/Shop.cs` (zakładki wędki…haki, siatki, podbieraki, akcesoria) |
| Targ rybny | działa (podstawa) | `Market/FishMarket.cs` | `FishSpecies.TrophyWeight` | budynek `market`, `Shop` → zakładka *Targ* |
| Skup ryb (stała cena) | działa | `Progression.FishValue` | `FishSpecies.PricePerKg` | budynek `fishbuyer` |
| Siatki | działa | `Gear/KeepnetModel.cs` | `data/tackle/keepnets/` | limit w `Profile.AddCatch`, kondycja w `Profile.PassTime` |
| Podbieraki | działa | `Gear/LandingModel.cs` | `data/tackle/landingnets/` | `FishingRod` (koniec holu) |
| Akcesoria | działa: okulary; reszta to szkielet | – | `data/tackle/accessories/` | `GearCatalog.Has(...)`, `Main.ApplyGearEffects` |
| Otwarty świat, chodzenie | działa | – | `data/locations/` | `World/LakeBed`, `Fishing/Player` |
| Animacje człowieka (FPP) | działa: proceduralne | `Animation/BodyMotion.cs` | – | `Fishing/FirstPersonBody.cs` |
| Tylko widok pierwszoosobowy | zasada projektu | – | – | `Player` ma jedną kamerę, brak trybu TPP |
| Unikalne łowiska | działa | – | `LocationData.Squareness/Lobes/Islands/Landmarks` | `LakeBed.InsideMetres`, `Props/Landmarks.cs`, `Graphics/Postcard.cs` |
| Ryby widoczne w wodzie | działa | `FishAI/FishSwimModel.cs` | obfitość z `LocationData` | `World/FishPopulation.cs`, `FishModel.SetUnderwater` |

## Jak to działa

### Ekonomia: skup i targ
- **Skup ryb** płaci zawsze `waga × cena/kg` (stała wartość zapisana przy złowieniu). Szybko i bez ryzyka.
- **Targ rybny** (`FishMarket`) płaci premię ×1,25, ale cena zależy od:
  - **popytu**: każdy sprzedany kilogram gatunku obniża jego cenę o 3% (do minimum 35%), popyt wraca o 5% na godzinę gry,
  - **dnia**: deterministyczne wahanie ±15% na gatunek i dzień,
  - **trofeum**: ×1,2 od 75% wagi trofealnej, ×1,5 dla trofeum (`FishSpecies.TrophyWeight`, domyślnie 80% `MaxWeight`),
  - **kondycji**: ryba trzymana długo w siatce traci kondycję, martwa ryba jest warta 30%.
- Stan targu (nasycenie, zegar) jest zapisywany w profilu (`"market"` w JSON).

### Siatki i podbieraki
- Siatka ma limit sztuk i kilogramów. Gdy jest pełna, złowiona ryba jest wypuszczana: XP, dziennik i rekordy liczą się
  nadal, ale nie ma jej czego sprzedać.
- Ryby w siatce tracą kondycję z czasem; ciasna siatka (wypełnienie wagowe) przyspiesza to do 2,5×.
- Na końcu holu `LandingModel` rozstrzyga, czy rybę udało się wyjąć: do 1,2 kg ręką bez ryzyka, większe potrzebują
  podbieraka o wystarczającym udźwigu; ryba z zapasem sił (stamina) szarpie się bardziej.
- Nowy gracz (i stare zapisy) dostaje małą siatkę i mały podbierak.

### Animacja pierwszoosobowa
`BodyMotion` liczy z prędkości i czynności (rzut, hol, lądowanie) pozę na każdą klatkę: bujanie głowy w rytm
kroków (~1,9 kroku/s), przechył, oddech w bezruchu, wymach rąk, uniesienie i pochylenie wędki. Zdarzenie
`Footstep` jest gotowe pod dźwięki kroków. `FirstPersonBody` przykłada pozę do kamery, wędki i przedramion
(prawa dłoń na stopce kołowrotka, lewa na korbce podczas holu).

### Ryby w wodzie
`FishPopulation` zarybia mapę ławicami według `Abundance` łowiska (płoć i leszcz w ławicach, karp i szczupak
pojedynczo), połowa ławic startuje przy pomoście i wiosce. `FishSwimModel` steruje każdą rybą: błądzenie, ławica
(spójność, wyrównanie, separacja), trzymanie się swojego pasa głębokości, omijanie brzegu i ucieczka przed
wędkarzem (bieg straszy z dalszej odległości). Ryba jest rysowana, gdy jest bliżej niż 20 m i płycej niż 1,4 m;
okulary polaryzacyjne dodają zasięg i głębokość oraz zmniejszają odblaski wody (`glare_cut` w shaderze).
Ryby pod wodą są rysowane po powierzchni wody i barwione jej kolorem, więc prześwitują, a dno i pomost je zasłaniają.

### Pasek walki
Pasek leży pod napisem stanu („Hol · 43 m”), a podczas holu hamulec stoi tuż po jego prawej stronie. Pokazuje zapas
wytrzymałości zestawu w czterech sekcjach bez napisów i przegródek: 0-10% (czerwona, 7% szerokości), 10-25% (18%),
25-50% (25%) i 50-100% (zielona, połowa paska). Ideał to środek paska, czyli jasny pomarańcz (`Core/FightGauge`).

### Każde łowisko jest inne
- **Jezioro Zielone**: zatoka na zachodzie, długie ramię na wschodzie, wyspa z ruinami wieży, kapliczka na wzgórzu.
- **Zatoka Szuwarowa**: trzy zatoczki, cztery trzcinowe wysepki, chata na palach w wodzie.
- **Staw Karpiowy**: prostokątny staw (`Squareness`), osobny staw narybkowy, grobla z mnichem i szpalerem topoli, pomosty-stanowiska.
- **Górskie Jezioro**: fiord na północ zakończony wodospadem w skalnej ścianie, skaliste wysepki.

Pocztówki (`Graphics/Postcard`) mają osobną kompozycję dla każdej mapy z jej obiektami; podgląd: tryb zrzutów 17,
kształty map z góry: tryb 18.

### Deszcz
Deszcz to `MultiMesh` animowany w `shaders/rain.gdshader`, a nie `GPUParticles3D`: cząsteczki rysowane przez dwa
widoki naraz z wektorami ruchu wywracały Godota 4.3 (`particles_get_instance_buffer_motion_vectors_offsets`).

## Dodawanie treści
- **Kształt łowiska**: `Lobes` ("x,z,rx,rz") dokładają zatoki, `Islands` ("x,z,r") wycinają wyspy, `Squareness` > 1 robi staw prostokątny.
- **Obiekt charakterystyczny**: wpis `"rodzaj,x,z[,obrót[,rozmiar]]"` w `Landmarks` (tower, chapel, stilthouse, dike, waterfall, rocks, platforms); budowa w `Props/Landmarks.cs`.
- **Siatka / podbierak / akcesorium**: plik `.tres` w `data/tackle/{keepnets,landingnets,accessories}/` i ścieżka w `Progress/GearCatalog.cs`.
- **Nowy rodzaj akcesorium**: wartość w `AccessoryKind` i jego efekt tam, gdzie działa (np. `Main.ApplyGearEffects`).
- **Targ na innej mapie**: wpis `"market,x,z"` w `Buildings` łowiska (test `village` sprawdza, czy teren jest równy).

## Kolejne kroki (backlog)

Kolejność według wartości dla gracza; każdy punkt ma już miejsce w strukturze.

1. **Brania z widocznych ryb.** Zrobione: ryby, które lubią daną przynętę (gatunek, głębokość, pora), podpływają
   do spławika (`FishSwimModel.Attractor`), a bierze konkretna ryba z okolicy (`PickNear`), z jej wagą. Spłoszone
   i zahaczone ryby nie biorą; zerwana ryba ucieka w popłochu, złowiona znika z łowiska, a w innym miejscu pojawia
   się nowa. Gdy przy spławiku nie ma chętnych, branie przychodzi „spoza wzroku” jak dawniej.
2. **Model człowieka.** Częściowo: dłonie z bloków trzymają wędkę (palce na rękojeści, lewa ręka na korbce
   i na złowionej rybie). Dalej: riggowany model rąk (`.glb` + `AnimationTree`) sterowany tymi samymi
   parametrami `BodyPose` / `BodyAction`, animacja zamachu kasarkiem.
3. **Dźwięki.** Zrobione: synteza bez plików (`Core/Audio/Synth`): otoczenie wg pory i pogody, kroki wg podłoża,
   plusk, kołowrotek, hamulec, zacięcie, złowienie, zerwanie, dzwoneczek brań. Dalej: dźwięk przestrzenny 3D,
   odgłosy wioski.
4. **Targ z NPC.** Hala targowa z handlarzami, zamówienia dnia ("kupię 3 leszcze powyżej 1 kg"), ceny na tablicy.
5. **Akcesoria w działaniu.** Zrobione: kapelusz (widać ryby dalej i głębiej), latarnia (światło nocą), zydel
   (czas 4× szybciej podczas czekania), wiadro (żywiec przeżywa w 35%), podpórka z dzwoneczkiem (sygnał brania).
   Dalej: odłożenie wędki na podpórkę i kilka wędek naraz, zanęta.
6. **Wypuszczanie ryb na żądanie** (zrobione: R po złowieniu) i limity ochronne (zrobione: wymiar ochronny i kara strażnika; dalej: okresy ochronne) (wymiar i okres ochronny), mandat od strażnika.
7. **Świat.** Łódź, więcej map, płynne przejścia między łowiskami pieszo zamiast przystanku, NPC wędkarze.
8. **Grafika.** Jedyny styl: nordycki (zbliżony do Valheima). Pełna rozdzielczość z ostrymi teksturami, gęsta
   mgła z promieniami słońca, cienie i okluzja, bloom, chłodne cienie i ciepłe światło, księżycowe noce. Pixel art
   został usunięty. Dalej: gęstsze lasy, mgła w dolinach, ognisko, modele budynków z prawdziwych siatek.
9. **Doba i HUD.** Zrobione: dzień 40 min, noc 20 min (`Core/DayClock`), minimapa i zegar w prawym górnym rogu
   (`UI/Minimap`).
10. **Ryby.** Zrobione: 66 gatunków z opisami, szczegółowe skóry (`Graphics/FishSkin`, malowane w tle wątkami),
    atlas w menu (`UI/FishAtlas`). Dalej: osobne tekstury płetw z promieniami, pyski i wąsy jako siatki.

11. **Realizm i wydajność.** Zrobione: hol na fizyce położenia i prędkości ryby, modele ryb per gatunek, niszczenie
    sprzętu z darmowym zestawem dla bez grosza, tekstury 1024 px z pamięcią podręczną, lżejszy preset Wysoka
    (SSIL, SSR, MSAA tylko na Ultra), wcześniejsze malowanie skór i materiałów ryb (bez przycięć).

## Testy
Wszystkie zestawy razem: `godot --headless --path . res://tests/run_tests.tscn` (447 sprawdzeń). Zestaw `gear` (`tests/GearSuite.cs`) sprawdza targ, siatki, podbieraki, akcesoria, zapis, animację ciała, pływanie
ryb i ich obecność w grze, brania od widocznych ryb, dźwięk, akcesoria, atlas oraz balans 66 gatunków (każdy da się złowić i wyholować). Uruchomienie:
```
FISHSIM_NO_SAVE=1 godot --headless --path . res://tests/run_tests.tscn -- gear
```
