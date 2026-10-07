# Manuelle Einträge und Suchordner-Kandidaten

> Vor der Veröffentlichung bereinigt: Installationsordner des Messrechners sind durch erfundene ersetzt (etwa
> `D:\Steam-Bibliothek`, `D:\GOG-Spiele`), vollständige Spielelisten durch erfundene Beispielzeilen.

Klassen `HdrPilot.Core.Games.ManualGames` und `HdrPilot.Core.Games.SearchFolderCandidateFinder`. Beide sind keine
Scanner hinter `IGameScanner`: Manuelle Einträge legt der Nutzer an, Kandidaten sind nur Vorschläge. Beide lesen nur.

## 1. Angaben aus dem Auftrag und Ergebnis

Der Abschnitt „Manuell“ enthält keine Angabe mit Vermerk (prüfen). Am Zielrechner wurden beide Bausteine trotzdem
lesend gegen echte Ordner ausgeführt (kleine Prüf-App gegen die gebaute `HdrPilot.Core.dll`, Ergebnis unten).

| Vorgabe | Umsetzung |
|---|---|
| EXE oder Ordner hinzufügen, Name frei wählbar | `ManualGames.Create(fileSystem, path, name, id)` |
| Suchordner: Unterordner mit EXE-Dateien werden als Kandidaten vorgeschlagen | `SearchFolderCandidateFinder.FindCandidates(searchFolders, knownInstallDirs)` |
| Nichts wird ungefragt übernommen | Der Finder liefert nur `GameCandidate`; ein Eintrag entsteht erst über `ManualGames.Create`. Test mit einem Dateisystem, das bei jedem Schreibzugriff wirft. |

## 2. Verhalten im Detail

### `ManualGames.Create`

| Eingabe | Ergebnis |
|---|---|
| Pfad zu einer `.exe` | `InstallDir` = ihr Ordner, `Executables` = [EXE], `LaunchCommand` = EXE, `LaunchWorkingDirectory` = Ordner, Name = Dateiname ohne Endung |
| Pfad zu einem Ordner | `InstallDir` = Ordner, `Executables` = EXEs der obersten Ebene ohne Hilfsprogramme (Ausschlussliste), nach Name sortiert; `LaunchCommand` = die EXE, wenn es genau eine gibt, sonst null; Name = Ordnername |
| `name` | frei wählbar, getrimmt; leer oder nur Leerzeichen → Standardname |
| `id` | `SourceId`; fehlt oder leer → neue GUID im Format N (32 Kleinbuchstaben/Ziffern) |
| Pfad mit `/`, doppelten oder abschließenden Trennern | über `PathUtil.Normalize` vereinheitlicht |
| leer, relativ, nicht vorhanden, andere Datei als `.exe`, Laufwerkswurzel | `ArgumentException` mit deutscher Meldung, `ParamName = "path"` |

### `SearchFolderCandidateFinder.FindCandidates`

- Jeder **direkte** Unterordner eines Suchordners ist Kandidat, wenn darin bis **Tiefe 3** (0 = der Unterordner selbst,
  3 = etwa `<Spiel>\<Projekt>\Binaries\Win64`) mindestens eine EXE liegt, die kein Hilfsprogramm ist.
- Ausgelassen werden Unterordner, die einem bekannten Installationsordner entsprechen, darin oder darüber liegen
  (Vergleich ohne Groß-/Kleinschreibung, Schrägstriche egal), außerdem Unterordner, die selbst ein Suchordner sind.
- Fehlende, leere oder relative Suchordner werden still übersprungen, doppelte nur einmal durchsucht. Nicht lesbare
  Ordner (Gerätefehler, Zugriff verweigert) werden ausgelassen, der Rest geht weiter.
