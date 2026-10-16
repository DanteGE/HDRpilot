# Umsetzung „Showcase Hero“: Ansichten (Teil VIEWS)

Stand: 2026-10-16, Zweig `feature/showcase-views`. Grundlage: genehmigter Prototyp v4 mit den Anmerkungen des Nutzers
(Ansichten Liste, Raster, Regal; Nit-Skala bis zur Spitze; Raster bei jeder Breite sauber).

## Was dieser Teil liefert

- **Hauptfenster neu** (`MainWindow.xaml`, `Themes/Library.xaml`, `Views/Library/**`): Kopf mit Bildschirm-Kapsel
  (echte Nit-Werte, Skala von 0 bis zur Spitze, nie 1000), Titelbereich je Ansicht, Werkzeugleiste, drei Ansichten,
  Detailbereich bzw. Schublade, Statuszeile.
- **Ansichten** in der Reihenfolge Liste, Raster, Regal; Liste ist der Standard. Die Wahl steht in
  `settings.libraryView` (Liste: nichts gespeichert), die Vorschau der Liste in `settings.libraryThumbnail`.
  Strg+1/2/3 wechseln, nur die aktive Ansicht hat Daten und eine Auswahl-Bindung.
- **Liste**: virtualisiert, Spalten Spiel/Quelle/Zuletzt gespielt/HDR/HDR automatisch, sortierbar über die Kopfzeile
  (gleiche Speicherung wie das Feld „Sortierung“), Schalter „HDR automatisch“ je Zeile, schlanker Titelbereich
  (180–240 px, beim Scrollen 120 px). Spalten fallen bei Platzmangel weg: erst Quelle, dann HDR, dann Zuletzt gespielt.
- **Raster**: eigenes `VirtualizingTilePanel`; Spaltenzahl und Kachelgröße aus `RasterLayout` (Kachel 150–220 px,
  gerade Pixel, 2:3, links bündig, rechts nur Rundungsrest). Bei 3440 × 1440 vierzehn bis siebzehn Spalten je nach
  Detailbereich, keine gestreckten Kacheln.
- **Regal**: großer Titelbereich mit Starten/Desktop/Startmenü, Karte „Automatik“, Kachelreihe mit Blättern,
  Detailbereich als Schublade (Taste I, Esc schließt).
- **Detailbereich** mit allen bisherigen Funktionen und dem Abschnitt „BILDER“ (Titelbild, Logo, Cover, Symbol;
  Klick öffnet „Bild wählen…“ bzw. beim Symbol das bisherige Menü „Symbol ändern…“), neu „Installationsordner öffnen“
  im Kontextmenü.
- **Dialog „Bild wählen…“** (`ArtworkPickerDialog`, `ArtworkPickerViewModel`): Reiter Titelbild/Logo/Cover,
  Kandidaten des Bilddienstes plus „Automatisch“, „Eigene Datei…“ (in Prüfläufen über `HDRPILOT_TEST_ART_FILE`),
  „Zurücksetzen“. Übernehmen schreibt `games[].artwork` und löscht ersetzte eigene Bilder.
- **Effekte** (`Infrastructure/Effects/**`): `MotionController` steuert alle Dauer-Effekte (Ken Burns, Strahlen,
  Lichtpunkte, Puls der Kapsel, Drehen beim Einlesen, Auswahlring, Glanz) nach `EffectPolicy`: Storyboards entstehen erst,
  wenn sie laufen dürfen, haben höchstens 30 Bilder pro Sekunde, halten im Hintergrund an der Stelle an, werden minimiert,
  unsichtbar, beim Spielen und bei geänderter Stufe entfernt und beim Schließen freigegeben. Übergänge springen in einem
  unsichtbaren Fenster sofort ans Ziel. `HdrMomentPlayer` spielt den HDR-Moment (Lichtstreifen 1,6 s, Linie, Blitz,
  leuchtender Rand 1,8 s, helle Fassung, bis 160 Partikel in einem `DrawingVisual`, nur 2,5 s am Bildtakt);
  „Reduziert“ blendet nur die helle Fassung höchstens 0,3 s, „Aus“ spielt nichts. Keine BlurEffect, DropShadowEffect,
  Shader oder Mischmodi (Quelltextprüfung in `EffectViewsTests`).

## Bewusste Abweichungen vom Prototyp

1. **Regal bei hohen Fenstern**: Titelbereich `clamp(H × 0,62; 500; max(660; H − 462))`, damit die Kachelreihe bei
   1440 px Höhe ganz sichtbar bleibt.
