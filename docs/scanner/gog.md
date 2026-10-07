# Scanner: GOG Galaxy

> Vor der Veröffentlichung bereinigt: Installationsordner des Messrechners sind durch erfundene ersetzt (etwa
> `D:\Steam-Bibliothek`, `D:\GOG-Spiele`), vollständige Spielelisten durch erfundene Beispielzeilen. Spiele an
> Standardorten (`C:\XboxGames`, `…\Steam\steamapps\common`, `C:\Program Files\EA Games`, `…\GOG Galaxy\Games`)
> tragen erfundene Namen und Kennungen.

Klasse `HdrPilot.Core.Games.Scanners.GogScanner` (`GameSource.Gog`, Name „GOG Galaxy“), Hilfsklassen unter
`src/HdrPilot.Core/Games/Scanners/Gog/` und `…/Scanners/Common/`. Tests: `tests/HdrPilot.Tests/Scanners/GogScannerTests.cs`
(35 Tests), Beispieldateien `tests/HdrPilot.Tests/Fixtures/Gog/`. Alle Angaben gemessen am 2026-10-07 auf dem
Zielrechner, nur lesend.

## 1. Prüfangaben aus dem Auftrag

| Angabe im Auftrag | Ergebnis | Beleg |
|---|---|---|
| `HKLM\Software\GOG.com\Games\<ID>` über die 32-Bit-Sicht (`RegistryView.Registry32`) | **bestätigt** | `RegistryKey.OpenBaseKey(LocalMachine, Registry32).OpenSubKey("Software\GOG.com\Games")` liefert 6 Unterschlüssel (5 Spiele und ein Zusatzinhalt; die einzelnen IDs vor der Veröffentlichung entfernt). In der 64-Bit-Sicht gibt es keinen Schlüssel `GOG.com`. |
| Wertnamen für Name, Pfad und EXE am System ablesen | **abgelesen, zwei Schreibweisen** | GOG-Galaxy-Einträge (4): `gameName`, `path`, `exe`, `exeFile`, `workingDir`, `launchParam`, `launchCommand`, `dependsOn`, `gameID`, `productID`. Ältere Offline-Installer (SWAT 3, SWAT 4): `GAMENAME`, `PATH`, `EXE`, `EXEFILE`, `WORKINGDIR`, `LAUNCHPARAM`, `LAUNCHCOMMAND`, `DEPENDSON`, dazu `gameID`. Registry-Wertnamen unterscheiden keine Groß-/Kleinschreibung; ein Satz Namen liest beide. |
| Arbeitsordner, Startparameter | **abgelesen** | `workingDir` = `D:\GOG-Spiele\Cyberpunk 2077`; `WORKINGDIR` = `F:\GOG-Spiele\SWAT 3\` (mit abschließendem `\`); `launchParam` überall leer. `launchCommand` = EXE plus Parameter, teils mit Leerzeichen am Ende (`…\swat4.exe `, `…\Starter.exe `). |
| Start direkt über die EXE | **bestätigt** (nicht gestartet) | Alle 5 `exe`-Werte zeigen auf vorhandene Dateien. Die Startmenü-Verknüpfungen der Offline-Installer zeigen ebenfalls direkt auf die EXE (`SWAT 3 - Tactical Game of The Year Edition.lnk` → `F:\GOG-Spiele\SWAT 3\swat.exe`). |
| DLC- oder abhängige Einträge (`dependsOn`) | **vorhanden, gefiltert** | `1256837418` „Cyberpunk 2077: Phantom Liberty“: `dependsOn` = `1423049311`, kein `exe`/`workingDir`/`launchCommand`, `path` = Ordner des Hauptspiels. Gegenstück am Hauptspiel: `DLC` = `1256837418`. Dazu `goggame-1256837418.info` mit `"rootGameId": "1423049311"` und leeren `playTasks`. |
| GOG Galaxy selbst ist kein Spiel | **bestätigt** | Kein Eintrag unter `Games`. Der Client steht unter `HKLM\Software\GOG.com\GalaxyClient` (`clientExecutable` = `GalaxyClient.exe`, `version` = `2.0.97.42`, `paths\client` = `C:\Program Files (x86)\GOG Galaxy`). Beispielspiel Eins liegt **unter** dem Client-Ordner (`…\GOG Galaxy\Games\Beispielspiel Eins`). |
| Zweite Quelle: Uninstall `<ID>_is1` | **bestätigt** | 6 Einträge unter `HKLM\…\Uninstall` (32-Bit-Sicht), `Publisher` = `GOG.com`, `InstallLocation` mit abschließendem `\`. |
| Zweite Quelle: `goggame-<ID>.info`, `playTasks` mit `isPrimary` | **bestätigt** | In allen 5 Spielordnern vorhanden (Cyberpunk-Ordner enthält zusätzlich die Datei des DLC). Neuere Dateien mit `category` (`launcher`, `game`, `document`), ältere (SWAT 3) ohne `category`. |

## 2. Format

**Registry** `HKLM\Software\GOG.com\Games\<ID>` (32-Bit-Sicht), alle Werte REG_SZ:

| Wert (Galaxy / Offline) | Beispiel | Verwendung |
|---|---|---|
| `gameID` | `1423049311` | `SourceId` (Ersatz: Name des Unterschlüssels) |
| `gameName` / `GAMENAME` | `Cyberpunk 2077` | Name; Ersatz: `name` der Spielinfo |
| `path` / `PATH` | `D:\GOG-Spiele\Cyberpunk 2077` | `InstallDir`; Pflicht |
| `exe` / `EXE` | `D:\GOG-Spiele\Cyberpunk 2077\REDprelauncher.exe` | `LaunchCommand` |
| `workingDir` / `WORKINGDIR` | `D:\GOG-Spiele\Cyberpunk 2077` | `LaunchWorkingDirectory` |
| `launchParam` / `LAUNCHPARAM` | (leer) | `LaunchArguments` |
| `dependsOn` / `DEPENDSON` | `1423049311` | gesetzt → Zusatzinhalt, überspringen |
| `launchCommand`, `exeFile`, `DLC`, `startMenu…`, `uninstallCommand`, `ver`, `BUILDID`, `INSTALLDATE`, `language` | | nicht genutzt |

**Spielinfo** `<path>\goggame-<ID>.info` (JSON, UTF-8 ohne BOM, LF): `gameId`, `rootGameId`, `name`, `playTasks[]` mit
`isPrimary`, `type` (`FileTask`/`URLTask`), `category`, `path` (relativ, `/` oder `\`, teils doppelt `Content//System`),
`workingDir`, `arguments`. Beispiel aus Beispielspiel Eins: primäre Aufgabe `Launcher/Starter.exe` (Kategorie `launcher`),
versteckte Aufgaben `bin/eins.exe` und `bin/eins_dx11.exe` (Kategorie `game`) = die eigentlichen Spielprozesse.

