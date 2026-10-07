# Steam-Scanner

> Vor der Veröffentlichung bereinigt: Installationsordner des Messrechners sind durch erfundene ersetzt (etwa
> `D:\Steam-Bibliothek`, `D:\GOG-Spiele`), vollständige Spielelisten durch erfundene Beispielzeilen. Spiele an
> Standardorten (`C:\XboxGames`, `…\Steam\steamapps\common`, `C:\Program Files\EA Games`, `…\GOG Galaxy\Games`)
> tragen erfundene Namen und Kennungen.

Stand: 2026-10-07, gemessen am Zielrechner (Windows 11 25H2, Build 26200) durch reines Lesen von Dateien und Registry.
Code: `src/HdrPilot.Core/Games/Scanners/SteamScanner.cs`, Hilfsklassen unter `src/HdrPilot.Core/Games/Scanners/Steam/`
(`KeyValuesParser`, `KeyValueNode`, `SteamTextDecoder`, `SteamAppInfoReader`, `SteamAppFilter`).
Tests: `tests/HdrPilot.Tests/Scanners/SteamScannerTests.cs`, `SteamKeyValuesParserTests.cs`, `SteamAppInfoReaderTests.cs`,
`SteamUbisoftRobustnessTests.cs` (Zufallsbeschädigung),
Beispieldateien unter `tests/HdrPilot.Tests/Fixtures/Steam/` (echte Dateien dieses Rechners, anonymisiert: `LastOwner` =
Basis-SteamID 76561197960265728, `LastPlayed`, Depot-Manifest- und Content-IDs durch Kunstwerte ersetzt;
Spiele an Standardorten mit erfundenem Namen, AppID, Depots und Build).

## 1 Angaben aus dem Auftrag und Prüfung am echten System

