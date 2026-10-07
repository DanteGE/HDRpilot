# Scanner: Epic Games

> Vor der Veröffentlichung bereinigt: Installationsordner des Messrechners sind durch erfundene ersetzt (etwa
> `D:\Steam-Bibliothek`, `D:\GOG-Spiele`), vollständige Spielelisten durch erfundene Beispielzeilen.

Klasse `HdrPilot.Core.Games.Scanners.EpicScanner` (`GameSource.Epic`, Name „Epic Games“), Hilfsklassen unter
`src/HdrPilot.Core/Games/Scanners/Epic/` und `…/Scanners/Common/`. Tests: `tests/HdrPilot.Tests/Scanners/EpicScannerTests.cs`
(44 Tests), Beispieldateien `tests/HdrPilot.Tests/Fixtures/Epic/`. Alle Angaben gemessen am 2026-10-07 auf dem
Zielrechner, nur lesend.

## 1. Prüfangaben aus dem Auftrag

| Angabe im Auftrag | Ergebnis | Beleg |
|---|---|---|
| Manifeste unter `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item`, JSON | **bestätigt** | 7 Dateien, Name = InstallationGuid (32 Hex-Zeichen, etwa `0123456789ABCDEF0123456789ABCDEF.item`). Daneben ein leerer Unterordner `Pending`. |
| Fehlt der Ordner: `HKCU\Software\Epic Games\EOS`, Wert `ModSdkMetadataDir` | **bestätigt** | REG_SZ `C:/ProgramData/Epic/EpicGamesLauncher/Data/Manifests` (Schrägstriche), zeigt auf denselben Ordner. Im selben Schlüssel: `ModSdkCommand` = `D:/Programmordner/Epic Games/Launcher/Portal/Binaries/Win64/EpicGamesLauncher.exe`. |
| Felder DisplayName, InstallLocation, LaunchExecutable, AppName, MainGameAppName, CatalogNamespace, CatalogItemId, bIsIncompleteInstall | **bestätigt** | Alle acht Felder in allen 7 Manifesten vorhanden (je 51–52 Felder). Typen: Zeichenketten, `bIsIncompleteInstall` ist `true`/`false`. |
| Unvollständige Installationen überspringen | **Feld bestätigt, Fall am System nicht vorhanden** | `"bIsIncompleteInstall": false` in allen 7. Regel per Test `Incomplete_install_is_skipped_without_error` abgesichert. |
| Zusatzinhalte überspringen (AppName ≠ MainGameAppName) | **Feld bestätigt, Fall am System nicht vorhanden** | Bei allen 7 gilt AppName = MainGameAppName (z. B. `"AppName": "Heather"`, `"MainGameAppName": "Heather"`); keine Zusatzinhalte installiert. Zusätzlich gibt es `MainGameCatalogNamespace`/`MainGameCatalogItemId`. Regel per Test `Dlc_manifest_with_different_MainGameAppName_is_skipped`. |
| Start `com.epicgames.launcher://apps/<CatalogNamespace>%3A<CatalogItemId>%3A<AppName>?action=launch&silent=true` | **bestätigt** (nicht gestartet) | Die vom Launcher selbst angelegten Verknüpfungen `%USERPROFILE%\Desktop\Games\*.url` enthalten für alle 7 Spiele exakt diese URI, z. B. `URL=com.epicgames.launcher://apps/b30b6d1b4dfd4dcc93b5490be5e094e5%3A22a7b503221442daa2fb16ad37b6ccbf%3AHeather?action=launch&silent=true`. Protokoll registriert: `HKCR\com.epicgames.launcher\shell\open\command` = `"D:\Programmordner\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe" %1`. |
| AppCategories prüfen (Engines, Plugins, Anwendungen ohne Spielcharakter) | **gemessen** | `["public","games","applications"]` (4 der 7 Manifeste) bzw. `["games","applications"]` (die übrigen 3). Keine Engine, kein Plugin installiert. `"applications"` steht also auch bei Spielen und taugt nicht als Ausschlusskriterium. |
| JSON mit BOM tolerieren | **kein BOM am System** | Erste Bytes aller 7 Dateien `{` (0x7B); UTF-8 ohne BOM, CRLF, Tab-Einrückung. BOM wird trotzdem toleriert (Test `Manifest_with_utf8_bom_is_read`). |

Weitere Beobachtungen, die den Code bestimmen:

