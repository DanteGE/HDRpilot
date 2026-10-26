# Plan: Die zwei Wünsche vom 18.10.2026

**Zu deiner Frage nach den Punkten von gestern:** Sie sind festgehalten, in `docs/stand.md` auf phase5/release (8fd62eb)
im Abschnitt „Neue Wünsche des Nutzers (2026-10-18), noch nicht begonnen“. Es sind zwei Punkte:
1. Deinstallierte Spiele
2. Quiver Launcher

Vereinbart war, sie erst auf dein Zeichen anzugehen, und zwar nach der Werkzeugleiste. Inzwischen sind beide recherchiert, nur lesend. Umgesetzt ist noch nichts.

**Reihenfolge:**
1. Werkzeugleiste abschließen und mergen (b2ea035)
2. Wunsch 1 (Phase 11, E90)
3. Wunsch 2 (Phase 12, E91)

Beide setzen auf feature/library-toolbar auf, weil Kapsel und Zähler dort neu sind. Die Gates von Phase 10 (G10.1–G10.11) sind in GATES.md noch offen.

## Prüfvermerk

Nachgeprüft habe ich in `.worktrees/s12-bar` (b2ea035), an den Kopien der echten Ablage und am Quiver-Ordner, alles nur lesend.

**Bestätigt:**
- `IsInstalled` ist das einzige Merkmal für „deinstalliert“ (`AppConfig.cs:173`). Gesetzt wird es nur in `LibraryMerger.cs:68-98`; ein abgestürzter Scanner markiert nichts.
- Nicht installierte Spiele mit „HDR automatisch“ oder Auto HDR werden weiter zugeordnet (`SessionManager.cs:1189`, E38).
- Starten ist gesperrt (`GameDetailsViewModel.cs:64`), Verknüpfungen lassen sich weiter anlegen (Z. 65-66).
- Entfernen gibt es nur für manuelle Spiele (Z. 68, 758-773; `LibraryViewModel.cs:897-907`; Knopf `MainWindow.xaml:1897`).
- Im Kontextmenü (`MainWindow.xaml:118-184`) fehlt ein Eintrag zum Entfernen.
- Nach dem Entfernen setzt `SyncFromConfig` die Auswahl auf null (`LibraryViewModel.cs` ab Z. 602). Zurzeit bleibt der Titelbereich dann leer.
- Bilder werden beim Schließen aufgeräumt (`ArtworkService.Resolve.cs:735`), die HDR-Liste beim nächsten Abgleich (`HdrListSync.cs:368`).
- Die .ico für Verknüpfungen wird nie gelöscht (`CustomIconStore.cs:72`).
- `ConfirmDialog.xaml` hat kein Häkchen. `ShortcutService` kann .lnk schon lesen (Z. 111).
- Echte Ablage, Kopie vom 19.10.: 656 Spiele, davon 5 nicht installiert, alle von Steam:
  - The Outlast Trials, mit „HDR automatisch“ an
  - VEIN Demo
  - ROLLA
  - Boltgun 2 Demo
  - Antivirus Survivors Demo
- Quiver:
  - `apps.json`: „Black“, `folderName` Black-BlackXBERecomp, `installPath` null, `AppsPath` leer.
  - `current\sq.version`: Id QuiverLauncher, Version 3.5.1.
  - `quiver-launch.log`: Quiver startet `BLACK.exe` direkt, ohne Argumente, mit dem Spielordner als Arbeitsordner.
  - Der Cache-Name `8C8FC6677FB84678288E4615E4E470A5` stimmt (MD5 nachgerechnet). Die Datei ist WebP mit rund 38 KB.
  - `install-incomplete.txt` und `selected_executable.txt` kommen im Quelltext vor.
  - Die README nennt MIT und einen Fork von GithubLauncher.

**Korrekturen an den Berichten:**
1. **CLI-Befehl:** Einen Befehl `games scan` gibt es nicht.
   - Einlesen und übernehmen heißt `hdrpilot games import` (`CliApp.Games.cs:51-57`).
   - Nur lesen, ohne Schreiben: `hdrpilot scan --source …` (`CliApp.cs:56, 84-86`).
   - Der Abgleich am echten System für Quiver muss deshalb `hdrpilot scan --source Quiver` sein.
