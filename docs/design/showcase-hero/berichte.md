# Showcase Hero: Berichte der Design-Agenten (2026-10-14), unverändert übernommen

Reihenfolge: Runde 2 (Entwurf C nach Kritik), dann der lokale Prototyp (Bau, Kritik mit Screenshots, Überarbeitung zu v4). Die Hinweise zur WPF-Umsetzung stehen in den Berichten und oben in prototyp/app.js.

## Runde 2: Entwurf C (Showcase Hero), überarbeitet

I revised both concept files and added a third artboard, ShowLibHell. All three render and stay fully interactive in headless Chrome, with no script errors or unknown holes. The tags balance and none of the CSS WPF can't reproduce is left.

**The library files are too big: about 101.7 KB each, against the ~70 KB limit.** The split the critique asked for doesn't make them smaller, because both states need all ten covers (28 KB) plus the shared code. So ShowLib and ShowLibHell use the same code and only start in a different state. The only way to get under 70 KB is to cut features or artwork detail; that needs a decision. ShowLook is 52.9 KB. I did not edit `canvas.json`, so ShowLibHell still has to be registered there.

## What changed
- **Scheme colours tint everything outside the covers.** The game colours now only colour the cover art and the HDR glow.
  - Colour 1 (accent): Starten, HDR button, switches, selection.
  - Colour 2 (atmosphere): background cast, glows, panel tint.
  - Colour 3 (shine): HDR sequence, light rays, bokeh, sheen, level meter.
  - In 2-colour mode the third colour is derived automatically.
  - The 8 presets are German: Abendrot, Cyber, Neongrün, Royal, Gletscher, Synthwave, Glut, Polarlicht.
- **Light theme for the library.** ShowLibHell opens light, in Raster view, with the no-artwork game Fracture Point selected and the Polarlicht preset.
- **Starten works.** The label goes Starten → Wird gestartet … → Läuft, with a progress bar.
  - If HDR automatisch is on, the full HDR-on sequence fires with a toast. If the automation is paused, a toast says HDR stays off.
  - Clicking again ends the game and turns HDR back off if "Nach dem Spiel zurückstellen" is on.
  - While a game runs, all idle animation freezes and the footer says so.
- **Artwork.** One art layer per game, with rim lights, 3–4 depth layers, shared fog/ember/light-ray elements, fine detail lines and a final colour grade. Each game has its own title styling.
  - Fracture Point and Zero Stress King are back. Both show the fallback cover generated from the EXE icon, plus the panel line "Artwork: aus EXE-Symbol erzeugt · Bild wählen …".
- **Regal | Raster toggle.** Regal pages by 7 tiles and shows the position in the footer. Raster collapses the hero to a 300 px banner and shows all 10 games as a 5×2 grid, with the details panel in a right column.
- **One "Darstellung" button replaces Farbe and Effekte.** It opens a popover with the presets (staggered in), 2/3 Farben, Hell/Dunkel, Voll/Reduziert/Aus and "Weitere Einstellungen …". The colour wave starts at the clicked swatch. "Spiel hinzufügen" and the logo no longer use the gradient, which is now only on Starten and HDR einschalten.
- **The other critique items are in:**
  - effect levels: Reduziert caps transitions at 0.3 s; in Aus, messages move to the footer;
  - performance: tile tilt is CSS-only, the hero uses 2 slots instead of 24 SVGs, and search no longer replays the shelf animation;
  - accessibility and copy: real switches, a select button on each tile, `aria-selected` in menus, text at least 11 px, and a read-only "Beim Start" chip in the hero;
  - collisions: styles are scoped to each artboard and keyframes are prefixed `sl`/`sk`.
- **ShowLook:**
  - "2 Farben | 3 Farben", with a role line under each colour well and an optional "Hintergrund folgt dem Spiel" switch.
  - The live preview is now a mini Showcase of the library.
  - Each effect level has a demo tile: Voll tilts with the moving border and sheen, Reduziert only fades, Aus is static.
  - Four new switches: Beim Spielen anhalten, Im Hintergrund anhalten, Windows-Animationseffekte beachten, Nach dem Start in den Infobereich minimieren.
  - Tray and window icons follow the HDR state in both artboards. Off is a plain glyph with a 3 px accent dot, on is a filled gradient tile. All four sizes (16/20/24/32 px) are shown, with a separate "Taskleiste: Hell | Dunkel" toggle.

## Signature effects
1. **HDR-on moment:** bright flash at the cover's light source, a light band sweeping across, a horizon streak, rings, a particle burst from the button, a window-edge glow, the meter filling, the nits counting up to 1000 and the cover's bloom fading in.
2. **Living hero:** slow zoom and pan, three sweeping light rays, drifting haze, bokeh at three depths, a sheen across the title and a breathing glow under Starten. Parallax depth: cover 12 px, haze and rays 26 px, bokeh 42 px, title moves the opposite way.
3. **Selected tile:** a bright dash that travels around the border, plus 3×3-zone tilt and a moving shine.
4. **Colour change:** a ring wave from the clicked swatch while all colours blend over 0.9 s.
5. **Game switch:** the new cover settles in from 1.08× while the old one fades.
6. **Regal ↔ Raster:** tiles slide into the grid one after another while the hero folds into the banner.

## WPF implementation notes
| Effect | WPF building block | Est. GPU cost | How it stops |
|---|---|---|---|
| Cover zoom/pan, parallax | `ScaleTransform`/`TranslateTransform` on a cached bitmap | very low | storyboard paused, `CompositionTarget.Rendering` unhooked |
| Light rays, haze, bokeh | ~20 small `Rectangle`/`Ellipse` with gradient brushes, transform + opacity only | low | not created in Reduziert/Aus; paused otherwise |
| Bloom | bright-pass version of the cover, computed once on load, opacity fade | low | one-off 1.1 s fade |
| HDR sequence | gradient rectangles; transform + opacity; burst = 12 small ellipses; 60 fps only for this one-off | short spike, about 1.6 s | ends by itself |
| Edge glow | 4 gradient strips around the window, opacity only | low | one-off |
| Moving tile border | `Rectangle.StrokeDashOffset` animation, selected tile only | very low | paused |
| Title sheen | `LinearGradientBrush` offset animation used as the text's opacity mask (small area) | low | paused |
| Colour wave | one ellipse with a radial gradient, scaled up | about 1.4 s of full-window fill | one-off |
| Colour blending | `ColorAnimation` on shared brush resources | very low | ends by itself |
| Tile tilt | `SkewTransform` + `ScaleTransform` from hover zones | very low | none in Reduziert/Aus |

