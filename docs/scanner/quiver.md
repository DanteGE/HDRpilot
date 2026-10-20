# Scanner: Quiver Launcher (`QuiverScanner`, Quelle `Quiver`)

Stand: 2026-10-20, Zielrechner (Windows 11 Build 26300, Quiver Launcher 3.5.1 portabel, Velopack 1.2.0, ein Spiel:
„Black“). Alle Angaben unten sind am echten System nur lesend geprüft (Dateinamen, kleine Textdateien geteilt geöffnet,
die gebaute Kommandozeile `scan --source Quiver` mit leerer eigener Ablage); nichts wurde gestartet oder geschrieben,
weder Quiver noch das Spiel. Quiver ist ein portabler Launcher für Programme aus GitHub-Releases (MIT); HDRpilot nutzt
keinen Code von Quiver, sondern liest nur dessen Dateien. Nachgebildet sind nur Namen, Adressen und Dateiformate; die
Prüfung des Bildkopfs und die Regel für die verkleinerten Kopien sind nach der Prüfung vom 2026-10-20 eigenständig neu
geschrieben (vorher zu nah an Quivers `LauncherIconCache`/`LauncherArtworkLoader`). Entscheidung: E91.

Code: `src/HdrPilot.Core/Games/Scanners/QuiverScanner.cs`, Hilfsklassen unter
`src/HdrPilot.Core/Games/Scanners/Quiver/` (`QuiverRoot`, `QuiverRootFinder`, `QuiverLibrary`, `QuiverExecutables`,
`QuiverArtwork`, `QuiverFolderLearner`).
Tests: `tests/HdrPilot.Tests/Scanners/QuiverScannerTests.cs`, `tests/HdrPilot.Tests/Games/QuiverFolderLearnerTests.cs`,
`tests/HdrPilot.Tests/Games/QuiverIntegrationTests.cs`, `tests/HdrPilot.Tests/Config/UnknownSourceConfigTests.cs`,
`tests/HdrPilot.App.Tests/Library/QuiverLibraryTests.cs`; Beispieldateien (erfundene Pfade) unter
`tests/HdrPilot.Tests/Fixtures/Quiver/`. Gates: `tools\gates\phase12-quiver-scan.ps1` (echtes System, nur lesend) und
`tools\gates\phase12-quiver-ui.ps1` (Testinstanz).

## 1. Aufbau eines Quiver-Ordners

Gemessen am Zielrechner (`%USERPROFILE%\Downloads\QuiverLauncher-win-Portable`, 304 Einträge):

