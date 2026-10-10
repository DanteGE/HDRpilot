> Vor der Veröffentlichung bereinigt: Die Aufzählungen der Spiele je Klasse sind durch Anzahlen ersetzt; einzelne Spiele
> bleiben nur als Beleg eines technischen Befunds (E92).

Baseline on your real 647-game library: 595 games get a game-specific icon from the current approach, and 52 get no icon or a bad one. That 52 is 9 with no EXE found, 10 with an EXE that has no icon, and 33 sharing a generic icon (30 truly generic after visual review). Visual review found 4 more generic icons that occur fewer than 3 times, so the real number of games without a good icon is **53**. All 647 games are installed, so "installed first" doesn't change any ordering.

**How it was measured**
- I didn't re-implement the logic. A small console tool (`IconBaseline.exe`, built in my folder) loads the app's own compiled `HDRpilot.dll` and `HdrPilot.Core.dll`, copied from `p4\src\HdrPilot.App\bin\Debug\net10.0-windows`. That build is from 2026-10-10 00:34, newer than every source file, so it matches the current code.
- For each game it calls `new IconExecutableResolver(PhysicalFileSystem.Instance).Resolve(game, Settings.HelperExecutablePatterns)`. That setting is null in your config, so the 66 default helper patterns apply. It then calls `GameIconService.ExtractIcon(exe, 48)`.
- Pixels are converted to Bgra32, fully transparent pixels are zeroed, and the icon's ID is the first 16 hex characters of SHA-256 over the pixels.
- Class (c) is any exact pixel match shared by at least 3 different games. I also ran a perceptual near-duplicate check; it found no further groups.
- I looked at all 628 extracted icons on 6 contact sheets to catch generic icons that occur only once or twice. That check was by eye at 48 px, so a few could be missed.
- Run time is sequential. Cold disk cache: Resolve took 12.8 s in total (median 1.1 ms, worst game 1.8 s) and ExtractIcon 1.8 s in total. Warm cache: the whole pass took 1.1 s.

**Counts per class and source** (Battle.net and Manual: no games in the config)

| Source | Total | a: no EXE | b: no icon | c: shared ≥3 | d: game-specific |
|---|---|---|---|---|---|
| Steam | 633 | 9 | 9 | 33 | 582 |
| Epic | 6 | 0 | 0 | 0 | 6 |
| GOG | 5 | 0 | 0 | 0 | 5 |
| EA | 1 | 0 | 0 | 0 | 1 |
| Ubisoft | 1 | 0 | 0 | 0 | 1 |
| Xbox | 1 | 0 | 1 | 0 | 0 |
| **All** | **647** | **9** | **10** | **33** | **595** |

How the EXE was chosen: folder scan 623 times, launcher-known EXE 14, detected EXE 1. By engine, about 324 games are Unity and 157 Unreal 4/5.

**(a) No EXE found (9)**
- **EXE more than one folder deep (7):** 7 games (names removed before publication).
- **Crimson Desert Enhanced:** the game EXE (`bin64`) is in a subfolder past the 32-subfolder limit (the folder has 37).
- **The Midnight Walkers Playtest:** no EXE at all.

**(b) EXE found but no icon (10)**
- **No icon resource in the EXE:** 5 games; in one of them the resolver picked a `*_dev.exe` whose sibling EXEs have no icon either.
- **Wrong EXE picked:**
  - Homesick, Viscera Cleanup Detail and Viscera Cleanup Detail: Shadow Warrior all got `P4PopulateDepot.exe`, a Perforce tool.
  - Portal got `elementviewer.exe`, an SDK tool.
- **Palworld (Xbox):** reading the game EXE is access-denied. I confirmed this also happens outside the Claude container, using a process started via WMI. The Xbox logo files (`Content\Resources\StoreLogo.png`, `Square480x480Logo.png`, `SmallLogo.ico`, listed in `MicrosoftGame.config`) are readable, so they are a possible fallback.

**(c) Icons shared by 3 or more games (33 games)**
- **C1, EasyAntiCheat default icon (12):** 12 games (names removed before publication).
- **C2, Unreal Engine default, black "U" (10):** 10 games (names removed before publication).
- **C3, Unreal Engine default, outlined "U" (4):** 4 games (names removed before publication).
- **C4, Epic Games / EOS launcher logo (4):** 4 games (names removed before publication).
- **C5, not generic (3):** the three Warhammer 40,000: Boltgun entries share their real game icon, so this group is a false positive.

**Generic icons the 3-game rule misses (counted in d):** Brotato and Pathogenic got a Godot default icon because the larger `GodotWorkshopUtility.exe` was picked over the game EXE. Dome Keeper's own EXE has the Godot default icon, and Slyders' has the Unity default icon. No other icon in the library is close to any of these generic ones.

**Other quality issues (counted in d):**
- 17 icons have nothing at 48 px in the EXE and are blown up from 32 px or smaller, so they look blurry. Examples: the two SWAT games; one icon exists only at 16 px.
- 8 picked EXEs have suspicious names, such as `deadbolt_map_editor.exe` for DEADBOLT and `EDITOR.exe` for Heroes of Hammerwatch II.

**Extra: what a smarter local search would recover.** I re-checked the 53 bad cases with a deeper search: every non-helper EXE up to 3 subfolders deep, skipping EXEs with no icon or a generic one, then taking the largest. It found an alternative for 30, and 24 of those look right on the sheet. Examples are the EasyAntiCheat and Epic launcher cases (via `<Project>\Binaries\Win64\*-Shipping.exe`), the two Godot cases above and the games whose EXE lies too deep. The other 6 picked the wrong thing: `java.exe`, `vconsole2.exe`, `qc_eyes.exe`, the Unreal 3 `UDK.exe` default icon three times, and a generic icon for one Unreal game. That still leaves about 29 games needing an icon from outside the game folder, mostly Unreal default-icon games plus the Godot and Unity cases above, the 5 games without an icon resource, Palworld and Portal. The deeper search took a median of 66 ms per game, worst case 12 s.

Everything is in `<Notizordner>\icons-research\baseline\` (full path below). The contact sheet you asked for is `out\c-clusters-contact-sheet.png`.

Files are in <Notizordner>\icons-research\baseline:
- out\c-clusters-contact-sheet.png
- out\report.txt
- out\generic-and-alternatives.txt
- out\results.csv
- out\local-alternatives-sheet.png
- out\near-generic-candidates-sheet.png
- out\all-icons-01.png … all-icons-06.png
- out\all-icons-index.tsv
- out\icons\ (598 distinct icon PNGs)
- generic-refs.txt
- tool\Program.cs
- tool\AltPass.cs
- tool\IconBaseline.csproj
- xbox-read-test.ps1
- xbox-read-real.txt
- xbox-read-container.txt