- `InstallLocation` mit gemischten Trennern bei 4 von 7: `"D:\\Anwendungen\\Epic Games/AlanWake2"`.
- `LaunchExecutable` mit Unterordner und Schrägstrich: `"newRoot/UnrailedGameEpic.exe"` (Unrailed).
- `MainWindowProcessName` ist nur bei RDR2 gesetzt: `"RDR2.exe"` neben dem Starter `"PlayRDR2.exe"`.
- Das Manifest-Feld `LaunchCommand` enthält Spielargumente, keinen Befehl: `"-ini:Engine:[OnlineSubsystem]:DefaultPlatformService=EOS"` (Witchfire), `" "` (Unrailed), sonst leer.
- `HKLM\SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher`, Wert `AppDataPath` = `C:\ProgramData\Epic\EpicGamesLauncher\Data\` (nicht genutzt, siehe Entscheidungen).

## 2. Format

`<Manifests>\<InstallationGuid>.item` ist ein JSON-Objekt (UTF-8, CRLF). Genutzt werden:

| Feld | Verwendung |
|---|---|
| `AppName` | `SourceId` (stabil, z. B. `Heather`, `Crow` oder 32 Hex-Zeichen); Pflicht |
| `MainGameAppName` | Zusatzinhalt, wenn gesetzt und ≠ `AppName` |
| `DisplayName` | Name; Pflicht |
| `InstallLocation` | Installationsordner (Trenner gemischt); Pflicht |
| `LaunchExecutable` | Start-EXE relativ zu `InstallLocation` → `Executables` |
| `MainWindowProcessName`, `ProcessNames` | weitere vom Launcher genannte EXE-Dateien → `Executables` |
| `CatalogNamespace`, `CatalogItemId` | Start-URI; Pflicht |
| `bIsIncompleteInstall` | `true` → überspringen |
| `bIsExecutable`, `AppCategories` | Spielcharakter (siehe Entscheidungen) |

Zweite Quelle: `%ProgramData%\Epic\UnrealEngineLauncher\LauncherInstalled.dat`, JSON `{"InstallationList":[{InstallLocation,
NamespaceId, ItemId, ArtifactId, AppVersion, AppName}]}`; der Launcher pflegt sie unabhängig von den Manifesten.

## 3. Scan-Ergebnis am echten System

`dotnet run --project src\HdrPilot.Cli -- scan --source epic`: `launcherDetected: true`, 7 Spiele, 0 Fehler, ca. 20 ms.

Die Tabelle nannte hier die 7 Spiele des Messrechners. Vor der Veröffentlichung ist sie durch erfundene Beispielzeilen
im selben Aufbau ersetzt (Namen, IDs und Ordner frei erfunden).

| ID (AppName) | Name | InstallDir | Start-Befehl |
|---|---|---|---|
| `0123456789abcdef0123456789abcdef` | Beispielspiel Eins | `D:\Anwendungen\Epic Games\BeispielspielEins` | `com.epicgames.launcher://apps/00000000000000000000000000000001%3A00000000000000000000000000000002%3A0123456789abcdef0123456789abcdef?action=launch&silent=true` |
| `Beispiel` | Beispielspiel Zwei | `D:\Anwendungen\Epic Games\BeispielspielZwei` | `com.epicgames.launcher://apps/00000000000000000000000000000003%3A00000000000000000000000000000004%3ABeispiel?action=launch&silent=true` |
| `fedcba9876543210fedcba9876543210` | Beispielspiel Drei | `D:\Anwendungen\Epic Games\BeispielspielDrei` | `com.epicgames.launcher://apps/00000000000000000000000000000005%3A00000000000000000000000000000006%3Afedcba9876543210fedcba9876543210?action=launch&silent=true` |

Bekannte EXE-Dateien: je die `LaunchExecutable` (z. B. `…\Unrailed\newRoot\UnrailedGameEpic.exe`), bei RDR2 zusätzlich
`RDR2.exe` aus `MainWindowProcessName`.

### Vergleich mit der zweiten Quelle

| Quelle | Einträge | Abgleich |
|---|---|---|
| `LauncherInstalled.dat` | 7 | 7/7 gleich: AppName, Installationsordner (nach Vereinheitlichung der Trenner) und die aus NamespaceId/ItemId/AppName gebildete URI stimmen exakt mit dem Scan überein. Keine Einträge nur in einer der Quellen. |
| Launcher-Verknüpfungen `Desktop\Games\*.url` (dritte Quelle) | 7 | 7/7 Start-URIs zeichengleich mit dem Scan. |

**Abweichungen: keine.** Die Filterregeln (unvollständig, Zusatzinhalt, kein Spiel) haben am System nichts aussortiert,
weil kein solcher Eintrag existiert; die Zahl 7 deckt sich deshalb mit beiden Quellen.

## 4. Entscheidungen