| Eintrag | Bedeutung | Gelesen |
|---|---|---|
| `current\sq.version` | NuSpec-XML von Velopack, `<id>QuiverLauncher</id>`, `<version>3.5.1</version>` | ja, Kennzeichen (höchstens 64 KB, ohne DTD) |
| `current\QuiverLauncher.exe`, `QuiverLauncher.exe`, `Update.exe`, `.portable`, `packages\` | Programm, Starter und Updater von Velopack | nur der Name (Ordner lernen) |
| `apps.json` | Bibliothek: `apps[]` mit `name`, `folderName`, `installPath`, `appIconUrl`, `project`, `customDisplayName`, … | ja |
| `settings.json` | Einstellungen, verwendet nur `AppsPath` (eigener Apps-Ordner, bei dir leer) | ja |
| `Apps\<folderName>\` | Spielordner; bei „Black“ `BLACK.exe`, `LastPlayed.txt`, `quiver-launch.log`, `hdd\` (Abbild 7,8 GB) | nur Namen, `LastPlayed.txt` |
| `Cache\Images\<MD5>` | gemeinsamer Bildzwischenspeicher ohne Endung (meist WebP) | nur Kopf (12 Byte) |
| `Cache\CustomIcons\`, `Cache\Icons\` | eigenes Cover des Nutzers, älterer Speicher je Spiel (am Zielrechner leer) | nur Kopf |
| `apps.json.lock`, `Backups\`, `launch-debug.log`, `Cache\catalog-*.json` | Sperre, Sicherungen, Protokoll, Katalog | nein |

`settings.json` enthält auch Zugangsschlüssel (`GitHubApiToken` u. a.); gelesen wird nur `AppsPath`, die Datei wird nie
protokolliert. `CustomAppListLocation` ist laut Oberfläche von Quiver eine zusätzliche Liste für den App-Katalog
(„A JSON file or URL … in the apps.json format“), nicht die Bibliothek; HDRpilot liest sie nicht.

Die alte Struktur bis 2.4.x (laut MIGRATING.md von Quiver) hat `Quiver.exe`, `apps.json` und `Apps\` im selben Ordner.
Sie wird ebenfalls erkannt (Tests), am Zielrechner gibt es sie nicht.

`quiver-launch.log` im Spielordner belegt, wie Quiver startet: `FileName=…\BLACK.exe`,
`WorkingDirectory=…\Apps\Black-BlackXBERecomp`, `UseShellExecute=True`, `Arguments=` leer, `selectedExecutableFile=(none)`,
ein Kandidat. Genau so startet HDRpilot.

## 2. Ordnersuche (`QuiverRootFinder`)

Quiver ist portabel, hinterlässt keinen Registry-Eintrag und liegt meist dort, wo es entpackt wurde. Gesucht wird, die
günstigsten Quellen zuerst; jeder Ordner wird nur an seinem Kennzeichen erkannt (`current\sq.version` mit der Kennung
`QuiverLauncher` und daneben `apps.json` oder `settings.json`, bzw. die alte Struktur), also mit wenigen Dateiabfragen:

1. Gelernte Ordner (`settings.quiverFolders` in `config.json`, siehe 7) und `ScanOptions.QuiverFolders`.
2. Das Protokoll von Velopack `%LOCALAPPDATA%\velopack\velopack_QuiverLauncher.log`: nur die letzten 256 KB, geteilt
   gelesen (Quiver schreibt weiter hinein); jeder Laufwerkspfad und seine zwei übergeordneten Ordner, die jüngsten
   Zeilen zuerst, höchstens 32. Je Zeile zuerst Pfade in einfachen Anführungszeichen (bis zum letzten `'` der Zeile),
   dann die übrigen bis zum Zeilenende, bis vor ein `"` (in doppelten Anführungszeichen verdoppelt Velopack die
   Backslashes) oder bis vor den nächsten Laufwerkspfad; so bleibt ein `'` im Pfad erhalten (`C:\Users\O'Brien\…`), und
   zwei Pfade einer Zeile werden nicht zu einem. Andere Velopack-Programme haben ihr eigenes Protokoll (am Zielrechner
   `velopack_SCSKiller.App.log`, `velopack.log`); sie werden nicht gelesen.
