/*
  leiste.js – Vorschau der Werkzeugleiste „Fließleiste mit Lichtnaht“ (nur Entwurf, die App bleibt unverändert).
  Grundlage: werkzeugleiste-vorschlaege.md („Bewertung und Empfehlung“) und die Entscheidungen des Nutzers vom 2026-10-17:
    1. Ansicht und „Spiel hinzufügen“ nur als Symbol (mit Tooltip), wie die Filter.
    2. Zähler-Plakette am Filterknopf, innen oben rechts (3 px Abstand), bleibt in 40×40; „Neu“ in der Neu-Farbe.
    3. Strg+F bei eingeklappter Leiste klappt sie dauerhaft aus und setzt den Fokus in die Suche.
  Rechnet in DIP bei der gewählten Fensterbreite; die Bühne wird nur zum Einpassen skaliert.
  Bewegung: eigene Zeitleiste (Kanäle) mit einstellbarer Uhr, damit sich Zwischenbilder per #t=<ms> einfrieren lassen.
  Debug: Hash-Parameter (siehe HASH unten) und window.leiste (u. a. running(), lastEnd() für die laufenden Uhren).
  Nach der Prüfung (2026-10-17): Bewegung beim Ziehen nur bei Zeilenwechsel und Sortier-Schritt; Zeilen-Morph mit Achsen nacheinander;
  Leiste in einer Klammer, die an der Oberkante der Liste endet; Gruppenabstand ≥ 24 (Schwellen +16); Einfalten ≤ 740 ms;
  Lasche −15…+3, Inhalt des Titelbereichs 22 über der Kante; Naht hängt an der Kapsel und spart den Griff aus.
*/
'use strict';
(() => {
const $ = (s, r = document) => r.querySelector(s);
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const fmt = n => String(n);

/* ================= Kurven (wie WPF) ================= */
function bezier(x1, y1, x2, y2) {
  const cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx, cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
  const sx = t => ((ax * t + bx) * t + cx) * t, sy = t => ((ay * t + by) * t + cy) * t, dx = t => (3 * ax * t + 2 * bx) * t + cx;
  return x => {
    if (x <= 0) return 0; if (x >= 1) return 1;
    let t = x;
    for (let i = 0; i < 10; i++) { const e = sx(t) - x, d = dx(t); if (Math.abs(e) < 1e-6 || Math.abs(d) < 1e-6) break; t -= e / d; }
    return sy(clamp(t, 0, 1));
  };
}
const E = {
  lin: t => t,
  cubicOut: t => 1 - Math.pow(1 - t, 3),
  cubicIn: t => t * t * t,
  expoOut6: t => 1 - (Math.exp(6 * (1 - t)) - 1) / (Math.exp(6) - 1),          // ExponentialEase Out, Exponent 6
  sineInOut: t => (1 - Math.cos(Math.PI * t)) / 2,
  spring: t => { const x = 1 - t; return 1 - (x * x * x - x * 0.35 * Math.sin(Math.PI * x)); },   // Hp.Ease.Spring: BackEase Out, Amplitude 0,35
  spline: bezier(0.16, 0.84, 0.24, 1),                                          // KeySpline der Naht
};

/* ================= Uhr und Kanäle (SnapshotAndReplace) ================= */
const Clock = {
  frozen: null, speed: 1, vBase: 0, rBase: performance.now(),
  now() { return this.frozen != null ? this.frozen : this.vBase + (performance.now() - this.rBase) * this.speed; },
  setSpeed(s) { const n = this.now(); this.vBase = n; this.rBase = performance.now(); this.speed = s; document.documentElement.style.setProperty('--spd', s); },
};
const CH = Object.create(null);
const AN = new Map();
let raf = 0;
const val = (k, d = 0) => (k in CH ? CH[k] : d);
function set(k, v) { AN.delete(k); CH[k] = v; }
/* anim(kanal, ziel, {delay, dur, ease, from, kf:[[0..1, wert], …]}): startet beim aktuellen Wert (Unterbrechen läuft weiter) */
function anim(k, to, o = {}) {
  if (o.from != null) CH[k] = o.from;
  const from = val(k, o.def != null ? o.def : to);
  let kf = null;
  if (o.kf) { kf = o.kf.map(p => p.slice()); if (kf[0][1] == null) kf[0][1] = from; }
  AN.set(k, { from, to, t0: Clock.now() + (o.delay || 0), dur: Math.max(1, o.dur || 1), ease: o.ease || E.cubicOut, kf });
  if (!(k in CH)) CH[k] = from;
  kick();
}
function kfAt(kf, p) {
  for (let i = 1; i < kf.length; i++) {
    if (p <= kf[i][0]) { const a = kf[i - 1], b = kf[i], u = (p - a[0]) / Math.max(1e-6, b[0] - a[0]); return a[1] + (b[1] - a[1]) * u; }
  }
  return kf[kf.length - 1][1];
}
function step() {
  const now = Clock.now();
  for (const [k, a] of AN) {
    const t = (now - a.t0) / a.dur;
    if (t < 0) { CH[k] = a.kf ? a.kf[0][1] : a.from; continue; }
    if (t >= 1) { CH[k] = a.kf ? a.kf[a.kf.length - 1][1] : a.to; AN.delete(k); continue; }
    const p = a.ease(t);
    CH[k] = a.kf ? kfAt(a.kf, p) : a.from + (a.to - a.from) * p;
  }
}
/* Bildschleife: requestAnimationFrame; in Bildschirmfotos (headless, virtuelle Zeit) läuft rAF nicht, dort ein 16-ms-Takt */
const frame = cb => (S.shot ? setTimeout(cb, 16) : requestAnimationFrame(cb));
function tick() { raf = 0; step(); apply(); if (AN.size && Clock.frozen == null) raf = frame(tick); }
function kick() { if (!raf && Clock.frozen == null) raf = frame(tick); }

/* ================= Zustand ================= */
const S = {
  W: 1745, H: 982, theme: 'dunkel', fx: 'voll', view: 'liste', fit: true, speed: 1,
  collapsed: false, q: '', src: 'Alle', sort: 'lp', fAuto: false, fNew: false, fHdr: false,
  sel: null, dragging: false, hy: { two: false, sortIcon: false, key: true }, shot: false, game: 'Gears of War: E-Day',
};
let G = null;          // aktuelle Geometrie
let TL = null;         // aktuelle Anordnung der Leiste
let D = { games: [], total: 0 };
const IDS = ['search', 'source', 'sort', 'fAuto', 'fNew', 'fHdr', 'view', 'rescan', 'seen', 'add'];
const SRC = { Steam: ['Steam', '#66C0F4'], Epic: ['Epic Games', '#D7DAE3'], Gog: ['GOG', '#B45CFF'], Ubisoft: ['Ubisoft Connect', '#4F8BFF'], EA: ['EA app', '#FF5A5A'], Xbox: ['Xbox', '#3FB950'], BattleNet: ['Battle.net', '#27B8FF'], Manual: ['Manuell', '#C8CCD8'] };
const SORTS = { lp: ['Zuletzt gespielt', ''], name: ['Name', ''], hdr: ['HDR-Unterstützung', ''] };
const HDRL = { Native: ['Nativ', 'nat', 'NATIVES HDR'], Limited: ['Teilweise', 'lim', 'HDR TEILWEISE'], ModOnly: ['Mod', 'mod', 'HDR PER MOD'], None: ['Nein', 'non', 'KEIN HDR'], Unknown: ['—', 'unk', 'HDR UNBEKANNT'] };

/* ================= Elemente ================= */
const win = $('#win'), tb = $('#tb'), hero = $('#hero'), art = $('#art'), hc = $('#hc'), pane = $('#pane'), grip = $('#grip');
const tabw = $('#tabw'), pill = $('#pill'), cpc = $('#cpc'), cx = $('#cx'), seam = $('#seam'), streak = $('#streak');
const lclip = $('#lclip'), lcbg = $('#lcbg'), lcin = $('#lcin'), gridEl = $('#grid'), gin = $('#gin'), tip = $('#tip'), menu = $('#menu');
const stage = $('#stage'), stageBox = $('#stageBox'), sizer = $('#sizer'), tbclip = $('#tbclip'), seamw = $('#seamw');
const TE = {};

/* ================= Daten ================= */
const MON = ['Jan.', 'Feb.', 'März', 'Apr.', 'Mai', 'Juni', 'Juli', 'Aug.', 'Sep.', 'Okt.', 'Nov.', 'Dez.'];
function when(iso) {
  if (!iso) return 'nie gespielt';
  const d = new Date(iso), now = new Date(), t0 = new Date(now.getFullYear(), now.getMonth(), now.getDate()), dd = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const days = Math.round((t0 - dd) / 864e5), hm = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
  if (days <= 0) return 'heute, ' + hm;
  if (days === 1) return 'gestern, ' + hm;
  if (days < 7) return 'vor ' + days + ' Tagen';
  if (days < 35) { const w = Math.round(days / 7); return 'vor ' + w + (w === 1 ? ' Woche' : ' Wochen'); }
  return d.getDate() + '. ' + MON[d.getMonth()] + ' ' + d.getFullYear();
}
const isHdr = g => g.hdr === 'Native' || g.hdr === 'Limited';
async function loadData() {
  try {
    const r = await fetch('data.json', { cache: 'no-store' });
    if (!r.ok) throw new Error(r.status);
    const j = await r.json();
    D.games = j.games.map(g => ({ n: g.n, src: g.src, lp: g.lp, fs: g.fs, hdr: g.hdr || 'Unknown', nw: !!g.nw, auto: !!g.auto, sid: g.sid, cmd: g.cmd || '', img: g.img || {} }));
    D.missing = false;
  } catch (e) {
    D.missing = true;
    D.games = ['Gears of War: E-Day', 'HELLDIVERS™ 2', 'TUNIC', 'Sekiro™: Shadows Die Twice', 'Aliens: Dark Descent', 'The Plucky Squire', 'Fracture Point', 'The Isle']
      .map((n, i) => ({ n, src: 'Steam', lp: new Date(Date.now() - (i + 1) * 864e5).toISOString(), hdr: i % 3 ? 'Unknown' : 'Native', nw: true, auto: false, img: {} }));
  }
  D.total = D.games.length;
  D.cAuto = D.games.filter(g => g.auto).length;
  D.cNew = D.games.filter(g => g.nw).length;
  D.cHdr = D.games.filter(isHdr).length;
  D.bySrc = {};
  for (const g of D.games) D.bySrc[g.src] = (D.bySrc[g.src] || 0) + 1;
}
function sorted(list) {
  const a = list.slice();
  if (S.sort === 'name') a.sort((x, y) => x.n.localeCompare(y.n, 'de'));
  else if (S.sort === 'hdr') { const o = { Native: 0, Limited: 1, ModOnly: 2, None: 3, Unknown: 4 }; a.sort((x, y) => (o[x.hdr] - o[y.hdr]) || x.n.localeCompare(y.n, 'de')); }
  else a.sort((x, y) => (y.lp || '').localeCompare(x.lp || '') || x.n.localeCompare(y.n, 'de'));
  return a;
}
function filtered() {
  const q = S.q.trim().toLowerCase();
  return sorted(D.games.filter(g => (S.src === 'Alle' || g.src === S.src) && (!q || g.n.toLowerCase().includes(q)) && (!S.fAuto || g.auto) && (!S.fNew || g.nw) && (!S.fHdr || isHdr(g))));
}
const isFiltered = () => !!(S.q.trim() || S.src !== 'Alle' || S.fAuto || S.fNew || S.fHdr);

/* ================= Farben aus dem Titelbild ================= */
function mixC(a, b, t) { return a.map((v, i) => Math.round(v + (b[i] - v) * t)); }
const hex = c => '#' + c.map(v => clamp(v, 0, 255).toString(16).padStart(2, '0')).join('');
function boost(c, s = 1.6, l = 1) { const m = (c[0] + c[1] + c[2]) / 3; return c.map(v => clamp(Math.round((m + (v - m) * s) * l), 0, 255)); }
function palette() {
  const P = D.pal || { avg: [70, 30, 30], left: [90, 35, 30], right: [60, 25, 35] };
  const st = win.style;
  if (S.theme === 'dunkel') {
    const bg = mixC(boost(P.avg, 1.5), [9, 7, 10], 0.83);
    st.setProperty('--bg', hex(bg)); st.setProperty('--bg2', hex(mixC(bg, boost(P.left, 1.6), 0.1))); st.setProperty('--bg3', hex(mixC(bg, [4, 4, 7], 0.35)));
  } else {
    const bg = mixC(boost(P.avg, 1.4), [246, 238, 243], 0.88);
    st.setProperty('--bg', hex(bg)); st.setProperty('--bg2', hex(mixC(bg, boost(P.left, 1.6), 0.06))); st.setProperty('--bg3', hex(mixC(bg, [250, 248, 250], 0.4)));
  }
  st.setProperty('--a1', hex(boost(P.left, 1.8, 1.25))); st.setProperty('--a2', hex(boost(P.right, 1.8, 1.25))); st.setProperty('--gc', hex(boost(P.avg, 2, 1.5)));
}
function readPalette(img) {
  try {
    const c = document.createElement('canvas'); c.width = 48; c.height = 16;
    const x = c.getContext('2d', { willReadFrequently: true }); x.drawImage(img, 0, 0, 48, 16);
    const d = x.getImageData(0, 0, 48, 16).data;
    const acc = (x0, x1) => { let r = 0, g = 0, b = 0, n = 0; for (let y = 0; y < 16; y++) for (let xx = x0; xx < x1; xx++) { const i = (y * 48 + xx) * 4, w = 1 + (Math.max(d[i], d[i + 1], d[i + 2]) - Math.min(d[i], d[i + 1], d[i + 2])) / 40; r += d[i] * w; g += d[i + 1] * w; b += d[i + 2] * w; n += w; } return [r / n, g / n, b / n].map(Math.round); };
    D.pal = { avg: acc(0, 48), left: acc(0, 24), right: acc(24, 48) };
  } catch (e) { D.pal = null; }
  palette();
}

/* ================= Geometrie (DIP) ================= */
const PAD = 32, GAP = 8;
/* Abstand zwischen Filtergruppe und Ansicht: mindestens 24 (sonst verschmelzen die Gruppen zu einem Band, Prüfung 2026-10-17).
   Dadurch geben die Felder 16 DIP früher nach als in der Tabelle des Dokuments (1138/1054/898 → 1154/1070/914).
   Nur in der Stufe „Sortierung nur Symbol“ darf er bis 16 sinken; darunter zwei Zeilen (798 → 806). */
const GROUP = 24, GROUP_MIN = 16;
const TH = { comfy: 1138 + GROUP - GAP, search: 1054 + GROUP - GAP, step: 898 + GROUP - GAP, two: 798 + GROUP_MIN - GAP };
TH.stepBack = TH.step + 24; TH.oneBack = TH.two + 32;            // Hysterese beim Ziehen: Beschriftung +24, eine Zeile +32
/* Werkzeugleiste: Leiter der Anpassung (Tabelle „Wie die Leiste in schmalen Fenstern nachgibt“) */
function layoutToolbar(L) {
  // Hysterese nur beim Ziehen; der erreichte Zustand bleibt, bis sich die Breite ohne Ziehen ändert
  const hy = S.hy, drag = S.dragging || (TL && TL.L === L);
  let two = drag && hy.two ? L < TH.oneBack : L < TH.two;
  const P = {};
  let ws, wsrc, wsort, sortIcon = false, stage, free = 0;
  if (!two) {
    sortIcon = drag && hy.sortIcon ? L < TH.stepBack : L < TH.step;
    if (sortIcon) { ws = 168; wsrc = 136; wsort = 60; stage = 'Sortierung nur Symbol (60), Felder am Minimum'; }
    else if (L >= TH.comfy) { ws = 320; wsrc = 188; wsort = 196; stage = 'bequem: Suche 320, Rest wird Freiraum'; }
    else if (L >= TH.search) { ws = 236 + (L - TH.search); wsrc = 188; wsort = 196; stage = 'Suche gibt nach (320 → 236)'; }
    else { const d = Math.min(156, TH.search - L); ws = 236 - d * 68 / 156; wsrc = 188 - d * 52 / 156; wsort = 196 - d * 36 / 156; stage = 'Felder geben nach (68 : 52 : 36)'; }
    const ws_ = [['search', ws], ['source', wsrc], ['sort', wsort], ['fAuto', 40], ['fNew', 40], ['fHdr', 40]];
    let acc = 0;
    for (const [id, w] of ws_) { const x0 = Math.round(acc); acc += w; P[id] = { x: x0, y: 0, w: Math.round(acc) - x0 }; acc += GAP; }
    const lEnd = P.fHdr.x + 40;
    P.add = { x: L - 40, y: 0, w: 40 }; P.seen = { x: L - 88, y: 0, w: 40 }; P.rescan = { x: L - 136, y: 0, w: 40 }; P.view = { x: L - 266, y: 0, w: 122 };
    free = P.view.x - GAP - lEnd;
  } else {
    stage = 'zwei Zeilen, Umbruch an der Gruppengrenze';
    const avail = L - 2 * GAP;
    if (avail >= 620) {
      // Zeile gefüllt; die Suche höchstens 320 (Maximum der Leiter), der Rest geht an Quelle und Sortierung
      const x = avail - 620; ws = 236 + x * 4 / 8; wsrc = 188 + x * 2 / 8; wsort = 196 + x * 2 / 8;
      if (ws > 320) { const ex = ws - 320; ws = 320; wsrc += ex / 2; wsort += ex / 2; }
    }
    else { const d = Math.min(156, 620 - avail); ws = 236 - d * 68 / 156; wsrc = 188 - d * 52 / 156; wsort = 196 - d * 36 / 156; }
    let acc = 0;
    for (const [id, w] of [['search', ws], ['source', wsrc], ['sort', wsort]]) { const x0 = Math.round(acc); acc += w; P[id] = { x: x0, y: 0, w: Math.round(acc) - x0 }; acc += GAP; }
    P.fAuto = { x: 0, y: 48, w: 40 }; P.fNew = { x: 48, y: 48, w: 40 }; P.fHdr = { x: 96, y: 48, w: 40 };
    P.add = { x: L - 40, y: 48, w: 40 }; P.seen = { x: L - 88, y: 48, w: 40 }; P.rescan = { x: L - 136, y: 48, w: 40 }; P.view = { x: L - 266, y: 48, w: 122 };
    free = P.view.x - GAP - 136;
  }
  const key = drag ? (hy.key ? P.search.w >= 204 : P.search.w >= 216) : P.search.w >= 204;
  return { rows: two ? 2 : 1, P, sortIcon, key, stage, free, raw: { ws, wsrc, wsort }, L };
}
function geometry() {
  const Wi = S.W - 2, Hi = S.H - 2;
  const paneW = Math.round(clamp(0.23 * Wi, 340, 396));
  const L = Wi - 64 - 24 - paneW;
  const tl = layoutToolbar(L);
  const th = tl.rows === 1 ? 40 : 88;
  const g = { Wi, Hi, paneW, L, tl, th, R: PAD + L, paneX: PAD + L + 24 };
  if (S.view === 'raster') {
    g.heroH = Math.round(clamp(Hi * 0.24, 196, 260));
    g.gap = 22; g.tbTop = g.heroH + 22;
    g.bodyTopExp = g.tbTop + th + 10; g.bodyTopCol = g.heroH + 22;
  } else {
    g.heroH = clamp(Math.round(Hi / 3), 186, Math.max(186, Math.floor(Hi - 368 - (th - 40))));
    g.gap = 10; g.tbTop = g.heroH + 10;
    g.bodyTopExp = g.tbTop + th + 10; g.bodyTopCol = g.heroH + 22;
  }
  g.edge = g.heroH;
  g.bodyTop = S.collapsed ? g.bodyTopCol : g.bodyTopExp;
  g.bodyBottom = Hi - 40;
  g.gripC = PAD + Math.round(L / 2);
  return g;
}

/* ================= Aufbau ================= */
const svgList = '<svg viewBox="0 0 24 24"><path d="M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01"/></svg>';
const svgGrid = '<svg viewBox="0 0 24 24"><path d="M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z"/></svg>';
const svgShelf = '<svg viewBox="0 0 24 24"><path d="M3 6h18M3 18h18M6 9v6M10 9v6M14 9v6M18 9v6"/></svg>';
const icoSwitch = '<span class="msw"><i class="tk"></i><i class="tf"></i><i class="kn"></i></span>';
const icoNew = '<span class="plq"><b>NEU</b></span>', icoHdr = '<span class="plq"><b>HDR</b></span>';
function buildToolbar() {
  tb.innerHTML = `
<div class="te f-search" id="e-search" data-tip="search"><span class="fr"></span><span class="ff"></span><span class="fh"></span><span class="gl sg"></span>
  <input id="q" type="text" placeholder="Suchen …" autocomplete="off" spellcheck="false" aria-label="Spiele durchsuchen" aria-keyshortcuts="Control+F">
  <span class="kbd" id="qKey">Strg+F</span><button class="sx" id="qX" tabindex="-1" aria-label="Suche leeren"></button></div>
<button class="te f-combo" id="e-source" data-tip="source" aria-haspopup="listbox" aria-label="Quelle"><span class="fr"></span><span class="cin"><i class="cdot" id="srcDot"></i><span class="cl" id="srcL">Alle</span><span class="ct" id="srcN">0</span></span><span class="gl ch"></span><span class="ff2"></span><span class="fring"></span></button>
<button class="te f-combo" id="e-sort" data-tip="sort" aria-haspopup="listbox" aria-label="Sortierung"><span class="fr"></span><span class="cin"><span class="cg" id="sortG"></span><span class="cl" id="sortL">Zuletzt gespielt</span></span><span class="gl ch"></span><span class="ff2"></span><span class="fring"></span></button>
<button class="te f-flt" id="e-fAuto" data-tip="fAuto" aria-pressed="false" aria-keyshortcuts="Alt+A" aria-label="Nur Spiele mit HDR automatisch"><span class="halo"></span><span class="fr"></span><span class="on"></span><span class="ib">${icoSwitch}</span><span class="bdg" id="bAuto"><span>0</span></span><span class="fring"></span></button>
<button class="te f-flt" id="e-fNew" data-tip="fNew" aria-pressed="false" aria-keyshortcuts="Alt+N" aria-label="Nur neue Spiele"><span class="halo"></span><span class="fr"></span><span class="on"></span><span class="ib">${icoNew}</span><span class="bdg" id="bNew"><span>0</span></span><span class="fring"></span></button>
<button class="te f-flt" id="e-fHdr" data-tip="fHdr" aria-pressed="false" aria-keyshortcuts="Alt+H" aria-label="Nur HDR-fähige Spiele"><span class="halo"></span><span class="fr"></span><span class="on"></span><span class="ib">${icoHdr}</span><span class="bdg" id="bHdr"><span>0</span></span><span class="fring"></span></button>
<div class="te f-seg" id="e-view" role="radiogroup" aria-label="Ansicht"><span class="fr"></span><span class="sgo" id="sgo"></span><span class="sgi">
  <button class="sb" data-v="liste" data-tip="vList" role="radio" aria-label="Liste">${svgList}</button><button class="sb" data-v="raster" data-tip="vGrid" role="radio" aria-label="Raster">${svgGrid}</button><button class="sb" data-v="regal" data-tip="vShelf" role="radio" aria-label="Regal">${svgShelf}</button></span></div>
<button class="te f-ico" id="e-rescan" data-tip="rescan" aria-label="Neu einlesen"><span class="fr"></span><span class="gi"></span><span class="fring"></span></button>
<button class="te f-ico" id="e-seen" data-tip="seen" aria-label="Alle als gesehen markieren"><span class="fr"></span><span class="gi"></span><span class="fring"></span></button>
<button class="te f-ico" id="e-add" data-tip="add" aria-label="Spiel hinzufügen"><span class="fr"></span><span class="gi"></span><span class="fring"></span></button>`;
  for (const id of IDS) TE[id] = $('#e-' + id);
}
function heroGame() {
  const want = (S.game || '').toLowerCase();
  return (want && D.games.find(g => g.n.toLowerCase().includes(want))) || sorted(D.games)[0];
}
function buildHero() {
  const g = S.sel;
  if (!g) return;
  if (g.img.hero) { art.src = g.img.hero; art.style.display = ''; } else art.style.display = 'none';
  const src = SRC[g.src] || [g.src, '#C8CCD8'], h = HDRL[g.hdr] || HDRL.Unknown;
  const logo = g.img.logo ? `<img id="logo" src="${esc(g.img.logo)}" alt="${esc(g.n)}" style="visibility:hidden">` : `<div class="nm">${esc(g.n)}</div>`;
  hc.innerHTML = `<div class="lg">${logo}</div>
  <div class="mid"><div class="ov">${g.nw ? 'NEU IN DER BIBLIOTHEK · ' + esc(when(g.fs).toUpperCase()) : 'ZULETZT GESPIELT · ' + esc(when(g.lp).toUpperCase())}</div>
  <div class="meta"><span class="chip"><i style="background:${src[1]}"></i>${esc(g.src === 'Gog' ? 'GOG' : g.src)}</span><span class="tag ${g.hdr === 'Native' ? 'nat' : ''}">${h[2]}</span>${g.nw ? '<span class="nb">NEU</span>' : ''}</div></div>
  <div class="act"><span class="pbtn2"><span class="gl"></span>Starten</span><span class="lchip"><span class="gl"></span><span><small>BEIM START</small><b>HDR bleibt unverändert</b></span></span></div>`;
  const li = $('#logo'); if (li) { if (li.complete && li.naturalWidth) trimLogo(li); else li.addEventListener('load', () => trimLogo(li), { once: true }); }
  const cover = g.img.cover || g.img.header || '';
  pane.innerHTML = `<div class="in">
  <div class="pp">${cover ? `<img src="${esc(cover)}" alt="">` : '<img alt="">'}<div class="t"><small>SPIELPROFIL</small><b>${esc(g.n)}</b><span>${esc(src[0].split(' ')[0])} · keine EXE erkannt</span></div><span class="mo gl"></span></div>
  <div class="ps">STARTEN</div>
  <div class="prw"><span class="pbt acc"><span class="gl"></span>Starten</span><span class="pbt">Desktop</span><span class="pbt">Startmenü</span></div>
  <div class="purl"><span class="gl"></span> ${esc(g.cmd || 'steam://rungameid/' + (g.sid || ''))}</div>
  <div class="pbt pwide"><span class="gl"></span>Steam-Startoption kopieren</div>
  <div class="ptx">Notlösung für den Start direkt aus Steam: HDR geht dann vor dem Spiel an.</div>
  <div class="ps">AUTOMATIK</div>
  <div class="psw"><div class="t"><b>HDR automatisch</b><span>HDR einschalten, sobald das Spiel startet.</span></div><span class="sw${g.auto ? ' on' : ''}"></span></div>
  <div class="psw"><div class="t"><b>Nach dem Spiel zurückstellen</b><span>Danach wieder den HDR-Zustand von vorher herstellen.</span></div><span class="sw on"></span></div>
  <div class="psw"><div class="t"><b>Bildschirm</b><span>Standard ist die globale Einstellung.</span></div></div>
  <div class="pcombo"><span class="gl"></span>Globale Einstellung (MAG 341C OLED)<span class="sp"></span><span class="gl ch"></span></div>
  <div class="ps">BILD</div>
  <div class="psw"><div class="t"><b>Windows Auto HDR</b><span>Wirkt beim Start über HDRpilot. Windows rechnet das SDR-Bild in HDR um.</span></div><span class="sw"></span></div>
  </div>`;
}
/* Logo: durchsichtigen Rand abschneiden (WPF: CroppedBitmap), dann höchstens 380 × 94, nur verkleinert */
function trimLogo(img) {
  const nw = img.naturalWidth, nh = img.naturalHeight;
  let x0 = 0, y0 = 0, x1 = nw, y1 = nh;
  try {
    const c = document.createElement('canvas'); c.width = nw; c.height = nh;
    const x = c.getContext('2d', { willReadFrequently: true }); x.drawImage(img, 0, 0);
    const d = x.getImageData(0, 0, nw, nh).data;
    x0 = nw; y0 = nh; x1 = 0; y1 = 0;
    for (let y = 0; y < nh; y++) for (let xx = 0; xx < nw; xx++) if (d[(y * nw + xx) * 4 + 3] > 12) { if (xx < x0) x0 = xx; if (xx >= x1) x1 = xx + 1; if (y < y0) y0 = y; if (y >= y1) y1 = y + 1; }
    if (x1 <= x0 || y1 <= y0) { x0 = 0; y0 = 0; x1 = nw; y1 = nh; }
  } catch (e) { /* ohne Zuschnitt */ }
  const cw = x1 - x0, ch = y1 - y0, s = Math.min(380 / cw, 94 / ch, 1);
  img.style.objectViewBox = `inset(${y0}px ${nw - x1}px ${nh - y1}px ${x0}px)`;
  img.style.width = Math.round(cw * s) + 'px'; img.style.height = Math.round(ch * s) + 'px';
  img.style.maxWidth = 'none'; img.style.maxHeight = 'none'; img.style.objectFit = 'fill';
  img.style.visibility = '';
}
function buildHeadStats() {
  $('#hdStats').innerHTML = `<span><i style="background:#8D92A8"></i>${D.total} Spiele</span><span><i style="background:#FF8A3D"></i>${D.cAuto} mit HDR automatisch</span>${D.cNew ? `<span><i style="background:#FF94B9"></i>${D.cNew} neu</span>` : ''}`;
}

/* Spalten der Liste (ShowcaseLayout.ListColumnsFor) */
function listColumns(w) {
  let name = w - (64 + 118 + 28 + 28) - (124 + 14 + 96 + 14 + 140 + 14);
  let src = true, hdr = true, last = true;
  if (name < 180) { src = false; name += 138; }
  if (name < 180) { hdr = false; name += 110; }
  if (name < 180) last = false;
  const shown = src + last + hdr, extra = shown && name > 640 ? Math.floor((name - 640) / shown) : 0;
  return { src, hdr, last, ws: 124 + extra, wl: 140 + extra, wh: 96 + extra };
}
let rowsKey = '';
function renderBody() {
  const list = filtered();
  const c = listColumns(G.L);
  lcin.style.setProperty('--ws', c.ws + 'px'); lcin.style.setProperty('--wl', c.wl + 'px'); lcin.style.setProperty('--wh', c.wh + 'px');
  lcin.classList.toggle('nosrc', !c.src); lcin.classList.toggle('nolast', !c.last); lcin.classList.toggle('nohdr', !c.hdr);
  const key = [S.view, S.view === 'raster' ? G.L : 0, S.q, S.src, S.fAuto, S.fNew, S.fHdr, S.sort, S.theme, D.total].join('|');
  if (key === rowsKey) return list.length;
  rowsKey = key;
  if (S.view === 'raster') {
    const cols = Math.max(2, Math.floor((G.L - 8 + 16) / (176 + 16)));
    gin.style.gridTemplateColumns = `repeat(${cols},1fr)`;
    gin.innerHTML = list.slice(0, cols * 6).map(g => `<div class="tile${g === S.sel ? ' sel' : ''}">${g.img.cover ? `<img loading="lazy" src="${esc(g.img.cover)}" alt="">` : ''}<b>${esc(g.n)}</b></div>`).join('');
    return list.length;
  }
  // Spaltenköpfe wie in der App: alle gedämpft, ohne Sortierpfeil
  const head = `<div class="lhead"><span class="tsw"><span class="on"></span><span></span></span><span class="ov nm">SPIEL · ${list.length}</span><span class="ov c-src">QUELLE</span><span class="ov c-lp">ZULETZT GESPIELT</span><span class="ov c-hd">HDR</span><span class="ov ar" style="width:118px">HDR AUTOMATISCH</span></div>`;
  let rows;
  if (!list.length) {
    rows = `<div class="nomatch"><span class="gl"></span><div class="t"><b>Keine Spiele für diese Auswahl</b><span>Suche, Quelle und Filter schließen alle Spiele aus.</span></div><button id="nmReset">Filter zurücksetzen</button></div>`;
  } else {
    rows = list.map(g => {
      const s = SRC[g.src] || [g.src, '#C8CCD8'], h = HDRL[g.hdr] || HDRL.Unknown;
      const hd = g.hdr === 'Unknown' ? '<span class="hd">—</span>' : `<span class="hp ${h[1]}">${g.hdr === 'Native' ? '<i></i>' : ''}${h[0]}</span>`;
      const th = g.img.header || g.img.cover || g.img.icon || '';
      return `<div class="lrow${g === S.sel ? ' sel' : ''}"><span class="th">${th ? `<img loading="lazy" src="${esc(th)}" alt="">` : ''}</span><span class="nm"><b>${esc(g.n)}</b>${g.nw ? '<span class="nwb">NEU</span>' : ''}</span><span class="c src c-src"><i style="background:${s[1]}"></i>${esc(s[0].replace(' Connect', '').replace(' Games', ''))}</span><span class="c lp c-lp">${esc(when(g.lp))}</span><span class="c c-hd">${hd}</span><span class="c au" style="width:118px">${g.auto ? 'An' : 'Aus'}<span class="sw${g.auto ? ' on' : ''}"></span></span></div>`;
    }).join('');
  }
  lcin.innerHTML = head + `<div class="rows" id="rows">${rows}</div>`;
  const nm = $('#nmReset'); if (nm) nm.onclick = resetFilters;
  return list.length;
}
function renderStatus(n) {
  const kh = S.W >= 1500 ? `<span class="kh"><span class="kbd">↑ ↓</span>Auswahl</span><span class="kh"><span class="kbd">Enter</span>Starten</span><span class="kh"><span class="kbd">Strg+Umschalt+F</span>Leiste</span><span class="kh"><span class="kbd">Umschalt+F10</span>Menü</span>` : '';
  $('#status').innerHTML = `<i class="dt"></i><span>Automatik aktiv · wartet auf Spielstart</span><span class="mu">${S.view === 'raster' ? 'Raster' : 'Liste'} · ${n} von ${D.total}${isFiltered() ? ' · gefiltert' : ''} · sortiert nach ${SORTS[S.sort][0]}</span><span class="sp"></span>${kh}`;
}

/* ================= Leiste: Inhalt ================= */
function syncToolbar() {
  const src = S.src === 'Alle' ? ['Alle', '#8D92A8'] : (SRC[S.src] || [S.src, '#8D92A8']);
  $('#srcL').textContent = src[0].replace(' Connect', '').replace(' app', '').replace(' Games', '');
  $('#srcDot').style.background = src[1];
  $('#srcN').textContent = S.src === 'Alle' ? D.total : (D.bySrc[S.src] || 0);
  $('#sortL').textContent = SORTS[S.sort][0]; $('#sortG').textContent = SORTS[S.sort][1];
  const bd = (id, n, nw) => { const b = $(id); b.firstChild.textContent = fmt(n); b.classList.toggle('zero', !n); b.classList.toggle('nw', !!nw && n > 0); };
  bd('#bAuto', D.cAuto); bd('#bNew', D.cNew, true); bd('#bHdr', D.cHdr);
  // ItemStatus der App: Zahl der Spiele je Filter
  for (const [id, n] of [['fAuto', D.cAuto], ['fNew', D.cNew], ['fHdr', D.cHdr]]) { TE[id].setAttribute('aria-pressed', String(S[id])); TE[id].setAttribute('aria-description', n + ' Spiele'); }
  pill.setAttribute('aria-description', isFiltered() ? 'Gefiltert: ' + filterText() : 'Keine Filter');
  const q = $('#q'); if (q.value !== S.q) q.value = S.q;
  TE.search.classList.toggle('has', !!S.q);
  const vi = S.view === 'raster' ? 1 : 0;
  // Optionsgruppe: ein Tab-Halt (die gewählte Ansicht), Pfeiltasten wechseln (roving tabindex)
  TE.view.querySelectorAll('.sb').forEach((b, i) => { b.classList.toggle('cur', i === vi); b.setAttribute('aria-checked', String(i === vi)); b.tabIndex = i === vi ? 0 : -1; });
  const sgo = $('#sgo'), was = sgo.dataset.vi;
  if (was != null && +was !== vi && S.fx === 'reduziert' && Clock.frozen == null && !S.shot) {
    // Reduziert: kein Versatz, die gewählte Fläche wird in 150 ms überblendet
    const old = sgo.cloneNode(true); old.removeAttribute('id'); sgo.after(old);
    old.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 150 / S.speed, fill: 'forwards' }).onfinish = () => old.remove();
    sgo.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 150 / S.speed });
  }
  sgo.dataset.vi = vi;
  sgo.style.transform = `translateX(${vi * 38}px)`;
  // Bedienfeld
  $('#pAuto').setAttribute('aria-pressed', String(S.fAuto)); $('#pNew').setAttribute('aria-pressed', String(S.fNew)); $('#pHdr').setAttribute('aria-pressed', String(S.fHdr));
  if ($('#pQ').value !== S.q) $('#pQ').value = S.q;
  $('#pSrc').value = S.src;
}