Not used, because WPF can't do them cheaply or at all: blend modes, background blur behind elements, rotating gradient rings, 3D tilt, or opacity masks on moving layers.

**How everything stops:**
- Cap all animation at 30 fps in the App's static constructor:
  ```csharp
  Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 30 });
  ```
  Only the short one-off sequences run at 60 fps.
- Pause all storyboards and unhook `CompositionTarget.Rendering` on `Deactivated`, on `StateChanged` to Minimized, on `IsVisibleChanged`, and when a game process starts.
- Read `SystemParameters.ClientAreaAnimation`; if Windows animations are off, use Reduziert.
- Closing to the tray must call `Close()`, not `Hide()`. That way no visual tree, animation clocks or bloom bitmaps stay in memory, so a closed or minimised window costs nothing.

## Things to know
- Root-level state selectors must be written with `:scope` (for example `:scope.hdr .x`). Inside `@scope`, Chrome does not match the scope's root element otherwise, so a selector like `.dark .x` silently does nothing. This needs Chrome 118 or newer.
- The canvas pass-through of `<use href="{{…}}">` was only tested in the local preview. If it fails in the real canvas, each hero slot shows its gradient fallback rather than a blank area.

Files are in `<Notizordner>\design\project`:
- ShowLib.dc.html
- ShowLibHell.dc.html
- ShowLook.dc.html

The pieces I built them from are in the `<Notizordner>\build` folder: `l1.html`–`l6.html` for the library, `k1.html`–`k4.html` for ShowLook.

## Prototyp: Bau (v3)