2. **icon-cache:** `IconChoiceCache.Remove` hat doch einen Aufrufer (`GameIconService.cs:429`), aber nur, wenn der Ordner fehlt und kein gutes Symbol da ist. Beim Entfernen eines Spiels ruft ihn niemand auf, und die echte `icon-cache.json` enthält noch alle 5.
3. **Steam-Updates:** Ein ausstehendes Steam-Update (StateFlags 6) zählt als installiert (`SteamScanner.cs:21`). „Nicht installiert“ entsteht nur in Update-Zuständen ohne Bit 4.
4. **Nummer der Entscheidung:** Beide Berichte wollten E90 vergeben. Deshalb Wunsch 1 = E90, Quiver = E91.
5. **Neues Risiko:**
   - Eine Konfiguration mit `"source": "Quiver"` hält jede ältere Version für defekt (`JsonStringEnumConverter`, `AppConfig.cs:213`; `ConfigStore.cs:42-47`).
   - Die ältere Version sichert sie dann als „defekt“ und startet mit leerer Bibliothek.
   - Der Setup verhindert einen Downgrade, außer mit `--allow-downgrade`.

---

## Wunsch 1: Nicht installierte Spiele

### So sieht es für dich aus
- **Rechtsklick** auf ein nicht installiertes Spiel zeigt „Einstellungen entfernen…“. Ganz unten im Detailbereich gibt es einen roten Knopf mit demselben Namen.
- **Rückfrage** vor dem Entfernen:
  - Sie nennt, was verloren geht, etwa „HDR automatisch ist an“ oder „2 gemerkte EXE-Dateien“.
  - Sie sagt, dass sich am Spiel und am Launcher nichts ändert.
  - Fehlt das Laufwerk, warnt sie: „F: ist nicht verbunden, vielleicht noch installiert“.
  - Der Fokus liegt auf „Abbrechen“.
- **Neue Karte** unter Einstellungen › Allgemein › „Bibliothek“:
  - Schalter „Nicht installierte Spiele ausblenden“, standardmäßig aus.
  - Daneben die Anzeige „zurzeit 5“.
  - „HDR automatisch“ wirkt bei ausgeblendeten Spielen weiter.
- **Wiederinstallation:** Ein entferntes Spiel erscheint beim nächsten Einlesen als neu, „HDR automatisch“ ist dann aus.
- **Manuelle Spiele** behalten ihr „Aus der Bibliothek entfernen“.

### Umsetzung
**Core**
- `AppConfig.cs`: neues Feld `Settings.HideUninstalledGames` nach Z. 115, Standard false. Ältere Versionen ignorieren es, weil unbekannte Felder erlaubt sind.
- Neu `Config/LibraryCleanup.cs` mit `RemoveNotInstalled(config, key)`:
  - Es löscht nur, wenn das Spiel `!IsInstalled` und nicht manuell ist.
  - Geprüft wird erst im Update-Lambda. So geht nichts verloren, wenn ein Einlesen das Spiel dazwischen wiederfindet.

**App**
- `GameDetailsViewModel.cs`: `RemoveSettingsCommand` neben `RemoveGameCommand` (Z. 68). Ausführbar nur bei `!IsInstalled && !IsManual && !IsRunning`. Der Text der Rückfrage wird je Spiel zusammengesetzt.
- `LibraryViewModel.cs`:
  - `RemoveSettingsAsync` neben `RemoveGameAsync` (Z. 897). Es entfernt den Eintrag, die eigene PNG, `Icons.Forget` und den icon-cache-Eintrag, auf Wunsch auch die .lnk.
  - Danach wird der Nachbar gewählt statt `null` (`SyncFromConfig`, Ende).
  - Ausblenden bedeutet: Die Grundmenge `Games` enthält dann nur die gelisteten Spiele, `_byKey` behält alle. Dadurch stimmen `TotalCount` (Z. 1060), Kapsel, Quellen und Leerzustand von selbst.
  - `IsFiltered` (Z. 258) bleibt unberührt.
