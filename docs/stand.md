# Zwischenstand (Übergabe)

Zuletzt aktualisiert: 2026-10-14. Dieses Dokument hält fest, wo die Arbeit steht, damit sie jederzeit nahtlos
fortgesetzt werden kann. Maßgeblich für Nachweise ist `GATES.md`, für Begründungen `docs/entscheidungen.md`.

## In Arbeit: Neugestaltung der Oberfläche (Wunsch des Nutzers 2026-10-14)
Noch kein Code, erst die Designrichtung. Die Vorschauseite war privat, die
Quellen der Entwürfe lagen nur lokal; beides ist nicht Teil des Repositorys.

**Vorgaben des Nutzers:**
- **Stil:** deutlich cooler und moderner. Die erste Runde schlichter Entwürfe war ihm „nicht professionell und
  detailliert genug, Effekte viel zu dezent“. Gewünscht sind kräftige, gut sichtbare Animationen.
- **Farben:** Duoton und Tritonus (zwei oder drei Farben) plus Vorgaben. Sie färben das ganze UI, das Tray-Symbol und
  das Fenstersymbol.
- **Hell und dunkel:** beide im neuen Stil, weiter automatisch nach Windows.
- **Leistung:** Bei geschlossenem oder minimiertem Fenster keine Kosten. Bei offenem Fenster darf die Grafikkarte nie
  an 100 % kommen. Dafür ist ein Messgate für Grafikkarte und Prozessor geplant.
- **Echte Nit-Werte des Monitors** statt Platzhaltern (Wunsch 2026-10-14):
  - Quelle: DXGI `IDXGIOutput6::GetDesc1`, nur im eigenen Prozess. Gelesen werden Spitzen-, Vollbild- und
    Minimalhelligkeit, Farbtiefe und ob HDR aktiv ist.
  - Gelesen wird nur bei HDR-Wechsel oder Bildschirmwechsel.
  - Gemessen am 2026-10-14 am MAG 341C OLED: Peak 456, Vollbild 254, Minimum 0,0000 Nits, 10 Bit. Das passt zu
    „True Black 400“. Es sind die Werte, die der Monitor meldet (EDID oder HDR-Kalibrierung), keine Messung.

