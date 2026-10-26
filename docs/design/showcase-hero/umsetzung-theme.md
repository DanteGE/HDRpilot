# Umsetzung THEME: Darstellung, Symbole, Einstellungen (Phase 10)

Stand des Zweigs `feature/showcase-theme`. Grundlage für README und E89 im Integrationsschritt (G10.10). Alle
Bezeichnungen stehen wörtlich so in `ShellStrings.resx`.

## Was der Nutzer sieht

### Einstellungen im Showcase-Stil

- Links die **Navigation**: Kopf „Einstellungen“ mit der Versionsangabe (`AppVersion.DisplayText`, wie bisher im
  Fuß), darunter **Allgemein**, **Bildschirme**, **Automatik**, **Suchordner**, **Hilfsprozesse**, **Darstellung** (mit
  **Farben**, **Bilder**, **Symbole und Tray**), **Erweitert**, **Protokolle**. Unten die Karte „Bibliothek“ mit
  „{Anzahl} Spiele · {Anzahl} mit HDR automatisch“ und **Spielebibliothek öffnen** (weicht bei Fensterhöhen unter 720 px).
- Rechts ein einziger scrollender Inhalt mit drei Gruppen: **Allgemein** (Start und Hinweise, Bildschirme, Automatik,
  Suchordner, Hilfsprozesse ausschließen), **Darstellung** (Farbschema, Live-Vorschau, Thema, Effekte, Bilder, Symbole und
  Tray) und **Erweitert** (Notlösung, Protokolle, HDR-Liste (PCGamingWiki)). Die Navigation springt zum Bereich und zeigt,
  welcher Bereich gerade oben steht.
- Raster je Gruppe (Anmerkung 4): Bereiche höchstens 760 px breit (DIP), Spalten mindestens 520 px, Abstand 24 px, höchstens
  drei Spalten; der Rest bleibt als Rand links und rechts. Bei 1280×800 eine Spalte, bei 1920×1080 zwei, bei 3440×1440
  drei. Bereiche fließen in die jeweils kürzeste Spalte (keine Lücken). Gruppenköpfe stehen bündig über dem Raster.
- „Speichern“ und „Abbrechen“ unten gelten wie bisher für Allgemein und Erweitert. Die Darstellung wirkt und speichert
  sofort; daneben steht **Wird sofort angewendet** und **Standard** (setzt die ganze Darstellung zurück). „Abbrechen“ nimmt
  Darstellungs-Änderungen nicht zurück.

### Seite „Darstellung“

- **Farbschema** („Jede Farbe hat eine eigene Aufgabe“): Umschalter **Duoton** | **Tritonus**, Knopf „Akzent und
  Atmosphäre tauschen“, drei Farbfelder **Akzent** („Schaltflächen, Auswahl“), **Atmosphäre** („Hintergrund, Leuchten“),
  **Glanz** („HDR-Moment, Lichtstreifen“, im Duoton „automatisch“ aus der Atmosphäre), Verlaufsbalken, **Vorlagen** (8: Abendrot,
  Cyber, Neongrün, Royal, Gletscher, Synthwave, Glut, Polarlicht mit DUO/TRI; 8 nebeneinander oder 4×2), Zeile **Textfarbe
  auf Akzentflächen automatisch** mit den Kennzeichen „Auf Akzent“ und „Akzenttext“ (Format „{Wert}:1 · {Note}“, z. B. „5,7:1 · AA“; Wert abgerundet,
  grün ab AA, gelb bei „AA groß“, rot darunter) und **Hintergrund folgt dem Spiel**.
- Eigene Farbe: Klick auf ein Farbfeld öffnet den eigenen **Farbwähler** (Feld für Sättigung und Helligkeit, Farbton-Leiste,
  Hex-Feld, **Übernehmen** / **Abbrechen**; Pfeiltasten und Hex-Eingabe gehen ohne Maus; Esc schließt nur den Farbwähler).
  Jede eigene Farbe oder „tauschen“ macht aus der Vorlage „Eigene Farben“. Duoton/Tritonus behält die Vorlage.