| Angabe im Auftrag | Ergebnis | Beleg am Zielrechner |
|---|---|---|
| Registry `HKCU\Software\Valve\Steam`, Wert `SteamPath` | bestätigt | `SteamPath` (REG_SZ) = `c:/program files (x86)/steam`, also klein und mit Schrägstrichen. Daneben `SteamExe` = `c:/program files (x86)/steam/steam.exe`. Zusätzlich vorhanden: `HKLM\SOFTWARE\WOW6432Node\Valve\Steam`, `InstallPath` = `C:\Program Files (x86)\Steam` (Rückfall, siehe E-S1). |
| `steamapps\libraryfolders.vdf` im Valve-KeyValues-Format | bestätigt | `C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf`, rund 17 KB, UTF-8 ohne BOM, Tabulatoren. Wurzel `"libraryfolders"`, darunter `"0"`, `"1"`, `"2"` mit `path`, `label`, `contentid`, `totalsize`, `update_clean_bytes_tally`, `time_last_update_verified` und dem Block `apps` (AppID → Größe in Byte). Pfade mit verdoppelten Backslashes: `"path"		"C:\\Program Files (x86)\\Steam"`. |
| Standardbibliothek immer einbeziehen | umgesetzt; steht am System selbst als `"0"` in der Datei | Bibliotheken: `"0"` `C:\Program Files (x86)\Steam`, `"1"` `D:\Steam-Bibliothek`, `"2"` `F:\Steam-Bibliothek`. Die Schreibweise der Datei (`C:\Program Files (x86)\Steam`) unterscheidet sich von `SteamPath` nur in Groß-/Kleinschreibung und Trennern; der Scanner führt beide zusammen (Test `Libraries_are_deduplicated_ignoring_case_and_slashes`). |
| Nicht mehr vorhandene Bibliotheken überspringen | umgesetzt | Am System sind alle drei Bibliotheken vorhanden; abgesichert durch `Missing_library_is_skipped_without_error` (Beispieldatei nennt `E:\SteamLibrary`). |
| `appmanifest_<AppID>.acf` mit `appid`, `name`, `installdir` | bestätigt | 637 Manifeste (C: 190, D: 107, F: 340). Ausschnitt: `"appid"		"9990120"`, `"name"		"Turmfall"`, `"installdir"		"Turmfall"`. Bei allen 637 passt der Dateiname zur `appid`. Sonderfall: `appmanifest_431960.acf` (Wallpaper Engine) hat als `name` den Platzhalter `"appid_431960"`. |
| Spielordner `<Bibliothek>\steamapps\common\<installdir>` | bestätigt | Für alle 637 Manifeste existiert der Ordner genau dort. Umgekehrt liegen 104 Ordner in `common` ohne Manifest (Reste deinstallierter Spiele, meist nur Spielstände/Konfiguration; nur einer enthält eine EXE: `D:\Steam-Bibliothek\steamapps\common\STALKER Shadow of Chernobyl`). Steam führt sie nicht als installiert; der Scanner auch nicht. |
| Start `steam://rungameid/<AppID>` | bestätigt | Protokoll registriert: `HKCR\steam\Shell\Open\Command` = `"C:\Program Files (x86)\Steam\steam.exe" -- "%1"`. 1 571 von Steam angelegte `.url`-Verknüpfungen auf Desktop und im Startmenü enthalten genau dieses Muster, z. B. `33 Immortals.url` → `URL=steam://rungameid/958520`. Nicht ausgeführt (nur gelesen). |
| Nicht-Spiele ausfiltern, etwa Steamworks Common Redistributables (AppID 228980) | bestätigt | `appmanifest_228980.acf`: `"name"		"Steamworks Common Redistributables"`, `"installdir"		"Steamworks Shared"`, `InstallScripts` nur für `_CommonRedist\vcredist\…`, `DirectX`, `DotNet`, `XNA`, `OpenAL`, `PhysX`. In `appcache\appinfo.vdf` hat 228980 den Typ `Tool`. |
| StateFlags am System prüfen (vollständig installiert = 4?) | bestätigt | Vormittags: 635 × `"StateFlags"		"4"` und 1 × `"6"` (3762660 Trials Survivors: `BytesToDownload` größer 0, `BytesDownloaded` 0, Ordner vorhanden, Registry `Apps\3762660\Installed` = 1). Nach dem Herunterladen des Updates stand dort `4`, und um 11:14 Uhr hatten alle 637 Manifeste `4`. Also 4 = vollständig installiert, 6 = 4 + 2 (Update erforderlich). |
| Weitere Werkzeuge am System suchen (SteamVR 250820, Steam Linux Runtime, Proton, Steamworks SDK, Dedicated Server …) | gesucht | Typ `common/type` aus `appcache\appinfo.vdf` für alle 637 installierten Apps: 621 `Game`, 6 `Demo`, 1 `Beta` (Playtest), 3 ohne Typ (Playtests ohne öffentliche Daten: 2887520, 3570950, 3919180), 2 `Tool` (228980, 250820 SteamVR) und 4 `Application` (431960 Wallpaper Engine, 993090 Lossless Scaling, 1366800 Crosshair X, 3477300 Scope X). Steam Linux Runtime, Proton, Steamworks SDK und Dedicated Server sind hier nicht installiert (Linux-Werkzeuge bzw. nicht vorhanden); sie fängt die Namensregel ab (E-S5). |

Zusätzlich gemessen: `C:\Program Files (x86)\Steam\appcache\appinfo.vdf`, 14,9 MB, Kennung `29 44 56 07` (Version 29),
Schlüsseltabelle ab Byte 14 707 732 (Int64 an Byte 8; bei der Nachprüfung gelesen, Dateilänge 14 857 830) mit 18 213
Schlüsseln. Lesen mit `File.ReadAllBytes` klappt auch bei laufendem Steam.

## 2 Format-Details

