# Zwischenstand (Übergabe)

Zuletzt aktualisiert: 2026-10-07 13:45. Dieses Dokument hält fest, wo die Arbeit steht, damit sie jederzeit nahtlos
fortgesetzt werden kann. Maßgeblich für Nachweise ist `GATES.md`, für Begründungen `docs/entscheidungen.md`.

## Erledigt (auf `main`, committet)
- Phase 1 HDR-Kern und Kommandozeile – alle Gates belegt (G1.5c vom Nutzer gestrichen).
- Phase 2 Spielbibliothek – 646 Spiele aus 7 Quellen, G2.2 vom Nutzer bestätigt.
- Phase 3 Überwachung und Sitzungslogik – alle Gates belegt; danach unabhängige Nachprüfung, 13 Funde behoben (E41–E48).
- Phase 4 Oberfläche und Tray – G4.1–G4.4, G4.6, G4.7 belegt (E49–E59). Offen ist nur G4.5, der gemeinsame Test.

## Als Nächstes
- G4.5 gemeinsam mit dem Nutzer: je installiertem Launcher ein Spiel über „Mit HDR starten“ und über die erzeugte
  Verknüpfung starten; Protokoll (`%LOCALAPPDATA%\HdrPilot\logs`) belegt „HDR bestätigt“ vor dem Prozessstart.
  Kandidaten: Steam, Epic (RDR2 oder Alan Wake 2), GOG (Cyberpunk 2077), Xbox (Palworld), Ubisoft (Avatar), EA
  (Battlefield 6); Battle.net hat kein Spiel. Dabei Tray-Menü und sichtbare Oberfläche vom Nutzer ansehen lassen.
  Dieser Test läuft mit der echten Ablage (`%APPDATA%\HdrPilot`), also ohne `--config-dir`.
- Phase 5: einzelne EXE veröffentlichen (`PublishSingleFile`, eigenständig, x64, ReadyToRun; CLI getrennt), README,
  Gates `phase5-publish.ps1` und `phase5-readme.ps1`, dann Rückfrage beim Nutzer zu WMI-Überwachung, Neustart bei
  verspätetem HDR, Steam-Hülle, englischer Oberfläche.
- Optional: Fehlermeldungen des Einlesens (ScanError) und von ManualGames in Ressourcen verschieben.

## Arbeitsweise und Fallen
- git liegt nicht im PATH der Sitzung: `& 'C:\Program Files\Git\cmd\git.exe' …`.
- PowerShell-5.1-Skripte mit Umlauten als UTF-8 mit BOM speichern.
- `hdrpilot.dll` (CLI) und `HdrPilot.dll` (App) dürfen nie im selben Ordner landen (E4, E49).
- Die App nie ohne `--config-dir` testen, solange die echte Ablage unberührt bleiben soll.
- HDR-Tests schalten den echten Bildschirm (MAG 341C OLED); Skripte stellen den Ausgangszustand wieder her.
- Leerlauf-CPU schwankt zwischen Messungen (0,10–0,39 % eines Kerns); Grenze im Gate 0,5 %.
