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

## Dodawanie treści
- **Siatka / podbierak / akcesorium**: plik `.tres` w `data/tackle/{keepnets,landingnets,accessories}/` i ścieżka w `Progress/GearCatalog.cs`.
- **Nowy rodzaj akcesorium**: wartość w `AccessoryKind` i jego efekt tam, gdzie działa (np. `Main.ApplyGearEffects`).
- **Targ na innej mapie**: wpis `"market,x,z"` w `Buildings` łowiska (test `village` sprawdza, czy teren jest równy).

## Kolejne kroki (backlog)

Kolejność według wartości dla gracza; każdy punkt ma już miejsce w strukturze.

1. **Brania z widocznych ryb.** Zrobione: ryby, które lubią daną przynętę (gatunek, głębokość, pora), podpływają
   do spławika (`FishSwimModel.Attractor`), a bierze konkretna ryba z okolicy (`PickNear`), z jej wagą. Spłoszone
   i zahaczone ryby nie biorą; zerwana ryba ucieka w popłochu, złowiona znika z łowiska, a w innym miejscu pojawia
   się nowa. Gdy przy spławiku nie ma chętnych, branie przychodzi „spoza wzroku” jak dawniej.
2. **Model człowieka.** Zastąpić prymitywne przedramiona riggowanym modelem rąk (`.glb` + `AnimationTree`),
   sterowanym tymi samymi parametrami `BodyPose` / `BodyAction` (bez zmian w logice).
3. **Dźwięki.** Kroki (`BodyMotion.Footstep`, rodzaj podłoża z `LakeBed`), plusk przynęty, terkot hamulca, otoczenie.
4. **Targ z NPC.** Hala targowa z handlarzami, zamówienia dnia ("kupię 3 leszcze powyżej 1 kg"), ceny na tablicy.
5. **Akcesoria w działaniu.** Latarka czołowa (światło w nocy), podpórka (kilka wędek naraz), krzesełko
   (szybszy czas), wiadro (żywotność żywca), zanęta.
6. **Wypuszczanie ryb na żądanie** i limity ochronne (wymiar i okres ochronny), mandat od strażnika.
7. **Świat.** Łódź, więcej map, płynne przejścia między łowiskami pieszo zamiast przystanku, NPC wędkarze.
8. **Grafika (medieval pixel art 3D).** Gotowe: potok niskiej rozdzielczości z paletą, ditheringiem i konturami,
   pixelowe tekstury, oświetlenie toon, szachulec i strzechy, latarnie, niebo z chmurami, pasmowa woda, drzewa
   z kart liści, trawa na wietrze. Zrobione też: minimalistyczny HUD, grosze, kramy, gildia, wóz na postoju,
   migoczące światła, ostre (niewygładzane) fonty. Dalej: prawdziwa pikselowa czcionka (plik .ttf/.fnt),
   ikony sprzętu jako sprite'y, minimalistyczne ekrany sklepu i ekwipunku.

## Testy
Zestaw `gear` (`tests/GearSuite.cs`) sprawdza targ, siatki, podbieraki, akcesoria, zapis, animację ciała, pływanie
ryb i ich obecność w grze. Uruchomienie:
```
FISHSIM_NO_SAVE=1 godot --headless --path . res://tests/run_tests.tscn -- gear
```