3. Die Suchordner des Nutzers und ihre direkten Unterordner (je höchstens 400).
4. `%LOCALAPPDATA%\QuiverLauncher` (Standardordner, falls Quiver je mit einem Velopack-Setup kommt) und
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuiverLauncher` (`InstallLocation`).
5. Nur wenn 1–4 nichts finden: Downloads (bekannter Ordner aus `User Shell Folders`, sonst `%USERPROFILE%\Downloads`),
   Desktop, Dokumente, Benutzerordner und die festen Laufwerke, je bis zwei Ebenen tief, höchstens 1500 Ordner zusammen,
   ohne Windows-, Programm- und Datenordner (`Windows`, `Program Files*`, `ProgramData`, `AppData`, `steamapps`,
   `node_modules`, `$…`, `.…` u. a.); die Suche endet beim ersten Treffer. Jeder Ordner wird höchstens einmal geprüft
   und einmal aufgelistet. Sie bleibt auf festen Laufwerken (ein auf eine Netzfreigabe oder einen Wechseldatenträger
   umgeleiteter Ordner fällt heraus) und betritt keinen verknüpften Ordner (Junction, symbolische Verknüpfung;
   `IFileSystem.EnumerateDirectoriesWithoutLinks`), damit sie nie über eine Verknüpfung auf eine Netzfreigabe gerät und
   dort hängt. Gelernte Ordner und Suchordner gelten weiter so, wie sie eingetragen sind.

Die MuiCache (Liste ausgeführter Programme) wird bewusst nicht gelesen (Entscheidung des Nutzers: Nutzungsverlauf).

Messung am Zielrechner (eigenes Messprogramm mit der echten Klasse, nur lesend): Fund über das Velopack-Protokoll 30 ms;
ohne Protokoll Fund über die Suche in Downloads 24 ms; ohne jeden Quiver-Ordner 576 geprüfte und 178 aufgelistete Ordner,
134 ms beim ersten und 72 ms beim zweiten Lauf. In der Shell der Claude-App fand das Messprogramm den Ordner über das
Velopack-Protokoll; außerhalb ihres Containers (E84) liegt das Protokoll ebenfalls vor (welche Quelle dort griff, gibt
die Kommandozeile nicht aus, das Ergebnis ist dasselbe).

## 3. Bibliothek (`QuiverLibrary`, `QuiverScanner`)

- `apps.json` und `settings.json` werden geteilt gelesen (`FileShare.ReadWrite | Delete`, `IFileSystem.ReadShared`),
  höchstens 16 MB. Fehlt `apps.json`, hat dieser Ordner noch keine Bibliothek (kein Fehler).
- Fehlt `apps` oder ist es keine Liste: „Quiver-Bibliothek defekt“ mit Pfad. Ein einzelner unbrauchbarer Eintrag
  („Quiver-Eintrag n unbrauchbar“) kostet nur diesen Eintrag.
- Apps-Ordner: `AppsPath` aus `settings.json`, wenn absolut, auch eine Laufwerkswurzel wie `G:\` (Quiver nimmt den Wert
  unverändert; die Spiele liegen dann in `G:\<folderName>`, und dieser Spielordner ist nie selbst eine Wurzel); sonst
  `<Quiver>\Apps` (defekte Datei, relativer oder ungültiger Pfad: Fehler mit Pfad, es gilt der Standardordner).
- Spielordner: `installPath`, wenn gesetzt (absolut und keine Laufwerkswurzel, sonst Fehler), sonst `<Apps>\<folderName>`.
- Schlüssel `Quiver:<folderName>`. Abgelehnt (Fehler mit Pfad) werden leere Namen, ein Name, der nur `.` ist, `..`
  irgendwo im Namen (`Spiel..Teil`; ein einzelner Punkt im Namen ist erlaubt), `\`, `/`, `:`, im Dateinamen ungültige
  Zeichen und ein Punkt oder Leerzeichen am Ende; Leerraum wird nicht abgeschnitten. So kann kein Eintrag aus dem
  Apps-Ordner hinauszeigen.
- Name: `customDisplayName`, sonst `name`, sonst `project`, sonst `folderName`. Quiver kennt daneben
  `UserAppDisplayNames` in `settings.json` (laut seiner Oberfläche eine ältere Ablage, die es übernimmt); das Feld ist am
  Zielrechner leer, und welchen Schlüssel es nutzt, ist nicht belegt. HDRpilot liest es deshalb nicht.
- Installiert wie in Quiver: Spielordner da, keine `install-incomplete.txt`, mindestens eine EXE, BAT oder CMD irgendwo
  im Spielordner (Quiver sucht ohne Grenze; HDRpilot bis 16 Ebenen und höchstens 3000 Ordner, siehe 4). Sonst
  übersprungen (Protokoll), ohne Fehler; ein schon bekanntes Spiel gilt dann als „Nicht installiert“ (E90). Endet die
  Suche an ihrer Grenze, gilt das Spiel als installiert (ohne Start-Befehl), nie als „Nicht installiert“: Das gäbe
  „Einstellungen entfernen…“ frei.
- Doppelt in `apps.json`: der erste Eintrag gilt, der Rest steht im Protokoll.
- Doppelt in zwei Quiver-Ordnern: Quiver beschreibt in MIGRATING.md den Umzug von 2.4.x auf 3.x so, dass der alte
  Ordner liegen bleibt, bis der neue passt. Es gilt deshalb der Ordner im Aufbau ab 3.0 (Velopack) vor der alten
  Struktur, dann der mit dem späteren „Zuletzt gespielt“, dann der zuerst gefundene (gelernte Ordner zuerst); der Rest
  steht im Protokoll.
- Doppelt mit einem eigenen Eintrag: Wurde ein Spiel früher von Hand eingetragen (Ordner oder EXE im Quiver-Spielordner),
  steht es zweimal in der Bibliothek. Beide Einträge nennen in den Details den anderen (`Details_DuplicateOfManual`,
  `Details_DuplicateOfQuiver`); entfernt wird nichts von selbst.

## 4. Start-EXE (`QuiverExecutables`)

1. `selected_executable.txt` im Spielordner (die Wahl des Nutzers in Quiver), nur wenn sie auf eine vorhandene Datei im
   Spielordner zeigt (relativ oder absolut). Zeigt sie hinaus, fehlt die Datei oder ist sie unlesbar, zählt sie nicht.
   Ist die gewählte Datei eine BAT/CMD, gibt es keinen Start-Befehl: HDRpilot startet nur EXE-Dateien. Die Wahl wird
   vor jeder Suche gelesen und gilt, wie viele Dateien der Ordner auch hat.
2. Sonst der einzige Kandidat oben im Spielordner. Wie in Quiver zählen oben EXE, BAT und CMD: Ist der einzige Kandidat
   ein Startskript (etwa `launch.bat`, das Quiver startet), gibt es keinen Start-Befehl, statt die EXE am Skript vorbei
   zu starten (Argumente, Umgebung und Arbeitsordner des Skripts fehlten sonst). Eine EXE und ein Skript oben sind zwei
   Kandidaten.
3. Gibt es oben keinen, der einzige bis drei Ebenen tief (ebenso mit Skripten).

Hilfsprogramme (`HelperExecutables`: Absturzmelder, Installer, Laufzeitpakete, Anti-Cheat-Installer …) zählen nicht.
Gesucht wird Ebene für Ebene nur nach Namen: verknüpfte Ordner (Junction, symbolische Verknüpfung) und Ordner von
Wine/Proton (`.wine-prefix`, `.steam-compat-data`, `dosdevices`, `drive_c`) betritt die Suche gar nicht erst, andere
Dateien als EXE, BAT und CMD kosten nichts (höchstens 256 Kandidaten, höchstens 3000 aufgelistete Ordner). Für
„installiert“ sucht sie weiter bis 16 Ebenen, aber nur bis zur ersten Ebene mit einem Kandidaten und nur, wenn bis drei
Ebenen keiner liegt; eine so tiefe EXE gibt keinen Start-Befehl. Das Abbild von „Black“ (7,8 GB) wird nie geöffnet.
Bleiben mehrere Kandidaten, gibt es keinen Start-Befehl (E61): „Mit HDR starten“ ist gesperrt, die Profilzeile sagt
„Quiver · mehrere EXE“, und die Details sagen, warum (mehrere Kandidaten, sehr tiefe EXE oder Startskript) und dass das
Spiel einmal in Quiver gestartet und die EXE dort gewählt werden soll, dann „Neu einlesen“ (`Launch_NoCommandQuiver`).
Bei einem Spiel, das nur über ein Skript startet, hilft das nicht; dann bleibt der Start aus Quiver. Die Erkennung beim
Spielen wirkt trotzdem, weil jedes Programm im Spielordner zählt.

Gestartet wird die EXE direkt mit ihrem Ordner als Arbeitsordner und ohne Argumente, wie Quiver selbst (Abschnitt 1),
nicht über `QuiverLauncher.exe --run`: Das öffnet laut Quelltext von Quiver ein Konsolenfenster, sucht online nach
Updates und schreibt in Quivers Einstellungen (Entscheidung des Nutzers).

Per Windows Installer installierte Spiele: Liegt im Spielordner `windows-installer.json` (`Phase`, `ExecutablePath`),
zählt nur `Linked` mit vorhandener, absolut angegebener EXE. Dann ist der Ordner dieser EXE der Installationsordner (dort
läuft das Spiel; Laufwerkswurzeln abgelehnt) und die EXE der Start-Befehl; die Details sagen das
(`Details_QuiverWindowsInstaller`). `Installing` und `NeedsExecutable` gelten als nicht installiert, eine unbekannte Phase
oder defekte Datei ist ein Fehler mit Pfad. Belegt nur mit Beispieldateien; am Zielrechner gibt es kein solches Spiel.

## 5. Zuletzt gespielt

`LastPlayed.txt` (Ortszeit im Format `yyyy-MM-dd HH:mm:ss`; mit `:`, `.` als Zeittrenner oder `T`) im Ordner der EXE
und im Spielordner; die spätere zählt, Unlesbares nicht. Quiver schreibt den Wert im Kalender der Kultur
(`DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")`): unter Thai steht dort das Jahr 2569, unter Umm al-Qura 1448. Werte vor
2000 oder mehr als einen Tag in der Zukunft zählen deshalb nicht; ein Jahr 2569 bliebe sonst für immer „zuletzt
gespielt“, weil der Abgleich der Bibliothek den späteren Wert behält. HDRpilot übernimmt den Wert wie bei Steam als
„Zuletzt gespielt“, solange es selbst keinen späteren Start gesehen hat.

