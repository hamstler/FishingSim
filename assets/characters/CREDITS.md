# Postacie (assets/characters)

Mieszkańcy wiosek są złożeni z darmowych (Standard) pakietów Quaterniusa na licencji CC0 1.0 (domena publiczna: wolno ich używać w każdym celu, także komercyjnym, bez opłat i bez obowiązku podawania autora). Dziękujemy autorowi!

| pakiet | co z niego jest | źródło |
|---|---|---|
| Universal Base Characters | głowy, oczy, brwi, fryzury, tekstury skóry i włosów | https://quaternius.com/packs/universalbasecharacters.html |
| Modular Character Outfits - Fantasy | stroje Peasant i Ranger (mężczyzna, kobieta) z teksturami | https://quaternius.com/packs/modularcharacteroutfitsfantasy.html |
| Universal Animation Library | animacje (stanie, rozmowa, siedzenie, chód, praca) | https://quaternius.com/packs/universalanimationlibrary.html |
| Universal Animation Library 2 | animacje (założone ręce, oparcie o barierkę, prace w polu, rąbanie drewna, przytaknięcie) | https://quaternius.com/packs/universalanimationlibrary2.html |

Modele połączono, przypięto do szkieletu stroju i przeniesiono na niego animacje narzędziem `tools/assets/quaternius.py`.

## MakeHuman (CC0 1.0)

Realistyczni mieszkańcy (`mh_*.glb`, `textures/mh_*.webp`): bazowa siatka, morfy i szkielet MakeHuman dostarczane z MPFB2 (LICENSE.ASSETS.md: CC0) oraz paczka „makehuman_system_assets” (skóry, oczy, brwi, rzęsy, fryzury, ubrania, buty, kapelusze; CC0) z https://static.makehumancommunity.org/asset_packs/makehuman_system_assets.html. Postacie zbudowano narzędziem `tools/assets/makehuman_build.py` (MPFB2 w Blenderze 4.2 tylko na etapie budowy; jego kod GPL nie trafia do gry), ustawiono w pozie szkieletu Quaternius i przypięto do niego, więc korzystają z tych samych animacji.