1. **SourceId = AppName.** Stabil über Updates; InstallationGuid ändert sich bei Neuinstallation, CatalogItemId ist
   nicht eindeutig zwischen Spiel und Zusatzinhalt.
2. **Registry nur als Ersatz**, wie im Auftrag: Der Standardordner unter `CommonApplicationData` hat Vorrang. Nennt die
   Registry einen fehlenden Ordner, gilt der Launcher als erkannt (Spur vorhanden), es gibt aber weder Spiele noch Fehler.
   `AppDataPath` unter HKLM wird nicht zusätzlich gelesen: am System zeigt es auf denselben Ordner, und der Auftrag nennt
   nur `ModSdkMetadataDir`.
3. **Nur die oberste Ebene** des Manifest-Ordners: `Pending\` enthält Manifeste laufender Installationen.
4. **Zusatzinhalt:** `MainGameAppName` gesetzt und ungleich `AppName` (ohne Groß-/Kleinschreibung, eine reine
   Schreibvariante ist kein DLC). Fehlt `MainGameAppName` (ältere Formate), gilt der Eintrag als Hauptspiel.
5. **Spielcharakter:** `bIsExecutable` nicht `false` **und** `AppCategories` enthält `games` und weder `engines` noch
   `plugins`. Ohne `AppCategories` gilt der Eintrag als Spiel. Begründung: `applications` steht am System auch bei allen
   Spielen; Engines und Plugins haben keinen Spielcharakter und würden sonst HDR für den Editor schalten.
6. **Executables** nur aus Angaben des Launchers (`LaunchExecutable`, `MainWindowProcessName`, `ProcessNames`), nur
   vorhandene `.exe` unter dem Installationsordner; es wird nicht gesucht. Ein Pfad, der per `..` aus dem Ordner führt,
   wird verworfen.
7. **Start über die URI**, nicht über die EXE: Epic-Spiele brauchen Anmeldung/Ownership-Token vom Launcher (gemessen:
   `"bRequiresAuth": true` in allen 7 Manifesten, bei Dead Island 2 zusätzlich `"OwnershipToken": "true"`). Das
   Manifest-Feld `LaunchCommand` (Spielargumente) wird deshalb nicht als `LaunchArguments` übernommen; der Launcher
   setzt es selbst.
8. **Fehlerbild:** defektes JSON, falsche Kodierung, fehlende Pflichtfelder, nicht lesbare Datei, Installationsordner
   relativ oder Laufwerkswurzel → `ScanError` mit Pfad der Manifestdatei, die übrigen Spiele kommen trotzdem.
   Fehlender Spielordner und unvollständige Installation → nur Protokoll (Information), kein Fehler: das ist ein
   Zustand (Laufwerk abgezogen, Download läuft), kein Datenfehler.
9. **Strenge Dekodierung** (`Scanners/Common/ScannerJson.cs`): UTF-8 mit/ohne BOM, UTF-16 nur mit BOM. Ungültige
   Bytes werden gemeldet statt still durch `?` ersetzt, sonst landete ein verstümmelter Name in der Bibliothek.
10. **Doppelte AppName** (z. B. verwaistes zweites Manifest): der erste Eintrag nach Dateiname gilt, Protokoll statt
    Fehler.
11. **Laufwerkswurzel als Installationsordner wird abgelehnt** (`Scanners/Common/InstallPaths.cs`), weil in Phase 3
    sonst jeder Prozess des Laufwerks als Spiel gälte.
12. **Ungültige Escape-Folgen** (einzelnes UTF-16-Surrogat wie `\uD800`, nach der Prüfung ergänzt): JSON lässt sie
    syntaktisch zu, System.Text.Json wirft erst beim Dekodieren eine `InvalidOperationException`. In einem genutzten
    Feld wird daraus der Fehler „Epic-Manifest defekt: Feld "…" enthält eine ungültige Escape-Folge …“; ein nicht
    dekodierbarer Name eines ungenutzten Felds wird übergangen. Vorher brach eine einzige solche Datei den ganzen
    Epic-Scan ab (alle Spiele weg, nur „Scanner abgestürzt“).

## 5. Bekannte Grenzen

- Zusatzinhalte, unvollständige Installationen, Engines und Plugins gibt es am Zielrechner nicht. Die Regeln sind mit
  Beispielmanifesten im echten Format getestet; Kategorien echter DLC- oder Engine-Manifeste konnten nicht gemessen
  werden (`unreal-engine.item` und `dlc.item` sind nachgebaut).
- Bei Unreal-Spielen läuft das eigentliche Spiel oft als Kindprozess, den kein Manifest nennt, z. B.
  `Witchfire\Binaries\Win64\Witchfire-Win64-Shipping.exe` und `DeadIsland\Binaries\Win64\DeadIsland-Win64-Shipping.exe`.
  Diese stehen nicht in `Executables`, liegen aber unter `InstallDir` und werden in Phase 3 über den Ordner erkannt.
  Hilfsprogramme im Spielordner gehören auf die Ausschlussliste von Phase 3. Gemessen (vollständige EXE-Liste der
  Ordner): `Engine\Binaries\Win64\CrashReportClient.exe` (Witchfire, Dead Island 2),
  `Engine\Extras\Redist\en-us\UE4PrereqSetup_x64.exe` (Witchfire), `Engine\Binaries\Win64\cdb.exe`,
  `LightweightDebugger\LightweightDebugger.exe` und `Engine\Binaries\ThirdParty\7-Zip\7z*.exe` (Dead Island 2),
  bei RDR2 `Redistributables\` (`Rockstar-Games-Launcher.exe`, `Rockstar-Games-Epic.exe`, `Social-Club-Setup.exe`,
  `VulkanRT-…-Installer.exe`, `DirectX\DXSETUP.exe`, `VCRed\vc_redist.x64.exe`) und `x64\crashpad\release\crashpad_handler.exe`.
- Der Start über die URI braucht den installierten Launcher. Dass `silent=true` den Launcher ohne Fenster bzw.
  minimiert startet und wie er sich bei fehlender Anmeldung verhält, ist **nicht geprüft** (Auftrag: nichts starten);
  belegt ist nur, dass der Launcher selbst genau diese URI in seine Verknüpfungen schreibt.
- Spiele, deren Manifest gelöscht wurde (etwa nach Neuinstallation von Windows ohne „Verify“), findet der Scanner nicht;
  sie lassen sich manuell eintragen.
- Ein Manifest, dessen `InstallLocation` per `..` auf einen übergeordneten Ordner zeigt (etwa die ganze Bibliothek
  `…\Epic Games`), wird nicht als fehlerhaft erkannt; nur Laufwerkswurzeln werden abgelehnt. Am Zielrechner kommt das
  nicht vor.

## 6. Unabhängige Nachprüfung (2026-10-07)

Zweiter Durchgang durch einen getrennten Prüfer, nur lesend am Zielrechner:

- Alle Angaben aus Abschnitt 1 erneut gemessen und **bestätigt**: 7 Manifeste (erstes Byte `0x7B`, kein BOM), leerer
  Unterordner `Pending`, `ModSdkMetadataDir` als REG_SZ mit Schrägstrichen, `AppDataPath` in der 32-Bit-Sicht,
  AppName = MainGameAppName und `bIsIncompleteInstall` = false bei allen 7, Kategorien wie angegeben, 51 bzw. 52 Felder,
  4 von 7 `InstallLocation` mit gemischten Trennern, `HKCR\com.epicgames.launcher\shell\open\command` wie angegeben.
- Scan selbst ausgeführt: 7 Spiele, 0 Fehler, Tabelle in Abschnitt 3 zeichengleich. `LauncherInstalled.dat` (7 Einträge)
  und die 7 Launcher-Verknüpfungen `Desktop\Games\*.url` stimmen bei AppName, Ordner und Start-URI überein. Alle
  Spielordner, `LaunchExecutable`-Dateien und `RDR2.exe` existieren; die Shipping-EXEs liegen unter `InstallDir`.
- **Gefundener Fehler, behoben:** Eine Manifestdatei mit einzelnem Surrogat (`\uD800`) in einem Wert oder Namen ließ
  `Scan` mit `InvalidOperationException` abbrechen (Entscheidung 12). Tests:
  `Broken_lone_surrogate_escape_in_value_is_reported_and_others_delivered`,
  `Broken_lone_surrogate_escape_in_unused_property_name_is_tolerated` (beide schlugen ohne die Korrektur fehl).
- **Schwacher Test verschärft:** `Plugin_and_non_executable_manifests_are_skipped` nutzte `["plugins","engine"]` ohne
  `games`; die Plugin-Regel hätte man entfernen können, ohne dass ein Test fehlschlägt. Jetzt `["plugins","games"]`
  (Mutationsprobe: ohne `plugins` in der Ausschlussliste schlägt der Test fehl).
- Zusätzlich abgesichert: falscher Registry-Werttyp für `ModSdkMetadataDir` (DWORD, REG_BINARY), Pfade mit Umlauten,
  `&`, `#`, Klammern, Schrägstrichen, abschließendem Trenner und fehlendem Laufwerk.