## 3. Scan-Ergebnis am echten System

`dotnet run --project src\HdrPilot.Cli -- scan --source gog`: `launcherDetected: true`, 5 Spiele, 0 Fehler, ca. 20 ms.

Die Tabellen dieses Abschnitts nannten die Spiele des Messrechners. Vor der Veröffentlichung sind sie durch erfundene
Beispielzeilen im selben Aufbau ersetzt (Namen, IDs und Ordner frei erfunden); der Text beschreibt weiter die Messung.

| ID | Name | InstallDir | Start-Befehl (Arbeitsordner) |
|---|---|---|---|
| `1999999901` | Beispielspiel Eins | `C:\Program Files (x86)\GOG Galaxy\Games\Beispielspiel Eins` | `…\Beispielspiel Eins\Launcher\Starter.exe` (`…\Launcher`) |
| `1999999902` | Beispielspiel Zwei | `D:\GOG-Spiele\Beispielspiel Zwei` | `D:\GOG-Spiele\Beispielspiel Zwei\Zwei.exe` (`D:\GOG-Spiele\Beispielspiel Zwei`) |
| `1999999903` | Beispielspiel Drei - Gold Edition | `F:\GOG-Spiele\Beispielspiel Drei` | `F:\GOG-Spiele\Beispielspiel Drei\System\drei.exe` (`F:\GOG-Spiele\Beispielspiel Drei\System`) |

