# Zmiany

## 0.11.2 (Unity 6)
- Wędkarz w widoku pierwszoosobowym. Żyłka przechodzi przez obracający się kabłąk, a grubość nawoju odpowiada ilości pozostałej żyłki.
- Dziesięć łowisk o rosnącej wielkości, z osobnymi progami poziomu i obsadami ryb. Panel podróży można przewijać.
- Drobniejsze skupiska liści na modelach dębów, brzóz i wierzb oglądanych z bliska.

## 0.11.1 (Unity 6)
- Mniejszy teren do chodzenia: najwyżej 10 m od wody, poza tym ścieżki, plac, okolice budynków i charakterystyczne miejsca.
- Poprawione artefakty otoczenia (z przeglądem Codexa): dalekie pnie nie świecą już na pomarańczowo, słupki latarni i drogowskazów są przeszkodami, ławki nie są czarne, łąka bez jaskrawej żółtej zieleni, okrągłe żarówki nad pomostem, łódka bez migoczących pasów.
- Woda (poprawki Codexa): bez srebrnych kresek w oddali i w deszczu, bez ciemnej plamy i jasnych łuków przy krawędziach ekranu, równe odbicie nieba.

## 0.11.0 (Unity 6)
- Start jak w nowoczesnej grze wędkarskiej: najpierw logowanie na konto (statyczny obraz łowiska), potem statyczny ekran wczytywania łowiska z nazwą, opisem, wskazówką i postępem, potem gra. Menu z kartami łowisk zastąpione panelem „Łowiska” w grze (M lub pauza).
- Szybsze wczytywanie: tekstury gruntu, zdjęcia materiałów, liście i tekstury postaci kompresowane na wątkach roboczych i zapisywane w pamięci podręcznej (texcache), więc kolejne starty tylko je odczytują; gra wczytuje tylko modele potrzebne na łowisku (wędka i kołowrotek w ręku, wioska, ryby tego jeziora) zamiast wszystkich 66 ryb i całego katalogu sprzętu.
- Grafika i modele przygotowują się w tle już podczas logowania; ekran wczytywania kończy się rozgrzewką shaderów (łowisko renderowane raz w każdą stronę, z deszczem, mgłą i rybami), więc pierwsze sekundy gry nie przycinają.
- Nowy wygląd interfejsu: płaskie ciemne panele, systemowy krój Bahnschrift, jeden ciepły akcent; HUD bez kart: godzina i pogoda w rogu, na dole długość żyłki, paski obciążenia żyłki i wędki z punktem poślizgu hamulca, hamulec w procentach i kilogramach, siła i zasięg rzutu.
- Deszcz z cienkich smug w kolorze nieba (bez białych pasów przy kamerze), horyzont zlewający się z mgłą przy zachmurzeniu, szare niebo bez beżowego odcienia.
- Z poprzednich wersji testowych: konta graczy (hasła solone i haszowane), 81 wędek i 87 kołowrotków o różnych parametrach i wykończeniu, rzut z oporem powietrza i wiatrem, zwijanie korbką (cm żyłki na obrót), włosowo cienka żyłka z odblaskiem słońca.

## 0.9.4 (Unity 6)
- Grafika przenoszona z wersji Godot: teren z siedmiu fotograficznych warstw (trawa, ściółka, piasek, ścieżki, dno, skały, śnieg), woda z falami Gerstnera, załamaniem światła, kolorem toni i pianą przy brzegu.
- Las jak w Godocie: świerki, sosny, dęby, brzozy i wierzby z kory ze zdjęć i koron z malowanych liści, krzewy, leszczyna, paprocie, głazy z mchem, kępy trawy wokół gracza.
- Rośliny przy wodzie: trzciny z pałkami, sitowie, żółte irysy, kwiaty, grzybienie białe i żółte, rdestnica.
- Niebo z rozpraszaniem światła i chmurami, łuna o zmierzchu, gwiazdy; słońce, księżyc i mgła według pory dnia; poranne opary nad wodą; deszcz i burze z błyskawicami.
- Ryby widoczne w jeziorze, kręgi po żerujących rybach, mewy, jętki wieczorem.
- Charakterystyczne miejsca map: ruina wieży na wyspie, kaplica, chata na palach, grobla ze śluzą i pomostami, wodospad, skałki.
- Wykończenie obrazu: cieniowanie AO, poświata, tonemapping ACES i nordycka gradacja kolorów.
- Mieszkańcy: własny kolor włosów i odcień skóry, połysk skóry, tkanin i włosów, przytłumione kolory ubrań.

