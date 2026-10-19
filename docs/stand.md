# Zwischenstand (Übergabe)

Zuletzt aktualisiert: 2026-10-18. Dieses Dokument hält fest, wo die Arbeit steht, damit sie jederzeit nahtlos
fortgesetzt werden kann. Maßgeblich für Nachweise ist `GATES.md`, für Begründungen `docs/entscheidungen.md`.

## Neue Wünsche des Nutzers (2026-10-18), noch nicht begonnen
Der Nutzer will sie beim nächsten Mal angehen, aber erst auf sein Zeichen. Vorher die Arbeit an der Werkzeugleiste
(unten) abschließen oder mit ihm die Reihenfolge klären.
1. **Deinstallierte Spiele:**
   - Bei deinstallierten Spielen soll man die Einstellungen des Spiels mit entfernen können. Das geht über das
     Kontextmenü und ganz unten im Detailbereich.
   - In den Einstellungen gibt es eine Option, deinstallierte Spiele auszublenden.
   - Entfernen heißt hier: die Einstellungen löschen, die HDRpilot für das Spiel gespeichert hat. Es wird nichts am
     Spiel oder am Launcher angefasst.
   - Vorher klären:
     - Wie erkennt HDRpilot „deinstalliert“ heute? Gibt es dafür schon einen Zustand im Scanner bzw. in der Bibliothek?
     - Muss das Entfernen eigens bestätigt werden?
     - Bleiben eigene Bilder und Symbole erhalten oder gehen sie mit?
2. **Quiver Launcher:**
   - Spiele des „Quiver Launchers“ sollen eingebunden werden, also erkannt, gestartet und mit HDR automatisch versehen.
   - Noch nicht recherchiert: wo der Launcher Installationen ablegt, ob es ein Manifest oder eine Datenbank gibt, und
     wie Spiele gestartet werden (URI oder EXE).
   - Erst recherchieren, nur lesend am Rechner des Nutzers, dann wie die anderen Launcher-Scanner aus Phase 2 umsetzen.
   - Grenzen: keine GPL-Abhängigkeiten, kein Netzwerk.

## Pausiert am 2026-10-16: Umsetzung „Showcase Hero“ (Phase 10)
Auf Wunsch des Nutzers gesichert. Alles ist committet, nichts läuft.
Installiert ist weiter 1.4.0. **1.5.0 nur nach ausdrücklichem Ja des Nutzers installieren;** dabei müssen die
Einstellungen der Vorversion erhalten bleiben (vorher/nachher vergleichen).

**Zweige und Arbeitsordner:**
- **`feature/showcase`** (`.worktrees/s11-ui`, 09824b7): die Neugestaltung mit allen Prüf- und Korrekturrunden.
  - **Zuletzt am 2026-10-16:**
    - Speicher nach dem Schließen, helle HDR-Fassung nur im Ruhezustand, Messgrenze im GPU-Gate (37ef909).
    - Helle Ebene nach verzögertem HDR-Moment; phase10-gpu meldet bei Fokusverlust UMGEBUNG statt FEHLER (52e7d1e).
    - Weiches Überblenden der hellen Ebene (8f796fc, 09824b7).
  - **Gegenprüfung:** ohne offene Funde, nur ein kosmetischer Hinweis. Wechselt die Effektstufe mitten im Übergang,
    behält die Ebene die Dauer ihres ersten Übergangs.
  - **GPU-Gate:** lief am 2026-10-16 mit eingeschaltetem Monitor einmal komplett grün (PHASE10_GPU_OK). Gemessen:
    - Liste in Ruhe: 1,2 % GPU.
    - HDR-Moment: Spitze rund 24 % GPU.
    - Minimiert: 0,2 % CPU.
- **`feature/hero-height`** (`.worktrees/s11-hero`, 9e51e95, auf 37ef909): Ziehgriff für die Höhe des Titelbereichs in
  der Liste (Wunsch 2026-10-16).
  - **Höhe und Speicherung:** Standard ein Drittel der Fensterhöhe, 186 px bis H − 416 (abzüglich sichtbarer Hinweise).
    Gemerkt als `settings.libraryHeroRatio`.
  - **Bedienung:**
    - Doppelklick oder Eingabetaste setzt zurück, Esc bricht das Ziehen ab.
    - Pfeiltasten 24 px, Bild auf/ab 96 px, Pos1/Ende.
    - UI-Automation über RangeValue und Invoke.
    - Kein Einklappen beim Scrollen mehr.
  - **Lage des Griffs:** mittig auf der Unterkante des Titelbilds. Über der Werkzeugleiste ist so viel Luft wie darunter:
    Liste 10 px, Raster 22 px. Der Titelbereich des Rasters gibt in niedrigen Fenstern nach.
  - **phase10-ui:** nimmt die Skalierung aus der DPI des Fensters.
  - **Prüfung:** von zwei Blickwinkeln geprüft, 9 Funde behoben.
