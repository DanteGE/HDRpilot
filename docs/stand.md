# Zwischenstand (Übergabe)

Zuletzt aktualisiert: 2026-10-09 23:20. Dieses Dokument hält fest, wo die Arbeit steht, damit sie jederzeit nahtlos
fortgesetzt werden kann. Maßgeblich für Nachweise ist `GATES.md`, für Begründungen `docs/entscheidungen.md`.

## Erledigt (auf `main`, committet)
- Phase 1 HDR-Kern und Kommandozeile – alle Gates belegt (G1.5c vom Nutzer gestrichen).
- Phase 2 Spielbibliothek – 646 Spiele aus 7 Quellen, G2.2 vom Nutzer bestätigt.
- Phase 3 Überwachung und Sitzungslogik – alle Gates belegt; danach unabhängige Nachprüfung, 13 Funde behoben (E41–E48).
- Phase 4 Oberfläche und Tray – alle Gates belegt, G4.5 gemeinsam mit dem Nutzer (alle Launcher außer Battle.net,
  dort ist kein Spiel installiert).
- Phase 5 Auslieferung – alle Gates belegt, inkl. Anti-Cheat-Prüfung (E65–E68) und Rückfrage G5.3.
- Phase 6 Feedback – alle Gates belegt (E69–E81): Starten-Bereich oben, Sortierung „Zuletzt gespielt“, verschiebbare
  Spalten, Umbenennung in HDRpilot, Steam-Startoption, Neustart-Puffer, Frühwarnung Epic/Steam, Hänger-Hinweis,
  HDR-Liste von PCGamingWiki (ohne Konto oder mit Bot-Passwort). Nachprüfung mit 4 Prüfern und Gegenprüfer, 10 Funde
  behoben (E81), beide Korrekturrunden gegengeprüft.
- Fehlermeldung des Nutzers behoben: Tray-Menü schloss sich nach dem Rechtsklick sofort (E82, G6.8, Gate
  `phase6-traymenu.ps1` mit echter Maus und Tastatur; bewegt die Maus, nur am echten Desktop ausführen).
- Phase 7 Installer (Wunsch des Nutzers, E83): eigener Setup `HDRpilot-Setup-1.1.0.exe` (normale EXE mit angehängtem
  Paket), installiert für den Benutzer ohne Adminrechte, Eintrag unter „Installierte Apps“, Aktualisieren mit Rückfall,
  Entfernen aus einer Temp-Kopie; Einstellungen und Daten bleiben. Drei parallele Teile, je gegengeprüft, Schlussprüfung
  mit 4 Blickwinkeln und Gegenprüfer (17 Funde), zweite Gegenprüfung (4 Funde), alles behoben. Gates G7.1–G7.5 belegt.
- Beim Nutzer per Setup auf 1.1.0 aktualisiert (G7.5): `%LOCALAPPDATA%\Programs\HDRpilot` (derselbe Ordner wie bisher),
  eingetragen unter „Installierte Apps“, Startmenü-Verknüpfung, Autostart; Einstellungen unverändert. Die alte EXE liegt
  noch als `HdrPilot-vorher.exe.bak` daneben (bleibt beim Entfernen stehen, da nicht vom Setup).

## Offen (nichts blockiert)
- Kontomodus der HDR-Liste nur mit Attrappe getestet; live erst, wenn der Nutzer ein Bot-Passwort einträgt.
- Battle.net ohne installiertes Spiel nicht live getestet.
- Ursache des Dead-Island-2-Hängers beim automatischen Umschalten nicht bewiesen (Neustart-Puffer und Frühwarnung
  mildern ihn; sicher sind „Mit HDR starten“, die Verknüpfung oder die Steam-Startoption).
- Aufräumen beim Nutzer (nur auf Wunsch): `config.json.vor-ruecksetzung.bak` im Roaming-Ordner, die gesicherte alte EXE
  `HdrPilot-vorher.exe.bak` im Programmordner. Die 6 Desktop-Verknüpfungen „(HDR)“ hat der Nutzer am 07.10. selbst gelöscht.
- Setup: Aktualisieren über eine laufende ältere Version lief beim Nutzer nicht live (die App lief gerade nicht); belegt
  durch die Neuinstallation über die laufende App (G7.2) und Unit-Tests. Neue Version: `tools\publish.ps1`, dann Setup.
- Optional: Fehlermeldungen des Einlesens (ScanError) und von ManualGames in Ressourcen verschieben; englische Oberfläche.

## Arbeitsweise und Fallen
- git liegt nicht im PATH der Sitzung: `& 'C:\Program Files\Git\cmd\git.exe' …`. Commit-Nachrichten mit Anführungszeichen
  oder Umlauten über eine Datei ohne BOM (`-F`) mit kurzem Pfad (lange Pfade: „Filename too long“).
- PowerShell-5.1-Skripte mit Umlauten als UTF-8 mit BOM speichern; typografische Anführungszeichen in Skripten nur in
  einfachen Anführungszeichen.
- `hdrpilot.dll` (CLI) und `HDRpilot.dll` (App) dürfen nie im selben Ordner landen (E4, E49).
- Die App nie ohne `--config-dir` testen, solange die echte Ablage unberührt bleiben soll; UI-Automation nur an der PID
  der Testinstanz (E72).
- HDR-Tests schalten den echten Bildschirm (MAG 341C OLED); Skripte stellen den Ausgangszustand wieder her. Ist der
  Bildschirm aus, brechen sie mit exit 3 ab („Keine aktiven Bildschirme gefunden“).
- Testfilter in Gates treffen Klassen- und Methodennamen, nicht Dateinamen (Teilklassen wie SessionManagerTests.*.cs).
- Leerlauf-CPU schwankt zwischen Messungen (0,10–0,39 % eines Kerns); Grenze im Gate 0,5 %.
- Workflows mit `isolation: 'worktree'` scheitern hier („Failed to resolve base branch HEAD“): Arbeitsordner selbst
  mit `git worktree add` anlegen.