2. **Werkzeugleiste**: Stufen wie `fitToolbar`, aber die Filter-Chips falten zuletzt (Stufe 6, nach der Sortierung in
   Stufe 5), damit sie bei der Standardgröße direkt schaltbar bleiben (phase4-ui); Stufe 7 (zwei Zeilen) für Fenster um
   1000 px, die der Prototyp nicht hatte.
3. **Detailbereich mit Trenner** wie bisher (Aufteilung gemerkt), aber höchstens 560 px breit (Prototyp 340–396 px).
   Eine alte Aufteilung (etwa 0,42 aus der bisherigen Oberfläche) wirkt damit bei breiten Fenstern nicht mehr wie ein
   halbes Fenster; Doppelklick auf den Trenner setzt auf 23 % zurück.
4. **Helle Fassung** beim HDR-Moment: Solange der Bilddienst keine vorab berechnete helle Fassung liefert
   (`GameArtwork.HeroBoost` ist noch null), legt der Titelbereich ein warmes Licht über das Bild (WPF hat keine
   Helligkeitsfilter).
5. **Glanz über dem Namen** bewegt eine Maske statt der Textfarbe; Auswahlring und Glanz werden im Code erzeugt und über
   Bindungen an das Fenster gelesen. Als Ressource oder Bindungsquelle in Vorlagen friert WPF sie ein, dann ließen sie
   sich nicht animieren (Absturz beim ersten Aktivieren, von `EffectViewsTests` gefunden).
6. **Logo** ohne durchsichtigen Rand (`LibraryArt.TrimTransparent`, wie `trimLogo` im Prototyp) und nach Bildpunkten
   statt dpi-Angabe der Datei skaliert.
7. **Schublade im Regal** fast deckend (97 %) statt Glas mit Weichzeichner.
8. Polarlichter aus `app.css` sind nicht umgesetzt (nicht in der Effektliste von G10.6).

## Offene Punkte für andere Teile

Stand nach der Zusammenführung (G10.10): Bilddienst (Kandidaten, eigene Bilder, helle Fassung), Helligkeitsdienst und
`ContentUnderTitleBar` wirken; `Hp.Font.Display` hat in `Controls.xaml` und den Token-Dateien denselben Wert. Offen
bleiben nur die Abnahme am echten System und phase10-gpu.ps1 mit eingeschaltetem Bildschirm (siehe E89).

- **Bilddienst (CORE, G10.3)**: `GetCandidatesAsync` liefert noch keine Kandidaten, `SaveCustomImage` wirft noch
  (eigene Bilder lassen sich wählen, aber noch nicht übernehmen), `HeroBoost` fehlt. Die Ansichten nutzen alles, sobald
  es da ist.
- **Helligkeit (CORE, G10.7)**: `CreateDisplayLuminanceService` liefert noch `NullDisplayLuminanceService`; die Kapsel
  zeigt dann „—“. Mit Attrappen ist die Anzeige getestet (Skala bis zur Spitze).
- **Thema (THEME)**: `ContentUnderTitleBar` wirkt erst mit dem neuen Fensterrahmen; bis dahin liegt der Inhalt unter der
  Titelleiste. `Controls.xaml` überschreibt `Hp.Font.Display`, deshalb nutzt die Bibliothek `Lib.Font.Display`.
- **Speicher der Bilder**: Die Bibliothek hält höchstens 96 Cover selbst; das Zwischenspeichern im Bilddienst gehört zu
  CORE.
- **README**: Die Schaltfläche heißt sichtbar „Starten“, ihr Name für Bildschirmleser bleibt „Mit HDR starten“
  (phase5-readme grün).
- **Abnahme (G10.11)**: Aussehen, Effekte und Vollbild 3440 × 1440 am echten System durch den Nutzer. phase10-gpu.ps1
  lief ohne sicheren Nachweis, dass der Bildschirm eingeschaltet war; die Messung gehört bei eingeschaltetem Bildschirm
  wiederholt.

## Prüfungen

- Tests: `Effects/*` (Steuerung, HDR-Moment, Partikel, Fenster, Quelltextprüfung), `Library/Showcase*` (Anordnung,
  ViewModel, Fenster), angepasste `MainWindowLayoutTests`, `CustomIconTests`, `LibraryViewModelTests`.
- Gates: `phase10-ui.ps1` (UI-Automation und Bildschirmfotos 1280 × 800, 1920 × 1080, 3440 × 1440, hell und dunkel),
  `phase10-gpu.ps1` (Grafikkartenlast; UMGEBUNG ohne aktiven Bildschirm); die bisherigen UI-Gates laufen unverändert.
