# Scanner: EA app (`EaScanner`, Quelle `EA`)

> Vor der Veröffentlichung bereinigt: Installationsordner des Messrechners sind durch erfundene ersetzt (etwa
> `D:\Steam-Bibliothek`, `D:\GOG-Spiele`), vollständige Spielelisten durch erfundene Beispielzeilen. Spiele an
> Standardorten (`C:\XboxGames`, `…\Steam\steamapps\common`, `C:\Program Files\EA Games`, `…\GOG Galaxy\Games`)
> tragen erfundene Namen und Kennungen.

Stand: 2026-10-07, Zielrechner (Windows 11 Build 26200, EA app 13.759.2.6273). Alle Angaben unten sind am echten
System nur lesend geprüft (PowerShell, `Get-ItemProperty`, `[IO.File]::ReadAllBytes`); nichts wurde gestartet,
geschrieben oder entschlüsselt.

Code: `src/HdrPilot.Core/Games/Scanners/EaScanner.cs`, Hilfsklassen unter `src/HdrPilot.Core/Games/Scanners/Ea/`.
Tests: `tests/HdrPilot.Tests/Scanners/EaScannerTests.cs`, Beispieldateien unter `tests/HdrPilot.Tests/Fixtures/EA/`.

## 1. Prüfpunkte aus dem Auftrag