- Ist der Suchordner eine Laufwerkswurzel, sind Windows-Systemordner keine Kandidaten: Namen mit `$` am Anfang
  (`$RECYCLE.BIN`, `$WINDOWS.~BT` …) und `System Volume Information`. Im Review ergänzt: Ein gelöschter Spielordner
  liegt mit allen EXEs unter `$RECYCLE.BIN\<SID>\$R…\` und wurde sonst als Kandidat „$RECYCLE.BIN“ vorgeschlagen
  (Test `Recycle_bin_and_windows_system_folders_on_a_drive_root_are_not_candidates`). In anderen Suchordnern bleibt ein
  Ordner wie `$Dollar` ein Kandidat.
- `Executables`: oberste Ebene zuerst; innerhalb einer Ebene zuerst die EXE, die wie der Ordner heißt („Swat4.exe“ in
  „SWAT 4“), dann ähnlich benannte, dann alle übrigen nach Pfad.
- Abbruch über das `CancellationToken` wirft `OperationCanceledException`.

## 3. Lauf am Zielrechner (2026-10-07)

Bekannte Installationsordner für den Lauf: das Ergebnis des Xbox-Scanners (`C:\XboxGames\Wolkenhof\Content`).

Die Tabellen dieses Abschnitts nannten Ordner und Spiele des Messrechners. Vor der Veröffentlichung sind sie durch
erfundene Beispielzeilen im selben Aufbau ersetzt (Namen und Ordner frei erfunden); Zahlen und Dauer stammen aus der
Messung.

| Suchordner | Kandidaten (Name, erste EXE) | Dauer |
|---|---|---|
| `C:\XboxGames` | keine: `Wolkenhof` liegt über einem bekannten Installationsordner, `GameSave` enthält keine EXE | 4 ms |
| `D:\GOG-Spiele` + `F:\GOG-Spiele` | Beispielspiel Eins (`bin\x64\Eins.exe`), Beispielspiel Zwei (`Zwei.exe`), Beispielspiel Drei (`drei.exe`, 19 EXEs), Beispielspiel Vier (`Content\System\Vier.exe`, 14 EXEs) | 10 ms |
| `D:\Anwendungen\Epic Games` | sieben Spielordner, etwa BeispielspielFuenf (`Fuenf.exe`) und BeispielspielSechs (`newRoot\SechsGame.exe`) | 8 ms |

Zum Vergleich: Die GOG-Registry nennt auf D: und F: genau diese vier Ordner (fünf Einträge, ein Hauptspiel und sein
Zusatzinhalt teilen sich einen Ordner; der sechste Eintrag liegt auf C:), die Epic-Manifeste genau diese sieben Ordner.
In der App sind GOG- und Epic-Spiele bekannte Installationsordner und würden deshalb nicht vorgeschlagen; hier fehlten
sie absichtlich, um die Erkennung zu zeigen.

Nachprüfung im Review (2026-10-07, eigene Prüf-App gegen die gebaute `HdrPilot.Core.dll`, nur lesend): dieselben
11 Kandidaten mit denselben ersten EXEs, 34 ms für alle vier Suchordner; die GOG-Registry (32-Bit-Sicht, Wert `path`)
nennt dieselben Ordner, die Epic-Manifeste (`InstallLocation`) dieselben sieben. Zusätzlich `D:\` und `F:\` als
Suchordner: 9 Kandidaten (die Spiele- und Programmordner auf diesen Laufwerken), kein Systemordner. Die drei manuellen
Einträge unten wurden mit identischem Ergebnis wiederholt; `D:\` ergibt die `ArgumentException` „Ein ganzes Laufwerk
kann kein Spielordner sein …“.

Manuelle Einträge:

| Pfad | Name | InstallDir | Start | Executables |
|---|---|---|---|---|
| `F:\GOG-Spiele\Beispielspiel Vier` | Beispielspiel Vier | `F:\GOG-Spiele\Beispielspiel Vier` | keiner | keine (oben nur `unins000.exe`, die Spiel-EXEs liegen tiefer) |
| `D:\GOG-Spiele\Beispielspiel Zwei` | Beispielspiel Zwei | `D:\GOG-Spiele\Beispielspiel Zwei` | `…\Zwei.exe` | `Zwei.exe` (`unins000.exe` ausgefiltert) |
| `F:\GOG-Spiele\Beispielspiel Vier\Content\System\Vier.exe` | Vier | `F:\GOG-Spiele\Beispielspiel Vier\Content\System` | `…\Vier.exe` | `Vier.exe` |

## 4. Entscheidungen

- **M1 Laufwerkswurzeln werden abgelehnt** (Ordner `D:\` oder EXE direkt in `D:\`): Sonst gälte in Phase 3 jeder
  Prozess auf dem Laufwerk als Spiel. Die Meldung schlägt vor, den Spielordner zu wählen.
- **M2 Nur `.exe`-Dateien oder Ordner**, keine Verknüpfungen oder Skripte: Der Prozessabgleich braucht einen Ordner,
  und eine `.lnk` liegt meist außerhalb davon.
- **M3 Relativer Pfad → Fehler** statt Auflösung gegen das aktuelle Arbeitsverzeichnis, das bei einer Tray-App zufällig ist.
- **M4 Ordner: nur oberste Ebene** (Vorgabe). Liegen die EXEs tiefer (SWAT 4), bleibt die Liste leer und der Nutzer
  wählt die EXE selbst; der Suchordner-Finder schlägt dagegen bis Tiefe 3 vor.
- **M5 Leere ID → neue GUID**, damit nie ein leerer Schlüssel `Manual:` entsteht.
- **M6 Finder ohne Fehlerliste**: Der Vertrag liefert nur Kandidaten; nicht lesbare Ordner sind für einen Vorschlag
  kein Fehler, den der Nutzer beheben müsste.
- **M7 Reihenfolge der EXEs**: oberste Ebene zuerst (Vorgabe), innerhalb der Ebene der Ordnername als Hinweis, damit
  die UI eine sinnvolle Start-EXE vorschlagen kann.

## 5. Bekannte Grenzen

- Beim Hinzufügen einer einzelnen EXE ist ihr Ordner der Installationsordner. Startet das Spiel weitere Prozesse in
  Nachbarordnern (SWAT 4: `ContentExpansion\System\Swat4X.exe`), werden sie nicht erkannt; dann besser den Spielordner
  hinzufügen.
- Kandidaten liegen nur eine Ebene unter dem Suchordner; Spiele in `Suchordner\Hersteller\Spiel` werden als Kandidat
  „Hersteller“ vorgeschlagen.
- EXEs tiefer als Tiefe 3 werden nicht gefunden.
- Ob eine EXE wirklich ein Spiel ist, entscheidet nur die Ausschlussliste; Werkzeuge wie Editoren oder Konfigurations-
  programme erscheinen mit in der Liste.
- Außer Laufwerkswurzeln werden keine weiteren zu breiten Ordner abgelehnt. Wählt der Nutzer etwa `C:\Program Files`
  oder eine EXE in `C:\Windows\System32`, gälte in Phase 3 jeder Prozess darunter als Spiel. Eine Prüfung gegen
  Systemordner bräuchte `IAppEnvironment`, die der vorgegebene Vertrag `Create(IFileSystem, …)` nicht hat; das sollte
  die Oberfläche (Phase 4) mit einer Rückfrage abfangen.
