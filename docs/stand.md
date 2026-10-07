# Zwischenstand (Übergabe)

Zuletzt aktualisiert: 2026-10-07 15:50. Dieses Dokument hält fest, wo die Arbeit steht, damit sie jederzeit nahtlos
fortgesetzt werden kann. Maßgeblich für Nachweise ist `GATES.md`, für Begründungen `docs/entscheidungen.md`.

## Erledigt (auf `main`, committet)
- Phase 1 HDR-Kern und Kommandozeile – alle Gates belegt (G1.5c vom Nutzer gestrichen).
- Phase 2 Spielbibliothek – 646 Spiele aus 7 Quellen, G2.2 vom Nutzer bestätigt.
- Phase 3 Überwachung und Sitzungslogik – alle Gates belegt; danach unabhängige Nachprüfung, 13 Funde behoben (E41–E48).
- Phase 4 Oberfläche und Tray – G4.1–G4.4, G4.6, G4.7 belegt (E49–E59). Offen ist nur G4.5, der gemeinsame Test.
- Phase 5 Auslieferung – G5.1 (Einzel-EXE über `tools\publish.ps1`) und G5.2 (README, gegen den Quelltext geprüft)
  belegt (E60–E64). Offen: G5.3, die Rückfrage beim Nutzer nach dem gemeinsamen Test.

## Als Nächstes
- G4.5 gemeinsam mit dem Nutzer, mit der veröffentlichten EXE aus `artifacts\publish\HdrPilot` (nur mit Zustimmung
  nach `%LOCALAPPDATA%\Programs\HdrPilot\` kopieren; läuft mit der echten Ablage, also ohne `--config-dir`). Je
  Launcher ein Spiel über „Mit HDR starten“ und über die erzeugte Verknüpfung; das Protokoll
  (`%LOCALAPPDATA%\HdrPilot\logs`) muss „HDR bestätigt“ vor dem Start-Befehl und dem erkannten Prozess zeigen.
  Testspiele mit belegtem eigenen HDR (Recherche PCGamingWiki u. a.): Dying Light: The Beast (Steam:3008130), Alan Wake 2 (Epic),
  Cyberpunk 2077 (GOG, REDlauncher), Avatar (Ubisoft), Battlefield 6 (EA, Javelin); Palworld (Xbox) hat kein eigenes HDR,`r`n  nur Gegenprobe; Battle.net hat kein Spiel. Anti-Cheat-Prüfung erledigt (E65–E68, G5.4). Dabei Tray-Menü und sichtbare Oberfläche vom Nutzer ansehen lassen.
- Danach in der README unter „Bekannte Grenzen“ den Punkt „Start über die Launcher“ durch das Testergebnis ersetzen,
  neu veröffentlichen (`phase5-readme.ps1`, `phase5-publish.ps1`), dann G5.3: Rückfrage zu WMI-Überwachung, Neustart
  bei verspätetem HDR, Steam-Hülle, englischer Oberfläche.
- Optional: Fehlermeldungen des Einlesens (ScanError) und von ManualGames in Ressourcen verschieben.

## Arbeitsweise und Fallen
- git liegt nicht im PATH der Sitzung: `& 'C:\Program Files\Git\cmd\git.exe' …`.
- PowerShell-5.1-Skripte mit Umlauten als UTF-8 mit BOM speichern.
- `hdrpilot.dll` (CLI) und `HdrPilot.dll` (App) dürfen nie im selben Ordner landen (E4, E49).
- Die App nie ohne `--config-dir` testen, solange die echte Ablage unberührt bleiben soll.
- HDR-Tests schalten den echten Bildschirm (MAG 341C OLED); Skripte stellen den Ausgangszustand wieder her.
- Leerlauf-CPU schwankt zwischen Messungen (0,10–0,39 % eines Kerns); Grenze im Gate 0,5 %.
