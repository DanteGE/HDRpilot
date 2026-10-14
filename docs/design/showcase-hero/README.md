# Neugestaltung „Showcase Hero“: Stand für die spätere Umsetzung

Stand 2026-10-14. Der Nutzer hat die Richtung „Showcase Hero“ gewählt und den lokalen Prototyp v4 mit
„Sieht gut aus“ abgenommen. Er soll später in WPF umgesetzt werden. Noch gibt es keinen App-Code dafür.

## Inhalt dieses Ordners

| Pfad | Was |
|---|---|
| `prototyp/` | Lokaler, bedienbarer Prototyp v4 (HTML, CSS und JS ohne Build-Schritt) mit der echten Bibliothek und echten Launcher-Bildern |
| `entwurf-runde2/` | Die Entwürfe der Designrunde 2 (`.dc.html`), Grundlage des Prototyps |
| `berichte.md` | Berichte der Design-Agenten: Entwurf C nach Kritik, Bau, Kritik mit Screenshots, Überarbeitung zu v4 |

Online liegen alle Entwurfsrunden zum Vergleich (privat, nur für den Nutzer):
(privater Link entfernt)

## Prototyp starten

1. `prototyp\make-data.ps1` erzeugt `data.json` und `art\` im eigenen Ordner, beides steht in `.gitignore`.
   - Gelesen wird nur: standardmäßig die echte `%APPDATA%\HDRpilot\config.json` und
     `%LOCALAPPDATA%\HDRpilot\hdr-support.json` sowie die Launcher-Caches.
   - **Aus der Claude-Sitzung heraus** (MSIX-Container) zuerst eine Kopie der echten Dateien per WMI ziehen und mit
     `-Config` bzw. `-Hdr` übergeben. Im Container sähe das Skript sonst nur die private Kopie.
2. `prototyp\start.cmd` startet `server.ps1`. Der Server ist nur lesend, lauscht nur auf 127.0.0.1:8765 und beantwortet
   nur GET. Ausgeliefert werden nur Steam-Pfade und eine Allowlist aus `data.json`. Danach den Browser öffnen.
3. Zustände über die Adresse, zum Beispiel:
   - Ansichten: `#view=liste`, `#view=regal`, `#view=raster`
   - Seiten und Designs: `#page=darstellung`, `#theme=hell`, `#preset=cyber&mode=tri`
   - HDR-Moment als Standbild: `#hdr=on&freeze=0.35`
   - Spielstart: `#run=1`
   - Alle Schlüssel stehen im Bericht „Überarbeitung (v4)“.

## Entscheidungen des Nutzers

- **Richtung:** Showcase Hero, eine Startseite im Konsolenstil mit großem Titelbild des gewählten Spiels. Die Wirkung
  soll premium, detailreich und kräftig sichtbar sein. Schlichte Entwürfe hat der Nutzer als „nicht professionell
  genug, Effekte zu dezent“ verworfen.
- **Ansichten:**
  - **Liste** ist der Standard und Pflicht: kompakt, sortierbar, mit „HDR automatisch“ je Zeile und einem schlanken
    Titelbereich des gewählten Spiels oben.
  - Dazu **Regal** (Titelbild und Kachelreihe) und **Raster**.
  - Die gewählte Ansicht wird gemerkt.
- **Bilder:**
  - Echte Bilder aus den lokalen Launcher-Caches, nur gelesen.
  - Bei fehlenden Bildern erzeugt HDRpilot ein Ersatzbild. Per Rechtsklick lässt sich „Bild wählen…“.
  - **SteamGridDB** ist optional zuschaltbar, mit eigenem API-Schlüssel und standardmäßig aus. Es füllt nur fehlende
    Bilder und ist die einzige Ausnahme von „nur lokal“.
- **Bewegung:** nur allgemeine Ebenen, die mit jedem Bild funktionieren, keine handgemachten Animationen pro Spiel.
- **Farben:** Duoton und Tritonus mit Vorgaben und eigenen Farben. Sie färben das ganze UI, das Tray-Symbol und das
  Fenstersymbol. Hell und dunkel gibt es beide im neuen Stil (Automatisch, Hell, Dunkel).
- **Echte Monitorwerte** statt Platzhaltern, aus DXGI `IDXGIOutput6::GetDesc1`: Spitze, Vollbild, Minimum, Farbtiefe
  und ob HDR aktiv ist. Gelesen wird nur bei einem HDR- oder Bildschirmwechsel. Der MAG 341C meldet Peak 456,
  Vollbild 254, Minimum 0,0000 Nits und 10 Bit.