/* ================= Kapsel ================= */
const capMeasure = document.createElement('span');
function capItems() {
  const it = [];
  if (!isFiltered()) {
    it.push({ k: 'q0', h: `<span class="gl"></span><span style="color:var(--muted)">Suchen</span><span class="kbd" style="margin-left:6px">Strg+F</span>` });
    return { items: it, x: false };
  }
  const n = filtered().length;
  if (S.q.trim()) it.push({ k: 'q', h: `<span class="gl"></span><b>„${esc(S.q.trim().slice(0, 16))}${S.q.trim().length > 16 ? '…' : ''}“</b>`, cls: 'q', drop: 3 });
  if (S.src !== 'Alle') { const s = SRC[S.src] || [S.src, '#8D92A8']; it.push({ k: 's', h: `<i class="sd" style="background:${s[1]}"></i>${esc(s[0].replace(' Connect', '').replace(' app', '').replace(' Games', ''))}`, drop: 2 }); }
  const fl = [];
  if (S.fAuto) fl.push(icoSwitch); if (S.fNew) fl.push(icoNew); if (S.fHdr) fl.push(icoHdr);
  if (fl.length) it.push({ k: 'f', h: fl.join(''), drop: 1, gap: true });
  it.push({ k: 'n', h: `<b>${n}</b>/${D.total}`, cls: 'ct' });
  return { items: it, x: true };
}
function capHtml(items) {
  return items.map((o, i) => (i ? '<span class="cpi sep"></span>' : '') + `<span class="cpi${i === 0 ? ' first' : ''}${o.cls ? ' ' + o.cls : ''}" data-i="${i}"${o.gap ? ' style="gap:4px"' : ''}>${o.h}</span>`).join('');
}
function measureW(html) {
  capMeasure.innerHTML = html;
  return Math.ceil(capMeasure.getBoundingClientRect().width / (stageScale || 1));
}
let capState = { w: 36, items: 0, x: false, key: '' };
function buildCapsule() {
  const { items, x } = capItems();
  const maxW = Math.max(120, G.R - (G.gripC + 32));   // höchstens bis 32 px vor die Mitte des Ziehgriffs
  const base = 30 + 2 + (x ? 26 : 12);                 // Pfeil + Trenner, rechts ✕ oder Rand
  let use = items.slice(), dropped = 0, html = capHtml(use), w = base + measureW(html);
  while (w > maxW && use.some(o => o.drop)) {
    const m = use.reduce((a, o) => (o.drop && (!a || o.drop > a.drop) ? o : a), null);
    use = use.filter(o => o !== m); dropped++;
    const more = { k: 'm', h: `+${dropped}`, cls: 'more' };
    html = capHtml([more, ...use]); w = base + measureW(html);
    if (w <= maxW) { use = [more, ...use]; break; }
    if (!use.some(o => o.drop)) { use = [more, ...use]; break; }
  }
  html = capHtml(use);
  const key = html + '|' + x;
  cpc.innerHTML = html;
  const mw = measureW(html), cw = Math.round(base + mw);
  // Inhalt rechtsbündig verankert: er steht still, während die Kapsel nach links aufgeht (kein Nachrutschen)
  cpc.style.left = 'auto'; cpc.style.right = (cw - 30 - mw) + 'px';
  cx.style.right = '0px';
  capState = { w: cw, items: use.length, x, key, flt: isFiltered() };
  pill.classList.toggle('flt', S.collapsed && capState.flt);
  return capState;
}