**Showcase Hero v3 ist fertig.** Für alle drei Ansichten und die Seite „Darstellung“ habe ich Bildschirmfotos gemacht und angesehen. Der eingebaute Selbsttest besteht 17 von 17 Prüfungen ohne Laufzeitfehler. Der Server ist wieder beendet, und geschrieben wurde nur unter `proto\`.

**Zur Frage, wie das mit Titelbildern bei späteren Spielen funktioniert:** Es gibt keine handgebauten Banner mehr. Pro Spiel kommen vier echte Bilder aus dem lokalen Steam-Cache (`appcache\librarycache\<appid>`, nur gelesen): `library_hero.jpg`, `logo.png`, das Hochformat-Cover und das `.ico`. Dazu kommt `library_hero_blur.jpg`, das Steam schon unscharf mitliefert. Es dient als Hintergrundlicht, deshalb braucht es keine teure Live-Unschärfe.

Die Bewegung ist allgemein und funktioniert mit jedem Bild:
- leichtes Heranzoomen über ~28 s (Ken Burns)
- Parallaxe: Bild und Logo verschieben sich mit der Maus gegeneinander
- Überblendung mit Seitwärtsbewegung beim Spielwechsel
- Farben einmal per Canvas aus dem Bild gelesen; sie färben den Fensterhintergrund („Hintergrund folgt dem Spiel“)
- HDR-an: Lichtstreifen, eine zweite, vorab aufgehellte Bildebene wird eingeblendet, Leuchten an der hellsten Bildstelle, Partikel in den Bildfarben

Logos werden automatisch auf ihren sichtbaren Teil zugeschnitten.

Fehlt ein Bild, wird es erzeugt:
- **Kein Titelbild:** Symbol groß, seine Farben als Verlauf, der Name groß in der Titelschrift.
- **Kein Logo:** Spielname in der Titelschrift.
- **Kein Cover:** Cover aus Symbol und Name.
- **Kein Symbol:** Monogramm aus den Anfangsbuchstaben.

Echte Beispiele aus deiner Bibliothek: Drone Sector Demo (Titelbild aus Symbol), Blue Estate (Name statt Logo), Clive Barker's Hellraiser: Revival von Xbox (Monogramm und erzeugtes Cover).

### Starten
- Doppelklick auf `start.cmd` öffnet ein Serverfenster und den Browser.
- Oder: `powershell -NoProfile -ExecutionPolicy Bypass -File "<Notizordner>\proto\server.ps1"`, danach **http://127.0.0.1:8765/** öffnen. Strg+C beendet den Server.
- Daten neu erzeugen mit `make-data.ps1` im selben Ordner.

Der Server lauscht nur auf 127.0.0.1, beantwortet nur GET und liefert unter `/ico` nur Pfade unterhalb von `C:\Program Files (x86)\Steam\` aus. Getestet: Pfad-Ausbrüche, `win.ini`, UNC-Pfade und `.ps1` werden mit 403 abgelehnt, POST mit 405.

Ordner: `<Notizordner>\proto\` mit `index.html`, `app.css`, `app.js` (Hinweise zur WPF-Umsetzung stehen oben im Kommentar), `data.json`, `make-data.ps1`, `server.ps1`, `start.cmd`, `_shots\shot.ps1`.

### Zustände über die Adresse (#a=b&c=d)
| Schlüssel | Werte |
|---|---|
| `view` | `liste` (Standard), `regal`, `raster` — die Wahl wird gespeichert |
| `page` / `look` | `darstellung`; dazu `look=lk-farben`, `lk-bilder` oder `lk-symbole` |
| `theme` | `auto`, `hell`, `dunkel` |
| `preset` | `0`–`7` oder Name, z. B. `cyber`; dazu `mode=duo` oder `tri`, eigene Farben mit `c1`/`c2`/`c3=RRGGBB` |
| `fx` | `voll`, `reduziert`, `aus` |
| `follow` | `0` oder `1` (Hintergrund folgt dem Spiel) |
| `hdr` | `on`; mit `freeze=0.25` als Standbild mitten im HDR-Moment |
| `sel` | Appid, Index oder Namensteil |
| Filter und Sortierung | `q=…`, `src=xbox`, `filter=auto,neu,hdr`, `sort=lp|name|src|hdr|auto`, `dir=asc|desc` |
| `thumb` | `bild` oder `symbol` |
| Menüs und Dialoge | `ctx=1`, `dlg=bild` (mit `tab=hero|logo|cover`), `dlg=symbol`, `menu=src|sort` |
| Abläufe | `run=1` (Spielstart simuliert), `pause=1`, `zz=1` (Effekte pausiert wie im Hintergrund) |
| Einstellungen | `sgdb=1`, `tb=hell|dunkel` (Taskleiste in der Vorschau), `pvhdr=1` |
| Demo und Test | `ahdr=<Namen oder IDs>` (Auto-HDR-Kennzeichen zur Vorführung), `gen=<IDs>` (erzeugte Bilder erzwingen), `shot=1600x1000`, `selftest=1` |

### Bildschirmfotos
Alle unter `<Notizordner>\proto\shots\` (Zwischenstände in `shots\_iteration\`):
- `final-01-liste-standard.png`
- `final-02-liste-hdr-moment.png`
- `final-03-regal.png`
- `final-04-regal-hdr-moment.png`
- `final-05-raster.png`
- `final-06-regal-hdr-an.png`
- `final-07-fallback-titelbild-aus-symbol.png`
- `final-08-fallback-name-statt-logo.png`
- `final-09-fallback-monogramm-xbox.png`
- `final-10-kontextmenue.png`
- `final-11-bild-waehlen.png`
- `final-12-symbol-aendern.png`
- `final-13-darstellung.png`
- `final-14-darstellung-bilder.png`
- `final-15-darstellung-tray.png`
- `final-16-liste-hell-autohdr-demo.png`
- `final-17-regal-hell.png`
- `final-18-raster-tritonus-cyber.png`
- `final-19-spiel-laeuft.png`
- `final-20-effekte-pausiert-hintergrund.png`
- `final-21-darstellung-hell.png`
- `final-22-symbol-ansicht-liste.png`
- `final-23-liste-1440x900.png`
- `final-24-regal-1920x1080.png`

### Echte Bilder in data.json (651 Spiele)
| Launcher | Spiele | Titelbild | Logo | Cover | Header | Blur | Symbol |
|---|---|---|---|---|---|---|---|
| Steam | 636 | 631 | 628 | 631 | 629 | 628 | 636 (.ico) |
| Epic | 6 | 0 | 0 | 0 | 0 | 0 | 0 |
| GOG | 5 | 0 | 0 | 0 | 0 | 0 | 0 (5 Symbolpfade vorhanden) |
| Xbox | 2 | 0 | 0 | 0 | 0 | 0 | 0 (2 Symbolpfade vorhanden) |
| EA | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| Ubisoft | 1 | 0 | 0 | 0 | 0 | 0 | 0 (1 Symbolpfad vorhanden) |

- **Logo 628 statt 625:** 3 Spiele haben nur `logo_german.png`; deutsche Varianten werden bevorzugt.
- **Cover 631:** 455 im Format 600×900 (davon 2 deutsch) und 176 nur als Kapsel 300×450.
- **Ohne Titelbild (5):** alles Demos und Playtests, z. B. Drone Sector Demo und Warhammer Survivors Demo.
- **Mit Titelbild, aber ohne Logo (3):** Blue Estate, Dustforce, Homesick.

### Schwächen
1. **Andere Launcher:** Für die 15 Spiele von Epic, GOG, Xbox, EA und Ubisoft wird kein Cache gelesen. Ihre Symbole liegen zwar auf der Platte, der Server liefert aber absichtlich nur Steam-Pfade aus. Im Prototyp bekommen sie deshalb ein Monogramm; die App müsste diese Symbole und den Bildcache von GOG Galaxy lesen.
2. **Schwache Steam-Kapseln:** Ein Teil der 176 Spiele mit nur 300×450-Kapsel hat ein von Steam erzeugtes, nach unten verschmiertes Hochformat (sichtbar bei Blue Estate). Dafür bräuchte es eine Erkennung und als Ersatz einen Ausschnitt aus dem Titelbild.
3. **Auto-HDR-Kennzeichen fehlen in den Daten:** Die Bibliothekskopie ist älter als 1.4.0, daher ist Windows Auto HDR überall aus. Das AUTO-HDR-Kennzeichen erscheint nur über die Demo-Adresse `ahdr=`.
4. **Bewegung nicht per Foto prüfbar:** Headless Chrome startet CSS-Animationen nicht von selbst. Für die Fotos spult `shot=` Übergänge ans Ende bzw. hält sie an einer Stelle an. Ken Burns, Parallaxe und die Überblendung beim Spielwechsel sind im echten Browser noch nicht mit dem Auge geprüft.
5. **Farbauslese:** Bei dunklen Bildern wird es oft ein dumpfes Blau (Drone Sector Demo). Manche Steam-Symbole sind nur 32 px groß und wirken im erzeugten Titelbild etwas weich.
6. **Schmale Fenster:** Unter etwa 1600 px verlieren „Desktop“ und „Startmenü“ im Detailbereich ihr Symbol. Bei 1440×900 zeigt die Liste nur etwa 9 Zeilen.
7. **Nur Oberfläche:** SteamGridDB ist ohne jede Verbindung. Eine eigene Bilddatei gilt nur für die laufende Sitzung. Spielstart und „Spiel läuft“ sind simuliert.
8. **Browser-Mittel ohne direktes WPF-Gegenstück:** Für die Leucht-Ebenen (Mischmodus `screen`) und das Logo-Zuschneiden (`object-view-box`) steht der jeweilige WPF-Ersatz im Kommentar in `app.js`. Schriften kommen von Google Fonts; ohne Netz greift Segoe UI.

## Prototyp: Kritik mit Screenshots

# Showcase Hero v3: review with 24 prioritized changes and 14 bugs

**Verdict:** the Steam art pipeline works. Regal looks premium with real art. The weak spots are where the user is looking right now: the fallback art, the slim hero in the list, and errors in the HDR moment.

I started `server.ps1` myself, took 25 headless screenshots plus 16 real-time screenshots driven over the DevTools protocol, measured the layout in a same-origin harness, read all of `app.js`, `app.css`, `server.ps1` and `make-data.ps1`, and stopped the server again (only PID 38152, which I started). Screenshots and harness are in `<Notizordner>\proto\_review\`.

**Checked and working:**
- No broken images (0 of 19–27 per view) and no console errors in any run.
- Measured in real time:
  - The game switch crossfade finishes: new slot at 0.93 opacity after 350 ms, then 1.00.
  - Parallax works: image −13.7 px against logo +8.8 px.
  - Ken Burns is running.
  - After the window loses focus, all CSS animations are paused (0 running).
  - Fast arrow-key scrolling ends on the correct game.

## Prioritized changes (most impact first)

1. **Read real art for non-Steam games from local caches.**
   - These files exist on the user's disk and are not used today:
     - **GOG:** `C:\ProgramData\GOG.com\Galaxy\webcache\<user>\gog\<gameid>\*_glx_bg_top_padding_7.webp` (hero), `*_glx_vertical_cover.webp` (cover), `*_glx_square_icon_v2.webp` (icon). Baldur's Gate 3 and Cyberpunk 2077 both have them.
     - **Xbox:** `<installDir>\Resources\SplashScreen.png` (1920×1080, a real hero) and `Square480x480Logo.png`. Hellraiser and Palworld both have them.
     - **Ubisoft:** `Ubisoft Game Launcher\cache\assets\*.jpg` (25 files) and `data\games\*.ico`.
     - **Epic and EA:** the 256 px icon from the main EXE (`IShellItemImageFactory`).
   - Extend `make-data.ps1` and add exact read-only roots to the server allowlist.
   - Today the showcase titles BG3, Cyberpunk and RDR2 look like placeholders even though their images exist.
   - For future games, add a `FileSystemWatcher` on `librarycache\<appid>` so art appears once Steam caches it.

2. **Redesign the generated hero (for when nothing exists).**
   - Remove the floating app tile from the middle of the hero; today it collides with the header (BG3 at y = 105–210).
   - Use the icon as a full-bleed color field: downscale to 6×6, upscale with linear filtering. In WPF that is a free blur via `BitmapScalingMode=Linear`.
   - Show a 96–128 px icon left of the title, plus the outlined name pattern.
   - Pick the largest icon frame or EXE jumbo icon; never upscale the 32 px community JPG.
   - Generated covers: anchor the name to the bottom with `-webkit-line-clamp: 3`, place the tile at `top: 14%`, and scale the font by container width.

3. **Fix the HDR mislabel.** `kindOf()` and the pane tag show "SDR-Spiel"/"SDR" for status Unknown (612 games) and ModOnly.
   - Use "HDR unbekannt", "HDR per Mod" and "HDR teilweise".
   - Remove the duplicate "Natives HDR" + "HDR · NATIV" in the hero meta.
   - Move "keine EXE erkannt" out of the hero into the pane.

4. **Make the list (the default view) actually slim.**
   - Use `.v-liste{--heroH:clamp(180px,22vh,240px)}`.
   - Drop the hero CTA in the list (the pane already has Starten).
   - Measured today: 7 rows at 1280×800 and 11 at 1600×1000. Target ≥ 10 and ≥ 14.
   - Optionally collapse the hero to 120 px once the list is scrolled.

5. **Regal pane.** It is clipped: content 757 px vs 450 px visible at 1600×1000, and 772 vs 330 at 1280×800. It also duplicates Starten, Desktop and Startmenü from the hero.
   - Replace it with a compact glass "Automatik" card: HDR automatisch, Zurückstellen, Auto HDR, and a "Details" button that opens the full pane as a slide-over drawer.

6. **HDR moment.**
   - Clamp the bright-point position before use:
     ```js
     ly = clamp(ly, .35, .8); lx = clamp(lx, .3, .7)
     ```
     The light streak currently crosses the window title text (y ≈ 18 px for Gears) and the bloom sits behind the header.
   - Scale bloom alpha by the measured highlight luminance. The fixed 72 % white blob looks like a smudge on dark art (Fracture Point).
   - Start the particle canvas below the header (`top: 96px`); particles currently fly over the min/max/close buttons.
   - Limit the edge wash to a 2–3 px line plus the hero, in the game colors. Today the whole list and pane go pink; in light theme the meta text is unreadable for about 1 s.
   - Move the toast to bottom-center above the status bar; it currently covers the art during the moment.

7. **Performance: the sparkle loop runs uncapped while HDR is on.**
   - Measured: 289 rAF calls in 2 s (about 145 fps) of canvas clear and redraw in steady state, with focus.
   - This contradicts "max. 30 fps im Leerlauf" and the WPF note ("nur während des Effekts").
   - Stop spawning after 2.5 s, or throttle to 30 fps.
   - Debounce hero decode and pane rebuild by about 120 ms while a key is held.

8. **Darstellung jumps to the top on many clicks.** `setHdr`, `tog`, pause, `lsw`, `tb`, `pvsel` and `pvhdr` call `renderLook()` without keeping the scroll position.
   - Measured: 986 → 0 and 1390 → 0 (tray "HDR jetzt an", Taskleiste Hell, Effekte switch).
   - Fix: wrap `renderLook()` to save and restore `#lookMain.scrollTop`, as `saveLook()` already does.

