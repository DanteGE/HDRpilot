# Umsetzung Showcase Hero: Teil CORE (Bilder, SteamGridDB, Helligkeit)

Stand 2026-10-16, Zweig `feature/showcase-core` (Basis e773a1e). Notizen für README und E89 im Schritt G10.10. Die Texte
der Oberfläche stehen in `src/HdrPilot.App/Resources/ArtworkStrings.resx` und sind hier wörtlich zitiert.

## Spielbilder (G10.3)

### Woher die Bilder kommen

Gelesen wird nur, nie geschrieben (`ArtworkLocator`, Dateinamen wie in `prototyp/make-data.ps1` gemessen):

| Launcher | Ordner | Titelbild | Logo | Cover | Sonst |
|---|---|---|---|---|---|
| Steam | `<Steam>\appcache\librarycache\<appid>\` samt Unterordnern | `library_hero_german.jpg`, `library_hero.jpg` | `logo_german.png`, `logo.png` | `library_600x900[_german].jpg`, sonst Kapsel `library_capsule[_german].jpg` | Kopfbild `header[_german].jpg`/`library_header…`, Farbfeld `library_hero_blur[_german].jpg`, Gemeinschaftssymbol `<40 Hex>.jpg` (32 px, nur letzter Ersatz) |
| GOG Galaxy | `%ProgramData%\GOG.com\Galaxy\webcache\<Benutzer>\gog\<ID>\` | `*_glx_bg_top_padding_7.webp`, sonst `*_glx_background*.webp` | `*_glx_logo*.webp` | `*_glx_vertical_cover.webp` | Symbol `*_glx_square_icon_v2.webp` |
| Ubisoft Connect | Produkt-Cache `cache\configuration\configurations`, Dateien in `cache\assets` | `background_image` | `logo_image` | `thumb_image` | Kopfbild `splash_image`; Werte auch über `localizations.default` |
| Xbox | `<Spielordner>\Resources` | – | – | – | Kachelbild `Square480x480Logo*.png` (Symbol, deckend); `SplashScreen*.png` nur unter „Bild wählen…“ |
| Epic, EA, Battle.net, Manuell | kein Bildcache | – | – | – | Symbol aus der Haupt-EXE (bis 256 px, Hilfsprogramme und Ausschlüsse übersprungen) |

Steam-Ordner wie der Steam-Scanner (HKCU `SteamPath`, sonst HKLM 32 Bit `InstallPath`, sonst `Programme (x86)\Steam`),
Ubisoft aus HKLM 32 Bit `SOFTWARE\Ubisoft\Launcher\InstallDir`, sonst der Standardordner.

Reihenfolge je Bildart: eigene Wahl („Bild wählen…“) → Launcher-Bild → SteamGridDB (nur eingeschaltet, nur wenn das Bild
fehlt) → erzeugtes Bild. Titelbild, Cover, Kopfbild, Farbfeld und Symbol sind nie leer; ohne Logo ist `Logo` null (der Name
steht dann in der Titelschrift). Symbol: eigenes Symbol („Symbol ändern…“), sonst das größte der Symbol-Hinweise des
Launchers, des GOG-Symbols und des Xbox-Kachelbilds; die EXE bei Epic, EA, Battle.net, manuellen Spielen und bei Spielen ohne
Titelbild mit einem Symbol unter 128 px; das 32-px-Gemeinschaftssymbol von Steam nur als letzter Ersatz, sonst Monogramm.
Deckende Bilder ab 256 px (Xbox-Kachelbild) gelten als Kachelbild und werden in erzeugten Bildern als Bildfläche gezeigt.

### Abgeleitet und erzeugt (einmal je Bild, dann gemerkt)

- **Umgebungsfarben** (Portierung von `extract()`): aus dem Titelbild (48 px breit), sonst aus Symbol oder Kachelbild,
  sonst aus dem Namen. Blasse Bilder (Sättigung unter 0,25 oder sehr dunkel) bekommen Mindest-Chroma 0,05/0,08/0,08
  (OKLCH); fast graue (unter 0,08) sind `AmbientDullness.Gray` – das Mischen mit der Schemafarbe macht die Oberfläche.
  Spitzlicht (`LightX`/`LightY`/`Highlight`) nur zwischen 22 % und 86 % der Breite.
- **Farbfeld**: Steams `library_hero_blur` beim Steam-Titelbild, sonst 8×3 (Titelbild) bzw. 6×6 (Symbol) Pixel, Sättigung
  ×1,25, Helligkeit ×0,82; gespeichert als Hex in index.json, die Oberfläche streckt es mit linearer Filterung.
- **Verschmierte Steam-Kapsel**: 60×90-Fassung, unteres Band Zeilen 62–89 (`v < 1,4`, `h ≥ 1,6·v`, `h < 6`,
  Streuung > 10) und oberes Band 0–8 (`v < 3,5`, Streuung > 3). Dann Cover aus einem Ausschnitt des Titelbilds um das
  Spitzlicht (`LightX` 0,2–0,8, senkrecht 30 %) mit Logo bzw. Name (`ArtworkSummary.CoverFromSmear`). Gemessen: Blue Estate,
  Dustforce, Homesick.
- **Logo** auf den sichtbaren Teil zugeschnitten (Deckkraft > 8, 2 px Rand), als PNG in `art\logo`.
- **Helle HDR-Fassung** (`GameArtwork.HeroBoost`): je Pixel wie CSS `brightness(1.22) contrast(1.14) saturate(1.42)`,
  nur für gezeigte Titelbilder, in Stufenbreite bis 2560 px als JPEG (Qualität 90) in `art\boost`, höchstens 300 MB (die
  am längsten nicht benutzten fallen heraus).
- **Erzeugte Bilder** (DrawingVisual + RenderTargetBitmap, Schrift Bahnschrift fett, sonst Segoe UI):
  - Titelbild 1920×620: Farbfeld, Abdunklung wie `.gshade`, Name als Umriss-Muster in zwei Zeilen (−6°, links
    ausgeblendet), bei Kachelbild das Kachelbild rechts der Mitte mit weichen Kanten.
  - Cover 600×900: mit Titelbild Ausschnitt plus Logo bzw. Name; sonst Farbfeld, Licht, Streifen, Symbol auf einer
    Glaskachel (höchstens 1,5-fach vergrößert) bzw. Monogramm, Kachelbild oben über die ganze Breite; unten der Name
    (höchstens drei Zeilen).
  - Kopfbild 460×215 (Vorschaubild der Liste ohne Steam-Kopfbild und ohne Titelbild): Farbfeld mit Symbol.
  - Monogramm 256×256 in den Namensfarben (unabhängig vom Farbschema), Regel wie `mono()`: „Baldur's Gate 3“ → „BG“.

### Verhalten

- Geladen wird auf höchstens zwei eigenen STA-Threads, zuletzt angefragte zuerst; abgebrochene Anfragen (weggescrollte
  Zeilen) entfallen. Bilder sind eingefroren und höchstens in der angefragten Breite dekodiert (nie hochskaliert);
  dekodierte Bilder im Speicher höchstens 192 MB (älteste fallen heraus).
- Speicher, Beobachtung der Cache-Ordner, Zeitgeber und Arbeits-Threads gibt es nur, solange ein Fenster eine Freigabe
  hält. Beim Schließen des letzten Fensters: Beobachtung aus, Bilder frei, index.json geschrieben.
- **Neue Bilder erscheinen von selbst**: `FileSystemWatcher` auf `librarycache`, den GOG-`webcache` und Ubisofts
  `cache\assets` (nur mit offenem Fenster), 750 ms gesammelt, dann `Changed(<Spielschlüssel>)`; Pufferüberlauf:
  `Changed(null)`.
- **Bild wählen…** (`GetCandidatesAsync`, Reiter Titelbild/Logo/Cover): „Automatisch“, jedes andere lokale Bild
  („SplashScreen.png (Xbox-Paket)“ mit dem Hinweis „Schwarzweiß – vermutlich Engine-Logo“ oder „Titelkarte mit
  Schriftzug in der Mitte“; bei Steam die nicht deutsche Fassung), „Erzeugtes Bild“ (beim Logo „Name statt Logo“) und die
  aktuelle „Eigene Datei“. Vorschauen höchstens 320 px.
- **Eigene Bilder** (`SaveCustomImage`): Titelbild höchstens 3840 px breit (JPEG), Logo höchstens 1280 px mit
  Durchsichtigkeit (PNG), Cover auf 600×900 zugeschnitten (JPEG); die gewählte Datei bleibt unverändert. Kein Bild: „Die
  Datei ist kein lesbares Bild.“ Kennzeichen `custom:<Stamm>-hero.jpg` usw.; gelöscht werden nur eigene Dateien dieses
  Musters.
- Zeile im Detailbereich (`ArtworkSummary.Description`), etwa „Lokal aus dem Steam-Cache · nichts heruntergeladen“,
  „Eigene Datei · nur auf diesem PC“, „Xbox-Paket: Kachelbild 480 px · Titelbild und Cover daraus erzeugt“,
  „Epic Games: kein Bildcache · Symbol aus RDR2.exe (256 px), Titelbild und Cover erzeugt“, „Steam hat für dieses Spiel
  kein Titelbild – erzeugt aus dem Symbol“, „Titelbild von SteamGridDB · mit eigenem Schlüssel geladen“.
- Beispiele für die Einstellungen (`GetFallbackExamplesAsync`, je Art möglichst ein anderes Spiel, zuletzt gespielte
  zuerst): Titelbild aus Symbol (EXE-Symbole bevorzugt, „EA app · aus bf6.exe · 256 px“), Name statt Logo („GOG · kein Logo
  im Cache“), Cover erzeugt (Kachelbilder bevorzugt, „Xbox · aus dem Kachelbild erzeugt“), Kapsel verschmiert
  („Steam · Kapsel verschmiert → Ausschnitt“), Monogramm („… · kein Symbol gefunden“).

### Ablage `%LOCALAPPDATA%\HDRpilot\art\`

| Datei | Inhalt |
|---|---|
| `index.json` | Fassung 1; je Spielschlüssel: Kennung (Prüfsumme über Name, eigene Wahl, eigenes Symbol, Symbol-Hinweise, EXE-Listen, Installationsordner sowie Pfad, Größe und Änderungszeit jeder gefundenen Datei), gefundene Bilder mit Größe, gewähltes Symbol, Kapsel-Prüfung samt Kennzahlen, Umgebungsfarben mit Farbfeld, Zuschnitt des Logos, Namen der erzeugten Bilder und hellen Fassungen, SteamGridDB (Abfragen je Bildart, Dateien, Spiel-ID); oben der letzte SteamGridDB-Lauf. Ändert sich die Kennung, werden die abgeleiteten Daten und ihre Dateien verworfen, SteamGridDB bleibt. Atomar geschrieben, 2 s gesammelt, nur mit offenem Fenster (sonst beim Ende des Berichts). Unlesbar oder andere Fassung: wird neu aufgebaut. |
| `gen\<Stamm>-hero.jpg`, `-cover.jpg`, `-header.jpg`, `-icon.png` | erzeugte Ersatzbilder |
| `logo\<Stamm>.png` | zugeschnittene Logos |
| `boost\<Stamm>-<Breite>.jpg` | helle HDR-Fassungen (höchstens 300 MB) |
| `custom\<Stamm>-hero.jpg`, `-logo.png`, `-cover.jpg` | eigene Bilder |
| `sgdb\<Stamm>-hero/-logo/-cover.<png/jpg/webp>` | Bilder von SteamGridDB |

`<Stamm>` = lesbarer Spielschlüssel plus 8 Hex-Zeichen wie bei den eigenen Symbolen (`Steam_553850_3F2A9C1B`).

### Messung (Kopie der Bibliothek, Launcher-Caches nur gelesen)

`HDRpilot.exe --artwork-report <datei.json> --config-dir <ordner>`: 651 Spiele (nicht 650).

| Launcher | Spiele | Titelbild genutzt/gefunden | Logo | Cover | Symbol |
|---|---|---|---|---|---|
| Steam | 636 | 631/631 | 628/628 | 628/631 (3 verschmiert) | 636/636 |
| Epic Games | 6 | 0/0 | 0/0 | 0/0 | 6/6 (EXE) |
| GOG | 5 | 5/5 | 0/0 | 5/5 | 5/5 |
| Xbox | 2 | 0/2 (Splash, nur wählbar) | 0/0 | 0/0 | 2/2 (Kachelbild) |
| Ubisoft Connect | 1 | 1/1 | 1/1 | 1/1 | 1/1 |
| EA app | 1 | 0/0 | 0/0 | 0/0 | 1/1 (bf6.exe, 256 px) |

Erzeugt: 14 Titelbilder, 17 Cover (davon 3 aus verschmierten Kapseln), 14 Kopfbilder, 0 Monogramme, 22-mal Name statt
Logo. Zeit für alle Spiele: erster Lauf überhaupt (Dateien nicht im Windows-Cache) 10,4 s, leeres Verzeichnis 0,9 s,
mit Verzeichnis 0,24 s. Kontaktbogen der Ersatzbilder: `<bericht>.kontakt.png` und `<bericht>.kontakt-hell.png`.

## SteamGridDB (G10.4)

- Schalter `settings.artwork.steamGridDb` (Standard aus), Schlüssel in `%LOCALAPPDATA%\HDRpilot\steamgriddb-key.dat`,
  DPAPI für den Windows-Benutzer mit eigenem Schlüsselanteil (`HDRpilot.SteamGridDB.v1`), Base64, nie im Protokoll.
- Läufe: nach dem Einschalten bzw. Speichern des Schlüssels, über `RunNowAsync` („Fehlende Bilder jetzt laden“) und
  höchstens einmal am Tag 60 s nach dem Start, nur mit normaler Ablage (mit `--config-dir` nie von selbst), eingeschaltet,
  mit Schlüssel und ohne laufendes Spiel („Übersprungen: Ein Spiel läuft“).
- Gefüllt werden nur Titelbild, Logo und Cover, die lokal fehlen und keine eigene Wahl haben (verschmierte Kapsel zählt als
  fehlendes Cover). Steam: `grids/steam/<appid>?dimensions=600x900`, `heroes/steam/<appid>`, `logos/steam/<appid>`, jeweils
  nur unbewegte Bilder ohne NSFW-/Spaß-Markierung; andere Launcher: `search/autocomplete/<Name>`, dann `…/game/<id>` (ID
  gemerkt). Schlüssel nur als `Authorization: Bearer` an www.steamgriddb.com; Bilder nur von https-Adressen unter
  steamgriddb.com, höchstens 15 MB, Zeitlimit 15 s, mindestens 1 s zwischen Anfragen, höchstens 200 Anfragen je Lauf.
  Fehlanzeigen werden 30 Tage gemerkt.
- Stand für die Einstellungen (`SteamGridDbStatus`): an/aus, Schlüssel (fehlt/gespeichert/„Gespeicherter Schlüssel nicht
  lesbar – bitte neu eintragen“), läuft, letzter Lauf, Anfragen, geladene Bilder, letzter Fehler („Schlüssel abgelehnt –
  bitte prüfen“, „SteamGridDB nicht erreichbar“, „Zu viele Anfragen – der nächste Lauf holt den Rest“ …) und die Zahl
  fehlender Titelbilder, Logos und Cover (lokal gezählt, ohne Netz).
- Aus oder ohne Schlüssel entsteht kein HttpClient und keine Anfrage (Tests zählen die Handler). „Prüfen“ fragt nur auf
  Klick einmal `search/autocomplete/HDRpilot`.

## Helligkeit der Bildschirme (G10.7)

- `DxgiLuminanceReader`: `CreateDXGIFactory1` → `EnumAdapters1` → `EnumOutputs` → `IDXGIOutput6::GetDesc1` über die
  COM-Tabellen (Stellen 12, 7, 27), `DXGI_OUTPUT_DESC1` 152 Byte; HDR aktiv bei Farbraum G2084/P2020 (12). Wirft nie.
- `DisplayLuminanceService`: liest nur mit offenem Fenster und nur, wenn sich Bildschirme, HDR-Zustand oder Zielbildschirm
  geändert haben; Zuordnung über den GDI-Namen. Jede Messung mit aktivem HDR landet in
  `%LOCALAPPDATA%\HDRpilot\display-luminance.json` (`{"version":1,"displays":{"<monitorDevicePath>":{"peakNits":456,
  "fullFrameNits":254,"minNits":0,"bits":10,"readAt":"…"}}}`); `DisplayNits.PeakNits` ist diese HDR-Spitze auch bei
  ausgeschaltetem HDR und damit `NitScale.Maximum`. Ohne Messung mit HDR: aktueller Wert, `PeakMeasuredWithHdr = false`.
- Mit ausgeschaltetem Bildschirm meldete DXGI hier `\\.\DISPLAY1` mit 0 Nits, 10 Bit, HDR aus: Werte unbekannt, die
  Oberfläche zeigt „—“. Die echten Werte (MAG 341C: 456/254/0/10) sind mit eingeschaltetem Bildschirm zu prüfen.

## Offene Punkte für die Zusammenführung

- VIEWS: Das erzeugte Titelbild enthält kein Symbol (es stünde bei jeder Höhe des Titelbereichs woanders). Für
  `Summary.Hero == Generated` zeigt die Oberfläche das Symbol (`GameArtwork.Icon`, `PixelWidth` ist die echte Größe bis
  256 px, höchstens 1,5-fach vergrößern) links vom Namen wie `.gtl`/`.gico` im Prototyp. Graue Umgebungsfarben
  (`AmbientDullness.Gray`) zur Hälfte mit der Schemafarbe mischen.
- THEME: Abdeckungstabelle aus `GetCoverageAsync` (Zeilen nach Zahl der Spiele), Beispiele aus `GetFallbackExamplesAsync`
  (Detailzeile steht fertig in `FallbackExample.Detail`), SteamGridDB-Status über `ISteamGridDbService.Status` (erster
  Zugriff zählt fehlende Bilder im Hintergrund und meldet `Changed`).
- README „Speicherorte“ nennt `art\`, `steamgriddb-key.dat` und `display-luminance.json` schon; die Unterordner und das
  Verhalten von SteamGridDB (Abstand, Grenzen, 30 Tage) gehören in „So funktioniert es“.
