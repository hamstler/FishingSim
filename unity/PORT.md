# Port gry na Unity 6 (6000.0.64f1 LTS, HDRP)

Stan: etap 1 z 8 zrobiony. Gra Godotowa (główny katalog repozytorium) zostaje, dopóki wersja Unity jej nie zastąpi.

| Etap | Zakres | Stan |
|---|---|---|
| 1 | Projekt Unity, logika gry (`Scripts/Core`, 53 pliki, 4,4 tys. linii), dane (`Scripts/Data`, 406 rekordów jako JSON w `StreamingAssets/data`, konwerter `tools/unity/tres_to_json.py`), warstwa zgodności (`Scripts/Compat`), budowanie w GitHub Actions (`.github/workflows/unity.yml`) | zrobione: kompiluje się w profilu Unity (.NET Standard 2.1, C# 10), wszystkie 406 rekordów się wczytuje |
| 2 | Zapis gry i konta (Progress, Accounts: System.Text.Json jako wtyczka), testy logiki w Unity Test Framework | do zrobienia |
| 3 | Świat: teren (LakeBed: wysokości, mapa gruntu), woda (shader HDRP), niebo, pora dnia, pogoda | do zrobienia |
| 4 | Modele: ryby, budynki, wędki, kołowrotki, postacie z .glb przez glTFast; LOD-y | do zrobienia |
| 5 | Wędkowanie: wędka i jej gięcie, żyłka (LineRope), spławik/przynęta, hol, ręce z IK | do zrobienia |
| 6 | Interfejs: menu, HUD, tarcza, sklep, panele (UI Toolkit) | do zrobienia |
| 7 | Wioska, mieszkańcy, łódka, obóz, dźwięk | do zrobienia |
| 8 | Grafika (ustawienia, jakość), wydajność, instalatory i wydanie | do zrobienia |

## Co musi zrobić właściciel
Unity nie zbuduje gry bez licencji przypisanej do konta (także Personal, darmowej):
1. Zainstaluj Unity Hub, zaloguj się, aktywuj licencję Personal.
2. Skopiuj zawartość pliku licencji `Unity_lic.ulf` (Windows: `C:\ProgramData\Unity\Unity_lic.ulf`).
3. W GitHubie: repozytorium -> Settings -> Secrets and variables -> Actions -> New repository secret:
   `UNITY_LICENSE` (zawartość pliku), `UNITY_EMAIL`, `UNITY_PASSWORD`.
4. Budowanie: Actions -> Unity build -> Run workflow (albo commit z `[unity]` w opisie).

## Uwagi techniczne
- Unity 6 kompiluje w C# 9; `Assets/csc.rsp` podnosi wersję do C# 10 (przestrzenie nazw na plik, rekordy), `Compat/Polyfills.cs`
  dodaje IsExternalInit. Rzeczy z .NET 5+ (MathF.Tau, StringSplitOptions.TrimEntries) zastąpione.
- `Compat/GodotCompat.cs`: Vector2/Vector3/Color/Mathf/Resource/[Export]/GD.Load z API Godota, żeby logika gry została bez zmian;
  kod po stronie Unity zamienia je na typy UnityEngine na brzegach.
- Dane: `res://data/x/y.tres` -> `StreamingAssets/data/x/y.json` (DataStore, Newtonsoft.Json, typ z pola `$type`).
- Sprawdzanie kompilacji bez licencji: biblioteki z archiwum edytora (Editor/Data/Managed) wystarczą do kompilacji,
  do uruchomienia i budowania potrzebna jest licencja.