9. **Darstellung at ≤ 1440 px.**
   - Color wells overflow (160 px vs 121 px) and role text is cut off.
   - Preset names are clipped ("Abendro", "Neongrü", "Gletsche", "Synthwa", "Polarlich").
   - The coverage table is 158 px wider than its card, so the "Symbol" column is cut off.
   - The preview caption wraps one word per line.
   - Fixes:
     - Wells: `grid-template-columns: repeat(auto-fit, minmax(150px, 1fr))`.
     - Presets: `repeat(auto-fill, minmax(76px, 1fr))`, which gives 4×2.
     - Table: numbers only below 1500 px.
     - Single-column layout below 1360 px.

10. **Library at 1280 px.**
    - "1 neu" sits under the monitor capsule (stats end at 382 px, capsule starts at 373 px).
    - The monitor name wraps onto two lines.
    - The toolbar overflows in Regal (`scrollWidth` 1287 > 1216; "+" at x = 1279–1319 is off-screen), and in Raster "+" slides under the pane.
    - Fixes: hide `.hds` below 1366 px; compact the capsule (drop "Minimum" and the meter); fold the filter chips into a "Filter (n)" dropdown before cutting the add button.

11. **Count-up and fake meter.**
    - The nit count-up shows invented intermediate values. Captured live at 450 ms: "331 nits / 184 nits". Show the real values immediately with a glow.
    - The 10-bar meter (3 bars lit when off, all lit when on) is decorative pseudo-data. Replace it with a 0–1000 nits scale marking Vollbild 254 and Spitze 456, or remove it.