**Entscheidungen 2026-10-14:**
- **Richtung:** „Showcase Hero“, eine Startseite im Konsolenstil.
- **Ansichten:** Liste (Standard, Pflicht), Regal und Raster; die gewählte Ansicht wird gemerkt.
- **Titelbilder:** Echte Bilder aus den lokalen Launcher-Caches, nur lesend. Steam liefert die Dateien in
  `appcache\librarycache\<appid>\`: `library_hero.jpg` hat 631 von 636 Spielen, `logo.png` 625, `library_600x900.jpg` 453.
  - Fehlt das Bild, erzeugt HDRpilot ein Ersatzbild aus dem Spielsymbol.
  - Per Rechtsklick lässt sich ein eigenes Bild wählen.
  - **SteamGridDB** gibt es optional, zuschaltbar mit eigenem API-Schlüssel und standardmäßig aus. Es füllt nur
    fehlende Bilder. Das ist die einzige Ausnahme von „nur lokal“ und gilt nur, wenn der Nutzer es einschaltet.
- **Bewegung:** nur allgemeine Ebenen, die mit jedem Bild funktionieren: Tiefenwirkung von Logo zu Bild, langsame
  Kamerafahrt, Farbschein aus den Bildfarben, Überblendung, HDR-Aufblühen. Keine handgemachten Animationen pro Spiel.

**Stand (gesichert 2026-10-14):** Der Nutzer hat den lokalen Prototyp „Showcase Hero v4“ mit „Sieht gut aus“
abgenommen und will die Umsetzung später.
- Alles für die Umsetzung liegt unter `docs/design/showcase-hero/`:
  - `README.md`: Spezifikation, Entscheidungen, Bildquellen, Effekte mit WPF-Primitiven, offene Punkte.
  - `prototyp/`: lauffähig mit `make-data.ps1` und `start.cmd`.
  - `entwurf-runde2/` und `berichte.md`.
- **Anmerkungen des Nutzers zum Prototyp (2026-10-14), beim nächsten Mal zuerst umsetzen;** Details im README,
  Abschnitt „Anmerkungen des Nutzers“:
  1. Raster bricht im Vollbild.
  2. Reihenfolge der Ansichten: Liste, Raster, Regal.
  3. Das „Maximum“ jeder Nit-Skala ist die höchste Nit-Zahl des Bildschirms bei aktivem HDR (456), nie 1000.
  4. In „Darstellung“ werden die Bereiche im Vollbild zu lang gezogen; Anordnung und Skalierung überarbeiten.
- **Seit 2026-10-15 in Umsetzung:**
  - Abnahmekriterien als Phase 10 in `GATES.md` (G10.1–G10.11), die Anmerkungen des Nutzers sind eingeschlossen.
  - Code auf dem Zweig `feature/showcase`, Arbeitsordner `.worktrees/s11-ui`. Teilzweige `feature/showcase-core`,
    `-theme` und `-views`.
  - Ablauf: Entwurf, drei Teile parallel, Zusammenführen, Feinschliff an Bildern bei drei Größen, Prüfung mit drei
    Blickwinkeln, Korrektur.
  - Der Nutzer hat dafür keine Installation freigegeben. Die Messung der Grafikkartenlast (G10.9) braucht einen
    eingeschalteten Monitor.

## Aktuell: HDRpilot 1.4.0 mit Windows Auto HDR ist beim Nutzer installiert
Phase 9 ist fertig bis auf den gemeinsamen Abschlusstest (G9.8). Nichts läuft im Hintergrund, alles ist committet.

**Neu in 1.4.0 (E88):**
- **Spiel-Tab, Abschnitt „BILD“:** Schalter „Windows Auto HDR“ mit Erklärung, klar getrennt von „HDR automatisch“.
  - Hinweis, dass Auto HDR nur beim Start über HDRpilot wirkt (Starten, Verknüpfung, Steam-Startoption).
  - Hinweis, wenn „HDR automatisch“ aus ist.
  - Bei Spielen mit eigenem HDR ausgegraut, aber nutzbar.
- **Spieleliste:** Abzeichen „AUTO HDR“.
- **Technik:** HDRpilot setzt den globalen Auto-HDR-Schalter von Windows direkt vor einem Start über HDRpilot und stellt
  ihn nach der Sitzung zurück, nach einem Absturz beim nächsten Start und beim Entfernen über den Setup. Eine Änderung
  durch den Nutzer (Windows oder Tray) bleibt. Werte pro EXE gibt es nicht, sie wirken laut Livetest unter 26H2 nicht
  (G9.2 gestrichen).
- **Steam:** Beim Einschalten für ein Steam-Spiel bietet HDRpilot an, die Steam-Startoption zu kopieren. Es schreibt
  nie selbst in Steams Dateien.
- **Tray-Menü:** Eintrag „Auto HDR“ mit Häkchen, zeigt und schaltet den Windows-Schalter.

**Installation (2026-10-13)** per WMI außerhalb des Containers, mit Zustimmung des Nutzers (Bedingung: alle Gates
grün):
- Ergebnis: Exit 0, 1.3.0 → 1.4.0, die App läuft wieder.
- Autostart und Eintrag unter „Installierte Apps“ (1.4.0) stimmen, der globale Auto-HDR-Schalter ist unverändert aus.
- Einstellungen und Spiel-Einstellungen sind unverändert: 651 Spiele, 28 mit HDR automatisch.
- Protokoll: Start mit der echten Konfiguration, 0 Fehler.
- Einzige rote Prüfzeile vorher: `phase5-publish.ps1` → `hdr status` exit 3. Der Monitor war aus, es gab keine
  aktiven Bildschirme; alle anderen 24 Punkte waren grün.

**Offen (nur mit dem Nutzer):**
- **G9.8 Abschlusstest am echten System:** ein freigegebenes Spiel ohne eigenes HDR (TUNIC war im Livetest die
  Positivprobe) über HDRpilot starten.
  - Erwartung: Auto-HDR-Regler in der Game Bar bedienbar, danach Schalter wieder aus.
  - Dasselbe mit der Steam-Startoption.
  - Den Tray-Eintrag „Auto HDR“ ausprobieren.
  - `phase6-traymenu.ps1` mit echter Maus laufen lassen (nur am Desktop, mit Zustimmung).
- **Zum Nachprüfen:** The Plucky Squire und Aliens: Dark Descent zeigten den Regler grau. Möglicherweise lag das am
  veralteten Game-Bar-Fenster: das Fenster ganz schließen und neu öffnen.
- **Optional:** Die Steam-Startoption direkt setzen statt kopieren wäre eine neue Funktion, die in Steams
  `localconfig.vdf` schreibt. Bisher verändert HDRpilot bewusst keine Launcher-Dateien.

**Unterlagen:**
- Livetest: G9.6 in `GATES.md`.
- Berichte der Agenten: `docs/uebergabe/auto-hdr-ablauf-2026-10-12.md` und `docs/uebergabe/auto-hdr-ablauf-2026-10-13.md`.
- Ursprünglicher Entwurf: `docs/uebergabe/auto-hdr-entwurf.md`.
- Recherche: `docs/recherche/auto-hdr-rtx-hdr.md`. RTX HDR bleibt zurückgestellt.

## Vorher abgeschlossen
- Phase 8 ist fertig (1.3.0, am 2026-10-12 installiert).
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