**Valve-KeyValues (Text).** Schlüssel-Wert-Paare; Wert ist eine Zeichenkette oder ein Block `{ … }`. Zeichenketten in
Anführungszeichen mit den Escapes `\\`, `\"`, `\n`, `\t`; Tokens ohne Anführungszeichen enden an Leerraum, `"` oder
Klammern; Kommentare `//` bis Zeilenende; Bedingungen wie `[$WIN32]` hinter Schlüssel oder Wert. Steam schreibt UTF-8
ohne BOM. Der eigene Parser (`KeyValuesParser`) liest das alles, überliest Bedingungen (ohne sie auszuwerten), behält
unbekannte Escapes unverändert (ein einzelner Backslash in `"D:\Spielordner"` bleibt erhalten) und meldet abgeschnittene
Zeichenketten, offene Blöcke, fehlende Werte, Steuer- und Nullzeichen (Binärmüll, UTF-16 ohne BOM) mit Zeilennummer.

**libraryfolders.vdf.** Aktuelles Format: `"libraryfolders" { "<n>" { "path" "<Pfad>" … "apps" { "<AppID>" "<Größe>" } } }`.
Älteres Format (bis 2021): `"LibraryFolders" { "TimeNextStatsReport" "…" "ContentStatsID" "…" "1" "<Pfad>" }`, also der
Pfad direkt als Wert. Beide werden gelesen; nur Schlüssel aus Ziffern zählen als Bibliothek.

**appmanifest_<AppID>.acf.** Wurzel `"AppState"`. Genutzt: `appid` (Pflicht, Zahl), `installdir` (Pflicht), `name`,
`StateFlags`. Weitere Felder (`LastUpdated`, `SizeOnDisk`, `buildid`, `LastOwner`, `BytesToDownload`, `InstalledDepots`,
`UserConfig` …) werden nicht ausgewertet.

**StateFlags.** Bitfeld. Am System beobachtet: 4 (vollständig installiert) und 6 (4 + 2, Update erforderlich). Laut
öffentlicher Beschreibung der Steam-App-Zustände außerdem u. a. 1024 (Update gestartet) und 2048 (wird deinstalliert);
eine laufende Erstinstallation hat Bit 4 noch nicht (etwa 1026).

**appcache\appinfo.vdf (binär, nur für den App-Typ).** Kopf: UInt32 Kennung (`0x07564427`/`28`/`29`), UInt32 Universum,
ab Version 29 Int64 Offset der Schlüsseltabelle. Je App: UInt32 AppID (0 = Ende), UInt32 Restgröße, 40 Byte (v27) bzw.
60 Byte (v28/v29) Kopfdaten, danach binäre KeyValues: Typbyte (0x00 Block, 0x01 Zeichenkette, 0x02 Int32, 0x03 Float,
0x04 Zeiger, 0x05 UTF-16, 0x06 Farbe, 0x07 UInt64, 0x0A Int64, 0x08/0x0B Ende), Schlüssel (v29: Int32-Index in die
Schlüsseltabelle, sonst nullterminiert), Wert. Gelesen werden nur `appinfo/common/type` und `appinfo/common/name` der
installierten AppIDs; jeder Lesezugriff ist auf die Grenzen des Eintrags geprüft.

## 3 Scan-Ergebnis am echten System

Aufruf: `dotnet run --project src\HdrPilot.Cli -- scan --source Steam` (2026-10-07, 11:14 Uhr).
Ergebnis: `launcherDetected` = true, **631 Spiele**, **0 Fehler**, Laufzeit 101 ms. Verteilung: C: 184, D: 107, F: 340.
`executables` ist bei allen leer (Steam nennt keine EXE). Ausgefiltert (Protokoll „ist kein Spiel“): 228980, 250820,
431960, 993090, 1366800, 3477300.

### Vergleich mit unabhängigen Quellen

Die Gegenquellen wurden mit PowerShell zeilenweise bzw. direkt aus der Registry gelesen, nicht mit dem Parser des Scanners.