12. **Tinting of the tray and window icon.** "HDR aus" is the most common state but only gets a 3 px c1 dot.
    - Draw the off glyph with a c1→c2 gradient stroke (tray and window icon).
    - Keep the filled gradient tile for "an".
    - Check the 16 px variants on a light taskbar.

13. **Light theme.**
    - The selected row is nearly invisible (HELLDIVERS 2). Use c1 at 14 % plus an inset 1 px c1 45 % ring.
    - Native-HDR "dim" text is about 2.4:1. Use `--t3` without opacity.
    - The Epic dot `#D7DAE3` is invisible on white; use `#8A8FA5` in light.
    - The hero-to-list fade leaves a milky band; use a 24 px fade instead.

14. **The "Windows-Animationseffekte beachten" switch does nothing.** `S.sws[2]` is never read, and `prefers-reduced-motion` only stops `.kb` and `.aur`; bokeh, rays, shimmer and ring keep running.
    - Map reduce, with the switch on, to `fx=1`.

15. **Path bidi bug.** `.exe small{direction:rtl}` renders "…\FunkTag" as "FunkTag\…" and moves the ellipsis to the end.
    - Do the middle ellipsis in JS. In WPF this needs a custom path-ellipsis converter.

16. **WPF feasibility gaps missing from the comment block.**
    - The tile tilt (`perspective`/`rotateX`/`rotateY`) has no cheap WPF equivalent. Replace it with 2D scale plus shine.
    - Add WPF equivalents for:
      - `mix-blend-mode: screen` (bloom/flash/edge): precomputed additive bitmaps.
      - `background-clip: text` shimmer: `OpacityMask`.
      - `-webkit-text-stroke`: `FormattedText.BuildGeometry`.
      - `color-mix(in oklab)`: precompute.

17. **Hero CTA in the list covers the art's focal point.** Starten and the chip sit on the Gears figure. If the CTA is kept, anchor it bottom-left under the meta row, as in Regal.

18. **Bilder settings.**
    - The disabled "Lokal" switch looks broken; replace it with an "Immer aktiv" lock tag.
    - The coverage table says "Symbol 5/5" for GOG and 2/2 for Xbox while the library shows monograms. Show "gefunden / genutzt", or make it true via item 1.

19. **Palette extraction.** If the dominant saturation is below 0.25 or lightness below 0.12, enforce a minimum chroma of 0.08 in OKLCH, or blend 50 % with the scheme color c2. This fixes the dull blue (Drone Sector Demo) and muddy brown backgrounds.

20. **Smeared Steam capsules (176 covers that exist only as 300×450 capsules).** If the bottom 35 % has row stdev < 6, use a crop of `library_hero` centered on lx with the logo overlay.

21. **Tile clutter.**
    - An always-visible switch plus "Steam" label over every cover hides cover logos (Agent 64).
    - Show the switch only on hover or selection; keep a small "AUTO" badge visible when on.
    - In Raster at 1600 px the chips lose their labels ("28 / 1 / 39" is ambiguous). Collapse the view-switch labels first.

22. **Wording.**
    - Toggle labels "An/Aus" → "Ein/Aus" (Windows German).
    - "nichts geladen" → "nichts heruntergeladen".
    - Use one date function: the hero shows "6. Okt. 2026" while the list shows "vor 1 Woche".
    - Remove the "Prototyp v3" tag in the app.

23. **Live preview capsule.** It says "HDR AUS" but pulses, because `.hdr .hcd` leaks from the app level. Scope it to `.stg.hdr`.

24. **Monogram bug.** "Baldur's Gate 3" becomes "BS".
    - Fix:
      ```js
      n.replace(/['’]s\b/gi, '')
      ```
      apply this before splitting words.
    - Also skip "-" tokens (affects SWAT 3).

## Bugs found

- **B1** Monogram "BS" for Baldur's Gate 3 (apostrophe split) — item 24.
- **B2** Unknown/ModOnly labelled "SDR-Spiel"/"SDR" (612+ games) — item 3.
- **B3** HDR streak crosses the title bar; bloom sits behind the header; particles over the caption buttons — item 6.
- **B4** Darstellung resets scroll to 0 on HDR, Taskleiste, Effekte and preview clicks — item 8.
- **B5** Toolbar overflow at 1280: "+" off-screen in Regal, under the pane in Raster — item 10.
- **B6** "1 neu" under the monitor capsule at 1280 — item 10.
- **B7** Darstellung overflow at 1280: wells, preset names, coverage table (158 px clipped), preview caption wrapping — item 9.
- **B8** EXE path bidi reversal: "FunkTag\…" — item 15.
- **B9** Generated cover name overlaps the monogram tile at tile width ≤ 150 px (Hellraiser, SWAT 3; seen at 1280) — item 2.
- **B10** Regal pane shows only 40–43 % of its content — item 5.
- **B11** Live preview capsule pulses while showing "HDR AUS" — item 23.
- **B12** "Windows-Animationseffekte beachten" switch not wired — item 14.
- **B13** Uncapped canvas loop (~145 fps) while HDR is on and the window is focused — item 7.
- **B14 (test path only)** `#src=gog` keeps Gears of War (Steam) selected in hero and pane while the grid shows only GOG; `applyHash` never applies refilter's reselect.
- **Contrast:** light-theme "dim" text about 2.4:1; the Epic dot is invisible in light.
- **Unreadable text over art:** only during the HDR moment (pink wash over the meta row in light theme). Regular states over bright art (Ascend to ZERO, Zero Stress King) stay readable.

## Prototyp: Überarbeitung (v4)