## 0.9.3 (Unity 6)
- Modele z Meshy: 66 gatunków ryb (hol, panel złowionej ryby), budynki wsi, wędki i kołowrotki (obracający się rotor i korbka).
- Wioska z mapy (wyrównany teren pod budynkami, ulice, latarnie nocą); E: sklep, skup, targ, gildia, wędzarnia.
- Mieszkańcy z animacjami (sprzedawcy, ludzie przy pracy, przy ognisku, spacerowicze w dzień).
- Dźwięk (syntezowany: wiatr, woda, ptaki, świerszcze, deszcz, żaby, wieś, kołowrotek, hamulec, chlupot, kroki).
- Łódka (wiosłowanie, fale, kotwica, znoszenie wiatrem, echosonda), ogniska i gotowanie z premiami, łopata, siatka na owady, podrywka, grzyby i jagody, tablica ogłoszeń, akcesoria w sklepie.
- Ręce wędkarza trzymające wędkę i korbkę.

## 0.9.2 (Unity 6)
- Pierwsza wersja na silniku Unity 6 (DirectX 11 na Windows, bez Vulkana, z którym wiązały się awarie przy starcie).
- Grywalne: 4 łowiska (teren i jezioro z tych samych wzorów co w wersji Godot), woda, niebo, pora dnia i pogoda, las, pomost; zarzucanie, branie, zacięcie, hol (te same modele brań i holu), siatka, skup, sklep (wędki, kołowrotki, żyłki, haczyki, przynęty), rekordy, ustawienia grafiki, zapis gry.
- Jeszcze nie przeniesione z wersji Godot: modele Meshy (ryby, budynki, wędki, kołowrotki), wioska i mieszkańcy, łódka, obóz, narzędzia, dźwięk, ręce wędkarza.

## 0.9.1
- Budynki z Meshy w pełnej jakości: tekstury 4K i 0,45–1 mln trójkątów (granice UV nieruszane, więc obraz się nie rozmazuje), wyglądają jak oryginały.
- Gotowe siatki z LOD-ami zapisywane w pamięci podręcznej (szybsze kolejne wczytania).
- Zdjęcia przedmiotów w sklepie dołączone do gry: koniec przycięć po wyjściu z kramu; nowe zdjęcia tylko w menu lub przy otwartym sklepie.
- Dziennik przycięć: logs/hitches.txt (klatki > 50 ms z przyczynami).
- Ustawienia: poziom Ultra niska; tekstury 1K / 2K / 4K / 8K.
- Uszkodzona pamięć shaderów („Malformed smolv”) czyszczona przy następnym starcie.
- Licencja: gra prywatna, wszelkie prawa zastrzeżone (Krystian Naskręski).

## 0.9.0
- Pierwsze wydanie z instalatorami: Windows (instalator i .zip), Linux (.deb i .tar.gz).
- Wioski z budynkami z Meshy (15 modeli), gracz jako zriggowany model wędkarza z dłońmi na wędce i kołowrotku.
- Żyłka biegnie przez przelotki i nawija się na szpulę (ubywa przy wyrzucie, przybywa przy zwijaniu).
- Grunty i trawa z materiałów CC0, spokojniejsze kolory ubrań mieszkańców z daleka.