/* ================= Zeilenwechsel (Morph, Stufe Voll) =================
  Die Achsen laufen nacheinander, damit sich nichts kreuzt oder überholt (Prüfung 2026-10-17):
  1 → 2  erst fallen die Zeilenwechsler zusammen mit der Liste (je 12 ms versetzt, sie bleiben hinter der Liste zurück),
         dann gleiten die Filter in Zeile 2 nach links (der vorderste zuerst) und die Felder der Zeile 1 wachsen.
  2 → 1  erst geben die Felder nach und die Filter gleiten in Zeile 2 nach rechts (der vorderste zuerst),
         dann steigen alle Zeilenwechsler gemeinsam, die Liste steigt im selben Takt mit (nie über ein Element).
  Gesamtdauer etwa 320 bzw. 350 ms (Spezifikation 300 ms). Kurve überall ExponentialEase Out, Exponent 6. */
const HERO_INSET = 22;
const FIELDS = ['search', 'source', 'sort'], FILTERS = ['fAuto', 'fNew', 'fHdr'], RIGHT = ['view', 'rescan', 'seen', 'add'];
const MORPH = {
  toTwo: { drop: 240, dropStep: 12, slideAt: 80, slide: 200, slideStep: 12, growAt: 96, grow: 220 },
  toOne: { shrink: 150, slide: 120, slideStep: 16, riseAt: 150, rise: 200 },
};
function morph(moved, toTwo) {
  const M = {}; for (const m of moved) M[m.id] = m;
  const ex = E.expoOut6;
  const go = (k, from, o) => { if (Math.abs(from) < 0.01) { set(k, 0); return; } set(k, from); anim(k, 0, Object.assign({ ease: ex }, o)); };
  for (const id of RIGHT) { set('m:' + id + ':x', 0); set('m:' + id + ':w', 0); }       // die rechte Gruppe hängt am Fensterrand
  for (const id of FILTERS) set('m:' + id + ':w', 0);
  for (const id of FIELDS) set('m:' + id + ':y', 0);
  if (toTwo) {
    const T = MORPH.toTwo;
    [...FILTERS, ...RIGHT].forEach((id, i) => go('m:' + id + ':y', M[id].dy, { delay: T.dropStep * i, dur: T.drop }));
    FILTERS.forEach((id, j) => go('m:' + id + ':x', M[id].dx, { delay: T.slideAt + T.slideStep * j, dur: T.slide }));   // nach links: „Automatik“ führt
    for (const id of FIELDS) { go('m:' + id + ':x', M[id].dx, { delay: T.growAt, dur: T.grow }); go('m:' + id + ':w', M[id].dw, { delay: T.growAt, dur: T.grow }); }
  } else {
    const T = MORPH.toOne;
    for (const id of FIELDS) { go('m:' + id + ':x', M[id].dx, { dur: T.shrink }); go('m:' + id + ':w', M[id].dw, { dur: T.shrink }); }
    FILTERS.slice().reverse().forEach((id, j) => go('m:' + id + ':x', M[id].dx, { delay: T.slideStep * j, dur: T.slide }));   // nach rechts: „HDR“ führt
    for (const id of [...FILTERS, ...RIGHT]) go('m:' + id + ':y', M[id].dy, { delay: T.riseAt, dur: T.rise });
  }
}