| Quelle | Umfang | Abweichungen zum Scan | Begründung |
|---|---|---|---|
| `libraryfolders.vdf`, Block `apps` je Bibliothek | 636 AppIDs | 6 nur in der Datei: 228980, 250820, 431960, 993090, 1366800, 3477300 | genau die ausgefilterten Werkzeuge/Anwendungen; alle mit StateFlags 4 und vorhandenem Ordner |
| | | 1 nur im Scan: 4338410 Warhammer 40,000: Boltgun 2 Demo | heute installiert (Manifest vorhanden, StateFlags 4, Ordner vorhanden, Registry `Installed` = 1); `libraryfolders.vdf` wurde zuletzt einige Stunden vorher geschrieben. Steam aktualisiert die `apps`-Liste verzögert; der Scanner liest deshalb die Manifeste, nicht diese Liste. |
| | | Bibliothek je App | keine Abweichung (jede App liegt in der Bibliothek, unter der sie in `apps` steht) |
| `HKCU\Software\Valve\Steam\Apps\<AppID>`, `Installed` = 1 | 804 AppIDs | alle 631 Scan-Einträge haben `Installed` = 1; 173 zusätzlich | 6 ausgefilterte Werkzeuge; 167 ohne Manifest: 68 DLCs (Typ `DLC` in appinfo.vdf, keine eigene Installation) und 99 Spiele, die nicht mehr installiert sind (kein Manifest, Steam räumt den Registry-Wert nicht auf; z. B. 620 Portal 2, 1145360 Hades). |
| `HKCU\…\Apps\<AppID>`, Wert `Name` | 519 der 631 haben einen Namen | 7 Namen weichen ab | Die Registry hält ältere Store-Namen: 992730 „Project Downfall“ → heute „Neckbreak“, 2437570 „Chambers“ → „Golden Chambers“, 2845630 „Codename: Ocean Keeper“ → „Ocean Keeper“, 2934220 „Monsters are Coming! Rock & Road“ → „Monsters are Coming!“, 3108510 „FlyKnight“ → „FlyKnight Enhanced Edition“, 3228590 „Deadzone Rogue“ → „Deadzone: Rogue“, 3321460 „Crimson Desert“ → „Crimson Desert Enhanced“. Der Scan-Name stimmt jeweils mit `common/name` in appinfo.vdf überein. |
| Uninstall `HKLM\…\Uninstall\Steam App <AppID>` (64-Bit-Sicht) | 795 Einträge | alle 631 Scan-Einträge haben einen; `InstallLocation` weicht bei 76 ab, bei weiteren 39 ist sie leer | Alle 76 wurden in eine andere Bibliothek verschoben (gleicher Ordnername, z. B. 1724770 Castle Of Alchemists: Uninstall `D:\Steam-Bibliothek\…`, Scan `F:\Steam-Bibliothek\…`). Der Uninstall-Pfad existiert in keinem der 76 Fälle mehr, der Scan-Pfad in allen. Die 39 leeren Werte (etwa 1765780 ADACA) sind keine Abweichung im Ordner, Steam füllt den Wert nicht immer. |
| | | 164 Uninstall-Einträge ohne Scan-Eintrag | 5 ausgefilterte Werkzeuge (228980 hat keinen Uninstall-Eintrag) und 159 Reste deinstallierter Spiele (158 davon mit leerer `InstallLocation`); bei keinem existiert der Ordner. |

Abgleich der Namen mit appinfo.vdf (`common/name`): gleich bis auf 431960 (Platzhalter `appid_431960`, wird ausgefiltert),
1485590 (`ENDLESS™ Dungeon - Definitive Edition` gegenüber `ENDLESS Dungeon™ - Definitive Edition`) und 616680
(`Monster Showdown` gegenüber `Monster Showdown VR`); der Scanner nimmt den Manifest-Namen.

### Tabelle aller gefundenen Spiele

Hier stand die vollständige Tabelle der 631 gefundenen Spiele mit ihren Installationsordnern. Sie zeigte die echte
Bibliothek des Messrechners und ist vor der Veröffentlichung durch erfundene Beispielzeilen im selben Aufbau ersetzt
(Namen, IDs und Ordner frei erfunden). Sortiert nach Name (Reihenfolge der CLI-Ausgabe).

