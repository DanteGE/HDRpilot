# Zwischenstand (Übergabe)

Zuletzt aktualisiert: 2026-10-07. Dieses Dokument hält fest, wo die Arbeit steht, damit sie jederzeit nahtlos
fortgesetzt werden kann. Maßgeblich für Nachweise ist `GATES.md`, für Begründungen `docs/entscheidungen.md`.

## Erledigt (auf `main`, committet)
- Phase 1 HDR-Kern und Kommandozeile – alle Gates belegt (G1.5c vom Nutzer gestrichen).
- Phase 2 Spielbibliothek – 646 Spiele aus 7 Quellen, G2.2 vom Nutzer bestätigt.
- Phase 3 Überwachung und Sitzungslogik – alle Gates belegt; danach unabhängige Nachprüfung, 13 Funde behoben (E41–E48).

## In Arbeit
- Phase 4 Oberfläche. Git-Arbeitskopien unter `.worktrees/` (nicht im Repository):
  - `p4` → Branch `phase4/ui`: App-Gerüst (Einzelinstanz per WM_COPYDATA, Design-Token hell/dunkel, ChromeWindow,
    Snapshot-Modus `--snapshot`, Testprojekt `tests/HdrPilot.App.Tests`, Gate-Skripte `tools/gates/phase4-*.ps1`).
  - `p4a` → `phase4/library`: Hauptfenster/Spielebibliothek (Workflow-Agent).
  - `p4b` → `phase4/shell`: Tray, Einstellungen, Autostart, Bildschirmereignisse (Workflow-Agent).
  - Demo-Konfiguration für Snapshots: `.worktrees/uitest-config`.
- Nächste Schritte Phase 4: `phase4/library` und `phase4/shell` in `phase4/ui` mergen, `main` in `phase4/ui` mergen
  (bringt E41–E48 und CoreStrings), App-Texte für `NotificationKind.StateNotSaved` prüfen, Gates G4.1–G4.7 ausführen,
  dann gemeinsamer Test G4.5 mit dem Nutzer (je Launcher ein Spiel über App und Verknüpfung starten; Protokoll prüfen).

## Danach
- Phase 5: einzelne EXE veröffentlichen (`PublishSingleFile`, eigenständig, x64, ReadyToRun), README, Rückfrage beim
  Nutzer zu WMI-Überwachung, Neustart bei verspätetem HDR, Steam-Hülle, englischer Oberfläche.

## Arbeitsweise und Fallen
- git liegt nicht im PATH der Sitzung: `& 'C:\Program Files\Git\cmd\git.exe' …`.
- PowerShell-5.1-Skripte mit Umlauten als UTF-8 mit BOM speichern.
- `hdrpilot.dll` (CLI) und `HdrPilot.dll` (App) dürfen nie im selben Ordner landen (E4); deshalb eigenes Testprojekt
  `HdrPilot.App.Tests`.
- Die App nie ohne `--config-dir` testen, solange die echte Ablage unberührt bleiben soll.
- HDR-Tests schalten den echten Bildschirm (MAG 341C OLED); Skripte stellen den Ausgangszustand wieder her.