| Angabe | Ergebnis | Beleg |
|---|---|---|
| Bibliotheksdatei unter `%ProgramData%\EA Desktop` ist verschlüsselt und hardwaregebunden; nicht entschlüsseln | **beachtet, nicht geöffnet** | `C:\ProgramData\EA Desktop\<64 Hex-Zeichen>\` enthält nur Binärdateien ohne Endung (`IS` rund 66 KB, `CATS`, `CATS2`, `IQ`, `CONF-production`). Nur die Namen wurden gelistet, kein Inhalt gelesen. |
| Spieleordner an `__Installer\installerdata.xml` erkennen | **bestätigt, mit Einschränkung** | `C:\Program Files\EA Games\Eisenwall 6\__Installer\installerdata.xml` (rund 360 KB). Suche auf C:, D:, E:, F: bis Tiefe 3 (ohne `C:\Windows`, `C:\Users`, `C:\ProgramData`): genau dieser eine Fund. Prüfung mit vollständiger Suche (`dir /s /b /a <Laufwerk>\*installerdata.xml` auf C:, D:, E:, F:): derselbe Fund plus **9 weitere** `installerdata.xml` von Zusatzinhalten unter `Eisenwall 6\__Installer\DLC\<DLC>\__Installer\` (`EW6_ZUS1_DLC`, `EW6_HRES_MARKER_DLC`, …), jeweils `<ContentIDs><contentID /></ContentIDs>` (leer), Titel „Eisenwall™ 6 - Single Player“, kein `runtime`. Diese Ordner sind keine Spiele (siehe Entscheidungen). |
| Übliche Installationsordner (z. B. `C:\Program Files\EA Games`) | **bestätigt** | `C:\Program Files\EA Games\` enthält `Eisenwall 6`. `C:\Program Files (x86)\Origin Games\` (Standard des alten Origin) existiert und ist leer (`dir /a`: 0 Dateien, keine Unterordner). |
| In der EA app eingestellter Installationsordner ohne Entschlüsselung lesbar (z. B. .ini unter `%LocalAppData%\Electronic Arts\EA Desktop`) | **abweichend: nicht lesbar** | `%LocalAppData%\Electronic Arts\EA Desktop\user_<Zahl>.ini` enthält nur Fenster-, Sprach- und Hinweiswerte; `%ProgramData%\EA Desktop\machine.ini` und `backgroundservice.ini` nur Update-, Experiment- und Dienstwerte; kein Installationsordner (nur die Schlüsselnamen gelesen). Die EA app hält ihn offenbar nur in der verschlüsselten Ablage. Lesbar ist dagegen die Einstellung des **alten Origin-Clients**: `%AppData%\Origin\local.xml`, `<Setting type="10" key="DownloadInPlaceDir" value="D:\Programmordner\EA Games\"/>` (Ordner existiert heute nicht mehr). |
| Uninstall-Einträge von EA-Spielen mit InstallLocation | **bestätigt** | `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{0f1e2d3c-…}`: `DisplayName` = `Eisenwall™ 6`, `Publisher` = `Electronic Arts`, `InstallLocation` = `C:\Program Files\EA Games\Eisenwall 6\`, `UninstallString` = `"C:\Program Files\Common Files\EAInstaller\Eisenwall 6\Cleanup.exe" uninstall_game -autologging`, `DisplayIcon` = `"C:\Program Files\EA Games\Eisenwall 6\ew6.exe"`. Nur in der 32-Bit-Sicht. |
| Anzeigename aus `gameTitles/gameTitle` mit `locale` | **bestätigt** | `<gameTitles><gameTitle locale="en_US">Eisenwall™ 6</gameTitle>… <gameTitle locale="de_DE">Eisenwall™ 6</gameTitle>…` (13 Sprachen, de_DE an dritter Stelle). |
| Stabile ID aus `contentIDs/contentID` | **bestätigt** | `<contentIDs><contentID>18306642</contentID></contentIDs>`. Dieselbe Zahl nutzt die EA app selbst: `HKLM\SOFTWARE\WOW6432Node\Origin Games\18306642` (`DisplayName` Eisenwall™ 6), Cloud-Spielstände `%LocalAppData%\Electronic Arts\EA Desktop\cloudsync\18306642_18306642.*`. |
| EXEs aus `runtime/launcher/filePath` mit `[HKEY_…]`-Makros | **bestätigt** | `<filePath>[HKEY_LOCAL_MACHINE\SOFTWARE\EA Games\Eisenwall 6\Install Dir]EAAntiCheat.GameServiceLauncher.exe</filePath>` und `…\Install Dir]SP/ew6.exe` (zweiter Starter `hidden=1`, Einzelspieler). Der Wert existiert in beiden Sichten: `HKLM\SOFTWARE\EA Games\Eisenwall 6` und `HKLM\SOFTWARE\WOW6432Node\EA Games\Eisenwall 6`, `Install Dir` = `C:\Program Files\EA Games\Eisenwall 6\` (mit abschließendem Backslash; Pfadteil danach mit `/`). |
| Welches Protokoll registriert die EA app (link2ea, origin2, …) | **bestätigt** | `HKCR\link2ea\shell\open\command` = `"…\EA Desktop\Link2EA.exe" "%1" …`; `HKCR\origin2` und `HKCR\origin` = `…\EA Desktop\EALauncher.exe "%1"`; `HKCR\ealink` = `…\legacyPM\OriginLegacyCLI.exe "%1" -wait`; `HKCR\eadm` zeigt noch auf das alte `Origin.exe`. |
| Wie sieht ein Start-Link aus (Desktop-Verknüpfungen) | **abweichend: Verknüpfungen zeigen auf die EXE** | `Desktop\Games\Eisenwall 6.lnk` → Ziel `C:\Program Files\EA Games\Eisenwall 6\ew6.exe`, Arbeitsordner der Spielordner, ohne Argumente. Kein `link2ea://`-Link auf dem Desktop oder im Startmenü. Den Protokoll-Link zeigt das Protokoll der EA app: `EALaunchHelper.log` (2026-05-06): `Command line: [...\EALaunchHelper.exe origin2://game/launch/?offerIds=18306642&title=Eisenwall%u2122%u00206&authCode=&cmdParams=]`, ebenso 2023 für ein früheres Spiel (Name vor der Veröffentlichung entfernt) mit `offerIds=<contentID>`. `EADesktop.bak` zeigt, dass die EA app die Zahl als contentID verarbeitet: `Handling launch game request: … contentIds=[<contentID>], requestOwner=[EA], requestSource=[RTP]` und danach `Issuing launch: … offer[Origin.OFR.<…>] content[<contentID>]`. `link2ea://launchgame/<SteamAppID>?platform=steam` kommt nur bei über Steam gekauften EA-Spielen vor (`Link2EA.log`). Nachprüfung (Review): `EALaunchHelper.log` 2026-05-06 und 2026-08-01 verarbeitet die URI als `"act":"GameLaunch"` mit `"id_list":"18306642"`; `EADesktop.log` zeigt den Start durch die EA app selbst: `Got exePath=[C:\Program Files\EA Games\Eisenwall 6\EAAntiCheat.GameServiceLauncher.exe]` und `Issuing launch: workingDirectory[C:\Program Files\EA Games\Eisenwall 6] offer[Origin.OFR.50.0009001] content[18306642]`. Das belegt contentID, InstallDir und dass die Spielprozesse unter dem Spielordner starten. |

## 2. Format

`__Installer\installerdata.xml` (gemessen: DiPManifest 4.0, „Manifest generated by the EAInstaller-5.08.03.00“):

