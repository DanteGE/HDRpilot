/* HDRpilot · Showcase Hero v4 – Prototyp (Vanilla JS, kein Build-Schritt)
 *
 * Bilder: ausschließlich lokal. data.json (make-data.ps1) sagt pro Spiel, welche Dateien in den Launcher-Caches liegen:
 * Steam appcache\librarycache, GOG Galaxy\webcache, Ubisoft Game Launcher\cache\assets, Xbox <Spiel>\Resources,
 * Epic/EA: Symbol aus der Haupt-EXE. server.ps1 liefert sie unter /steam/…, /ico?path=…, /file?path=… (Allowlist) und
 * /art/… aus. Keine Netzwerkaufrufe außer Google Fonts. SteamGridDB ist nur Oberfläche (kein Aufruf).
 *
 * ── Umsetzung in WPF: Primitive · Kosten · Anhalten ───────────────────────────────────────────────────────────────────
 *  Anhalten gilt für alle Dauer-Effekte gleich: Window.StateChanged (Minimized), Deactivated, IsVisible=false (Fenster
 *  geschlossen, App im Tray) und „Spiel läuft“ → Storyboard.Pause() bzw. Rendering-Handler abmelden; Fenster zu →
 *  Storyboards entfernen, Bitmaps freigeben. Nichts läuft im Leerlauf mit mehr als 30 fps (Timeline.DesiredFrameRate=30).
 *
 *  Ken-Burns-Drift      Image in Grid, RenderTransform = Scale+Translate; DoubleAnimation 28 s AutoReverse Forever.
 *                       Kosten: 1 Transform auf einer GPU-Textur. Stufe „Reduziert“/„Aus“: keine Animation.
 *  Parallaxe            MouseMove auf dem Hero setzt Zielwerte; je Ebene TranslateTransform (Bild −14 px, Logo +9 px,
 *                       Lichtpunkte −40 px) per DoubleAnimation 0,6 s QuinticEase – kein Per-Frame-Code. Stoppt mit der Maus.
 *  Spielwechsel         Zwei Image-Slots: Opacity 0→1 (0,9 s) + TranslateX 70→0 + Scale 1,04→1, alter Slot −70 px/aus.
 *                       Logo/Text gestaffelt (BeginTime +60 ms). Bei gehaltener Taste 120 ms entprellt (nur Zeile sofort).
 *  Umgebungsfarben      Einmal je Spiel: BitmapImage(DecodePixelWidth=48) → FormatConvertedBitmap → CopyPixels → Farb-Buckets
 *                       wie extract() unten; Ergebnis im Bild-Cache (JSON). Blasse Bilder: Mindest-Chroma 0,08 (OKLCH) bzw.
 *                       50 % Schemafarbe. Hintergrund: RadialGradientBrush-Stopps per ColorAnimation 1,4 s (einmalig).
 *  Farbfeld (Blur gratis) library_hero_blur.jpg von Steam bzw. ein 6×6-Bild aus Symbol/Titelbild (DecodePixelWidth=6),
 *                       Stretch=Fill, RenderOptions.BitmapScalingMode=Linear → weiche Fläche ohne BlurEffect. Kosten: ~0.
 *  Erzeugtes Titelbild  DrawingGroup: Farbfeld + Abdunklung + Name als Umriss (FormattedText.BuildGeometry → Path mit Stroke,
 *                       ersetzt -webkit-text-stroke) + Symbol 96–128 px links vom Titel (größtes ICO-Bild bzw. EXE-Jumbo-
 *                       Symbol über IShellItemImageFactory; nie ein 32-px-Bild hochskalieren). Einmal per RenderTargetBitmap
 *                       nach %LocalAppData%\HDRpilot\art\ – danach ein normales Bild.
 *  Erzeugtes Cover      wie oben im Hochformat; Name unten, max. 3 Zeilen (TextTrimming, MaxHeight), Größe nach Breite (Viewbox).
 *                       Verschmierte Steam-Kapsel (coverSmear) → Ausschnitt aus library_hero (CroppedBitmap um lx) + Logo.
 *  Lichtstrahlen        3 Rectangles, LinearGradientBrush, SkewTransform; Opacity/Translate-Animation 14 s, 30 fps.
 *  Lichtpunkte          16 Ellipsen mit RadialGradientBrush, Translate/Opacity-Animation; Farben aus dem Spiel.
 *  HDR-Moment (2,5 s)   Lichtstreifen: Rectangle + LinearGradientBrush + SkewTransform, TranslateX über die Breite (1,6 s).
 *                       Boost: zweites Image mit VORAB berechneter Bitmap (Helligkeit/Kontrast/Sättigung einmal per
 *                       WriteableBitmap beim Laden) → nur Opacity 0→1 (1,2 s). Bloom/Blitz: mix-blend-mode „screen“ gibt es in
 *                       WPF nicht → vorab additiv gerechnete Bitmap (Bild + Licht) bzw. Ellipse mit RadialGradientBrush,
 *                       Deckkraft nach gemessener Spitzlicht-Helligkeit (hl). Position auf den Bildbereich unter der
 *                       Kopfzeile geklemmt. Partikel: ≤ 160 Ellipsen in einem DrawingVisual, CompositionTarget.Rendering
 *                       nur 2,5 s angemeldet (danach 0 Frames). Kante: Border 2 px mit LinearGradientBrush (Spielfarben),
 *                       Opacity-Puls 1,8 s – keine Fläche über Liste/Detailbereich.
 *  Titel-Glanz          background-clip:text → TextBlock mit Foreground-Verlauf + OpacityMask (LinearGradientBrush, dessen
 *                       Offsets animiert werden); alle 7 s, Stufe „Reduziert“ aus.
 *  Auswahlring Kachel   Rectangle StrokeDashArray, StrokeDashOffset-Animation 2,6 s (nur ausgewählte Kachel).
 *  Kachel-Hover         nur 2D: ScaleTransform 1,035 + TranslateY −10 px + Glanz (RadialGradientBrush, Center = Maus).
 *                       Das frühere 3D-Kippen (perspective/rotateX/Y) entfällt – kein günstiges Gegenstück in WPF.
 *  Logo zuschneiden     object-view-box → CroppedBitmap auf die sichtbaren Pixel (einmal beim Laden, gespeichert).
 *  color-mix(in oklab)  alle Mischfarben im ViewModel vorab berechnen (OKLab-Funktionen wie unten), als SolidColorBrush.
 *  Pfad-Kürzung         Mitte auslassen („C:\…\Binaries\Win64“): eigener IValueConverter mit FormattedText-Messung.
 *  Liste (650 Spiele)   ListView + VirtualizingStackPanel (Recycling), Vorschaubilder async DecodePixelWidth=128.
 *  Raster/Regal         VirtualizingWrapPanel bzw. horizontales VirtualizingStackPanel.
 *  Bildquellen          GOG-WebP: WIC-WebP-Codec (unter Windows 11 vorinstalliert), sonst einmal nach PNG wandeln.
 *                       FileSystemWatcher auf librarycache\<appid>, GOG webcache\…\gog, Ubisoft cache\assets: neue Dateien
 *                       → Bild-Cache des Spiels verwerfen, Bild nachladen (Bilder für künftige Spiele erscheinen von selbst).
 *  Stufe „Reduziert“    nur Übergänge ≤ 0,3 s, keine Dauer-Storyboards; „Aus“: Werte direkt setzen. Windows-Option
 *                       „Animationseffekte“ aus (SystemParameters.ClientAreaAnimation) + Schalter an → mindestens „Reduziert“.
 * ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
 */
'use strict';
(() => {
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
const app = $('#app');
const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const tf = v => v ? 'true' : 'false';
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/* ---------- Symbole ---------- */
const I = {
  play: 'M8 5v14l11-7z', stop: 'M7 7h10v10H7z', pause: 'M9 5v14M15 5v14',
  sun: 'M16 12a4 4 0 1 1-8 0 4 4 0 1 1 8 0zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
  star: 'M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z',
  mon: 'M5 4h14a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM8 21h8M7 13l3-3 2 2 4-4',
  list: 'M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01', shelf: 'M3 6h18M3 18h18M6 9v6M10 9v6M14 9v6M18 9v6', grid: 'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z',
  search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM20 20l-3.5-3.5', down: 'M6 9l6 6 6-6', left: 'M15 18l-6-6 6-6', right: 'M9 18l6-6-6-6',
  clock: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 7v5l3 2', refresh: 'M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7', plus: 'M12 5v14M5 12h14',
  more: 'M5 12h.01M12 12h.01M19 12h.01', desk: 'M3 4h18v12H3zM8 20h8M12 16v4', startm: 'M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z',
  file: 'M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5', img: 'M3 5h18v14H3zM3 15l5-5 4 4 3-3 6 6M15 9h.01',
  shape: 'M4 4h7v7H4zM17.5 4a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7zM4 20l3.5-6 3.5 6zM14 14h7v7h-7z',
  folder: 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z',
  hide: 'M3 3l18 18M10.6 10.6a2 2 0 0 0 2.8 2.8M9.9 5.2A10 10 0 0 1 12 5c6.4 0 10 7 10 7a17 17 0 0 1-3.2 4M6.6 6.6A17 17 0 0 0 2 12s3.6 7 10 7a9.7 9.7 0 0 0 5.4-1.6',
  check: 'M5 12.5l4.5 4.5L19 7.5', link: 'M10 14L21 3M15 3h6v6M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6',
  pulse: 'M3 12h4l3-7 4 14 3-7h4', info: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 16v-4M12 8h.01',
  key: 'M14.5 4a5.5 5.5 0 1 1-4.9 8l-6.1 6.1V21H6v-2h2v-2h2l1.1-1.1M16 8h.01', cloud: 'M7 18a5 5 0 1 1 1-9.9A6 6 0 0 1 19.5 10 4 4 0 0 1 18 18z',
  disk: 'M4 5h16v6H4zM4 13h16v6H4zM7.5 8h.01M7.5 16h.01', spark: 'M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8zM19 16l.8 2.2L22 19l-2.2.8L19 22l-.8-2.2L16 19l2.2-.8z',
  pal: 'M12 3a9 9 0 1 0 0 18c1 0 1.5-.8 1.5-1.6 0-1.2-1-1.4-1-2.4 0-.9.7-1.5 1.6-1.5H16a5 5 0 0 0 5-5c0-4.1-4-7.5-9-7.5zM7.5 11.5h.01M10.5 7.5h.01M15 8h.01',
  swap: 'M7 7h12l-3-3M17 17H5l3 3', reset: 'M4 12a8 8 0 1 0 2.3-5.7M4 4v5h5', arrow: 'M5 12h14M13 6l6 6-6 6', eye: 'M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
  bolt: 'M13 2L4 14h7l-1 8 9-12h-7z', tray: 'M4 5h16v10H4zM2 19h20', theme: 'M12 3a9 9 0 1 0 9 9 7 7 0 0 1-9-9z', ok: 'M5 12.5l4.5 4.5L19 7.5',
  gen: 'M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z', upload: 'M12 16V4M7 9l5-5 5 5M4 20h16', power: 'M12 3v8M6.3 6.8a8 8 0 1 0 11.4 0',
  logs: 'M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5M9 13h6M9 17h4', set: 'M4 7h10M18 7h2M4 17h4M12 17h8M16 5v4M10 15v4',
  lock: 'M6 11h12v9H6zM8.5 11V8a3.5 3.5 0 0 1 7 0v3', filter: 'M3 5h18l-7 8v6l-4-2v-4z', x: 'M6 6l12 12M18 6L6 18', panel: 'M3 4h18v16H3zM15 4v16',
  nav: ['M4 7h10M18 7h2M4 17h4M12 17h8M16 5v4M10 15v4', 'M3 5h18v11H3zM8 20h8M12 16v4', 'M12 21a8 8 0 1 0 0-16 8 8 0 0 0 0 16zM12 9v4l2 2M9 2h6', 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z', 'M3 5h18l-7 8v6l-4-2v-4z', 'M12 3a9 9 0 1 0 0 18c1 0 1.5-.8 1.5-1.6 0-1.2-1-1.4-1-2.4 0-.9.7-1.5 1.6-1.5H16a5 5 0 0 0 5-5c0-4.1-4-7.5-9-7.5zM7.5 11.5h.01M10.5 7.5h.01M15 8h.01', 'M14.7 6.3a4 4 0 0 0-5.4 5.4L3 18l3 3 6.3-6.3a4 4 0 0 0 5.4-5.4l-2.5 2.5-2.4-.6-.6-2.4z', 'M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5']
};
const ic = (d, cls = '') => `<svg class="ic ${cls}" viewBox="0 0 24 24" aria-hidden="true"><path d="${d}"></path></svg>`;
const GEAR = '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="7.6" stroke-width="3.4" stroke-dasharray="2.9 3.07"></circle><circle cx="12" cy="12" r="5.4"></circle><circle cx="12" cy="12" r="2"></circle></svg>';

/* ---------- Farbvorlagen: Name, [Akzent, Atmosphäre, Glanz], Standardanzahl ---------- */
const P = [['Abendrot', ['#FF8A3D', '#FF3D7F', '#FFD166'], 2], ['Cyber', ['#00E5FF', '#7C5CFF', '#FF2BD6'], 3], ['Neongrün', ['#C6FF3D', '#00D68F', '#00B3FF'], 2], ['Royal', ['#8B5CF6', '#EC4899', '#F5B83D'], 3],
  ['Gletscher', ['#9BE7FF', '#3B82F6', '#C4B5FD'], 2], ['Synthwave', ['#FF71CE', '#B967FF', '#01CDFE'], 3], ['Glut', ['#FFB238', '#FF3B30', '#FFE08A'], 2], ['Polarlicht', ['#3DFFB0', '#3DB8FF', '#A35CFF'], 3]];
/* Quellenfarben; Epic ist im hellen Thema dunkler (sonst auf Weiß unsichtbar) → CSS-Variable */
const SRC = { Steam: ['Steam', '#66C0F4'], Epic: ['Epic Games', 'var(--epic)'], Gog: ['GOG', '#B45CFF'], Xbox: ['Xbox', '#3FB950'], EA: ['EA app', '#FF5A5A'], Ubisoft: ['Ubisoft Connect', '#4F8BFF'] };
const srcName = s => (SRC[s] || [s])[0], srcCol = s => (SRC[s] || [0, '#C8CCD8'])[1];
const CACHE_N = { steam: 'Steam-Cache', gog: 'GOG-Galaxy-Cache', ubisoft: 'Ubisoft-Cache' };
const HDRL = { Native: ['Nativ', 'nat', 'Natives HDR'], Limited: ['Teilweise', 'lim', 'HDR teilweise'], ModOnly: ['Mod', 'mod', 'HDR per Mod'], Unknown: ['—', 'unk', 'HDR unbekannt'] };
const FXN = ['Voll', 'Reduziert', 'Aus'];

/* ---------- Speicher (nur Komfort: Ansicht, Farben …) ---------- */
const LS = {
  get(k, d) { try { const v = localStorage.getItem('hp4.' + k); return v == null ? d : JSON.parse(v); } catch (e) { return d; } },
  set(k, v) { try { localStorage.setItem('hp4.' + k, JSON.stringify(v)); } catch (e) { /* privat/gesperrt */ } }
};

/* ---------- Zustand ---------- */
const S = {
  page: 'bibliothek', view: LS.get('view', 'liste'), theme: LS.get('theme', 'auto'), fx: LS.get('fx', 0), follow: LS.get('follow', true),
  c: LS.get('colors', P[0][1].slice()), n3: LS.get('n3', false), pr: LS.get('preset', 0), autoTxt: LS.get('autoTxt', true),
  thumb: LS.get('thumb', 'bild'), sort: LS.get('sort', { k: 'lp', d: -1 }), sgdb: LS.get('sgdb', false), sgdbKey: '', tb: LS.get('tb', 'dunkel'),
  sws: LS.get('sws', [1, 1, 1, 0]), ovr: LS.get('ovr', {}),
  hdr: false, hk: 0, pause: false, run: -1, sel: 0, q: '', src: 'all', fa: false, fn: false, fh: false,
  zz: false, zzWhy: '', forceZz: false, ctx: null, dlg: null, menu: null, off: 0, pvHdr: false, pvSel: -1, ck: 0, tk: 0, vis: [],
  dw: false, lcol: false, rm: false, lastKey: 0
};
let G = [], D = null, MON = { name: 'MAG 341C OLED', peak: 456, full: 254, min: '0,0000', bits: 10 };
const mqReduce = window.matchMedia ? matchMedia('(prefers-reduced-motion: reduce)') : { matches: false, addEventListener() { } };
/* wirksame Effektstufe: Windows „Animationseffekte“ aus + Schalter an → mindestens „Reduziert“ */
const reducedByOs = () => !!S.sws[2] && (S.rm || mqReduce.matches);
const fxEff = () => reducedByOs() ? Math.max(S.fx, 1) : S.fx;

/* ---------- Farben ---------- */
const rgb = h => { const n = parseInt(h.slice(1), 16); return [n >> 16 & 255, n >> 8 & 255, n & 255]; };
const hex = c => '#' + c.map(v => clamp(Math.round(v), 0, 255).toString(16).padStart(2, '0')).join('').toUpperCase();
const mix = (x, y, t) => { const p = rgb(x), q = rgb(y); return hex(p.map((v, i) => v + (q[i] - v) * t)); };
const lum = h => { const c = rgb(h).map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }); return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]; };
const ratio = (x, y) => { const a = lum(x), b = lum(y); return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05); };
const readable = (c, bg, dark, target = 4.6) => { let x = c; for (let i = 0; i < 30 && ratio(x, bg) < target; i++) x = mix(x, dark ? '#FFFFFF' : '#000000', 0.1); return x; };
function hsl2hex(h, s, l) { h = ((h % 360) + 360) % 360 / 360; const f = n => { const k = (n + h * 12) % 12, a = s * Math.min(l, 1 - l); return l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)); }; return hex([f(0) * 255, f(8) * 255, f(4) * 255]); }
function rgb2hsl(r, g, b) { r /= 255; g /= 255; b /= 255; const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2; let h = 0, s = 0; if (mx !== mn) { const d = mx - mn; s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn); h = mx === r ? (g - b) / d + (g < b ? 6 : 0) : mx === g ? (b - r) / d + 2 : (r - g) / d + 4; h *= 60; } return [h, s, l]; }
/* OKLab/OKLCH – Mindest-Chroma für blasse Bilder, Mischungen wie color-mix(in oklab) (WPF: vorab rechnen) */
const s2l = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
const l2s = c => 255 * (c <= 0.0031308 ? 12.92 * c : 1.055 * Math.pow(Math.max(0, c), 1 / 2.4) - 0.055);
function toOklab(h) { const [r, g, b] = rgb(h).map(s2l); const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b), m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b), s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b); return [0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s, 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s, 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s]; }
function fromOklab(L, A, B) { const l = Math.pow(L + 0.3963377774 * A + 0.2158037573 * B, 3), m = Math.pow(L - 0.1055613458 * A - 0.0638541728 * B, 3), s = Math.pow(L - 0.0894841775 * A - 1.291485548 * B, 3); return hex([l2s(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s), l2s(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s), l2s(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s)]); }
const mixOk = (x, y, t) => { const a = toOklab(x), b = toOklab(y); return fromOklab(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t); };
function minChroma(h, cMin) { const [L, A, B] = toOklab(h), C = Math.hypot(A, B); if (C >= cMin || C < 1e-4) return h; const k = cMin / C; return fromOklab(L, A * k, B * k); }
function hashHue(s) { let h = 2166136261; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0) % 360; }
function namePal(g) { const h = hashHue(g.n); return { a1: hsl2hex(h, 0.55, 0.16), a2: hsl2hex(h + 18, 0.78, 0.55), a3: hsl2hex(h - 36, 0.85, 0.74), lx: 0.62, ly: 0.45, hl: 0.55, gen: true }; }
const c3Of = () => S.n3 ? S.c[2] : mix(S.c[1], '#FFFFFF', 0.45);