- **`feature/toolbar-wrap`** (`.worktrees/s11-bar`, eaff8e2, enthält feature/hero-height): Die Werkzeugleiste bricht in
  gefüllte, ausgeglichene Zeilen um (`ToolbarFlowPanel`), alle Elemente mit Text, ohne „Filter“-Menü.
  - **Status:** ungeprüft, der Lauf wurde vor der Prüfung angehalten. Inhaltlich überholt durch die neuen Anmerkungen
    des Nutzers (unten). Nicht ohne Entscheidung zusammenführen. Das Umbrechen kann als Rückfall für schmale Fenster
    weiterverwendet werden.

**Offene Entscheidung des Nutzers: Werkzeugleiste über der Liste.** Nichts davon einbauen, bevor er geantwortet hat.
Seine Anmerkungen (2026-10-16, wörtlich):
1. „Die "Automatik", "Neu" und "HDR-Fähig" Buttons zum Filtern müssen nicht so groß sein, sie können so klein bleiben
   wie der "Neu Laden" Button. Erklärungen können bei 1-2 sekündigem Mouse-Over angezeigt werden.“
2. „Eine sinnvolle Lösung zum Aus- und Einblenden der Leiste hätte ich gerne von dir. Sie muss cool und zeitgemäß
   sein.“
3. „Die Anpassung der Icons an schmale Fenster muss smooth sein und optisch sauber aussehen, es kann bei Bedarf bei
   schmalen Fenstern zweizeilig werden, aber der Fokus muss auf einzeilig liegen.“

Vorschläge einer Entwurfsrunde liegen in `docs/design/showcase-hero/werkzeugleiste-vorschlaege.md`: vier Konzepte
(Bewegung, Bedienung, Platz, fließende Anpassung) und eine Bewertung mit Empfehlung („Fließleiste mit Lichtnaht“).

**Entscheidungen des Nutzers am 2026-10-17:**
- **Vorgehen:** Erst eine interaktive Vorschau ansehen, dann entscheiden. Die Vorschau liegt neben dem Prototyp,
  `docs/design/showcase-hero/prototyp/leiste.html`. Sie ist nur ein Entwurf, der App-Code bleibt unverändert.
- **Ansicht-Umschaltung und „Spiel hinzufügen“:** nur als Symbol.
- **Zahlen der Filter:** als Zähler-Plakette am Knopf, nicht nur im Tooltip. Die Plakette bleibt innerhalb der 40×40,
  damit die Luft über der Leiste gleich bleibt.
- **Strg+F bei eingeklappter Leiste:** klappt sie dauerhaft aus.

**Abgenommen am 2026-10-17:** „Ansonsten gefällt es mir gut und kann so umgesetzt werden“. Vorher kam eine Korrektur
dazu: Die Lasche sitzt 8 px über der Leiste, so viel wie der Abstand der Knöpfe (8ddfdb2). Die Kriterien stehen in
G10.5. Seit 2026-10-17 wird sie umgesetzt:
- **Zweig und Ordner:** `feature/library-toolbar` in `.worktrees/s12-bar`, ausgehend von `feature/showcase` 09824b7.
- **Hineingeführt:** `feature/toolbar-wrap` mit `feature/hero-height` und der Wackeltest-Zweig.
- **Danach:** Prüfung aus drei Blickwinkeln, Korrektur und Gegenprüfung.

**Pausiert am 2026-10-17 auf Wunsch des Nutzers.** Stand auf `feature/library-toolbar`
(d043930, Arbeitsordner sauber):
- **Zusammenführung fertig:**
  - 117e05b: toolbar-wrap mit hero-height.
  - ae6a7ef: Wackeltest-Zweig.
  - 04dd8fb: EffectReviewTests laufen allein.
  - Ohne Textkonflikte; `_scrimEdge` und alle Überblend-Korrekturen sind erhalten.
- **Umsetzung fertig:** ac5fe53, a6d35d4, 1cf6cec, d043930.
  - Neue Dateien: ToolbarLayout, MotionChannels, ToolbarChoreography, ToolbarPanel, ToolbarToggle, ToolbarTip,
    FilterToggle, FilterGlyph, `MainWindow.Toolbar.cs` und `LibraryViewModel.Toolbar.cs`, dazu Tests.
  - ToolbarFlowPanel ist entfernt.
  - Mutationsprüfung: 17 von 17 erkannt.
  - Alle erlaubten Gates gaben Exit 0, `phase10-ui` misst am echten Bildschirm.
  - **Ausnahme:** `test.ps1` scheitert gelegentlich an zeitabhängigen Tests, die mit der Leiste nichts zu tun haben.
    Laut Zusammenführung tritt das auch auf 09824b7 auf. Klären, bevor 1.5.0 gebaut wird.