/* ================= Anordnen (Layout, einmal je Änderung) ================= */
let stageScale = 1;
function layout(o = {}) {
  const prev = G, prevTL = TL;
  G = geometry();
  TL = G.tl;
  const anim_ = o.animate !== false && S.dragging && prevTL && S.fx !== 'aus';
  // Hysterese-Gedächtnis
  S.hy = { two: TL.rows === 2, sortIcon: TL.sortIcon, key: TL.key };
  const ws = win.style;
  ws.width = S.W + 'px'; ws.height = S.H + 'px';
  win.classList.toggle('dark', S.theme === 'dunkel'); win.classList.toggle('light', S.theme === 'hell');
  win.classList.remove('fx-voll', 'fx-red', 'fx-aus'); win.classList.add(S.fx === 'voll' ? 'fx-voll' : S.fx === 'reduziert' ? 'fx-red' : 'fx-aus');
  document.documentElement.dataset.theme = S.theme === 'hell' ? 'light' : 'dark';
  // Titelbild, Kopf, Inhalt des Titelbereichs (unterer Innenabstand 22 statt 16: 10 px Luft über Kapsel und Lasche, Bildhöhe unverändert)
  hero.style.height = G.heroH + 'px';
  $('#head').classList.toggle('compact', S.W < 1366);
  Object.assign(hc.style, { top: '98px', height: (G.heroH - 98 - HERO_INSET) + 'px', width: G.L + 'px' });
  hc.classList.toggle('narrow', G.L < 1080);
  Object.assign(pane.style, { left: G.paneX + 'px', width: G.paneW + 'px', height: (G.bodyBottom - 104) + 'px' });
  // Ziehgriff: Mitte genau auf der Unterkante des Titelbilds (nur Liste)
  grip.style.display = S.view === 'raster' ? 'none' : '';
  Object.assign(grip.style, { left: PAD + 'px', width: G.L + 'px', top: (G.edge - 10) + 'px' });
  // Werkzeugleiste in ihrer Klammer: Oberkante 100 über der Bildkante, Unterkante = Oberkante der ausgeklappten Liste
  const clipTop = G.edge - 100, clipX = 24;
  Object.assign(tbclip.style, { left: (PAD - clipX) + 'px', top: clipTop + 'px', width: (G.L + 2 * clipX) + 'px', height: (G.bodyTopExp - clipTop) + 'px' });
  Object.assign(tb.style, { left: clipX + 'px', top: (G.tbTop - clipTop) + 'px', width: G.L + 'px', height: G.th + 'px' });
  // Bewegt wird nur bei einem Zeilenwechsel oder beim Sortier-Schritt; sonst folgt alles dem Fensterrand direkt
  const rowChange = !!(anim_ && prevTL.rows !== TL.rows);
  const stepChange = !!(anim_ && !rowChange && prevTL.sortIcon !== TL.sortIcon);
  const moved = [];
  for (const id of IDS) {
    const p = TL.P[id], el = TE[id];
    if (rowChange || stepChange) {
      const q = prevTL.P[id];
      const ox = q.x + val('m:' + id + ':x'), oy = q.y + val('m:' + id + ':y') + (prev ? prev.tbTop - G.tbTop : 0);
      const ow = (id === 'sort' ? val('sortW', q.w) : q.w) + val('m:' + id + ':w');
      moved.push({ id, dx: ox - p.x, dy: oy - p.y, dw: ow - p.w });
    }
    el.style.left = p.x + 'px'; el.style.top = p.y + 'px';
  }
  // Sortierung: Breite und Beschriftung
  const sortW = TL.P.sort.w, lab = TL.sortIcon ? 0 : 1, labX = TL.sortIcon ? -4 : 0, labChange = !!(prevTL && prevTL.sortIcon !== TL.sortIcon);
  if (stepChange && S.fx === 'voll') {
    anim('sortW', sortW, { from: val('sortW', prevTL.P.sort.w), dur: 160, ease: E.cubicOut });
    if (TL.sortIcon) { anim('sortL:o', 0, { dur: 90, ease: E.cubicOut }); anim('sortL:x', -4, { dur: 160, ease: E.cubicOut }); }
    else { anim('sortL:o', 1, { delay: 40, dur: 120, ease: E.cubicOut }); anim('sortL:x', 0, { dur: 160, ease: E.cubicOut }); }
  } else if ((stepChange || rowChange) && S.fx === 'reduziert') {
    set('sortW', sortW); set('sortL:x', labX);
    if (stepChange) anim('sortL:o', lab, { dur: 150, ease: E.lin }); else set('sortL:o', lab);
  } else if (rowChange) {                                   // Voll: der Morph übernimmt die Breite (m:sort:w)
    set('sortW', sortW);
    if (labChange && TL.sortIcon) { anim('sortL:o', 0, { dur: 90, ease: E.cubicOut }); anim('sortL:x', -4, { dur: 160, ease: E.cubicOut }); }
    else if (labChange) { anim('sortL:o', 1, { delay: MORPH.toTwo.growAt + 40, dur: 140, ease: E.cubicOut }); anim('sortL:x', 0, { delay: MORPH.toTwo.growAt, dur: 160, ease: E.cubicOut }); }
  } else if (AN.has('sortW')) AN.get('sortW').to = sortW;   // laufender Schritt: das Ziel folgt dem Fensterrand
  else { set('sortW', sortW); if (!AN.has('sortL:o')) { set('sortL:o', lab); set('sortL:x', labX); } }
  // Tastenkappe „Strg+F“ (blendet in 120 ms)
  if (anim_ && prevTL.key !== TL.key) anim('key:o', TL.key ? 1 : 0, { dur: 120, ease: E.cubicOut });
  else if (!AN.has('key:o')) set('key:o', TL.key ? 1 : 0);
  $('#q').style.paddingRight = (TL.key && !S.q ? 70 : 36) + 'px';
  // Verschieben (FLIP)
  const zeroM = id => { set('m:' + id + ':x', 0); set('m:' + id + ':y', 0); set('m:' + id + ':w', 0); };
  if (rowChange && !S.collapsed && S.fx === 'voll') morph(moved, TL.rows === 2);
  else if (rowChange && !S.collapsed && S.fx === 'reduziert') { ghost(tb, 150); anim('tb:o', 1, { from: 0, dur: 150, ease: E.lin }); IDS.forEach(zeroM); }
  else if (stepChange && !S.collapsed && S.fx === 'voll') {
    // Sortier-Schritt: nur die linke Gruppe rückt nach (Breite 160 ms CubicEase Out); die rechte Gruppe hängt am Fensterrand
    for (const m of moved) {
      if (RIGHT.includes(m.id)) continue;
      const k = 'm:' + m.id;
      if (Math.abs(m.dx) > 0.5) { set(k + ':x', m.dx); anim(k + ':x', 0, { dur: 160, ease: E.cubicOut }); }
      if (m.id !== 'sort' && Math.abs(m.dw) > 0.5) { set(k + ':w', m.dw); anim(k + ':w', 0, { dur: 160, ease: E.cubicOut }); }
    }
  } else if (!anim_ || ((rowChange || stepChange) && (S.collapsed || S.fx !== 'voll'))) IDS.forEach(zeroM);
  // Liste bzw. Raster: beim Zeilenwechsel mitgeführt (FLIP), gekoppelt an die Zeilenwechsler
  const bodyH = G.bodyBottom - G.bodyTop;
  if (rowChange && !S.collapsed && prev.bodyTop !== G.bodyTop) {
    const oldVis = prev.bodyTop + val('list:dy');
    if (S.fx === 'voll') {
      set('list:dy', oldVis - G.bodyTop);
      anim('list:dy', 0, TL.rows === 2 ? { dur: MORPH.toTwo.drop, ease: E.expoOut6 } : { delay: MORPH.toOne.riseAt, dur: MORPH.toOne.rise, ease: E.expoOut6 });
    } else set('list:dy', 0);
  } else if (prev && prev.bodyTop !== G.bodyTop && !o.keepListDy && !AN.has('list:dy')) set('list:dy', 0);
  const isList = S.view !== 'raster';
  lclip.style.display = isList ? '' : 'none'; gridEl.hidden = isList;
  Object.assign(lclip.style, { left: PAD + 'px', width: G.L + 'px', top: (G.bodyTop - 100) + 'px', height: (bodyH + 100) + 'px' });
  Object.assign(lcbg.style, { height: bodyH + 'px' });
  Object.assign(lcin.style, { top: '100px', height: bodyH + 'px' });
  Object.assign(gridEl.style, { left: PAD + 'px', width: G.L + 'px', top: (G.bodyTop - 100) + 'px', height: (bodyH + 100) + 'px' });
  gin.style.top = '100px';
  const n = renderBody();
  const rows = $('#rows'); if (rows) rows.style.height = (bodyH - 37) + 'px';
  renderStatus(n);
  syncToolbar();
  // Lasche/Kapsel, Naht, Streif
  buildCapsule();
  Object.assign(tabw.style, { right: (G.Wi - G.R) + 'px', top: (G.edge - 12) + 'px', width: Math.max(capState.w, 36) + 'px' });
  const capL = G.R - capState.w;
  // Naht: Klammer über die ganze Listenbreite mit ruhender Lücke um den Griff; die Naht selbst endet immer an der Lasche bzw. Kapsel (apply)
  Object.assign(seamw.style, { left: PAD + 'px', width: G.L + 'px', top: (G.edge - 5) + 'px' });
  seam.style.width = Math.max(0, G.L - 36) + 'px';
  const gc = G.gripC - PAD;
  const mask = S.view === 'raster' ? '' : `linear-gradient(90deg,#000 0,#000 ${gc - 34}px,transparent ${gc - 26}px,transparent ${gc + 26}px,#000 ${gc + 34}px,#000 100%)`;
  seamw.style.webkitMaskImage = mask; seamw.style.maskImage = mask;
  Object.assign(streak.style, { top: (G.edge - 6) + 'px' });
  const tabL = S.collapsed ? capL : G.R - 36;   // Leuchtlinie des Griffs endet 12 px vor Lasche bzw. Kapsel
  $('#gripLine').style.width = Math.max(0, tabL - 12 - PAD - 18) + 'px';
  if (!o.keepTab) settleTab();
  if (stageFit) fitStage();
  readout();
  apply();
}
/* Lasche/Kapsel ohne laufende Bewegung auf den Endwert setzen */
const TAB_TY = -3;   // Lasche 3 px höher als die Kapsel: −15 bis +3 um die Bildkante (Kapsel −12 bis +12)
function settleTab() {
  const busy = ['pill:w', 'pill:h', 'pill:r', 'pill:ty'].some(k => AN.has(k));
  if (busy) { if (S.collapsed && AN.has('pill:w')) AN.get('pill:w').to = capState.w; return; }
  if (S.collapsed) {
    if (val('pill:w', 0) !== capState.w && S.fx !== 'aus' && CH['pill:w'] != null && val('pill:h') === 24) anim('pill:w', capState.w, { dur: 160, ease: E.cubicOut });
    else set('pill:w', capState.w);
    set('pill:h', 24); set('pill:ty', 0); set('pill:r', 180); set('cdv:o', 1); set('hint:o', 0);
    for (let i = 0; i < 12; i++) { if (!AN.has('ci:' + i + ':o')) set('ci:' + i + ':o', 1); if (!AN.has('ci:' + i + ':x')) set('ci:' + i + ':x', 0); }
    if (!AN.has('cx:o')) set('cx:o', capState.x ? 1 : 0);
    const rest2 = capState.flt ? 0.35 : 0;
    if (!AN.has('halo2:o')) { if (S.fx === 'aus') set('halo2:o', rest2); else anim('halo2:o', rest2, { dur: 200 }); }
    if (!AN.has('seam:sx') && !AN.has('seam:o')) {
      const want = capState.flt ? 1 : 0;
      if (S.fx === 'voll' && val('seam:sx') !== want) {
        if (want) { set('seam:o', 1); anim('seam:sx', 1, { from: val('seam:sx'), dur: 400, ease: E.spline }); }
        else anim('seam:sx', 0, { dur: 200, ease: E.cubicOut });
      } else if (S.fx === 'reduziert' && val('seam:o') !== want) { set('seam:sx', 1); anim('seam:o', want, { dur: 150, ease: E.lin }); }
      else if (S.fx === 'aus') { set('seam:sx', want); set('seam:o', want); }
    }
  } else {
    if (!hoverTab) set('pill:w', 36);
    set('pill:h', 18); set('pill:ty', TAB_TY); set('pill:r', 0); set('cdv:o', 0);
    for (let i = 0; i < 12; i++) { set('ci:' + i + ':o', 0); set('ci:' + i + ':x', -6); }
    set('cx:o', 0); if (!AN.has('halo2:o')) set('halo2:o', 0);
    if (!AN.has('seam:sx')) { set('seam:sx', 0); set('seam:o', 0); }
  }
}