- **Live-Vorschau**: echte Bilder der vier zuletzt gespielten Spiele (Titelbild, Logo oder Name, Quelle, „vor 2 Tagen“),
  Schalter **HDR** spielt einen kurzen HDR-Moment nur in der Vorschau (schaltet kein HDR), Kapsel „HDR AUS“/„HDR EIN“ mit
  der echten Spitze des Bildschirms („{n} nits“, sonst „—“, nie ein fester Wert).
- **Thema**: **Automatisch** („wie Windows“), **Hell**, **Dunkel**; Unterzeile „Folgt Windows · gerade dunkel“, „Immer hell“
  oder „Immer dunkel“.
- **Effekte**: **Voll**, **Reduziert**, **Aus** als Karten mit Vorführung (die Karte „Voll“ bewegt sich nur nach den Regeln
  der Effekte), Kennzeichen „WIRKSAM“ an „Reduziert“, wenn Windows-Animationen aus sind; Schalter **Beim Spielen anhalten**,
  **Im Hintergrund anhalten**, **Windows-Animationseffekte beachten** (Unterzeile mit dem Windows-Zustand), **Nach dem Start
  in den Infobereich**.
- **Bilder**: **Lokal · Launcher-Caches** (Kennzeichen HAUPTQUELLE, „Immer aktiv“) mit Tabelle genutzt/Spiele und „+n“
  (gefunden, aber nicht genutzt; Balken nur bei breiter Tabelle), **Erzeugte Bilder** mit je einem echten Beispiel,
  **SteamGridDB (online)** (OPTIONAL, Schalter Standard aus, Schlüsselfeld, **Prüfen**, Stand des Netzwerkzugriffs, Hinweis
  DPAPI) und „Einzelnes Spiel: Rechtsklick › „Bild wählen…“ oder „Symbol ändern…““.
- **Symbole und Tray**: Taskleiste **Hell** | **Dunkel** (nur Vorschau, Standard wie Windows), Kacheln „HDR aus“,
  „HDR ein“, „Pausiert“, „Spiel läuft“ (der aktuelle Zustand umrandet), „Echte Größe · aus / ein“ in 16/20/24/32 px, das
  Tray-Menü als Vorschau (mit „Spitze {n} · Vollbild {n} nits · {n} Bit“, wenn bekannt) und eine Taskleiste mit Fenster- und
  Tray-Symbol.

### Farben der ganzen App

- Alle Farb-Ressourcen (`Theming/ThemeKeys.cs`) werden aus der Palette berechnet (`PaletteEngine`, `ThemeResources`) und
  direkt hinter die Token-Datei gehängt; Wechsel von Vorlage, eigener Farbe oder Textfarbe blendet bei offenem Fenster 0,9 s
  weich über („Reduziert“ 0,3 s, „Aus“ sofort, hell/dunkel immer sofort); danach sind alle Pinsel wieder eingefroren.
- Kontrast: Text auf Akzentflächen mindestens 4,5:1 (automatisch dunkle Schrift oder Weiß; reicht es nicht, weicht die
  Fläche aus, nie die Textregel), Akzenttext und gedämpfter Text 4,5:1 auf Fläche und Hintergrund, Schalter, Fokus- und
  Auswahlring 3:1. Im hellen Thema werden Schalter dafür etwas dunkler.
- Hell/Dunkel/Automatisch: Automatisch folgt dem App-Modus von Windows (AppsUseLightTheme); die Taskleiste
  (SystemUsesLightTheme) bestimmt die Linienfarben der Symbole.
- Schriften: Titel „Bahnschrift“, Text „Segoe UI Variable Text“, Zahlen „Cascadia Mono“ (Windows-Schriften, nichts
  eingebettet).
- Keine Unschärfe- und Schatteneffekte mehr: Leuchten und Schatten sind vorab gezeichnete Verläufe.

### Tray- und Fenstersymbol