**Showcase Hero v4 ist fertig.** Alle 24 Punkte und 14 Fehler aus der Kritik sind umgesetzt. Ich habe 37 Bildschirmfotos aufgenommen und angesehen. Der Selbsttest besteht 21 von 21 Prüfungen ohne Laufzeitfehler. Der Server ist wieder beendet; ich habe nur meine eigenen Prozesse beendet, und geschrieben wurde nur unter `proto\`.

## Wie das mit den Titelbildern bei späteren Spielen funktioniert

Es gibt keine handgebauten Banner mehr. Jedes Spiel bekommt seine Bilder aus dem lokalen Cache des Launchers; gelesen wird nur, geladen wird nichts.

- **Steam:** Titelbild, Logo, Cover und Symbol aus dem Steam-Bildcache, wie bisher.
- **GOG (neu):** Titelbild und Cover aus dem Bildcache von GOG Galaxy. Dadurch haben Baldur's Gate 3, Cyberpunk 2077, die beiden SWAT-Spiele und Showgunners jetzt echte Bilder.
- **Ubisoft (neu):** Avatar hat Titelbild, Logo, Cover und Header.
- **Xbox (neu):** Das 480-px-Kachelbild aus dem Spielordner dient als Symbol. Daraus werden Titelbild und Cover erzeugt.
- **Epic und EA (neu):** Diese Launcher haben keinen Bildcache. Das Symbol wird aus der EXE-Datei des Spiels gelesen, meist in 256 px.

Kommt ein neues Spiel hinzu, erscheinen seine Bilder von selbst, sobald der Launcher sie speichert. In der App überwacht dafür ein `FileSystemWatcher` die Cache-Ordner.

Die Bewegung ist allgemein und funktioniert mit jedem Bild: langsames Heranzoomen, Parallaxe mit der Maus, Überblendung beim Spielwechsel, Hintergrundfarben aus dem Bild und der Licht-Effekt beim Einschalten von HDR.

## Starten

- Doppelklick auf `start.cmd`, oder:
  `powershell -NoProfile -ExecutionPolicy Bypass -File "<Notizordner>\proto\server.ps1"`
- Danach **http://127.0.0.1:8765/** öffnen. Strg+C beendet den Server.
- Daten neu erzeugen mit `make-data.ps1` im selben Ordner (dauert etwa 5 s).

## Zustände über die Adresse (`#a=b&c=d`)

| Schlüssel | Werte |
|---|---|
| `view` | `liste` (Standard), `regal`, `raster` |
| `page` | `darstellung`; dazu `look=lk-farben`, `lk-effekte`, `lk-bilder` oder `lk-symbole` |
| `theme` | `auto`, `hell`, `dunkel` |
| `preset`, `mode`, `c1`–`c3` | Farbvorlage, `duo` oder `tri`, eigene Farben als `RRGGBB` |
| `fx` | `voll`, `reduziert`, `aus` |
| `rm=1` (neu) | simuliert „Windows-Animationseffekte aus“ |
| `follow` | `0` oder `1` (Hintergrund folgt dem Spiel) |
| `hdr=on` | mit `freeze=0.35` als Standbild mitten im HDR-Moment |
| `sel` | Spiel-ID, Index oder Namensteil, z. B. `Heather`, `Crow`, `Hellraiser`, `305380` |
| Filter | `q`, `src`, `filter=auto,neu,hdr`, `sort`, `dir`, `thumb=symbol` |
| Menüs und Dialoge | `ctx=1`, `dlg=bild` (mit `tab=`), `dlg=symbol`, `menu=` |
| Neu | `lcol=1` (Liste mit eingeklapptem Titelbereich), `dw=1` (Regal mit Detail-Schublade), `toast=<Text>` |
| Abläufe | `run=1` (Spielstart simuliert), `pause=1`, `zz=1` (Effekte pausiert wie im Hintergrund) |
| Einstellungen und Test | `sgdb=1`, `tb=hell` oder `dunkel`, `pvhdr=1`, `ahdr=<Namen>` (Auto-HDR-Kennzeichen zur Vorführung), `gen=<IDs>` (erzeugte Bilder erzwingen), `shot=BxH`, `selftest=1` |

## Was geändert wurde

1. **Bilder anderer Launcher:** GOG, Ubisoft, Xbox, Epic und EA haben jetzt echte Bilder oder echte Symbole (siehe oben). Der Server liefert über `/file` nur genau die 35 Dateien aus, die `make-data.ps1` einträgt. Pfad-Ausbrüche und `win.ini` lehnt er mit 403 ab.
2. **Erzeugtes Titelbild neu gestaltet:**
   - Die schwebende Kachel in der Mitte ist weg.
   - Der Hintergrund ist ein Farbfeld: das Bild auf 6×6 Pixel verkleinert und gestreckt.
   - Der Name steht als Umriss-Muster im Hintergrund, das Symbol (54–92 px, im Regal 128 px) links vom Titel.
   - Es wird immer das größte vorhandene Symbolbild genommen und höchstens 1,5-fach vergrößert.
   - Erzeugte Cover: Name unten, höchstens 3 Zeilen, Schriftgröße passt sich der Kachelbreite an.
3. **HDR-Bezeichnungen:** „HDR unbekannt“, „HDR teilweise“, „HDR per Mod“ und „Natives HDR“ statt „SDR“. Im Titelbereich steht nur noch eine Angabe; „keine EXE erkannt“ steht im Detailbereich.
4. **Schlanke Liste:**
   - Der Titelbereich ist 180–240 px hoch, beim Scrollen 120 px. Die Starten-Schaltfläche steht nur noch im Detailbereich.
   - Sichtbare Zeilen: 10 bei 1280×800, 12 bei 1440×900, 14 bei 1600×1000, 15 bei 1920×1080. Vorher waren es 7 und 11.
5. **Regal:** Rechts steht eine kompakte Karte „Automatik“ mit drei Schaltern. Die Schaltfläche „Details“ (Taste I) öffnet den vollen Detailbereich als Schublade, Esc schließt sie. Die doppelte Starten-Zeile ist dort ausgeblendet.
6. **HDR-Moment:**
   - Lichtpunkt und Lichtstreifen bleiben im Bild: unter der Kopfzeile, nicht hinter dem Detailbereich.
   - Das Leuchten richtet sich nach der gemessenen Helligkeit des Bildes.
   - Die Partikel beginnen erst 96 px unter dem oberen Rand, also unter Titelleiste und Fensterknöpfen.
   - Statt der rosa Fläche über Liste und Detailbereich leuchtet nur noch eine 2-px-Linie in den Spielfarben am Fensterrand.
   - Hinweise erscheinen unten mittig über der Statuszeile.
7. **Leistung:**
   - Partikel laufen nur 2,5 s. Gemessen: 0 Bildaufrufe in 2 s bei eingeschaltetem HDR (vorher 289).
   - Bei gehaltener Pfeiltaste wird das Titelbild um 120 ms verzögert: Bei 10 Tastenwiederholungen wurde es während des Haltens einmal aktualisiert.
