# Ersatz-Symbole (G8.2) und eigenes Symbol (G8.3): Auftrag und Stand

> **Status: angehalten am 2026-10-10 auf Wunsch des Nutzers, halbfertig.** Der Code liegt auf dem Zweig `feature/icons`
> (Arbeitsordner `.worktrees/s9-icons`, WIP-Commit `b1eedc5` auf `ad98e51`). Er ist **weder gebaut noch getestet**.

## Wünsche und Entscheidungen des Nutzers (2026-10-10)
- Findet der Scan kein brauchbares Symbol, soll HDRpilot andere Quellen nutzen.
- **Offizielles Steam-Symbol zuerst.** Die EXE kommt nur dann zum Zug, wenn das Steam-Symbol fehlt, ein bekanntes
  Allerweltssymbol ist oder deutlich unschärfer ist.
- **Nur lokale Quellen**, nichts aus dem Netz.
- **Eigenes Symbol per Rechtsklick** in der Bibliothek: „Symbol ändern…“ und „Symbol zurücksetzen“.
- Beanstandet hat der Nutzer: Bei manchen Spielen nahm die App das Symbol vom Anti-Cheat oder von Fremdbestandteilen
  statt vom Spiel. Beispiele sind Gears of War: E-Day und The Isle.
  - Die Ausschlussliste muss deshalb EasyAntiCheat, BattlEye, EA Anti-Cheat, GameGuard und EOS abdecken (so zugesagt).
  - Nach der Umsetzung diese Spiele gezielt nachmessen.

## Was im WIP-Commit schon steckt (laut Arbeitsverlauf des angehaltenen Agenten)
| Teil | Stand |
|---|---|
| A. Symbol-Hinweise aus den Scannern | weitgehend umgesetzt. Zum Code: <ul><li>Felder in `GameModels`, `AppConfig`, `LibraryMerger`, `ConfigStore`</li><li>Steam: `SteamAppInfoReader` liest `clienticon`, neuer `SteamIconLocator`</li><li>GOG</li><li>Xbox: `PackageAssets`, `AppxManifest`, `MicrosoftGameConfig`</li><li>Ubisoft</li><li>CLI-Ausgabe</li></ul> |
| B. Kandidatenkette, Qualitätsregel, Erkennung von Allerweltssymbolen | umgesetzt in `GameIconService`, `IconReader`, `GenericIcons`, `IconFiles`, noch ungeprüft |
| C. Bessere EXE-Wahl (Ausschlussliste, `*-Shipping.exe`, begrenzte Tiefensuche) | vermutlich in `GameIconService` (`IconExecutableResolver`). Prüfen, ob BattlEye, EA Anti-Cheat und GameGuard drin sind |
| D. Gemerkte Symbolwahl | `IconChoiceCache` angelegt |
| E. Eigenes Symbol | `CustomIcons`, `CustomIconStore`, `AppPaths`, Befehle im `LibraryViewModel`, Dialog in `LibraryDialogs`/`ILibraryContext`, Texte in `LibraryStrings.resx`. **Fehlt:** die Kontextmenü-Einträge in der XAML. Daran arbeitete der Agent, als er angehalten wurde |
| F. Tests (mindestens 30 mit „Icon“ im Namen, mindestens 6 mit „CustomIcon“, Gegenproben) | **fehlen ganz** |
| G. E86 in `docs/entscheidungen.md`, README | **fehlen** |

## Weitermachen
1. `.worktrees/s9-icons` bauen (`tools\gates\build.ps1`) und Fehler beheben. Den Diff von `b1eedc5` gegen den Auftrag unten
   lesen.
2. Den Auftrag fertig umsetzen, ab E (XAML), dann F und G. Danach unabhängig prüfen lassen, Funde beheben.
3. **Messen** an einer Kopie der echten Bibliothek, nur lesend. Die alte Kopie `g78\after-config.json` lag im
   Sitzungs-Scratchpad und ist dort eventuell weg.
   - Neue Kopie: `%APPDATA%\HDRpilot\config.json` per WMI außerhalb des Claude-Containers nach `%TEMP%` kopieren (E84).
     Im Container sieht man nur die private Kopie.
   - Ziel laut G8.2: 647 von 647 mit eigenem Symbol, mindestens 618 scharf (≥ 48 px).
   - Vergleich: die Messung vor der Änderung (Kennzahlen in `berichte/baseline.md`; die Rohdaten mit der echten
     Spieleliste wurden vor der Veröffentlichung entfernt). Das Messwerkzeug liegt unter `werkzeuge/baseline/` und
     braucht `HDRpilot.dll` und `HdrPilot.Core.dll` aus einem Build in `..\lib`.