/* ================= Anwenden (einmal je Bild; nur opacity/transform, dazu Breite/Höhe der Lasche) ================= */
const GH = new Map(); let gid = 0;
function ghost(el, dur) {
  const g = el.cloneNode(true);
  g.removeAttribute('id'); g.querySelectorAll('[id]').forEach(n => n.removeAttribute('id'));
  g.style.pointerEvents = 'none'; g.setAttribute('aria-hidden', 'true'); g.inert = true;
  el.parentNode.insertBefore(g, el.nextSibling);
  const k = 'ghost:' + (++gid); GH.set(k, g); set(k, 1); anim(k, 0, { dur, ease: E.lin });
}
function apply() {
  const tbo = val('tb:o', 1);
  let anyVisible = false;
  for (const id of IDS) {
    const el = TE[id];
    const o = val('w:' + id + ':o', 1), x = val('w:' + id + ':x') + val('m:' + id + ':x'), y = val('w:' + id + ':y') + val('m:' + id + ':y'), s = val('w:' + id + ':s', 1);
    el.style.transform = (x || y || s !== 1) ? `translate(${x}px,${y}px)${s !== 1 ? ` scale(${s})` : ''}` : '';
    if (TL && id !== 'sort') { const w = TL.P[id].w + val('m:' + id + ':w'); el.style.width = (Math.round(w * 100) / 100) + 'px'; }
    el.style.opacity = o >= 0.999 ? '' : String(Math.max(0, o));
    if (o > 0.001) anyVisible = true;
  }
  tb.style.opacity = tbo >= 0.999 ? '' : String(tbo);
  tb.classList.toggle('gone', S.collapsed && !anyVisible);
  tb.inert = S.collapsed;
  // Sortierung
  if (TE.sort) {
    TE.sort.style.width = Math.round((val('sortW', TL ? TL.P.sort.w : 196) + val('m:sort:w')) * 100) / 100 + 'px';
    TE.sort.style.setProperty('--lo', String(val('sortL:o', 1)));
    TE.sort.style.setProperty('--lx', val('sortL:x') + 'px');
    const k = $('#qKey'); if (k) k.style.opacity = String(val('key:o', 1));
  }
  if (G) {
    // Liste/Raster: Inhalt verschoben, Fläche von unten skaliert (unterer Rand bleibt)
    const dy = val('list:dy'), bodyH = G.bodyBottom - G.bodyTop;
    lcin.style.transform = dy ? `translateY(${dy}px)` : '';
    lcbg.style.transform = dy ? `scaleY(${(bodyH - dy) / bodyH})` : '';
    gin.style.transform = dy ? `translateY(${dy}px)` : '';
    // Klammer der Leiste: Unterkante genau auf der aktuellen Oberkante der Liste, Inhalt gegenverschoben (steht still)
    const off = G.bodyTop + dy - G.bodyTopExp;
    tbclip.style.transform = off ? `translateY(${off}px)` : '';
    tb.style.transform = off ? `translateY(${-off}px)` : '';
  }
  // Lasche/Kapsel
  const pw = val('pill:w', 36), ph = val('pill:h', 18), pty = val('pill:ty', S.collapsed ? 0 : TAB_TY);
  pill.style.width = Math.round(pw * 10) / 10 + 'px'; pill.style.height = Math.round(ph * 10) / 10 + 'px';
  pill.style.borderRadius = (ph / 2) + 'px';
  pill.style.transform = pty ? `translateY(${Math.round(pty * 10) / 10}px)` : '';
  $('#pillAr').style.transform = `rotate(${val('pill:r')}deg)`;
  $('#cdv').style.opacity = String(val('cdv:o'));
  $('#pillHint').style.opacity = String(val('hint:o'));
  cpc.querySelectorAll('.cpi').forEach((n, i) => { const o = val('ci:' + i + ':o'), x = val('ci:' + i + ':x'); n.style.opacity = String(o); n.style.transform = x ? `translateX(${x}px)` : ''; });
  const cxo = val('cx:o'); cx.style.opacity = String(cxo); cx.classList.toggle('live', S.collapsed && cxo > 0.5); cx.tabIndex = S.collapsed && capState.x ? 0 : -1;
  tabw.style.opacity = String(val('tabw:o', 1));
  const ch = $('#chalo'), ch2 = $('#chalo2');
  ch.style.opacity = String(val('halo:o')); ch.style.width = (pw + 20) + 'px';
  ch2.style.opacity = String(val('halo2:o')); ch2.style.width = (pw + 8) + 'px';
  pill.classList.toggle('flt', S.collapsed && capState.flt);
  pill.setAttribute('aria-expanded', String(!S.collapsed));
  tabw.classList.toggle('col', S.collapsed);
  pill.setAttribute('aria-label', S.collapsed ? 'Werkzeugleiste einblenden' : 'Werkzeugleiste ausblenden');
  // Naht: das rechte Ende liegt immer an der linken Kante von Lasche bzw. Kapsel (auch während diese wächst oder schrumpft)
  seam.style.opacity = String(val('seam:o'));
  if (G) {
    const W0 = Math.max(1, G.L - 36), k = val('seam:sx') * Math.max(0, G.R - pw - PAD) / W0;
    seam.style.transform = `translateX(${Math.round((36 - pw) * 10) / 10}px) scaleX(${k})`;
  }
  const so = val('streak:o');
  streak.style.opacity = String(so);
  if (G && so > 0) {
    const capL = G.R - capState.w, p = val('streak:p');
    const rev = streak.classList.contains('rev');
    const x0 = rev ? capL - 20 : PAD - 160, x1 = rev ? PAD - 160 : capL - 120;
    streak.style.transform = `translateX(${x0 + (x1 - x0) * p}px)`;
  }
  for (const [k, g] of GH) { const v = val(k); g.style.opacity = String(v); if (v <= 0.001 && !AN.has(k)) { g.remove(); GH.delete(k); delete CH[k]; } }
}