## 6. Bilder (`QuiverArtwork`, `ArtworkLocator`)

Nur lokal aus `<Quiver>\Cache`, nur Dateien mit Bildkopf (PNG, JPEG, GIF, BMP, ICO, WebP), höchstens vier Kandidaten:

1. eigenes Cover des Nutzers `CustomIcons\<folderName>_custom.<png|jpg|jpeg|webp|bmp|gif|ico>`;
2. gemeinsamer Zwischenspeicher `Images\<MD5>`: MD5 der Bildadresse (ASCII), Hex in Großbuchstaben. Bilder von
   SteamGridDB (`https://cdn2.steamgriddb.com/grid|hero/…`) lädt Quiver als verkleinerte Kopie über
   `https://quiverlauncher.com/cdn-cgi/image/width=<w>,quality=70,format=auto,fit=scale-down/<Adresse>` (Breiten 960, 720,
   480, 360, 240; als Titelbild 1920 mit Qualität 85); geprüft werden diese Adressen und die Adresse selbst, große
   zuerst;
3. älterer Speicher je Spiel `Icons\<folderName>_<erste 16 Hex-Zeichen von SHA-256 der Adresse><Endung>`.

Nachgerechnet: `appIconUrl` von „Black“ ist `https://cdn2.steamgriddb.com/grid/df6f08b0faeb3094ca257cafd49dbcd9.png`;
die Kopie mit Breite 480 heißt `Cache\Images\8C8FC6677FB84678288E4615E4E470A5` (WebP 480×480, rund 38 KB, dekodiert
mit dem WebP-Decoder von Windows). Die Kandidaten stehen je Spiel in `config.json` (`coverCandidates`), damit die
Bibliothek ohne neues Einlesen weiß, wo sie suchen soll; dekodiert wird erst in der App. Titelbild und Logo hat Quiver
nicht: HDRpilot erzeugt sie wie bei Spielen anderer Launcher ohne solche Bilder (README, „Spielbilder“), optional füllt
SteamGridDB sie. Das Symbol kommt aus der Spiel-EXE (`BLACK.exe` hat keins, daher das Monogramm). Ein Quiver-Logo und
Programmsymbole von Quiver werden nie verwendet. Neue Bilder von Quiver erscheinen nach dem nächsten Einlesen; den
Cache-Ordner beobachtet HDRpilot nicht (er liegt oft in Downloads, wo ein Beobachter viele fremde Änderungen sähe).