- `ShortcutService.cs`: neu `FindFor(key)` und `Delete(paths)`. Erfasst werden nur .lnk mit Ziel HdrPilot.exe und genau `--run "<Key>"`.
- `ILibraryContext.cs:130`, `LibraryDialogs.cs:82` und `ConfirmDialog.xaml`: optionales Häkchen.
- `MainWindow.xaml`:
  - Menüeintrag `ContextRemoveSettings` nach „Ordner öffnen“ (Z. 178-183).
  - Knopf `RemoveSettingsButton` neben Z. 1897.
- `LibraryStrings.resx`: neue Texte, `Details_NotInstalled` (Z. 321) um den Hinweis „ganz unten entfernen“ ergänzt.
- `SettingsWindow.xaml`/`.cs`: Karte „Bibliothek“ nach „Suchordner“ (Z. 395-443), Nav-Knopf nach `NavSearchFolders` (Z. 152).
- `SettingsViewModel.cs`: wird mit „Speichern“ übernommen (Z. 393-408, `Capture()` Z. 646). Dazu `ShellStrings.resx`.

**Doku:** E90, README, GATES.md Phase 11.

### Gates (Phase 11)
- [ ] G11.1: Kern und Bibliothek, belegt per Unit-Tests mit Gegenproben:
  - Entfernt wird nur bei nicht installiert, nicht manuell und ohne Sitzung. Findet ein Einlesen das Spiel dazwischen wieder, bleibt alles.
  - Gelöscht werden nur Eintrag, PNG, icon-cache-Eintrag und, nur mit Häkchen, die exakt passenden .lnk. Ein Fake-Dateisystem belegt: sonst nichts.
  - Nach Neuinstallation ist das Spiel neu und „HDR automatisch“ aus.
  - Fehlt `hideUninstalledGames`, ist es aus; die Rundreise stimmt.
  - Ausblenden: Zähler, Kapsel und Quellen passen, `IsFiltered` bleibt false, der Nachbar wird gewählt, der Leerzustand stimmt.

  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Uninstalled|FullyQualifiedName~RemoveSettings|FullyQualifiedName~LibraryCleanup" -MinTests 25
  EXPECT: TESTS_OK
- [ ] G11.2: Oberfläche an einer Testinstanz (TestConfigSeed mit 2 nicht installierten Launcher-Spielen und 1 manuellem):
  - Menüeintrag und Knopf erscheinen nur bei nicht installierten Launcher-Spielen.
  - „Abbrechen“ lässt die Testkonfiguration byteweise gleich.
  - „Entfernen“ nimmt genau ein Spiel heraus.
  - Ausblenden plus Speichern senkt die Zählung um 2.
  - Bilder bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel.

  CHECK: ps tools\gates\phase11-uninstalled-ui.ps1
  EXPECT: PHASE11_UI_OK
- [ ] G11.3: Migration an einer Kopie der echten Ablage:
  - Sie lädt ohne Warnung und zählt 5 nicht installierte Spiele.
  - Nach Umschalten und Speichern unterscheidet sich die Datei nur in `hideUninstalledGames`.
  - Der Hash der echten Ablage ist vorher und nachher gleich.

  CHECK: ps tools\gates\phase11-migration.ps1
  EXPECT: PHASE11_MIGRATION_OK
- [ ] G11.4: Gesamtstand: build.ps1 ohne Warnungen, alle Tests grün, README und E90 passen zu den resx-Texten.
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
- [ ] G11.5: Abnahme durch dich am echten System. Installiert wird erst nach deinem Ja.

### Offene Fragen (mit Empfehlung)
1. **Gehen eigene Bilder und Symbole mit?** Empfehlung: ja, sie hängen am Spielschlüssel und blieben sonst verwaist. HDRpilot-Verknüpfungen nur per Häkchen in der Rückfrage. Zurzeit gibt es keine.
2. **Rückfrage oder Rückgängig?** Empfehlung: nur eine Rückfrage mit Fokus auf „Abbrechen“, wie bei manuellen Spielen. Zusätzlich in der Karte „Einstellungen aller 5 nicht installierten Spiele entfernen…“ mit Namensliste. Die Taste Entf nur mit Rückfrage.
3. **Zählen ausgeblendete Spiele mit, in „656 Spiele“, der Kapsel und den Quellen?** Empfehlung: nein. Die Zahl steht nur in der Einstellungskarte und im Tooltip „Zurücksetzen“. Kein zusätzlicher Haken im Menü der Werkzeugleiste.