- UTF-8 ohne BOM, Deklaration `<?xml version='1.0' encoding='utf-8'?>`, LF-Zeilenenden. Der Parser bestimmt die
  Kodierung aus BOM bzw. Deklaration; Bytes, die nicht dazu passen, gelten als defekte Datei.
- Wurzel `<DiPManifest version="4.0">`, darunter `buildMetaData`, `contentIDs`, `gameTitles`, `uninstall`, `runtime`,
  `installMetaData` (Dateilisten, 320 KB), `touchup`, `installManifest`.
- Verwendet: `contentIDs/contentID` (erste = `SourceId`), `gameTitles/gameTitle[@locale]` (Name: de_DE, sonst en_US,
  sonst erster Eintrag; `-` und `_` sowie Groß-/Kleinschreibung im Locale gleichgesetzt), `runtime/launcher/filePath`
  (EXEs), `uninstall/path` (Makro auf `…\Uninstall\{GUID}\UninstallString`; daraus `DisplayIcon` desselben Schlüssels).
- Registry-Makro: `[<Hive>\<Schlüssel>\<Wertname>]<relativer Pfad>`, Hive `HKEY_LOCAL_MACHINE`/`HKLM` oder
  `HKEY_CURRENT_USER`/`HKCU`. Gelesen erst in der 64-Bit-, dann in der 32-Bit-Sicht, weil der EA-Installer den
  Uninstall-Schlüssel nur nach WOW6432Node schreibt, obwohl das Makro den Pfad ohne WOW6432Node nennt.
- `touchup/filePath` (`/__Installer/touchup.exe`) ist der Reparatur-Installer und wird nicht als Spiel-EXE geführt.
- Zusatzinhalte haben eine eigene `installerdata.xml` unter `<Spiel>\__Installer\DLC\<DLC>\__Installer\` (gemessen:
  9 Stück bei Eisenwall 6, Wurzel ebenfalls `DiPManifest` 4.0, `<ContentIDs>` mit leerer `<contentID />`, kein
  `runtime`, `uninstall/path` auf `HKLM\SOFTWARE\EA Games\Eisenwall 6\DLCs\<DLC>\UninstallerPath`).

`%AppData%\Origin\local.xml`: `<Settings><Setting type="…" key="…" value="…"/>…</Settings>`; nur `DownloadInPlaceDir`
wird gelesen (die Datei enthält auch Anmeldedaten, die nicht angefasst werden).

Fundorte in dieser Reihenfolge (jeder Ordner nur einmal, Vergleich ohne Groß-/Kleinschreibung, Schrägstriche
vereinheitlicht):

1. Uninstall-Einträge (HKLM 64 Bit, HKLM 32 Bit, HKCU) mit `InstallLocation`, deren `UninstallString`/`QuietUninstallString`
   `EAInstaller` enthält oder deren `Publisher` „Electronic Arts“ enthält.
2. `HKLM\SOFTWARE\EA Games\<Spiel>`, Wert `Install Dir` (beide Sichten).
3. Unterordner von `%ProgramFiles%\EA Games`, `%ProgramFiles(x86)%\Origin Games` und `DownloadInPlaceDir` aus Origin.
4. Suchordner des Nutzers (`ScanOptions.SearchFolders`): der Ordner selbst und Unterordner bis Tiefe 3; in einem
   erkannten Spielordner wird nicht weiter abgestiegen.

Ein Ordner zählt nur, wenn `__Installer\installerdata.xml` darin liegt und er selbst nicht unterhalb eines
`__Installer`-Ordners liegt (Zusatzinhalte). In `__Installer`-Ordner steigt die Suche nicht ab.

## 3. Ergebnis am echten System

`dotnet run --project src\HdrPilot.Cli -- scan --source EA` (Exit 0, 53 ms, `launcherDetected: true`, keine Fehler):

| ID | Name | InstallDir | Start-Befehl |
|---|---|---|---|
| 18306642 | Eisenwall™ 6 | `C:\Program Files\EA Games\Eisenwall 6` | `origin2://game/launch/?offerIds=18306642&authCode=&cmdParams=` |

EXEs: `…\EAAntiCheat.GameServiceLauncher.exe`, `…\SP\ew6.exe`, `…\ew6.exe` (alle vorhanden).
Mit Suchordnern `D:\;E:\;F:\;C:\Program Files` dasselbe eine Spiel (Duplikate aus vier Fundorten zusammengeführt).