Quadratische Cover: Die Kartenbilder von SteamGridDB, die Quiver ablegt, sind meist quadratisch (bei „Black“ 480 × 480).
Als Cover im Hochformat zugeschnitten fehlte links und rechts je ein Sechstel, beim Schriftzug „BLACK“ etwa das „B“.
Ein Cover aus dem Cache von Quiver mit einem Verhältnis von Höhe zu Breite unter 1,2 setzt HDRpilot
deshalb wie das Kachelbild eines Xbox-Spiels ganz oben in ein erzeugtes Cover im Hochformat (600 × 900, darunter der
Name; `art\gen\…-cover.jpg`, im Verzeichnis `cover-square`) und rechts der Mitte in das erzeugte Titelbild; die Farben
kommen aus diesem Bild. Die Herkunft bleibt „Quiver-Cache“, die Zeile unter den Bildern sagt „Lokal aus dem
Quiver-Cache: quadratisches Bild · Titelbild und Cover daraus erzeugt“. Ein Cover im Hochformat bleibt, wie es ist
(„Cover lokal aus dem Quiver-Cache · Titelbild aus dem Symbol erzeugt“). Das Symbol bleibt das der EXE bzw. das
Monogramm (E91).

Gelesen werden die Bilder in der App nur so lange, bis ihr Inhalt im Speicher liegt (`ArtworkImaging.OpenRead`); die
Datei ist zu, bevor dekodiert wird (siehe 10).