### Aufwand und Risiken
- **Aufwand:** mittel, etwa 14 Dateien, 25–30 Tests und 2 neue Gate-Skripte.
- **Fälschlich „nicht installiert“:** etwa bei getrenntem Laufwerk oder im Update-Zustand ohne Bit 4. Dagegen hilft:
  - Es wird nie automatisch gelöscht.
  - Die Rückfrage warnt, wenn das Laufwerk fehlt.
  - Die Prüfung erfolgt im Update-Lambda.
- **Ausblenden greift in die neue Zähl-Logik der Werkzeugleiste ein** (`LibraryViewModel.Toolbar.cs:66-108`). Darum erst nach dem Merge umsetzen.
- **The Outlast Trials hat „HDR automatisch“ an.** Nach dem Entfernen und einer Neuinstallation ist es aus; die Rückfrage sagt das.

---

## Wunsch 2: Quiver Launcher

### So sieht es für dich aus
- **Neue Quelle:** Quiver-Spiele erscheinen von selbst mit der Quelle „Quiver“: eigene Farbe, Filter, Cover aus Quivers lokalem Cache, kein Quiver-Logo. Bei dir ist das „Black“.
- **HDR automatisch** funktioniert wie sonst, auch beim Start aus Quiver. Die Erkennung läuft über den Spielordner, etwa 1 s nach dem Start.
- **„Mit HDR starten“ und HDR-Verknüpfungen** starten die EXE direkt, wie Quiver selbst. So ist HDR schon vor dem Spiel an.
- **Den Quiver-Ordner findet HDRpilot selbst** über Velopack-Protokoll, Downloads/Desktop/Dokumente, Suchordner, oder sobald Quiver einmal läuft. Notfalls trägst du ihn als Suchordner ein.

### Umsetzung
- **`GameModels.cs`:**
  - `GameSource.Quiver` nach `Manual` (Z. 15).
  - `ScanOptions.QuiverFolders` (Z. 80-86).
  - `CoverCandidates` neben `IconCandidates` (Z. 57).
- **Neu `Scanners/QuiverScanner.cs`** und `Scanners/Quiver/` mit diesen Teilen:
  - **Ordnersuche:**
    - Erkennung über `current\sq.version` mit Id QuiverLauncher. Andere Velopack-Programme fallen so heraus; im selben Ordner liegt bei dir auch ein `velopack.log`.
    - Die alte Struktur 2.4.x wird ebenfalls erkannt.
  - **Lesen von `apps.json`/`settings.json`** über `ScannerJson`.
  - **Wahl der EXE:**
    1. `selected_executable.txt`, nur wenn die Datei im Spielordner liegt.
    2. Sonst ein einzelner Kandidat, rekursiv höchstens bis Tiefe 3, ohne Reparse Points.
    3. Bei mehreren Kandidaten gibt es keinen Start-Befehl (E61).
  - **Cover-Cache:** MD5 der Bildadresse, nur lesen.
  - Schlüssel `Quiver:<folderName>`. `..`, Trenner und Laufwerkswurzeln werden abgelehnt (`InstallPaths.NormalizeInstallDir`).
- **`ScannerCatalog.cs`, `AppConfig.cs`** (`CoverCandidates`, versteckt `quiverFolders`), **`LibraryMerger.cs`**.
- **`ConfigJson` (`AppConfig.cs:213`):** Unbekannte Quellen werden tolerant gelesen. So bleibt eine Konfiguration mit einer neuen Quelle in späteren Versionen lesbar.
- **ScanOptions an drei Stellen:** `AutomationHost.cs:199`, `CliApp.cs:126`, `CliApp.Games.cs:55`, dazu die Hilfe in `CliApp.cs:87`.
- **Ordner lernen:** Startet `…\current\QuiverLauncher.exe`, merkt sich HDRpilot den Ordner und liest verzögert neu ein (E31: jeder Prozess wird nur einmal zugeordnet).
- **`ArtworkService.Resolve.cs:397`:** Quiver kommt in die `needExe`-Liste, dazu `ArtworkLocator`.
- **`HdrListSync.cs:241-254`:** Quiver wird ausgelassen, weil der Namensabgleich (Z. 254) meist das Originalspiel träfe.
- **Oberfläche:** `LibraryText.cs`, `LibraryStrings.resx`, Theme-Farbtoken für hell und dunkel.
- **`SearchFolderCandidateFinder.cs`:** schlägt den Quiver-Ordner nicht als Spiel vor.
- **Doku:** `docs/scanner/quiver.md`, E91, README, GATES.md Phase 12.