- Sonne über dem Horizont: **HDR aus** als Linie im Verlauf Akzent → Atmosphäre ohne Kachel (je Taskleiste auf 3:1
  gebracht), **HDR an** als gefüllte Kachel Akzent → Atmosphäre → Glanz, **Automatik pausiert** mit gelbem Pause-Abzeichen
  (ohne HDR graue Linie), **Spiel läuft** (HDR an, ein Spiel läuft) mit grünem Abzeichen; Pause geht vor.
- Unter 24 px eine vereinfachte Glyphe mit ganzzahligen, am Pixelraster ausgerichteten Strichen.
- Neu gezeichnet nur bei Wechsel von HDR, Pause, Spiel, Farbschema oder Taskleisten-Thema; nie nach Zeit. Das
  Fenstersymbol (16, 20, 24, 32, 48, 64, 128, 256 px in einer Symboldatei) wird erst gezeichnet, wenn ein Fenster offen ist,
  und je Zustand einmal. Setup-Fenster behalten das feste Programmsymbol.
- Tray-Menü im neuen Stil: Kopf mit Programmsymbol, „HDRpilot“, Statuspunkt und Zustandszeile (Zustand · Detail, z. B. „{Spiel} läuft“), darunter die Version; „HDR jetzt …“ hervorgehoben;
  Linien-Symbole; Kennungen und Reihenfolge unverändert.

## Speicherung und Formate

- `settings.appearance` (Felder aus `ShowcaseSettings.cs`): Vorlage „cyber“ usw. bzw. „custom“ mit `accent`, `atmosphere`,
  `shine` als „#RRGGBB“ (bei eigenen Farben werden alle drei gespeichert, der Glanz gilt nur im Tritonus), `paletteMode`
  „Duotone“/„Tritone“ (null: Standard der Vorlage), `theme` „System“/„Light“/„Dark“, `effectLevel` „Full“/„Reduced“/„Off“,
  Schalter wie im Gerüst. Wahl einer Vorlage löscht eigene Farben und die Anzahl. „Standard“ setzt den ganzen Abschnitt zurück.
- `settings.artwork.steamGridDb` schreibt nur der Schalter (über `ISteamGridDbService.SetEnabled`). Der Schlüssel wird
  erst mit „Prüfen“ gespeichert (gültig oder nicht prüfbar: gespeichert; ungültig: nicht).
- Token-Dateien `Themes/Tokens.*.xaml` sind die Ausgabe der Farbrechnung für den Auslieferungszustand; Test
  `ThemeTokenFileTests` vergleicht, `HDRPILOT_WRITE_TOKENS=1` schreibt sie neu.

## Prüfungen

- `tools/gates/phase10-settings-ui.ps1` → `PHASE10_SETTINGS_UI_OK` (UI-Automation an einer Testinstanz, Raster bei den
  Größen des Bildschirms und maximiert, 24 Bilder `einstellungen-{allgemein,darstellung,darstellung-bilder,darstellung-tray}`
  in 1280×800, 1920×1080, 3440×1440, dunkel und hell). Drückt nie „Prüfen“.
- Bildschirmfotos: `--snapshot … --snapshot-scenes einstellungen` (Zustände „allgemein“, „darstellung“, „darstellung-bilder“,
  „darstellung-tray“; warten auf Bilder, springen zum Anker).

## Offene Punkte für die Integration

Stand nach der Zusammenführung (G10.10): README und E89 sind ergänzt; Abdeckungstabelle, Beispiele, Nit-Werte und helle
Fassung kommen aus den echten Diensten.

- README: Abschnitt Einstellungen um die Navigation und die Seite „Darstellung“ ergänzen (Bezeichnungen oben), „Abbrechen“
  gilt nicht für die Darstellung. Tray-Symbol beschreiben (Linie bei „HDR aus“, Kachel bei „HDR an“, Abzeichen).
- E89: Raster-Zahlen (520/760/24/3), Kontrastregeln, Farbwechsel 0,9 s, Symbol-Zustände und „neu nur bei Wechsel“.
- Die Beispiele „Erzeugte Bilder“ und die Abdeckungstabelle zeigen, was `IArtworkService` liefert (CORE). Ohne Beispiele
  steht „Keine Ersatzbilder nötig: …“.