| # | ID | Name | InstallDir | Start-Befehl |
|---:|---:|---|---|---|
| 1 | 9990010 | Beispielspiel Eins | `C:\Program Files (x86)\Steam\steamapps\common\Beispielspiel Eins` | `steam://rungameid/9990010` |
| 2 | 9990020 | Beispielspiel Zwei: Remastered | `D:\Steam-Bibliothek\steamapps\common\BeispielspielZwei` | `steam://rungameid/9990020` |
| 3 | 9990030 | Beispielspiel Drei | `F:\Steam-Bibliothek\steamapps\common\Beispielspiel Drei` | `steam://rungameid/9990030` |
| 4 | 9990040 | Beispielspiel Vier Demo | `F:\Steam-Bibliothek\steamapps\common\Beispielspiel Vier Demo` | `steam://rungameid/9990040` |

## 4 Entscheidungen

**E-S1 Steam-Ordner.** Reihenfolge: `HKCU\Software\Valve\Steam\SteamPath` (Auftrag), dann `HKLM\SOFTWARE\Valve\Steam\InstallPath`
über die 32-Bit-Sicht, dann `<Programme (x86)>\Steam`, falls dort `steam.exe` liegt (über `IAppEnvironment`). Ein Pfad
zählt nur, wenn der Ordner existiert. Ohne Treffer: `LauncherDetected` = false, keine Fehler.