/* ================= Einklappen / Ausklappen ================= */
const order = () => IDS.slice().sort((a, b) => (TL.P[a].x - TL.P[b].x) || (TL.P[a].y - TL.P[b].y));
function finals(collapsed) {
  for (const id of IDS) { set('w:' + id + ':o', collapsed ? 0 : 1); set('w:' + id + ':x', collapsed ? 16 : 0); set('w:' + id + ':y', collapsed ? -6 : 0); set('w:' + id + ':s', collapsed ? 0.94 : 1); }
  set('tb:o', 1); set('list:dy', 0); set('streak:o', 0); set('halo:o', 0); set('tabw:o', 1);
  finalsTab(collapsed);
  const f = collapsed && capState.flt;
  set('seam:sx', f ? 1 : 0); set('seam:o', f ? 1 : 0);
}
function setCollapsed(c, opt = {}) {
  if (S.collapsed === c) return;
  closeMenu(); hideTip(true);
  const hadFocus = tb.contains(document.activeElement);
  const oldVis = G.bodyTop + val('list:dy');
  S.collapsed = c; hoverTab = false;
  layout({ keepTab: true, keepListDy: true, animate: false });
  const dy = oldVis - G.bodyTop;
  const fx = opt.instant ? 'aus' : S.fx;
  if (fx === 'aus') { finals(c); apply(); }
  else if (fx === 'voll') { if (c) collapseVoll(dy); else expandVoll(dy, !!opt.fromSearch); }
  else (c ? collapseRed : expandRed)(dy);
  if (c && hadFocus) pill.focus({ preventScroll: true });
  readout();
  if (Clock.frozen == null) kick(); else { step(); apply(); }
}
/* Einfalten, Voll: alles endet spätestens nach 740 ms (danach laufen keine Uhren mehr) */
function collapseVoll(dy) {
  set('list:dy', dy); anim('list:dy', 0, { delay: 40, dur: 380, ease: E.expoOut6 });
  set('tb:o', 1);
  order().forEach((id, i) => {
    const d = i * 14, o = { delay: d, dur: 140, ease: E.cubicIn };
    anim('w:' + id + ':o', 0, o); anim('w:' + id + ':x', 16, o); anim('w:' + id + ':y', -6, o); anim('w:' + id + ':s', 0.94, o);
  });
  anim('hint:o', 0, { dur: 80 });
  // Lasche wird Kapsel: Breite ohne Überschwingen (CubicEase Out), nur Höhe und Lage federn (höchstens 0,4 px)
  anim('pill:w', capState.w, { delay: 120, dur: 260, ease: E.cubicOut });
  anim('pill:h', 24, { delay: 120, dur: 260, ease: E.spring }); anim('pill:ty', 0, { delay: 120, dur: 260, ease: E.spring });
  anim('pill:r', 180, { delay: 120, dur: 220, ease: E.cubicOut });
  anim('cdv:o', 1, { delay: 260, dur: 120 });
  for (let i = 0; i < capState.items * 2; i++) { set('ci:' + i + ':o', 0); set('ci:' + i + ':x', -6); anim('ci:' + i + ':o', 1, { delay: 300 + i * 15, dur: 120, ease: E.cubicOut }); anim('ci:' + i + ':x', 0, { delay: 300 + i * 15, dur: 150, ease: E.cubicOut }); }
  if (capState.x) { set('cx:o', 0); anim('cx:o', 1, { delay: 300 + capState.items * 30, dur: 120 }); }
  // „Zündung“: Lichtstreif läuft 340–640 auf der Kante zur Kapsel und verglüht dort bis 740;
  // der Hof blitzt 460–740 auf (Spitze bei 614, wenn der Streif ankommt), der große Schein 540–740
  streak.classList.remove('rev');
  set('streak:p', 0); anim('streak:p', 1, { delay: 340, dur: 300, ease: E.sineInOut });
  anim('streak:o', 0, { delay: 340, dur: 400, ease: E.lin, kf: [[0, 0], [0.3, 0.95], [0.72, 0.9], [1, 0]] });
  const rest = capState.flt ? 0.35 : 0;
  anim('halo2:o', rest, { delay: 460, dur: 280, ease: E.lin, kf: [[0, null], [0.55, 0.8], [1, rest]] });
  anim('halo:o', 0, { delay: 540, dur: 200, ease: E.lin, kf: [[0, 0], [0.4, 1], [1, 0]] });
  if (capState.flt) { set('seam:o', 1); set('seam:sx', val('seam:sx')); anim('seam:sx', 1, { delay: 340, dur: 400, ease: E.spline }); }
}
/* Ausfalten, Voll (≤ 620 ms). fromSearch (Strg+F): die Elemente entfalten sich von der Suche aus statt von der Lasche */
function expandVoll(dy, fromSearch) {
  set('list:dy', dy); anim('list:dy', 0, { dur: 380, ease: E.expoOut6 });
  set('tb:o', 1);
  // Naht zieht sich in die Kapsel zurück; ihr rechtes Ende bleibt an der schrumpfenden Kapsel (apply)
  if (val('seam:sx') > 0) anim('seam:sx', 0, { dur: 200, ease: E.cubicOut }); else set('seam:sx', 0);
  streak.classList.add('rev');
  set('streak:p', 0); anim('streak:p', 1, { dur: 320, ease: E.sineInOut });
  anim('streak:o', 0, { dur: 320, ease: E.lin, kf: [[0, 0], [0.3, 0.9], [1, 0]] });
  for (let i = 0; i < 12; i++) { anim('ci:' + i + ':o', 0, { dur: 90 }); anim('ci:' + i + ':x', -6, { dur: 120 }); }
  anim('cx:o', 0, { dur: 80 }); anim('cdv:o', 0, { dur: 100 });
  anim('pill:w', 36, { delay: 60, dur: 200, ease: E.cubicOut });
  anim('pill:h', 18, { delay: 60, dur: 200, ease: E.spring }); anim('pill:ty', TAB_TY, { delay: 60, dur: 200, ease: E.spring });
  anim('pill:r', 0, { delay: 60, dur: 220, ease: E.cubicOut });
  anim('halo2:o', 0, { dur: 200 }); anim('halo:o', 0, { dur: 120 });
  (fromSearch ? order() : order().reverse()).forEach((id, i) => {
    const d = 120 + i * 16;
    anim('w:' + id + ':o', 1, { delay: d, dur: 180, ease: E.cubicOut });
    anim('w:' + id + ':x', 0, { delay: d, dur: 300, ease: E.spring }); anim('w:' + id + ':y', 0, { delay: d, dur: 300, ease: E.spring }); anim('w:' + id + ':s', 1, { delay: d, dur: 300, ease: E.spring });
  });
}
/* Reduziert (≤ 300 ms): Lasche und Kapsel nacheinander überblendet (alt 0–90, neu 60–150), damit sich Pfeil und Zahl nicht mischen */
function collapseRed(dy) {
  set('list:dy', dy); anim('list:dy', 0, { dur: 200, ease: E.cubicOut });
  for (const id of IDS) {
    anim('w:' + id + ':o', 0, { dur: 120, ease: E.cubicOut });
    for (const [p, v] of [['x', 16], ['y', -6], ['s', 0.94]]) anim('w:' + id + ':' + p, v, { delay: 120, dur: 1 });
  }
  ghost(tabw, 90);
  finalsTab(true); set('tabw:o', 0); anim('tabw:o', 1, { delay: 60, dur: 90, ease: E.lin });
  if (capState.flt) { set('seam:sx', 1); set('seam:o', 0); anim('seam:o', 1, { dur: 150, ease: E.lin }); }
  set('streak:o', 0);
}
function expandRed(dy) {
  set('list:dy', dy); anim('list:dy', 0, { dur: 200, ease: E.cubicOut });
  // Leiste blendet erst ab 60 ms ein, wenn die Liste schon Platz gemacht hat
  for (const id of IDS) { for (const [p, v] of [['x', 0], ['y', 0], ['s', 1]]) set('w:' + id + ':' + p, v); anim('w:' + id + ':o', 1, { delay: 60, dur: 120, ease: E.cubicOut }); }
  ghost(tabw, 90);
  finalsTab(false); set('tabw:o', 0); anim('tabw:o', 1, { delay: 60, dur: 90, ease: E.lin });
  if (val('seam:o') > 0) anim('seam:o', 0, { dur: 150, ease: E.lin }); else set('seam:o', 0);
  set('streak:o', 0);
}
function finalsTab(c) {
  set('pill:w', c ? capState.w : 36); set('pill:h', c ? 24 : 18); set('pill:ty', c ? 0 : TAB_TY); set('pill:r', c ? 180 : 0); set('cdv:o', c ? 1 : 0); set('hint:o', 0);
  for (let i = 0; i < 12; i++) { set('ci:' + i + ':o', c ? 1 : 0); set('ci:' + i + ':x', c ? 0 : -6); }
  set('cx:o', c && capState.x ? 1 : 0); set('halo2:o', c && capState.flt ? 0.35 : 0); set('halo:o', 0);
}
function toggleCollapsed() { setCollapsed(!S.collapsed); }

/* Lasche: beim Überfahren wächst sie nach links zu „Leiste ausblenden · Strg+Umschalt+F“ (160 ms) */
let hoverTab = false;
function tabHover(on) {
  if (S.collapsed || ['pill:h', 'pill:r', 'pill:ty'].some(k => AN.has(k)) && on) return;
  hoverTab = on;
  const hw = 28 + measureHint() + 12;
  const w = on ? hw : 36;
  if (S.fx === 'voll') { anim('pill:w', w, { dur: 160, ease: E.cubicOut }); anim('hint:o', on ? 1 : 0, { delay: on ? 50 : 0, dur: on ? 110 : 60, ease: E.cubicOut }); }
  else if (S.fx === 'reduziert') { set('pill:w', w); anim('hint:o', on ? 1 : 0, { dur: 100, ease: E.lin }); }
  else { set('pill:w', w); set('hint:o', on ? 1 : 0); }
  if (Clock.frozen != null) { step(); apply(); }
}
function measureHint() { return Math.ceil($('#pillHint').getBoundingClientRect().width / (stageScale || 1)); }

/* ================= Filter, Suche, Ansicht ================= */
function pop(el) {
  if (S.fx !== 'voll' || Clock.frozen != null) return;
  const ib = el.querySelector('.ib'); if (!ib) return;
  ib.animate([{ transform: 'scale(.88)' }, { transform: 'scale(1.06)', offset: 0.55 }, { transform: 'scale(1)' }], { duration: 240 / S.speed, easing: 'ease-out' });
}
function toggleFilter(k) {
  S[k] = !S[k];
  if (S[k]) pop(TE[k]);
  refresh();
}
function setQuery(q) { S.q = q; refresh(); }
function resetFilters() { S.q = ''; S.src = 'Alle'; S.fAuto = S.fNew = S.fHdr = false; refresh(); }
function setView(v) { if (v === 'regal') { showNote('Regal ist in dieser Vorschau nicht nachgebaut.'); return; } S.view = v; rowsKey = ''; layout({ animate: false }); syncPanel(); }
function refresh() {
  rowsKey = '';
  const wasW = capState.w;
  layout({ animate: false, keepTab: true });
  if (S.collapsed) {
    if (wasW !== capState.w && S.fx !== 'aus') anim('pill:w', capState.w, { dur: 160, ease: E.cubicOut }); else if (!AN.has('pill:w')) set('pill:w', capState.w);
    for (let i = 0; i < 12; i++) { set('ci:' + i + ':o', 1); set('ci:' + i + ':x', 0); }
    set('cdv:o', 1); set('cx:o', capState.x ? 1 : 0);
    const rest = capState.flt ? 0.35 : 0; if (S.fx === 'aus') set('halo2:o', rest); else anim('halo2:o', rest, { dur: 200 });
    const want = capState.flt ? 1 : 0;
    if (S.fx === 'voll' && val('seam:sx') !== want) { if (want) { set('seam:o', 1); anim('seam:sx', 1, { dur: 400, ease: E.spline }); } else anim('seam:sx', 0, { dur: 200, ease: E.cubicIn }); }
    else if (S.fx === 'reduziert') { set('seam:sx', 1); anim('seam:o', want, { dur: 150, ease: E.lin }); }
    else if (S.fx === 'aus') { set('seam:sx', want); set('seam:o', want); }
  }
  syncPanel(); apply(); kick();
  if (tipFor) showTip(tipFor, true);
}
function focusSearch() {
  if (S.collapsed) setCollapsed(false, { fromSearch: true });   // Entscheidung 3: Strg+F klappt dauerhaft aus; die Suche entfaltet sich zuerst
  const q = $('#q'); noFocusTip = true; q.focus({ preventScroll: true }); q.select(); noFocusTip = false;
}
let noFocusTip = false;                          // nach Strg+F kein Tooltip an der Suche (das Kürzel ist ja bekannt)

/* ================= Tooltips (Lib.ToolTip): 1 s, Nachbar sofort innerhalb 400 ms ================= */
let tipTimer = 0, tipFor = null, tipHiddenAt = -1e9;
const tf = (n, on) => on ? `<b>Filter an</b> · ${n} von ${D.total} · Klick hebt ihn auf` : `${n} Spiele · Filter aus`;
const TIPS = {
  search: () => ({ t: 'Suchen', k: 'Strg+F', d: 'Nach Namen filtern. Esc leert die Suche.', f: S.q ? `${filtered().length} Treffer für „${esc(S.q)}“` : `${D.total} Spiele` }),
  source: () => ({ t: 'Quelle', d: 'Nur Spiele eines Launchers zeigen.', f: S.src === 'Alle' ? `Alle Quellen · ${D.total} Spiele` : `${esc((SRC[S.src] || [S.src])[0])} · ${D.bySrc[S.src] || 0} Spiele` }),
  sort: () => ({ t: 'Sortierung', d: TL && TL.sortIcon ? `${SORTS[S.sort][0]}. Im schmalen Fenster nur als Symbol.` : 'Reihenfolge der Liste; ein Klick auf den Spaltenkopf geht auch.', f: SORTS[S.sort][0] }),
  fAuto: () => ({ t: 'HDR automatisch', k: 'Alt+A', d: 'Nur Spiele, für die HDRpilot HDR beim Start einschaltet.', f: tf(D.cAuto, S.fAuto) }),
  fNew: () => ({ t: 'Neu', k: 'Alt+N', d: 'Nur Spiele, die seit dem letzten Ansehen dazugekommen sind.', f: tf(D.cNew, S.fNew) }),
  fHdr: () => ({ t: 'HDR-fähig', k: 'Alt+H', d: 'Nur Spiele mit HDR laut PCGamingWiki (auch eingeschränkt).', f: tf(D.cHdr, S.fHdr) }),
  vList: () => ({ t: 'Liste', k: 'Strg+1', d: 'Alle Spiele als Liste mit Spalten.' }),
  vGrid: () => ({ t: 'Raster', k: 'Strg+2', d: 'Cover als Kacheln.' }),
  vShelf: () => ({ t: 'Regal', k: 'Strg+3', d: 'Eine große Kachelreihe unter dem Titelbild.', f: 'in dieser Vorschau nicht nachgebaut' }),
  rescan: () => ({ t: 'Neu einlesen', d: 'Alle Launcher erneut nach Spielen durchsuchen.' }),
  seen: () => ({ t: 'Alle als gesehen markieren', d: 'Entfernt die Markierung „Neu“ bei allen Spielen.', f: `${D.cNew} neu` }),
  add: () => ({ t: 'Spiel hinzufügen', d: 'Ein Spiel von Hand aufnehmen (EXE oder Verknüpfung).' }),
  pill: () => S.collapsed
    ? { t: 'Leiste einblenden', k: 'Strg+Umschalt+F', d: capState.flt ? 'Gefiltert: ' + filterText() : 'Keine Filter aktiv.', f: 'Klick blendet ein · Rechtsklick: Menü' }
    : null,
  cx: () => ({ t: 'Suche und Filter zurücksetzen', d: 'Zeigt wieder alle ' + D.total + ' Spiele.' }),
};
function filterText() {
  const p = [];
  if (S.q.trim()) p.push('Suche „' + esc(S.q.trim()) + '“');
  if (S.src !== 'Alle') p.push(esc((SRC[S.src] || [S.src])[0]));
  if (S.fAuto) p.push('HDR automatisch'); if (S.fNew) p.push('Neu'); if (S.fHdr) p.push('HDR-fähig');
  return p.join(', ') + ' – ' + filtered().length + ' von ' + D.total;
}
function tipTarget(el) { return el && el.closest && el.closest('[data-tip], #pill, #cx'); }
function tipKey(el) { return el.id === 'pill' ? 'pill' : el.id === 'cx' ? 'cx' : el.dataset.tip; }
function showTip(el, now) {
  const key = tipKey(el), f = TIPS[key] && TIPS[key]();
  if (!f) { hideTip(); return; }
  tipFor = el;
  tip.innerHTML = `<div class="tt">${f.t}${f.k ? `<span class="kbd">${f.k}</span>` : ''}</div>${f.d ? `<div class="te2">${f.d}</div>` : ''}${f.f ? `<div class="tf">${f.f}</div>` : ''}`;
  // unten mittig, 8 px Abstand, im Fenster gehalten
  const r = rectInWin(el);
  tip.style.left = '0px'; tip.style.top = '0px';
  const tw = tip.offsetWidth, x = clamp(Math.round(r.x + r.w / 2 - tw / 2), 8, G.Wi - 8 - tw), y = Math.round(r.y + r.h + 8);
  tip.style.left = x + 'px'; tip.style.top = y + 'px';
  if (now || S.fx === 'aus') { tip.classList.add('vis'); return; }
  tip.classList.remove('vis'); void tip.offsetWidth; tip.classList.add('vis');
}
function hideTip(force) { clearTimeout(tipTimer); if (tipFor) tipHiddenAt = performance.now(); tipFor = null; tip.classList.remove('vis'); if (force) tipHiddenAt = -1e9; }
function rectInWin(el) {
  const a = el.getBoundingClientRect(), b = win.getBoundingClientRect(), s = stageScale || 1;
  return { x: (a.left - b.left) / s - 1, y: (a.top - b.top) / s - 1, w: a.width / s, h: a.height / s };
}
win.addEventListener('pointerover', e => {
  const t = tipTarget(e.target); if (!t || t === tipFor) return;
  if (e.relatedTarget && t.contains(e.relatedTarget)) return;
  clearTimeout(tipTimer);
  const quick = tipFor || performance.now() - tipHiddenAt < 400;
  if (tipFor) { tipFor = null; tip.classList.remove('vis'); }
  if (quick) showTip(t, true); else tipTimer = setTimeout(() => showTip(t), 1000);
});
win.addEventListener('pointerout', e => {
  const t = tipTarget(e.target); if (!t) return;
  if (e.relatedTarget && t.contains(e.relatedTarget)) return;
  if (t === tipFor || !tipFor) hideTip();
});
win.addEventListener('focusin', e => { const t = tipTarget(e.target); if (t && !noFocusTip && Clock.frozen == null && e.target.matches(':focus-visible')) { clearTimeout(tipTimer); tipTimer = setTimeout(() => showTip(t), 1000); } });
win.addEventListener('focusout', () => hideTip());