### Gates (Phase 12)
- [ ] G12.1: Scanner mit Beispieldateien (erfundene Pfade):
  - Der Ordner wird gefunden über Velopack-Protokoll, Suchordner, Downloads oder einen gelernten Ordner. Ein fremdes Velopack-Programm ist kein Treffer.
  - `AppsPath` und `installPath` werden beachtet.
  - `selected_executable.txt` gewinnt; zeigt sie aus dem Spielordner heraus, wird sie ignoriert.
  - Mehrere EXEs ergeben keinen Start-Befehl.
  - Ein `folderName` mit `..` wird abgelehnt.
  - Der Cache-Name stimmt: `…df6f08b0faeb3094ca257cafd49dbcd9.png` → `8C8FC667…`.
  - `ReadOnlyGuards` belegen: nichts geschrieben, kein Netzwerk.

  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Quiver" -MinTests 30
  EXPECT: TESTS_OK
- [ ] G12.2: Pflichttests je Scanner: `Missing_launcher_returns_empty_without_errors`, `Broken_*`, `Incomplete_*`.
  CHECK: ps tools\gates\phase2-scanner-tests.ps1
  EXPECT: PHASE2_SCANNER_TESTS_OK
- [ ] G12.3: Verträglichkeit:
  - Eine Konfiguration mit unbekannter Quelle lädt.
  - Alle übrigen Spiele und Einstellungen bleiben, nichts wird als „defekt“ gesichert.
  - Gegenprobe: Mit dem bisherigen Konverter schlägt der Test fehl.

  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~UnknownSource" -MinTests 3
  EXPECT: TESTS_OK
- [ ] G12.4: Echtes System, nur lesend:
  - `hdrpilot scan --source Quiver` liefert `Quiver:Black-BlackXBERecomp` mit `…\Apps\Black-BlackXBERecomp\BLACK.exe`, ohne Fehler.
  - Die Dateiliste des Quiver-Ordners mit Größen und Zeitstempeln ist vorher und nachher gleich.

  CHECK: ps tools\gates\phase12-quiver-scan.ps1
  EXPECT: PHASE12_QUIVER_SCAN_OK
- [ ] G12.5: Testinstanz:
  - Die Quelle „Quiver“ steht im Filter und in der Kapsel.
  - Das Cover kommt aus dem Cache der Beispieldateien.
  - „Mit HDR starten“ startet ein harmloses Prüfprogramm direkt, mit dem EXE-Ordner als Arbeitsordner, und die Sitzung erkennt es.
  - Bilder bei drei Größen, hell und dunkel.

  CHECK: ps tools\gates\phase12-quiver-ui.ps1
  EXPECT: PHASE12_QUIVER_UI_OK
- [ ] G12.6: Gesamtstand: build.ps1 ohne Warnungen, alle Tests grün, README, E91 und quiver.md passen zu den resx-Texten.
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
- [ ] G12.7: Livetest mit „Black“, nur nach deinem OK: Aus Quiver gestartet schaltet HDR ein, nach dem Ende wird zurückgestellt. Danach die Abnahme durch dich.