8. **Darstellung springt nicht mehr nach oben:** Bei allen sechs geprüften Bedienelementen bleibt die Scroll-Position stehen (1000 → 1000).
9. **Darstellung bei kleinen Fenstern:** Bei 1280–1920 px ist nichts mehr abgeschnitten. Farbfelder, Vorlagen (4×2), Tabelle und Bildunterschrift passen sich an; bei 1280 px wird die Seite einspaltig.
10. **Bibliothek bei 1280 px:**
    - Die Zähler „651 Spiele …“ sind unter 1366 px ausgeblendet, die Bildschirm-Kapsel ist kompakter.
    - Die Werkzeugleiste kürzt sich in sechs Stufen; zuletzt werden die Filter zu einem „Filter (n)“-Menü zusammengefasst.
    - Nichts läuft mehr aus dem Fenster oder unter den Detailbereich.
11. **Echte Werte statt Hochzählen:** Die nits-Werte stehen sofort da. Die 10-Balken-Anzeige ist ersetzt durch eine Skala 0–1000 nits mit Markierungen bei Vollbild 254 und Spitze 456.
12. **Tray- und Fenstersymbol:** „HDR aus“ ist jetzt eine Linie im Farbverlauf Akzent → Atmosphäre, mit mindestens 3:1 Kontrast je Taskleiste. „HDR ein“ bleibt die gefüllte Kachel.
13. **Helles Thema:** Die ausgewählte Zeile ist deutlich sichtbar, der gedämpfte Text hat etwa 5:1 Kontrast, der Epic-Punkt ist dunkler, der Übergang vom Titelbild zur Liste ist 24 px kurz.
14. **„Windows-Animationseffekte beachten“ wirkt jetzt:** Sind die Animationen in Windows aus, gilt mindestens „Reduziert“. Das wird angezeigt.
15. **EXE-Pfade** werden in der Mitte gekürzt, z. B. „…\Binaries\Win64“.
16. **WPF-Hinweise** sind ergänzt, siehe unten. Das 3D-Kippen der Kacheln ist ersetzt durch Vergrößern und Glanz.
17. In der Liste verdeckt keine Schaltfläche mehr das Titelbild; im Regal steht sie unten links.
18. **Einstellungen „Bilder“:**
    - Statt des ausgegrauten Schalters steht „Immer aktiv“ mit Schloss.
    - Die Tabelle zeigt „genutzt / Spiele“. Ein hochgestelltes „+n“ bedeutet: gefunden, aber nicht genutzt.
19. **Blasse Bilder:** Sehr blasse Farben werden kräftiger gemacht; fast graue Bilder werden zur Hälfte mit der Schemafarbe gemischt.
20. **Verschmierte Steam-Cover:** werden am Bildinhalt erkannt und durch einen Ausschnitt aus dem Titelbild mit Logo bzw. Name ersetzt.
21. **Kacheln:**
    - Der Schalter erscheint nur bei Mausberührung oder Auswahl; sonst zeigt ein kleines Sonnen-Zeichen, dass HDR automatisch an ist.
    - Die Quelle steht nur bei Mausberührung oder Auswahl.
    - Filter erscheinen nie als Chips, die nur eine Zahl tragen.
22. **Wortwahl:** „Ein/Aus“, „nichts heruntergeladen“, eine Datumsfunktion für alle Stellen. Der Hinweis „Prototyp v3“ ist entfernt.
23. Die Vorschau-Kapsel in „Darstellung“ pulsiert nur noch bei „HDR EIN“.
24. **Monogramm:** „Baldur's Gate 3“ ergibt „BG“; Bindestriche zählen nicht als Wort.

Fehler B14 ist ebenfalls behoben: Mit `#src=gog` wird jetzt ein GOG-Spiel ausgewählt.

## Bildschirmfotos