Startargumente: bei allen leer. Bekannte EXE-Dateien:

| Spiel | Executables |
|---|---|
| Beispielspiel Eins | `Launcher\Starter.exe`, `bin\eins.exe`, `bin\eins_dx11.exe` |
| Beispielspiel Zwei | `Zwei.exe`, `Engine\Binaries\Win64\Zwei-Win64-Shipping.exe` |
| Beispielspiel Drei | `System\drei.exe` |

### Vergleich mit der zweiten Quelle

| ID | Uninstall `DisplayName` | `rootGameId` (.info) | im Scan | Name gleich | Ordner gleich | primäre playTask = Start-EXE |
|---|---|---|---|---|---|---|
| 1999999901 | Beispielspiel Eins | 1999999901 | ja | ja | ja | ja |
| 1999999904 | Beispielspiel Eins: Erweiterung | **1999999901** | **nein** | – | – | – (keine playTasks) |
| 1999999902 | Beispielspiel Zwei | 1999999902 | ja | ja | ja | ja |
| 1999999903 | Beispielspiel Drei - Gold Edition | 1999999903 | ja | ja | ja | ja |

Uninstall: 6 Einträge, Scan: 5. **Einzige Abweichung: Phantom Liberty (1256837418)**, gewollt: Zusatzinhalt
(`dependsOn` = `1423049311`, `rootGameId` = `1423049311`, keine EXE, gleicher Ordner wie das Hauptspiel). Als eigenes
Spiel würde er denselben Ordner doppelt belegen. Ordner verglichen ohne den abschließenden `\` der Uninstall-Einträge;
die primäre playTask wurde relativ zum Ordner aufgelöst und ohne Groß-/Kleinschreibung verglichen (`SWAT.EXE`).

Dritte Quelle (Startmenü `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\*[GOG.com]\*.lnk`): Galaxy legt
Verknüpfungen auf `GalaxyClient.exe /command=runGame /gameId=<ID> /path="<Ordner>"` an (4 Spiele), die
Offline-Installer direkt auf die EXE (`swat.exe`, `Swat4.exe`). IDs und Ordner stimmen mit dem Scan überein.

## 4. Entscheidungen

1. **32-Bit-Sicht über `RegistryViewKind.Registry32`**, kein `WOW6432Node` im Pfad (Test
   `Games_key_is_opened_through_32bit_view_without_wow6432node_path` prüft jeden geöffneten Pfad).
2. **Zusatzinhalte** werden doppelt erkannt: `dependsOn` nennt ein anderes Spiel **oder** `rootGameId` der Spielinfo
   ≠ ID. Beide Wege sind einzeln getestet. Die Spielinfo wird erst gelesen, wenn der Spielordner existiert. „Nennt ein
   anderes Spiel“ heißt: nicht leer, nicht `0` und nicht die eigene ID (nach der Prüfung ergänzt). Vorher machte ein
   als DWORD 0 geschriebenes `dependsOn` (gelesen als „0“) oder ein `"rootGameId": 0` ein Spiel still zum
   Zusatzinhalt; es verschwand ohne Fehler aus der Bibliothek.
3. **Start direkt über die EXE** (Auftrag): `exe` + `launchParam` + `workingDir` aus der Registry. Fehlt die EXE dort
   oder auf der Platte, gilt die primäre `FileTask` der Spielinfo (Pfad relativ zum Spielordner). Ohne beides:
   `ScanError` „keine vorhandene Start-EXE“, Spiel nicht geliefert, weil der Eintrag dann kaputt ist.
4. **Arbeitsordner** nur, wenn angegeben und vorhanden; sonst `null` (der Start nimmt dann den Ordner der EXE).
5. **Executables:** Start-EXE, Registry-`exe` und aus der Spielinfo die `FileTask`s mit `isPrimary` oder Kategorie
   `game`. Werkzeuge ohne Kategorie (SWAT 3: `wc.exe`, `ScenarioEd.exe`) und Dokumente bleiben draußen. Nur vorhandene
   `.exe` unter dem Spielordner; es wird nicht gesucht. Das Spiel selbst wird so auch hinter einem Starter bekannt
   (`eins.exe` hinter `Starter.exe`, `Cyberpunk2077.exe` hinter `REDprelauncher.exe`).
6. **Pfade behalten die Schreibweise der Quelle** (`SWAT.EXE`, auf der Platte `swat.exe`); Vergleiche erfolgen
   ohne Groß-/Kleinschreibung (`PathUtil`).
7. **GOG Galaxy ist nie ein Spiel:** Ein Eintrag, dessen Ordner den Client-Ordner (`GalaxyClient\paths\client`) enthält
   oder ihm gleicht, wird als Fehler gemeldet. Spiele **unter** dem Client-Ordner (Beispielspiel Eins) bleiben gültig.
8. **Name:** `gameName`, sonst `name` der Spielinfo; fehlt beides → `ScanError`.
9. **Fehlerbild:** nicht lesbarer Schlüssel, fehlender `path`, falscher Werttyp, Laufwerkswurzel, Galaxy-Ordner →
   `ScanError` mit Registry-Pfad; defekte oder nicht lesbare Spielinfo → `ScanError` mit Dateipfad, das Spiel kommt
   trotzdem (Start aus der Registry). Fehlender Spielordner → nur Protokoll.
10. **LauncherDetected:** Schlüssel `Games` oder `GalaxyClient` vorhanden. Offline-Installer schreiben `Games` auch
    ohne Galaxy.
11. Der Konstruktor nimmt `IAppEnvironment` laut Vertrag an, der Scanner braucht aber keine Systemordner.
12. **Ungültige Escape-Folgen in der Spielinfo** (einzelnes UTF-16-Surrogat wie `\uD800`, nach der Prüfung ergänzt):
    System.Text.Json wirft dafür erst beim Dekodieren eine `InvalidOperationException`, die vorher den ganzen GOG-Scan
    abbrach. Jetzt: „GOG-Spielinfo defekt: Feld "…" enthält eine ungültige Escape-Folge …“, das Spiel kommt trotzdem
    aus der Registry (gemeinsamer Leser `Scanners/Common/ScannerJson.cs`, siehe auch `epic.md`).

## 5. Bekannte Grenzen

- **Start ohne Galaxy:** Der direkte EXE-Start umgeht GOG Galaxy (Annahme, nicht geprüft: dann keine
  Cloud-Speicher-Synchronisierung, kein Overlay, keine Spielzeit). Galaxy selbst startet über
  `GalaxyClient.exe /command=runGame /gameId=<ID> /path="<Ordner>"` (gemessen in den Startmenü-Verknüpfungen).
  Umsetzung so wie im Auftrag; die Galaxy-Variante wäre eine spätere Option.
- **Cyberpunk 2077:** `REDprelauncher.exe` startet laut Spielinfo (`additionalPaths`) den REDlauncher unter
  `%LOCALAPPDATA%\Programs\CD Projekt Red\REDlauncher`, also **außerhalb** des Spielordners. Für die Prozesszuordnung
  ist das unkritisch (das Spiel `bin\x64\Cyberpunk2077.exe` liegt unter `InstallDir`), der REDlauncher selbst wird aber
  nicht als Spiel erkannt.
- **Showgunners:** Die Spielinfo nennt `ArtificerOne/Binaries/Win64/Showgunners.exe`, die Datei existiert nicht; sie
  fehlt deshalb in `Executables` (nur vorhandene Dateien).
- **SWAT 4:** Die Erweiterung „The Stetchkov Syndicate“ (`ContentExpansion\System\SWAT4X.exe`) ist eine playTask ohne
  Kategorie und steht nicht in `Executables`; sie liegt unter `InstallDir` und wird in Phase 3 über den Ordner erkannt.
- SWAT 3 enthält viele weitere EXE-Dateien (`swat_1366x768.exe`, `AccountWizard.exe`, `unins000.exe` …); Hilfsprogramme
  wie `unins000.exe` gehören auf die Ausschlussliste von Phase 3. Weitere gemessene Kandidaten: `unins001.exe`
  (Deinstaller von Phantom Liberty im Cyberpunk-Ordner), `bin\x64\REDEngineErrorReporter.exe` und
  `bin\x64\CrashReporter\CrashReporter.exe`/`7za.exe` (Cyberpunk), `Launcher\CrashReporter.exe`,
  zwei Prüfprogramme des Starters und `Launcher\runtimes\…\CefSharp.BrowserSubprocess.exe` (Beispielspiel Eins, Teil des
  Launcher-Fensters), `Engine\Extras\Redist\en-us\UE4PrereqSetup_x64.exe` (Showgunners), `Swat4DedicatedServer.exe`,
  `SwatEd.exe`, `UCC.exe` (SWAT 4).
- Liegen zwei Einträge, die keine Zusatzinhalte sind, im selben Ordner (oder einer im Ordner des anderen), liefert der
  Scanner beide; in Phase 3 passt ein Prozess dann zu beiden. Am Zielrechner teilt sich nur Phantom Liberty (gefiltert)
  den Ordner mit Cyberpunk 2077.
- Spiele, die nur in der Galaxy-Datenbank (`%ProgramData%\GOG.com\Galaxy\storage\galaxy-2.0.db`, SQLite, am
  Zielrechner vorhanden) stehen, aber keinen Registry-Eintrag haben, findet der Scanner nicht. Die Datenbank wurde nicht
  gelesen: SQLite bräuchte ein neues Paket. Am Zielrechner decken sich Registry, Uninstall-Einträge und Spielinfos.

## 6. Unabhängige Nachprüfung (2026-10-07)

Zweiter Durchgang durch einen getrennten Prüfer, nur lesend am Zielrechner:

- Registry über `RegistryKey.OpenBaseKey(LocalMachine, Registry32)` erneut gelesen: 6 Unterschlüssel, alle Werte
  REG_SZ, beide Schreibweisen der Wertnamen wie in Abschnitt 1; die 64-Bit-Sicht hat keinen Schlüssel `GOG.com`.
  `GalaxyClient` (`clientExecutable`, `version` 2.0.97.42, `paths\client`) wie angegeben. **Bestätigt.**
- Uninstall-Einträge (32-Bit-Sicht): 6 Spiele `<ID>_is1` mit `Publisher` = `GOG.com`, dazu der Client selbst
  (`{7258BA11-…}_is1`, „GOG GALAXY“); 64-Bit-Sicht und HKCU ohne GOG-Einträge.
- Alle 6 `goggame-<ID>.info` gelesen (SWAT 3 beginnt mit einem Zeilenumbruch vor `{`, wird korrekt gelesen);
  `rootGameId`, primäre playTasks und Kategorien wie in Abschnitt 2; existierende Dateien wie in Abschnitt 3
  (Showgunners: `ArtificerOne/Binaries/Win64/Showgunners.exe` fehlt, `…-Win64-Shipping.exe` existiert).
- Startmenü-Verknüpfungen erneut gelesen, Angaben der dritten Quelle bestätigt.
- Scan selbst ausgeführt: 5 Spiele, 0 Fehler, Tabelle in Abschnitt 3 zeichengleich; einzige Abweichung zu den
  Uninstall-Einträgen bleibt Phantom Liberty (gewollt).
- **Gefundene Fehler, behoben** (je ein Test, der ohne die Korrektur fehlschlug):
  - Spielinfo mit einzelnem Surrogat (`\uD800`) brach den ganzen GOG-Scan ab (Entscheidung 12):
    `Broken_info_file_with_lone_surrogate_escape_is_reported_but_game_delivered`.
  - `dependsOn` als DWORD 0 versteckte das Spiel still als Zusatzinhalt (Entscheidung 2):
    `Broken_dependsOn_written_as_dword_zero_is_not_dlc`.
  - `"rootGameId": 0` in der Spielinfo ebenso: `Incomplete_info_with_root_game_id_zero_empty_or_own_id_is_not_dlc`.
- Zusätzlich abgesichert: Spielinfo in falscher Kodierung (Latin-1), Registry-Werte als DWORD, QWORD, REG_MULTI_SZ und
  REG_BINARY, Pfade mit Umlauten, `&`, `#`, Klammern, Schrägstrichen, abschließendem Trenner, abweichender
  Groß-/Kleinschreibung und fehlendem Laufwerk.