- Die Nit-Werte kommen aus `IDisplayLuminanceService` (CORE); bis dahin „—“.
- Die Vorschau zeigt die „helle HDR-Fassung“, sobald CORE `HeroBoost` liefert; vorher fällt nur der Schleier weg.
- Die Bibliothek (VIEWS) benutzt die neuen Schlüssel `Hp.Brush.ToggleOn`, `Hp.Brush.BackgroundGlow2`, `Hp.Brush.SuccessSoft`,
  `Hp.Brush.WarningSoft`, `Hp.Brush.DangerSoft` nach Bedarf; `Hp.Brush.AccentSolid` ist jetzt die lesbare Akzentfarbe (Text).
  `ChromeWindow.ContentUnderTitleBar` ist umgesetzt (Inhalt ab Zeile 0, Titel und Fensterknöpfe in `Hp.Brush.TextOnArt`),
  die Titelleiste ist 32 px hoch.

## Spielfarben (E94)

„Farben folgen dem Spiel“ (Schalter auf der Seite „Darstellung“, Standard aus) färbt die Akzente der Bibliothek in den
Farben des gewählten Spiels. Was die App dafür baut:

- **Auslesen auf Anfrage** über `IGameColorsSource` (`context.Artwork as IGameColorsSource`; `IArtworkService` bleibt
  unverändert): `TryPeek` nur aus dem Speicher, `GetGameColorsAsync` aus dem Speicher, aus `colors` in `art\index.json` oder
  einmal auf dem Bilder-Thread berechnet (`ArtworkService.GameColors.cs`). Fragen nur das Bibliotheksfenster bei
  eingeschaltetem Schalter und die Seite „Darstellung“ für ihr Vorschauspiel; nie `LoadArtwork`, Kacheln, Kontaktbogen,
  Bildschirmfotos oder die Vorschau in „Bild wählen…“. Analyse, Einpassen und Ableiten liegen ohne WPF in `HdrPilot.Core`
  (`ColorScience`, `GameColorAnalysis`, `GameColorMapping`).
- **Farbebene** `GameAccentLayer`: ein Wörterbuch an `MainWindow.Resources.MergedDictionaries[0]`, nur solange der Schalter an
  ist und kein hoher Kontrast gilt. 14 bleibende Pinsel (`BrushKeys`: TextOnAccent, Accent, AccentShine, AccentSolid,
  AccentText, AccentSoft, AccentStrong, AtmosphereSoft, Shine, SelectionFill, SelectionRing, FocusRing, HdrOn, ToggleOn;
  19 Farben) und 11 Farbwerte (`ColorKeys`: C1–C3, OnAccent, AccentText, Accent1–3, Glow, Ambient2, Ambient3) in einem
  Kind-Wörterbuch. Bewusst beim Schema (`ExcludedKeys`): Background, Ambient1, Shadow, WindowBackground, BackgroundGlow,
  BackgroundGlow2 sowie alle Flächen-, Text-, Zustands-, Quellen- und Schrift-Schlüssel. Neue ThemeKeys gibt es nicht.
- **`ThemeResources.AddAccentEntries`** schreibt genau diese 25 Schlüssel; `Build` ruft sie auf (Token-Dateien unverändert).
- **Überblendung** `OklchFade`: vier Schlüsselbilder auf dem OKLCH-Weg (kürzerer Bogen) zu den Zeiten von CubicEase
  EaseInOut (0,397 / 0,5 / 0,603 / 1), ohne `From`, also von der Farbe auf dem Bildschirm aus. Voll 0,9 s, Reduziert 0,3 s,
  Aus, verstecktes oder minimiertes Fenster, Spiel mit Pause und Wechsel des Themas sofort (`MotionController.Transition`).
- **Farbwerte zu Beginn:** Das Kind-Wörterbuch wird zu Beginn einer geänderten Überblendung einmal getauscht (eine
  Invalidierung), wie die Ressourcen der App „Farben gelten sofort“; Leuchten, Punkte und Kennzeichen mit Rohfarben wechseln
  so mit der Handlung des Nutzers.