Nachprüfung im Review (2026-10-07): Scan ohne Suchordner identisch (1 Spiel, keine Fehler, 52 ms). Mit Suchordnern
`C:\;D:\;E:\;F:\;C:\Program Files\EA Games\Eisenwall 6\__Installer;…\__Installer\DLC` lieferte der Stand vor dem
Review dasselbe Spiel, aber **9 falsche Scanfehler** („enthält keine contentID“) für die DLC-Manifeste; nach der
Korrektur 1 Spiel, keine Fehler (1,5 s, die Suche in `C:\` bis Tiefe 3 kostet den Großteil).

Zweite Quelle: Uninstall-Einträge mit Herausgeber Electronic Arts und die tatsächlichen Ordner.

| Zweite Quelle | Eintrag | Im Scan | Begründung |
|---|---|---|---|
| Uninstall `{0f1e2d3c-…}` | Eisenwall™ 6, `C:\Program Files\EA Games\Eisenwall 6\` | ja | übereinstimmend |
| Uninstall `Steam App <AppId>` | ein früheres Steam-Spiel, Herausgeber Electronic Arts (Name vor der Veröffentlichung entfernt) | nein | Steam-Spiel (`steam://uninstall/<AppId>`); sein Ordner unter `…\steamapps\common\` existiert nicht mehr, ohne `__Installer`. Gehört ohnehin zum Steam-Scanner. |
| Uninstall `{C2622085-…}`, `{96fdc5f5-…}`, `Origin` | EA app, Origin | nein | Launcher, keine Spiele (kein `installerdata.xml`). |
| Ordner `C:\Program Files\EA Games\` | `Eisenwall 6` | ja | übereinstimmend |
| `%ProgramData%\EA Desktop\InstallData\` | Eisenwall 6 und vier weitere Ordner früherer Installationen (Namen vor der Veröffentlichung entfernt) | nur Eisenwall 6 | Diese Ordner bleiben nach einer Deinstallation stehen. Die Spielordner der anderen fehlen (Orte laut Desktop-Verknüpfungen bzw. EA-Protokoll, teils unter `D:\Programmordner\EA Games\`, teils unter `C:\Program Files\EA Games\`) – alle nicht vorhanden. |
| `HKLM\SOFTWARE\WOW6432Node\Origin Games\<contentID>` | 20 Schlüssel (u. a. 18306642, ew_content00x, ew_hd00x und Kennungen früherer Spiele, vor der Veröffentlichung entfernt) | nur 18306642 | Nur `DisplayName`/`Locale`, kein Pfad; Reste früherer Installationen und Teilinhalte (DLC, HD-Texturen) von Eisenwall 6. Deshalb keine Quelle für den Scanner. |

## 4. Entscheidungen

- **Kein Lesen der EA-Bibliothek.** Wie vorgegeben; auch die Dateien unter `%ProgramData%\EA Desktop\<Hash>\` wurden
  nicht geöffnet.
- **Start über `origin2://game/launch/?offerIds=<contentID>&authCode=&cmdParams=`** statt über die EXE. Das ist genau
  der Aufruf, den das Spiel selbst absetzt, wenn man `ew6.exe` direkt startet (Protokoll oben); so laufen Anmeldung,
  Updates und Anti-Cheat der EA app wie beim Start aus der EA app. Der Parameter `title` (im Protokoll in nicht
  standardgemäßer `%u`-Kodierung) wird weggelassen, die leeren Parameter bleiben wie beobachtet. Desktop-Verknüpfungen
  der EA app zeigen zwar auf die EXE, die aber auch nur diesen Umweg nimmt.
- **SourceId = erste contentID.** Sie ist stabil, in der EA app selbst als Spiel-ID verwendet und hängt nicht am Ordner.
  Fehlt sie, ist die Datei unbrauchbar: Fehler mit Pfad, Spiel übersprungen.
- **InstallDir = Ordner, der `__Installer` enthält.** Alle Spielprozesse (`ew6.exe`, `SP\ew6.exe`, Anti-Cheat) liegen
  darunter.
- **Executables** = alle auflösbaren `runtime/launcher/filePath` plus `DisplayIcon` des in `uninstall/path` genannten
  Uninstall-Eintrags (bei Eisenwall 6 die eigentliche Spiel-EXE `ew6.exe`, die installerdata.xml selbst nicht nennt).
  Nur vorhandene `.exe`-Dateien **unterhalb** des Spielordners: Zeigt der Registry-Wert auf einen alten Ordner, wird
  relativ zum gefundenen Spielordner aufgelöst. Nichts wird geraten.
- **Unvollständige Installation:** Nennt die Datei Start-EXEs, aber keine davon liegt im Ordner, wird der Ordner
  übersprungen (Rest einer Deinstallation oder laufender Download), ohne Scanfehler.
- **Steam-Bibliotheken (`…\steamapps\common\…`) werden ausgelassen**, auch wenn dort ein EA-Spiel mit `__Installer` liegt:
  Solche Spiele meldet der Steam-Scanner mit eigener ID; sonst stünde ein Ordner zweimal in der Bibliothek und in Phase 3
  passte ein Prozess zu zwei Spielen. (Am Zielrechner liegt in keiner Steam- oder Epic-Bibliothek ein `__Installer`.)
- **Ordner unterhalb von `__Installer` sind nie Spiele.** Dort liegen die Manifeste der Zusatzinhalte (gemessen, siehe
  oben). Ohne diese Regel führte ein Suchordner oder Registry-Eintrag in diesen Baum zu einem Scanfehler je DLC bzw.
  bei gefüllter contentID zu einem DLC als eigenem Spiel (Test `Dlc_manifests_inside_the_game_installer_folder_are_neither_games_nor_errors`).
- **Laufwerksstamm als Spielordner wird abgelehnt** (Scanfehler mit Pfad, Spiel übersprungen): Liegt `__Installer`
  direkt in `D:\`, wäre `InstallDir` das ganze Laufwerk und in Phase 3 gälte jeder Prozess darauf als Spiel
  (Test `Broken_game_directly_in_drive_root_is_reported_and_skipped`).
- **Origin-Einstellungen sind nur ein Hinweis:** Ist `local.xml` defekt, wird das protokolliert (Warnung), aber nicht
  als Scanfehler gemeldet, weil dadurch kein Spiel verloren geht, das an anderer Stelle gefunden würde.
- **Doppelte contentID** (etwa eine Sicherungskopie des Spielordners in einem Suchordner): nur der erste Fund zählt;
  die Reihenfolge der Fundorte bevorzugt Registry-Angaben vor Ordnersuche.
- **Fehlermeldungen** nennen immer den Pfad der Datei; die übrigen Spiele werden weiter geliefert.

## 5. Bekannte Grenzen

- Spiele, die die EA app in einen eigenen Ordner außerhalb von `%ProgramFiles%\EA Games` installiert hat, findet der
  Scanner nur über ihren Uninstall-Eintrag bzw. `HKLM\SOFTWARE\EA Games` oder einen Suchordner. Der in der EA app
  eingestellte Standardordner ist nicht lesbar.
- Nur DiPManifest 4.0 ist am Zielrechner belegt. Ältere Manifeste (Origin-Zeit) ohne `gameTitles` bekommen den
  Ordnernamen als Anzeigenamen; ohne `contentIDs` werden sie als defekt gemeldet.
- Mehrere contentIDs in einer Datei: nur die erste wird verwendet (am Zielrechner nicht vorgekommen).
- Eine andere Wurzel als `DiPManifest` oder eine Kodierung, die .NET ohne Zusatzpaket nicht kennt (etwa
  `encoding="windows-1252"`), gilt als defekte Datei (Scanfehler mit Pfad). Ob es solche Manifeste aus der Origin-Zeit
  gibt, ist am Zielrechner nicht prüfbar.
- Nennt `runtime/launcher` nur Pfade, die keine vorhandene EXE im Spielordner ergeben (auch Einträge ohne `.exe`), gilt
  die Installation als unvollständig und wird ohne Fehler ausgelassen. Am Zielrechner nicht vorgekommen.
- EA-Spiele aus dem Epic Games Store mit `__Installer` würden zusätzlich zum Epic-Eintrag als EA-Spiel erscheinen (nur
  Steam-Bibliotheken werden ausgelassen). Am Zielrechner hat keine der 7 Epic-Installationen einen `__Installer`-Ordner.
- Der Start-Befehl ist am Protokoll belegt, aber in dieser Phase bewusst nicht ausgeführt worden (kein Spielstart).
- EA-Spiele aus Steam oder Epic (Start über `link2ea://launchgame/<ID>?platform=steam`) gehören zu diesen Scannern.
- Der Anti-Cheat-Starter `EAAntiCheat.GameServiceLauncher.exe` steht in den EXEs, weil die Quelle ihn nennt. Ob er in
  Phase 3 als Spielprozess zählen oder auf die Ausschlussliste soll, ist dort zu entscheiden.