/* ---------- Bilder eines Spiels (inkl. eigener Auswahl) ---------- */
const gkey = g => g.src + ':' + g.sid;
const blobs = {};   // Dateiauswahl nur für diese Sitzung (Objekt-URLs), nichts wird hochgeladen
function art(g) {
  const o = S.ovr[gkey(g)] || {}, b = blobs[gkey(g)] || {}, im = g.img || {};
  const alt = k => { const m = /^alt:(\d+)$/.exec(o[k] || ''); return m && im.alts && im.alts[+m[1]] ? im.alts[+m[1]].url : null; };
  const pick = k => b[k] ? b[k] : o[k] === 'gen' ? null : alt(k) || im[k];
  const smear = im.coverSmear && !o.cover && !b.cover;
  const r = { hero: pick('hero'), logo: pick('logo'), cover: smear ? null : pick('cover'), header: im.header, blur: o.hero || b.hero ? null : im.blur,
    icon: b.icon || (o.icon === 'mono' ? null : im.icon), iconPx: b.icon ? 256 : im.iconPx || 0, iconOpaque: !b.icon && !!im.iconOpaque, smear };
  const where = CACHE_N[im.heroFrom] || 'Launcher-Cache';
  r.heroSrc = b.hero ? 'Eigene Datei' : alt('hero') ? 'Xbox-Paket (Splash)' : r.hero ? where : 'erzeugt';
  r.logoSrc = b.logo ? 'Eigene Datei' : r.logo ? where : 'Name als Schrift';
  r.coverSrc = b.cover ? 'Eigene Datei' : r.cover ? (CACHE_N[g.src.toLowerCase()] || where) + (im.coverKind === 'capsule' ? ' (Kapsel)' : '') : smear ? 'aus Titelbild erzeugt (Steam-Kapsel verschmiert)' : r.hero ? 'aus Titelbild erzeugt' : im.iconOpaque ? 'aus Kachelbild erzeugt' : 'aus Symbol erzeugt';
  r.iconSrc = b.icon ? 'Eigene Datei' : r.icon ? ({ ico: '.ico-Datei', exe: 'aus ' + (im.iconExe || 'der EXE'), 'xbox-tile': 'Xbox-Kachelbild', 'steam-cache': 'Steam-Cache', bild: 'Bilddatei' }[im.iconKind] || 'Symbol') + ' · ' + r.iconPx + ' px' : 'Monogramm';
  return r;
}
/* Monogramm: „Baldur's Gate 3“ → BG (Genitiv-s weg), Bindestriche zählen nicht als Wort */
const mono = n => { const w = n.replace(/['’]s\b/gi, '').replace(/[™®©:’'"!?.,\-–—()\[\]]/g, ' ').split(/\s+/).filter(x => x && x !== '-' && /[A-Za-z0-9ÄÖÜäöü]/.test(x)); return ((w[0] || '?')[0] + (w[1] ? w[1][0] : (w[0] || '')[1] || '')).toUpperCase(); };

/* ---------- Farben und Farbfeld aus dem Bild (Canvas, gleiche Herkunft) ---------- */
const PAL = new Map(Object.entries(LS.get('pal', {})));
let palSave = 0;
const palQueue = new Map();
const palUrl = g => { const a = art(g); return a.hero || a.icon || ''; };
function palOf(g) { return PAL.get(gkey(g) + '|' + palUrl(g)) || null; }
function palette(g) {
  const a = art(g), url = a.hero || a.icon, k = gkey(g) + '|' + (url || '');
  if (PAL.has(k)) return Promise.resolve(PAL.get(k));
  if (!url) { const p = namePal(g); PAL.set(k, p); return Promise.resolve(p); }
  if (palQueue.has(k)) return palQueue.get(k);
  const pr = new Promise(res => {
    const im = new Image(); im.decoding = 'async';
    im.onload = () => {
      let p; try { p = extract(im, !!a.hero); } catch (e) { p = null; }
      if (!p) p = namePal(g);
      PAL.set(k, p); palQueue.delete(k);
      clearTimeout(palSave); palSave = setTimeout(() => { const o = {}; let n = 0; for (const [kk, v] of PAL) { if (n++ > 700) break; o[kk] = v; } LS.set('pal', o); }, 800);
      res(p);
    };
    im.onerror = () => { const p = namePal(g); PAL.set(k, p); palQueue.delete(k); res(p); };
    im.src = url;
  });
  palQueue.set(k, pr); return pr;
}
const cv = document.createElement('canvas'), cx2 = cv.getContext('2d', { willReadFrequently: true });
const cvF = document.createElement('canvas'), cxF = cvF.getContext('2d', { willReadFrequently: true });
function extract(im, isScene) {
  const W = 48, H = clamp(Math.round(48 * im.naturalHeight / Math.max(1, im.naturalWidth)), 12, 48);
  cv.width = W; cv.height = H; cx2.clearRect(0, 0, W, H); cx2.imageSmoothingQuality = 'high'; cx2.drawImage(im, 0, 0, W, H);
  const d = cx2.getImageData(0, 0, W, H).data, B = new Map();
  let ar = 0, ag = 0, ab = 0, an = 0, best = -1, bx = 0.62, by = 0.4;
  const L = new Float32Array(W * H);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const o = (y * W + x) * 4, r = d[o], g = d[o + 1], b = d[o + 2], al = d[o + 3];
    if (al < 120) continue;
    ar += r; ag += g; ab += b; an++;
    L[y * W + x] = 0.2126 * r + 0.7152 * g + 0.0722 * b;
    const [h, s, l] = rgb2hsl(r, g, b), key = (r >> 5) << 6 | (g >> 5) << 3 | (b >> 5);
    let e = B.get(key); if (!e) { e = { n: 0, r: 0, g: 0, b: 0, s: 0 }; B.set(key, e); }
    e.n++; e.r += r; e.g += g; e.b += b; e.s += s * (l > 0.18 && l < 0.82 ? 1 : 0.3);
  }
  if (!an) return null;
  // hellste Stelle (3×3) im sichtbaren Bereich → Spitzlicht/Bloom; hl = ihre Helligkeit (0–1) steuert die Bloom-Stärke
  if (isScene) for (let y = 1; y < H - 1; y++) for (let x = Math.floor(W * 0.22); x < Math.floor(W * 0.86); x++) {
    let s = 0; for (let j = -1; j <= 1; j++) for (let i = -1; i <= 1; i++) s += L[(y + j) * W + x + i];
    if (s > best) { best = s; bx = x / W; by = y / H; }
  }
  const hl = isScene ? clamp(best / 9 / 255, 0, 1) : 0.55;
  const list = Array.from(B.values()).map(e => { const r = e.r / e.n, g = e.g / e.n, b = e.b / e.n, [h, s, l] = rgb2hsl(r, g, b); return { r, g, b, h, s, l, w: e.n * (0.15 + e.s / e.n) * (0.4 + s) }; }).sort((p, q) => q.w - p.w);
  const v1 = list.find(c => c.s > 0.22 && c.l > 0.14 && c.l < 0.86) || list[0];
  const v2 = list.find(c => c !== v1 && c.s > 0.18 && c.l > 0.14 && Math.min(Math.abs(c.h - v1.h), 360 - Math.abs(c.h - v1.h)) > 28) || v1;
  const [ah, as] = rgb2hsl(ar / an, ag / an, ab / an);
  // blasse oder sehr dunkle Bilder: Mindest-Chroma 0,08 (OKLCH); fast grau → beim Anwenden 50 % Schemafarbe
  const dull = v1.s < 0.08 ? 'grau' : (v1.s < 0.25 || v1.l < 0.12) ? 'blass' : '';
  let a1 = hsl2hex(v1.s > 0.25 ? v1.h : ah, clamp(Math.max(as, v1.s) * 0.85, 0.2, 0.75), 0.15);
  let a2 = hsl2hex(v1.h, clamp(v1.s * 1.15, 0.45, 0.95), clamp(v1.l, 0.46, 0.6));
  let a3 = hsl2hex(v2.h, clamp(v2.s * 1.1, 0.5, 1), clamp(v2.l + 0.22, 0.68, 0.82));
  if (dull === 'blass') { a1 = minChroma(a1, 0.05); a2 = minChroma(a2, 0.08); a3 = minChroma(a3, 0.08); }
  return { a1, a2, a3, lx: +bx.toFixed(3), ly: +by.toFixed(3), hl: +hl.toFixed(2), dull, field: field(im, isScene ? 8 : 6, isScene ? 3 : 6, a1) };
}
/* Farbfeld: Bild auf 6×6 (Titelbild 8×3) verkleinern, leicht sättigen/abdunkeln; gestreckt mit linearer Filterung = weiche
   Fläche ohne Live-Unschärfe (WPF: DecodePixelWidth=6 + BitmapScalingMode=Linear) */
function field(im, w, h, base) {
  cvF.width = w; cvF.height = h; cxF.fillStyle = base; cxF.fillRect(0, 0, w, h); cxF.imageSmoothingQuality = 'high';
  const iw = cv.width, ih = cv.height, s = Math.max(w / iw, h / ih) * (w === h ? 1.3 : 1);
  cxF.drawImage(cv, (w - iw * s) / 2, (h - ih * s) / 2, iw * s, ih * s);   // aus der 48-px-Stufe → saubere Mittelung
  const id = cxF.getImageData(0, 0, w, h), d = id.data;
  for (let i = 0; i < d.length; i += 4) { const [hh, ss, ll] = rgb2hsl(d[i], d[i + 1], d[i + 2]); const c = rgb(hsl2hex(hh, clamp(ss * 1.25, 0, 1), clamp(ll * 0.82, 0.04, 0.7))); d[i] = c[0]; d[i + 1] = c[1]; d[i + 2] = c[2]; d[i + 3] = 255; }
  cxF.putImageData(id, 0, 0);
  return cvF.toDataURL('image/png');
}
function nameField(p) { return `linear-gradient(120deg,${p.a1},${mix(p.a2, '#05060A', 0.35)} 55%,${mix(p.a3, '#05060A', 0.45)})`; }
/* Palette fürs Anwenden: „grau“ → 50 % Schemafarben */
function palUse(p) {
  if (!p || p.dull !== 'grau') return p;
  return Object.assign({}, p, { a1: mixOk(p.a1, mix(S.c[1], '#05060A', 0.8), 0.5), a2: mixOk(p.a2, S.c[1], 0.5), a3: mixOk(p.a3, c3Of(), 0.5) });
}

/* ---------- Datum: eine Funktion für alle Stellen ---------- */
const MON_DE = ['Jan.', 'Feb.', 'März', 'Apr.', 'Mai', 'Juni', 'Juli', 'Aug.', 'Sep.', 'Okt.', 'Nov.', 'Dez.'];
function when(iso) {
  if (!iso) return 'nie gespielt';
  const d = new Date(iso), now = new Date(), t0 = new Date(now.getFullYear(), now.getMonth(), now.getDate()), dd = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const days = Math.round((t0 - dd) / 864e5), hm = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
  if (days <= 0) return 'heute, ' + hm;
  if (days === 1) return 'gestern, ' + hm;
  if (days < 7) return 'vor ' + days + ' Tagen';
  if (days < 35) { const w = Math.round(days / 7); return 'vor ' + w + (w === 1 ? ' Woche' : ' Wochen'); }
  return d.getDate() + '. ' + MON_DE[d.getMonth()] + ' ' + d.getFullYear();
}

/* ---------- Ableitungen ---------- */
const exeName = p => p.split('\\').pop();
const HELPER = /(start_protected_game|launcher|prelauncher|anticheat|helper|crash|unins|setup|redist|vcredist|dxsetup|battleye|easyanti|gamelaunchhelper|^play[a-z0-9]+\.exe$)/i;
function mainExe(g) {   // Haupt-EXE: Hilfs- und Startprogramme nach hinten
  if (!g.exe.length) return null;
  const s = g.exe.slice().sort((a, b) => (HELPER.test(exeName(a)) ? 1 : 0) - (HELPER.test(exeName(b)) ? 1 : 0) || (/shipping|win64|x64|bin/i.test(b) ? 1 : 0) - (/shipping|win64|x64|bin/i.test(a) ? 1 : 0));
  return s[0];
}
/* Logo: transparenten Rand einmal per Canvas ermitteln und mit object-view-box abschneiden (WPF: CroppedBitmap) */
const TRIM = new Map();
function trimLogo(img) {
  const apply = v => { if (v) img.style.objectViewBox = v; img.classList.add('trim'); };
  if (TRIM.has(img.src)) return apply(TRIM.get(img.src));
  try {
    const W = 160, H = Math.max(8, Math.round(160 * img.naturalHeight / Math.max(1, img.naturalWidth)));
    cv.width = W; cv.height = H; cx2.clearRect(0, 0, W, H); cx2.drawImage(img, 0, 0, W, H);
    const d = cx2.getImageData(0, 0, W, H).data; let x0 = W, y0 = H, x1 = -1, y1 = -1;
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (d[(y * W + x) * 4 + 3] > 20) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
    const v = x1 < 0 ? '' : `inset(${(y0 / H * 100).toFixed(1)}% ${((W - 1 - x1) / W * 100).toFixed(1)}% ${((H - 1 - y1) / H * 100).toFixed(1)}% ${(x0 / W * 100).toFixed(1)}%)`;
    TRIM.set(img.src, v); apply(v);
  } catch (e) { apply(''); }
}
document.addEventListener('load', e => { if (e.target.classList && e.target.classList.contains('logo')) trimLogo(e.target); }, true);
/* Pfad mit Auslassung in der MITTE (keine bidi-Tricks): „…\Binaries\Win64“ bleibt lesbar. WPF: IValueConverter + FormattedText */
const mctx = document.createElement('canvas').getContext('2d');
function midPath(p, px, font) {
  mctx.font = font; if (mctx.measureText(p).width <= px) return p;
  const parts = p.split('\\'), head = parts.slice(0, 1), tail = parts.slice(1);
  while (tail.length > 1 && mctx.measureText(head.concat(['…'], tail).join('\\')).width > px) tail.shift();
  let s = head.concat(['…'], tail).join('\\');
  if (mctx.measureText(s).width > px) { let t = tail.join('\\'); while (t.length > 4 && mctx.measureText('…' + t).width > px) t = t.slice(1); s = '…' + t; }
  return s;
}
const exeDir = (g, p) => { const dir = p.slice(0, p.lastIndexOf('\\')); return g.dir && dir.toLowerCase().startsWith(g.dir.toLowerCase()) ? '…\\' + (g.dir.split('\\').pop() + dir.slice(g.dir.length)) : dir; };
const hdrCap = g => g.hdr === 'Native' || g.hdr === 'Limited' || g.hdr === 'ModOnly';
const kindOf = g => (HDRL[g.hdr] || HDRL.Unknown)[2];
const kindTag = g => `<span class="kd ${(HDRL[g.hdr] || HDRL.Unknown)[1]}" title="HDR-Unterstützung laut HDR-Liste">${esc(kindOf(g))}</span>`;
function overline(g) {
  if (g.nw) return 'Neu in der Bibliothek · ' + when(g.fs);
  if (!g.lp) return 'Noch nicht gespielt';
  return 'Zuletzt gespielt · ' + when(g.lp);
}
const chipTxt = g => S.pause ? 'Automatik pausiert' : g.auto ? 'HDR ein · danach ' + (g.rest ? 'zurück' : 'bleibt ein') : 'HDR bleibt unverändert';
/* Abzeichen: AUTO HDR (= Windows Auto HDR), NEU; in Listen zusätzlich HDR-Art */
const badges = (g, hdrToo) => (g.ahdr ? '<span class="bd g" title="Windows Auto HDR beim Start über HDRpilot">AUTO HDR</span>' : '') + (g.nw ? '<span class="bd n">NEU</span>' : '') + (hdrToo && g.hdr === 'Native' ? '<span class="bd h">HDR</span>' : '');

/* ---------- Filtern, Sortieren ---------- */
const HORD = { Native: 0, Limited: 1, ModOnly: 2, Unknown: 3 };
function computeView() {
  const q = S.q.trim().toLowerCase();
  const v = [];
  for (let i = 0; i < G.length; i++) {
    const g = G[i];
    if (g.hidden) continue;
    if (q && g.nl.indexOf(q) < 0) continue;
    if (S.src !== 'all' && g.src !== S.src) continue;
    if (S.fa && !g.auto) continue;
    if (S.fn && !g.nw) continue;
    if (S.fh && !hdrCap(g)) continue;
    v.push(i);
  }
  const k = S.sort.k, d = S.sort.d, nm = (a, b) => G[a].n.localeCompare(G[b].n, 'de', { sensitivity: 'base' });
  const cmp = {
    name: (a, b) => d * nm(a, b),
    src: (a, b) => d * srcName(G[a].src).localeCompare(srcName(G[b].src)) || nm(a, b),
    lp: (a, b) => { const x = G[a].lpt, y = G[b].lpt; if (x === y) return nm(a, b); if (!x) return 1; if (!y) return -1; return d * (x - y); },
    hdr: (a, b) => d * (HORD[G[a].hdr] - HORD[G[b].hdr]) || nm(a, b),
    auto: (a, b) => d * ((G[a].auto ? 1 : 0) - (G[b].auto ? 1 : 0)) || nm(a, b)
  }[k] || ((a, b) => nm(a, b));
  v.sort(cmp);
  S.vis = v;
  return v;
}
const SORTS = [['lp', 'Zuletzt gespielt', -1], ['name', 'Name (A–Z)', 1], ['src', 'Quelle', 1], ['hdr', 'HDR-Unterstützung', 1], ['auto', 'HDR automatisch zuerst', -1]];

/* ========================================================================================================== */
/*  Darstellung anwenden                                                                                      */
/* ========================================================================================================== */
function themeDark() { return S.theme === 'dunkel' || (S.theme === 'auto' && (!window.matchMedia || matchMedia('(prefers-color-scheme: dark)').matches)); }
function applyLook() {
  const dark = themeDark(), c1 = S.c[0], c2 = S.c[1], c3 = c3Of();
  const dk = Math.min(ratio(c1, '#0B0C12'), ratio(c2, '#0B0C12')), wh = Math.min(ratio(c1, '#FFFFFF'), ratio(c2, '#FFFFFF'));
  const on = S.autoTxt ? (dk >= wh ? '#0B0C12' : '#FFFFFF') : '#FFFFFF';
  app.classList.toggle('dark', dark); app.classList.toggle('light', !dark);
  const st = app.style; st.setProperty('--c1', c1); st.setProperty('--c2', c2); st.setProperty('--c3', c3); st.setProperty('--on', on);
  if (S.autoTxt) st.setProperty('--at', readable(c1, dark ? '#14151D' : '#FFFFFF', dark)); else st.removeProperty('--at');
  // Symbolfarben je Taskleiste: Verlauf c1→c2, auf mind. 3:1 gegen den Taskleisten-Hintergrund gebracht
  st.setProperty('--icD1', readable(c1, '#1C1D24', true, 3)); st.setProperty('--icD2', readable(c2, '#1C1D24', true, 3));
  st.setProperty('--icL1', readable(c1, '#F3F4F8', false, 3)); st.setProperty('--icL2', readable(c2, '#F3F4F8', false, 3));
  const fx = fxEff();
  app.classList.remove('fx-0', 'fx-1', 'fx-2'); app.classList.add('fx-' + fx);
  app.classList.toggle('nofollow', !S.follow);
  applyAmbient();
  updateIcons();
}
function applyAmbient() {
  const g = G[S.sel]; if (!g) return;
  const p = palUse(palOf(g)), st = app.style;
  if (S.follow && p) { st.setProperty('--a1', p.a1); st.setProperty('--a2', p.a2); st.setProperty('--a3', p.a3); }
  else { const c2 = S.c[1]; st.setProperty('--a1', mix(c2, '#05060A', 0.82)); st.setProperty('--a2', c2); st.setProperty('--a3', c3Of()); }
  placeLight(p || namePal(g));
}
/* Spitzlicht auf den Bildbereich klemmen: unter der Kopfzeile, links vom Detailbereich (keine Lichtkante über Titel/Knöpfen) */
function placeLight(p) {
  const hero = $('#hero'); if (!hero) return;
  const W = hero.clientWidth || 1600, Hh = hero.clientHeight || 300, pane = $('#pane'), acard = $('#acard');
  const blocker = S.view === 'regal' ? acard : pane, bl = blocker && blocker.offsetWidth ? blocker.getBoundingClientRect().left - hero.getBoundingClientRect().left : W * 0.75;
  const lyMin = Math.max(112, Hh * 0.3), lyMax = Math.max(lyMin, Math.min(Hh - 26, Hh * 0.78));
  const lx = clamp(+p.lx || 0.62, 0.3, Math.min(0.7, (bl - 60) / W)), ly = clamp((+p.ly || 0.45) * Hh, lyMin, lyMax);
  hero.style.setProperty('--lx', (lx * 100).toFixed(1) + '%'); hero.style.setProperty('--ly', Math.round(ly) + 'px');
  const hl = p.hl == null ? 0.55 : +p.hl;
  hero.style.setProperty('--blA', clamp(hl * 0.85 - 0.05, 0.14, 0.7).toFixed(2));   // Kern
  hero.style.setProperty('--blC', clamp(0.18 + hl * 0.32, 0.18, 0.46).toFixed(2));  // farbiger Hof
  return { lx, ly };
}
let ambSlot = 0;
function setAmbientImage(url) {
  const a = $('#ambA'), b = $('#ambB'), next = ambSlot ? a : b, cur = ambSlot ? b : a;
  next.style.backgroundImage = url ? 'url("' + url + '")' : 'none';
  next.classList.toggle('on', !!url); cur.classList.remove('on'); ambSlot = 1 - ambSlot;
}
function iconState() { return S.run >= 0 && S.hdr ? 'Run' : S.pause ? 'Pause' : S.hdr ? 'On' : 'Off'; }
function updateIcons() {
  const s = iconState(), u = $('#wico use'); if (u) u.setAttribute('href', '#i' + s + 'S');
  const c1 = S.c[0], c2 = S.c[1], c3 = c3Of(), on = app.style.getPropertyValue('--on').trim() || '#0B0C12';
  const d1 = readable(c1, '#1B1C24', true, 3), d2 = readable(c2, '#1B1C24', true, 3);
  const glyph = (stroke, w) => `<path d="M3.5 16.5h17M7 16.5a5 5 0 0 1 10 0M12 6v2.4M6.2 9l1.6 1.6M17.8 9l-1.6 1.6" fill="none" stroke="${stroke}" stroke-width="${w}" stroke-linecap="round"/>`;
  const svg = s === 'Off' || s === 'Pause'
    ? `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><defs><linearGradient id="o" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${d1}"/><stop offset="1" stop-color="${d2}"/></linearGradient></defs><rect width="24" height="24" rx="6" fill="#1B1C24"/>${glyph(s === 'Pause' ? '#9AA0B4' : 'url(#o)', 2.4)}${s === 'Pause' ? '<rect x="15" y="15" width="8" height="8" rx="2.4" fill="#FFB020"/>' : ''}</svg>`
    : `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${c1}"/><stop offset=".55" stop-color="${c2}"/><stop offset="1" stop-color="${c3}"/></linearGradient></defs><rect width="24" height="24" rx="6" fill="url(#g)"/><path d="M7 16.5a5 5 0 0 1 10 0z" fill="${on}"/>${glyph(on, 2.2)}</svg>`;
  const fav = $('#favicon'); if (fav) fav.href = 'data:image/svg+xml,' + encodeURIComponent(svg);
}

/* ========================================================================================================== */
/*  Größen: Hero-Höhe je Ansicht, Fensterbreite                                                                 */
/* ========================================================================================================== */
function heroSize() {
  const H = app.clientHeight || innerHeight, W = app.clientWidth || innerWidth;
  const h = S.view === 'regal' ? clamp(H * 0.62, 500, 660) : S.view === 'raster' ? clamp(H * 0.24, 196, 260) : S.lcol ? 120 : clamp(H * 0.22, 180, 240);
  app.style.setProperty('--heroH', Math.round(h) + 'px');
  app.style.setProperty('--tbY', Math.round(S.view === 'regal' ? h - 50 : h - 2) + 'px');
  app.classList.toggle('w1366', W < 1366); app.classList.toggle('w1500', W < 1500);
  app.classList.toggle('lcol', S.view === 'liste' && S.lcol);
}

/* ========================================================================================================== */
/*  Kopfzeile                                                                                                  */
/* ========================================================================================================== */
function renderHead() {
  const auto = G.filter(g => g.auto).length, nw = G.filter(g => g.nw).length;
  $('#hds').innerHTML = `<span><i></i>${G.length} Spiele</span><span><i class="a"></i>${auto} mit HDR automatisch</span><span><i class="b"></i>${nw} neu</span>`;
  const burst = Array.from({ length: 12 }, (_, i) => `<i style="--r:${i * 30}deg;--l:${i % 2 ? 84 + i % 3 * 4 : 64 + i % 4 * 3}px"></i>`).join('');
  // echte DXGI-Werte, sofort (kein Hochzählen); Skala 0–1000 nits mit Vollbild 254 und Spitze 456
  $('#mon').innerHTML = `<span class="hcd"></span>
    <div class="hct"><span class="cap2">${esc(MON.name)} · ${MON.bits} Bit</span><b id="hdrState">${S.hdr ? 'HDR EIN' : 'HDR AUS'}</b></div>
    <div class="rd" title="Werte des Bildschirms laut DXGI: MaxLuminance ${MON.peak} · MaxFullFrameLuminance ${MON.full} · MinLuminance ${MON.min} · ${MON.bits} Bit pro Farbe">
      <div><small>Spitze</small><b>${MON.peak}<em>nits</em></b></div>
      <div><small>Vollbild</small><b>${MON.full}<em>nits</em></b></div>
      <div class="rmin"><small>Minimum</small><b>${MON.min}<em>nits</em></b></div></div>
    <div class="nsc" title="Skala 0–1000 nits · Vollbild ${MON.full} · Spitze ${MON.peak} (Fähigkeit des Bildschirms, keine Messung)" aria-hidden="true"><div class="tr"><i class="f" style="width:${MON.peak / 10}%"></i><i class="v" style="left:${MON.full / 10}%"></i><i class="p" style="left:${MON.peak / 10}%"></i></div><div class="nsl"><span>0</span><span>1000 nits</span></div></div>
    <button class="hb" data-act="hdr" aria-pressed="${tf(S.hdr)}"><span class="bu" id="bu">${burst}</span>${ic(I.sun)}<span id="hdrBtn">${S.hdr ? 'HDR ausschalten' : 'HDR einschalten'}</span></button>`;
  renderPauseBtn();
  $('.hbar .gear').innerHTML = GEAR;
}
function renderPauseBtn() {
  const b = $('#pauseBtn'); b.classList.toggle('on', S.pause); b.setAttribute('aria-pressed', tf(S.pause));
  b.innerHTML = ic(S.pause ? I.play : I.pause) + `<span class="lbl">${S.pause ? 'Automatik fortsetzen' : 'Automatik pausieren'}</span>`;
  b.title = S.pause ? 'Automatik fortsetzen' : 'Automatik pausieren';
  app.classList.toggle('paused', S.pause);
}

/* ========================================================================================================== */
/*  Werkzeugleiste – passt sich an: erst Beschriftungen kürzen, dann Filter in ein Menü falten                   */
/* ========================================================================================================== */
const CHIPS = [['fa', 'Automatik', I.sun, 'Nur Spiele mit HDR automatisch'], ['fn', 'Neu', I.star, 'Nur neue Spiele'], ['fh', 'HDR-fähig', I.mon, 'Nur Spiele mit HDR-Unterstützung (nativ, teilweise, Mod)']];
function renderToolbar() {
  const all = G.filter(g => !g.hidden), cnt = s => all.filter(g => g.src === s).length;
  const srcs = [['all', 'Alle Quellen', all.length, 'var(--alls)']].concat(Object.keys(SRC).filter(s => cnt(s)).map(s => [s, srcName(s), cnt(s), srcCol(s)]));
  const cur = srcs.find(s => s[0] === S.src) || srcs[0], so = SORTS.find(s => s[0] === S.sort.k) || SORTS[0];
  const n = { fa: all.filter(g => g.auto).length, fn: all.filter(g => g.nw).length, fh: all.filter(hdrCap).length };
  const act = CHIPS.filter(c => S[c[0]]).length;
  const views = [['liste', 'Liste', I.list], ['regal', 'Regal', I.shelf], ['raster', 'Raster', I.grid]];
  const tb = $('#toolbar');
  tb.innerHTML = `
    <label class="srch">${ic(I.search)}<input id="q" type="text" placeholder="Suchen …" aria-label="Spiele durchsuchen" value="${esc(S.q)}" autocomplete="off"><kbd>Strg+F</kbd></label>
    <div class="ddw"><button class="dd" data-act="menu" data-v="src" aria-haspopup="listbox" aria-expanded="${tf(S.menu === 'src')}"><i class="sdot" style="--sr:${cur[3]}"></i><span class="dlb">${esc(cur[1])}</span><span class="ct">${cur[2]}</span>${ic(I.down)}</button>
      ${S.menu === 'src' ? `<div class="menu" role="listbox" aria-label="Quelle">${srcs.map(s => `<button class="mi" role="option" aria-selected="${tf(s[0] === S.src)}" data-act="src" data-v="${s[0]}"><i class="sdot" style="--sr:${s[3]}"></i>${esc(s[1])}<span>${s[2]}</span></button>`).join('')}</div>` : ''}</div>
    <div class="ddw sort"><button class="dd" data-act="menu" data-v="sort" aria-haspopup="listbox" aria-expanded="${tf(S.menu === 'sort')}" title="Sortierung: ${so[1]}">${ic(I.clock)}<span class="dlb">${so[1]}</span>${ic(I.down)}</button>
      ${S.menu === 'sort' ? `<div class="menu" role="listbox" aria-label="Sortierung">${SORTS.map(s => `<button class="mi" role="option" aria-selected="${tf(s[0] === S.sort.k)}" data-act="sort" data-v="${s[0]}">${s[1]}</button>`).join('')}</div>` : ''}</div>
    ${CHIPS.map(c => `<button class="ch" data-act="chip" data-v="${c[0]}" aria-pressed="${tf(S[c[0]])}" title="${c[3]}">${ic(c[2])}<span class="lbl">${c[1]}</span><span class="ct">${n[c[0]]}</span></button>`).join('')}
    <div class="ddw flt"><button class="dd${act ? ' on' : ''}" data-act="menu" data-v="flt" aria-haspopup="true" aria-expanded="${tf(S.menu === 'flt')}">${ic(I.filter)}Filter${act ? `<span class="ct">${act}</span>` : ''}${ic(I.down)}</button>
      ${S.menu === 'flt' ? `<div class="menu" role="menu" aria-label="Filter">${CHIPS.map(c => `<button class="mi" role="menuitemcheckbox" aria-checked="${tf(S[c[0]])}" data-act="chip" data-v="${c[0]}">${ic(c[2])}${c[1]}<span>${n[c[0]]}</span></button>`).join('')}</div>` : ''}</div>
    <span class="sp"></span>
    <div class="seg v" role="radiogroup" aria-label="Ansicht">${views.map(v => `<button role="radio" aria-checked="${tf(S.view === v[0])}" data-act="view" data-v="${v[0]}" title="${v[1]} (Strg+${views.indexOf(v) + 1})">${ic(v[2])}<span class="lbl">${v[1]}</span></button>`).join('')}</div>
    <button class="ibt" data-act="rescan" aria-label="Neu einlesen" title="Neu einlesen">${ic(I.refresh)}</button>
    <button class="add" data-act="add" title="Spiel hinzufügen">${ic(I.plus)}<span class="lbl">Spiel hinzufügen</span></button>`;
  fitToolbar();
}
/* Stufen: 1 „Spiel hinzufügen“ nur Symbol · 2 Ansichts-Beschriftungen weg · 3 Suche schmal · 4 Anzahl an der Quelle weg ·
   5 Filter-Chips → „Filter (n)“ (nie Chips nur mit Zahl) · 6 Sortierung nur als Symbol (Bezeichnung im Tooltip) */
function fitToolbar() {
  const tb = $('#toolbar'); if (!tb || S.page !== 'bibliothek') return;
  const L = ['f1', 'f2', 'f3', 'f4', 'f5', 'f6'];
  tb.classList.remove(...L);
  const fits = () => { const r = tb.getBoundingClientRect().right, last = tb.lastElementChild; return tb.scrollWidth <= tb.clientWidth && (!last || last.getBoundingClientRect().right <= r + 0.5); };
  for (let i = 0; i < L.length && !fits(); i++) tb.classList.add(L[i]);
}

/* ========================================================================================================== */
/*  Hero                                                                                                       */
/* ========================================================================================================== */
const slots = [];
let slotCur = 0, heroGi = -1, hcK = 1;
/* Erzeugtes Titelbild: Farbfeld (6×6 gestreckt) + Abdunklung + Name als Umriss-Muster; bei deckendem Kachelbild (Xbox 480 px)
   zusätzlich das Bild rechts mit weicher Kante. Das Symbol steht im Titelbereich links vom Namen (renderHeroContent). */
function genBanner(g, a, p) {
  const pal = p || namePal(g), bg = pal.field ? `url(${pal.field})` : nameField(pal);
  return `<div class="gfield" style="background-image:${bg}"></div><div class="gshade"></div>
    <div class="gpat" aria-hidden="true"><span>${esc(g.n)}</span><span>${esc(g.n)}</span></div>
    ${a.icon && a.iconOpaque ? `<img class="gart" src="${esc(a.icon)}" alt="">` : ''}`;
}
function fillSlot(el, g) {
  const a = art(g);
  el.className = 'slot' + (a.hero ? '' : ' gen');
  if (a.hero) el.innerHTML = `<img class="art" src="${esc(a.hero)}" alt=""><img class="art boost" src="${esc(a.hero)}" alt=""><div class="veil"></div>`;
  else {
    el.innerHTML = genBanner(g, a, palOf(g));
    palette(g).then(p => { if (el.isConnected && heroGi === g.i) { const f = $('.gfield', el); if (f && p.field) f.style.backgroundImage = `url(${p.field})`; } });
  }
  const im = el.querySelector('img.art,img.gart');
  if (!im) return Promise.resolve();
  return Promise.race([im.decode ? im.decode().catch(() => { }) : Promise.resolve(), new Promise(r => setTimeout(r, 450))]);
}
function showHero(gi, instant) {
  const g = G[gi]; if (!g) return;
  if (!slots.length) { const box = $('#slots'); for (let i = 0; i < 2; i++) { const d = document.createElement('div'); d.className = 'slot'; box.appendChild(d); slots.push(d); } }
  const same = heroGi === gi; heroGi = gi;
  const next = same ? slots[slotCur] : slots[1 - slotCur];
  fillSlot(next, g).then(() => {
    if (heroGi !== gi) return;
    if (!same) {
      const cur = slots[slotCur];
      if (instant || fxEff() === 2) { cur.classList.remove('on', 'out'); next.classList.remove('out'); next.classList.add('on'); }
      else { next.classList.remove('out'); cur.classList.remove('on'); cur.classList.add('out'); void next.offsetWidth; next.classList.add('on'); }
      slotCur = slots.indexOf(next);
    } else next.classList.add('on');
  });
  const a = art(g), p0 = palOf(g);
  setAmbientImage(S.follow ? (a.blur || (p0 && p0.field) || null) : null);
  palette(g).then(p => { if (S.sel === gi) { applyAmbient(); markSelRow(); if (S.follow && !a.blur && p.field) setAmbientImage(p.field); } });
  renderHeroContent(g);
}
/* Symbol-Kachel links vom Namen: größtes verfügbares Bild, kleine Symbole höchstens 1,5-fach (nie ein 32-px-Bild aufblasen) */
function icoTile(g, a) {
  if (!a.icon) { const p = palOf(g) || namePal(g); return `<span class="gico mono" style="--g1:${p.a1};--g2:${p.a2}"><b>${esc(mono(g.n))}</b></span>`; }
  if (a.iconOpaque) return `<span class="gico art"><img src="${esc(a.icon)}" alt=""></span>`;
  return `<span class="gico" style="--ip:${a.iconPx || 64}"><img src="${esc(a.icon)}" alt=""></span>`;
}
function titleHTML(g, a) {
  if (a.logo) return `<img class="logo" src="${esc(a.logo)}" alt="${esc(g.n)}">`;
  const len = g.n.length, t = `<h1 class="tt ${len > 34 ? 'xl' : len > 20 ? 'l' : ''}" data-t="${esc(g.n)}">${esc(g.n)}</h1>`;
  return a.hero ? t : `<div class="gtl">${icoTile(g, a)}${t}</div>`;
}
function renderHeroContent(g) {
  const a = art(g), run = S.run === g.i;
  hcK = hcK === 1 ? 2 : 1;
  const hc = $('#hc');
  hc.className = 'hc k' + hcK + (a.hero ? '' : ' isgen');
  hc.innerHTML = `<div class="hc-l">
      <div class="ov an">${esc(overline(g))}</div>
      <div class="lgw an" style="--d:.06s">${titleHTML(g, a)}</div>
      <div class="meta an" style="--d:.14s"><span class="srcp" style="--sr:${srcCol(g.src)}"><i></i>${esc(srcName(g.src))}</span>${kindTag(g)}<span class="bds">${badges(g, false)}</span></div>
    </div>
    <div class="hc-r an" style="--d:.22s">
      <span class="pw"><button class="play${run ? ' run' : ''}" data-act="start" title="${run ? 'Läuft – Klick simuliert das Spielende' : esc(g.n) + ' starten'}">${ic(run ? I.stop : I.play)}<span>${run ? 'Läuft' : 'Starten'}</span></button></span>
      <button class="gb" data-act="shortcut" data-v="desktop">${ic(I.desk)}Desktop</button>
      <button class="gb" data-act="shortcut" data-v="start">${ic(I.startm)}Startmenü</button>
      <div class="chip">${ic(I.sun)}<div><span class="cap2">Beim Start</span><b>${esc(chipTxt(g))}</b></div></div>
    </div>`;
}

/* Parallaxe: Maus über dem Hero → Ebenen gegeneinander verschieben */
let pxT = null;
function heroMove(e) {
  if (fxEff() || S.zz || S.run >= 0) return;
  const h = $('#hero'), r = h.getBoundingClientRect();
  const px = clamp(((e.clientX - r.left) / r.width - 0.5) * 2, -1, 1), py = clamp(((e.clientY - r.top) / r.height - 0.5) * 2, -1, 1);
  if (pxT) return;
  pxT = requestAnimationFrame(() => { pxT = null; h.style.setProperty('--px', px.toFixed(3)); h.style.setProperty('--py', py.toFixed(3)); });
}
function heroLeave() { const h = $('#hero'); h.style.setProperty('--px', 0); h.style.setProperty('--py', 0); }

/* Schwebende Lichtpunkte (CSS), Farben aus dem Spiel */
function renderBokeh() {
  $('#bk').innerHTML = Array.from({ length: 16 }, (_, i) => `<i style="--l:${18 + (i * 37) % 60}%;--t:${30 + (i * 23) % 55}%;--z:${[4, 10, 22][i % 3]}px;--o:${[0.95, 0.6, 0.35][i % 3]};--d:${11 + i % 5 * 2}s;--w:-${(i * 1.7).toFixed(1)}s;--x:${(i % 2 ? -1 : 1) * (20 + i * 7 % 40)}px"></i>`).join('');
}

/* ---------- HDR-Moment: Partikel in den Bildfarben, nur 2,5 s (danach kein Frame mehr) ---------- */
const PFX_TOP = 96;   // Partikel erst unterhalb der Kopfzeile (nie über Titelleiste/Fensterknöpfen)
const pfx = { ps: [], raf: 0, last: 0, sweepT: 0, until: 0 };
function burst() {
  if (fxEff() !== 0) return;
  const c = $('#pfx'), r = c.getBoundingClientRect(), dpr = Math.min(1.5, window.devicePixelRatio || 1);
  c.width = Math.round(r.width * dpr); c.height = Math.round(r.height * dpr);
  const g = G[S.sel], p = palUse(palOf(g) || namePal(g)), c3 = c3Of(), pos = placeLight(p);
  pfx.cols = (S.follow ? [p.a3, p.a2, '#FFFFFF', mix(p.a3, '#FFFFFF', 0.5), c3] : [c3, S.c[0], '#FFFFFF', mix(c3, '#FFFFFF', 0.5), S.c[1]]).map(h => h.toUpperCase());
  const hero = $('#hero'); pfx.hx = pos.lx * hero.clientWidth; pfx.hy = pos.ly - PFX_TOP;
  pfx.sweepT = performance.now(); pfx.until = pfx.sweepT + 2500;
  for (let i = 0; i < 46; i++) spawn(pfx.hx + (Math.random() - 0.5) * 120, pfx.hy + (Math.random() - 0.5) * 70, true);
  if (!pfx.raf) { pfx.last = performance.now(); pfx.raf = requestAnimationFrame(tick); }
}
function spawn(x, y, big) {
  const a = Math.random() * Math.PI * 2, sp = big ? 40 + Math.random() * 160 : 10 + Math.random() * 40;
  pfx.ps.push({ x, y, vx: Math.cos(a) * sp * 0.8 + 18, vy: Math.sin(a) * sp * 0.6 - 40 - Math.random() * 50, r: big ? 1.2 + Math.random() * 3.2 : 0.8 + Math.random() * 2, life: 0, max: 1.1 + Math.random() * 1.6, c: pfx.cols[(Math.random() * pfx.cols.length) | 0] });
}
function tick(t) {
  const c = $('#pfx'), x = c.getContext('2d'), dpr = c.width / Math.max(1, c.clientWidth), dt = Math.min(0.05, (t - pfx.last) / 1000); pfx.last = t;
  const W = c.clientWidth, H = c.clientHeight, since = t - pfx.sweepT;
  if (S.zz) { pfx.raf = 0; return; }   // pausiert: Zustand bleibt stehen, setZz setzt fort
  if (t < pfx.until && fxEff() === 0) {
    if (since < 1500) { const k = since / 1500, sx = (-0.2 + k * 1.5) * W; for (let i = 0; i < 4; i++) spawn(sx + (Math.random() - 0.5) * 140, Math.random() * H, Math.random() < 0.4); }
    else if (Math.random() < 0.35) spawn(pfx.hx + (Math.random() - 0.5) * W * 0.3, pfx.hy + (Math.random() - 0.3) * H * 0.5, false);
  }
  x.setTransform(dpr, 0, 0, dpr, 0, 0); x.clearRect(0, 0, W, H); x.globalCompositeOperation = 'lighter';
  pfx.ps = pfx.ps.filter(p => (p.life += dt) < p.max);
  for (const p of pfx.ps) {
    p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 12 * dt; p.vx *= 0.99;
    const k = p.life / p.max, al = k < 0.15 ? k / 0.15 : 1 - (k - 0.15) / 0.85, rr = p.r * (1 + k * 0.4);
    const gr = x.createRadialGradient(p.x, p.y, 0, p.x, p.y, rr * 3);
    gr.addColorStop(0, '#FFFFFF'); gr.addColorStop(0.18, p.c); gr.addColorStop(0.45, p.c + '66'); gr.addColorStop(1, p.c + '00');
    x.globalAlpha = al; x.fillStyle = gr; x.beginPath(); x.arc(p.x, p.y, rr * 3, 0, 6.283); x.fill();
  }
  x.globalAlpha = 1;
  if ((pfx.ps.length || t < pfx.until) && S.page === 'bibliothek') pfx.raf = requestAnimationFrame(tick); else { pfx.raf = 0; pfx.ps = []; x.clearRect(0, 0, W, H); }
}
function kick(id, base) { const el = $(id); if (!el) return; el.className = base; void el.offsetWidth; el.className = base + ' k' + (S.hk % 2 ? 1 : 2); }
function setHdr(on, why) {
  if (S.hdr === on) return;
  S.hdr = on; S.hk++;
  app.classList.toggle('hdr', on);
  if (on) { placeLight(palUse(palOf(G[S.sel]) || namePal(G[S.sel]))); kick('#sweep', 'sweep'); kick('#flash', 'flash'); kick('#edge', 'edge'); kick('#bu', 'bu'); burst(); }
  else kick('#dimfx', 'dimfx');
  $('#hdrState') && ($('#hdrState').textContent = on ? 'HDR EIN' : 'HDR AUS');
  $('#hdrBtn') && ($('#hdrBtn').textContent = on ? 'HDR ausschalten' : 'HDR einschalten');
  const hb = $('#mon .hb'); if (hb) hb.setAttribute('aria-pressed', tf(on));
  if (why !== 'silent') toast(on ? (why || 'HDR ist eingeschaltet') : (why || 'HDR ist ausgeschaltet'), on ? MON.name + ' · Spitze ' + MON.peak + ' nits · Vollbild ' + MON.full + ' nits' : MON.name + ' · zurück auf SDR', I.sun);
  updateIcons(); renderStatus(); if (S.page === 'darstellung') renderLook();
}

/* ========================================================================================================== */
/*  Detailbereich (Liste/Raster) · im Regal als Schublade; dort steht stattdessen die kompakte Automatik-Karte   */
/* ========================================================================================================== */
let pbK = 1;
function coverMini(g, a) { return a.cover ? `<img src="${esc(a.cover)}" alt="">` : coverGen(g, a, true); }
/* Erzeugtes Cover: Hochformat-Kachel. Mit Titelbild: Ausschnitt um das Spitzlicht + Logo/Name; ohne: Farbfeld + Symbol + Name. */
function coverGen(g, a, small) {
  const p = palOf(g) || namePal(g);
  if (a.hero) {
    const lx = clamp(+(p.lx || 0.5), 0.2, 0.8);
    return `<div class="ca gc"><img class="hcrop" src="${esc(a.hero)}" alt="" loading="lazy" style="object-position:${(lx * 100).toFixed(0)}% 30%"><div class="gsh"></div>${a.logo ? `<img class="gl" src="${esc(a.logo)}" alt="" loading="lazy">` : small ? '' : `<div class="gn">${esc(g.n)}</div>`}</div>`;
  }
  const bg = p.field ? `url(${p.field})` : nameField(p);
  const top = a.icon && a.iconOpaque ? `<img class="gka" src="${esc(a.icon)}" alt="" loading="lazy">`
    : `<div class="gic">${a.icon ? `<img src="${esc(a.icon)}" alt="" loading="lazy" style="--ip:${a.iconPx || 64}">` : `<span class="mono-l">${esc(mono(g.n))}</span>`}</div>`;
  return `<div class="ca gc${a.iconOpaque ? ' ka' : ''}" data-gi="${g.i}"><div class="gbg" style="background-image:${bg}"></div><div class="gpt"></div>${top}${small ? '' : `<div class="gn">${esc(g.n)}</div>`}</div>`;
}
function artLine(g, a) {
  const im = g.img || {};
  if (a.heroSrc === 'Eigene Datei') return 'Eigene Datei · nur auf diesem PC';
  if (a.hero && CACHE_N[im.heroFrom]) return 'Lokal aus dem ' + CACHE_N[im.heroFrom] + ' · nichts heruntergeladen';
  if (g.src === 'Xbox') return 'Xbox-Paket: Kachelbild ' + (im.iconPx || 480) + ' px · Titelbild und Cover daraus erzeugt';
  if (im.iconKind === 'exe') return srcName(g.src) + ': kein Bildcache · Symbol aus ' + (im.iconExe || 'der EXE') + ' (' + im.iconPx + ' px), Titelbild und Cover erzeugt';
  if (g.src === 'Steam') return 'Steam hat für dieses Spiel kein Titelbild – erzeugt aus dem Symbol';
  return srcName(g.src) + ': kein lokales Titelbild – erzeugt aus ' + (a.icon ? 'dem Symbol' : 'Monogramm und Name');
}
function renderPane() {
  const g = G[S.sel]; if (!g) { $('#psc').innerHTML = ''; return; }
  const a = art(g), run = S.run === g.i, nat = g.hdr === 'Native', h = HDRL[g.hdr] || HDRL.Unknown;
  pbK = pbK === 1 ? 2 : 1;
  const exW = Math.max(120, ($('#pane').clientWidth || 368) - 36 - 22 - 26 - 92);
  const ex = g.exe.length ? g.exe.map(p => { const d = exeDir(g, p); return `<div class="exe">${ic(I.file)}<div><b title="${esc(p)}">${esc(exeName(p))}</b><small title="${esc(d)}">${esc(midPath(d, exW, '500 11px Manrope'))}</small></div><span class="ok">erkannt</span></div>`; }).join('')
    : '<div class="none">Noch keine EXE-Datei erkannt – sie wird beim ersten Start über HDRpilot gefunden.</div>';
  const pp = palOf(g) || namePal(g), gm = lbl => `style="--g1:${pp.a1};--g2:${pp.a2}">` + (lbl === 'logo' ? '<b>Aa</b>' : a.icon && lbl !== 'icon' ? `<img src="${esc(a.icon)}" alt="">` : `<b>${esc(mono(g.n))}</b>`);
  const ak = (lbl, url, src, ct) => `<button class="ak" data-act="dlg" data-v="${lbl[1] === 'icon' ? 'symbol' : 'bild'}" data-tab="${lbl[1]}" title="${esc(lbl[0] + ': ' + src)}">${url ? `<span class="pv${ct ? ' ct' : ''}"><img src="${esc(url)}" alt="" loading="lazy"></span>` : `<span class="pv gmini" ${gm(lbl[1])}</span>`}<em class="${/erzeugt|Monogramm|Schrift/.test(src) ? 'gen' : ''}"></em><span>${lbl[0]}</span></button>`;
  const gens = [!a.hero && 'Titelbild erzeugt', !a.logo && 'Name statt Logo', !a.cover && (a.smear ? 'Cover aus Titelbild (Steam-Kapsel verschmiert)' : 'Cover erzeugt'), !a.icon && 'Monogramm statt Symbol'].filter(Boolean);
  $('#psc').innerHTML = `<div class="pb k${pbK}">
    <div class="ph" style="--j:0"><div class="pcv">${coverMini(g, a)}</div><div class="phx"><span class="cap2">Spielprofil</span><b title="${esc(g.n)}">${esc(g.n)}</b><span>${esc(srcName(g.src))} · ${g.exe.length ? esc(exeName(mainExe(g))) : '<em class="noexe">keine EXE erkannt</em>'}</span></div>
      <button class="ib s" data-act="ctx-pane" aria-label="Weitere Optionen" title="Weitere Optionen">${ic(I.more, '')}</button><button class="ib s dwx" data-act="drawer" aria-label="Details schließen" title="Schließen (Esc)">${ic(I.x)}</button></div>
    <div class="sec" style="--j:1"><div class="lb"><span>Starten</span><i></i></div>
      <div class="stb"><button class="pbtn pri${run ? ' run' : ''}" data-act="start">${ic(run ? I.stop : I.play)}${run ? 'Läuft' : 'Starten'}</button><button class="pbtn" data-act="shortcut" data-v="desktop" title="Verknüpfung auf dem Desktop">${ic(I.desk)}Desktop</button><button class="pbtn" data-act="shortcut" data-v="start" title="Eintrag im Startmenü">${ic(I.startm)}Startmenü</button></div>
      <div class="cmd" title="${esc(g.cmd)}">${ic(I.link)}<span>${esc(g.cmd ? (/^[A-Za-z]:\\/.test(g.cmd) ? midPath(g.cmd, exW + 60, "500 11px JetBrains Mono") : g.cmd) : 'direkt über die EXE-Datei')}</span></div></div>
    <div class="sec" style="--j:2"><div class="lb"><span>Automatik</span><i></i></div>
      <div class="rw"><div><b>HDR automatisch</b><small>Schaltet HDR ein, sobald das Spiel startet</small></div><button class="sw" role="switch" aria-checked="${tf(g.auto)}" data-act="tog" data-k="auto" data-i="${g.i}" aria-label="HDR automatisch"></button></div>
      <div class="rw"><div><b>Nach dem Spiel zurückstellen</b><small>Stellt den vorherigen HDR-Zustand wieder her</small></div><button class="sw" role="switch" aria-checked="${tf(g.rest)}" data-act="tog" data-k="rest" data-i="${g.i}" aria-label="Nach dem Spiel zurückstellen"></button></div>
      <div class="rw"><div><b>Bildschirm</b><small>Hier wird HDR geschaltet</small></div><button class="selb" data-act="toast" data-v="Nur ein HDR-fähiger Bildschirm angeschlossen|${esc(MON.name)} · Spitze ${MON.peak} nits · ${MON.bits} Bit">${ic(I.desk)}${esc(MON.name)}${ic(I.down)}</button></div></div>
    <div class="sec" style="--j:3"><div class="lb"><span>Bild</span><i></i></div>
      <div class="rw${nat ? ' dim' : ''}"><div><b>Windows Auto HDR <span class="tg2 ${h[1]}">${esc(kindOf(g))}</span></b><small>Wirkt beim Start über HDRpilot</small>
        ${nat ? `<span class="hint">${ic(I.info)}Das Spiel gibt selbst HDR aus. Auto HDR wird dann meist nicht gebraucht, bleibt aber schaltbar.</span>` : ''}</div>
        <button class="sw" role="switch" aria-checked="${tf(g.ahdr)}" data-act="tog" data-k="ahdr" data-i="${g.i}" aria-label="Windows Auto HDR"></button></div></div>
    <div class="sec" style="--j:4"><div class="lb"><span>EXE-Dateien · ${g.exe.length}</span><i></i><button class="lk" data-act="toast" data-v="EXE-Datei hinzufügen|Öffnet in der App den Dateidialog">${ic(I.plus)}Hinzufügen</button></div>${ex}</div>
    <div class="sec" style="--j:5"><div class="lb"><span>Bilder</span><i></i><button class="lk" data-act="dlg" data-v="bild">${ic(I.img)}Bild wählen …</button></div>
      <div class="arts">${ak(['Titelbild', 'hero'], a.hero || null, a.heroSrc)}${ak(['Logo', 'logo'], a.logo, a.logoSrc, true)}${ak(['Cover', 'cover'], a.cover, a.coverSrc)}${ak(['Symbol', 'icon'], a.icon, a.iconSrc, true)}</div>
      <div class="src-l">${ic(I.disk)}<span>${esc(artLine(g, a))}</span></div>
      ${gens.length ? `<div class="afb">${ic(I.gen)}<span>${esc(gens.join(' · '))}</span><button data-act="dlg" data-v="bild">Ändern …</button></div>` : ''}</div>
  </div>`;
  renderACard();
}
function renderACard() {
  const g = G[S.sel], el = $('#acard'); if (!g || !el) return;
  const nat = g.hdr === 'Native', h = HDRL[g.hdr] || HDRL.Unknown;
  el.innerHTML = `<div class="ach"><span class="cap2">Automatik</span><b title="${esc(g.n)}">${esc(g.n)}</b></div>
    <div class="arw"><div><b>HDR automatisch</b><small>beim Start einschalten</small></div><button class="sw" role="switch" aria-checked="${tf(g.auto)}" data-act="tog" data-k="auto" data-i="${g.i}" aria-label="HDR automatisch"></button></div>
    <div class="arw"><div><b>Zurückstellen</b><small>nach dem Spiel</small></div><button class="sw" role="switch" aria-checked="${tf(g.rest)}" data-act="tog" data-k="rest" data-i="${g.i}" aria-label="Nach dem Spiel zurückstellen"></button></div>
    <div class="arw${nat ? ' dim' : ''}"><div><b>Windows Auto HDR</b><small><span class="tg2 ${h[1]}">${esc(kindOf(g))}</span></small></div><button class="sw" role="switch" aria-checked="${tf(g.ahdr)}" data-act="tog" data-k="ahdr" data-i="${g.i}" aria-label="Windows Auto HDR"></button></div>
    <button class="pbtn dtl" data-act="drawer" aria-expanded="${tf(S.dw)}">${ic(I.panel)}Details<span class="sp"></span><kbd>I</kbd></button>`;
}
function setDrawer(on) {
  S.dw = !!on && S.view === 'regal'; app.classList.toggle('dw', S.dw);
  const b = $('#acard .dtl'); if (b) b.setAttribute('aria-expanded', tf(S.dw));
  if (S.dw) { const x = $('#pane .dwx'); x && setTimeout(() => x.focus({ preventScroll: true }), 50); }
}

/* ========================================================================================================== */
/*  Liste (virtualisiert)                                                                                      */
/* ========================================================================================================== */
const RH = 46;
let lv = null, pool = new Map();
function thumbHTML(g) {
  const a = art(g), p = palOf(g) || namePal(g);
  if (S.thumb === 'symbol') return a.icon ? `<span class="th"><img src="${esc(a.icon)}" alt="" loading="lazy"></span>` : `<span class="th gen" style="--g1:${p.a1};--g2:${p.a2}"><span class="mono-l">${esc(mono(g.n))}</span></span>`;
  const u = a.header || a.hero;
  if (u) return `<span class="th"><img src="${esc(u)}" alt="" loading="lazy"></span>`;
  const bg = p.field ? `url(${p.field})` : nameField(p);
  return `<span class="th gen" style="background-image:${bg}">${a.icon ? `<img src="${esc(a.icon)}" alt="" loading="lazy"${a.iconOpaque ? ' class="op"' : ''}>` : `<span class="mono-l">${esc(mono(g.n))}</span>`}</span>`;
}
function rowHTML(g) {
  const h = HDRL[g.hdr] || HDRL.Unknown;
  return `${thumbHTML(g)}<span class="nm"><b title="${esc(g.n)}">${esc(g.n)}</b>${badges(g, false)}</span>
    <span class="sr" style="--sr:${srcCol(g.src)}"><i></i>${esc(srcName(g.src))}</span>
    <span class="lp${g.lp ? '' : ' never'}">${esc(when(g.lp))}</span>
    <span class="hd">${g.hdr === 'Unknown' ? '<span class="hp unk" title="HDR unbekannt">—</span>' : `<span class="hp ${h[1]}" title="${esc(h[2])}">${g.hdr === 'Native' ? '<i></i>' : ''}${h[0]}</span>`}</span>
    <span class="swc"><small class="${g.auto ? 'on' : ''}">${g.auto ? 'Ein' : 'Aus'}</small><button class="sw" role="switch" aria-checked="${tf(g.auto)}" data-act="tog" data-k="auto" data-i="${g.i}" aria-label="HDR automatisch für ${esc(g.n)}"></button></span>`;
}
function renderList(keep) {
  const v = S.vis, sk = S.sort.k, sd = S.sort.d, prev = keep && lv ? lv.scrollTop : 0;
  const hd = (k, l, cls = '') => `<button class="${cls}" data-act="col" data-v="${k}" aria-sort="${sk === k ? (sd > 0 ? 'ascending' : 'descending') : 'none'}">${l}${ic(I.down)}</button>`;
  const root = $('#lst');
  root.classList.toggle('thumbs-symbol', S.thumb === 'symbol');
  root.innerHTML = `<div class="lh" role="row"><div class="seg sm thm" role="radiogroup" aria-label="Vorschau"><button role="radio" aria-checked="${tf(S.thumb === 'bild')}" data-act="thumb" data-v="bild" title="16:9-Vorschaubild">${ic(I.img)}</button><button role="radio" aria-checked="${tf(S.thumb === 'symbol')}" data-act="thumb" data-v="symbol" title="Symbol">${ic(I.shape)}</button></div>
      ${hd('name', 'Spiel · ' + v.length)}${hd('src', 'Quelle')}${hd('lp', 'Zuletzt gespielt')}${hd('hdr', 'HDR')}${hd('auto', 'HDR automatisch', 'c-sw')}</div>
    <div class="lv" id="lv" tabindex="0" role="listbox" aria-label="Spiele" aria-activedescendant=""><div class="lsz" style="height:${v.length * RH}px"></div></div>
    ${v.length ? '' : emptyHTML()}`;
  lv = $('#lv'); pool = new Map();
  lv.addEventListener('scroll', onListScroll, { passive: true });
  if (prev) lv.scrollTop = prev;
  updateRows();
}
/* Beim Scrollen der Liste klappt der Hero auf 120 px zusammen (mehr Zeilen); ganz oben wieder auf */
function onListScroll() {
  requestAnimationFrame(updateRows);
  if (!lv || S.view !== 'liste' || S.shot) return;
  const want = lv.scrollTop > 60 ? true : lv.scrollTop <= 0 ? false : S.lcol;
  if (want !== S.lcol) { S.lcol = want; heroSize(); setTimeout(fitToolbar, 820); }
}
function emptyHTML() { return `<div class="emp">${ic(I.search)}<div><b>${S.q.trim() ? 'Keine Treffer für „' + esc(S.q.trim()) + '“' : 'Keine Spiele für diese Filter'}</b><small>Prüfe die Schreibweise oder setze Quelle und Filter zurück.</small></div><button class="pbtn" data-act="reset">Filter zurücksetzen</button></div>`; }
function updateRows() {
  if (!lv || S.view !== 'liste') return;
  const v = S.vis, st = lv.scrollTop, h = lv.clientHeight || 600;
  const first = Math.max(0, Math.floor(st / RH) - 6), last = Math.min(v.length - 1, Math.ceil((st + h) / RH) + 6);
  for (const [k, el] of pool) if (k < first || k > last || +el.dataset.i !== v[k]) { el.remove(); pool.delete(k); }
  for (let k = first; k <= last; k++) {
    if (pool.has(k)) continue;
    const g = G[v[k]], el = document.createElement('div');
    el.className = 'row' + (g.i === S.sel ? ' sel' : ''); el.dataset.i = g.i; el.id = 'r' + g.i; el.setAttribute('role', 'option'); el.setAttribute('aria-selected', tf(g.i === S.sel));
    el.style.transform = 'translateY(' + (k * RH) + 'px)';
    if (g.i === S.sel) { const p = palOf(g); if (p) el.style.setProperty('--rc', palUse(p).a2); }
    el.innerHTML = rowHTML(g);
    lv.appendChild(el); pool.set(k, el);
  }
}
function markSelRow() {
  $$('.row.sel').forEach(r => { r.classList.remove('sel'); r.setAttribute('aria-selected', 'false'); });
  const r = $('#r' + S.sel); if (r) { r.classList.add('sel'); r.setAttribute('aria-selected', 'true'); const p = palOf(G[S.sel]); if (p) r.style.setProperty('--rc', palUse(p).a2); }
  if (lv) lv.setAttribute('aria-activedescendant', 'r' + S.sel);
  $$('.tile.sel').forEach(t => t.classList.remove('sel'));
  $$('.tile[data-i="' + S.sel + '"]').forEach(t => t.classList.add('sel'));
}
function ensureRowVisible() {
  if (!lv) return; const k = S.vis.indexOf(S.sel); if (k < 0) return;
  const top = k * RH, h = lv.clientHeight;
  if (top < lv.scrollTop - RH * 3 || top > lv.scrollTop + h + RH * 3) lv.scrollTop = top - h / 2 + RH / 2;   // weit weg: mittig
  else if (top < lv.scrollTop) lv.scrollTop = top - RH; else if (top + RH > lv.scrollTop + h) lv.scrollTop = top + RH * 2 - h;
  updateRows();
}
function refreshRow(gi) {
  const g = G[gi];
  $$('[data-i="' + gi + '"].row').forEach(el => { el.innerHTML = rowHTML(g); });
  $$('.tile[data-i="' + gi + '"]').forEach(el => { const n = tileEl(g, el.dataset.k, el.style.getPropertyValue('--x'), el.style.getPropertyValue('--y')); el.replaceWith(n); });
}

/* ========================================================================================================== */
/*  Regal und Raster                                                                                           */
/* ========================================================================================================== */
const RING = '<svg class="rg" viewBox="0 0 100 150" preserveAspectRatio="none" aria-hidden="true"><rect x="1" y="1" width="98" height="148" rx="10" pathLength="100" vector-effect="non-scaling-stroke"></rect><rect class="g2" x="1" y="1" width="98" height="148" rx="10" pathLength="100" vector-effect="non-scaling-stroke"></rect></svg>';
function tileEl(g, k, x, y) {
  const a = art(g), el = document.createElement('div');
  el.className = 'tile' + (g.i === S.sel ? ' sel' : '') + (a.cover ? '' : ' gn') + (g.auto ? ' au' : ''); el.dataset.i = g.i; el.dataset.k = k;
  el.style.setProperty('--x', x); if (y) el.style.setProperty('--y', y);
  el.innerHTML = `<div class="tw2">${RING}<div class="ti">${a.cover ? `<div class="ca"><img src="${esc(a.cover)}" alt="" loading="lazy"></div>` : coverGen(g, a, false)}
      <i class="hx"></i><div class="tsc"></div><div class="shn"></div><div class="tbg">${badges(g, true)}</div>
      <div class="tf"><div class="tn">${esc(g.n)}</div><div class="tsr" style="--sr:${srcCol(g.src)}"><i></i>${esc(srcName(g.src))}</div></div></div></div>
    <span class="tau" title="HDR automatisch ist eingeschaltet" aria-hidden="true">${ic(I.sun)}</span>
    <div class="tsw"><button class="sw xs" role="switch" aria-checked="${tf(g.auto)}" data-act="tog" data-k="auto" data-i="${g.i}" aria-label="HDR automatisch für ${esc(g.n)}"></button></div>`;
  el.setAttribute('role', 'option'); el.setAttribute('aria-label', g.n + (g.auto ? ', HDR automatisch ein' : ''));
  return el;
}
const SH = { tw: 180, th: 270, gap: 18, vis: 6 };
function shelfMetrics() {
  const b = $('#shf'), h = b.clientHeight || 300, w = b.clientWidth || 1400;
  SH.th = clamp(h - 34, 200, 340); SH.tw = Math.round(SH.th * 2 / 3); SH.vis = Math.max(2, Math.floor((w - 64 + SH.gap) / (SH.tw + SH.gap)));
}
function renderShelf() {
  const b = $('#shf'); shelfMetrics();
  const v = S.vis, maxOff = Math.max(0, v.length - SH.vis); S.off = clamp(S.off, 0, maxOff);
  b.innerHTML = `<div class="strack" id="strack" style="--tw:${SH.tw}px;--th:${SH.th}px;transform:translateX(${-S.off * (SH.tw + SH.gap)}px)"></div>
    <button class="nv l${S.off > 0 ? '' : ' off'}" data-act="shelf" data-v="-1" aria-label="Vorherige Spiele">${ic(I.left)}</button>
    <button class="nv r${S.off < maxOff ? '' : ' off'}" data-act="shelf" data-v="1" aria-label="Nächste Spiele">${ic(I.right)}</button>
    <span class="shpos">${v.length ? S.off + 1 : 0}–${Math.min(S.off + SH.vis, v.length)} von ${v.length}</span>
    ${v.length ? '' : emptyHTML()}`;
  fillShelf();
}
function fillShelf() {
  const tr = $('#strack'); if (!tr) return;
  const v = S.vis, a0 = Math.max(0, S.off - 4), a1 = Math.min(v.length - 1, S.off + SH.vis + 4), have = new Set();
  $$('.tile', tr).forEach(t => { const k = +t.dataset.k; if (k < a0 || k > a1 || +t.dataset.i !== v[k]) t.remove(); else have.add(k); });
  for (let k = a0; k <= a1; k++) if (!have.has(k)) tr.appendChild(tileEl(G[v[k]], k, k * (SH.tw + SH.gap) + 'px'));
}
function shelfGo(off) {
  const maxOff = Math.max(0, S.vis.length - SH.vis); S.off = clamp(off, 0, maxOff);
  const tr = $('#strack'); if (!tr) return;
  fillShelf(); tr.style.transform = 'translateX(' + (-S.off * (SH.tw + SH.gap)) + 'px)';
  const b = $('#shf'); $('.nv.l', b).classList.toggle('off', S.off <= 0); $('.nv.r', b).classList.toggle('off', S.off >= maxOff);
  $('.shpos', b).textContent = (S.vis.length ? S.off + 1 : 0) + '–' + Math.min(S.off + SH.vis, S.vis.length) + ' von ' + S.vis.length;
}
const GR = { tw: 150, th: 225, gap: 16, cols: 5 };
function renderGrid() {
  const b = $('#grd'), w = (b.clientWidth || 1000) - 8;
  GR.cols = Math.max(2, Math.floor((w + GR.gap) / (GR.tw + GR.gap)));
  GR.tw = Math.floor((w - (GR.cols - 1) * GR.gap) / GR.cols); GR.th = Math.round(GR.tw * 1.5);
  const rows = Math.ceil(S.vis.length / GR.cols);
  b.innerHTML = `<div class="gsz" style="height:${rows * (GR.th + GR.gap) + 24}px"></div><div class="gtr" id="gtr" style="--tw:${GR.tw}px;--th:${GR.th}px"></div>${S.vis.length ? '' : emptyHTML()}`;
  b.onscroll = () => requestAnimationFrame(fillGrid);
  fillGrid();
}
function fillGrid() {
  const b = $('#grd'), tr = $('#gtr'); if (!tr || S.view !== 'raster') return;
  const rh = GR.th + GR.gap, r0 = Math.max(0, Math.floor(b.scrollTop / rh) - 1), r1 = Math.ceil((b.scrollTop + b.clientHeight) / rh) + 1;
  const a0 = r0 * GR.cols, a1 = Math.min(S.vis.length - 1, (r1 + 1) * GR.cols - 1), have = new Set();
  $$('.tile', tr).forEach(t => { const k = +t.dataset.k; if (k < a0 || k > a1 || +t.dataset.i !== S.vis[k]) t.remove(); else have.add(k); });
  for (let k = a0; k <= a1; k++) if (!have.has(k)) tr.appendChild(tileEl(G[S.vis[k]], k, (k % GR.cols) * (GR.tw + GR.gap) + 'px', Math.floor(k / GR.cols) * rh + 'px'));
}
function ensureTileVisible() {
  const k = S.vis.indexOf(S.sel); if (k < 0) return;
  if (S.view === 'regal') { if (k < S.off) shelfGo(k); else if (k >= S.off + SH.vis) shelfGo(k - SH.vis + 1); }
  if (S.view === 'raster') {
    const b = $('#grd'), rh = GR.th + GR.gap, row = Math.floor(k / GR.cols), top = row * rh;
    if (top < b.scrollTop - rh * 2 || top > b.scrollTop + b.clientHeight + rh) b.scrollTop = Math.max(0, (row - 1) * rh);
    else if (top < b.scrollTop) b.scrollTop = top; else if (top + rh > b.scrollTop + b.clientHeight) b.scrollTop = top + rh - b.clientHeight + 16;
    fillGrid();
  }
}
/* Glanz unter der Maus (nur 2D – kein Kippen) */
function tileShine(e) {
  if (fxEff()) return;
  const t = e.target.closest('.tile'); if (!t) return;
  const r = t.getBoundingClientRect();
  t.style.setProperty('--mx', ((e.clientX - r.left) / r.width * 100).toFixed(0) + '%'); t.style.setProperty('--my', ((e.clientY - r.top) / r.height * 100).toFixed(0) + '%');
}

/* ========================================================================================================== */
/*  Fußzeile, Hinweise                                                                                         */
/* ========================================================================================================== */
function renderStatus() {
  const run = S.run >= 0, so = SORTS.find(s => s[0] === S.sort.k) || SORTS[0];
  const st = run ? 'Spiel läuft · ' + esc(G[S.run].n) + ' · Effekte pausiert' : S.pause ? 'Automatik pausiert · HDR wird nicht geschaltet' : 'Automatik aktiv · ' + (S.hdr ? 'HDR ein' : 'wartet auf Spielstart');
  const vn = { liste: 'Liste', regal: 'Regal', raster: 'Raster' }[S.view];
  const zz = S.zz && !run, fx = fxEff();
  $('#status').innerHTML = `<span class="st${run ? ' r' : S.pause ? ' p' : ''}"><i></i>${st}</span><span class="vw">${vn} · ${S.vis.length} von ${G.length} · sortiert nach ${so[1]}</span><span class="sp"></span>
    <span class="keys"><kbd>↑</kbd><kbd>↓</kbd> Auswahl</span><span class="keys"><kbd>Enter</kbd> Starten</span><span class="keys"><kbd>Leertaste</kbd> HDR automatisch</span><span class="keys"><kbd>Umschalt+F10</kbd> Menü</span>
    <span class="pf${zz || run ? ' zz' : ''}">${ic(I.bolt)}${zz ? 'Effekte pausiert · ' + esc(S.zzWhy || 'Fenster im Hintergrund') : run ? 'Effekte pausiert, solange das Spiel läuft' : 'Effekte ' + FXN[fx] + (fx !== S.fx ? ' (Windows-Einstellung)' : '') + (fx < 2 ? ' · pausieren im Hintergrund und beim Spielen' : '')}</span>`;
}
let toastT = 0;
function toast(msg, sub, icon) {
  const t = $('#toast'); S.tk++;
  t.innerHTML = `<span class="tic">${ic(icon || I.ok)}</span><b>${esc(msg)}</b><small>${esc(sub || '')}</small>`;
  t.className = 'toast'; void t.offsetWidth; t.className = 'toast k' + (S.tk % 2 ? 1 : 2);
  if (fxEff() === 2) { clearTimeout(toastT); const s = $('#status .st'); if (s) s.innerHTML = '<i></i>' + esc(msg + (sub ? ' · ' + sub : '')); toastT = setTimeout(renderStatus, 4000); }
}
function wave(e) {
  const el = e && e.target && e.target.closest('button,label'); if (!el) return;
  const r = el.getBoundingClientRect(), w = $('#wv');
  w.style.setProperty('--wx', (r.left + r.width / 2) + 'px'); w.style.setProperty('--wy', (r.top + r.height / 2) + 'px');
  S.ck++; w.className = 'wv'; void w.offsetWidth; w.className = 'wv k' + (S.ck % 2 ? 1 : 2);
}

/* ========================================================================================================== */
/*  Auswahl, Ansicht, Starten                                                                                  */
/* ========================================================================================================== */
let selT = 0;
function select(gi, opt = {}) {
  if (!G[gi]) return;
  const changed = gi !== S.sel; S.sel = gi;
  clearTimeout(selT);
  if (changed || opt.force) {
    if (opt.defer) selT = setTimeout(() => { showHero(S.sel, false); renderPane(); }, 120);   // Taste gehalten: Bild/Detail entprellt
    else { showHero(gi, opt.instant); renderPane(); }
  }
  markSelRow();
  if (opt.scroll) { if (S.view === 'liste') ensureRowVisible(); else ensureTileVisible(); }
  renderStatus();
}
function setView(v, keepScroll) {
  if (!['liste', 'regal', 'raster'].includes(v)) v = 'liste';
  S.view = v; LS.set('view', v); S.menu = null; if (v !== 'liste') S.lcol = false;
  setDrawer(false);
  app.classList.remove('v-liste', 'v-regal', 'v-raster'); app.classList.add('v-' + v);
  heroSize();
  renderToolbar(); renderBody(); if (!keepScroll) select(S.sel, { scroll: true }); renderStatus();
  // nach der Höhenänderung des Hero (0,8 s) Regal/Raster neu vermessen
  setTimeout(() => { if (S.view === 'regal') renderShelf(); if (S.view === 'raster') renderGrid(); fitToolbar(); select(S.sel, { scroll: true }); applyAmbient(); }, fxEff() === 2 ? 0 : 820);
}
function renderBody() {
  computeView();
  if (S.view === 'liste') renderList(); else { $('#lst').innerHTML = ''; lv = null; }
  if (S.view === 'regal') renderShelf(); else $('#shf').innerHTML = '';
  if (S.view === 'raster') renderGrid(); else $('#grd').innerHTML = '';
}
function reselect() { if (S.vis.length && S.vis.indexOf(S.sel) < 0) select(S.vis[0], { scroll: true }); else markSelRow(); }
function refilter() { S.off = 0; renderToolbar(); renderBody(); reselect(); renderStatus(); }
function start(gi) {
  const g = G[gi];
  if (S.run >= 0) {   // laufendes Spiel beenden (Simulation)
    const was = G[S.run], wasAuto = S.runAuto; S.run = -1; setZz(false, 'run');
    if (wasAuto && was.rest && S.hdr) setHdr(false, 'Spiel beendet · HDR zurückgestellt');
    else toast('Spiel beendet', wasAuto ? 'HDR bleibt ein · Zurückstellen ist aus' : was.n, I.ok);
    renderHeroContent(G[S.sel]); renderPane(); renderStatus(); updateIcons(); return;
  }
  S.run = gi; S.runAuto = false; renderHeroContent(G[S.sel]); renderPane(); renderStatus();
  if (S.pause) toast('Automatik pausiert – HDR bleibt aus', g.n + ' läuft ohne HDR-Wechsel', I.pause);
  else if (!g.auto) toast(g.n + ' gestartet', 'HDR automatisch ist für dieses Spiel aus', I.play);
  else if (S.hdr) toast(g.n + ' gestartet', 'HDR ist bereits eingeschaltet', I.play);
  else { toast(g.n + ' wird gestartet …', 'HDR schaltet sich ein, sobald das Spiel läuft', I.play); setTimeout(() => { if (S.run === gi && !S.hdr) { S.runAuto = true; setHdr(true, 'HDR automatisch eingeschaltet · ' + g.n); setTimeout(() => { if (S.run === gi) setZz(true, 'run'); }, 2600); } }, fxEff() === 2 ? 0 : 1300); return; }
  setTimeout(() => { if (S.run === gi) setZz(true, 'run'); }, 600);
  updateIcons();
}

/* Pausieren aller Daueranimationen: Fenster verdeckt/minimiert/inaktiv oder Spiel läuft */
const zzSet = new Set();
function setZz(on, why) {
  if (on) zzSet.add(why); else zzSet.delete(why);
  if (S.forceZz) zzSet.add('force');
  S.zz = ((zzSet.has('hidden') || zzSet.has('blur')) && S.sws[1] !== 0) || (zzSet.has('run') && S.sws[0] !== 0) || zzSet.has('force');
  S.zzWhy = zzSet.has('hidden') ? 'Fenster minimiert' : zzSet.has('blur') ? 'Fenster im Hintergrund' : zzSet.has('force') ? 'Fenster im Hintergrund' : '';
  app.classList.toggle('zz', S.zz);
  if (!S.zz && pfx.ps.length && !pfx.raf && S.page === 'bibliothek') { pfx.last = performance.now(); pfx.raf = requestAnimationFrame(tick); }
  renderStatus();
}

/* ========================================================================================================== */
/*  Kontextmenü                                                                                                */
/* ========================================================================================================== */
function openCtx(gi, x, y) {
  const g = G[gi]; if (!g) return;
  if (gi !== S.sel) select(gi);
  S.ctx = gi; const m = $('#ctx'), run = S.run === gi;
  m.innerHTML = `<div class="chd">${thumbHTML(g)}<div><b>${esc(g.n)}</b><small>${esc(srcName(g.src))} · ${esc(when(g.lp))}</small></div></div>
    <button class="mi acc" role="menuitem" data-act="start">${ic(run ? I.stop : I.play)}${run ? 'Spiel beenden (Simulation)' : 'Starten'}<span>Enter</span></button>
    <button class="mi" role="menuitemcheckbox" aria-checked="${tf(g.auto)}" data-act="tog" data-k="auto" data-i="${gi}">${ic(I.sun)}HDR automatisch<svg class="ic ck" viewBox="0 0 24 24"><path d="${I.check}"></path></svg></button>
    <button class="mi" role="menuitemcheckbox" aria-checked="${tf(g.rest)}" data-act="tog" data-k="rest" data-i="${gi}">${ic(I.reset)}Nach dem Spiel zurückstellen<svg class="ic ck" viewBox="0 0 24 24"><path d="${I.check}"></path></svg></button>
    <button class="mi" role="menuitemcheckbox" aria-checked="${tf(g.ahdr)}" data-act="tog" data-k="ahdr" data-i="${gi}">${ic(I.pulse)}Windows Auto HDR<svg class="ic ck" viewBox="0 0 24 24"><path d="${I.check}"></path></svg></button>
    <hr>
    <button class="mi acc" role="menuitem" data-act="dlg" data-v="bild">${ic(I.img)}Bild wählen …</button>
    <button class="mi acc" role="menuitem" data-act="dlg" data-v="symbol">${ic(I.shape)}Symbol ändern …</button>
    <hr>
    <button class="mi" role="menuitem" data-act="shortcut" data-v="desktop">${ic(I.desk)}Desktop-Verknüpfung erstellen</button>
    <button class="mi" role="menuitem" data-act="shortcut" data-v="start">${ic(I.startm)}Zum Startmenü hinzufügen</button>
    <button class="mi" role="menuitem" data-act="toast" data-v="Installationsordner|${esc(g.dir)}">${ic(I.folder)}Installationsordner öffnen</button>
    <hr>
    <button class="mi dng" role="menuitem" data-act="hide">${ic(I.hide)}Aus der Bibliothek ausblenden</button>`;
  m.hidden = false;
  const W = innerWidth, H = innerHeight, r = m.getBoundingClientRect();
  const left = x + r.width > W - 8 ? Math.max(8, x - r.width) : x, top = y + r.height > H - 8 ? Math.max(8, H - r.height - 8) : y;
  m.style.left = left + 'px'; m.style.top = top + 'px'; m.style.setProperty('--ox', (x - left) + 'px'); m.style.setProperty('--oy', (y - top) + 'px');
  const f = $('.mi', m); f && f.focus({ preventScroll: true });
}
function closeCtx() { const m = $('#ctx'); if (!m.hidden) { m.hidden = true; S.ctx = null; } }

/* ========================================================================================================== */
/*  Dialoge: Bild wählen, Symbol ändern                                                                        */
/* ========================================================================================================== */
const TABN = { hero: 'Titelbild', logo: 'Logo', cover: 'Cover' };
function openDlg(type, tab) {
  const g = G[S.sel]; closeCtx(); setDrawer(false);
  if (tab === 'icon') { type = 'symbol'; tab = null; }
  const t = tab || 'hero';
  S.dlg = { type, gi: g.i, tab: t, pick: type === 'symbol' ? symPick(g) : curPick(t, g), file: null };
  renderDlg();
}
function symPick(g) {
  const o = S.ovr[gkey(g)] || {}, b = blobs[gkey(g)] || {};
  if (b.icon) return 'file'; if (o.icon === 'mono') return 'mono';
  const k = (g.ico || []).findIndex(c => c.url && g.img.icon === c.url); return k >= 0 ? 'cand' + k : g.img.iconKind === 'exe' ? 'exe-art' : 'mono';
}
function curPick(t, g0) { const g = g0 || G[S.dlg.gi], o = S.ovr[gkey(g)] || {}, b = blobs[gkey(g)] || {}; return b[t] ? 'file' : o[t] === 'gen' ? 'gen' : /^alt:/.test(o[t] || '') ? o[t] : o[t] === 'local' ? 'local' : (t === 'cover' && g.img.coverSmear) || !g.img[t] ? 'gen' : 'local'; }
function renderDlg() {
  const w = $('#dlgw'), d = S.dlg; if (!d) { w.hidden = true; w.innerHTML = ''; return; }
  const g = G[d.gi], im = g.img || {}, p = palOf(g) || namePal(g), a0 = art(g), fbg = p.field ? `url(${p.field})` : nameField(p);
  const where = g.src === 'Steam' ? 'Steam-Cache' : CACHE_N[g.src.toLowerCase()] || (g.src === 'Xbox' ? 'Xbox-Paket' : srcName(g.src));
  let body = '';
  if (d.type === 'bild') {
    const t = d.tab, local = im[t], port = t === 'cover', ct = t === 'logo';
    const icoBox = `<div class="mini-ic${a0.iconOpaque ? ' art' : ''}">${a0.icon ? `<img src="${esc(a0.icon)}" alt="">` : `<span class="mono-l">${esc(mono(g.n))}</span>`}</div>`;
    const genPv = t === 'hero' ? `<div class="gfield" style="background-image:${fbg}"></div><div class="gshade"></div>${icoBox}<div class="mini-n" style="left:72px">${esc(g.n)}</div>`
      : t === 'logo' ? `<div class="gfield" style="background-image:${fbg}"></div><div class="mini-n" style="bottom:auto;top:50%;transform:translateY(-50%);font-size:15px;white-space:normal">${esc(g.n)}</div>`
        : im.hero ? `<img src="${esc(im.hero)}" alt="" style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover;object-position:${Math.round(clamp(+(p.lx || .5), .2, .8) * 100)}% 30%"><div class="gshade"></div>${im.logo ? `<img src="${esc(im.logo)}" alt="" style="position:absolute;left:12%;right:12%;bottom:14%;width:76%;max-height:26%;object-fit:contain">` : `<div class="mini-n">${esc(g.n)}</div>`}`
          : `<div class="gfield" style="background-image:${fbg}"></div>${icoBox.replace('mini-ic', 'mini-ic top')}<div class="mini-n">${esc(g.n)}</div>`;
    const fileUrl = d.file || (blobs[gkey(g)] || {})[t];
    const card = (id, pv, title, sub, opts = {}) => `<button class="cho" role="radio" aria-checked="${tf(d.pick === id)}" data-act="dpick" data-v="${id}" ${opts.dis ? 'disabled' : ''}><span class="pv${port ? ' port' : ''}${opts.ct ? ' ct' : ''}">${pv}${opts.tag ? `<span class="tagx${opts.off ? ' off' : ''}">${opts.tag}</span>` : ''}</span><span class="cx"><b>${title}</b><small>${sub}</small></span></button>`;
    const dim = t === 'hero' ? (im.heroW ? ' · ' + im.heroW + ' × ' + im.heroH : '') : t === 'cover' ? (im.coverW ? ' · ' + im.coverW + ' × ' + im.coverH : '') : ' · transparent';
    const fileName = { hero: 'Titelbild', logo: 'Logo', cover: im.coverKind === 'capsule' ? 'Kapsel' : 'Hochformat' }[t];
    const smearNote = t === 'cover' && im.coverSmear ? ' · von Steam verschmiert' : '';
    const alts = (im.alts || []).map((x, i) => [x, i]).filter(([x]) => x.t === t);
    body = `<div class="seg tabs" role="radiogroup" aria-label="Bildart">${Object.keys(TABN).map(k => `<button role="radio" aria-checked="${tf(t === k)}" data-act="dtab" data-v="${k}">${TABN[k]}</button>`).join('')}</div>
      <div class="chs">
        ${card('local', local ? `<img src="${esc(local)}" alt="">` : `<span class="ph2">${ic(I.disk)}</span>`, 'Lokal · ' + where, local ? fileName + dim + smearNote : 'Nicht im Cache vorhanden', { dis: !local, ct, tag: local && curPick(t) === 'local' ? 'aktuell' : '' })}
        ${alts.map(([x, i]) => card('alt:' + i, `<img src="${esc(x.url)}" alt="">`, esc(x.label), esc((x.w ? x.w + ' × ' + x.h + ' · ' : '') + x.note), { tag: curPick(t) === 'alt:' + i ? 'aktuell' : '' })).join('')}
        ${card('gen', genPv, 'Erzeugt', t === 'hero' ? 'Farbfeld aus dem Symbol, Symbol und Name' : t === 'logo' ? 'Spielname in der Titelschrift' : im.hero ? 'Ausschnitt aus dem Titelbild mit ' + (im.logo ? 'Logo' : 'Name') : 'Symbol und Name auf dem Farbfeld', { tag: curPick(t) === 'gen' ? 'aktuell' : '' })}
        ${card('file', fileUrl ? `<img src="${esc(fileUrl)}" alt="">` : `<span class="ph2">${ic(I.upload)}</span>`, 'Eigene Datei …', fileUrl ? 'Nur auf diesem PC · wird nicht hochgeladen' : 'PNG, JPG oder WebP von diesem PC', { ct, tag: curPick(t) === 'file' ? 'aktuell' : '' })}
        ${card('sgdb', `<span class="ph2">${ic(I.cloud)}</span>`, 'SteamGridDB', S.sgdb ? (local && !smearNote ? 'Nur für fehlende Bilder · hier vorhanden' : 'Online-Suche · im Prototyp ohne Verbindung') : 'Aus · unter Darstellung › Bilder einschalten', { dis: !S.sgdb || (!!local && !smearNote), tag: S.sgdb ? 'online' : 'aus', off: !S.sgdb })}
      </div>
      <div class="dnote">${ic(I.info)}<span>HDRpilot liest Bilder nur aus den lokalen Caches der Launcher (Steam, GOG Galaxy, Ubisoft, Xbox-Paket). Fehlt eines, wird es aus Symbol und Name erzeugt. ${S.sgdb ? 'SteamGridDB füllt nur Lücken und überschreibt nie lokale Bilder.' : 'SteamGridDB (online) ist ausgeschaltet.'}</span></div>`;
  } else {
    const cands = (g.ico || []).map((c, i) => ({ id: 'cand' + i, url: c.url, path: c.path, px: c.px }));
    const fileUrl = d.file || (blobs[gkey(g)] || {}).icon;
    body = `<div class="icl" role="radiogroup" aria-label="Symbol">
      ${cands.slice(0, 3).map(c => { const fn = c.path.split('\\').pop(), dir = c.path.slice(0, c.path.length - fn.length - 1); return `<button class="ico-o" role="radio" aria-checked="${tf(d.pick === c.id)}" data-act="dpick" data-v="${c.id}" ${c.url ? '' : 'disabled'} title="${esc(c.path)}"><span class="ip">${c.url ? `<img src="${esc(c.url)}" alt="">` : ic(I.img)}</span><div><b>${c.url ? (/steam\\games/i.test(c.path) ? 'Steam-Symbol' : /goggame/i.test(c.path) ? 'GOG-Symbol' : /Square480/i.test(c.path) ? 'Xbox-Kachelbild' : 'Symbol') + ' · ' + esc(fn.length > 28 ? fn.slice(0, 10) + '…' + fn.slice(-12) : fn) : esc(fn)}</b><small class="dir">${esc(midPath(dir, 420, '500 11.5px Manrope'))}</small></div><span class="sz">${c.url ? (c.px ? c.px + ' px' : fn.split('.').pop().toLowerCase()) : 'fehlt'}</span></button>`; }).join('')}
      ${cands.length > 3 ? `<div class="none" style="padding:0 4px">+ ${cands.length - 3} weitere Kandidaten im Spielordner</div>` : ''}
      ${g.img.iconKind === 'exe' ? `<button class="ico-o" role="radio" aria-checked="${tf(d.pick === 'exe-art')}" data-act="dpick" data-v="exe-art"><span class="ip"><img src="${esc(g.img.icon)}" alt=""></span><div><b>Aus ${esc(g.img.iconExe || 'EXE')} gelesen</b><small class="dir">Größtes Symbolbild der Haupt-EXE</small></div><span class="sz">${g.img.iconPx} px</span></button>` : g.exe.slice(0, 1).map((p, i) => `<button class="ico-o" role="radio" aria-checked="${tf(d.pick === 'exe' + i)}" data-act="dpick" data-v="exe${i}"><span class="ip">${ic(I.file)}</span><div><b>Aus EXE-Datei lesen</b><small>${esc(midPath(p, 420, '500 11px JetBrains Mono'))}</small></div><span class="sz">in der App</span></button>`).join('')}
      <button class="ico-o" role="radio" aria-checked="${tf(d.pick === 'mono')}" data-act="dpick" data-v="mono"><span class="ip" style="background:linear-gradient(135deg,${p.a2},${p.a1})"><b style="font:800 13px Unbounded;color:#fff">${esc(mono(g.n))}</b></span><div><b>Monogramm</b><small class="dir">Anfangsbuchstaben auf den Spielfarben</small></div></button>
      <button class="ico-o" role="radio" aria-checked="${tf(d.pick === 'file')}" data-act="dpick" data-v="file"><span class="ip">${fileUrl ? `<img src="${esc(fileUrl)}" alt="">` : ic(I.upload)}</span><div><b>Eigene Datei …</b><small class="dir">${fileUrl ? 'Gewählt · bleibt auf diesem PC' : '.ico oder .png von diesem PC'}</small></div></button>
    </div>
    <div class="dnote">${ic(I.info)}<span>Das Symbol erscheint in der Liste, im Kontextmenü und in erzeugten Titelbildern und Covern. Gewählt wird automatisch das größte verfügbare Bild.</span></div>`;
  }
  w.innerHTML = `<div class="dlg" role="dialog" aria-modal="true" aria-label="${d.type === 'bild' ? 'Bild wählen' : 'Symbol ändern'}">
    <div class="dh"><div><h2>${d.type === 'bild' ? 'Bild wählen' : 'Symbol ändern'}</h2><p class="sub">${esc(g.n)} · ${esc(srcName(g.src))}</p></div><button class="ib s" data-act="dclose" aria-label="Schließen">${ic(I.x)}</button></div>
    ${body}
    <div class="dft"><button class="pbtn" data-act="dreset">${ic(I.reset)}Zurücksetzen</button><span class="sp"></span><button class="pbtn" data-act="dclose">Abbrechen</button><button class="pbtn pri" data-act="dapply">${ic(I.check)}Übernehmen</button></div></div>`;
  w.hidden = false;
}
function applyDlg() {
  const d = S.dlg, g = G[d.gi], k = gkey(g), o = Object.assign({}, S.ovr[k] || {}), b = blobs[k] || (blobs[k] = {});
  const t = d.type === 'bild' ? d.tab : 'icon';
  if (d.pick === 'file') { if (d.file) b[t] = d.file; else if (!b[t]) { $('#filepick').click(); return; } delete o[t]; }
  else {
    delete b[t];
    if (d.pick === 'gen') { if (t === 'cover' && g.img.coverSmear) delete o[t]; else o[t] = 'gen'; }
    else if (d.pick === 'mono') o.icon = 'mono';
    else if (/^alt:/.test(d.pick)) o[t] = d.pick;
    else if (d.pick === 'local' && t === 'cover' && g.img.coverSmear) o[t] = 'local';
    else delete o[t];
  }
  if (Object.keys(o).length) S.ovr[k] = o; else delete S.ovr[k];
  LS.set('ovr', S.ovr);
  S.dlg = null; renderDlg();
  heroGi = -1; showHero(g.i, true); renderPane(); refreshRow(g.i);
  toast(d.type === 'bild' ? TABN[t] + ' übernommen' : 'Symbol übernommen', g.n, I.img);
}

/* ========================================================================================================== */
/*  Seite „Darstellung“                                                                                         */
/* ========================================================================================================== */
function trayStatus() { return (S.pause ? 'Pausiert · ' : '') + (S.hdr ? 'HDR ein' : 'HDR aus') + ' · ' + MON.name; }
function pvGames() {
  const recent = G.filter(g => g.img.hero && g.lp).sort((a, b) => b.lpt - a.lpt).slice(0, 3);
  const fb = G.filter(g => !g.img.hero && g.img.icon).sort((a, b) => (b.img.iconPx - a.img.iconPx) || ((b.lpt || 0) - (a.lpt || 0)))[0];
  return recent.concat(fb ? [fb] : []);
}
/* je Ersatzfall ein echtes Beispiel aus der Bibliothek */
function fbExamples() {
  const by = f => G.filter(f).sort((a, b) => (b.lpt || 0) - (a.lpt || 0))[0];
  return [
    [by(g => !g.img.hero && g.src !== 'Steam' && g.img.iconKind === 'exe' && g.img.iconPx >= 128) || by(g => !g.img.hero), 0],
    [by(g => g.img.hero && !g.img.logo && g.src !== 'Steam') || by(g => g.img.hero && !g.img.logo), 1],
    [by(g => !g.img.cover && !g.img.hero && g.img.iconOpaque) || by(g => !g.img.cover), 2],
    [by(g => g.img.coverSmear), 3]
  ].filter(x => x[0]);
}
let lookY = 0;
function renderLook() {   // Scroll-Position bleibt bei jedem Neuaufbau erhalten
  const m = $('#lookMain'), y = m ? m.scrollTop : lookY;
  renderLookInner();
  const m2 = $('#lookMain'); if (m2) { m2.scrollTop = y; lookY = y; m2.onscroll = () => { lookY = m2.scrollTop; }; }
}
function renderLookInner() {
  const n3 = S.n3, c1 = S.c[0], c2 = S.c[1], c3 = c3Of(), dark = themeDark();
  const sf = dark ? '#14151D' : '#FFFFFF', dk = Math.min(ratio(c1, '#0B0C12'), ratio(c2, '#0B0C12')), wh = Math.min(ratio(c1, '#FFFFFF'), ratio(c2, '#FFFFFF'));
  const on = S.autoTxt ? (dk >= wh ? '#0B0C12' : '#FFFFFF') : '#FFFFFF', onR = on === '#FFFFFF' ? wh : dk, atc = S.autoTxt ? readable(c1, sf, dark) : c1, atR = ratio(atc, sf);
  const grade = r => r >= 7 ? ['okc', 'AAA'] : r >= 4.5 ? ['okc', 'AA'] : r >= 3 ? ['wrn', 'AA groß'] : ['bad', 'zu niedrig'], f2 = r => r.toFixed(1).replace('.', ',') + ':1 · ';
  const g1 = grade(onR), g2 = grade(atR);
  const roles = [['Akzent', 'Schaltflächen, Auswahl'], ['Atmosphäre', 'Hintergrund, Leuchten'], ['Glanz', 'HDR-Moment, Lichtstreifen']], cur = [c1, c2, c3];
  const navL = ['Allgemein', 'Bildschirme', 'Automatik', 'Suchordner', 'Hilfsprozesse', 'Darstellung', 'Erweitert', 'Protokolle'];
  const pvs = pvGames(); if (S.pvSel < 0 || !G[S.pvSel]) S.pvSel = (pvs.find(g => g.i === S.sel) || pvs[0] || G[0]).i;
  const pg = G[S.pvSel], pa = art(pg), pp = palUse(palOf(pg) || namePal(pg)), hdrP = S.pvHdr;
  const cov = D.coverage, srcKeys = Object.keys(cov);
  const cell = (u, f, t, note) => `<td class="n" title="${u} genutzt · ${f} gefunden · ${t} Spiele${note ? ' · ' + note : ''}"><span class="bar${u ? '' : ' z'}"><i style="--p:${t ? Math.round(u / t * 100) : 0}%"></i></span>${u}/${t}${f !== u ? `<sup>${f > u ? '+' + (f - u) : ''}</sup>` : ''}</td>`;
  const miss = { hero: G.filter(g => !g.img.hero).length, logo: G.filter(g => !g.img.logo).length, cover: G.filter(g => !g.img.cover || g.img.coverSmear).length };
  const diffs = srcKeys.flatMap(k => { const c = cov[k], o = []; if (c.found.hero > c.used.hero) o.push(`${c.name}: ${c.found.hero - c.used.hero} Titelbild${c.found.hero - c.used.hero > 1 ? 'er' : ''} gefunden, aber nicht genutzt (Splash: Engine-Logo bzw. Titelkarte) – unter „Bild wählen …“ wählbar`); if (c.smear) o.push(`${c.name}: ${c.smear} verschmierte Kapseln → Cover aus dem Titelbild`); if (c.exeIcons) o.push(`${c.name}: ${c.exeIcons} Symbol${c.exeIcons > 1 ? 'e' : ''} aus der EXE gelesen`); return o; });
  const fbs = fbExamples();
  const fbCard = (g, kind) => {
    const a = art(g), p = palOf(g) || namePal(g);
    let pv = '', t = '', s = '';
    if (kind === 0) { pv = `<div class="slot gen on" style="opacity:1;transform:none">${genBanner(g, a, p)}</div><div class="fgt">${icoTile(g, a)}<h3 class="tt" data-t="${esc(g.n)}">${esc(g.n)}</h3></div>`; t = 'Titelbild aus Symbol'; s = srcName(g.src) + ' · ' + art(g).iconSrc; }
    else if (kind === 1) { pv = `<img class="hb2" src="${esc(a.hero)}" alt=""><div class="fsc"></div><h3 class="tt" data-t="${esc(g.n)}">${esc(g.n)}</h3>`; t = 'Name statt Logo'; s = srcName(g.src) + ' · kein Logo im Cache'; }
    else if (kind === 2) { pv = `<div class="gfield" style="background-image:${p.field ? `url(${p.field})` : nameField(p)}"></div>` + coverGen(g, a, false); t = 'Cover erzeugt'; s = srcName(g.src) + ' · ' + (a.iconOpaque ? 'aus dem Kachelbild' : 'aus dem Symbol'); }
    else { pv = `<img class="smr" src="${esc(g.img.cover)}" alt=""><span class="arr">${ic(I.arrow)}</span>` + coverGen(g, a, false); t = 'Kapsel verschmiert → Ausschnitt'; s = g.n + ' · Steam'; }
    return `<button class="fbc k${kind}" data-act="gosel" data-i="${g.i}" title="${esc(g.n)} in der Bibliothek zeigen"><div class="fpv">${pv}</div><b>${t}</b><small>${esc(kind === 3 ? s : g.n + ' · ' + s)}</small></button>`;
  };
  const ist = (id, l, sub, cls) => `<div class="ist${cls ? ' cur' : ''}"><svg viewBox="0 0 24 24" aria-hidden="true"><use href="#${id}L"></use></svg>${l}<small>${sub}</small></div>`;
  const cs = iconState(), osRed = reducedByOs();
  $('#look').innerHTML = `<div class="bgl"></div>
  <aside class="side">
    <div class="lgl"><div class="lgo"><svg viewBox="0 0 24 24" class="glyph" aria-hidden="true"><path class="lgS" d="M7 17a5 5 0 0 1 10 0z"></path><path d="M4 17h16M7 17a5 5 0 0 1 10 0M12 6.5v2.2M6.4 9.4l1.5 1.5M17.6 9.4l-1.5 1.5"></path></svg></div><div><b>Einstellungen</b><small>HDRpilot 1.4.0</small></div></div>
    ${navL.map((l, i) => `<button class="navb" aria-current="${i === 5 ? 'page' : 'false'}" ${i === 5 ? '' : `data-act="toast" data-v="${l}|Im Prototyp ist nur „Darstellung“ ausgearbeitet"`}>${ic(I.nav[i])}${l}</button>${i === 5 ? `<button class="navb sub" data-act="lookjump" data-v="lk-farben">Farben</button><button class="navb sub" data-act="lookjump" data-v="lk-bilder">Bilder</button><button class="navb sub" data-act="lookjump" data-v="lk-symbole">Symbole und Tray</button>` : ''}`).join('')}
    <div class="sidef"><span class="cap2">Bibliothek</span><b>${G.length} Spiele · ${G.filter(g => g.auto).length} mit HDR automatisch</b><button class="pbtn" data-act="page" data-v="bibliothek">Spielebibliothek öffnen${ic(I.arrow)}</button></div>
  </aside>
  <div class="main" id="lookMain">
    <div class="mh"><div><span class="cap2">Einstellungen</span><h1>Darstellung</h1><p>Farben, Thema, Effekte und Bilder. Wirkt auf Oberfläche, Tray-Symbol und Fenstersymbol.</p></div>
      <div class="mht"><span class="live"><i></i>Wird sofort angewendet</span><button class="pbtn" data-act="lreset">${ic(I.reset)}Standard</button><button class="pbtn pri" data-act="page" data-v="bibliothek">${ic(I.check)}Fertig</button></div></div>
    <div class="cols">
      <div class="colc">
        <section class="card" id="lk-farben">
          <div class="cardh"><span class="cib">${ic(I.pal)}</span><div><h2>Farbschema</h2><p>Jede Farbe hat eine eigene Aufgabe</p></div>
            <button class="ibtn" data-act="swap" aria-label="Akzent und Atmosphäre tauschen" title="Akzent und Atmosphäre tauschen">${ic(I.swap)}</button>
            <div class="seg" role="radiogroup" aria-label="Anzahl Farben"><button role="radio" aria-checked="${tf(!n3)}" data-act="n3" data-v="0">Duoton</button><button role="radio" aria-checked="${tf(n3)}" data-act="n3" data-v="1">Tritonus</button></div></div>
          <div class="wells">${roles.map((r, i) => { const auto = i === 2 && !n3; return `<label class="wl${auto ? ' auto' : ''}"><span class="swt" style="--c:${cur[i]}"><em>${i + 1}</em>${auto ? '' : `<input type="color" value="${cur[i].toLowerCase()}" data-act="well" data-v="${i}" aria-label="Farbe ${r[0]} wählen">`}</span><span class="wtx"><b>${r[0]}</b><small>${auto ? 'automatisch' : cur[i]}</small><span class="role">${r[1]}</span></span></label>`; }).join('')}</div>
          <div class="gbar"></div>
          <div class="lbl2"><span class="cap2">Vorlagen</span><small>${S.pr >= 0 ? P[S.pr][0] : 'Eigene Farben'} · ${n3 ? 'Tritonus, 3 Farben' : 'Duoton, Glanz automatisch'}</small></div>
          <div class="pg" role="radiogroup" aria-label="Farbvorlagen">${P.map((p, i) => `<button class="pc" role="radio" aria-checked="${tf(i === S.pr)}" data-act="preset" data-v="${i}" aria-label="Vorlage ${p[0]}" title="${p[0]}" style="--pg:linear-gradient(100deg,${p[2] === 3 ? p[1].join(',') : p[1][0] + ',' + p[1][1]})"><span class="pv"></span><em>${p[2] === 3 ? 'TRI' : 'DUO'}</em><span>${p[0]}</span></button>`).join('')}</div>
          <div class="cr"><span class="smp">Aa</span><div><b>Textfarbe auf Akzentflächen automatisch</b><div class="chips"><span class="cchip">Auf Akzent <i class="${g1[0]}">${f2(onR) + g1[1]}</i></span><span class="cchip">Akzenttext <i class="${g2[0]}">${f2(atR) + g2[1]}</i></span></div></div><button class="sw" role="switch" aria-checked="${tf(S.autoTxt)}" data-act="ltog" data-v="autoTxt" aria-label="Textfarbe automatisch"></button></div>
          <div class="cr"><span class="smp img" style="background-image:${(() => { const a = art(G[S.sel]), p = palOf(G[S.sel]); return a.blur ? `url('${esc(a.blur)}')` : p && p.field ? `url(${p.field})` : 'none'; })()};background-color:${(palOf(G[S.sel]) || namePal(G[S.sel])).a2}"></span><div><b>Hintergrund folgt dem Spiel</b><small>Umgebungslicht und Leuchten übernehmen die Farben des gewählten Titelbilds – einmal pro Spiel ausgelesen</small></div><button class="sw" role="switch" aria-checked="${tf(S.follow)}" data-act="ltog" data-v="follow" aria-label="Hintergrund folgt dem Spiel"></button></div>
        </section>
        <section class="card">
          <div class="cardh"><span class="cib">${ic(I.theme)}</span><div><h2>Thema</h2><p>${S.theme === 'auto' ? 'Folgt Windows · gerade ' + (dark ? 'dunkel' : 'hell') : S.theme === 'hell' ? 'Immer hell' : 'Immer dunkel'}</p></div></div>
          <div class="thg" role="radiogroup" aria-label="Thema">${[['auto', 'Automatisch', 'wie Windows'], ['hell', 'Hell', 'Helle Flächen, Bilder bleiben kräftig'], ['dunkel', 'Dunkel', 'Kino-Look']].map(t => `<button class="tho" role="radio" aria-checked="${tf(S.theme === t[0])}" data-act="theme" data-v="${t[0]}"><span class="tpv">${t[0] === 'auto' ? '<div class="tlt"></div><div class="tdk"></div>' : t[0] === 'hell' ? '<div class="tlt"></div>' : '<div class="tdk"></div>'}<span class="tbt"></span></span><b>${t[1]}</b><small>${t[2]}</small></button>`).join('')}</div>
        </section>
        <section class="card" id="lk-effekte">
          <div class="cardh"><span class="cib">${ic(I.spark)}</span><div><h2>Effekte</h2><p>Parallaxe, Ken-Burns-Drift, Licht beim HDR-Wechsel</p></div></div>
          <div class="fxg" role="radiogroup" aria-label="Effekte">${[['Voll', 'Parallaxe, Drift, Lichtstreifen, Partikel · Partikel nur 2,5 s, Leerlauf max. 30 fps'], ['Reduziert', 'Nur kurze Übergänge bis 0,3 s, keine Daueranimation'], ['Aus', 'Keine Bewegung, sofortige Wechsel']].map((f, i) => `<button class="fo" role="radio" aria-checked="${tf(S.fx === i)}" data-act="fx" data-v="${i}"><span class="demo d${i}"><i></i><em style="--l:62%;--t:60%;--w:0s"></em><em style="--l:72%;--t:40%;--w:-.8s"></em><em style="--l:55%;--t:30%;--w:-1.5s"></em></span><b>${f[0]}${osRed && i === 1 && S.fx === 0 ? ' <span class="tg2 nat">wirksam</span>' : ''}</b><small>${f[1]}</small></button>`).join('')}</div>
          <div class="lst2">${[['Beim Spielen anhalten', 'Solange ein Spiel läuft, steht jede Animation still'], ['Im Hintergrund anhalten', 'Minimiert, verdeckt oder nicht aktiv: 0 % GPU'], ['Windows-Animationseffekte beachten', osRed ? 'Windows: Animationen aus → es gilt mindestens „Reduziert“' : (S.rm || mqReduce.matches) ? 'Windows: Animationen aus – wird gerade ignoriert' : 'Ist die Option in Windows aus, gilt mindestens „Reduziert“ · Windows: Animationen ein'], ['Nach dem Start in den Infobereich', 'Fenster schließt, HDRpilot läuft im Tray weiter']].map((w, i) => `<div class="prow"><div><b>${w[0]}</b><small>${w[1]}</small></div><button class="sw" role="switch" aria-checked="${tf(S.sws[i])}" data-act="lsw" data-v="${i}" aria-label="${w[0]}"></button></div>`).join('')}</div>
          <div class="perf">${ic(I.bolt)}<span>Im Prototyp: Fenster verlassen oder Tab wechseln hält alle Daueranimationen an – genau wie die App beim Minimieren, Schließen und während ein Spiel läuft. Keine großflächige Live-Unschärfe: das Umgebungslicht ist Steams vorgefertigtes <span class="mono">library_hero_blur.jpg</span> bzw. ein gestrecktes 6×6-Farbfeld.</span></div>
        </section>
      </div>
      <div class="colc">
        <section class="card">
          <div class="cardh"><span class="cib">${ic(I.eye)}</span><div><h2>Live-Vorschau</h2><p>Echte Bilder aus deiner Bibliothek mit deinem Schema</p></div><span class="cap2">HDR</span><button class="sw" role="switch" aria-checked="${tf(hdrP)}" data-act="pvhdr" aria-label="HDR in der Vorschau"></button></div>
          <div class="stg${hdrP ? ' hdr' : ''}" style="--a1:${pp.a1};--a2:${S.follow ? pp.a2 : c2};--a3:${S.follow ? pp.a3 : c3};--lx:${(clamp(+pp.lx || .62, .3, .62) * 100).toFixed(0)}%;--ly:${Math.round(clamp(+pp.ly || .45, .3, .75) * 100)}%;--blA:${clamp((pp.hl || .55) * .85 - .05, .14, .7).toFixed(2)};--blC:.32">
            <div class="stage" style="inset:-12px"><div class="kb"><div class="slot on${pa.hero ? '' : ' gen'}">${pa.hero ? `<img class="art" src="${esc(pa.hero)}" alt=""><img class="art boost" src="${esc(pa.hero)}" alt=""><div class="veil"></div>` : genBanner(pg, pa, pp)}</div></div></div>
            <div class="bloom"></div><div class="scrim"></div><div class="sweep${hdrP ? ' k' + (S.hk % 2 ? 1 : 2) : ''}"><i></i><b></b></div>
            <div class="hcx"><div class="lgw">${pa.logo ? `<img class="logo" src="${esc(pa.logo)}" alt="${esc(pg.n)}">` : pa.hero ? `<h3 class="tt" data-t="${esc(pg.n)}">${esc(pg.n)}</h3>` : `<div class="gtl">${icoTile(pg, pa)}<h3 class="tt" data-t="${esc(pg.n)}">${esc(pg.n)}</h3></div>`}</div><div class="meta"><span class="srcp" style="--sr:${srcCol(pg.src)}"><i></i>${srcName(pg.src)}</span><span>${esc(when(pg.lp))}</span></div>
              <div class="ctaR"><span class="pw"><button class="play">${ic(I.play)}Starten</button></span><button class="gb">${ic(I.desk)}Desktop</button></div></div>
            <div class="mcap glass"><span class="hcd"></span>${hdrP ? 'HDR EIN' : 'HDR AUS'}<b>${MON.peak} nits</b></div>
          </div>
          <div class="spk">${pvs.map(g => `<button class="mt" aria-pressed="${tf(g.i === S.pvSel)}" data-act="pvsel" data-i="${g.i}" title="${esc(g.n)}">${g.img.header || g.img.hero ? `<img src="${esc(g.img.header || g.img.hero)}" alt="">` : thumbHTML(g).replace('class="th', 'class="th mtg')}</button>`).join('')}<small title="${esc(pg.n)}">${esc(pg.n)}</small></div>
        </section>
        <section class="card" id="lk-bilder">
          <div class="cardh"><span class="cib">${ic(I.img)}</span><div><h2>Bilder</h2><p>Woher Titelbild, Logo, Cover und Symbol kommen</p></div></div>
          <div class="srcs">
            <div class="so pri"><div class="soh"><span class="soi">${ic(I.disk)}</span><div><b>Lokal · Launcher-Caches <span class="tg2 nat">Hauptquelle</span></b><small>Nur lesen, kein Netzwerk. Neue Bilder erscheinen von selbst, sobald ein Launcher sie zwischenspeichert.</small></div><span class="lockt" title="Lokale Bilder lassen sich nicht abschalten">${ic(I.lock)}Immer aktiv</span></div>
              <table class="cvt"><thead><tr><th>Launcher</th><th>Titelbild</th><th>Logo</th><th>Cover</th><th>Symbol</th></tr></thead><tbody>
              ${srcKeys.map(k => { const c = cov[k]; return `<tr><td title="${esc(c.where)}"><span class="sdot" style="--sr:${srcCol(k)}"></span>${esc(c.name)} <span class="mono tot">· ${c.total}</span></td>${cell(c.used.hero, c.found.hero, c.total)}${cell(c.used.logo, c.found.logo, c.total)}${cell(c.used.cover, c.found.cover, c.total, c.smear ? c.smear + ' verschmiert' : '')}${cell(c.used.icon, c.found.icon, c.total, c.exeIcons ? c.exeIcons + ' aus EXE' : '')}</tr>`; }).join('')}
              </tbody></table>
              <div class="src-l lgd">${ic(I.info)}<span>Zahl = genutzt / Spiele, <sup>+n</sup> = gefunden, aber nicht genutzt. ${diffs.map(esc).join(' · ')}</span></div></div>
            <div class="so"><div class="soh"><span class="soi">${ic(I.gen)}</span><div><b>Erzeugte Bilder</b><small>Springen ein, wenn etwas fehlt – je Fall ein echtes Beispiel aus deiner Bibliothek</small></div></div>
              <div class="fbx">${fbs.map(([g, k]) => fbCard(g, k)).join('')}</div></div>
            <div class="so"><div class="soh"><span class="soi">${ic(I.cloud)}</span><div><b>SteamGridDB (online) <span class="tg2">optional</span></b><small>Füllt nur fehlende Bilder: ${miss.hero} Titelbilder, ${miss.logo} Logos, ${miss.cover} Cover. Lokale Bilder bleiben unangetastet.</small></div><button class="sw" role="switch" aria-checked="${tf(S.sgdb)}" data-act="ltog" data-v="sgdb" aria-label="SteamGridDB verwenden"></button></div>
              <div class="key${S.sgdb ? '' : ' off'}"><label>${ic(I.key)}<input type="password" placeholder="API-Schlüssel von steamgriddb.com" value="${esc(S.sgdbKey)}" data-act="sgkey" ${S.sgdb ? '' : 'disabled'} aria-label="SteamGridDB API-Schlüssel" autocomplete="off" spellcheck="false"></label><button class="pbtn" data-act="toast" data-v="Prototyp|Es wird keine Verbindung aufgebaut" ${S.sgdb ? '' : 'disabled'}>Prüfen</button></div>
              <div class="src-l">${ic(I.info)}<span>Standard: aus. Der Schlüssel wird in der App verschlüsselt (DPAPI) gespeichert, im Prototyp gar nicht.</span></div></div>
            <div class="src-l" style="margin-top:2px">${ic(I.img)}<span>Einzelnes Spiel: Rechtsklick › <b>Bild wählen …</b> oder <b>Symbol ändern …</b></span></div>
          </div>
        </section>
        <section class="card" id="lk-symbole">
          <div class="cardh"><span class="cib">${ic(I.tray)}</span><div><h2>Symbole und Tray</h2><p>Zeigen den Zustand in deinen Farben</p></div><span class="cap2">Taskleiste</span><div class="seg sm" role="radiogroup" aria-label="Taskleiste"><button role="radio" aria-checked="${tf(S.tb === 'hell')}" data-act="tb" data-v="hell">Hell</button><button role="radio" aria-checked="${tf(S.tb === 'dunkel')}" data-act="tb" data-v="dunkel">Dunkel</button></div></div>
          <div class="scene ${S.tb === 'hell' ? 'tbl' : 'tbd'}" style="--tbg:${S.tb === 'hell' ? '#F3F4F8' : '#1C1D24'}">
            <div>
              <div class="st4">${ist('iOff', 'HDR aus', 'wartet', cs === 'Off')}${ist('iOn', 'HDR ein', 'manuell/auto', cs === 'On')}${ist('iPause', 'Pausiert', 'Automatik', cs === 'Pause')}${ist('iRun', 'Spiel läuft', 'HDR ein', cs === 'Run')}</div>
              <div class="rsz"><span class="cap2">Echte Größe · aus / ein</span><div class="rsg">${[16, 20, 24, 32].map(z => `<span><div><svg viewBox="0 0 24 24" style="width:${z}px;height:${z}px" aria-hidden="true"><use href="#iOff${z < 24 ? 'S' : 'L'}"></use></svg><svg viewBox="0 0 24 24" style="width:${z}px;height:${z}px" aria-hidden="true"><use href="#iOn${z < 24 ? 'S' : 'L'}"></use></svg></div>${z} px</span>`).join('')}</div></div>
            </div>
            <div class="tm${S.hdr ? ' on' : ''}" role="menu" aria-label="Tray-Menü">
              <div class="tm-h"><span class="lgo"><svg viewBox="0 0 24 24" class="glyph" aria-hidden="true"><path class="lgS" d="M7 17a5 5 0 0 1 10 0z"></path><path d="M4 17h16M7 17a5 5 0 0 1 10 0M12 6.5v2.2M6.4 9.4l1.5 1.5M17.6 9.4l-1.5 1.5"></path></svg></span><div><b>HDRpilot</b><small><i></i>${esc(trayStatus())}</small></div></div>
              <div class="tm-v">Spitze ${MON.peak} · Vollbild ${MON.full} nits · ${MON.bits} Bit</div><hr>
              <button class="tm-i hl" role="menuitem" data-act="hdr">${ic(I.sun)}${S.hdr ? 'HDR jetzt aus' : 'HDR jetzt ein'}</button>
              <button class="tm-i" role="menuitem" data-act="pause">${ic(S.pause ? I.play : I.pause)}${S.pause ? 'Automatik fortsetzen' : 'Automatik pausieren'}</button>
              <button class="tm-i" role="menuitemcheckbox" aria-checked="${tf(G[S.sel].ahdr)}" data-act="tog" data-k="ahdr" data-i="${S.sel}">${ic(I.pulse)}Auto HDR · ${esc(G[S.sel].n.length > 16 ? G[S.sel].n.slice(0, 15) + '…' : G[S.sel].n)}<em>${G[S.sel].ahdr ? ic(I.check) : ''}</em></button><hr>
              <button class="tm-i" role="menuitem" data-act="page" data-v="bibliothek">${ic(I.startm)}Bibliothek öffnen</button>
              <button class="tm-i" role="menuitem">${ic(I.set)}Einstellungen</button>
              <button class="tm-i" role="menuitem">${ic(I.logs)}Protokolle</button><hr>
              <button class="tm-i" role="menuitem">${ic(I.power)}HDRpilot beenden</button>
            </div>
            <div class="tk"><div class="tk-c"><span class="tki"><span class="tapp" style="background:linear-gradient(135deg,#7AA7FF,#4F6BFF)"></span></span><span class="tki"><span class="tapp" style="background:linear-gradient(135deg,#FFD36B,#F59E0B)"></span></span><span class="tki act" title="HDRpilot"><svg class="tapp" viewBox="0 0 24 24" role="img" aria-label="HDRpilot-Fenstersymbol"><use href="#i${cs}L"></use></svg></span></div>
              <div class="tray"><span class="trs">${ic('M6 15l6-6 6 6')}</span><span class="trs hl"><span class="ring${S.hdr ? ' k' + (S.hk % 2 ? 1 : 2) : ''}"></span><svg viewBox="0 0 24 24" role="img" aria-label="HDRpilot-Tray-Symbol"><use href="#i${cs}S"></use></svg></span><span class="trs">${ic('M2 9a15 15 0 0 1 20 0M5 12.5a10 10 0 0 1 14 0M8.5 16a5 5 0 0 1 7 0M12 19.5h.01')}</span><span class="clk">${new Date().toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}<br>${new Date().toLocaleDateString('de-DE')}</span></div></div>
          </div>
          <div class="src-l" style="margin-top:10px">${ic(I.info)}<span>„HDR aus“ (der häufigste Zustand) trägt den Verlauf Akzent → Atmosphäre als Linie; „HDR ein“ die gefüllte Kachel. Die Farben werden je Taskleiste auf mindestens 3:1 Kontrast gebracht.</span></div>
        </section>
      </div>
    </div>
  </div>`;
  palette(pg).then(p => { const s = $('.stg'); if (s && S.pvSel === pg.i && p !== palOf(pg)) { const q = palUse(p); s.style.setProperty('--a1', q.a1); if (S.follow) { s.style.setProperty('--a2', q.a2); s.style.setProperty('--a3', q.a3); } } });
  // Farbfelder der Beispiele nachreichen, sobald ausgelesen
  fbs.forEach(([g]) => { if (!palOf(g) || !palOf(g).field) palette(g).then(() => { if (S.page === 'darstellung') { clearTimeout(renderLook.t); renderLook.t = setTimeout(renderLook, 60); } }); });
}
function setPage(p) {
  S.page = p === 'darstellung' ? 'darstellung' : 'bibliothek'; closeCtx(); S.menu = null; setDrawer(false);
  app.classList.remove('p-bibliothek', 'p-darstellung'); app.classList.add('p-' + S.page);
  $('#wtitle').textContent = S.page === 'darstellung' ? 'HDRpilot – Einstellungen' : 'HDRpilot – Spielebibliothek';
  document.title = $('#wtitle').textContent;
  if (S.page === 'darstellung') renderLook();
  else renderToolbar();
}

/* ========================================================================================================== */
/*  Ereignisse                                                                                                 */
/* ========================================================================================================== */
function tog(gi, k, el) {
  const g = G[gi]; g[k] = !g[k];
  if (el && el.classList.contains('sw')) { el.setAttribute('aria-checked', tf(g[k])); el.classList.add('pop'); }
  refreshRow(gi);
  if (k === 'auto') $$('#r' + gi + ' .sw, .tile[data-i="' + gi + '"] .sw').forEach(s => s.classList.add('pop'));
  if (gi === S.sel) { renderPaneSwitches(); renderHeroContentSoft(); }
  if (k === 'auto') { renderHead(); renderToolbar(); if (S.fa) refilter(); }
  if (k === 'ahdr') toast('Windows Auto HDR ' + (g[k] ? 'ein' : 'aus') + ' · ' + g.n, 'Wirkt beim nächsten Start über HDRpilot', I.pulse);
  if (S.page === 'darstellung') renderLook();
}
function renderPaneSwitches() { const g = G[S.sel]; $$('#psc .sw[data-k], #acard .sw[data-k]').forEach(s => s.setAttribute('aria-checked', tf(g[s.dataset.k]))); }
function renderHeroContentSoft() { const g = G[S.sel], c = $('#hc .chip b'); if (c) c.textContent = chipTxt(g); const b = $('#hc .bds'); if (b) b.innerHTML = badges(g, false); }

app.addEventListener('click', e => {
  const t = e.target.closest('[data-act]');
  if (!e.target.closest('#ctx')) closeCtx();
  if (S.menu && !e.target.closest('.ddw')) { S.menu = null; renderToolbar(); }
  if (!t) {
    const row = e.target.closest('.row,.tile');
    if (row) { select(+row.dataset.i); if (row.classList.contains('row')) lv && lv.focus({ preventScroll: true }); }
    if (e.target.id === 'dlgw') { S.dlg = null; renderDlg(); }
    if (e.target.id === 'dwb') setDrawer(false);
    return;
  }
  const a = t.dataset.act, v = t.dataset.v;
  switch (a) {
    case 'hdr': setHdr(!S.hdr); break;
    case 'pause': S.pause = !S.pause; renderPauseBtn(); toast(S.pause ? 'Automatik pausiert' : 'Automatik läuft wieder', S.pause ? 'HDR wird nicht automatisch geschaltet' : 'HDR schaltet beim Spielstart', S.pause ? I.pause : I.play); renderHeroContentSoft(); renderStatus(); updateIcons(); if (S.page === 'darstellung') renderLook(); break;
    case 'page': setPage(v); break;
    case 'menu': S.menu = S.menu === v ? null : v; renderToolbar(); break;
    case 'src': S.src = v; S.menu = null; refilter(); break;
    case 'sort': { const s = SORTS.find(x => x[0] === v); S.sort = { k: v, d: s[2] }; LS.set('sort', S.sort); S.menu = null; refilter(); break; }
    case 'col': { const s = SORTS.find(x => x[0] === v); S.sort = S.sort.k === v ? { k: v, d: -S.sort.d } : { k: v, d: s ? s[2] : 1 }; LS.set('sort', S.sort); refilter(); break; }
    case 'chip': S[v] = !S[v]; refilter(); break;
    case 'view': if (S.view !== v) setView(v); break;
    case 'thumb': S.thumb = v; LS.set('thumb', v); renderList(true); break;
    case 'drawer': setDrawer(!S.dw); break;
    case 'rescan': t.querySelector('.ic').animate([{ transform: 'rotate(0)' }, { transform: 'rotate(360deg)' }], { duration: fxEff() === 2 ? 0 : 900, easing: 'cubic-bezier(.4,0,.2,1)' }); toast('Bibliothek eingelesen', G.length + ' Spiele · ' + G.filter(g => g.nw).length + ' neu · 0,8 s', I.refresh); break;
    case 'add': toast('Spiel hinzufügen', 'Öffnet in der App den Dateidialog für eine EXE-Datei', I.plus); break;
    case 'reset': S.q = ''; S.src = 'all'; S.fa = S.fn = S.fh = false; refilter(); break;
    case 'tog': e.stopPropagation(); tog(+t.dataset.i, t.dataset.k, t); if (t.closest('#ctx')) { t.setAttribute('aria-checked', tf(G[+t.dataset.i][t.dataset.k])); } break;
    case 'start': closeCtx(); start(S.sel); break;
    case 'shortcut': toast(v === 'desktop' ? 'Desktop-Verknüpfung erstellt' : 'Zum Startmenü hinzugefügt', G[S.sel].n + ' · startet über HDRpilot', v === 'desktop' ? I.desk : I.startm); closeCtx(); break;
    case 'toast': { const [m, s] = (v || '').split('|'); toast(m, s, I.info); closeCtx(); break; }
    case 'hide': { const g = G[S.ctx != null ? S.ctx : S.sel]; g.hidden = true; closeCtx(); toast('Ausgeblendet · ' + g.n, 'Unter Einstellungen › Allgemein wieder einblenden', I.hide); refilter(); renderHead(); if (S.vis.length) select(S.vis[0], { scroll: true }); break; }
    case 'ctx-pane': { const r = t.getBoundingClientRect(); openCtx(S.sel, r.left - 220, r.bottom + 6); break; }
    case 'shelf': shelfGo(S.off + (+v) * Math.max(1, SH.vis - 1)); break;
    case 'dlg': openDlg(v, t.dataset.tab); break;
    case 'dtab': S.dlg.tab = v; S.dlg.pick = curPick(v); S.dlg.file = null; renderDlg(); break;
    case 'dpick': S.dlg.pick = v; if (v === 'file' && !S.dlg.file && !(blobs[gkey(G[S.dlg.gi])] || {})[S.dlg.type === 'bild' ? S.dlg.tab : 'icon']) $('#filepick').click(); renderDlg(); break;
    case 'dclose': S.dlg = null; renderDlg(); break;
    case 'dapply': applyDlg(); break;
    case 'dreset': { const g = G[S.dlg.gi]; if (S.dlg.type === 'bild') { const t2 = S.dlg.tab; S.dlg.pick = t2 === 'cover' && g.img.coverSmear ? 'gen' : g.img[t2] ? 'local' : 'gen'; } else { const k = (g.ico || []).findIndex(c => c.url && c.url === g.img.icon); S.dlg.pick = k >= 0 ? 'cand' + k : g.img.iconKind === 'exe' ? 'exe-art' : 'mono'; } S.dlg.file = null; renderDlg(); break; }
    case 'gosel': setPage('bibliothek'); S.q = ''; S.src = 'all'; S.fa = S.fn = S.fh = false; refilter(); select(+t.dataset.i, { scroll: true }); break;
    case 'lookjump': { const el = $('#' + v); el && el.scrollIntoView({ behavior: fxEff() ? 'auto' : 'smooth', block: 'start' }); break; }
    case 'n3': { const n = v === '1'; if (S.n3 !== n) { S.n3 = n; if (n && !S.c[2]) S.c[2] = mix(S.c[1], '#FFFFFF', 0.45); S.pr = -1; saveLook(); wave(e); } break; }
    case 'preset': { const p = P[+v]; S.pr = +v; S.c = p[1].slice(); S.n3 = p[2] === 3; saveLook(); wave(e); break; }
    case 'swap': S.c = [S.c[1], S.c[0], S.c[2]]; S.pr = -1; saveLook(); wave(e); break;
    case 'ltog': S[v] = !S[v]; LS.set(v, S[v]); if (v === 'follow') { showHero(S.sel, true); } saveLook(); if (v === 'sgdb') toast(S.sgdb ? 'SteamGridDB eingeschaltet' : 'SteamGridDB aus', S.sgdb ? 'Füllt nur fehlende Bilder · im Prototyp ohne Verbindung' : 'Nur lokale Bilder', I.cloud); break;
    case 'theme': S.theme = v; saveLook(); break;
    case 'fx': S.fx = +v; saveLook(); heroLeave(); toast('Effekte: ' + FXN[S.fx] + (fxEff() !== S.fx ? ' · wirksam: ' + FXN[fxEff()] : ''), ['Alle Animationen · Partikel nur 2,5 s · Leerlauf max. 30 fps', 'Nur kurze Übergänge, keine Daueranimation', 'Keine Animationen, sofortige Wechsel'][fxEff()], I.bolt); break;
    case 'lsw': S.sws[+v] = S.sws[+v] ? 0 : 1; LS.set('sws', S.sws); setZz(false, '_'); applyLook(); renderLook(); break;
    case 'pvhdr': S.pvHdr = !S.pvHdr; S.hk++; renderLook(); break;
    case 'pvsel': S.pvSel = +t.dataset.i; renderLook(); break;
    case 'tb': S.tb = v; LS.set('tb', v); renderLook(); break;
    case 'lreset': S.c = P[0][1].slice(); S.n3 = false; S.pr = 0; S.theme = 'auto'; S.fx = 0; S.autoTxt = true; S.follow = true; S.sws = [1, 1, 1, 0]; LS.set('sws', S.sws); saveLook(); wave(e); break;
    case 'min': toast('Im Prototyp ohne Funktion', 'In der App: Fenster minimieren → alle Effekte stoppen', I.info); break;
  }
});
function saveLook() {
  LS.set('colors', S.c); LS.set('n3', S.n3); LS.set('preset', S.pr); LS.set('theme', S.theme); LS.set('fx', S.fx); LS.set('autoTxt', S.autoTxt); LS.set('follow', S.follow);
  applyLook(); if (S.page === 'darstellung') renderLook();
  renderStatus();
}
app.addEventListener('input', e => {
  const t = e.target;
  if (t.id === 'q') { S.q = t.value; const pos = t.selectionStart; refilter(); const q = $('#q'); q.focus(); q.setSelectionRange(pos, pos); }
  if (t.dataset.act === 'well') { const i = +t.dataset.v; S.c[i] = t.value.toUpperCase(); S.pr = -1; LS.set('colors', S.c); LS.set('preset', -1); applyLook(); const sw = t.closest('.wl'); sw.querySelector('.swt').style.setProperty('--c', S.c[i]); sw.querySelector('small').textContent = S.c[i]; }
  if (t.dataset.act === 'sgkey') S.sgdbKey = t.value;
});
app.addEventListener('change', e => { if (e.target.dataset.act === 'well') saveLook(); });
$('#filepick').addEventListener('change', e => {
  const f = e.target.files && e.target.files[0]; if (!f || !S.dlg) return;
  S.dlg.file = URL.createObjectURL(f); S.dlg.pick = 'file'; renderDlg(); e.target.value = '';
});
app.addEventListener('contextmenu', e => {
  const r = e.target.closest('.row,.tile,#hero,.pane,#acard');
  if (!r || e.target.closest('input')) return;
  e.preventDefault();
  openCtx(r.dataset.i != null ? +r.dataset.i : S.sel, e.clientX, e.clientY);
});
app.addEventListener('dblclick', e => { const r = e.target.closest('.row,.tile'); if (r && !e.target.closest('.sw')) { select(+r.dataset.i); start(+r.dataset.i); } });
$('#hero').addEventListener('mousemove', heroMove);
$('#hero').addEventListener('mouseleave', heroLeave);
['#shf', '#grd'].forEach(s => $(s).addEventListener('mousemove', tileShine));
$('#shf').addEventListener('wheel', e => { if (S.view !== 'regal') return; e.preventDefault(); const now = performance.now(); if (now - (S.wt || 0) < 90) return; S.wt = now; shelfGo(S.off + Math.sign(e.deltaY || e.deltaX)); }, { passive: false });

document.addEventListener('keydown', e => {
  const tag = (e.target.tagName || '').toLowerCase(), typing = tag === 'input' && e.target.type !== 'checkbox';
  if (e.key === 'Escape') { if (S.dlg) { S.dlg = null; renderDlg(); } else if (!$('#ctx').hidden) closeCtx(); else if (S.menu) { S.menu = null; renderToolbar(); } else if (S.dw) setDrawer(false); else if (typing) e.target.blur(); return; }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'f') { e.preventDefault(); if (S.page !== 'bibliothek') setPage('bibliothek'); $('#q').focus(); $('#q').select(); return; }
  if (e.ctrlKey && ['1', '2', '3'].includes(e.key)) { e.preventDefault(); setView(['liste', 'regal', 'raster'][+e.key - 1]); return; }
  if (S.dlg || S.page !== 'bibliothek') return;
  if (!$('#ctx').hidden) {
    const items = $$('#ctx .mi'), i = items.indexOf(document.activeElement);
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); const n = items[(i + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length]; n.focus(); }
    return;
  }
  if (typing && !['ArrowDown', 'ArrowUp', 'Enter'].includes(e.key)) return;
  if (!typing && (e.key === 'i' || e.key === 'I') && S.view === 'regal') { e.preventDefault(); setDrawer(!S.dw); return; }
  const v = S.vis, k = v.indexOf(S.sel); let n = -1;
  const step = S.view === 'raster' ? { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -GR.cols, ArrowDown: GR.cols } : S.view === 'regal' ? { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -1, ArrowDown: 1 } : { ArrowUp: -1, ArrowDown: 1 };
  if (step[e.key] != null) n = clamp(k + step[e.key], 0, v.length - 1);
  else if (e.key === 'PageDown') n = clamp(k + 10, 0, v.length - 1); else if (e.key === 'PageUp') n = clamp(k - 10, 0, v.length - 1);
  else if (e.key === 'Home' && !typing) n = 0; else if (e.key === 'End' && !typing) n = v.length - 1;
  else if (e.key === 'Enter') { if (typing) { e.target.blur(); return; } if (tag === 'button') return; e.preventDefault(); start(S.sel); return; }
  else if (e.key === ' ' && !typing && tag !== 'button') { e.preventDefault(); const sw = $('#r' + S.sel + ' .sw') || $('.tile[data-i="' + S.sel + '"] .sw'); tog(S.sel, 'auto', sw); return; }
  else if ((e.key === 'F10' && e.shiftKey) || e.key === 'ContextMenu') { e.preventDefault(); const el = $('#r' + S.sel) || $('.tile[data-i="' + S.sel + '"]') || $('#hero'); const r = el.getBoundingClientRect(); openCtx(S.sel, r.left + 120, r.top + Math.min(40, r.height / 2)); return; }
  if (n >= 0 && v[n] != null) {
    e.preventDefault();
    const now = performance.now(), fast = e.repeat || now - S.lastKey < 160; S.lastKey = now;
    select(v[n], { scroll: true, defer: fast });
  }
});
document.addEventListener('visibilitychange', () => setZz(document.hidden, 'hidden'));
window.addEventListener('blur', () => setZz(true, 'blur'));
window.addEventListener('focus', () => setZz(false, 'blur'));
window.addEventListener('scroll', closeCtx, true);
let rzT = 0, lastSz = { shf: '', grd: '' };
const relayout = () => { clearTimeout(rzT); rzT = setTimeout(() => {
  heroSize(); fitToolbar();
  const s = $('#shf'), g = $('#grd'), ks = s.clientWidth + 'x' + s.clientHeight, kg = g.clientWidth + 'x' + g.clientHeight;
  if (S.view === 'regal' && ks !== lastSz.shf) { lastSz.shf = ks; renderShelf(); ensureTileVisible(); }
  if (S.view === 'raster' && kg !== lastSz.grd) { lastSz.grd = kg; const st = g.scrollTop; renderGrid(); g.scrollTop = st; fillGrid(); }
  updateRows(); }, 90); };