### Offene Fragen (mit Empfehlung)
1. **Eigene Quelle „Quiver“, mit automatischer Ordnersuche und Lernen, sobald Quiver läuft?** Empfehlung: ja. Die MuiCache (Liste ausgeführter Programme) wird nicht gelesen, das wäre Nutzungsverlauf.
2. **Start direkt über die EXE oder über `QuiverLauncher.exe --run`?** Empfehlung: direkt. `--run` öffnet ein Konsolenfenster, sucht online nach Updates und schreibt in Quivers Einstellungen. Bei mehreren EXEs ohne gespeicherte Wahl gibt es keinen Start-Befehl; der Hinweis lautet „in Quiver einmal starten“.
3. **Quiver-Spiele beim PCGamingWiki-Abgleich auslassen?** Empfehlung: ja, sie erscheinen als „unbekannt“. Der Namensabgleich träfe meist das Originalspiel, und so gehen keine Namen ans Netz.

### Aufwand und Risiken
- **Aufwand:** mittel bis groß, etwa so viel wie der GOG-Scanner, plus Oberfläche und Bilder. Etwa 20 Dateien (6 neu), rund 35 Tests, 2 neue Gate-Skripte.
- **Ordnersuche:** Quiver ist portabel und hat keinen Registry-Eintrag. Ungewöhnliche Orte werden erst gefunden, wenn Quiver einmal läuft oder ein Suchordner gesetzt ist.
- **Schnelle Formatwechsel:** v3.5.1 ist vom 15.10. und es erscheinen häufig neue Versionen. Das Velopack-Protokoll ist keine offizielle Schnittstelle. Darum tolerant lesen.
- **Verschobener Ordner:** Die Spiele gelten dann als „nicht installiert“. Die Einstellungen bleiben, und Wunsch 1 hilft beim Aufräumen.
- **Doppelte Einträge:** wenn „Black“ früher von Hand eingetragen wurde. Dann nur einen Hinweis zeigen, nichts automatisch löschen.
- **Große Ordner:** Bei dir liegt ein Abbild mit 7,8 GB. Darum nur Namen lesen, Tiefe und Anzahl begrenzen.
- **Kein früheres HDR beim Start aus Quiver:** Einen Wrapper wie bei Steam gibt es nicht. Spiele, die HDR nur beim Start abfragen, über HDRpilot starten.
- **Downgrade:** Eine Version vor Quiver hielte die Konfiguration für defekt (siehe Korrektur 5). Der Setup verhindert das, die tolerante Lesart schützt künftige Versionen; 1.4.0 bleibt betroffen.

### Was zu Quiver nicht belegt ist
- **GitHub-Angaben:** 2 445 Sterne, angelegt am 18.06., Release-Datum, offizielles Repo. Ich habe keine Webseiten abgerufen, das sind nur die Angaben aus dem Bericht.
- **TRADEMARKS.md:** liegt nicht in der lokalen Kopie. Den Inhalt kenne ich nur aus dem Bericht; die README verweist darauf.
- **Eigener Apps-Ordner (`AppsPath`), alte Struktur 2.4.x und MSI-Spiele:** nur aus dem Quelltext bzw. MIGRATING.md. Bei dir ist `AppsPath` leer.
- **Dass `--run` zweimal in `settings.json` schreibt:** nur aus dem Quelltext.
- **Velopack-Protokoll und MuiCache:** Ob das, was der Container zeigt, die echte Ansicht ist, wurde nicht von außen geprüft.
- **Offen:** wie Velopack das Protokoll kürzt, ob ein Windows-Installer kommt und ob Quiver-Spiele wie „Black“ selbst HDR können.

---

## Nebenbefund
Dein Benutzer-PATH enthält einen überflüssigen, harmlosen Eintrag auf einen alten Claude-Arbeitsordner (Pfad vor der Veröffentlichung entfernt). Er steht so im Startprotokoll von Quiver. Ob er außerhalb des Containers genauso drinsteht, ist nicht geprüft. Nichts geändert.

## Zur zweiten Frage (Dauer)
Dieser Plan beantwortet sie nicht. Aus git, falls die Werkzeugleiste gemeint ist:
- **Zeitraum und Umfang:** Die Arbeit lief vom 16.10. bis 19.10. Seit 8fe23c6 sind es 13 Commits in 45 Dateien mit +9 390/−603 Zeilen, davon +2 936 in Tests.
- **Zusammenführungen:** zwei Zweige.
- **Prüfung:** aus drei Blickwinkeln, mit Korrektur (b2ea035: 21 Dateien, +1 226).
- **UI-Gates:** am echten Bildschirm.