/* ================= Menüs (Kontextmenü, Auswahllisten) ================= */
let menuFor = null;
/* alignRight: x ist die rechte Kante (Menü rechtsbündig an Lasche bzw. Kapsel) */
function openMenu(items, x, y, owner, alignRight) {
  menuFor = owner || null;
  menu.innerHTML = items.map((it, i) => it === '-' ? '<div class="msep"></div>' : `<div class="mi${it.cur ? ' cur' : ''}" data-i="${i}" role="menuitem">${it.ck !== undefined ? `<span class="ck gl">${it.ck ? '' : ''}</span>` : ''}${it.dot ? `<i class="sd" style="background:${it.dot}"></i>` : ''}<span>${it.t}</span><span class="sp"></span>${it.n != null ? `<span class="n">${it.n}</span>` : ''}${it.k ? `<span class="kbd">${it.k}</span>` : ''}</div>`).join('');
  menu.hidden = false;
  const mw = menu.offsetWidth, mh = menu.offsetHeight;
  menu.style.left = clamp(Math.round(alignRight ? x - mw : x), 8, G.Wi - mw - 8) + 'px'; menu.style.top = clamp(Math.round(y), 40, G.Hi - mh - 8) + 'px';
  menu.querySelectorAll('.mi').forEach(n => n.onclick = ev => { ev.stopPropagation(); const it = items[+n.dataset.i]; closeMenu(); it.do && it.do(); });
  if (owner) owner.classList.add('open');
  hideTip(true);
}
function closeMenu() { if (menu.hidden) return; menu.hidden = true; if (menuFor) menuFor.classList.remove('open'); menuFor = null; }
function ctxItems() {
  return [
    { t: S.collapsed ? 'Leiste einblenden' : 'Leiste ausblenden', k: 'Strg+Umschalt+F', do: toggleCollapsed }, '-',
    { t: 'HDR automatisch', ck: S.fAuto, n: D.cAuto, k: 'Alt+A', do: () => toggleFilter('fAuto') },
    { t: 'Neu', ck: S.fNew, n: D.cNew, k: 'Alt+N', do: () => toggleFilter('fNew') },
    { t: 'HDR-fähig', ck: S.fHdr, n: D.cHdr, k: 'Alt+H', do: () => toggleFilter('fHdr') }, '-',
    { t: 'Alle Filter zurücksetzen', do: resetFilters },
  ];
}
function winPoint(e) { const b = win.getBoundingClientRect(), s = stageScale || 1; return { x: (e.clientX - b.left) / s - 1, y: (e.clientY - b.top) / s - 1 }; }
win.addEventListener('contextmenu', e => {
  const onTb = e.target === tb || e.target.closest('#tabw');
  if (!onTb) return;
  e.preventDefault();
  const p = winPoint(e); openMenu(ctxItems(), p.x, p.y);
});
function comboMenu(which) {
  const el = TE[which], r = rectInWin(el);
  if (menuFor === el) { closeMenu(); return; }
  let items;
  if (which === 'source') {
    items = [{ t: 'Alle', dot: '#8D92A8', n: D.total, cur: S.src === 'Alle', do: () => { S.src = 'Alle'; refresh(); } }]
      .concat(Object.keys(D.bySrc).sort((a, b) => D.bySrc[b] - D.bySrc[a]).map(k => ({ t: esc((SRC[k] || [k])[0]), dot: (SRC[k] || [0, '#C8CCD8'])[1], n: D.bySrc[k], cur: S.src === k, do: () => { S.src = k; refresh(); } })));
  } else items = Object.keys(SORTS).map(k => ({ t: SORTS[k][0], cur: S.sort === k, do: () => { S.sort = k; refresh(); } }));
  openMenu(items, r.x, r.y + r.h + 8, el);
  menu.style.minWidth = Math.max(236, r.w) + 'px';
}
document.addEventListener('pointerdown', e => { if (!menu.hidden && !menu.contains(e.target) && !(menuFor && menuFor.contains(e.target))) closeMenu(); }, true);

/* ================= Ereignisse im Fenster ================= */
function wireWindow() {
  const q = $('#q');
  q.addEventListener('input', () => { S.q = q.value; refresh(); });
  q.addEventListener('focus', () => TE.search.classList.add('foc'));
  q.addEventListener('blur', () => TE.search.classList.remove('foc'));
  q.addEventListener('keydown', e => { if (e.key === 'Escape') { if (S.q) { e.stopPropagation(); setQuery(''); } else q.blur(); } });
  $('#qX').onclick = () => { setQuery(''); q.focus(); };
  TE.source.onclick = () => comboMenu('source');
  TE.sort.onclick = () => comboMenu('sort');
  TE.fAuto.onclick = () => toggleFilter('fAuto');
  TE.fNew.onclick = () => toggleFilter('fNew');
  TE.fHdr.onclick = () => toggleFilter('fHdr');
  TE.view.querySelectorAll('.sb').forEach(b => b.onclick = () => setView(b.dataset.v));
  // Optionsgruppe Ansicht: Pfeiltasten, Pos1, Ende (ein Tab-Halt)
  TE.view.addEventListener('keydown', e => {
    const bs = [...TE.view.querySelectorAll('.sb')], i = bs.indexOf(document.activeElement);
    if (i < 0) return;
    const j = { ArrowRight: i + 1, ArrowDown: i + 1, ArrowLeft: i - 1, ArrowUp: i - 1, Home: 0, End: bs.length - 1 }[e.key];
    if (j == null) return;
    e.preventDefault(); const b = bs[(j + bs.length) % bs.length]; b.focus(); b.click();
  });
  TE.rescan.onclick = () => { const g = TE.rescan.querySelector('.gi'); if (S.fx !== 'aus') g.animate([{ transform: 'rotate(0)' }, { transform: 'rotate(360deg)' }], { duration: 700 / S.speed, easing: 'cubic-bezier(.4,0,.2,1)' }); };
  TE.seen.onclick = () => showNote('In der Vorschau ohne Wirkung.');
  TE.add.onclick = () => showNote('In der Vorschau ohne Wirkung.');
  pill.onclick = () => { hideTip(true); toggleCollapsed(); };
  $('#tabHit').onclick = () => { hideTip(true); toggleCollapsed(); };
  $('#tabHit').addEventListener('pointerenter', () => tabHover(true));
  $('#tabHit').addEventListener('pointerleave', e => { if (e.relatedTarget !== pill) tabHover(false); });
  pill.addEventListener('keydown', e => { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); toggleCollapsed(); } });
  pill.addEventListener('pointerenter', () => tabHover(true));
  pill.addEventListener('pointerleave', e => { if (e.relatedTarget && e.relatedTarget.id === 'tabHit') return; tabHover(false); });
  cx.onclick = e => { e.stopPropagation(); hideTip(true); resetFilters(); };
  grip.addEventListener('pointerenter', () => grip.classList.add('hover'));
  grip.addEventListener('pointerleave', () => grip.classList.remove('hover'));
}
let noteT = 0;
function showNote(t) { const r = $('#readout'); const n = document.createElement('span'); n.className = 'bad'; n.textContent = t; r.appendChild(n); clearTimeout(noteT); noteT = setTimeout(readout, 2200); }

/* ================= Tastatur (ganze Seite) ================= */
document.addEventListener('keydown', e => {
  const k = e.key.toLowerCase(), ctrl = e.ctrlKey || e.metaKey;
  if (ctrl && e.shiftKey && k === 'f' || ctrl && e.key === 'F1') { e.preventDefault(); toggleCollapsed(); return; }
  if (ctrl && !e.shiftKey && k === 'f') { e.preventDefault(); focusSearch(); return; }
  if (e.altKey && !ctrl && ['a', 'n', 'h'].includes(k)) { e.preventDefault(); toggleFilter(k === 'a' ? 'fAuto' : k === 'n' ? 'fNew' : 'fHdr'); return; }
  if (ctrl && (k === '1' || k === '2')) { e.preventDefault(); setView(k === '1' ? 'liste' : 'raster'); return; }
  if (e.key === 'Escape') { if (!menu.hidden) { closeMenu(); return; } hideTip(true); }
  if (e.shiftKey && e.key === 'F10' && (document.activeElement === pill || tb.contains(document.activeElement))) { e.preventDefault(); const r = rectInWin(pill); openMenu(ctxItems(), r.x + r.w, r.y + r.h + 6, null, true); }
});

/* ================= Bühne einpassen ================= */
let stageFit = true;
function fitStage() {
  const avail = Math.max(320, document.documentElement.clientWidth - 32 - (S.shot ? 0 : 18));
  stageScale = S.shot || !S.fit ? 1 : Math.min(1, avail / S.W);
  if (dragLock) stageScale = dragLock;
  stage.style.transform = stageScale === 1 ? '' : `scale(${stageScale})`;
  stageBox.style.width = Math.ceil(S.W * stageScale) + 'px'; stageBox.style.height = Math.ceil(S.H * stageScale) + 'px';
  sizer.style.left = Math.ceil(S.W * stageScale) + 'px'; sizer.style.height = Math.ceil(S.H * stageScale) + 'px';
}
window.addEventListener('resize', () => { fitStage(); });

/* ================= Bedienfeld ================= */
/* Voreinstellungen wie die Zeilen der Breitentabelle im Dokument (Bildschirm @ Skalierung → Fenster in DIP); 100 % ausdrücklich markiert */
const PRESETS = [
  [1000, 620, '1000×620 kleinstes'], [1164, 727, '1280×800 @110 %'], [1242, 698, '1366×768 @110 %'], [1280, 800, '1280×800 @100 %'],
  [1366, 768, '1366×768 @100 %'], [1455, 909, '1600×1000 @110 %'], [1536, 864, '1920×1080 @125 %'], [1600, 1000, 'Startfenster 1600×1000'],
  [1745, 982, '1920×1080 @110 %'], [3127, 1309, '3440×1440 @110 %'], [3440, 1440, '3440×1440 @100 %'],
];
function seg(id, cur) { document.querySelectorAll('#' + id + ' button').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.v === cur))); }
function syncPanel() {
  $('#wRange').value = S.W;
  $('#wOut').textContent = `${S.W} × ${S.H} DIP`;
  seg('pTheme', S.theme); seg('pFx', S.fx); seg('pView', S.view); seg('pFit', S.fit ? 'fit' : '1'); seg('pSpd', String(S.speed));
  document.querySelectorAll('#presets button').forEach(b => b.classList.toggle('cur', +b.dataset.w === S.W && +b.dataset.h === S.H));
  $('#pAuto').setAttribute('aria-pressed', String(S.fAuto)); $('#pNew').setAttribute('aria-pressed', String(S.fNew)); $('#pHdr').setAttribute('aria-pressed', String(S.fHdr));
}
function wirePanel() {
  const Lof = w => (w - 2) - 88 - Math.round(clamp(0.23 * (w - 2), 340, 396));
  $('#presets').innerHTML = PRESETS.map(p => `<button data-w="${p[0]}" data-h="${p[1]}" title="${p[0]} × ${p[1]} DIP · Liste L ${Lof(p[0])}">${p[2]}</button>`).join('');
  $('#presets').onclick = e => { const b = e.target.closest('button'); if (!b) return; S.dragging = false; S.W = +b.dataset.w; S.H = +b.dataset.h; layout({ animate: false }); syncPanel(); };
  const r = $('#wRange');
  r.addEventListener('pointerdown', () => { S.dragging = true; });
  window.addEventListener('pointerup', () => { if (S.dragging && !sizerDrag) { S.dragging = false; readout(); } });
  r.addEventListener('input', () => { S.W = +r.value; layout(); syncPanel(); });
  r.addEventListener('keydown', () => { S.dragging = false; });
  const bindSeg = (id, fn) => $('#' + id).addEventListener('click', e => { const b = e.target.closest('button'); if (b) fn(b.dataset.v); });
  bindSeg('pTheme', v => { S.theme = v; palette(); rowsKey = ''; layout({ animate: false }); syncPanel(); });
  bindSeg('pFx', v => { S.fx = v; layout({ animate: false }); syncPanel(); });
  bindSeg('pView', v => setView(v));
  bindSeg('pFit', v => { S.fit = v === 'fit'; fitStage(); syncPanel(); });
  bindSeg('pSpd', v => { S.speed = +v; Clock.setSpeed(S.speed); syncPanel(); });
  $('#pToggle').onclick = toggleCollapsed;
  $('#pFind').onclick = focusSearch;
  $('#pAuto').onclick = () => toggleFilter('fAuto'); $('#pNew').onclick = () => toggleFilter('fNew'); $('#pHdr').onclick = () => toggleFilter('fHdr');
  $('#pQ').addEventListener('input', e => setQuery(e.target.value));
  const ps = $('#pSrc');
  ps.innerHTML = '<option value="Alle">Quelle: Alle</option>' + Object.keys(D.bySrc).map(k => `<option value="${k}">Quelle: ${esc((SRC[k] || [k])[0])}</option>`).join('');
  ps.onchange = () => { S.src = ps.value; refresh(); };
  // Griff rechts neben dem Fenster: Fensterrand ziehen
  sizer.addEventListener('pointerdown', e => {
    sizerDrag = { x: e.clientX, w: S.W }; dragLock = stageScale; S.dragging = true; sizer.classList.add('on'); sizer.setPointerCapture(e.pointerId);
  });
  sizer.addEventListener('pointermove', e => { if (!sizerDrag) return; S.W = clamp(Math.round(sizerDrag.w + (e.clientX - sizerDrag.x) / dragLock), 1000, 3440); layout(); syncPanel(); });
  const end = () => { if (!sizerDrag) return; sizerDrag = null; dragLock = 0; S.dragging = false; sizer.classList.remove('on'); fitStage(); readout(); };
  sizer.addEventListener('pointerup', end); sizer.addEventListener('pointercancel', end);
}
let sizerDrag = null, dragLock = 0;

