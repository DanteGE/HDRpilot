# Zwischenstand (Übergabe)

Zuletzt aktualisiert: 2026-10-10 20:10. Dieses Dokument hält fest, wo die Arbeit steht, damit sie jederzeit nahtlos
fortgesetzt werden kann. Maßgeblich für Nachweise ist `GATES.md`, für Begründungen `docs/entscheidungen.md`.

## Angehalten (2026-10-10 20:05, auf Wunsch des Nutzers: Wochenvolumen knapp, weiter nächste Woche)
Alle laufenden Arbeiten sind gestoppt. Nichts läuft mehr im Hintergrund, und der Stand ist committet.
Die Wünsche des Nutzers vom 2026-10-10 bilden Phase 8 (G8.1–G8.3 in `GATES.md`, alle noch
ohne Nachweis).

| Wunsch | Zweig / Arbeitsordner | Stand | Übergabe |
|---|---|---|---|
| Versionsnummer sichtbar (G8.1) | `feature/version-display` / `.worktrees/s9-version` | **Fertig bis auf einen Nachweis.** <ul><li>Umsetzung `67d9136` und Korrekturen `651dd4d`, `2952c72` sind gegengeprüft.</li><li>Laut Agenten liefen Build, Tests und Gates grün. Selbst nachgeprüft ist das noch nicht.</li><li>Offen (Prüfer: HOCH): die Tray-Zeile an einer laufenden Testinstanz per UI-Automation. Das Menü öffnet sich nur per Rechtsklick aufs Tray-Symbol. Entweder einen Weg ohne echte Eingabe finden, oder `phase6-traymenu.ps1` laufen lassen, wenn der Nutzer am Bildschirm ist (bewegt die Maus).</li></ul> | `docs/uebergabe/versionsanzeige.md`, `docs/uebergabe/ablauf-2026-10-10-abend.md` |
| Ersatz-Symbole, auch statt Anti-Cheat-Symbolen (G8.2), und eigenes Symbol per Rechtsklick (G8.3) | `feature/icons` / `.worktrees/s9-icons` | **Umgesetzt, Korrekturrunde unvollständig.** <ul><li>`9294239`: vollständige Umsetzung mit E86, README und 220 Symbol-Tests. Laut Agenten liefen alle Gates grün.</li><li>Zwei Prüfer fanden je 1 mittleren und 4 kleine Punkte.</li><li>Deren Korrektur ist angefangen, aber nicht fertig: `e7e59bc`, WIP, nicht gebaut.</li><li>Die Messung an der echten Bibliothek fehlt noch.</li></ul> | `docs/uebergabe/ablauf-2026-10-10-abend.md` (Funde), `docs/recherche/symbole/auftrag.md` |
| Auto HDR / RTX HDR pro Spiel | – | recherchiert, vom Nutzer zurückgestellt (siehe unten) | `docs/recherche/auto-hdr-rtx-hdr.md` |

**Reihenfolge beim Weitermachen** (Volumen schonen: wenige Agenten, Prüfungen selbst laufen lassen):
1. G8.1: Zweig selbst bauen und testen und den offenen Tray-Nachweis klären.
2. G8.2 und G8.3:
   - Den WIP `e7e59bc` gegen die Funde in `ablauf-2026-10-10-abend.md` fertigstellen, bauen und testen.
   - An einer Kopie der echten Bibliothek messen. Die Kopie liegt im Sitzungs-Scratchpad unter `g81\real-config.json`
     (650 Spiele, 2026-10-10). Fehlt sie, neu per WMI ziehen.
   - Statt der großen Messung mit Bildtafeln reicht zur Not eine gezielte Prüfung: Gears of War: E-Day, The Isle und
     die Spiele mit vorher Anti-Cheat- oder Engine-Symbol (`docs/recherche/symbole/messung-vorher/`).
3. Beide Zweige nach `phase5/release` zusammenführen. G8.x belegen und alle Gates laufen lassen.
4. Version auf **1.3.0** setzen, dann `tools\publish.ps1` und den Setup bauen.
5. Mit Zustimmung des Nutzers per WMI außerhalb des Containers installieren (wie G7.8).
6. `main` per `merge --ff-only phase5/release` nachziehen.

**Hinweise:**
- Die Recherche-Unterlagen lagen nur im Sitzungs-Scratchpad. Alles Nötige ist jetzt unter `docs/recherche/` gesichert.
  - Nicht gesichert: die Bibliothekskopie `g78\after-config.json` (Nutzerdaten, neu zu ziehen) und 1697 Bilder. Ein
    Agent hatte diese Bilder aus Launcher-Caches in den Repo-Ordner kopiert. Ich hatte sie in den Scratchpad verschoben;
    es sind entbehrliche Kopien.
- Die Workflows selbst lassen sich in einer neuen Sitzung nicht fortsetzen. Weitergemacht wird anhand der Übergabe-
  dokumente und der WIP-Commits.

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
- Beim Nutzer installiert: 1.1.0 (G7.5, die Eintragung entstand erst durch den eigenen Setup-Lauf des Nutzers, E84),
  dann 1.2.0 (G7.8, per WMI außerhalb des Containers): `%LOCALAPPDATA%\Programs\HDRpilot`, „Installierte Apps“, Startmenü-
  und Desktop-Verknüpfung, Autostart. Die alte EXE liegt noch als `HdrPilot-vorher.exe.bak` daneben.
- Container der Claude-App entdeckt (E84): aus der Sitzung gestartete Prozesse schreiben AppData und HKCU\Software in den
  privaten Speicher der Claude-App. Setup und Entfernen brechen dort ab (Exit-Code 7), die App warnt im Protokoll.
  Arbeitsregel: echte Aktionen nur per WMI (`Win32_Process.Create`) von außen, Ergebnis über `%TEMP%` lesen.
- Neustart-Puffer einstellbar (E85, G7.6): Einstellungen, Minuten und Sekunden, Standard 1:00.

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

## Zurückgestellt (Entscheidung des Nutzers)
- Auto HDR (Windows) und RTX HDR (NVIDIA) pro Spiel für Spiele ohne natives HDR, recherchiert am 2026-10-10.
  - Ergebnis: technisch teilweise möglich. Zwei Kernfragen lassen sich nur durch Versuche am PC klären.
  - Der Nutzer will vorerst nicht weitermachen, aber vielleicht später darauf zurückkommen.
  - Alles Nötige zum Wiederaufnehmen (Ergebnis, Vorschlag „HDR-Art“, Versuche E0–E12, Quellen, Lesewerkzeuge) steht in
    `docs/recherche/auto-hdr-rtx-hdr.md`.

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