4. Zusammenführen nach `phase5/release`. Dabei G8.2 und G8.3 in `GATES.md` belegen, E86 schreiben, alle Gates neu laufen
   lassen.

## Unterlagen in diesem Ordner
- `synthese.md`: Ergebnis der Recherche (Ausgangslage 53 Spiele ohne gutes Symbol, lokale Quellenkette).
- `berichte/`: Einzelberichte (`baseline`, `steam`, `others`, `online`; online wurde vom Nutzer abgelehnt).
- Die Rohdaten der Messung vor der Änderung (`messung-vorher/`: Bericht, Index aller Symbole mit Pfaden) enthielten die
  echte Spieleliste und wurden vor der Veröffentlichung entfernt; die Kennzahlen stehen in `berichte/baseline.md`.
- `werkzeuge/`: nur lesende Mess- und Prüfwerkzeuge der Recherche.
  - Fingerabdrücke der Allerweltssymbole: `werkzeuge/baseline/generic-refs.txt`.
  - Die `.cmd`-Skripte schreiben nach `%TEMP%\HDRpilot-Recherche` (vorher ein fester Pfad im Scratchpad); den Ordner
    vor einem Lauf anlegen.

## Ursprünglicher Auftrag an den Agenten (englisch, Pfade auf dieses Repository umgestellt)