window.addEventListener('resize', relayout);
if (window.ResizeObserver) { const ro = new ResizeObserver(relayout); ro.observe($('#shf')); ro.observe($('#grd')); ro.observe($('#toolbar')); }
if (window.matchMedia) matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { if (S.theme === 'auto') saveLook(); });
mqReduce.addEventListener('change', () => { applyLook(); if (S.page === 'darstellung') renderLook(); renderStatus(); });

/* ========================================================================================================== */
/*  Zustände über die Adresse (#view=regal&hdr=on&theme=hell&page=darstellung …) – für Bildschirmfotos          */
/* ========================================================================================================== */
function findGame(s) {
  if (s == null || s === '') return -1;
  const n = G.findIndex(g => g.sid === s); if (n >= 0) return n;
  if (/^\d+$/.test(s) && G[+s]) return +s;
  const q = s.toLowerCase(); return G.findIndex(g => g.nl.indexOf(q) >= 0);
}
function readHash() { const o = {}; location.hash.replace(/^#/, '').split('&').filter(Boolean).forEach(kv => { const i = kv.indexOf('='); o[decodeURIComponent(i < 0 ? kv : kv.slice(0, i))] = i < 0 ? '1' : decodeURIComponent(kv.slice(i + 1)); }); return o; }
function applyHash(first) {
  const h = readHash();
  S.shot = !!h.shot;
  // headless Chrome rechnet mit kleinerem Viewport als das spätere Foto → feste Fenstergröße für stabile Bilder
  const m = /^(\d{3,4})x(\d{3,4})$/.exec(h.shot || ''); if (m) { app.style.right = 'auto'; app.style.bottom = 'auto'; app.style.width = m[1] + 'px'; app.style.height = m[2] + 'px'; }
  const TH = { light: 'hell', hell: 'hell', dark: 'dunkel', dunkel: 'dunkel', auto: 'auto' }, FX = { voll: 0, reduziert: 1, aus: 2, 0: 0, 1: 1, 2: 2 };
  if (h.theme && TH[h.theme]) S.theme = TH[h.theme];
  if (h.fx != null && FX[h.fx] != null) S.fx = FX[h.fx];
  if (h.rm != null) S.rm = h.rm !== '0';
  if (h.preset != null) { const i = /^\d+$/.test(h.preset) ? +h.preset : P.findIndex(p => p[0].toLowerCase() === h.preset.toLowerCase()); if (P[i]) { S.pr = i; S.c = P[i][1].slice(); S.n3 = P[i][2] === 3; } }
  if (h.mode) S.n3 = h.mode === 'tri';
  ['c1', 'c2', 'c3'].forEach((k, i) => { if (/^[0-9a-f]{6}$/i.test(h[k] || '')) { S.c[i] = '#' + h[k].toUpperCase(); S.pr = -1; } });
  if (h.follow != null) S.follow = h.follow !== '0';
  if (h.thumb) S.thumb = h.thumb === 'symbol' ? 'symbol' : 'bild';
  if (h.sgdb != null) S.sgdb = h.sgdb !== '0';
  if (h.tb) S.tb = h.tb === 'hell' || h.tb === 'light' ? 'hell' : 'dunkel';
  if (h.q != null) S.q = h.q;
  if (h.src) S.src = Object.keys(SRC).find(k => k.toLowerCase() === h.src.toLowerCase()) || 'all';
  if (h.filter) h.filter.split(',').forEach(f => { if (f === 'auto') S.fa = true; if (f === 'neu') S.fn = true; if (f === 'hdr') S.fh = true; });
  if (h.sort) { const s = SORTS.find(x => x[0] === h.sort); if (s) S.sort = { k: s[0], d: h.dir === 'asc' ? 1 : h.dir === 'desc' ? -1 : s[2] }; }
  if (h.ahdr) h.ahdr.split(',').forEach(s => { const i = findGame(s); if (i >= 0) G[i].ahdr = true; });
  if (h.gen) h.gen.split(',').forEach(s => { const i = findGame(s); if (i >= 0) blobs[gkey(G[i])] = {}, S.ovr[gkey(G[i])] = { hero: 'gen', logo: 'gen', cover: 'gen' }; });
  const si = findGame(h.sel); if (si >= 0) S.sel = si;
  if (h.zz != null) S.forceZz = h.zz !== '0';
  if (h.pause != null) S.pause = h.pause !== '0';
  S.lcol = h.lcol === '1';
  applyLook(); renderHead(); renderPauseBtn();
  if (['liste', 'regal', 'raster'].includes(h.view)) S.view = h.view;
  setView(S.view, true);
  if (S.vis.length && S.vis.indexOf(S.sel) < 0) S.sel = S.vis[0];   // Filter aus der Adresse: Auswahl wie bei refilter()
  select(S.sel, { force: true, instant: true, scroll: true });
  setPage(h.page === 'darstellung' || h.page === 'look' ? 'darstellung' : 'bibliothek');
  if (h.pvhdr) S.pvHdr = true;
  if (S.page === 'darstellung') { renderLook(); if (h.look) setTimeout(() => { const el = $('#' + h.look); el && el.scrollIntoView({ block: 'start' }); }, 50); }
  setZz(S.forceZz, 'force');
  if (h.dw && S.view === 'regal') setTimeout(() => setDrawer(true), 200);
  if (h.hdr === 'on' || h.hdr === '1') setTimeout(() => setHdr(true, 'silent'), +h.hdrdelay || 300);
  if (h.run) setTimeout(() => start(S.sel), 400);
  if (h.ctx) setTimeout(() => { const el = $('#r' + S.sel) || $('.tile[data-i="' + S.sel + '"]') || $('#hero'); const r = el.getBoundingClientRect(); openCtx(S.sel, r.left + Math.min(260, r.width * 0.5), r.top + Math.min(30, r.height / 2)); }, 900);
  if (h.dlg) setTimeout(() => openDlg(h.dlg === 'symbol' ? 'symbol' : 'bild', h.tab || 'hero'), 900);
  if (h.menu) setTimeout(() => { S.menu = h.menu; renderToolbar(); }, 300);
  if (h.toast) setTimeout(() => toast(h.toast, 'Hinweis unten mittig, über der Statuszeile', I.sun), 600);
  if (h.freeze) setTimeout(() => freezeSweep(h.freeze), (+h.hdrdelay || 300) + 50);
  if (h.selftest) setTimeout(selftest, 1500);
  if (h.shot && !S.shotT) S.shotT = setInterval(() => settle(h.freeze ? /^(sw|sk|fl|eg|bs)\d$/ : null), 250);
}
/* Bildschirmfoto-Modus (#shot=1): headless Chrome mit virtueller Zeit startet CSS-Animationen nicht von selbst
   (sie bleiben „pending“). settle() spult Übergänge ans Ende und hält Daueranimationen an einer festen Stelle an. */
function settle(skip) {
  document.getAnimations().forEach(a => {
    if (a.playState === 'paused' || a.playState === 'finished') return;
    if (skip && skip.test(a.animationName || '')) return;
    const t = a.effect && a.effect.getTiming ? a.effect.getTiming() : null;
    try {
      if (t && t.iterations === Infinity) { a.currentTime = 5200 + (parseFloat(t.delay) || 0) * -1; a.pause(); }
      else if (/^to\d$/.test(a.animationName || '')) { a.currentTime = 1200; a.pause(); }   // Hinweis sichtbar halten
      else a.finish();
    } catch (e) { /* ignorieren */ }
  });
}
/* Bildschirmfoto mitten im HDR-Moment: Animationen an einer Stelle anhalten */
function freezeSweep(at) {
  const t = clamp(parseFloat(at) || 0.5, 0, 1);
  document.getAnimations().forEach(a => { const n = a.animationName || ''; if (/^(sw|sk|fl|eg)\d$/.test(n)) { a.pause(); a.currentTime = (a.effect.getTiming().duration || 1000) * t; } });
  if (pfx.raf) { cancelAnimationFrame(pfx.raf); pfx.raf = 0; }
  // Partikel für das Standbild einmal vorrechnen
  const t0 = performance.now(); pfx.sweepT = t0 - t * 1500; pfx.until = pfx.sweepT + 2500;
  for (let i = 0; i < 26; i++) { pfx.last = t0 - 33 - (26 - i) * 33; tick(t0 - (26 - i) * 33); if (pfx.raf) { cancelAnimationFrame(pfx.raf); pfx.raf = 0; } }
}
window.addEventListener('hashchange', () => applyHash(false));
/* Selbsttest (#selftest=1): klickt die wichtigsten Wege durch und schreibt Fehler in <pre id="selftest"> */
const ERR = [];
window.addEventListener('error', e => ERR.push('error: ' + e.message + ' @' + (e.lineno || '?')));
window.addEventListener('unhandledrejection', e => ERR.push('promise: ' + (e.reason && e.reason.message || e.reason)));
function selftest() {
  const log = [], click = sel => { const el = typeof sel === 'string' ? $(sel) : sel; if (!el) { log.push('fehlt: ' + sel); return; } el.dispatchEvent(new MouseEvent('click', { bubbles: true })); };
  const key = (k, o = {}) => document.dispatchEvent(new KeyboardEvent('keydown', Object.assign({ key: k, bubbles: true }, o)));
  const step = (name, fn) => { try { fn(); log.push('ok  ' + name); } catch (e) { log.push('ERR ' + name + ': ' + e.message); } };
  step('Liste: Pfeil runter ×3', () => { setView('liste'); key('ArrowDown'); key('ArrowDown'); key('ArrowDown'); if (S.sel !== S.vis[3]) throw new Error('Auswahl ' + S.sel); });
  step('Leertaste schaltet HDR automatisch', () => { const g = G[S.sel], v = g.auto; key(' '); if (g.auto === v) throw new Error('unverändert'); key(' '); });
  step('Spalte Name sortieren', () => { click('.lh [data-v="name"]'); if (S.sort.k !== 'name') throw new Error(S.sort.k); click('.lh [data-v="name"]'); if (S.sort.d !== -1) throw new Error('Richtung'); click('.lh [data-v="lp"]'); });
  step('Filter Neu', () => { S.fn = false; S.fn = true; refilter(); if (S.vis.length !== G.filter(g => g.nw).length) throw new Error(S.vis.length); S.fn = false; refilter(); });
  step('Filter HDR-fähig', () => { S.fh = true; refilter(); if (S.vis.length !== G.filter(hdrCap).length) throw new Error(S.vis.length); S.fh = false; refilter(); });
  step('Suche ohne Treffer', () => { const q = $('#q'); q.value = 'zzzz'; q.dispatchEvent(new Event('input', { bubbles: true })); if (S.vis.length) throw new Error(S.vis.length); if (!$('.emp')) throw new Error('kein Leerzustand'); click('[data-act="reset"]'); });
  step('Quelle GOG wählt GOG-Spiel', () => { click('[data-act="menu"][data-v="src"]'); click('[data-act="src"][data-v="Gog"]'); if (S.vis.length !== 5) throw new Error(S.vis.length); if (G[S.sel].src !== 'Gog') throw new Error('Auswahl ' + G[S.sel].n); S.src = 'all'; refilter(); });
  step('Monogramm Baldur\'s Gate 3 = BG', () => { if (mono("Baldur's Gate 3") !== 'BG') throw new Error(mono("Baldur's Gate 3")); });
  step('HDR-Art nie „SDR“', () => { if (G.some(g => /SDR/.test(kindOf(g)))) throw new Error('SDR'); });
  step('Ansicht Thumbnail ↔ Symbol', () => { click('[data-act="thumb"][data-v="symbol"]'); click('[data-act="thumb"][data-v="bild"]'); });
  step('Kontextmenü + Bild wählen', () => { openCtx(S.sel, 300, 300); click('#ctx [data-act="dlg"][data-v="bild"]'); if (!S.dlg) throw new Error('kein Dialog'); click('[data-act="dtab"][data-v="logo"]'); click('[data-act="dpick"][data-v="gen"]'); click('[data-act="dapply"]'); if ((S.ovr[gkey(G[S.sel])] || {}).logo !== 'gen') throw new Error('nicht übernommen'); openDlg('bild', 'logo'); click('[data-act="dreset"]'); click('[data-act="dapply"]'); });
  step('Symbol ändern → Monogramm', () => { openDlg('symbol'); click('[data-act="dpick"][data-v="mono"]'); click('[data-act="dapply"]'); if ((S.ovr[gkey(G[S.sel])] || {}).icon !== 'mono') throw new Error('nicht übernommen'); openDlg('symbol'); click('[data-act="dreset"]'); click('[data-act="dapply"]'); });
  step('HDR ein/aus', () => { click('#mon .hb'); if (!S.hdr) throw new Error('aus'); click('#mon .hb'); if (S.hdr) throw new Error('ein'); });
  step('Automatik pausieren', () => { click('#pauseBtn'); if (!S.pause) throw new Error('nicht pausiert'); click('#pauseBtn'); });
  step('Regal + Pfeil rechts + Details', () => { setView('regal'); renderShelf(); const k = S.vis.indexOf(S.sel); key('ArrowRight'); if (S.vis.indexOf(S.sel) !== k + 1) throw new Error('kein Schritt'); click('#acard .dtl'); if (!S.dw) throw new Error('Schublade zu'); key('Escape'); if (S.dw) throw new Error('Schublade offen'); click('.nv.r'); });
  step('Raster + Pfeil runter', () => { setView('raster'); renderGrid(); const k = S.vis.indexOf(S.sel); key('ArrowDown'); if (S.vis.indexOf(S.sel) !== Math.min(S.vis.length - 1, k + GR.cols)) throw new Error('kein Zeilensprung'); });
  step('Starten (Simulation)', () => { setView('liste'); start(S.sel); if (S.run !== S.sel) throw new Error('läuft nicht'); start(S.sel); if (S.run !== -1) throw new Error('nicht beendet'); });
  step('Darstellung: Klicks behalten die Scroll-Position', () => { setPage('darstellung'); const m = $('#lookMain'); m.scrollTop = 900; const y = m.scrollTop; click('[data-act="tb"][data-v="hell"]'); if ($('#lookMain').scrollTop !== y) throw new Error($('#lookMain').scrollTop + ' statt ' + y); click('[data-act="tb"][data-v="dunkel"]'); click('[data-act="lsw"][data-v="3"]'); click('[data-act="lsw"][data-v="3"]'); if ($('#lookMain').scrollTop !== y) throw new Error('lsw'); });
  step('Darstellung: Vorlage, Tritonus, Thema, Effekte', () => { click('[data-act="preset"][data-v="1"]'); if (S.c[0] !== P[1][1][0]) throw new Error('Vorlage'); click('[data-act="n3"][data-v="0"]'); click('[data-act="theme"][data-v="hell"]'); click('[data-act="fx"][data-v="1"]'); click('[data-act="fx"][data-v="0"]'); click('[data-act="theme"][data-v="auto"]'); click('[data-act="preset"][data-v="0"]'); click('[data-act="ltog"][data-v="sgdb"]'); click('[data-act="ltog"][data-v="sgdb"]'); click('[data-act="pvhdr"]'); setPage('bibliothek'); });
  step('Windows-Animationen aus → Reduziert', () => { S.rm = true; applyLook(); if (!app.classList.contains('fx-1')) throw new Error(app.className); S.sws[2] = 0; applyLook(); if (!app.classList.contains('fx-0')) throw new Error('Schalter aus'); S.sws[2] = 1; S.rm = false; applyLook(); });
  step('Ausblenden', () => { const n = S.vis.length, gi = S.sel; openCtx(gi, 200, 200); click('#ctx [data-act="hide"]'); if (S.vis.length !== n - 1) throw new Error(S.vis.length); G[gi].hidden = false; refilter(); renderHead(); });
  const pre = document.createElement('pre'); pre.id = 'selftest'; pre.textContent = log.concat(ERR.length ? ERR : ['keine Laufzeitfehler']).join('\n'); document.body.appendChild(pre);
}

/* ========================================================================================================== */
/*  Start                                                                                                      */
/* ========================================================================================================== */
fetch('data.json', { cache: 'no-store' }).then(r => r.json()).then(d => {
  D = d; MON = Object.assign(MON, d.monitor || {});
  G = d.games.map(g => Object.assign(g, { nl: g.n.toLowerCase(), lpt: g.lp ? Date.parse(g.lp) : 0, ahdr: false, hidden: false }));
  const sorted = G.slice().sort((a, b) => b.lpt - a.lpt); S.sel = sorted[0] ? sorted[0].i : 0;
  renderBokeh();
  // Farben und Farbfelder für erzeugte Bilder (Spiele ohne Titelbild/Cover) vorab lesen – ~25 kleine Bilder
  G.filter(g => !g.img.hero || !g.img.cover || g.img.coverSmear).forEach(g => palette(g));
  applyHash(true);
  document.documentElement.dataset.ready = '1';
}).catch(err => { document.body.innerHTML = '<pre style="color:#fff;padding:40px;font:14px monospace">data.json konnte nicht geladen werden. Bitte über server.ps1 öffnen (http://127.0.0.1:8765/).\n\n' + esc(err) + '</pre>'; });
})();