- **Abweichungen laut Bericht:**
  - Im Regal sinken Logo und Knöpfe eingeklappt um 32 statt 40 px.
  - Eine Zeile kommt beim Ziehen bei 838 zurück, wie in der Vorschau.
  - „Reduziert“ blendet zwischen Lasche und Kapsel über ein Bitmap über.
  - Die Abstände betragen bei 110 % 9 Gerätepixel, also 8,15 DIP.
  - Die Automatik-Karte ist jetzt eine UIA-Gruppe.
  - Die Verweise in E89 und der README auf `leiste.html` und `werkzeugleiste-vorschlaege.md` stimmen erst nach dem
    Zusammenführen mit phase5/release.
- **Noch offen:** Die Prüfung aus drei Blickwinkeln (Funktion, Aussehen und Bewegung, Treue zur Vorschau) wurde beim
  Anhalten abgebrochen. Neu starten, danach Korrektur und Gegenprüfung. Dann geht es weiter mit „Nächste Schritte“
  ab Punkt 3, wobei `feature/library-toolbar` an die Stelle von `feature/showcase` tritt.
- **Regal, Lasche und angehobene Kachel:** Die Lasche liegt in der 22-px-Fuge zwischen Leiste und Kachelreihe. Beim
  Überfahren hebt sich die rechte Kachel an, ihre obere rechte Ecke reicht dann unter die Lasche.
  - **Entscheidung des Nutzers 2026-10-19:** „Den Vorschlag notieren, erstmal so lassen.“
  - **Begründung der Korrektur:** Über der Leiste stieße die Lasche bei kleinem Titelbereich an die Automatik-Karte;
    unter den Kacheln wäre sie schlecht anklickbar.
  - **Falls es später stört:** Die angehobene Kachel könnte die Ecke unter der Lasche frei lassen. Die Lasche könnte
    beim Überfahren der Kachel kurz ausblenden. Oder die Kachelreihe hebt sich nicht nach oben in die Fuge.

Der Zweig `claude/modest-thompson-62a15c` (87699e4) stammt aus der Sitzung des Nutzers zum Wackeltest. Er lässt die
Bindungsfehler-Prüfungen allein laufen (`ShellSerialCollection`, `BindingErrorRecorder`) und baut auf 5731db1 auf.
Beim Zusammenführen mitnehmen.

**Nächste Schritte, in dieser Reihenfolge:**
1. **Vorschläge vorlegen,** am besten mit einer Vorschau. Die Wahl des Nutzers abholen.
2. **Werkzeugleiste umsetzen und prüfen:**
   - auf `feature/toolbar-wrap` oder neu von `feature/hero-height` aus;
   - Prüfung aus zwei Blickwinkeln;
   - Bilder bei 1280×800, 1366×768, 1920×1080 und 3440×1440, hell und dunkel, mit Pixelmessung der Abstände.
3. **In `feature/showcase` zusammenführen.** In `HeroStage.cs` ist ein Konflikt möglich (`_scrimEdge` aus hero-height
   gegen die Überblend-Korrekturen).
4. **Abschlussprüfung:**
   - build, voller test, phase10-ui, phase10-settings-ui, phase3-idle;
   - phase4-ui, phase6-ui, phase8-icons, phase9-autohdr-ui;
   - phase7-setup gegen die Testwurzel, phase4-resources, phase5-readme;
   - phase10-gpu bei eingeschaltetem Monitor.
5. **Abschluss auf `phase5/release`:**
   - Merge in `phase5/release`, G10-Nachweise in `GATES.md` eintragen;
   - Version 1.5.0 setzen, Setup bauen, **nicht installieren**;
   - Berichte der Läufe nach `docs/design/showcase-hero/` übernehmen;
   - `main` vorspulen.
6. **Aufräumen:** die Arbeitsordner s11-core, s11-theme, s11-views und nicht mehr gebrauchte ältere entfernen (vorher
   ansehen).
7. **Mit dem Nutzer:** G10.9 (GPU), G10.11 (Sichtabnahme), G9.8 (Auto-HDR-Livetest), echtes Tray-Gate.

**Bekannte Wackeltests:**
- `Main_window_has_no_binding_errors_in_any_view`: fängt Bindungsfehler parallel laufender Tests ein und ist für sich
  allein grün. Der Nutzer hat dafür am 2026-10-16 eine eigene Sitzung gestartet („Fix flaky binding-error test
  interference“). Deren Ergebnis ist unbekannt; den Stand prüfen, bevor man daran arbeitet.
- Selten: `HdrListServiceTests.Empty_password_reuses_the_stored_one_for_the_same_user_only` und
  `ArtworkReviewTests.Steamgriddb_images_count_only_while_steamgriddb_is_on`.

## Vorgeschichte: Neugestaltung der Oberfläche (Wunsch des Nutzers 2026-10-14)
Stand der Designphase; die Umsetzung steht oben unter „Pausiert am 2026-10-16“. Die Vorschauseite war privat, die
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