Read first: the research synthesis `docs/recherche/symbole/synthese.md` and the detailed reports `berichte/baseline.md`,
`berichte/steam.md`, `berichte/others.md` (measured on the user's real library: 647 games, 53 without a good icon), the
measurement tool sources under `werkzeuge/baseline/` and the generic icon fingerprints `werkzeuge/baseline/generic-refs.txt`,
plus GATES.md gates G8.2 and G8.3 and current code (src/HdrPilot.App/Services/GameIconService.cs incl.
IconExecutableResolver; src/HdrPilot.Core/Scanners/*, Games/GameModels.cs, Config/AppConfig.cs, LibraryMerger,
SteamAppInfoReader).

USER DECISIONS:
1. Steam games: OFFICIAL STEAM ICON FIRST. Order:
   1. `steam\games\<clienticon>.ico` from appinfo.vdf `common/clienticon`.
   2. Else the Uninstall 'Steam App <id>' DisplayIcon in the 64-bit registry view (NOT WOW6432Node).
   3. Else the Start menu/desktop .url IconFile.
   4. Else `appcache\librarycache\<appid>\<icon>.jpg` (32 px, no alpha).

   Use the game EXE only if the Steam icon is missing, a known generic icon, or clearly blurrier (Steam icon < 48 px
   while the EXE icon has >= 48 px).
2. NO online sources.
3. Custom icon via right-click in the library:
   - "Symbol ändern…" lets the user choose an image (.png/.jpg/.ico/.webp) or an .exe.
   - The icon is scaled to e.g. a 96 px PNG and stored as `<DataDirectory>\icons\<safe key>.png`. Config field e.g.
     `GameConfig.CustomIcon` = file name.
   - "Symbol zurücksetzen" removes it.
   - The custom icon has priority over everything.

IMPLEMENT (gate G8.2 + G8.3):

**A. Scanners deliver icon hints.**
- GameEntry/GameConfig get an ordered list of icon candidate paths (e.g. IconCandidates), merged like KnownExecutables.
  Covers LibraryMerger, ConfigStore null-safety and JSON serialization.
- Keep the config compact. Consider not persisting derived candidates if they can be recomputed cheaply at scan time;
  decide and document.
- Steam: extend SteamAppInfoReader to read `common/clienticon` (+ `common/icon`). The scanner builds the candidate paths.
- GOG: `goggame-<id>.ico` in the install folder, and the Galaxy webcache `*_glx_square_icon_v2.webp` under
  `C:\ProgramData\GOG.com\Galaxy\webcache\<user>\gog\<productId>\`.
- Xbox/Store: logos from MicrosoftGame.config / AppxManifest (Square480x480Logo, Square150x150Logo, StoreLogo,
  SmallLogo.ico), with wildcard resolution of `.scale-*`/`.targetsize-*` variants.
- Ubisoft: DisplayIcon of 'Uplay Install <id>'.
- Epic/EA/Battle.net: EXE only.

**B. GameIconService: candidate chain per game.**
- Order: custom icon > source-specific order (Steam: official first as above) > EXE (improved resolver).
- .exe/.ico go through the existing extraction. Images go through the WPF decoders with a decode width.
- Quality rule: the icon is present, not a known generic icon, and its native size is >= 48 px. Read the ICO directory or
  EXE icon group to know the real largest size. If none is "good", take the best available.
- Generic icon detection: a fixed fingerprint list from generic-refs.txt (EasyAntiCheat, Unreal variants, Epic/EOS
  starter, Godot, Unity, the Windows default app icon, Unreal 3 UDK).
  - Compare after normalising pixels, plus a small perceptual distance threshold if needed.
  - Document the measured distances.
- Keep the cache per game and never block the UI. Failures (missing codecs, unreadable files) are swallowed and logged at
  debug.

**C. IconExecutableResolver improvements.**
- An icon-only exclusion list: start_protected_game.exe, *EAC*.exe, EOSBootstrapper.exe, LaunchGame.exe,
  GodotWorkshopUtility.exe, P4PopulateDepot.exe, elementviewer.exe, *editor*.exe, java.exe, vconsole2.exe, qc_eyes.exe.
  Also BattlEye, EA Anti-Cheat and GameGuard, as promised to the user.
  - Do NOT add them to HelperExecutables, which has another meaning.
- Prefer the Unreal `<Project>\Binaries\Win64\*-Shipping.exe`.
- Skip EXEs without an icon or with a generic icon when another candidate exists.
- A deeper search (up to 3 levels) only as a fallback.
  - Bound it by count and time. The research saw a 12 s worst case, so cap it, e.g. 300 directories / 1 s.
  - Run it on the icon worker thread only.

**D. Persisted icon choice cache** (optional, but recommended by the research).
- A small JSON in `<DataDirectory>` that remembers per game the chosen source path, size and last write time, so cold
  starts avoid folder scans.
- Invalidated when the file changes or the game changes.

**E. Custom icon (G8.3).**
- Context menu items in the library (MainWindow) for the selected game:
  - "Symbol ändern…" with OpenFileDialog filters "Bilder und Programme".
  - "Symbol zurücksetzen", only enabled when a custom icon exists.
- Store the scaled PNG in the data folder (with `--config-dir`: the test data folder) and update the UI immediately.
- Texts in LibraryStrings.resx. AutomationIds "ChangeIconMenuItem", "ResetIconMenuItem".
- Tests: storing, scaling, reset, priority, and fallback when the file is missing.
- A UI-automation check in tools/gates/phase6-ui.ps1, or a new small gate, on a test instance:
  - Set a custom icon through the view model command with a provided file path. There is no file dialog in automation,
    so make the dialog injectable.
  - Verify the stored PNG and the config field, then reset it.

**F. Tests.**
- Names must contain "Icon"; custom icon tests "CustomIcon". At least 30 Icon tests overall.
- Required fixtures and cases:
  - Steam appinfo clienticon parsing (binary VDF fixture).
  - Uninstall DisplayIcon lookup (fake registry, 64-bit view).
  - .url parsing.
  - GOG/Xbox/Ubisoft candidates.
  - Generic detection: positive with real fingerprints from small embedded test icons you generate, negative with a
    normal icon.
  - The quality rule.
  - Resolver exclusions, Shipping preference and the bounded deep search.
- Mutation checks for at least 6 behaviours.

**G. Docs.**
- E86 in docs/entscheidungen.md (CRLF): decisions, measured numbers and design.
- README: where icons come from, and the custom icon via right-click. The Bedienung section labels must match the resx.
- Leave GATES.md untouched (the orchestrator fills in the evidence).

**Rules from the original prompt that still apply:**
- Write files only inside the worktree or a scratch folder. Never extract or copy launcher cache files into the repo: an
  earlier agent dumped 1697 images into the repo root. Tests use small fixtures created in temp folders.
- Never modify launcher files, the registry or game folders (read-only).
- No network access in code and no new NuGet packages. WebP decoding only via the Windows imaging codecs, failing
  gracefully.
- Respect AntiCheatSafetyTests.
- Never run the app without `--config-dir` and never send mouse/keyboard input.

**Verification:**
- `tools\gates\build.ps1` (BUILD_OK_0_WARNINGS).
- `tools\gates\test.ps1` full run (TESTS_OK), plus `-Filter "FullyQualifiedName~Icon" -MinTests 30` and
  `-Filter "FullyQualifiedName~CustomIcon" -MinTests 6`.
- phase4-resources.ps1, phase6-name.ps1, phase5-readme.ps1, phase4-ui.ps1, phase6-ui.ps1, phase2-scanner-tests.ps1 and
  phase2-scan.ps1.

**Review checklist after the implementation:**
- User decisions are honoured.
- appinfo parsing and the registry view are correct.
- No generic-icon false positives (e.g. Boltgun shares a real icon across 3 entries and must not be flagged).
- No work while idle, a bounded deep search, and no UI blocking.
- Robust against missing codecs, unreadable files, huge images and malicious paths in the config.
- Old configs still load.
- Files are written only to the data folder.
- Tests fail without the code.
- resx and README are consistent.