- **Leistung:**
  - Bei geschlossenem oder minimiertem Fenster und während eines Spiels gibt es keine Animation und keine
    Grafikkarten-Last.
  - Bei offenem Fenster darf die Grafikkarte nie in die Nähe von 100 % kommen. Dauer-Effekte laufen höchstens mit 30 fps.
  - Die Effektstufen heißen Voll, Reduziert und Aus. Ist die Windows-Option „Animationseffekte“ aus, gilt mindestens
    „Reduziert“.

## Bildquellen (gemessen an der Bibliothek, 651 Spiele)

| Launcher | Spiele | Quelle |
|---|---|---|
| Steam | 636 | `appcache\librarycache\<appid>\`: Titelbild `library_hero.jpg` (631), Logo `logo.png` (628, deutsche Varianten bevorzugt), Cover (631, davon 455 im Format 600×900), unscharfes Titelbild `library_hero_blur.jpg` als Farbfeld |
| GOG | 5 | Bildcache von GOG Galaxy (`ProgramData\GOG.com\Galaxy\webcache`): Titelbild und Cover (WebP über WIC) |
| Ubisoft | 1 | `Ubisoft Game Launcher\cache\assets`: voller Bildsatz |
| Xbox | 2 | 480-px-Kachelbild aus dem Spielordner; daraus erzeugtes Titelbild und Cover |
| Epic, EA | 7 | kein Bildcache: Symbol aus der Haupt-EXE (meist 256 px), daraus erzeugte Bilder |

**Ersatzbilder:**
- Fehlt das Titelbild, entsteht ein Farbfeld aus dem Symbol mit dem Namen als Umriss-Muster und dem Symbol neben dem
  Titel.
- Fehlt das Logo, steht der Spielname in der Titelschrift.
- Ein verschmiertes Steam-Hochformat wird durch einen Ausschnitt aus dem Titelbild plus Logo ersetzt.
- Fehlt das Symbol, entsteht ein Monogramm.

Neue Bilder erscheinen von selbst: In der App überwacht ein `FileSystemWatcher` die Cache-Ordner.

## Effekte und Umsetzung in WPF

Die vollständige Liste steht oben in `prototyp/app.js` unter „Umsetzung in WPF: Primitive · Kosten · Anhalten“, die
Begründungen in `berichte.md`. Die wichtigsten Punkte:
- **Titelbild:** langsamer Zoom (Ken Burns) und Tiefenwirkung mit der Maus, nur über Transforms. Beim Spielwechsel
  wird zwischen zwei Bildplätzen überblendet.
- **Umgebungsfarben:** einmal je Spiel aus einer kleinen Fassung des Bildes berechnet und zwischengespeichert. Der
  Hintergrund ist ein Verlauf, kein Weichzeichner.
- **HDR-Moment (2,5 s):**
  - Ein Lichtstreifen läuft durch das Bild, eine vorab berechnete, hellere Fassung des Bildes wird eingeblendet.
  - Bis zu 160 Partikel laufen nur 2,5 s, danach gibt es 0 Bildaufrufe.
  - Der Fensterrand leuchtet in den Spielfarben.
  - Ohne `BlurEffect`, `DropShadowEffect` und Mischmodi.
- **Liste:** virtualisiert (650 Spiele), Vorschaubilder asynchron und klein dekodiert.
- **Anhalten:** bei Minimieren, Deaktivieren, geschlossenem Fenster und laufendem Spiel. Beim Schließen werden die
  Storyboards entfernt und die Bitmaps freigegeben.

## Offene Punkte für die Umsetzung

- Bilder vorab berechnen und zwischenspeichern (`%LocalAppData%\HDRpilot\art\`): erzeugte Bilder, helle HDR-Fassung,
  zugeschnittene Logos, Umgebungsfarben.
- Bei sehr dunklen Bildern wirkt die Umgebungsfarbe teils blass. Die Mindest-Sättigung ist im Prototyp ein Ansatz.
- Schriften: Der Prototyp lädt Google Fonts. In der App müssen die Schriften (OFL) mitgeliefert werden; sonst gilt
  Segoe UI.
- SteamGridDB echt anbinden: nur wenn eingeschaltet, mit eigenem Schlüssel, nur für fehlende Bilder, mit Hinweis auf
  den Netzwerkzugriff.
- Eigene Bilder per „Bild wählen…“ dauerhaft speichern, wie die eigenen Symbole (E86).
- Abnahmekriterien als Phase 10 in `GATES.md` aufsetzen. Dazu gehört ein Messgate für Grafikkarten- und
  Prozessorlast: bei offenem Fenster in Ruhe und im HDR-Moment, bei geschlossenem und minimiertem Fenster sowie
  während eines Spiels.