/* ================= Messung (für Abnahme und Bildschirmfotos) ================= */
function measure() {
  const s = stageScale || 1, b = win.getBoundingClientRect();
  const y = el => (el.getBoundingClientRect().top - b.top) / s - 1, yb = el => (el.getBoundingClientRect().bottom - b.top) / s - 1;
  const edge = Math.round(yb(hero) * 100) / 100;
  const tbT = Math.min(...IDS.filter(i => TL.P[i].y === 0).map(i => G.tbTop + TL.P[i].y));
  const tbB = G.tbTop + G.th;
  const listT = G.bodyTop + (S.view === 'raster' ? 12 : 0);
  return {
    W: S.W, H: S.H, L: G.L, pane: G.paneW, rows: TL.rows, stage: TL.stage, sortIcon: TL.sortIcon, keycap: TL.key,
    search: TL.P.search.w, source: TL.P.source.w, sort: Math.round(val('sortW', TL.P.sort.w)), free: TL.free, groupGap: TL.rows === 1 ? TL.P.view.x - (TL.P.fHdr.x + 40) : null,
    edge, toolbarTop: tbT, toolbarBottom: tbB, bodyTop: listT,
    airAbove: S.collapsed ? null : tbT - edge, airBelow: S.collapsed ? null : listT - tbB,
    collapsed: S.collapsed, capsule: S.collapsed ? { w: capState.w, top: G.edge - 12, bottom: G.edge + 12, airBelow: listT - (G.edge + 12) } : null,
    domSearchTop: Math.round(y(TE.search) * 100) / 100, domListTop: Math.round(y(lcbg) * 100) / 100,
  };
}
function readout() {
  if (!G) return;
  const m = measure();
  const ok = v => `<b class="${v === (S.view === 'raster' ? 22 : 10) ? 'ok' : 'bad'}">${v}</b>`;
  $('#readout').innerHTML = [
    `Liste L <b>${m.L}</b> DIP · Detailbereich ${m.pane}`,
    `Leiste: <b>${m.rows === 1 ? 'eine Zeile' : 'zwei Zeilen'}</b> · ${m.stage}`,
    `Suche ${m.search} · Quelle ${m.source} · Sortierung ${m.sort}${m.rows === 1 ? ` · Abstand Filter–Ansicht ${m.groupGap}` : ''}${m.keycap ? '' : ' · ohne Strg+F-Kappe'}`,
    S.collapsed ? `eingeklappt: Kapsel ${m.capsule.w} breit, Luft darunter ${ok(m.capsule.airBelow)}` : `Luft über der Leiste (zur Bildkante) ${ok(m.airAbove)} · darunter ${ok(m.airBelow)}`,
    S.dragging ? '<b>ziehen</b> (Hysterese aktiv)' : '',
  ].filter(Boolean).map(t => `<span>${t}</span>`).join('');
  window.leiste.last = m;
}

/* ================= Hash-Parameter (Debug) =================
  w, h            Fenster in DIP (Standard 1745 × 982 = 1920 × 1080 bei 110 %)
  theme           dunkel | hell          fx  voll | reduziert | aus        view  liste | raster
  shot=1          nur das Fenster, Maßstab 1:1 (für Bildschirmfotos); schreibt „MESS {…}“ in die Konsole
  fit=0           Maßstab 1:1 statt einpassen
  game=<text>     Spiel im Titelbild (Teil des Namens)
  q=<text> src=<Quelle> auto=1 neu=1 hdr=1      Suche, Quelle, Filter
  collapsed=1     Leiste eingeklappt
  anim=collapse|expand|drag   t=<ms>  Bewegung starten und bei t einfrieren (ohne t: live abspielen)
  from=<w>        bei anim=drag: Ausgangsbreite (gezogen wird nach w, mit Hysterese und Morph)
  tip=<id>        Tooltip sofort zeigen: search source sort fAuto fNew fHdr vList vGrid vShelf rescan seen add pill cx
  hover=tab|grip|<id>   Zustand „Maus darüber“      focus=search|grip|pill|<id>   Tastaturfokus
  ctx=1           Kontextmenü an der Lasche/Kapsel      menu=source|sort   Auswahlliste offen
  do=ctrlf|toggle|alta|altn|alth|reset   Bedienung auslösen (mehrere mit Komma; „aktion@ms“ später), z. B.
                  collapsed=1&do=ctrlf&t=200   oder   do=toggle,toggle@250&t=400 (Einklappen nach 250 ms umgekehrt)
  speed=0.25      Zeitlupe
*/
function hashParams() { const p = new URLSearchParams(location.hash.replace(/^#/, '')); const o = {}; for (const [k, v] of p) o[k] = v; return o; }
function applyHash() {
  const h = hashParams();
  const num = (k, d) => (h[k] != null && h[k] !== '' && !isNaN(+h[k]) ? +h[k] : d);
  S.shot = h.shot === '1'; document.body.classList.toggle('shot', S.shot);
  S.W = clamp(Math.round(num('w', S.W)), 1000, 3440); S.H = clamp(Math.round(num('h', S.H)), 620, 2160);
  if (h.theme) S.theme = h.theme === 'hell' || h.theme === 'light' ? 'hell' : 'dunkel';
  if (h.fx) S.fx = ({ voll: 'voll', reduziert: 'reduziert', red: 'reduziert', aus: 'aus' })[h.fx] || 'voll';
  if (h.view) S.view = h.view === 'raster' ? 'raster' : 'liste';
  if (h.fit != null) S.fit = h.fit !== '0';
  if (h.speed) { S.speed = num('speed', 1); Clock.setSpeed(S.speed); }
  if (h.game) S.game = h.game;
  if (h.q != null) S.q = h.q;
  if (h.src) S.src = h.src;
  S.fAuto = h.auto === '1'; S.fNew = h.neu === '1'; S.fHdr = h.hdr === '1';
  S.sel = heroGame();
  buildHero();
  palette();
  const a = h.anim, t = h.t != null ? num('t', 0) : null;
  Clock.frozen = null; AN.clear(); for (const k of Object.keys(CH)) delete CH[k];
  if (t != null) Clock.frozen = 0;
  S.dragging = false; S.hy = { two: false, sortIcon: false, key: true };
  if (a === 'drag') {
    const to = S.W; S.W = clamp(Math.round(num('from', to + 60)), 1000, 3440);
    S.collapsed = h.collapsed === '1'; rowsKey = ''; layout({ animate: false }); finals(S.collapsed); apply();
    S.dragging = true; S.W = to; layout();
  } else if (a === 'collapse' || a === 'expand') {
    S.collapsed = a === 'expand'; rowsKey = ''; layout({ animate: false }); finals(S.collapsed); apply();
    setCollapsed(a === 'collapse');
  } else {
    S.collapsed = h.collapsed === '1'; rowsKey = ''; layout({ animate: false }); finals(S.collapsed);
  }
  // Skriptierte Bedienung (wie Tastendruck bzw. Klick): „aktion“ sofort, „aktion@ms“ nach ms (Unterbrechen prüfen)
  const act = d => {
    if (d === 'ctrlf') focusSearch();
    else if (d === 'toggle') toggleCollapsed();
    else if (d === 'alta') toggleFilter('fAuto'); else if (d === 'altn') toggleFilter('fNew'); else if (d === 'alth') toggleFilter('fHdr');
    else if (d === 'reset') resetFilters();
  };
  const later = [];
  if (h.do) for (const d of h.do.split(',')) { const [n, at] = d.split('@'); if (at == null) act(n); else later.push([+at, n]); }
  later.sort((a, b) => a[0] - b[0]);
  if (t != null) {
    for (const [at, n] of later) if (at <= t) { Clock.frozen = at; step(); apply(); act(n); }
    Clock.frozen = t; step(); apply();
  } else for (const [at, n] of later) setTimeout(() => act(n), at / S.speed);
  apply();
  syncPanel(); readout();
  // Zustände für Bildschirmfotos
  if (h.hover) {
    if (h.hover === 'tab') { hoverTab = false; tabHover(true); if (Clock.frozen != null) { Clock.frozen += 1000; step(); apply(); } }
    else if (h.hover === 'grip') grip.classList.add('hover');
    else if (TE[h.hover]) TE[h.hover].classList.add('hover');
  }
  if (h.focus) {
    const el = h.focus === 'search' ? $('#q') : h.focus === 'grip' ? grip : h.focus === 'pill' ? pill : (TE[h.focus] || null);
    if (el) { el.focus({ preventScroll: true }); (TE[h.focus] || el).classList.add('kfocus'); }
  }
  if (h.tip) {
    const el = h.tip === 'pill' ? pill : h.tip === 'cx' ? cx : (TE[h.tip] || win.querySelector(`[data-tip="${h.tip}"]`));
    if (el) { if (TE[h.tip]) TE[h.tip].classList.add('hover'); showTip(el, true); }
  }
  if (h.ctx === '1') { const r = rectInWin(pill); openMenu(ctxItems(), r.x + r.w, r.y + r.h + 6, null, true); }
  if (h.menu === 'source' || h.menu === 'sort') comboMenu(h.menu);
  if (S.shot) {
    const m = measure();
    console.log('MESS ' + JSON.stringify(m));
    document.title = 'MESS ' + JSON.stringify(m);
    if (h.dump) setTimeout(() => {   // Zustand nach Ablauf (live) für die Fehlersuche
      const o = { collapsed: S.collapsed, running: [...AN.keys()], ch: {} };
      for (const k of Object.keys(CH)) if (/^(w:f|seam|halo|tb:o|pill)/.test(k)) o.ch[k] = Math.round(CH[k] * 1000) / 1000;
      o.op = IDS.map(id => id + '=' + getComputedStyle(TE[id]).opacity);
      o.pressed = ['fAuto', 'fNew', 'fHdr'].map(id => TE[id].getAttribute('aria-pressed'));
      console.log('DUMP ' + JSON.stringify(o));
    }, +h.dump);
  }
}

/* ================= Start ================= */
window.leiste = {
  S, CH, get G() { return G; }, get TL() { return TL; }, measure, layout, setCollapsed, toggleCollapsed, toggleFilter, focusSearch, resetFilters,
  freeze(ms) { Clock.frozen = ms; step(); apply(); }, unfreeze() { Clock.frozen = null; Clock.setSpeed(S.speed); kick(); },
  running() { return [...AN.keys()]; },          // Kanäle mit laufender (oder noch wartender) Bewegung
  lastEnd() { let e = 0; for (const a of AN.values()) e = Math.max(e, a.t0 + a.dur); return e; },   // Zeitpunkt, an dem die letzte Uhr endet
  setWidth(w, drag = false) { S.dragging = drag; S.W = w; layout(); syncPanel(); }, last: null,
};
(async () => {
  capMeasure.style.cssText = 'position:absolute;left:-9999px;top:0;visibility:hidden;white-space:nowrap;display:flex;align-items:center;font:600 11.5px/1 var(--ft)';
  capMeasure.className = 'pill-measure';
  win.appendChild(capMeasure);
  buildToolbar();
  await loadData();
  if (D.missing) $('#panel .sub').insertAdjacentHTML('beforeend', ' · <b>data.json fehlt</b> (make-data.ps1 ausführen) – Beispielnamen ohne Bilder');
  buildHeadStats();
  wireWindow(); wirePanel();
  art.addEventListener('load', () => { readPalette(art); rowsKey = ''; });
  applyHash();
  window.addEventListener('hashchange', applyHash);
  document.fonts && document.fonts.ready.then(() => { if (!S.shot) { layout({ animate: false, keepTab: true }); apply(); } });
})();
})();