- **`LiveBrushSet`** führt bleibende Pinsel je Pinsel bzw. Verlaufsstopp zu ihrem Ziel; gleiches Ziel heißt nichts tun, so
  bricht `ApplyAmbient` im selben Durchlauf keine Überblendung ab. Im Fenster: `Lib.Brush.RowSelection`, `EdgeGlow`
  (Stopps der Umgebung sofort, Akzent-Stopp mit der Überblendung), Naht und Streif der Werkzeugleiste; der Schein der Lasche
  kommt über `ToolbarToggle.SetAccentColors` (vorher fest Abendrot). Ersetzt wird ein Pinsel nur bei anderem Aufbau
  (hell/dunkel, hoher Kontrast). Themenpinsel, die der Code bisher als Momentaufnahme setzte (Kapselpunkt, Markenzeichen,
  Nit-Skala, Statuspunkt, Effekte-Symbol, Linie der Fensterkante), sind jetzt Verweise (`SetResourceReference`).
- **Entprellung** `GameAccentScheduler`: einzelner Wechsel sofort; Wechsel schneller als 300 ms oder eine gehaltene Taste
  (Wiederholung der letzten 150 ms) halten die gezeigten Farben, das letzte Spiel folgt 300 ms nach dem letzten Wechsel.
  Die Farben kommen mit den Bildern des Spiels; graue Spiele zeigen genau das Schema, ladende Bilder und eine leere Auswahl
  behalten die gezeigten Farben.
- **Ruhiger Grund:** Ist „Hintergrund folgt dem Spiel“ aus, nimmt die Umgebung das ruhige Schema (`GamePalette.Calm`: Chroma
  höchstens 0,03, gleiche Helligkeit und gleicher Farbton), für jedes Spiel gleich. Sind beide an, mischen sich graue
  Titelbilder mit den gezeigten Akzenten.
- **Kontrast:** `GamePalette.Compute` rechnet die Spielpalette mit `PaletteEngine` (Überladung mit der Flächen-Atmosphäre des
  Schemas und dem hellsten bzw. dunkelsten möglichen Grundton der Bibliothek als weiterer Fläche); im hellen Thema
  Bernstein (Farbton 75°) statt Oliv für Akzenttext und Bedienfarben aus Gelb und Gold. `GamePalette.Scheme` ist genau die
  Palette der App.
- **Popups:** Kontextmenüs und Tooltips des Bibliotheksfensters bekommen die Ebene beim Öffnen in ihre Ressourcen.
- **Live-Vorschau:** Die Bühne der Seite „Darstellung“ trägt eine eigene Ebene (sofort, ohne Überblendung), solange der
  Schalter an ist und das Vorschauspiel Spielfarben hat; bei hohem Kontrast nicht. Die Kontrast-Kennzeichen zeigen weiter
  das Schema; die Farbprobe des Schalters zeigt, was er ergäbe.

| Bereich | Farben | Grund |
|---|---|---|
| Bibliotheksfenster | Spielfarben (Schalter an) | Wunsch des Nutzers |
| Live-Vorschau der Einstellungen | Spielfarben des Vorschauspiels | zeigt, was der Schalter tut |
| Übrige Einstellungen | Schema | zeigt und bewertet das gewählte Schema (Kontrast-Kennzeichen) |
| Tray-Menü, Tray-, Fenster- und Taskleistensymbol | Schema | E89: Symbole folgen dem Schema, kein Neuzeichnen je Spiel |
| Dialoge (Bestätigung, Spiel hinzufügen, „Bild wählen…“, Einstellungen entfernen) | Schema | eigene Fenster auf den Ressourcen der App |
| Titelbereich (`HeroStage`) | Spielfarben über die Palette | nur die Argumente ändern sich |

Prüfungen: G15.1 bis G15.10 in `GATES.md`; Bildtests `GameColorRenderTests` und `tools/gates/phase15-gamecolors.ps1`.