## 7. Ordner lernen (`QuiverFolderLearner`)

Die Prozessüberwachung meldet neu gestartete Prozesse. Heißt einer `QuiverLauncher.exe` (`…\current\` oder der Starter
darüber) oder `Quiver.exe` und trägt der Ordner darüber das Kennzeichen von Quiver (Bibliothek darf noch fehlen), trägt
HDRpilot ihn in `settings.quiverFolders` ein und liest 5 s später neu ein (App und `hdrpilot watch`). Jeder EXE-Pfad wird
nur einmal geprüft (E31), ein schon bekannter Ordner schreibt nichts. Ohne gelernten Ordner fehlt das Feld in
`config.json` ganz, ältere Versionen sehen es nicht. Die Einstellung hat keine Oberfläche; einen verschobenen Ordner
lernt HDRpilot beim nächsten Start von Quiver neu (der alte bleibt in der Liste und wird übersprungen, solange er kein
Quiver-Ordner ist).

## 8. Ergebnis am echten System

`phase12-quiver-scan.ps1` (2026-10-20): `hdrpilot scan --source Quiver --config-dir <leerer Temp-Ordner>` in der Shell der
Claude-App und außerhalb ihres Containers (WMI, E84), beide Male Exit 0, Scanner 64 ms, keine Fehler, ein Spiel:

| Schlüssel | Name | Start-EXE | Arbeitsordner | Zuletzt gespielt | Cover |
|---|---|---|---|---|---|
| `Quiver:Black-BlackXBERecomp` | Black | `<Quiver>\Apps\Black-BlackXBERecomp\BLACK.exe` | Spielordner, ohne Argumente | Ortszeit aus `LastPlayed.txt` (+02:00) | `<Quiver>\Cache\Images\8C8FC6677FB84678288E4615E4E470A5` |

Die Dateiliste des Quiver-Ordners (304 Einträge, Größen, Änderungszeiten, Attribute) war vorher und nachher gleich, die
eigene Ablage blieb leer. Zweite Quelle: `quiver-launch.log` (Abschnitt 1) nennt dieselbe EXE, denselben Arbeitsordner
und keine Argumente.

## 9. Entscheidungen

- **Eigene Quelle `GameSource.Quiver`** am Ende der Aufzählung (Entscheidung des Nutzers); Farbe Pink
  (`Hp.Brush.Source.Quiver`, dunkel `#F472B6`, hell `#D0408C`), Bezeichnung „Quiver“.
- **Start direkt über die EXE** statt `--run` (siehe 4).
- **Kein PCGamingWiki-Abgleich** für Quiver-Spiele (`HdrListSync.Skips`): Der Name träfe meist das Originalspiel (bei
  „Black“ das Spiel von 2006), und so gehen ihre Namen nicht ans Netz. Sie stehen als „unbekannt“ und zählen im Stand der
  HDR-Liste nicht mit.
- **Keine Frühwarnung und kein Wrapper**: Quiver schreibt kein auswertbares Startereignis vor dem Spielprozess (sein
  `quiver-launch.log` entsteht beim Start selbst), und eine Startoption wie bei Steam gibt es nicht. Beim Start aus Quiver
  schaltet die normale Erkennung über den Spielordner (bis zu ein Abfrageintervall nach dem Start).
- **Kein Speichern gefundener Ordner beim Einlesen**: Nur das Lernen aus der Prozessüberwachung schreibt
  `quiverFolders`. Das Einlesen bleibt dadurch rein lesend (auch `hdrpilot scan --config-dir`).
- **Vorschläge aus Suchordnern** übergehen den Quiver-Ordner und alles darin (`SearchFolderCandidateFinder`): Quivers
  eigene EXE und die Spiele dort wären sonst doppelt.
- **Unbekannte Quellen in `config.json`** (E91, G12.3): `AppConfigConverter` behält Spiele mit einer Quelle, die diese
  Version nicht kennt, unverändert in der Datei (beim Schreiben wieder angehängt), statt die ganze Datei als defekt zu
  sichern. Auch ihre eigenen Bilder und Logos bleiben: Das Aufräumen der Bilder nach dem Schließen zählt ihre Schlüssel
  mit (`AppConfig.AllGameKeys`). HDRpilot 1.4.0 hat das noch nicht.
- **Startskripte** (BAT, CMD) startet HDRpilot nie, und es startet nie eine andere Datei als Quiver: Startet Quiver ein
  Skript, gibt es keinen Start-Befehl (siehe 4).

## 10. Grenzen

- Ordner außerhalb der durchsuchten Orte findet HDRpilot erst, wenn Quiver einmal läuft, während HDRpilot läuft, oder
  ein Suchordner gesetzt ist.
- Das Format der Dateien von Quiver und das Protokoll von Velopack sind keine offizielle Schnittstelle. Quiver
  erscheint häufig neu (3.5.1 vom 15.10.2026); HDRpilot liest tolerant, meldet Unlesbares als Fehler des Scanners.
- Ein Schreibvorgang von Quiver genau während des Lesens kann eine halbe Datei liefern; das ergibt einmal „defekt“, das
  nächste Einlesen ist wieder richtig.
- Geteilt lesen heißt nicht ganz ohne Einfluss: Solange HDRpilot eine Datei offen hat (auch mit Freigabe zum Lesen,
  Schreiben und Löschen), scheitert das Ersetzen dieser Datei durch Verschieben einer anderen darüber (`MoveFileEx`,
  .NET `File.Move(…, overwrite: true)`) mit „Zugriff verweigert“ (Fehler 5, am Zielrechner nachgemessen; `ReplaceFile`
  und gewöhnliches Schreiben gehen). So ersetzt Quiver `windows-installer.json` (`WindowsInstallerService.WriteReceipt`,
  ohne Wiederholung) und Bilder im älteren Speicher `Cache\Icons` (`LauncherIconCache`). HDRpilot hält diese Dateien
  deshalb nur so lange offen, wie das Lesen dauert: höchstens 4 KB bzw. 12 Byte beim Einlesen, das ganze Bild beim
  Dekodieren in der App (dann zu, bevor dekodiert wird), jeweils unter einer Millisekunde. Trifft Quiver genau diesen
  Moment, scheitert dieser eine Schritt von Quiver einmal (Verknüpfung des Windows Installers bzw. Auffrischen eines
  Bildes); `apps.json` und `settings.json` (Quiver nutzt `ReplaceFile`), `LastPlayed.txt` und
  `selected_executable.txt` (gewöhnlich geschrieben) betrifft das nicht.
- WebP-Cover brauchen die WebP-Erweiterung von Windows; ohne sie erzeugt HDRpilot ein Cover.
- Spiele aus dem Windows Installer und die alte Struktur 2.4.x sind nur mit Beispieldateien geprüft.
- Livetest (HDR beim Start von „Black“ aus Quiver) nur mit dem Nutzer (G12.7).
