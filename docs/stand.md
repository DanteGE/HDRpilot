# Zwischenstand (Übergabe)

Zuletzt aktualisiert: 2026-10-12 (abends). Dieses Dokument hält fest, wo die Arbeit steht, damit sie jederzeit
nahtlos fortgesetzt werden kann. Maßgeblich für Nachweise ist `GATES.md`, für Begründungen `docs/entscheidungen.md`.

## Angehalten: Phase 9 Windows Auto HDR pro Spiel (auf Wunsch des Nutzers, 2026-10-12)
HDRpilot 1.3.0 ist beim Nutzer installiert. Phase 9 ist halb fertig. Nichts läuft im Hintergrund, und alles ist
committet.

**Wünsche und Entscheidungen des Nutzers:** `GATES.md`, Abschnitt „Phase 9“ (G9.1–G9.6). Kurz:
- **Platz:** eigener Abschnitt „BILD“ im Spiel-Tab mit dem Schalter „Windows Auto HDR“, klar getrennt von „HDR
  automatisch“. In der Liste nur ein Abzeichen „AUTO HDR“.
- **Unabhängig:** schaltbar ohne „HDR automatisch“, mit Hinweis.
- **Natives HDR:** ausgegraut, aber nutzbar, mit Hinweis.
- **Technik:** beide Wege, pro Spiel (Token `AutoHDREnable=2097` je EXE) und eine Notlösung „global nur während des
  Spiels“ (Einstellung, Standard aus).
- **Rückbau:** über Einstellungen, Setup und Entfernen des Spiels.
- **Versuch am echten System:** später, gemeinsam mit dem Nutzer (G9.6).
- RTX HDR bleibt zurückgestellt.

| Zweig / Arbeitsordner | Stand |
|---|---|
| `feature/autohdr` / `.worktrees/s10-autohdr` | `62dfed3` Entwurf und Gerüst. `20ee7f5` und `ad33347` führen beide Teilzweige zusammen. `4e1c7b6` ist ein WIP der Abstimmung danach: nicht fertig, nicht vollständig gebaut und geprüft |
| `feature/autohdr-core` / `.worktrees/s10-core` | `34c83cf`: Werte je Spiel, globale Notlösung, Rücknahme (G9.2–G9.4), fertig laut Agent |
| `feature/autohdr-ui` / `.worktrees/s10-ui` | `96c4962`: Oberfläche, Einstellungen, Setup, README und E88 (G9.1, G9.5), fertig laut Agent |

**Unterlagen:**
- Entwurf mit Schnittstellen, Formaten, Testplan und Aufteilung: `docs/uebergabe/auto-hdr-entwurf.md`.
- Berichte der Agenten (Entwurf, beide Teilzweige): `docs/uebergabe/auto-hdr-ablauf-2026-10-12.md`. Der Bericht der
  Abstimmung fehlt, weil sie unterbrochen wurde.
- Recherche: `docs/recherche/auto-hdr-rtx-hdr.md`.

**Weitermachen:**
1. In `.worktrees/s10-autohdr` den WIP `4e1c7b6` bauen und durchsehen, dann die Abstimmung fertigstellen: Schalter →
   Konfiguration → Werte je Spiel, Notlösung, Rücknahme-Schaltfläche, Setup.
2. Alle Prüfungen laufen lassen:
   - `build.ps1`, volles `test.ps1`
   - `-Filter AutoHdr -MinTests 25`, `-Filter AutoHdrRegistry -MinTests 15`, `-Filter AutoHdrGlobal -MinTests 8`
   - `phase9-autohdr-ui.ps1`, `phase7-setup.ps1`, `phase7-setup-ui.ps1`
   - die übrigen Gates ohne echte HDR-Umschaltung
3. Unabhängige Prüfung (Registry-Sicherheit und Rückbau, Notlösung und Spielstart samt Anti-Cheat, Bedienung), dann
   Korrektur und Gegenprüfung.
4. Nach `phase5/release` zusammenführen und G9.1–G9.5 belegen. Danach eine Version (1.4.0) mit Zustimmung des Nutzers
   installieren und den Versuch G9.6 gemeinsam machen.
- Tests und Gates dürfen nie die echten Schlüssel `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` beschreiben,
  sondern nur eine Testwurzel. Der Entwurf beschreibt den Schalter dafür.

## Vorher abgeschlossen
- Phase 8 ist fertig, HDRpilot 1.3.0 ist beim Nutzer installiert.
- Die Übergabedokumente vom 2026-10-10 sind nur noch Verlauf: `docs/uebergabe/versionsanzeige.md`,
  `docs/uebergabe/ablauf-2026-10-10-abend.md` und der Abschnitt „Stand“ in `docs/recherche/symbole/auftrag.md`.

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
- Phase 8 Wünsche vom 2026-10-10 (G8.1–G8.3 belegt):
  - **Versionsnummer (E87):** im Kopf der Einstellungen „HDRpilot 1.3.0“, im Tray-Menü „Version 1.3.0“ (Wahl des
    Nutzers). Der echte Tray-Test lief am Desktop mit Zustimmung des Nutzers.
  - **Ersatz-Symbole aus lokalen Quellen (E86):** Steam-Symbol zuerst, Ausschlussliste für Anti-Cheat und
    Fremdbestandteile, Erkennung von Standardsymbolen.
    - Gemessen an der echten Bibliothek: 651 von 651 mit eigenem Symbol, 626 mit ≥ 48 px.
    - Gears of War: E-Day und The Isle zeigen ihr Steam-Symbol statt des EasyAntiCheat-Symbols.
  - **Eigenes Symbol per Rechtsklick (E86):** auch für Verknüpfungen, über eine `.ico` im Datenordner.
  - **Prüfung:** drei Prüfrunden mit unabhängigen Prüfern und eine Schlussprüfung des zusammengeführten Stands.
- Beim Nutzer installiert am 2026-10-12, per WMI außerhalb des Containers, mit Zustimmung des Nutzers:
  - 1.3.0 ersetzt 1.2.0, Setup-Exit-Code 0.
  - Die App läuft wieder, Autostart und Eintrag unter „Installierte Apps“ (1.3.0) stimmen.
  - Einstellungen und alle Spiel-Einstellungen sind unverändert: 651 Spiele, 28 mit HDR automatisch.
  - Das Protokoll zeigt den Start von 1.3.0 mit der echten Konfiguration, 0 Fehler.

## Offen (nichts blockiert)
- Kontomodus der HDR-Liste nur mit Attrappe getestet; live erst, wenn der Nutzer ein Bot-Passwort einträgt.
- Battle.net ohne installiertes Spiel nicht live getestet.
- Ursache des Dead-Island-2-Hängers beim automatischen Umschalten nicht bewiesen (Neustart-Puffer und Frühwarnung
  mildern ihn; sicher sind „Mit HDR starten“, die Verknüpfung oder die Steam-Startoption).
- Aufräumen beim Nutzer (nur auf Wunsch): `config.json.vor-ruecksetzung.bak` im Roaming-Ordner, die gesicherte alte EXE
  `HdrPilot-vorher.exe.bak` im Programmordner. Die 6 Desktop-Verknüpfungen „(HDR)“ hat der Nutzer am 07.10. selbst gelöscht.
- Setup: Aktualisieren über eine laufende ältere Version lief am 2026-10-12 beim Nutzer live: 1.2.0 lief, der Setup
  beendete die App, ersetzte sie durch 1.3.0 und startete sie neu. Neue Version: `tools\publish.ps1`, dann Setup.
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