Alle unter `<Notizordner>\proto\shots\v4\`:

- **Liste:** `01-liste-standard`, `02-liste-hdr-moment`, `03-liste-hdr-ein-toast`, `04-liste-eingeklappt-beim-scrollen`
- **Regal und Raster:** `05-regal`, `06-regal-hdr-moment`, `07-regal-details-schublade`, `08-raster`
- **Ersatzbilder und andere Launcher:**
  - `09-ersatz-epic-titelbild-aus-exe-symbol` (Red Dead Redemption 2)
  - `10-ersatz-xbox-kachelbild-regal`
  - `11-gog-echtes-titelbild-name-statt-logo` (Baldur's Gate 3)
  - `12-ersatz-kleines-symbol-44px` (Dead Island 2)
  - `12b-ersatz-steam-demo-ohne-titelbild`
  - `13-steam-kapsel-verschmiert-ersetzt` (Blue Estate)
  - `14-ubisoft-titelbild-logo`
  - `15-quelle-gog-auswahl-folgt`
- **Menüs und Dialoge:** `16-kontextmenue`, `17-bild-waehlen-xbox-splash`, `18-symbol-aendern-exe`
- **Darstellung:** `19-darstellung`, `20-darstellung-bilder`, `21-darstellung-tray-taskleiste-hell`, `22-darstellung-hell-windows-animationen-aus`
- **Helles Thema:** `23-liste-hell`, `24-liste-hell-hdr-moment-helles-bild`, `25-regal-hell`
- **Farben und Abläufe:** `26-raster-tritonus-cyber`, `27-spiel-laeuft`, `28-effekte-pausiert-hintergrund`
- **Fenstergrößen:** `29-liste-1280x800`, `30-regal-1280x800`, `31-raster-1280x800`, `32-darstellung-1280x800`, `33-liste-1440x900`, `34-darstellung-1440x900`, `35-regal-1920x1080`
- **Symbolansicht:** `36-liste-symbole-filter-hdr`

Ich habe auch in Echtzeit nachgemessen (Chrome über das DevTools-Protokoll):
- Leerlauf: 0 Bildaufrufe pro Sekunde, das Heranzoomen läuft.
- Parallaxe: Bild und Logo verschieben sich gegenläufig.
- Überblendung beim Spielwechsel: nach 350 ms bei 0,60, danach bei 1,00.
- Fenster im Hintergrund: 0 laufende Animationen.
- Keine Fehler.

## Bildquellen in data.json

Angabe: genutzt / gefunden.

| Launcher | Spiele | Titelbild | Logo | Cover | Symbol |
|---|---|---|---|---|---|
| Steam | 636 | 631 / 631 | 628 / 628 | 628 / 631 (3 verschmiert) | 636 / 636 (1 aus der EXE) |
| GOG | 5 | 5 / 5 | 0 | 5 / 5 | 5 / 5 |
| Ubisoft | 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| Xbox | 2 | 0 / 2 | 0 | 0 | 2 / 2 (480-px-Kachel) |
| Epic | 6 | 0 | 0 | 0 | 6 / 6 (aus der EXE) |
| EA | 1 | 0 | 0 | 0 | 1 / 1 (aus der EXE) |

- 628 der Steam-Cover sind tatsächlich nur 300×450 px groß, auch wenn die Datei `library_600x900` heißt. Die Bilder sind aber in Ordnung; nur 3 sind verschmiert (Blue Estate, Dustforce, Homesick).
- Die Xbox-Startbilder (SplashScreen) sind Engine-Logos oder Titelkarten mit Schriftzug in der Mitte. Ich nutze sie deshalb nicht automatisch; unter „Bild wählen …“ lassen sie sich auswählen.

## Umsetzung in WPF je Effekt

Bei allen Effekten gilt dasselbe Anhalten: Beim Minimieren, beim Verlust des Fokus, beim Schließen in den Tray und während ein Spiel läuft werden die Animationen pausiert bzw. die Bildschleife abgemeldet. Beim Schließen des Fensters werden sie entfernt und die Bilder freigegeben. Im Leerlauf läuft nichts mit mehr als 30 Bildern pro Sekunde.

| Effekt | Umsetzung in WPF | Kosten | Wann es läuft |
|---|---|---|---|
| Heranzoomen (Ken Burns) | Animation auf Vergrößerung und Verschiebung, 28 s, hin und zurück | eine Textur-Transformation | nur bei „Voll“ |
| Parallaxe | zwei Verschiebungen, je 0,6 s weich animiert | nichts im Leerlauf | nur bei Mausbewegung |
| Spielwechsel | zwei Bildplätze: Deckkraft + Seitwärtsbewegung, 0,9–1,2 s | einmalig | bei gehaltener Taste 120 ms verzögert |
| Hintergrundfarben | Farben einmal pro Spiel aus einem 48-px-Bild lesen, gespeichert; Farbverlauf 1,4 s | einmalig | – |
| Farbfeld (Unschärfe gratis) | Steams fertiges Unschärfebild bzw. 6-px-Bild gestreckt mit linearer Filterung, kein Weichzeichner-Effekt | etwa null | statisch |
| Erzeugte Titelbilder und Cover | einmal als Bild gerendert und in `%LocalAppData%\HDRpilot\art\` gespeichert; Umrissschrift über `FormattedText.BuildGeometry` | einmalig | danach ein normales Bild |
| Lichtstreifen | Rechteck mit Verlauf, schräg, 1,6 s quer durchs Bild | 1,6 s | nur beim Einschalten von HDR |
| Aufhellung bei HDR | zweites Bild, vorab einmal aufgehellt; nur Deckkraft 1,2 s | einmalig | – |
| Leuchten und Blitz | Ersatz für den Mischmodus „screen“: Ellipse mit radialem Verlauf oder vorab gerechnetes Bild; Stärke nach gemessener Helligkeit | gering | einmalig |
| Partikel | höchstens 160 Punkte; Bildschleife nur 2,5 s angemeldet | 2,5 s | danach 0 Bilder |
| Leuchtlinie am Rand | Rahmen 2 px mit Verlauf, Deckkraft-Puls 1,8 s | gering | einmalig |
| Lichtstrahlen und Lichtpunkte | Rechtecke bzw. 16 Ellipsen mit Animation, 30 fps | gering | nur bei „Voll“ |
| Glanz über dem Titel | Text mit Verlauf + Maske (`OpacityMask`), alle 7 s | gering | ab „Reduziert“ aus |
| Auswahlring der Kachel | gestrichelter Rahmen, Strich-Versatz 2,6 s | nur die ausgewählte Kachel | – |
| Kachel unter der Maus | nur 2D: Vergrößern 1,035 + Anheben + Glanz | nur bei Mausberührung | – |
| Logo zuschneiden | einmal auf den sichtbaren Teil zuschneiden (`CroppedBitmap`), gespeichert | einmalig | – |
| Farbmischungen (`color-mix`) | im Programm vorab berechnen (OKLab) | keine | – |
| Pfadkürzung in der Mitte | eigener Konverter mit Textmessung | keine | – |
| GOG-Bilder (WebP) | Windows-11-WebP-Codec, sonst einmal nach PNG wandeln | einmalig | – |

## Schwächen

1. **Fehlende Logos:** GOG-, Xbox-, Epic- und EA-Spiele haben kein Logo im Cache; dort steht der Name in der Titelschrift.
2. **Dead Island 2:** Die EXE enthält nur ein 44-px-Symbol. Es wird höchstens 1,5-fach vergrößert und sieht im großen Titelbereich klein aus.
3. **Epic:** Den Bildcache des Epic-Launchers habe ich nicht untersucht. Bilder für Epic-Spiele brauchen deshalb SteamGridDB oder „Bild wählen …“.
4. **Auto-HDR-Kennzeichen:** Die Bibliothekskopie ist älter als Version 1.4.0, daher ist Windows Auto HDR überall aus. Das Kennzeichen erscheint nur über `ahdr=` in der Adresse.
5. **Nur Oberfläche:** SteamGridDB ist ohne Verbindung, eine eigene Bilddatei gilt nur für die laufende Sitzung, Spielstart und „Spiel läuft“ sind simuliert. Schriften kommen von Google Fonts.
6. **Bildschirmfotos:** Die Fotos zeigen Standbilder. Die Bewegung habe ich nur über Messwerte geprüft, nicht im echten Browser mit dem Auge.

## Dateien

Alles im Ordner `<Notizordner>\proto\`:

- `index.html`, `app.css`, `app.js` (die WPF-Hinweise stehen oben im Kommentar)
- `data.json`, `art\` (8 aus EXE-Dateien gelesene Symbole)
- `make-data.ps1`, `server.ps1`, `start.cmd`
- `shots\v4\` (Bildschirmfotos)
- `_old\` (Stand v3), `_test\` (Testwerkzeuge)