**E-S2 Bibliotheken.** Die Standardbibliothek steht immer an erster Stelle, auch wenn `libraryfolders.vdf` fehlt, defekt
ist oder sie nicht nennt. Doppelte Einträge werden über `PathUtil.PathEquals` (ohne Groß-/Kleinschreibung, `/` = `\`,
abschließender Trenner egal) erkannt. Die erste Schreibweise aus der Datei ersetzt die kleingeschriebene aus der Registry,
damit `InstallDir` lesbar bleibt (`C:\Program Files (x86)\Steam\…`). Fehlende Bibliotheken: Protokolleintrag, kein Fehler.

**E-S3 Manifeste statt `apps`-Liste.** Installierte Apps kommen aus den `appmanifest_*.acf`. Die `apps`-Liste in
`libraryfolders.vdf` hinkt nach (gemessen: 4338410 fehlte dort zwei Stunden nach der Installation).

**E-S4 Installationszustand.** Aufgenommen wird nur, wer Bit 4 (vollständig installiert) in `StateFlags` hat und nicht
Bit 2048 (wird deinstalliert). Ausstehende oder laufende Updates (etwa 6) bleiben drin: Das Spiel liegt vollständig im Ordner.
Erstinstallationen ohne Bit 4 (etwa 1026) fallen heraus. Fehlt `StateFlags` oder ist es keine Zahl, gilt der Eintrag als
installiert, wenn der Ordner existiert (Warnung im Protokoll). Danach muss `<Bibliothek>\steamapps\common\<installdir>`
existieren.

**E-S5 Nicht-Spiele (pflegbare Regel, `SteamAppFilter`).**
1. Feste Liste `NonGameAppIds` (eine Zeile je App, gilt immer): 228980 Steamworks Common Redistributables, 250820 SteamVR,
   431960 Wallpaper Engine, 993090 Lossless Scaling, 1366800 Crosshair X, 3477300 Scope X (alle am Zielrechner gefunden).
2. Typ aus `appinfo.vdf`: `Tool`, `Application`, `Config`, `DLC`, `Music`, `Video`, `Media`, `Driver`, `Hardware`,
   `Guide`, `Plugin`, `Series`, `Comic`, `Franchise`, `Advertising` fallen heraus; `Game`, `Demo`, `Beta` (Playtests) und
   unbekannte Typen bleiben. Kennt der Cache einen Typ, entscheidet er allein.
3. Nur ohne Typ: Namensregel `Redistributable`, `Dedicated Server`, Anfang `Steamworks SDK`, `Steam Linux Runtime`,
   `SteamVR`, `Proton <Ziffer|Experimental|Hotfix|Next|BattlEye|EasyAntiCheat>`.

Begründung: Anwendungen wie Wallpaper Engine oder Lossless Scaling laufen dauerhaft bzw. neben Spielen; als „Spiel“
würden sie in Phase 3 jeden Prozess unter ihrem Ordner zum Spielstart machen. Ein verstecktes Spiel wäre schlimmer als ein
sichtbares Werkzeug; deshalb bleiben Playtests ohne Typ und unbekannte Typen in der Liste, und Namensregeln greifen nur,
wenn der Typ unbekannt ist. Jeder ausgefilterte Eintrag wird mit Grund protokolliert.

**E-S6 appinfo.vdf nur lesend und tolerant.** Unbekannte Formatversion: Warnung, kein Fehler (künftige Steam-Versionen
sollen den Scan nicht rot färben); dann gelten nur Liste und Namensregel. Abgeschnittene oder beschädigte Datei bzw. nicht
lesbar: `ScanError` mit Pfad; die Spiele kommen trotzdem.

**E-S7 Name.** `name` aus dem Manifest. Ist er leer oder der Platzhalter `appid_<AppID>`, dann `common/name` aus appinfo.vdf,
sonst der Ordnername (`installdir`). Fehlender Name ist kein Fehler.

**E-S8 Fehler oder Protokoll.** `ScanError` (deutsch, mit Pfad): Manifest nicht lesbar, defekt (Parserfehler), ohne
`AppState`, ohne gültige `appid`, ohne `installdir`, mit `installdir` außerhalb der Bibliothek; Bibliotheksliste nicht
lesbar, defekt oder mit unerwartetem Aufbau (dann nur Standardbibliothek); Bibliothekseintrag ohne bzw. mit relativem
Pfad; appinfo.vdf nicht lesbar oder beschädigt. Nur Protokoll: fehlende Bibliothek, fehlender Spielordner, nicht
vollständig installiert, Duplikat, Latin-1-Rückfall, ausgefiltertes Werkzeug.

**E-S9 Sicherheit von `installdir`.** Absolute Pfade, Laufwerksangaben (`D:Spiel`), `..` und Abschnitte nur aus Punkten
oder Leerzeichen werden abgelehnt. Sonst könnte ein manipuliertes Manifest `InstallDir` auf `C:\Windows` legen, und in
Phase 3 gälte jeder Systemprozess als Spiel.

**E-S10 Duplikate.** Je AppID und je Installationsordner nur ein Eintrag (erste Bibliothek gewinnt, Standardbibliothek zuerst).

**E-S11 Kodierung.** UTF-8 streng; BOM (UTF-8, UTF-16) wird beachtet. Ungültiges UTF-8 wird als Latin-1 gelesen (Warnung),
damit Umlaute korrekt bleiben. UTF-16 ohne BOM ergibt Nullzeichen und damit einen `ScanError`.

**E-S12 Keine EXE-Dateien.** Steam nennt keine Spiel-EXE in den Manifesten; `Executables` bleibt leer (nichts geraten).
Die Startoptionen in appinfo.vdf (`config/launch`) werden bewusst nicht genutzt, weil sie oft nur Launcher oder
Hilfsprogramme nennen.

## 5 Bekannte Grenzen

- Ohne `libraryfolders.vdf`-Eintrag für die Standardbibliothek bleibt deren Pfad in der Schreibweise der Registry (klein).
  Funktional egal, weil alle Pfadvergleiche ohne Groß-/Kleinschreibung laufen.
- Nicht-Steam-Spiele, die der Nutzer in Steam eingetragen hat (`userdata\<id>\config\shortcuts.vdf`), werden nicht gelesen.
- Ubisoft-, EA- oder andere Spiele, die über Steam gekauft wurden (etwa 3159330 Assassin's Creed Shadows), erscheinen als
  Steam-Spiel. Startet Steam dafür einen fremden Launcher, laufen dessen Hilfsprozesse außerhalb des Steam-Ordners; das
  betrifft erst Phase 3.
- Der App-Typ kommt aus einem Cache von Steam. Fehlt eine neu installierte App dort noch, greifen nur Liste und Namensregel.
- Laufwerke, die gerade fehlen (USB), werden still übersprungen; ihre Spiele fehlen bis zum nächsten Scan.
- Soundtracks (`steamapps\music`) und Workshop-Inhalte werden nicht betrachtet.
- `StateFlags` während eines laufenden Updates sind am Zielrechner nicht gemessen (beobachtet nur 4 und 6). Nach
  öffentlichen Beschreibungen setzt Steam während des Herunterladens eines Updates Werte wie 1026 (1024 + 2, ohne Bit 4);
  dann fiele ein installiertes Spiel für die Dauer des Updates aus dem Scan. Für Phase 3/4 heißt das: Ein Spiel, das bei
  einem erneuten Scan fehlt, darf nicht sofort aus der Bibliothek (samt HDR-Einstellung) gelöscht werden.

## 6 Nachprüfung (unabhängiger Prüfer, 2026-10-07)

Am echten System nur lesend nachgeprüft (eigene PowerShell-Auswertung, nicht der Parser des Scanners):

- Registry: `HKCU\Software\Valve\Steam\SteamPath` (REG_SZ) = `c:/program files (x86)/steam`, `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`
  = `C:\Program Files (x86)\Steam`, `HKCR\steam\Shell\Open\Command` = `"C:\Program Files (x86)\Steam\steam.exe" -- "%1"`.
- `libraryfolders.vdf` (rund 17 KB, zuletzt einige Stunden vor dieser Installation geschrieben): Bibliotheken C:, D:, F:; Block `apps` 636 IDs.
- Manifeste per Regex gelesen: 637 (C: 190, D: 107, F: 340), keine doppelte AppID, alle Spielordner vorhanden. StateFlags
  bei der Nachprüfung: 636 × 4, 1 × 6 (1621690 Core Keeper, Update ausstehend; bleibt im Scan wie vorgesehen).
- Scan (`dotnet run … scan --source Steam`): 631 Spiele, 0 Fehler, 112 ms. Für alle 631 stimmen Name, `InstallDir` und
  Start-Befehl mit der Regex-Auswertung der Manifeste überein; jeder `InstallDir` existiert; keine doppelte ID, kein
  doppelter Ordner; `executables` überall leer. Nur in den Manifesten: genau die 6 Einträge der festen Liste.
- App-Typen über den Leser des Scanners für alle 637 Manifeste: 615 `Game` und 6 `game` (Kleinschreibung, etwa 400 Portal;
  der Vergleich ist deshalb ohne Groß-/Kleinschreibung), 6 `Demo`, 1 `Beta`, 3 ohne Typ, 2 `Tool`, 4 `Application`.
- Gegenquellen wie in Abschnitt 3 nachgezählt: HKCU `Apps` 804 × `Installed` = 1 (173 zusätzlich, 167 davon ohne
  Manifest), 7 abweichende Registry-Namen, 1 571 `.url`-Verknüpfungen mit `steam://rungameid/<AppID>`, 104 Ordner in
  `common` ohne Manifest (nur `STALKER Shadow of Chernobyl` mit EXE).
- Neue Tests: Werttyp DWORD/REG_BINARY für `SteamPath`, Bibliothekspfad mit Umlaut, `/` und abschließendem `\`, nicht
  vorhandenes Laufwerk, leere Manifest- und Bibliotheksdatei, Zufallsbeschädigung aller drei Dateiformate
  (`SteamUbisoftRobustnessTests`, je 1 500 Runden, testweise 40 000 ohne Befund). Am Steam-Code war nichts zu korrigieren.
