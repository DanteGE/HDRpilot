# Werkzeugleiste: Vorschläge der Entwurfsrunde (2026-10-16)

Nur Entwurf, nichts davon ist gebaut. Anlass sind die drei Anmerkungen des Nutzers vom 2026-10-16 (wörtlich in `docs/stand.md`, Abschnitt "Pausiert am 2026-10-16"). Vier unabhängige Entwürfe (motion, utility, space, adaptive), danach eine Bewertung mit Empfehlung. Dem Nutzer noch nicht vorgelegt.

## Bewertung und Empfehlung

# Werkzeugleiste: Bewertung der vier Entwürfe und Empfehlung

## 1) Die Filter als Symbolknöpfe

**Einschätzung:** Ja, die Filter sollen so klein werden wie „Neu einlesen“. Das spart 241 DIP, so viel wie keine andere Maßnahme. Außerdem ändert sich die Breite nicht mehr mit den Zahlen: Heute ist „Neu 652“ 13 DIP breiter als „Neu 1“. In zwei Zeilen werden die Chips heute auf bis zu ca. 300 DIP gestreckt (Render 1920). Feste 40 DIP beenden beides. **Allein reicht es aber nicht für eine Zeile** (siehe 2). Die heutigen Symbole sind ohne Text nicht eindeutig: Die Sonne steht schon für „HDR einschalten“, den Stern liest man als „Favorit“, den Monitor als „Bildschirm“.

**So sehen die Filter aus**
- **Größe:** 40×40, Radius 12, Glas wie `Lib.FieldFrame`, `Grow 0`, Abstand 8. Sie bleiben drei einzelne Knöpfe ohne gemeinsamen Rahmen, denn ein Segment sähe aus wie eine Einzelwahl.
- **Reihenfolge:** Automatik, Neu, HDR-fähig, wie heute nach der Sortierung.
- **Symbole zeigen, was in der Liste steht:**
  - Automatik ist ein Mini-Schalter (Spur 18×10, Kontur 1,6, Knopf 6 px), wie in der Spalte „HDR AUTOMATISCH“.
  - Neu ist eine Plakette „NEU“ (22×13, Radius 4, 8,5 Bold).
  - HDR-fähig ist eine Plakette „HDR“ als Kontur.
  - Ersatz, falls die Plaketten bei 100 % zu klein wirken: Funkeln ✦ für Neu, Monitor mit HDR-Strich für HDR-fähig. Sonne, Stern und Monitor E7F4 nicht mehr verwenden.
- **Aktiver Zustand (nicht nur Farbe):**
  - Fläche wie heute bei `Lib.Chip`: Fläche C1 18 %, Rahmen AccentStrong 1 px, Hof AccentSoft (−4, 0,55).
  - Dazu wird das Symbol gefüllt: Der Mini-Schalter springt auf „an“ (Spur in Akzentfarbe, Knopf gleitet nach rechts), die Plaketten werden gefüllt.
  - Tastaturfokus: FocusRing 2 px mit −3 Abstand, klar von „an“ zu unterscheiden.
  - Gedrückt: Scale 0,96.
- **Zahlen:** keine dauerhafte Zahl am Knopf.
  - „Neu“ bekommt einen 6-px-Punkt innen oben rechts, in der Farbe des Punkts bei „652 neu“ in der Kopfzeile, solange es ungesehene Spiele gibt und der Filter aus ist.
  - Die Zahlen stehen im Tooltip, für den Bildschirmleser (ItemStatus), im Listenkopf („SPIEL · 36“) und in der Statuszeile.
  - Eine Zähler-Plakette (Entwürfe 3 und 4) würde 5–6 px in die Luft über der Leiste ragen und damit deine Regel „oben so viel Luft wie unten“ brechen. Deshalb nicht.
  - Ergibt ein Filter eine leere Liste, steht dort „Keine Spiele für diese Auswahl · Filter zurücksetzen“.
- **Tooltip:** neuer Stil `Lib.ToolTip`, gilt für alle reinen Symbolknöpfe der Leiste.
  - Verzögerung 1000 ms. Das ist das untere Ende deines Wunsches; bei 2 s glaubt man, es kommt nichts.
  - Danach erscheint der Tooltip des Nachbarknopfs sofort, wenn man innerhalb von 400 ms hinüberfährt. Er bleibt, solange Maus oder Fokus darauf sind, und erscheint auch bei Tastaturfokus. Platz: unten mittig, 8 px Abstand.
  - Aussehen: deckend Hp.Brush.Menu, Rahmen LineStrong, Radius 12, Innenabstand 12/9, höchstens 300 breit. Schatten als `Lib.PopupShadow`-Fläche, kein DropShadowEffect.
  - Aufbau: Titel (13 SemiBold) mit Tastenkappe, Erklärung (12, TextSecondary), Fußzeile in Mono 11.
  - Beispiel: „HDR-fähig [Alt+H]“ / „Nur Spiele mit HDR laut PCGamingWiki (auch eingeschränkt).“ / „36 Spiele · Filter aus“. Ist der Filter an: „Filter an · 36 von 652 · Klick hebt ihn auf“.
  - Einblenden auf der Stufe Voll: 140 ms (Opacity, Y 4→0). Reduziert: 100 ms nur Opacity. Aus: sofort.
- **Animation beim Einschalten:**
  - Voll: Fläche 200 ms, Hof 200 ms, Symbol springt kurz an (0,88→1,06→1 in 240 ms), der Knopf im Mini-Schalter gleitet mit der Federkurve (220 ms).
  - Reduziert: 150 ms Überblenden. Aus: sofort.
- **Tastatur und Bildschirmleser:** Es bleiben ToggleButtons.
  - Name wie heute (ganzer Satz). ItemStatus mit der Zahl („36 Spiele“), HelpText gleich dem Tooltip, AcceleratorKey.
  - Neue Tastenkürzel Alt+A, Alt+N und Alt+H, die von überall wirken, auch bei ausgeblendeter Leiste.
- **Ansicht und „Spiel hinzufügen“: Ich empfehle, sie auch nur als Symbol zu zeigen.**
  - Die Ansicht wird 3×38 + 8 = 122 statt 225 DIP breit. Die gewählte Fläche gleitet beim Wechsel (240 ms), die Tooltips nennen Strg+1/2/3.
  - „+“ wird 40 statt 142 DIP breit. Im abgenommenen Prototyp (final-01) stand „+“ schon nur als Symbol.
  - Folgen: Alle Symbolknöpfe sind eine Familie, nichts verschwindet beim Verkleinern, und einzeilig ab Listenbreite 1054 statt 1259. Die Zeilenzahlen je Größe stehen in der Tabelle unter 2.

## 2) Breiten und Zeilen

**Nachgerechnet am Code**
- **Breite der Liste:** Breite des Inhalts = Fenster − 2 DIP. Detailbereich = round(clamp(0,23 · Inhalt; 340; 396)). Liste L = Inhalt − 64 − 24 − Detailbereich. Das bestätigt die Probe (Fenster 1000 → Leiste 570, 1987 → 1501).
- **Gemessen in den Renders bei 110 %:** etwa 4 DIP weniger, bei 1920 sind es 1381 px = 1255,5 DIP.
- **„Spiel hinzufügen“ zählt 142, nicht 143:** Das Panel rechnet `Ceiling(Breite − 0,01)`, und 142,01 ergibt 142.
- **Wo die Entwürfe auseinanderliegen:**
  - Heute braucht eine Zeile 1500 DIP (mit „Neu 652“ 1513), wie die Probe zeigt. Entwürfe 2, 3 und 4 rechnen 1 DIP zu viel.
  - Nur die Filter als Symbole brauchen **1259**. Bei 1920 liegt das rechnerisch auf 0 DIP Rest, im Render fehlen 4 DIP. Unbrauchbar, wie alle vier Entwürfe richtig schließen.
  - Entwürfe 1 und 2 rechnen bei 1920 mit 1261 statt 1259 (ohne die −2 DIP) und lassen das Startfenster 1600×1000 DIP (L 1142) aus. Dort bleibt ihre Variante zweizeilig.
  - Entwurf 2 sagt, bei 1600 px sei eine Zeile mit beschrifteter Ansicht unmöglich. Das stimmt nicht, wenn die Felder nachgeben (1001 ≤ 1024).
  - Entwurf 4 hat recht: Wenn die Felder stufenlos nachgeben, ist auch 1366×768 einzeilig.

**Zeilen der Leiste (110 %)**

| Fenster | L (Formel / Render) | heute | nur Filter als Symbol | + Ansicht und „+“ als Symbol, feste Breiten | **Empfehlung (fließend)** | Regal: heute → Empfehlung |
|---|---|---|---|---|---|---|
| 1000×620 (kleinstes Fenster) | 570 | 4 | 3 | 3 | **2** | 934: 2 → **1** |
| 1280×800 | 733 / ≈730 | 3 | 2 | 2 | **2** | 1097: 2 → **1** |
| 1366×768 | 811 / ≈807 | 2 | 2 | 2 | **1** (Sortierung nur Symbol, 9–13 DIP Rest) | 1175: 2 → **1** |
| 1600×1000 px | 1024 / ≈1020 | 2 | 2 | 2 (30 zu wenig) | **1** (Felder −30) | 1388: 2 → **1** |
| Startfenster 1600×1000 DIP | 1142 | 2 | 2 | 1 | **1** (Suche 320) | 1534: 1 |
| 1920×1080 | 1259 / ≈1255 | 2 | 2 (auf der Kante) | 1 | **1** | 1679: 1 |
| 1920×1080 @125 % | 1093 | 2 | 2 | 1 | **1** | 1470: 2 → **1** |
| 3440×1440 | 2641 | 1 | 1 | 1 | **1** | 3061: 1 |

Benötigte Breite für eine Zeile:
- Heute: 1500.
- Nur Filter als Symbol: 1259.
- Filter, Ansicht und „+“ als Symbol: 1054.
- Dazu Felder am Minimum: 898.
- Dazu Sortierung nur als Symbol: 798.

Im Regal ist die Leiste damit **bei jeder erlaubten Fenstergröße einzeilig**. Der Sonderfall „Leiste neben der Automatik-Karte“ tritt praktisch nicht mehr auf.

**Wie die Leiste in schmalen Fenstern nachgibt** (L = Breite der Listenspalte)

| L | Was passiert | Bewegung |
|---|---|---|
| ≥ 1138 | alles bequem, Suche 320, der Rest wird Freiraum zwischen linker und rechter Gruppe | – |
| 1054–1138 | Suche 320 → 236 | stufenlos, folgt dem Fensterrand direkt |
| 898–1054 | Suche 236→168, Quelle 188→136, Sortierung 196→160 anteilig (68 : 52 : 36). Lange Namen bekommen „…“ (TextTrimming ist schon da). Die Tastenkappe „Strg+F“ weicht ab Suchbreite < 204 und kommt ab ≥ 216 zurück. | stufenlos; Kappe blendet in 120 ms |
| 798–898 | **Einziger Schritt:** Die Sortierung zeigt nur noch das Symbol der gewählten Sortierung und ⌄ (60 DIP). Die frei werdenden 100 DIP werden Freiraum. | Text weicht nach links aus: Breite 160 ms CubicEase Out, Opacity 90 ms, X 0→−4 |
| < 798 | zwei Zeilen, nur an Gruppengrenzen: oben Suche · Quelle · Sortierung (wieder mit Text, gefüllt), unten Filter links und Ansicht · ⟳ · ◉ · + rechtsbündig | 300 ms Morph |
| < 410 | drei Zeilen, nur im kleinsten Fenster mit ganz breitem Detailbereich | wie oben |

**Regeln dazu**
1. Beim Schmalerziehen wird nie etwas breiter. Zeilen, die nur aus festen Knöpfen bestehen, werden nie gestreckt.
2. Schritte werden nur animiert, während du selbst ziehst (WM_ENTERSIZEMOVE bis WM_EXITSIZEMOVE oder am Detail-Trenner). Beim Maximieren, Einrasten, DPI- oder Monitorwechsel und beim Start gilt sofort der Endzustand, ohne Hysterese.
3. Hysterese nur beim Ziehen: Die Beschriftung kommt erst 24 DIP über der Schwelle zurück, die eine Zeile erst ab L ≥ 830.
4. Zeilenwechsel auf der Stufe Voll:
   - Das Panel verschiebt die Elemente von der alten in die neue Lage, 300 ms ExponentialEase Out mit Exponent 6 (die Kurve des Titelbereichs).
   - Elemente, die die Zeile wechseln, starten je 16 ms versetzt.
   - Die Liste darunter wird nur verschoben (FLIP): ein einziger Layout-Durchgang, danach nur TranslateY.
   - Reduziert: 150 ms Überblenden, der Inhalt springt. Aus: sofort.
5. Pixel: Abstände round(8·Skalierung). Zwischenbilder ungerundet, das letzte Bild auf ganzen Gerätepixeln. Während der Bewegung TextHintingMode Animated.
6. Elemente werden nur angeordnet, nie umgehängt. Die Tab-Reihenfolge bleibt, der Fokus geht nicht verloren.

## 3) Empfohlenes Konzept: „Fließleiste mit Lichtnaht“

**Bewertung der vier Entwürfe (1–10; bei Risiko und Aufwand heißt 10 „gering“)**

| Kriterium | 1 Lichtnaht | 2 Faltleiste | 3 Lasche + Kantenkapsel | 4 Fließleiste |
|---|---|---|---|---|
| Einzeilig und fließend (Pflicht) | 4 | 5 | 6 | **9** |
| Cool und zeitgemäß | **9** | 5 | 7 | 7 |
| Klarheit, Auffindbarkeit | 6 | 8 | 8 | 7 |
| Passt zum Showcase Hero | **9** | 6 | 8 | 8 |
| Filterzustand sichtbar | 9 | 9 | 8 | 8 |
| Tastatur, Barrierefreiheit | 7 | **9** | 8 | 8 |
| GPU, Leistung | 8 | 8 | 6 | 6 |
| Risiko, dass es dir missfällt | 5 | 4 | 6 | 6 |
| Aufwand | 5 | 6 | 7 | 5 |

**Synthese:** Die Basis ist Entwurf 4: fließende Leiste, Lasche und Kapsel rechts auf der Bildkante, kurzes Einblenden mit Strg+F. Vereinfacht: Ansicht und „+“ sind fest nur Symbole (wie in Entwurf 3 und im Prototyp), dadurch gibt es nur noch einen Schritt statt vier. Übernommen werden:
- aus Entwurf 1: die leuchtende Naht, die Zündung beim Einklappen und das Verschieben nach dem FLIP-Prinzip;
- aus Entwurf 3: die Symbole nach dem Vorbild der Liste und die Leiste, die unter der Bildkante verschwindet;
- aus Entwurf 2: das Kontextmenü und die Alt-Kürzel.

**Aussehen**
```
ausgeklappt (Liste, 1920×1080):
 GEARS OF WAR E-DAY                       [▷ Starten] [☼ Beim Start …]
                                                        16 px frei
═════════════════════ ▬▬ (Griff) ═════════════════════ ╭─⌃─╮ ═   ← Bildkante
                                                       ╰───╯     10 px
[⌕ Suchen …   Strg+F][● Alle 652 ⌄][◷ Zuletzt gespielt ⌄][◐][NEU•][HDR] … [☰|▦|⫼][⟳][◉][+]
                                                                  10 px
┌ SPIEL · 652 ───────── QUELLE ── ZULETZT GESPIELT ── HDR ── HDR AUTOMATISCH ┐

eingeklappt, nichts aktiv:
═════════════════════ ▬▬ ════════════════╭ ⌄ │ ⌕ Suchen  Strg+F ╮═
┌ SPIEL · 652 ──────── (Liste 38 px höher) ──────────────────────────┐

eingeklappt, gefiltert (die Naht leuchtet im Akzentverlauf):
━━━━━━━━━━━━━━━ ▬▬ ━━━━━━━━━━╭ ⌄ │ ⌕ „gears“ · ● Steam · [NEU][HDR] · 3/652  ✕ ╮
┌ SPIEL · 3 ─────────────────────────────────────────────────────────┐

zwei Zeilen (1280×800):
[⌕ Suchen …          Strg+F][● Alle        652 ⌄][◷ Zuletzt gespielt  ⌄]
[◐][NEU•][HDR]                                    [☰|▦|⫼][⟳][◉][+]
```

**Lasche**
- Sichtbar 36×18, Trefferfläche 44×24, rechts auf der Bildkante (−12 bis +6 um die Kante).
- Glas mit Rahmen Line, Pfeil E70E in 9 px.
- Beim Überfahren wächst sie nach links zu „⌃ Leiste ausblenden · Strg+Umschalt+F“ (160 ms).
- Sie kostet keine Breite in der Leiste.

**Kapsel (eingeklappt)**
- 24 hoch, mittig auf der Kante, rechtsbündig, höchstens bis 32 px vor die Mitte des Ziehgriffs; was nicht passt, wird zu „+2“.
- Inhalt: Suchbegriff (bis 16 Zeichen), Quelle (wenn nicht „Alle“), aktive Filter als Mini-Symbole, „x/652“ in Mono und ✕ zum Zurücksetzen.
- Gefiltert: Rahmen AccentStrong, ruhender Hof 0,35 und **Lichtnaht**: eine 1 px feine, statische Linie im Akzentverlauf (C1 → C2, 90 %), die von der Kapsel nach links ausläuft.

**Auslöser**
- **Maus:** Klick auf die Lasche bzw. die Kapsel. Rechtsklick auf freie Fläche der Leiste, auf Lasche oder Kapsel (oder Umschalt+F10) öffnet ein Menü:
  - „Leiste aus-/einblenden“
  - die drei Filter mit Häkchen, Zahl und Kürzel
  - „Alle Filter zurücksetzen“
- **Tastatur:**
  - Strg+Umschalt+F schaltet um; Strg+F1 geht zusätzlich, wie „Menüband“ in Office. Beide sind in der App frei.
  - Enter oder Leertaste auf Lasche bzw. Kapsel.
  - Alt+A, Alt+N und Alt+H schalten die Filter auch bei ausgeblendeter Leiste.
- **Strg+F bei ausgeblendeter Leiste:**
  - Die Leiste klappt vorübergehend aus (260 ms), der Fokus geht in die Suche.
  - Sie faltet sich 600 ms, nachdem der Fokus die Leiste verlassen hat, wieder ein, sofern keine Auswahlliste offen ist und die Maus nicht darüber steht. Der Suchbegriff steht dann in der Kapsel.
  - Esc in der leeren Suche faltet sofort ein; der Fokus geht auf das gewählte Spiel.
  - Ein Klick auf die Lasche macht „offen“ dauerhaft.
- **Nichts passiert automatisch:** kein Ausblenden beim Scrollen (dein „Größe bleibt“), kein Einblenden beim Überfahren.

**Animation**

| | Voll | Reduziert (auch bei Windows „Animationseffekte“ aus; höchstens 300 ms) | Aus, minimiert, Spiel läuft |
|---|---|---|---|
| Einfalten, ≈ 0,74 s, sofort bedienbar | **0 ms:** Fokus aus der Leiste auf die Lasche; Endlayout sofort; Liste TranslateY +Δ. **0–260:** Elemente fließen als Welle von links zur Lasche (14 ms Versatz, je 140 ms: Opacity 1→0 CubicEase In, X +16, Y −6, Scale 0,94). **40–420:** Liste steigt (+Δ→0, 380 ms, ExpoOut Exp 6). **120–380:** Lasche wird Kapsel (Höhe 18→24, Breite nach links, Hp.Ease.Spring), Pfeil 180° in 220 ms. **300–450:** Kapselinhalt (30 ms Versatz, X −6→0). **340–740 „Zündung“:** Lichtstreif 2×160 px läuft auf der Kante zur Kapsel (400 ms, SineEase InOut, Opacity 0→0,9→0); danach leuchtet der Hof kurz auf (0→0,8→Ruhewert in 280 ms); gefiltert wächst die Naht von der Kapsel nach links (ScaleX 0→1, 400 ms, KeySpline 0.16,0.84,0.24,1). | Leiste Opacity in 120 ms, Liste gleitet 200 ms CubicEase Out, Lasche und Kapsel überblenden in 150 ms, Naht blendet in 150 ms ein. Kein Versatz, kein Skalieren, kein Drehen, kein Lichtstreif. | Endzustand sofort; wird das Fenster mitten im Morph minimiert, springt er ans Ende. |
| Ausfalten, ≈ 0,62 s | **0:** Leiste wieder im Layout (Opacity 0, sofort per Tab erreichbar), Liste −Δ→0 in 380 ms ExpoOut. **0–200:** Naht zieht sich in die Kapsel zurück. **0–320:** Lichtstreif von der Kapsel nach links. **60–260:** Kapsel wird Lasche. **120–620:** Elemente entfalten sich von der Lasche aus (16 ms Versatz, Opacity 180 ms CubicEase Out, X/Y/Scale 300 ms Feder). | spiegelbildlich | sofort |

**Unterbrechen:** Ein zweites Umschalten läuft vom aktuellen Wert weiter (SnapshotAndReplace).

**GPU:**
- Es bewegen sich nur Opacity und RenderTransform an höchstens 14 kleinen Flächen, dazu eine Verschiebung der Liste und ein 2×160-px-Verlauf.
- Kein Blur, kein Schatten-Effekt, keine Mischmodi.
- Das Layout wird nur einmal gerechnet; die virtualisierte Liste misst nicht neu.
- Der Übergang läuft mit 60 Bildern pro Sekunde für höchstens 0,74 s, danach laufen keine Uhren mehr. Die Naht ist statisch, also gibt es keinen Dauer-Effekt.

**Merken:**
- Neue Einstellung `AppSettings.LibraryToolbarCollapsed`, Standard ausgeklappt.
- Ein Zustand für alle drei Ansichten, gespeichert beim Umschalten, beim Start ohne Animation hergestellt.
- Das kurze Einblenden per Strg+F wird nicht gemerkt.

**Barrierefreiheit:**
- **Ein Steuerelement:** Lasche und Kapsel sind dasselbe Element (`ToolbarToggle`). Es hat einen eigenen Automation-Peer mit ExpandCollapse.
  - Name „Werkzeugleiste“.
  - ItemStatus „Gefiltert: Suche „gears“, Steam, Neu – 3 von 652“ bzw. „Keine Filter“.
  - HelpText mit den Kürzeln.
- **Ansage beim Umschalten:** einmalig über RaiseNotificationEvent. Das ✕ ist ein eigener Knopf („Suche und Filter zurücksetzen“, 24×24).
- **Tab-Reihenfolge:**
  - Ausgeklappt: Griff → Lasche → Suche … → Liste.
  - Eingeklappt: Griff → Kapsel → ✕ → Liste.
- **Fokus:**
  - Liegt er beim Einklappen in der Leiste, geht er auf die Kapsel.
  - Nach dem Morph ist die Leiste `Collapsed`, also nicht mehr per Tab und nicht im UIA-Baum.
- **Hoher Kontrast:** Systemfarben, keine Verläufe.

**Ziehgriff, Raster, Regal**
- **Ziehgriff:** bleibt genau auf der Kante, sein Mittelteil wird nie verdeckt.
  - Lasche und Kapsel liegen rechts darüber in der Z-Reihenfolge; seine Leuchtlinie endet 12 px vor der Kapsel.
  - Der Titelbereich ändert sich beim Umschalten nie: `ListHeroRange` und `GridHeroHeight` rechnen immer mit der ausgeklappten Höhe.
- **Luft:** Ausgeklappt bleibt 10/10 (Liste) bzw. 22/22 (Raster). Eingeklappt bleiben 10 px unter der Kapsel.
- **Gewinn in der Liste:** +38 DIP bei einer Zeile Leiste, +86 bei zwei Zeilen.
- **Raster:** Lasche und Kapsel wie in der Liste; die Kacheln rücken um +50 bzw. +98 DIP nach oben.
- **Regal:**
  - Die Lasche sitzt rechts in der 22-px-Fuge zwischen Leiste und Kachelreihe, also auf der Bildkante.
  - Eingeklappt steht die Kapsel rechts in der früheren Leistenzeile, auch beim kleinsten Titelbereich (440) mindestens 16 px unter der Automatik-Karte.
  - Knöpfe und Logo sinken um 40 DIP (unterer Innenrand 74 → 34), die Kachelreihe bleibt, wo sie ist. Das gibt mehr Bühne für das Bild.

## 4) Alternativen
- **Faltleiste (Entwurf 2):** Eingeklappt bleibt eine Zeile mit Suche, Filtern und Marken für alles Verborgene. Das ist sehr praktisch und zeigt den Filterzustand am deutlichsten, blendet die Leiste aber nie wirklich aus.
- **Lichtnaht pur (Entwurf 1):** Die Kapsel sitzt mittig auf dem Griff, und ein Glasband verdichtet sich in sie. Das ist der spektakulärste Entwurf, kollidiert aber mit dem Ziehgriff, ist etwas aufwendiger und löst die Einzeiligkeit nicht.

## 5) Offene Fragen an dich
1. **Sollen Ansicht und „Spiel hinzufügen“ auch nur als Symbol erscheinen?** Empfehlung: ja. Ohne das ist die Leiste im Startfenster und bei 1366/1600 zweizeilig, oder es bräuchte vier Stufen, in denen Beschriftungen weichen. Die alte Stufen-Faltung mochtest du nicht.
2. **Zahlen an den Filtern?** Empfehlung: keine dauerhafte Zahl, ein Punkt an „Neu“ und die Zahlen im Tooltip. Eine Zähler-Plakette ginge auch, ragt aber in die Luft über der Leiste.
3. **Strg+F bei ausgeblendeter Leiste?** Empfehlung: vorübergehend einblenden, die Leiste faltet sich nach dem Verlassen wieder ein. Die Alternative: Strg+F blendet sie dauerhaft ein.

## 6) Aufwand
Insgesamt etwa 4,5–5,5 Tage. Empfohlene Reihenfolge: zuerst Teil 1 und 2, dann Renders zur Abnahme, danach Teil 3. Für Teil 3 vorher einen kurzen HTML-Prototyp im Stil von app.js als Bildfolge zur Abnahme (etwa 0,5 Tag).

| Teil | Inhalt | Aufwand |
|---|---|---|
| 1 | Filter als Symbolknöpfe (`Lib.FilterToggle`, Mini-Symbole, Neu-Punkt), `Lib.ToolTip`, Ansicht und „+“ als Symbol, Alt-Kürzel, Hinweis bei leerer Liste | ≈ 1 Tag |
| 2 | Fließende Leiste im `ToolbarFlowPanel`: Mindestbreiten, Sortier-Schritt, Umbruch an Gruppengrenzen, keine gestreckten Symbolzeilen, Hysterese beim Ziehen, Pixelrundung, Zeilen-Morph, Zielhöhe statt ActualHeight; Tests für 570/733/811/1024/1142/1259/2641 und 100/110/125/150 % | ≈ 1,5 Tage |
| 3 | Lasche und Kapsel (`ToolbarToggle` mit Peer), FLIP-Morph, Lichtnaht, Strg+F-Einblenden, Kontextmenü, Merken, eingeklappte Maße in `ShowcaseLayout` (Liste, Raster, Regal) | ≈ 2 Tage |
| 4 | UIA- und GPU-Prüfungen (0 Uhren nach dem Morph), Bildschirmfotos ein- und ausgeklappt, Doku (`README.md`, `GATES.md`) | ≈ 0,5–1 Tag |

Gerechnet habe ich mit diesen Dateien:
- .worktrees\s11-bar\src\HdrPilot.App\Views\Library\ToolbarFlowPanel.cs
- .worktrees\s11-bar\src\HdrPilot.App\Views\Library\ShowcaseLayout.cs
- .worktrees\s11-bar\src\HdrPilot.App\MainWindow.xaml
- .worktrees\s11-bar\src\HdrPilot.App\MainWindow.xaml.cs
- .worktrees\s11-bar\src\HdrPilot.App\Themes\Library.xaml
- .worktrees\s11-bar\src\HdrPilot.App\Infrastructure\Effects\EffectPolicy.cs
- <Notizordner>\toolbar\rvf-probe-sweep.txt (Probe)
- <Notizordner>\toolbar\shots\final2\ (Renders)

## Konzept 1 (motion): Lichtnaht

### filterButtons

Einschätzung: Ja. Die drei Filter als 40×40-Symbolschalter wie „Neu einlesen“ sind richtig, wenn drei Bedingungen erfüllt sind.

Warum ja:
• Platz: Die Chips brauchen heute 133 + 107 + 133 DIP (bei deinen Zahlen Neu 1 / HDR-fähig 36: 133 + 95 + 139). Als Symbolschalter sind es 3 × 40. Das spart 247–253 DIP, mehr als jede andere einzelne Maßnahme. Zusammen mit einer etwas schmaleren Suche wird 1920×1080 bei 110 % damit einzeilig (Rechnung unter Breitenanalyse).
• Es sind Schalter mit Zustand. Nach zwei, drei Benutzungen kennt man sie, danach überliest man den Text nur noch. Die eigentliche Information ist an/aus, und die zeigt ein Symbolschalter genauso gut.
• Ruhigeres Bild: drei Felder (Suche, Quelle, Sortierung), das Ansichts-Segment, sieben gleich große Quadrate und „Spiel hinzufügen“. Das ist ein klarer Rhythmus statt fünf verschieden breiter Pillen.

Bedingungen (sonst wird es schlechter als heute):
1. Die Symbole müssen eindeutig sein. Die heutigen sind es ohne Text nicht: Die Sonne steht in HDRpilot schon für „HDR einschalten“ und steckt im Logo, der Stern heißt fast überall „Favorit“, und der Monitor ist auch das Symbol der Bildschirmauswahl.
2. Die Zahlen dürfen nicht einfach wegfallen. Sie ziehen in den Tooltip, die Kopfzeile und den Listenkopf um, und „Neu“ behält ein sichtbares Signal.
3. An/aus darf nicht nur an der Farbe erkennbar sein, sondern braucht auch eine Form.

Konkrete Spezifikation:
• Größe: 40 × 40 DIP, Radius 12, Glasfläche wie Lib.ToolbarIconButton (Lib.FieldFrame). Feste Breite: ToolbarFlowPanel.Grow = 0 statt 2, Abstand 8 wie überall. Reihenfolge bleibt: Automatik, Neu, HDR-fähig direkt nach der Sortierung.
• Symbole (16 px, als LineIcon bzw. kleine Vorlage, für beide Themen):
  – Automatik: ein Mini-Schalter. Spur 18 × 10 als 1,6-px-Kontur, gefüllter 6-px-Knopf rechts. Es ist dasselbe Bild wie der Schalter „HDR automatisch“ in jeder Zeile, das Symbol zeigt also genau das, wonach gefiltert wird.
  – Neu: eine „NEU“-Plakette 22 × 13, Radius 4, gefüllt und invertiert wie die NEU-Marke der Liste (dunkles Thema weiß mit dunkler Schrift, helles Thema dunkel mit heller Schrift), Schrift 8,5 Bold.
  – HDR-fähig: eine „HDR“-Plakette 22 × 13, nur als 1,2-px-Kontur wie die HDR-Kennzeichen der Liste.
  – Ersatz, falls die Plaketten nicht gefallen: ein Funke (✦) für Neu, ein Monitor mit HDR-Strich für HDR-fähig. Stern und Sonne nicht mehr verwenden.
• Aktiv (IsChecked): wie heute der Zustand „On“ von Lib.Chip, also Fläche C1 18 %, Rahmen AccentStrong 1 px, Hof AccentSoft (−4 px, 55 %), Symbol bzw. Plakette in AccentText. Dazu kommt ein Formsignal: ein Indikator 14 × 2 px, Radius 1, unten innen 4 px über der Unterkante, im Akzentverlauf. Hover: Surface2. Gedrückt: Scale 0,96. Tastaturfokus: FocusRing 2 px mit −3 px Abstand (wie Lib.ToolbarButton).
• Zahlen: keine dauerhafte Zahl im Schalter.
  – „Neu“ bekommt einen 6-px-Punkt in C3 oben rechts innen (x 30–36, y 5–11), solange es neue Spiele gibt. Das ist dieselbe Farbe wie „652 neu“ in der Kopfzeile, ein Hinweis wie bei ungelesenen Nachrichten.
  – Automatik und HDR-fähig sind feste Eigenschaften der Spiele, kein Posteingang. Ihre Zahlen stehen im Tooltip und ab 1366 DIP Fensterbreite in der Kopfzeile; die gefilterte Menge zeigen der Listenkopf („SPIEL · 36“) und die Statuszeile.
  – Nicht empfohlen ist eine Zählerkappe am Rand des Schalters: Sie ragt 5–6 px in die Luft über der Leiste, die oben und unten gleich bleiben soll. Wenn du die Zahlen trotzdem dauerhaft willst, ist sie die Ausweichlösung (16 px hoch, Cascadia Mono 9,5, ab 100 „99+“).
• Tooltip-Verzögerung und Verhalten:
  – ToolTipService.InitialShowDelay = 1000 ms. Das ist die untere Grenze deines Wunsches; bei 2 s glaubt man, es kommt gar nichts. Heute gilt der Windows-Standard von etwa 400 ms.
  – BetweenShowDelay = 300 ms: Wer zum Nachbarn weiterfährt, sieht dessen Tooltip sofort.
  – ShowDuration = 12 s.
  – Placement unten, mittig, 8 px Abstand.
  – ShowsToolTipOnKeyboardFocus = True.
  – Dieselben Werte gelten für Neu einlesen, gesehen, die Ansicht und die Naht-Kapsel, damit die ganze Leiste gleich reagiert.
• Tooltip-Aussehen: eigener Stil Lib.ToolTip. Fläche Hp.Brush.Menu, Rahmen LineStrong 1 px, Radius 12, Innenabstand 12/9/12/10, höchstens 300 breit. Schatten als Verlaufsfläche (Lib.PopupShadow), kein DropShadowEffect.
• Tooltip-Inhalt: Titel 13 SemiBold, Text 12 in TextSecondary, darunter eine Fußzeile mit der Zahl im Stil Lib.Count und der Taste im Stil Lib.Kbd:
  – „Automatik“ · „Nur Spiele, bei denen HDRpilot HDR beim Start automatisch einschaltet.“ · „0 Spiele · Alt+A“
  – „Neu“ · „Nur neu gefundene Spiele, die du noch nicht gesehen hast.“ · „1 Spiel · Alt+N“
  – „HDR-fähig“ · „Nur Spiele mit HDR laut PCGamingWiki (auch eingeschränkt).“ · „36 Spiele · Alt+H“
  – Bei aktivem Filter zusätzlich in AccentText: „● Filter aktiv – klicken zum Aufheben“.
• Gruppierung: kein eigener Rahmen, denn ein Segment-Rahmen sähe aus wie eine Einzelauswahl (wie Liste/Raster/Regal), die Filter sind aber mehrfach wählbar. Bei zwei Zeilen eröffnen die drei Schalter die zweite Zeile. Oben steht dann „finden“, unten „filtern, ansehen, handeln“.
• Tastatur:
  – Die Schalter bleiben ToggleButtons in der Tab-Reihenfolge; Leertaste und Enter schalten.
  – Neu (empfohlen): Alt+A, Alt+N und Alt+H schalten die Filter von überall, auch bei ausgeblendeter Leiste. Die Kürzel stehen im Tooltip.
• Bildschirmleser:
  – AutomationProperties.Name bleibt der ganze Satz („Nur Spiele mit HDR automatisch“, „Nur neue Spiele“ …).
  – Dazu ItemStatus mit der Zahl („1 Spiel“), HelpText gleich dem Tooltip-Text und AcceleratorKey („Alt+N“).
  – Das Toggle-Pattern meldet an/aus.

### widthAnalysis

Grundlage und Regeln:
• ToolbarFlowPanel rechnet in ganzen DIP; natürliche Breiten werden aufgerundet.
• Zwischen allen Elementen liegen 8 DIP (Gap-Standard). Bei 10 Elementen sind das 9 × 8 = 72.
• Die Leiste ist einzeilig, wenn die Summe ≤ verfügbare Breite ist.
• Textbreiten habe ich mit WPF-FormattedText gemessen (Segoe UI Variable Text SemiBold 13 bzw. Bold 12, Cascadia Mono SemiBold 10,5). Sie decken sich mit der früheren Messung (Chips 132,40 / 94,88 / 132,68).
• Die Renders sind bei 110 % aufgenommen: Der Detailbereich misst dort 434 px, also 396 DIP.

Natürliche Breiten (DIP):
• Suche 236 (Basis).
• Quelle 188 (Basis).
• Sortierung 196 (Basis). Die längste Option „HDR-Unterstützung“ braucht 12 + 13 + 9 + 115 + 34 ≈ 183, die Basis bleibt also.
• Filter-Chip = 11 + 13 (Symbol) + 8 + Text + (8 + 7 + Zahl + 7) + 10 = 64 + Text + Zahl:
  – Automatik „0“: 64 + 62,0 + 6,2 = 132,1 → 133
  – Neu „652“: 64 + 24,5 + 18,5 = 106,9 → 107 (mit „1“: 94,6 → 95)
  – HDR-fähig „0“: 64 + 62,5 + 6,2 = 132,7 → 133 (mit „36“: 138,8 → 139)
• Ansicht 225 = Rahmen 1 + 3 + Liste (10 + 15 + 6 + 26,0 + 10 = 67) + Raster 77 + Regal 73 + 3 + 1. Nur mit Symbolen: 3 × 38 (MinWidth) + 8 = 122.
• Neu einlesen 40, gesehen 40.
• Spiel hinzufügen 142 (11 + 13 + 8 + ≈99 + 11). Nur Symbol: 40.

Summen der Varianten:
• Heute: 236 + 188 + 196 + 133 + 107 + 133 + 225 + 40 + 40 + 142 = 1440, plus 72 = 1512. Mit Neu 1 / HDR-fähig 36 sind es 1506.
• A – Filter als 40×40: 1440 − 373 + 120 = 1187, plus 72 = 1259.
• A+ – zusätzlich Basis der Suche 216 statt 236 (empfohlen): 1239. Einzeilig wächst die Suche wie heute bis 320, schmaler wird sie also nur beim Umbruch.
• B – A+ und Ansicht nur mit Symbolen: 1239 − 103 = 1136.
• C – B und „Spiel hinzufügen“ nur als Symbol: 1136 − 102 = 1034 (mit Suche 200: 1018).
• Der Umschalter der Lichtnaht kostet 0, weil er auf der Naht sitzt und nicht in der Leiste.

Breite der Liste bzw. des Rasters bei 110 %:
• Formel: W = px / 1,1. Detailbereich = clamp(round(0,23 · W); 340; 396). Liste = W − 2 · 32 − 24 − Detailbereich.
• Gemessen in den Renders ist sie jeweils etwa 4–5 DIP schmaler (Fensterrand).
• 1280×800: W 1163,6 → Detail 340 → Liste 735,6 (Render ≈ 731)
• 1366×768: W 1241,8 → 340 → 813,8 (≈ 809)
• 1600×1000: W 1454,5 → 340 (0,23 · W = 334,5) → 1026,5 (≈ 1022)
• 1920×1080: W 1745,5 → 396 → 1261,5 (Render 1256,4)
• 3440×1440: W 3127,3 → 396 → 2643 (im Render ist der Detailbereich ≈ 453 breit → ≈ 2577)
• Regal (ganze Breite W − 64): 1099,6 / 1177,8 / 1390,5 / 1681,5 / 3063.

Zeilen in Liste und Raster (heute | A | A+ | B | C):
• 1280×800: 3 | 2 | 2 | 2 | 2
• 1366×768: 2 | 2 | 2 | 2 | 2
• 1600×1000: 2 | 2 | 2 | 2 | 2. Nur C mit Suche 200 passt rechnerisch (1018 ≤ 1022), aber mit 4 DIP Reserve.
• 1920×1080: 2 | 2* | 1 | 1 | 1. *1259 ist größer als die gemessenen 1256; rechnerisch (1261,5) wäre es einzeilig, aber es kippt bei 2 DIP – unbrauchbar.
• 3440×1440: 1 | 1 | 1 | 1 | 1

Zeilen im Regal (heute | A | A+ | B | C):
• 1280: 2 | 2 | 2 | 2 | 1
• 1366: 2 | 2 | 2 | 1 | 1
• 1600: 2 | 1 | 1 | 1 | 1
• 1920: 1 | 1 | 1 | 1 | 1
• 3440: 1 überall

Aufteilung bei zwei Zeilen mit A+ (BalancedRows):
• Zeile 1: Suche, Quelle, Sortierung = 216 + 188 + 196 + 2 · 8 = 616.
• Zeile 2: drei Filter, Ansicht, ⟳, 👁, Hinzufügen = 3 · 40 + 225 + 40 + 40 + 142 + 6 · 8 = 615.
• Die beiden Zeilen sind fast gleich lang (die Teilung nach der 4. Stelle ergäbe 664 / 567) und inhaltlich sauber getrennt.
• Gestreckt bei 1366 (Raum 813):
  – Zeile 1 bekommt +197: Suche +99, Quelle und Sortierung je +49 (Anteile 4 : 2 : 2).
  – Zeile 2 bekommt +198, verteilt auf Ansicht und Hinzufügen (je +99).
  – Die Symbolschalter bleiben 40.

Fazit:
• Symbolschalter plus Suche 216 machen 1920×1080 einzeilig, mit 17 DIP Reserve. Das hält, solange der Detailbereich höchstens 413 DIP breit ist.
• Im Regal ist die Leiste damit schon ab 1600 einzeilig.
• Bei 1280–1600 bleibt die Liste in jeder Variante zweizeilig; selbst C reicht bei 1366 nicht (1034 > 809). Dort hilft nur das Einklappen: +98 DIP, also gut zwei Listenzeilen à 46.
• B und C empfehle ich nicht: In der Liste bringen sie nur bei 1600 etwas, und dort knapp, kosten aber die Beschriftungen, die du behalten wolltest.

### hideShowConcept

Name: Lichtnaht. Die Leiste faltet sich in die Naht zwischen Titelbild und Inhalt und kommt dort wieder heraus.

Grundidee: Alles passiert an einem Ort. Die Grenze zwischen Titelbild und Inhalt, auf der schon der Ziehgriff liegt, wird zur „Naht“. Auf ihr sitzt eine kleine Glas-Kapsel, in beiden Zuständen an derselben Stelle. Eingeklappt verschwindet die Leiste nicht spurlos: Sie wird zu einer feinen Linie auf der Naht, und die Kapsel zeigt, was gerade aktiv ist. Der Umschalter kostet keine Breite in der Leiste.

AUSGEKLAPPT (Liste, 1366×768, 2 Zeilen)
  GEARS OF WAR        ── NEU IN DER BIBLIOTHEK          [▷ Starten]
  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─(⠿ ⌃)─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─   ← Grenze: Griff + Kapsel
  ╭⌕ Suchen …        Strg+F╮ ╭● Alle     652 ⌄╮ ╭◷ Zuletzt gespielt ⌄╮
  ╭◖●╮ ╭NEU╮ ╭HDR╮ ╭☰ Liste│▦ Raster│≡ Regal╮ ╭⟳╮ ╭◉╮ ╭ + Spiel hinzufügen ╮
  ╭ SPIEL · 652          ZULETZT GESPIELT     HDR    HDR AUTOMATISCH ╮

EINGEKLAPPT, nichts aktiv
  GEARS OF WAR        ── NEU IN DER BIBLIOTHEK          [▷ Starten]
  ┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈(⠿ ⌄ WERKZEUGE)┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈   ← Naht, neutral
  ╭ SPIEL · 652          ZULETZT GESPIELT     HDR    HDR AUTOMATISCH ╮
  │ ▣ Gears of War: E-Day NEU  gestern, 22:17    —     Aus ○─        │
  │ … zwei Zeilen mehr als ausgeklappt                                │

EINGEKLAPPT, Suche und Filter aktiv
  ━━━━━━━(⠿ ⌄ WERKZEUGE · ⌕ „tun“ · ● Steam · NEU · HDR · 3 / 652)━━━━━━━   ← Naht im Akzentverlauf

REGAL, EINGEKLAPPT
  GEARS OF WAR E-DAY (das Logo bekommt mehr Raum)
  [▷ Starten] [Desktop] [Startmenü]              ← sinken um 50 / 98 DIP
  ┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈(⌄ WERKZEUGE)┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈   ← Unterkante des Titelbilds
  ▯▯▯▯▯▯ Kachelreihe bleibt, wo sie ist ▯▯▯▯▯▯

KAPSEL
• Lage: mittig auf der Grenze.
• Sichtbare Höhe 16 DIP, also ±8 um die Grenze. Oben liegt sie im freien 16-px-Innenrand des Titelbilds, unten in den 10 DIP Luft. Sie berührt weder Titelinhalt noch Leiste noch Liste (je 2 DIP Rest).
• Trefferfläche 24 DIP hoch.
• Breite ausgeklappt 44 (⠿ und ⌃). Eingeklappt so breit wie ihr Inhalt, höchstens 60 % der Spalte; was nicht passt, wird zu „+2“.
• Aussehen: Glas wie die Bildschirm-Kapsel (HeaderGlass, HeaderLine), Radius 8. Beschriftung in Großbuchstaben 10,5 Bold im Stil Lib.Overline, wie „NEU IN DER BIBLIOTHEK“; Zahlen in Cascadia Mono.
• In der Liste liegt sie über dem Ziehgriff:
  – ⠿ ist nur Bild (IsHitTestVisible = False). Ziehen geht wie bisher an den HeroSplitter, der ganze 20-px-Streifen bleibt Griff.
  – Nur ⌃/⌄ und die Beschriftung sind Schaltfläche.
• In Raster und Regal fehlt ⠿.

NAHT-LINIE (nur eingeklappt)
• 1 px genau auf der Grenze, über die Spaltenbreite, mit 18 px Rand wie die heutige Linie beim Überfahren. Zu den Enden läuft sie aus.
• Nichts aktiv: LineStrong mit 50 %.
• Suche, Quelle ≠ Alle oder ein Filter aktiv: Akzentverlauf C1 → C2 mit 90 %. So ist über die ganze Breite sichtbar, dass gefiltert ist, ohne laut zu sein.
• Statisch, keine Dauer-Animation.

AUSLÖSER
• Maus:
  – Klick auf ⌃ klappt ein, Klick auf die eingeklappte Kapsel klappt aus.
  – Rechtsklick auf ein Element der Leiste öffnet ein Kontextmenü „Werkzeugleiste ausblenden  Strg+Umschalt+L“ als zweiten, großen Weg.
• Tastatur:
  – Strg+Umschalt+L schaltet um. L steht für Leiste; in Excel ist es zufällig auch der Filter-Schalter. Das Kürzel ist frei: Die App nutzt Strg+F, Strg+1/2/3, Esc, Enter, Leertaste, I und Umschalt+F10.
  – Die Kapsel ist ein Tab-Stopp; Enter und Leertaste schalten.
• Suche: Strg+F bei eingeklappter Leiste.
  – Die Leiste klappt mit demselben Morph aus, aber nur vorübergehend, und der Fokus geht in die Suche.
  – Esc leert die Suche wie heute.
  – Ein zweites Esc bei leerer Suche faltet die vorübergehend geöffnete Leiste wieder ein und gibt den Fokus an das gewählte Spiel zurück, wie die Suchleiste im Browser.
  – Vorübergehend heißt: Es wird nicht gemerkt. Ein Klick auf die Kapsel oder Strg+Umschalt+L macht daraus „angeheftet“.
• Alt+A, Alt+N und Alt+H schalten die Filter auch eingeklappt; die Kapsel zeigt es sofort.
• Automatik: keine.
  – Kein Einklappen beim Scrollen. Das passt zu deinem „Nein, Größe bleibt“ beim Titelbereich.
  – Kein Aufklappen beim Überfahren, denn das würde die Liste unter der Maus verschieben.
  – Die einzige Ausnahme ist der Weg über Strg+F, und den löst der Nutzer selbst aus.

AKTIVE FILTER IM EINGEKLAPPTEN ZUSTAND
1. Die Kapsel nennt alles Aktive in der Reihenfolge der Leiste:
  – Suchbegriff („⌕ „tun““, höchstens 12 Zeichen, dann …)
  – Quelle, wenn nicht „Alle“: Punkt in der Quellfarbe und Name
  – aktive Filter als Mini-Symbole in Akzentfarbe (Schalter, NEU, HDR)
  – Treffer, z. B. „3 / 652“
  – Die Sortierung steht nicht drin, sie ist kein Filter.
2. Die Naht leuchtet im Akzentverlauf.
3. Listenkopf („SPIEL · 3“) und Statuszeile zeigen die Menge wie heute.
4. Ist die Liste leer, erscheint ein Hinweis mit „Filter zurücksetzen“ und „Werkzeugleiste einblenden“.
5. Der Tooltip der Kapsel (nach 1 s) fasst zusammen: „Werkzeugleiste einblenden · Aktiv: Suche „tun“, Steam, Neu · 3 von 652 Spielen“.

MERKEN
• Neue Einstellung AppSettings.LibraryToolbarCollapsed (bool). Standard ist false, also ausgeklappt, damit nach dem Update nichts überrascht.
• Ein Zustand für alle drei Ansichten, gespeichert beim Umschalten, wie Ansicht und Titelhöhe.
• Das vorübergehende Öffnen per Strg+F wird nicht gespeichert.

ZIEHGRIFF UND LUFT
• Der Griff bleibt in beiden Zuständen genau auf der Grenze (Mitte seines 20-px-Streifens).
• Ausgeklappt gilt weiter „oben so viel Luft wie unten“: 10 DIP in der Liste, 22 im Raster. Die Kapsel liegt in der Luft darüber, mittig, mit 2 DIP Abstand zur Leiste.
• Eingeklappt beginnt der Inhalt dort, wo heute die Leiste beginnt: 10 DIP unter der Grenze (im Raster 10 + 12). Der Griffstreifen endet genau an der Liste.
• Der Titelbereich behält beim Umschalten seine Höhe („Größe bleibt“). ListHeroRange und GridHeroHeight rechnen immer mit der ausgeklappten Leiste.
• Der gewonnene Platz geht ganz an die Liste: +50 DIP bei einer Zeile, +98 bei zwei, +146 bei drei. Das sind etwa 1, 2 bzw. 3 Listenzeilen à 46.

RASTER
• Wie die Liste, nur ohne ⠿.
• Das Kachelraster rückt um Leistenhöhe + 10 nach oben, der Titelbereich bleibt.

REGAL
• Hier steht die Leiste im Titelbild über der Kachelreihe. Die Naht ist die Unterkante des Titelbilds, also die Oberkante der Kachelreihe; die Kapsel sitzt dort.
• Eingeklappt bleibt die Kachelreihe, wo sie ist.
• Starten, Desktop, Startmenü und das Logo sinken: Der untere Innenrand wird 34 statt 74 (+ zusätzliche Zeilen). Die Bühne wird frei, das Logo bekommt mehr Raum.
• Die Automatik-Karte bleibt unverändert.
• Der Sonderfall „Leiste neben der Karte“ (sehr niedrige Fenster) entfällt eingeklappt.

DETAILBEREICH UND TRENNER
• Bleiben unverändert. Der Trenner beginnt eingeklappt an der Oberkante des Inhalts.

### animationSpec

Bewegungssprache: Ich verwende dieselben Kurven wie im abgenommenen Prototyp und in der App.
• e1 = cubic-bezier(.16,.84,.24,1) aus dem Prototyp, in WPF als KeySpline „0.16,0.84 0.24,1“.
• Feder = Hp.Ease.Spring (BackEase EaseOut, Amplitude 0,35), wie der Knopf der Schalter.
• Exp6 = ExponentialEase EaseOut mit Exponent 6, wie HeroMotion beim Ansichtswechsel.
• Staffelung wie im Prototyp, aber knapper: 16–18 ms statt 40 ms.

Bausteine:
• Leistenelemente (10): Opacity sowie Scale und Translate über eine vorübergehende RenderTransform.
• Band: ein einzelner Glas-Border in einer eigenen Canvas-Ebene über der Spalte (IsHitTestVisible = False). Seine Lage und Größe werden direkt animiert; er misst dabei nur sich selbst, und die Ecken bleiben rund.
• Kapsel: Breite und Opacity ihres Inhalts.
• Naht-Linie: ScaleX ab der Mitte.
• Lichtpunkt: ein 80 px breiter Verlauf, TranslateX.
• Inhalt (Liste, Raster, Kachelreihe): nach dem FLIP-Prinzip. Die Endlage wird sofort angeordnet (genau ein Layout-Durchgang), der Inhalt um ±Δ zurückversetzt (Δ = Leistenhöhe + 10, also 50, 98 oder 146) und dann nur noch verschoben.

VOLL – EINKLAPPEN (etwa 0,82 s, ab 0 ms bedienbar)
• 0 ms:
  – Liegt der Fokus in der Leiste, geht er auf die Kapsel.
  – Die Leiste ist ab jetzt nicht mehr Teil des Layouts, wird aber als Ebene über dem Inhalt weiter gezeichnet (IsHitTestVisible = False, TabNavigation = None).
  – Der Inhalt bekommt TranslateY +Δ und sieht damit unverändert aus.
• 0–260 ms: Die Elemente verdichten sich von außen nach innen, sortiert nach Abstand zur Kapsel, mit 16 ms Staffel.
  – Opacity 1 → 0 in 140 ms (CubicEase In).
  – Scale 1 → 0,86 und Verschiebung um 35 % des Wegs zur Kapselmitte in 220 ms (QuadraticEase In).
• 40–80 ms: Das Band erscheint auf der Fläche der Leiste (Opacity 0 → 0,9).
• 60–580 ms: Der Inhalt steigt auf (+Δ → 0, 520 ms, Exp6). Die Liste gleitet unter den verblassenden Elementen nach oben.
• 80–400 ms: Das Band zieht sich in die Kapsel zusammen (Lage und Größe, 320 ms, KeySpline 0.5,0 0.2,1).
• 400–480 ms:
  – Das Band geht in die Kapsel über (Opacity → 0, 80 ms).
  – Die Kapsel wächst auf ihre Inhaltsbreite (300 ms, Feder).
  – Beschriftung und Zeichen erscheinen mit 30 ms Staffel (Opacity 0 → 1, TranslateX −6 → 0, 180 ms).
• 420–820 ms: „Zündung“.
  – Die Naht-Linie wächst von der Mitte nach außen (ScaleX 0 → 1, 400 ms, e1).
  – Ein Lichtpunkt läuft einmal von der Mitte zu beiden Enden (TranslateX 400 ms, CubicEase Out, Opacity 0 → 1 → 0).
  – Das ist ein kleiner Verwandter des Lichtstreifens im HDR-Moment und des Glanzes auf „Starten“.

VOLL – AUSKLAPPEN (etwa 0,76 s, ab 0 ms bedienbar)
• 0 ms:
  – Die Endlage steht sofort: Die Leiste ist wieder im Layout, ihre Elemente stehen auf Opacity 0.
  – Der Inhalt bekommt TranslateY −Δ und gleitet ab 40 ms in 520 ms (Exp6) nach unten, um Platz zu machen.
  – Die Leiste liegt in der Z-Reihenfolge über dem Inhalt.
• 0–220 ms:
  – Die Naht-Linie zieht sich zur Mitte zusammen (ScaleX 1 → 0, CubicEase In) und blendet aus.
  – Die Beschriftung der Kapsel blendet in 120 ms aus.
  – Die Kapsel schrumpft auf 44 (200 ms, Hp.Ease.Snappy) mit einem kurzen Puls (Scale 1 → 1,08 → 1).
• 120–500 ms: Das Band gießt aus der Kapsel auf die Fläche der Leiste.
  – Waagerecht (Lage und Breite): 360 ms, e1.
  – Senkrecht (Lage und Höhe): 380 ms, BackEase mit Amplitude 0,3; es federt 3–4 % nach.
  – Opacity 0 → 0,9 in 80 ms.
• 300–760 ms: Die Elemente kristallisieren von innen nach außen (nächstes zuerst), mit 18 ms Staffel.
  – Opacity 0 → 1 in 180 ms (CubicEase Out).
  – Scale 0,9 → 1 und TranslateY −8 → 0 in 300 ms (Feder).
• 380–560 ms: Das Band blendet aus (0,9 → 0); die Elemente übernehmen seine Fläche.

REDUZIERT (jeder Übergang höchstens 300 ms; kein Band, keine Staffel, keine Zündung)
• Einklappen:
  – Leiste Opacity 1 → 0 in 150 ms.
  – Inhalt +Δ → 0 in 240 ms (CubicEase Out).
  – Breite der Kapsel in 200 ms.
  – Naht Opacity 0 → 1 in 150 ms.
• Ausklappen spiegelbildlich: Inhalt −Δ → 0 in 240 ms, Leiste Opacity 0 → 1 ab 60 ms in 180 ms.

AUS (auch ohne Hardware-Darstellung oder über Remotedesktop)
• Alle Werte werden direkt gesetzt; es bleibt nur die Änderung des Layouts.
• Fokus und Ergebnis sind identisch zu den anderen Stufen.

WINDOWS-OPTION „ANIMATIONSEFFEKTE“
• Ist sie aus, gilt mindestens „Reduziert“, über EffectPolicy.Effective wie überall.

UNTERBRECHEN UND ANHALTEN
• Wird während des Morphs erneut umgeschaltet, läuft er ab dem aktuellen Wert weiter (HandoffBehavior.SnapshotAndReplace). Der FLIP-Versatz wird aus der aktuellen Lage berechnet, nichts springt.
• Wird das Fenster mitten drin minimiert oder deaktiviert, springt der Morph sofort ans Ende (SkipToFill), wie die übrigen Einmal-Effekte. Im minimierten Fenster startet er nie.

MIKRO-ANIMATIONEN
• Filter-Schalter einschalten:
  – Symbol Scale 0,85 → 1 in 260 ms (Feder).
  – Hof 0 → 0,55 in 200 ms.
  – Indikator ScaleX 0 → 1 in 220 ms (e1).
• Filter-Schalter ausschalten: 150 ms Opacity.
• Der Neu-Punkt erscheint einmal mit Scale 0 → 1,25 → 1 in 320 ms.
• Tooltip: Opacity 0 → 1 und TranslateY 4 → 0 in 140 ms (CubicEase Out).
• Bei „Reduziert“ nur Opacity bis 150 ms, bei „Aus“ sofort.

GPU-KOSTEN
• Animiert werden nur RenderTransform und Opacity auf höchstens 14 kleinen Elementen, dazu ein TranslateTransform auf dem Inhalt.
• Keine Effekte: kein Blur, kein Shadow, keine Mischmodi, keine animierte OpacityMask.
• Opacity-Gruppen entstehen nur für kleine Elemente; die Liste selbst wird nie ausgeblendet, nur verschoben.
• Die Liste wird genau einmal neu angeordnet. Es gibt kein Messen pro Bild, die virtualisierten Zeilen bleiben, wie sie sind.
• Bildrate: EffectPolicy.OneShotFrameRate = 60 für höchstens 0,82 s, also etwa 50 Bilder. Danach 0: FillBehavior.Stop, Endwerte gesetzt, keine Dauer-Uhren. Das Effekt-Protokoll („0 Dauer-Effekte mit Uhren“) und das GPU-Gate im Leerlauf bleiben unverändert.
• Optional CacheMode = BitmapCache auf Band und Elementen, nur während des Morphs.
• Erwartung: ein kurzer Ausschlag wie beim Ansichtswechsel, der heute sogar die Titelhöhe 0,8 s lang über das Layout animiert.

### accessibility

Kapsel (neues Steuerelement ToolbarSeam)
• Eigener AutomationPeer: ControlType Button mit ExpandCollapsePattern (Expanded/Collapsed). AutomationId „ToolbarToggle“, Name „Werkzeugleiste“.
• HelpText „Strg+Umschalt+L blendet die Werkzeugleiste ein oder aus“, AcceleratorKey „Strg+Umschalt+L“.
• ItemStatus im eingeklappten Zustand, z. B. „Aktiv: Suche „tun“, Steam, Neu · 3 von 652 Spielen“, sonst „Keine Filter aktiv“.
• Ändert sich die Trefferzahl im eingeklappten Zustand (etwa per Alt+N), meldet die Kapsel das als LiveRegion (polite).

Tab-Reihenfolge
• Liste: … Bildschirm-Kapsel, Pause, Einstellungen, dann Griff, Kapsel, Leiste, Liste. Raster und Regal: Kapsel direkt vor der Leiste.
• Die Kapsel bleibt auch eingeklappt ein normaler Tab-Stopp, mit Fokusrahmen 2 px in FocusRing.

Fokus geht nie verloren
• Vor dem Einklappen: Liegt der Fokus in der Leiste, geht er auf die Kapsel (Bildschirmleser: „Werkzeugleiste, reduziert“). Die Suche behält Text, Einfügemarke und Auswahl.
• Ausklappen über Kapsel oder Tastenkürzel: Der Fokus bleibt auf der Kapsel, wie bei einem Expander.
• Ausklappen über Strg+F: Der Fokus geht in die Suche.
• Einfalten per Esc: Der Fokus geht auf das gewählte Spiel, ohne Auswahl auf die Liste.
• Während des Morphs ist die Leiste nicht anklickbar (IsHitTestVisible = False) und nicht per Tab erreichbar (KeyboardNavigation.TabNavigation = None). Am Ende wird sie Visibility = Collapsed und verschwindet aus Tab-Reihenfolge und UIA-Baum.

Trefferflächen
• Kapsel: sichtbar 16 hoch, Trefferfläche 24 × mindestens 44 (WCAG 2.5.8). Als großer Zweitweg dient das Kontextmenü „Werkzeugleiste ausblenden“.
• Filter-Schalter: 40 × 40.

Nicht nur Farbe
• Aktive Filter tragen zusätzlich den Indikator-Strich (Form).
• Die leuchtende Naht hat immer die Beschriftung der Kapsel dazu.
• Der Neu-Punkt ist nur ein Hinweis; die Zahl steht im Tooltip und im ItemStatus.

Hoher Kontrast
• Naht und Indikatoren in SystemColors.Highlight, Rahmen der Kapsel in WindowText, keine Verläufe.

Filter-Schalter
• Bleiben ToggleButtons (Toggle-Pattern meldet an/aus).
• Name bleibt der ganze Satz, dazu ItemStatus mit der Zahl („1 Spiel“), HelpText gleich dem Tooltip, AcceleratorKey „Alt+A“, „Alt+N“, „Alt+H“.

Tooltips
• Sie erscheinen auch bei Tastaturfokus (ShowsToolTipOnKeyboardFocus = True) und schließen mit Esc.
• Ihr Inhalt steht zusätzlich im HelpText, Bildschirmleser brauchen den Tooltip also nicht.

Bewegung
• Die Stufen Voll, Reduziert und Aus sowie die Windows-Option werden wie überall beachtet. Ohne Animation funktioniert alles gleich; nichts hängt am Ende einer Animation, Fokus und Zustand werden sofort gesetzt.

Prüfbarkeit
• Das UIA-Gate schaltet über ExpandCollapse um und prüft Fokus, Zeilenzahl und die Abstände von der Grenze zum Inhalt (10 bzw. 22 DIP).
• Außerdem prüft es, dass nach dem Morph keine Uhren mehr laufen.
• Der Zustand ist ohne echte Eingaben setzbar (Tests, Bildschirmfotos).

### risks

1. Die Kapsel ist zu unauffällig. Ausgeklappt ist sie mit 44 × 16 bewusst klein, damit die Luft über der Leiste ruhig bleibt.
   Gegenmittel: Eingeklappt trägt sie die Beschriftung „WERKZEUGE“; dazu Tooltip mit Kürzel, Kontextmenü und Strg+Umschalt+L.
   Falls dir das zu klein ist: 20 DIP Höhe (dann berührt sie die Leiste) oder die Variante „Insel in der Kopfzeile“ neben dem Titel (die Suche bliebe sichtbar). Gemessen ist dort neben dem Titel aber kaum Platz: 3 DIP bei 1000, 167 bei 1280×800 und 123 bei 1600×1000. Die Insel müsste dort den Titel verdrängen, und der Umschalter hätte im ausgeklappten Zustand keinen Platz. Deshalb empfehle ich die Naht.

2. Der Morph wirkt zu verspielt oder zu lang.
   Gegenmittel: höchstens 0,82 s, ab 0 ms bedienbar, bei „Reduziert“ 0,3 s. Dauern und Staffel sind Konstanten und lassen sich nach dem ersten Render feinjustieren.

3. Die leuchtende Naht beißt sich mit manchen Titelbildern.
   Gegenmittel: Akzent nur bei aktiven Filtern, sonst neutral mit 50 %; die Enden laufen aus.

4. Ausgeblendete Filter lassen Spiele scheinbar fehlen.
   Gegenmittel: Inhalt der Kapsel, leuchtende Naht, Listenkopf, Statuszeile und ein Hinweis bei leerer Liste mit „Filter zurücksetzen“.

5. Dir fehlen die Zahlen an den Filtern.
   Gegenmittel: Tooltip, Kopfzeile (ab 1366 DIP Fensterbreite), Neu-Punkt. Wer sie dauerhaft will, bekommt die Zählerkappe als Option.

6. Die Plaketten „NEU“ und „HDR“ mit 8,5 px sind bei 100 % Skalierung klein.
   Gegenmittel: vor dem Bau an einem Render bei 100 und 110 % prüfen; Ersatzsymbole (Funke, Monitor mit HDR-Strich) stehen bereit.

7. Die Einzeiligkeit bei 1920×1080 hängt an 17 DIP. Zieht man den Detailbereich breiter als 413 DIP, bricht die Leiste wieder um. Das ist gewollt, sollte aber bekannt sein.
   Bei 1280–1600 bleibt die Liste zweizeilig; dort ist das Einklappen die eigentliche Antwort.

8. Strg+F verschiebt die Liste, weil die Leiste ausklappt.
   Gegenmittel: Der Nutzer löst es selbst aus, und Esc faltet sie wieder ein (wie die Suchleiste im Browser). Alternative, die ich erst bei Bedarf bauen würde: eine Suche direkt in der Kapsel, ohne Ausklappen.

9. Technik:
   – FLIP mit virtualisierter Liste und Unterbrechungen.
   – Z-Reihenfolge: Die Leiste muss während des Morphs über dem Inhalt liegen.
   – Kapsel und Griff dürfen sich die Treffer nicht streitig machen (⠿ ohne Treffer, ⌃ als Schaltfläche).
   – Zwei Fokus-Wege (Kapsel, Suche).
   Absichern über Unit-Tests für ShowcaseLayout und das UIA-Gate.

10. Der Titelbereich bleibt beim Umschalten gleich hoch, weil er immer mit ausgeklappter Leiste gerechnet wird. Wer ihn groß gezogen hat, sieht eingeklappt keinen größeren Titel. Das ist bewusst so („Größe bleibt“): Der Platz soll der Liste gehören.

11. Neue Einstellung, neue Tastenkürzel (Strg+Umschalt+L, Alt+A/N/H): Sie gehören in die Tastenhinweise der Statuszeile ab 1500 DIP und in die Hilfe.

### effort

Teil 1 – Filter als Symbolschalter und Tooltips: etwa 0,5–1 Tag.
• src/HdrPilot.App/Themes/Library.xaml:
  – neuer Stil Lib.FilterToggle auf Basis von Lib.ToolbarIconButton mit den Zuständen von Lib.Chip
  – Indikator-Strich und Neu-Punkt
  – Lib.ToolTip
  – Mini-Schalter und Plaketten als Vorlagen
• src/HdrPilot.App/MainWindow.xaml:
  – die drei Chips ersetzen, Grow 0
  – Basis der Suche 216
  – Werte des ToolTipService für alle Elemente der Leiste
• src/HdrPilot.App/Resources/LibraryStrings.resx und Designer: Tooltip-Texte und Zählformen
• src/HdrPilot.App/ViewModels/Library/LibraryViewModel.cs: Zähltexte für Tooltip und ItemStatus
• src/HdrPilot.App/MainWindow.xaml.cs: Alt+A/N/H in HandleKey
• Tests:
  – ToolbarFlowLayoutTests: Zeilen für die Breiten 735, 813, 1026, 1256 und 2643 sowie Regal 1099–3063
  – tools/gates/phase10-ui.ps1: Namen, ItemStatus, eine Zeile bei 1920
  – neue Bildschirmfotos

Teil 2 – Lichtnaht: etwa 2,5–3 Tage.
• Neu:
  – src/HdrPilot.App/Views/Library/ToolbarSeam.cs: die Kapsel mit Peer und ExpandCollapse
  – src/HdrPilot.App/Views/Library/ToolbarMorph.cs: FLIP, Band, Staffel, Zündung, Stufen, Unterbrechen
• Geändert:
  – ShowcaseLayout.cs: Parameter „Leiste sichtbar“ für ToolbarTop, LargeHeroInset und Luft; die Titelhöhe bleibt mit ausgeklappter Leiste gerechnet
  – MainWindow.xaml: Canvas-Ebene für Band und Naht, Kapsel über dem HeroSplitter, Z-Reihenfolge
  – MainWindow.xaml.cs: Zustand, ApplyVerticalLayout, Fokus, Strg+Umschalt+L, Strg+F und Esc, Kontextmenü
  – src/HdrPilot.Core/Config/AppConfig.cs: LibraryToolbarCollapsed
  – LibraryViewModel: IsToolbarCollapsed und eine Zusammenfassung der Filter für Kapsel und UIA
  – Themes/Library.xaml: Kapsel, Naht, Lichtpunkt
  – LibraryStrings.resx
• Tests:
  – Unit-Tests für ShowcaseLayout (Luft 10 bzw. 22, Titelhöhe unverändert, Regal-Innenrand 34)
  – UIA-Gate: umschalten, Fokus, Abstände, 0 Uhren
  – phase10-gpu: Messung während des Morphs und im Leerlauf
• Dokumentation: Entscheidung in docs/design/showcase-hero/README.md und in GATES.md festhalten.

Empfohlene Reihenfolge: zuerst Teil 1, klein und sofort sichtbar. Für Teil 2 vor dem Bau zur Abnahme eine Bildfolge oder ein kurzes Video aus einem HTML-Prototyp im Stil von app.js, mit Liste und Regal bei 1366 und 1920.

## Konzept 2 (utility): Faltleiste

### filterButtons

Kurz: Ich bin dafür, mit zwei Ergänzungen.

WARUM JA
- Die drei Chips sind heute 133 + 95 + 139 = 367 DIP breit, also ein Viertel der Leiste.
- Ihre Zähler stehen ohnehin doppelt da: in der Kopfzeile ("652 Spiele · 0 mit HDR automatisch · 652 neu", ab 1366 px), im Listenkopf ("SPIEL · 652") und in der Statuszeile ("652 von 652").
- Nach ein paar Tagen liest man das Wort "Automatik" nicht mehr, man kennt die Stelle.
- Im abgenommenen Prototyp v4 stand "Spiel hinzufügen" in der Liste schon nur als "+".

ERGÄNZUNG 1: EINE GRUPPE STATT DREI LOSER QUADRATE
Sonst stehen fünf gleiche 40-px-Quadrate nebeneinander: Automatik, Neu, HDR-fähig, Neu einlesen, gesehen. Dann sieht man nicht mehr, was ein Schalter ist (bleibt an) und was eine Aktion (löst einmal aus). Die Gruppe "Schnellfilter" bekommt denselben Rahmen wie die Ansicht-Gruppe. Man kann aber mehrere wählen, und "an" sieht anders aus (getönt statt Farbverlauf).

ERGÄNZUNG 2: DIE RECHNUNG
Kleinere Filter allein bringen die Leiste bei 1366 bis 1920 px nicht in eine Zeile. Die Leiste schrumpft von 1507 auf 1260 DIP. Die Liste ist bei 1920 aber nur etwa 1256 DIP breit (Details unter "Breitenanalyse"). Deshalb schlage ich zusätzlich das Ein- und Ausklappen vor (Faltleiste).

SPEZIFIKATION
- Gruppe:
  - Rahmen wie Lib.SegmentFrame: Höhe 40, Ecken 12, Innenabstand 3, Glas, Linie 1 px.
  - Breite 122 (8 + 3 × 38), feste Breite (Grow 0). Insgesamt 122 statt 367 DIP.
  - Steht direkt rechts neben der Suche, in beiden Zuständen der Leiste an derselben Stelle.
- Knopf:
  - 38 × 34, Ecken 9, Symbol 16 px als Linien-Ikone (Strich 1,8) in TextSecondary, kein Abstand innerhalb der Gruppe.
  - Hover: Fläche Surface2, Symbol in Textfarbe.
  - Gedrückt: Skalierung 0,94.
  - An: Fläche C1 18 %, Rand 1 px AccentStrong, Symbol AccentText und zusätzlich gefüllt. So ist "an" nicht nur an der Farbe erkennbar.
  - Fokus: Ring 1,5 px FocusRing wie bei Lib.SegmentRadio.
- Symbole (müssen ohne Text eindeutig sein):
  - Automatik: Sonne mit kleinem "A", wie der Automatik-Modus einer Kamera. Die nackte Sonne (E706) steht schon auf "HDR einschalten" und wäre doppeldeutig.
  - Neu: Funkeln ✦ (vierzackiger Stern). Der Stern E734 bedeutet fast überall "Favorit". Eingeschaltet gefüllt.
  - HDR-fähig: kleines "HDR"-Kennzeichen (Rahmen 20 × 13, Text 8 px fett), wie die HDR-Kennzeichen in Liste und Titelbereich. Eingeschaltet gefüllt bzw. negativ.
- Zähler: keine Zahlen mehr im Knopf. "652" auf 38 px ist Unruhe, und nach dem ersten Einlesen stünde ohnehin "Neu 652" da. Stattdessen:
  - "Neu" bekommt einen 7-px-Punkt in Akzentfarbe oben rechts, solange es neue Spiele gibt und der Filter aus ist (wie "ungelesen").
  - Die Zahlen stehen im Tooltip, im Namen für Screenreader und als Trefferzahl ("36 von 652") in der eingeklappten Leiste, im Listenkopf und in der Statuszeile.
- Tooltip (deine 1 bis 2 Sekunden):
  - Verzögerung 1000 ms (ToolTipService.InitialShowDelay). 2 s fühlen sich an wie "kein Tooltip"; der Höhen-Griff hat heute 900 ms.
  - BetweenShowDelay 400 ms: Fährt man danach zum Nachbarknopf, erscheint dessen Erklärung sofort. So kann man die Gruppe in einem Zug ablesen.
  - ShowDuration 20 s, ShowsToolTipOnKeyboardFocus = True, Placement Bottom, Abstand 8.
  - Eigenes Aussehen statt Windows-Standard (in Themes gibt es noch keinen ToolTip-Stil): neuer Stil Lib.ToolTip wie Lib.PopupBody. Deckend Hp.Brush.Menu, Ecken 10, Rand LineStrong, Innenabstand 12/9, Schatten als Lib.PopupShadow-Fläche, HasDropShadow False, kein Effekt, MaxWidth 300.
  - Drei Zeilen: Titel (13, SemiBold) mit Tastenkappe rechts; Erklärung (12, TextSecondary); Zahl und Zustand (Mono 11, TextMuted).
  - Automatik: "Automatik [Alt+A]" / "Nur Spiele mit „HDR automatisch“ zeigen." / "28 Spiele · aus"
  - Neu: "Neu [Alt+N]" / "Nur neu gefundene Spiele zeigen." / "1 neues Spiel · aus"
  - HDR-fähig: "HDR-fähig [Alt+H]" / "Nur Spiele mit HDR laut PCGamingWiki zeigen (auch eingeschränkt)." / "36 Spiele · aus"
  - Ist der Filter an, lautet Zeile 3: "Filter an · 36 von 652 · Klick hebt ihn auf".
- Tastatur:
  - Die Gruppe ist ein einziger Tab-Halt (KeyboardNavigation.TabNavigation=Once). ←/→ wechseln, Leertaste oder Enter schaltet.
  - Alt+A, Alt+N und Alt+H schalten die Filter aus dem ganzen Fenster, auch aus der Suche heraus.
- Rechtsklick auf die Gruppe: Menü im Stil Lib.ContextMenu mit ausgeschriebenen Namen, Häkchen, Zahl und Kürzel, dazu "Alle Filter zurücksetzen". Das ist der Weg ohne Hover.
- Zum Vergleich mit dem Prototyp: "Spiel hinzufügen" nur als "+" (40), "Ansicht" behält ihre Beschriftung.

### widthAnalysis

GRUNDLAGE
Alle Werte in DIP bei 110 %. Innenabstände und Basisbreiten stammen aus MainWindow.xaml und Themes/Library.xaml. Die Textbreiten habe ich mit WPF FormattedText gemessen (Segoe UI Variable Text SemiBold 13, Cascadia Mono SemiBold 10,5). ToolbarFlowPanel rundet jede natürliche Breite auf. Abstand und Zeilenabstand sind je 8. Höhe: 1 Zeile 40, 2 Zeilen 88, 3 Zeilen 136.

BREITE JE ELEMENT
- Suche: Basis 236 (in einer Zeile wächst sie bis 320)
- Quelle: Basis 188. Der Inhalt "● Alle 652" braucht nur 12+8+9+22,4+10+7+18,5+7+34 = 127,9; selbst "Battle.net" etwa 150.
- Sortierung: Basis 196. Längster Eintrag "HDR-Unterstützung": 12+13+9+117,0+34 = 185,0.
- Chip Automatik (Zähler 0): 11+13+8+62,0+8+7+6,2+7+10 = 132,1 → 133
- Chip Neu (Zähler 1): 11+13+8+24,5+8+14+6,2+10 = 94,6 → 95
- Chip HDR-fähig (Zähler 36): 11+13+8+62,3+8+14+12,3+10 = 138,6 → 139
- Die drei Chips zusammen: 367
- Ansicht mit Text: Rahmen 2×(1+3) = 8, dazu Liste 10+15+6+26,0+10 = 67, Raster 77, Regal 73 → 225
- Ansicht nur mit Symbolen: 8 + 3×38 = 122
- Neu einlesen 40, gesehen 40
- Spiel hinzufügen: 11+13,3+8+98,8+11 = 142,0 → 143. Nur "+": 40
- NEU Schnellfilter-Gruppe: 8 + 3×38 = 122 (drei einzelne 40er bräuchten 3×40 + 2×8 = 136)
- NEU Faltlasche: 40 + Abstand 8 = 48, als eigene Spalte rechts
- NEU Marken in der eingeklappten Leiste (Höhe 28, Text 12 SemiBold):
  - "● Steam ×" = 9+12+33,8+6+9+7 = 77
  - "◷ Name ×" = 81
  - "◷ HDR-Unterstützung ×" = 156 (kann gekürzt werden)
  - Zähler "36 von 652 ×" = 10+61,5+6+9+8 = 95

BREITE DER LISTE
Formel: Fensterbreite/1,1 − 64 (Ränder) − 24 (Trenner) − Detailbereich. Der Detailbereich hat seine Standardbreite clamp(0,23·W; 340; 396). In den Renders ist die Leiste jeweils etwa 5 DIP schmaler; bei 1920 reicht sie von 38 bis 1419 px, also 1381 px = 1255,5 DIP.
- 1280×800: 1163,6 − 88 − 340 = 735,6 (Render 731)
- 1366×768: 1241,8 − 88 − 340 = 813,8 (Render 809)
- 1600×1000: 1454,5 − 88 − 340 = 1026,5 (etwa 1022)
- 1920×1080: 1745,5 − 88 − 396 = 1261,5 (Render 1256)
- 3440×1440: 3127,3 − 88 − 396 = 2643,3 (etwa 2638)
- Regal (volle Breite, W/1,1 − 64): 1100 / 1178 / 1390 / 1681 / 3063

BREITE FÜR EINE ZEILE, JE VARIANTE
- A, heute: 236+188+196+133+95+139+225+40+40+143 = 1435, dazu 9 Abstände à 8 = 1507
- B, nur dein Wunsch (drei einzelne 40er): 1435 − 367 + 120 = 1188, dazu 72 = 1260
- C, mein Vorschlag ausgeklappt (Suche, Schnellfilter 122, Quelle, Sortierung, Ansicht mit Text 225, ⟳, ◉, "+" 40): 1087, dazu 7×8 = 1143. Mit Lasche (+48) braucht die Liste mindestens 1191.
- D, wie C, aber Ansicht nur mit Symbolen: 984 + 56 = 1040. Mit Lasche mindestens 1088.
- E, mein Vorschlag eingeklappt: Suche (180–320) + 8 + Gruppe 122 + Marken und Zähler (werden gekürzt) + 48. Mindestens 366, bricht nie um.

ZEILEN IN LISTE UND RASTER (mit den Breiten aus den Renders)
Reihenfolge: 1280 | 1366 | 1600 | 1920 | 3440
- A: 3 | 2 | 2 | 2 | 1. Eine Zeile erst ab etwa 2200 px Bildschirmbreite.
- B: 2 | 2 | 2 | 2* | 1. *1260 gegen 1256: 4 DIP zu wenig, rechnerisch (1261) passt es gerade. Die Leiste wechselt also auf dieser Kante.
- C: 2 | 2 | 2 | 1 | 1. Eine Zeile ab etwa 1850 px.
- D: 2 | 2 | 2 | 1 | 1. Eine Zeile ab etwa 1700 px.
- E: immer 1 Zeile.

ZEILEN IM REGAL
- A: 2 | 2 | 2 | 1 | 1
- B: 2 | 2 | 1 | 1 | 1
- C: 2 | 2 | 1 | 1 | 1. Bei 1366 fehlen 13 DIP; eine Zeile ab etwa 1380 px.
- D: immer 1 Zeile
- E: immer 1 Zeile

AUFTEILUNG VON C BEI 1280 BIS 1600
ToolbarFlowPanel verteilt ausgewogen:
- Zeile 1: Suche, Schnellfilter, Quelle = 236+8+122+8+188 = 562
- Zeile 2: Sortierung, Ansicht, ⟳, ◉, + = 196+8+225+8+40+8+40+8+40 = 573
Beide Zeilen werden auf die Breite minus 48 gestreckt. Zeile 1 enthält damit genau den Teil, der eingeklappt sichtbar bleibt.

EINGEKLAPPT IM SCHMALSTEN FALL (1280)
- Platz: 731 − 48 = 683. Suche 236 + 8 + Gruppe 122 + 8 = 374. Für Marken und Zähler bleiben 309.
- Normalfall: Steam 77 + 8 + Name 81 + 8 + Zähler 95 = 269, passt.
- Schlimmster Fall: Battle.net 96 + HDR-Unterstützung 156 + Zähler 95 + 24 = 371. Dann kürzt sich die Sortier-Marke auf 120, und die Suche gibt bis 180 nach. Es bleibt immer eine Zeile.

HÖHENGEWINN
Von 2 auf 1 Zeile sind es 48 px, also genau eine Listenzeile (46). Bei 1280 sind es heute von 3 auf 1 Zeile 96 px.

FAZIT
- Kleinere Filter sparen 247 DIP, reichen aber bei 1366 bis 1920 nicht für eine Zeile: Die Liste ist dort 731 bis 1256 breit, gebraucht werden 1260.
- Mit "+" als Symbol (wie im Prototyp) passt bei 1920 alles in eine Zeile.
- Bei 1280 bis 1600 ist eine Zeile mit beschrifteter Quelle, Sortierung und Ansicht rechnerisch nicht möglich. Das löst nur das Einklappen.
- Optional: Wie im Prototyp in der Liste über die Spaltenköpfe sortieren (Sortierung weg, −204). Dann ist die Liste schon ab 1600 einzeilig. Dafür hätten die Ansichten aber unterschiedliche Leisten.

### hideShowConcept

IDEE
"Faltleiste": Die Leiste hat zwei Zustände. Du wählst selbst, die App merkt sich die Wahl.
- Eingeklappt bleibt genau eine Zeile mit allem zum Finden: Suche und Schnellfilter. Dazu kommen Marken für alles Verborgene, das nicht auf Standard steht.
- Ausgeklappt kommt der Teil "Ordnen und Verwalten" dazu: Sortierung, Ansicht, Neu einlesen, gesehen, +.
- Umgeschaltet wird mit einer Faltlasche am rechten Rand, mit Strg+Umschalt+F oder per Rechtsklick, nie automatisch.

VORBILDER UND WAS ICH ÜBERNEHME
- Notion: Filter- und Sortierleiste, aktive Filter als Pillen, per Klick ein- und ausblendbar → Marken für aktive, verborgene Einstellungen.
- Windows 11 Explorer (Bereiche per Tastenkürzel, Zustand gemerkt) und Arc (Seitenleiste mit Strg+S) → ein Kürzel und ein gemerkter Zustand.
- Linear und Raycast (alles über Strg+K, eigene Kürzel für Filter) → Alt-Kürzel je Filter, Befehlspalette als Ausbaustufe.
- Apple Music und Spotify: Die Suche ist nie versteckt → sie bleibt in beiden Zuständen sichtbar.
- Bewusst nicht übernommen:
  - Arcs Einblenden beim Überfahren: Es verschiebt die Liste oder verdeckt die obersten Treffer.
  - Einklappen beim Scrollen: Das hast du beim Titelbereich abgelehnt.

EINGEKLAPPT (1366×768, Liste, eine Zeile = 40 px)
┌──────────────────────────┐┌─────────────┐╭─────────╮╭────────╮       ╭──────────────╮┌───┐
│ ⌕ Suchen …        Strg+F ││ ☀ᴬ │ ✦• │ HDR ││● Steam ×││◷ Name ×│       │ 36 von 652 × ││ ⌄ │
└──────────────────────────┘└─────────────┘╰─────────╯╰────────╯       ╰──────────────╯└───┘
- Marken erscheinen nur, wenn etwas vom Standard abweicht: Quelle nicht "Alle", Sortierung nicht "Zuletzt gespielt", oder "⟳ Liest ein …" während des Einlesens.
- Klick auf eine Marke klappt die Leiste auf und öffnet genau diese Auswahl. × setzt die Einstellung zurück.
- Die Trefferzahl steht rechts und nur, wenn gefiltert oder gesucht wird. Ihr × setzt alle Filter und die Suche zurück.
- ✦• zeigt mit dem Punkt: Es gibt neue Spiele.
- Wird es eng, kürzen sich die Marken (erst Text mit "…", zuletzt nur Symbol), und die Suche gibt bis 180 nach. Eingeklappt bricht die Leiste nie um.

AUSGEKLAPPT (gleiche Breite, zwei Zeilen = 88 px)
┌───────────────────────────┐┌─────────────┐┌──────────────────────┐┌───┐
│ ⌕ Suchen …         Strg+F ││ ☀ᴬ │ ✦ │ HDR ││ ● Alle           652 ⌄││ ⌃ │
└───────────────────────────┘└─────────────┘└──────────────────────┘│   │
┌──────────────────────┐┌──────────────────────────────┐┌──┐┌──┐┌──┐│   │
│ ◷ Zuletzt gespielt  ⌄││ ☰ Liste │ ▦ Raster │ ▥ Regal  ││⟳ ││◉ ││+ ││   │
└──────────────────────┘└──────────────────────────────┘└──┘└──┘└──┘└───┘
- Ab etwa 1850 px (Liste) bzw. 1380 px (Regal) passt ausgeklappt alles in eine Zeile:
  [Suche][Schnellfilter][Quelle][Sortierung] …… [Ansicht][⟳][◉][+][⌃]

NEUE REIHENFOLGE
Suche · Schnellfilter · Quelle | Sortierung · Ansicht · ⟳ · ◉ · +
- Die Schnellfilter stehen damit in beiden Zuständen und bei jeder Breite in Zeile 1 direkt neben der Suche. Sie springen nie, die Hand findet sie immer an derselben Stelle.
- Der Zeilenumbruch liegt zwischen "Finden" und "Ordnen und Verwalten". Bei 1280 bis 1600 ergibt das ausgewogene Umbrechen genau diese Aufteilung (562 | 573). Optional bekommt die Sortierung den Hinweis "hier bevorzugt umbrechen".

WO DER SCHALTER SITZT
- Die Faltlasche ist eine eigene Spalte rechts außen, 40 breit und so hoch wie die Leiste (40 / 88). Der Pfeil sitzt immer auf Höhe der ersten Zeile.
- Sie steht also in beiden Zuständen an derselben Stelle und rutscht bei zwei Zeilen nicht in die zweite.
- Ein Pfeil (E70D) zeigt eingeklappt nach unten und ist ausgeklappt um 180° gedreht.
- Tooltip:
  - Ausgeklappt: "Leiste einklappen [Strg+Umschalt+F] / Sortierung, Ansicht und Aktionen ausblenden. Suche und Schnellfilter bleiben."
  - Eingeklappt: "Leiste ausklappen [Strg+Umschalt+F] / Sortierung, Ansicht, Neu einlesen und Spiel hinzufügen einblenden."

AUSLÖSER
- Maus: Klick auf die Lasche, oder Klick auf eine Marke (klappt auf und öffnet die Auswahl).
- Tastatur:
  - Strg+Umschalt+F schaltet von überall um, auch aus der Suche heraus ("F" wie Filter, Nachbar von Strg+F).
  - Strg+F setzt den Fokus wie bisher in die Suche. Die Suche ist in beiden Zuständen da, also muss nichts aufklappen und nichts springt beim Tippen.
  - Alt+A, Alt+N, Alt+H schalten die Filter.
  - Neu: Esc in der leeren Suche setzt den Fokus in die Liste (mit Text leert Esc die Suche wie bisher). ↓ in der Suche springt zum ersten Treffer.
- Rechtsklick auf eine freie Stelle der Leiste, auf die Lasche oder auf eine Marke öffnet ein Menü:
  - "Leiste einklappen/ausklappen (Strg+Umschalt+F)"
  - die drei Filter mit Häkchen
  - "Alle Filter zurücksetzen"
- Ausbaustufe Befehlspalette (Strg+K): Ein Fenster "Befehle" (deckend, oben mittig) sucht über alle Befehle der Leiste und über die Spiele, jeweils mit Häkchen und Kürzel. Beispiele: "Filter: Neu", "Quelle: Steam", "Sortieren: Name", "Ansicht: Regal", "Neu einlesen", "Spiel hinzufügen …", "Leiste einklappen", "Starten: TUNIC". Mit der Tastatur braucht man die ausgeklappte Leiste dann nie.
- Nichts passiert automatisch: kein Einklappen beim Scrollen, kein Einblenden beim Überfahren, kein Umschalten nach Fensterbreite.

FILTERZUSTAND, WENN EINGEKLAPPT
- Schnellfilter sind direkt sichtbar (getönt, gefüllte Symbole).
- Quelle und Sortierung erscheinen als Marken, die Suche steht als Text im Feld.
- Rechts steht die Trefferzahl, ein laufendes Einlesen als Marke.
- Nichts, was gerade wirkt, ist unsichtbar.

GEMERKT
- AppConfig.LibraryToolbarCollapsed (bool). Standard ist ausgeklappt, damit Neue alles sehen.
- Eine Einstellung für alle drei Ansichten, gespeichert beim Umschalten (wie LibraryHeroRatio).

ZIEHGRIFF UND LUFT
- Eingeklappt ist die Leiste genau eine Zeile: 40 px = ShowcaseLayout.ToolbarHeight, ToolbarExtra = 0.
- Alle vorhandenen Formeln gelten unverändert: ToolbarTop, ListHeroRange, GridHeroHeight, LargeHeroInset, ShelfToolbarBesideCard.
- Die Luft oben bleibt gleich der Luft unten: Liste 10/10, Raster 22/22.
- Der Ziehgriff bleibt genau auf der Grenze. Der Titelbereich der Liste behält deine Höhe ("Größe bleibt"). Nur die Liste wird 48 px höher, und der Griff hat 48 px mehr Spielraum (ListHeroRange.Max).

RASTER
- Wie in der Liste: Die Leiste steht in der Listenspalte, Luft 22/22.
- In niedrigen Fenstern hat der Titelbereich wegen der Leiste nachgegeben (GridHeroHeight). Beim Einklappen bekommt er diese Höhe zurück. Titelbereich und Raster bewegen sich dann gemeinsam mit derselben Kurve.

REGAL
- Die Leiste liegt in voller Breite über dem unteren Rand des Titelbilds.
- Eingeklappt (eine Zeile) wird mehr vom Bild frei: Starten und Desktop rücken 48 px tiefer (LargeHeroInset 74 statt 122), das Logo bekommt mehr Platz. Die Kachelreihe bleibt, wo sie ist.
- "Leiste neben der Karte" (in sehr niedrigen Fenstern) entfällt eingeklappt.
- Damit wirkt das Regal wie eine Kinoleinwand: viel Bild, wenig Bedienung.

### animationSpec

GRUNDSATZ
- Pro Umschalten wird das Layout genau einmal neu berechnet. So rechnen Titelbereich, Griff, Trenner und Listenspalten nur einmal; ToolbarArea.SizeChanged feuert nicht bei jedem Bild.
- Die sichtbare Bewegung entsteht nur über Opacity und RenderTransform. Die Liste gleitet per TranslateTransform (Zeilen 1 und 2 von ListArea, ClipToBounds), im Regal entsprechend HeroLarge.
- Die Dauer kommt immer aus MotionController.Transition(...), die XAML-Trigger hängen an LibraryMotion.Instance.IsEnabled.
- Alles sind einmalige Übergänge: FillBehavior=Stop, Endwert lokal gesetzt. Nach höchstens 0,8 s laufen keine Uhren mehr; phase10-gpu.ps1 bleibt bei 0.

STUFE VOLL: EINKLAPPEN (etwa 440 ms, Lichtkante bis 720 ms)
1. t=0:
   - Fokus in Sicherheit bringen und offene Auswahllisten schließen.
   - Positionen von Quelle und Sortierung merken (für den Flug der Marken).
2. t=0 bis 160 ms, die Elemente der zweiten Zeile:
   - Opacity 1→0, TranslateY 0→−10, ScaleY 1→0,92 (Ursprung oben)
   - je 120 ms, CubicEase EaseIn, um 10 ms versetzt von rechts nach links
3. t=160 ms, Layout auf eine Zeile setzen. Gleichzeitig:
   - Die Liste startet mit TranslateY +48 (alte Lage) und gleitet auf 0: 260 ms, ExponentialEase EaseOut, Exponent 5 (wie HeroMotion).
   - Lasche: Der Hintergrund ist ein eigenes Element ohne Einfluss auf das Layout. Height 88→40, 260 ms ExpoOut.
   - Lasche: Pfeil RotateTransform 180°→0°, 220 ms, CubicEase Out.
   - Marken fliegen aus der gemerkten Lage ihres Bedienelements an ihren Platz (FLIP): TranslateX/Y Δ→0, 280 ms ExponentialEase Out (Exponent 4), Opacity 0→1 in den ersten 120 ms, um 30 ms versetzt.
4. t=200 ms, Lichtkante:
   - Ein 2-px-Streifen an der Unterkante der Leiste, IsHitTestVisible=false.
   - LinearGradient transparent → AccentStrong 60 % → transparent, 160 px breit.
   - TranslateX von −160 bis zur vollen Breite, 520 ms, SineEase InOut, Opacity 0→0,8→0.
   - Sie greift das Licht des HDR-Moments auf.

STUFE VOLL: AUSKLAPPEN (etwa 420 ms)
1. t=0 bis 100 ms: Marken Opacity 1→0, Scale 1→0,9 (Ursprung links).
2. t=100 ms, Layout auf zwei Zeilen setzen. Die neuen Elemente sind sofort Visible mit Opacity 0, also sofort per Tab erreichbar. Gleichzeitig:
   - Die Liste gleitet mit TranslateY von −48 auf 0, 280 ms ExpoOut.
   - Lasche Height 40→88, 280 ms ExpoOut; Pfeil 0°→180°, 220 ms CubicOut.
3. Die zweite Zeile entfaltet sich:
   - Opacity 0→1, TranslateY −12→0, ScaleY 0,92→1 (Ursprung oben)
   - je 220 ms CubicEase Out, um 25 ms versetzt von links nach rechts (5 Elemente, Ende bei t≈420)
   - Ist alles einzeilig, kommt der rechte Teil stattdessen mit TranslateX −12→0.
4. Quelle und Sortierung, falls sie eine Marke hatten: Der Rahmen in AccentStrong pulsiert einmal (Opacity 0→1→0, 400 ms). Das verbindet die Marke sichtbar mit ihrem Bedienelement.
5. Lichtkante wie beim Einklappen.

STUFE VOLL: KLEINERE BEWEGUNGEN
- Marke kommt dazu (zum Beispiel neue Quelle per Kontextmenü):
  - Scale 0,85→1 und Opacity 0→1, 180 ms, BackEase Out, Amplitude 0,3
- Marke geht:
  - Opacity und Scale auf 0,9, 120 ms
  - die folgenden Marken rutschen nach (FLIP TranslateX, 160 ms CubicOut)
- Trefferzahl ändert sich: die Zahl blendet über (Opacity 0→1, TranslateY 4→0, 140 ms).
- Schnellfilter wird eingeschaltet:
  - Ebene "An" Opacity 0→1 in 200 ms CubicOut (wie bei den Chips heute)
  - Symbol blendet von Umriss zu gefüllt über, 140 ms
  - Druck Scale 0,94→1, 120 ms
- Punkt an "Neu" erscheint mit Scale 0→1, 200 ms BackEase.

STUFE REDUZIERT (auch wenn Windows "Animationseffekte" aus ist; dann gilt höchstens Reduziert)
- Kein Versatz, kein Flug der Marken, keine Lichtkante, keine Skalierung, kein Gleiten der Liste. Das Layout springt sofort.
- Nur die Teile, die erscheinen oder verschwinden, und die Marken blenden über Opacity in 150 ms über (EffectPolicy begrenzt Reduziert auf 300 ms).
- Der Pfeil springt in seine neue Lage.

STUFE AUS
Alles sofort, Dauer 0.

ANHALTEN
Ist das Fenster minimiert oder unsichtbar oder läuft ein Spiel mit "pausieren beim Spielen", liefert MotionController.OneShotAllowed false. Transition() gibt dann 0 zurück, und alles springt sofort. Es gibt keinen Dauer-Effekt, also nichts, was auf 30 fps begrenzt werden müsste. Die einmaligen Übergänge laufen mit der Bildrate des Bildschirms (WPF-Standard 60).

GPU-LAST
- Bewegt werden nur Opacity und Translate/Scale/Rotate an höchstens 12 kleinen Elementen, dazu ein 2-px-Streifen und eine Rahmenhöhe in einem Element ohne Layouteinfluss.
- Keine Effekte (Blur, Schatten), keine OpacityMask, keine Mischmodi.
- Die Bewegung der Liste ist ein Transform: WPF setzt das vorhandene Bild der Liste nur neu zusammen (etwa 17 Bilder in 280 ms) und muss die Zeilen nicht neu messen.
- Erwartet: GPU-Last im einstelligen Prozentbereich für unter eine halbe Sekunde, danach null.

### accessibility

TASTATUR
- Tab-Reihenfolge ausgeklappt: Höhen-Griff → Suche → Schnellfilter (ein Halt, ←/→, Leertaste) → Quelle → Sortierung → Ansicht (←/→) → Neu einlesen → gesehen → + → Faltlasche → Liste.
- Tab-Reihenfolge eingeklappt: Höhen-Griff → Suche → (Löschen-×) → Schnellfilter → je Marke Körper und × → × der Trefferzahl → Faltlasche → Liste.
- Die Lasche ist in beiden Zuständen der letzte Halt der Leiste, so wie sie auch optisch rechts außen steht.
- Kürzel: Strg+F (Suche), Strg+Umschalt+F (Leiste), Alt+A / Alt+N / Alt+H (Filter), Strg+1/2/3 (Ansicht, wie bisher). Neu: Esc in der leeren Suche setzt den Fokus in die Liste, ↓ springt zum ersten Treffer. Strg+K (Befehlspalette) als Ausbaustufe.
- Alle Kürzel gelten nur im aktiven Fenster (OnPreviewKeyDown/HandleKey) und funktionieren auch, wenn die Suche den Fokus hat.

FOKUS GEHT NIE VERLOREN
- Bevor eingeklappt wird: Liegt der Fokus in einem Element, das verschwindet (IsKeyboardFocusWithin), geht er erst auf die Faltlasche (Keyboard.Focus). Offene Auswahllisten werden vorher geschlossen.
- Verborgene Elemente bekommen erst am Ende der Bewegung Visibility=Collapsed. So kommt man nie per Tab auf unsichtbare Elemente, und der Screenreader liest sie nicht vor.
- Beim Ausklappen sind die Elemente sofort Visible und damit sofort erreichbar.
- Wird eine Marke per × oder Entf entfernt, geht der Fokus zur nächsten Marke, sonst zur vorigen, sonst in die Suche.
- Ein zweites Umschalten während der Bewegung kehrt sie um (HandoffBehavior.SnapshotAndReplace). Die Leiste ist nie gesperrt.
- Die Suche selbst verschwindet nie, also wird ihr Fokus nie verschoben.

SCREENREADER (UI Automation)
- Faltlasche:
  - ToggleButton mit eigenem AutomationPeer, nach dem Vorbild von ToolbarPeer in ToolbarFlowPanel.cs. Er bietet ExpandCollapse an.
  - Name "Werkzeugleiste", Zustand erweitert/reduziert, AcceleratorKey "Strg+Umschalt+F".
  - Beim Umschalten das Zustandsereignis auslösen, damit der Screenreader "reduziert/erweitert" ansagt. Dazu eine UIA-Mitteilung (AutomationPeer.RaiseNotificationEvent): "Leiste eingeklappt. Suche und Schnellfilter bleiben sichtbar."
- Schnellfilter:
  - Gruppe "Schnellfilter" (GroupBorder, Steuerelementtyp Group).
  - Die Knöpfe behalten die heutigen Namen ("Nur Spiele mit HDR automatisch", "Nur neue Spiele", "Nur Spiele mit HDR laut PCGamingWiki …"). Den Zustand an/aus meldet das Toggle-Muster.
  - Die Zahl steht in ItemStatus ("28 Spiele", "1 neues Spiel"), das Kürzel in AcceleratorKey ("Alt+A").
- Marken:
  - Schaltflächen mit Namen wie "Quelle: Steam, Auswahl öffnen".
  - Das × heißt "Quelle Steam zurücksetzen" bzw. "Sortierung Name zurücksetzen".
- Trefferzahl: AutomationProperties.LiveSetting=Polite. Sie meldet "36 von 652" nach einer Filteränderung, mit 300 ms Verzögerung gebündelt.
- Tooltips zeigen sich auch bei Tastaturfokus (ShowsToolTipOnKeyboardFocus). Ihr Inhalt steht zusätzlich in HelpText. Die Erklärung hängt also nie allein am Überfahren mit der Maus.
- Für Maus ohne Hover und für Lesende gibt es das Rechtsklick-Menü mit ausgeschriebenen Namen.

SICHTBARKEIT
- "An" ist nicht nur Farbe: gefülltes Symbol und zusätzlich Rand.
- Der Punkt an "Neu" steht zusätzlich im Namen und im Tooltip.
- Hoher Kontrast (SystemParameters.HighContrast): Symbole und Rahmen nutzen die Systemfarben. Die gefüllte Symbolvariante bleibt das Erkennungszeichen für "an".
- Kontrast der Symbole (TextSecondary auf Glas) mindestens 3:1 (WCAG 1.4.11).
- Klickflächen: Filter 38×34, Lasche mindestens 40×40, × der Marken mit 24×24 Trefferfläche (WCAG 2.5.8).
- Fokusring 1,5 bis 2 px FocusRing wie bei den übrigen Elementen der Leiste.

BEWEGUNG
Die Effektstufen Voll, Reduziert und Aus werden beachtet. Ist "Animationseffekte" in Windows aus, gilt höchstens Reduziert: nur Überblenden, keine Bewegung im Raum.

### risks

1. "Eingeklappt ist ja immer noch eine Leiste."
Vielleicht erwartest du, dass sie ganz verschwindet.
- Gegenmaßnahme: Die Suche ist bei 652 Spielen das wichtigste Werkzeug. Bleibt sie stehen, muss Strg+F nichts aufklappen, nichts springt beim Tippen, und der Filterzustand bleibt sichtbar.
- Rückfallvariante, falls gewünscht: "ganz aus" (0 px). Eine 28-px-Lasche sitzt dann rechts auf der Grenze, Strg+F klappt nur die Suchzeile auf. Nachteile: mehr verborgener Zustand und eine springende Liste beim Suchen. Ich empfehle das nicht.

2. Symbole statt Wörter heißt erst mal lernen; 1 s bis zum Tooltip kann sich für Neue träge anfühlen.
- Gegenmaßnahme: eindeutige Symbole (Sonne mit A, Funkeln, HDR-Kennzeichen).
- Nach dem ersten Tooltip erscheinen die Nachbarn sofort (BetweenShowDelay).
- Tooltips auch bei Tastaturfokus, Rechtsklick-Menü mit Namen, gestaltete Tooltips statt Windows-Standard.

3. Die Zahlen in den Knöpfen fallen weg. "Neu 1" sieht man nicht mehr auf einen Blick.
- Gegenmaßnahme: Punkt an "Neu", Zahlen im Tooltip, Kopfzeilen-Statistik ab 1366 px, Trefferzahl in der eingeklappten Leiste.
- Alternative, falls du Zahlen willst: eine kleine Mono-Zahl unten rechts am Knopf (9 px, ab 100 als "99+").

4. Ausgeklappt bleibt die Leiste bei 1280 bis 1600 px zweizeilig. Das ist rechnerisch nicht zu vermeiden, solange Quelle, Sortierung und Ansicht beschriftet sind.
- Gegenmaßnahme: Genau dafür gibt es das Einklappen.
- Wer ausgeklappt mehr Einzeiligkeit will: Ansicht nur mit Symbolen (Variante D, Regal immer einzeilig, Liste ab etwa 1700 px) oder in der Liste über die Spaltenköpfe sortieren (wie im Prototyp).

5. Die neue Reihenfolge (Schnellfilter direkt neben der Suche) bricht kurz die Gewohnheit.
- Gegenmaßnahme: Danach stehen die Schnellfilter in beiden Zuständen und bei jeder Breite an derselben Stelle. Das ist auf Dauer ruhiger.

6. Die hohe Faltlasche (40×88 bei zwei Zeilen) könnte ungewohnt wirken.
- Gegenmaßnahme: Glas ohne Inhalt außer dem Pfeil.
- Alternative: eine 40×40-Lasche nur in Zeile 1. Dafür muss ToolbarFlowPanel in Zeile 1 Platz freihalten können (etwas mehr Aufwand).

7. Bewegung: Liste und Titelbereich könnten ruckeln.
- Gegenmaßnahme: Das Layout wird nur einmal berechnet, die Bewegung ist reines Transform mit Clip, Reduziert und Aus überspringen sie.
- Rest-Risiko: Im Raster in niedrigen Fenstern bewegen sich Titelbereich und Raster zugleich. Beide nutzen dieselbe Kurve.

8. Tastenkürzel können mit anderen Programmen kollidieren (Overlays von Discord, NVIDIA, Steam).
- Gegenmaßnahme: Strg+Umschalt+F und Alt+A/N/H sind in Windows nicht belegt und gelten nur, wenn HDRpilot das aktive Fenster ist. Strg+Alt meide ich, weil das auf deutschen Tastaturen AltGr ist (@, €).

9. Dein Verlauf: Automatik, die du nicht ausgelöst hast, hast du abgelehnt (Einklappen beim Scrollen, das alte Stufen-Falten).
- Gegenmaßnahme: Hier gibt es nichts Automatisches. Nur deine Wahl zählt, sie wird gemerkt, und der Standard ist ausgeklappt.

10. Bei Kleinigkeiten könnten "kürzbare Marken" die Leiste doch umbrechen.
- Gegenmaßnahme: Eingeklappt schaltet ToolbarFlowPanel auf "eine Zeile" (Wrap=false). Marken und Suche haben feste Mindestbreiten (28 bzw. 180), die Rechnung bei 1280 geht auch im schlimmsten Fall auf.

### effort

STUFE A, PFLICHT (etwa 1,5 bis 2 Tage)
Schnellfilter als Symbolgruppe, "+" nur als Symbol, neue Reihenfolge, Faltlasche, eingeklappte Zeile mit Marken und Trefferzahl, Kürzel, Fokus-Regeln, UIA, gemerkter Zustand. Noch ohne die aufwendige Bewegung, nur Überblenden.
- src/HdrPilot.App/MainWindow.xaml: Leiste umbauen. Reihenfolge, Gruppe "Schnellfilter" statt drei Chips, "+" nur als Symbol, Spalte für die Lasche neben ToolbarArea, Marken und Trefferzahl, Kontextmenü, Tooltips.
- src/HdrPilot.App/Themes/Library.xaml: neue Stile Lib.FilterGroup (BasedOn Lib.SegmentFrame), Lib.FilterToggle, Lib.FoldTab, Lib.StateMark, Lib.CountChip, Lib.ToolTip (mit Vorlage für Titel, Text und Tastenkappe). Die Chip-Stile bleiben für andere Stellen.
- src/HdrPilot.App/Views/Library/ToolbarFlowPanel.cs: Eigenschaft Wrap (eingeklappt false: eine Zeile, Elemente mit Mindestbreite geben nach), optional "bevorzugter Umbruch vor". Etwa 60 Zeilen plus Tests.
- Neu src/HdrPilot.App/Views/Library/ToolbarFoldButton.cs: ToggleButton mit AutomationPeer für ExpandCollapse.
- src/HdrPilot.App/MainWindow.xaml.cs:
  - HandleKey: Strg+Umschalt+F, Alt+A/N/H, Esc und ↓ in der Suche
  - Umschalten mit Fokus-Rettung
  - einmal RefreshHeroHeight/ApplyVerticalLayout nach dem Umschalten
- src/HdrPilot.App/ViewModels/Library/LibraryViewModel*.cs:
  - IsToolbarCollapsed, ToggleToolbarCommand, ResetFiltersCommand
  - Sichtbarkeit und Texte der Marken (Quelle ≠ Alle, Sortierung ≠ Standard, Einlesen läuft)
  - FilteredCountText, Tooltip-Texte mit Zahlen
- src/HdrPilot.Core/Config/AppConfig.cs: LibraryToolbarCollapsed (bool?)
- src/HdrPilot.App/Resources/LibraryStrings.resx: etwa 20 neue Texte (Tooltips, Namen, Marken, Menü)
- ShowcaseLayout.cs: keine neue Formel, eingeklappt ist einfach 40 px = ToolbarHeight. Nur die Kommentare anpassen.
- Tests:
  - tests/HdrPilot.App.Tests/Library/ToolbarFlowLayoutTests.cs: neue Reihenfolge, Wrap=false, Rechnung bei 1280/1366/1920 wie in der Breitenanalyse
  - MainWindowLayoutTests.cs: Zeilen und Höhen ein- und ausgeklappt je Ansicht
  - neu: Kürzel und Fokus-Rettung, Marken im ViewModel, Speichern und Laden der Einstellung, UIA ExpandCollapse
- Dazu Bildschirmfoto-Skript (Zustände eingeklappt und ausgeklappt) sowie docs/design/showcase-hero/README.md und docs/entscheidungen.md.

STUFE B, DIE AUFWENDIGE BEWEGUNG (etwa 0,5 bis 1 Tag)
- Neu src/HdrPilot.App/Views/Library/ToolbarFold.cs: Ablauf (Layout einmal, Liste per Transform, versetztes Ein- und Ausblenden, Flug der Marken, Lichtkante, Puls) über MotionController.Transition und LibraryMotion.
- Dazu ein Test, dass danach keine Uhren laufen (wie bei phase10-gpu).

STUFE C, BEFEHLSPALETTE STRG+K (optional, etwa 1,5 bis 2 Tage)
- Neues Fenster (Popup) mit Suche, Gruppen, Häkchen und Kürzeln.
- Die Befehle kommen aus dem LibraryViewModel.
- Tests für Treffer und Fokus-Rückgabe.

GESAMT
Etwa 2 bis 3 Tage für A und B. Mit C etwa 4 bis 5 Tage.

## Konzept 3 (space): Kompaktleiste mit Lasche und Kantenkapsel

### filterButtons

FEEDBACK ZUR ERSTEN ANMERKUNG: Ich stimme zu. Zwei Bedingungen müssen aber erfüllt sein, und ein Hinweis gehört dazu.

Warum das richtig ist:
1. Platz. Die drei Chips brauchen heute 133 + 95 + 133 = 361 DIP, dazu 2 Abstände von je 8 (mit „Neu 652“ ≈ 374). Als 40-px-Kacheln sind es 120 DIP, als Gruppe mit 4-px-Fugen 128. Das spart ≈ 241–254 DIP und ist der größte einzelne Hebel der ganzen Leiste.
2. Ruhe. Heute wächst ein Chip mit jeder Ziffer seines Zählers um ≈ 6,3 DIP (Mono 10,5, Lib.Count). Schon der Sprung von 9 auf 10 neue Spiele nach „Neu einlesen“ kann die Leiste in eine andere Zeilenzahl kippen lassen. Eine Kachel mit aufgesetzter Plakette hat eine feste Breite, die Leiste springt dann nie wegen einer Zahl.
3. Beim Umbruch streckt das ToolbarFlowPanel die Chips heute (Grow 2), bei 1920 × 1080 auf ≈ 280–300 DIP („Neu 652“, „HDR-fähig 0“). Das wirkt beliebig. Feste Kacheln mit Grow 0 bleiben 40 × 40.
4. Filter sind selten geklickte Schalter. Für sie passt eine Erklärung per verzögertem Tooltip, anders als für Suche oder Quelle.

Bedingungen:
(a) Die Symbole müssen ohne Text eindeutig sein, und das sind sie heute nicht. E706 (Sonne) ist dasselbe Symbol wie bei „HDR einschalten“. Den Stern E734 liest man als „Favoriten“, den Monitor E7F4 als „Bildschirm“.
(b) Der An-Zustand muss auch ohne Text sofort auffallen.
(c) Hinweis: Allein reicht diese Änderung nicht für eine Zeile bei 1920 × 1080 und 110 %. Nötig wären 1260 DIP, da sind aber nur 1259,5 (laut Screenshot ≈ 1255). Mehr dazu in der Breitenrechnung.

SPEZIFIKATION „Filter-Kachel“ (neuer Stil Lib.FilterTile auf Basis eines ToggleButton):
- Größe 40 × 40 DIP, genau wie „Neu einlesen“ (Lib.ToolbarIconButton). Glasfläche Lib.FieldFrame, Radius 12. ToolbarFlowPanel.Grow = 0, die Kachel wächst also auch beim Umbruch nie.
- Gruppe: Die drei Kacheln bilden einen Block mit 4 px Fuge (3 × 40 + 2 × 4 = 128 DIP) direkt nach „Sortierung“, in der Reihenfolge Automatik · Neu · HDR-fähig. Der Block bricht als Ganzes um.
- Symbole sind Spiegel dessen, was die Zeilen zeigen. So erklären sie sich ohne Lernen:
  • Automatik: ein Mini-Schalter (LineIcon, Pille 18 × 10 mit Knopf rechts), wie die Schalter in der Spalte „HDR AUTOMATISCH“.
  • Neu: ein Mini-Etikett „NEU“ (24 × 13, Radius 4, 8,5 px fett), wie das weiße NEU-Kennzeichen in Zeilen und Kacheln.
  • HDR-fähig: ein Mini-Etikett „HDR“ mit Punkt, wie „● NATIV“.
  Inaktiv sind sie umrandet in TextSecondary, beim Überfahren in Text, aktiv gefüllt (Fläche Accent, Inhalt TextOnAccent). Der Unterschied ist also nicht nur Farbe, sondern gefüllt gegen Umriss. Falls die Etiketten nicht gefallen: Fluent-Glyphen in 15 px als Ersatz.
- Aktiv wie heute beim Chip: Akzentfläche 18 %, 1 px AccentStrong, Hof (Halo 0,55), 0,2 s Einblenden. Dazu wird die Plakette zum Akzentverlauf.
- Zähler-Plakette: oben rechts aufgesetzt und 5 px über die Ecke hinaus (Margin −5,−5,0,0 an der Ecke). Höhe 16, MinWidth 16, Padding 4,0, Radius 8, Mono 9,5 halbfett. Zahlen bis 999, darüber „999+“.
  • Inaktiv: Surface2 mit 1 px Line-Rand (lesbar auch über dem Titelbild im Regal), Schrift TextSecondary.
  • Aktiv: Hp.Brush.Accent, Schrift TextOnAccent.
  • Bei 0 ist sie ausgeblendet. Ausnahme: Ist der Filter an, steht „0“ in Akzentfarbe und erklärt so die leere Liste.
  • IsHitTestVisible = false. Die Plakette beeinflusst die Breite nie.
  • Kurzes „Pop“ bei Änderung (Scale 1 → 1,15 → 1 in 180 ms), nur auf der Stufe Voll.
- Tooltip (gilt einheitlich für alle reinen Symbol-Knöpfe der Leiste):
  • InitialShowDelay 1200 ms statt der Windows-Vorgabe (Mausverweilzeit, meist ≈ 400 ms).
  • BetweenShowDelay 400 ms: Gleitet man zur Nachbarkachel, erscheint deren Erklärung sofort.
  • ShowDuration Int32.MaxValue: bleibt, bis die Maus geht. ShowsToolTipOnKeyboardFocus = true.
  • Platzierung unter der Kachel, mittig, 8 px Abstand. Glasstil wie Lib.PopupBody (Radius 12, MaxWidth 280). Das Ausblenden erfolgt als Fade, nur wenn LibraryMotion.IsEnabled gilt.
  • Inhalt in drei Zeilen: Titel (13, halbfett), Erklärung (12, TextSecondary), Fußzeile (Mono 11):
    – „HDR automatisch“ / „Nur Spiele zeigen, bei denen HDRpilot HDR beim Start selbst einschaltet.“ / „0 Spiele · Klick: Filter an“
    – „Neu“ / „Nur Spiele zeigen, die neu in der Bibliothek sind (noch nicht als gesehen markiert).“ / „652 Spiele · Klick: Filter an“
    – „HDR-fähig“ / „Nur Spiele mit HDR laut PCGamingWiki, auch eingeschränkt.“ / „36 Spiele · Klick: Filter an“
    – Ist der Filter an, lautet die Fußzeile „Filter aktiv · 36 von 652 · Klick: aus“.
- Screenreader: Der Name bleibt der volle Satz (Filter_OnlySelectedName usw.). ItemStatus „{n} Spiele“, HelpText = Erklärung. Das Toggle-Muster meldet an/aus. Fokusrahmen 2 px FocusRing wie bei Lib.ToolbarButton; beim Chip ist es heute nur die Randfarbe.

ZUSATZEMPFEHLUNG (eure Freigabe nötig, weil bisher „Alles bleibt ausgeschrieben“ galt):
- Ansicht nur mit Symbolen: 122 statt 225 DIP. Liste, Raster und Regal sind als Symbole eindeutig, die Tooltips nennen Strg+1/2/3.
- „Spiel hinzufügen“ als ＋-Kachel: 40 statt 143 DIP. So war es schon im abgenommenen Prototyp v4.
Erst damit ist die Leiste bei 1920 × 1080 @110 % und im Startfenster 1600 × 1000 DIP sicher einzeilig.

### widthAnalysis

GRUNDLAGEN AUS DEM CODE (DIP)
- ToolbarFlowPanel: Gap 8 (im XAML nicht gesetzt, also Vorgabe), RowGap 8, Zeilenhöhe 40. Eine Zeile entsteht, wenn Σ ceil(Breite) + (n − 1) · 8 ≤ floor(Listenbreite). Sonst wird gierig gezählt und dann ausgewogen verteilt.
- Suche: Basis 236, wächst nur in der Einzeile bis 320. Quelle: Basis 188. Sortierung: Basis 196. Beide Auswahllisten werden in ihrer Basisbreite gemessen.
- Chip (Lib.Chip): Padding 11 + 10, Glyphe 13 + 8, dann der Text, dann der Zähler (Margin 8 + Padding 7 + 7 + Ziffern zu je ≈ 6,3).
  Gemessen (Sweep-Protokoll): Automatik 132,4 · Neu 94,9 · HDR-fähig 132,7, jeweils mit einstelliger Zahl. „Neu 652“ ≈ 107,5.
- Ansicht mit Text: 2 · (3 Padding + 1 Rand) = 8, dazu Liste 67,0 + Raster 76,5 + Regal 72,3 = 223,8; gemessen 225.
  Nur Symbole: Radio 10 + 15 + 10 = 35, MinWidth 38, also 3 · 38 + 8 = 122.
- Neu einlesen 40, gesehen 40. „Spiel hinzufügen“ 142,0, als ＋-Kachel 40. Filter-Kachel 40; die Plakette liegt außerhalb der Breite.

BEDARF FÜR EINE ZEILE
- Heute: 236 + 188 + 196 + 133 + 95 + 133 + 225 + 40 + 40 + 143 = 1429, plus 9 · 8 = 72, ergibt 1501. Mit „Neu 652“ sind es 1514.
- V1, nur die Filter als Kacheln: 236 + 188 + 196 + 3 · 40 + 225 + 40 + 40 + 143 = 1188, plus 72, ergibt 1260.
- V2, zusätzlich Ansicht nur mit Symbolen: 1188 − 103 = 1085, plus 72, ergibt 1157. Genauso viel braucht V1 mit ＋ statt der Ansicht.
- V3, zusätzlich Ansicht und ＋ nur als Symbol: 982 + 72 = 1054.
- Empfohlen (R), also V3 mit Blöcken plus Lasche:
  Blöcke: Suche 236 | Quelle 188 | Sortierung 196 | Filter-Block 128 | Ansicht 122 | Aktionen 156.
  Der Aktionen-Block besteht aus ⟳ ◉ ＋ (3 · 40 + 2 · 4), 4 px Fuge und der Lasche (24).
  Summe 1026, plus 5 · 8 = 40, ergibt 1066.

LISTENBREITE BEIM STANDARD-DETAILBEREICH
- Root = Fenster − 2 (der Fensterrahmen, durch das Protokoll bestätigt: 1000 → 570, 1987 → 1501).
- Detailbereich = clamp(round(0,23 · Root); 340; 396).
- Liste/Raster = Root − 2 · 32 − 24 − Detailbereich. Regal = Root − 64.

| Fenster | DIP-Breite | Rechnung Liste/Raster | Liste/Raster | Regal |
|---|---|---|---|---|
| 1280×800 @110 % | 1163,6 | 1161,6 − 88 − 340 | 733,6 | 1097,6 |
| 1366×768 @110 % | 1241,8 | 1239,8 − 428 | 811,8 | 1175,8 |
| 1600×1000 @110 % | 1454,5 | 0,23 · 1452,5 = 334, also 340; 1452,5 − 428 | 1024,5 | 1388,5 |
| Startfenster 1600×1000 DIP (eure Standardgröße auf 3440×1440 @110 %) | 1600 | 1598 − 88 − 368 | 1142 (Protokoll: 1142) | 1534 |
| 1920×1080 @110 % | 1745,5 | 1743,5 − 484 | 1259,5 (Screenshot gemessen ≈ 1255) | 1679,5 |
| 3440×1440 @110 % | 3127,3 | 3125,3 − 484 | 2641,3 | 3061,3 |
| Zum Vergleich: 1920×1080 @125 % | 1536 | 1534 − 88 − 353 | 1093 | 1470 |
| Zum Vergleich: 1920×1080 @100 % | 1920 | | 1434 | 1854 |

ZEILEN IN LISTE UND RASTER

| Fenster | heute | V1 | V2 | V3 | R |
|---|---|---|---|---|---|
| 1280×800 | 3 | 2 | 2 | 2 | 2 |
| 1366×768 | 2 | 2 | 2 | 2 | 2 |
| 1600×1000 @110 % | 2 | 2 | 2 | 2 (fehlen 30) | 2 (fehlen 42) |
| Startfenster 1600 DIP | 2 | 2 | 2 (fehlen 15) | 1 (88 frei) | 1 (76 frei) |
| 1920×1080 @110 % | 2 | 2 (fehlt 0,5, gemessen ≈ 5) | 1 | 1 | 1 (193 frei) |
| 3440×1440 | 1 | 1 | 1 | 1 | 1 |
| 1920 @125 % | 2 | 2 | 2 | 1 | 1 (27 frei) |
| 1920 @100 % | 2 | 1 | 1 | 1 | 1 |

ZEILEN IM REGAL

| Fenster | heute | V1 | V2 | V3 | R |
|---|---|---|---|---|---|
| 1280×800 | 2 | 2 | 2 | 1 | 1 (31 frei) |
| 1366×768 | 2 | 2 | 1 | 1 | 1 |
| 1600×1000 @110 % | 2 | 1 | 1 | 1 | 1 |
| Ab Startfenster 1600 DIP | 1 | 1 | 1 | 1 | 1 |

AB WELCHER FENSTERBREITE DIE LEISTE IN LISTE UND RASTER EINZEILIG IST
- Heute: ab 1987 DIP (≈ 2186 px @110 %).
- V1: ab 1746 DIP (1921 px @110 %, also knapp über 1920).
- V2: ab 1619 DIP.
- V3: ab 1485 DIP.
- R: ab 1501 DIP (1651 px @110 %, 1876 px @125 %).
- Im Regal: R ab 1132 DIP.

FAZIT ZUR BREITE
- Nur die Filter als Kacheln (V1) verfehlt 1920 × 1080 @110 % um einen halben Pixel und das Startfenster um 118 DIP.
- R ist ab dem Startfenster und ab 1920 × 1080 @110 % bzw. @125 % immer einzeilig, im Regal sogar schon ab 1280 × 800.
- Unter ≈ 1500 DIP Fensterbreite (1280, 1366 und 1600 × 1000 @110 %) bleibt sie ausgeklappt zweizeilig, denn der Detailbereich nimmt fest 340 + 24 + 64. Dort ist das Einklappen die Antwort.
- Zweizeilige Notlösung mit R bei 812 DIP: Zeile 1 enthält Suche und Quelle (432), Zeile 2 Sortierung, Filter, Ansicht und Aktionen samt Lasche (626). Den Zuwachs bekommen nur Suche, Quelle und Sortierung; Kacheln, Ansicht und Knöpfe behalten ihre Breite.

HÖHE: GANZE LISTENZEILEN (rechnerisch)
- Formel: (H − 40 − (Titelbereich + 10 + Leiste + 10) − 38) / 46. Eingeklappt: (H − 40 − (Titelbereich + 10) − 38) / 46. Der Titelbereich ist H/3.
- Eingeklappt bringt +50 DIP gegenüber einer Zeile Leiste und +98 gegenüber zwei Zeilen (+1,1 bzw. +2,1 Listenzeilen).

| Fenster | heute | mit R | eingeklappt |
|---|---|---|---|
| 1280×800 (H 727, Titelbereich 242) | 5,5 (3 Zeilen Leiste) | 6,5 | 8,6 |
| 1366×768 (H 698, 233) | 6,1 | 6,1 | 8,2 |
| 1600×1000 @110 % (H 909, 303) | 9,1 | 9,1 | 11,3 |
| Startfenster (H 1000, 333) | 10,5 | 11,5 | 12,6 |
| 1920×1080 @110 % (H 982, 327) | 10,2 | 11,2 | 12,3 |
| 3440×1440 (H 1309, 436) | 16,0 | 16,0 | 17,1 |

### hideShowConcept

IDEE: Die Leiste wohnt unter der Unterkante des Titelbilds und fährt wie eine Schublade heraus und wieder hinein. Ihr Griff ist eine schmale Lasche am rechten Ende der Leiste. Fährt die Leiste ein, bleibt die Lasche an der Bildkante hängen und wird zur gläsernen „Kantenkapsel“. Die Kapsel zeigt, was gerade gefiltert ist. Sie kostet nie eine eigene Zeile. Der Titelbereich bewegt sich beim Umschalten nie (euer „Größe bleibt“); die gewonnene Höhe bekommt immer der Inhalt.

AUSGEKLAPPT (Liste, 1920 × 1080 @110 %, eine Zeile, 1259 DIP):
  ▓▓▓▓▓▓▓▓ Titelbild ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ ━━ (Ziehgriff) ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
                                10 px Luft
  [🔍 Suchen …   Strg+F] [● Alle 652 ⌄] [◷ Zuletzt gespielt ⌄] [⊙][NEU][HDR]    [≡ ▦ ⫼] [⟳][◉][＋][⌃]
                                10 px Luft
  ┌ SPIEL · 652 ─────── QUELLE ──── ZULETZT GESPIELT ──── HDR ──── HDR AUTOMATISCH ┐
- Die Lasche ist 24 × 40, aus Glas, Radius 8, mit Chevron ⌃ (E70E, 9 px). Sie sitzt mit 4 px Fuge nach ＋ im Aktionen-Block, also immer ganz rechts, auch in der zweizeiligen Notlösung (dort unten rechts).

EINGEKLAPPT (Liste/Raster), ohne Filter:
  ▓▓▓▓▓▓▓▓ Titelbild ▓▓▓▓▓▓▓▓▓▓▓▓ ━━ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓( ⌄ 🔍 Suche & Filter )
  ┌ SPIEL · 652 ─────── QUELLE ──── ZULETZT GESPIELT ──── HDR ──── HDR AUTOMATISCH ┐
EINGEKLAPPT mit Suche und Filtern (Akzentrand und Hof):
  ▓▓▓▓▓ Titelbild ▓▓▓▓▓ ━━ ▓▓▓▓▓▓▓( ⌄ „gears“ · ● Steam · [HDR] · 12/652 )
- Die Kapsel ist 22 px hoch, so hoch wie die Kennzeichen „NEU“ und „HDR UNBEKANNT“. Sie liegt rechtsbündig auf der Listenkante, von 12 px über bis 10 px unter der Bildkante.
  • Oben bleiben ≥ 3 px zu den Knöpfen des Titelbereichs (dessen 16-px-Innenrand).
  • Unten schließt sie in der Liste bündig an der Kartenkante ab, im Raster 12 px über der ersten Kachelreihe.
  • Damit liegt sie genau im Band, in dem auch der Ziehgriff liegt. Der Griff bleibt in der Mitte; seine Leuchtlinie endet vor der Kapsel.
- Ohne Filter (≈ 133 × 22): ⌄, Lupe, „Suche & Filter“.
- Gefiltert erscheinen Marken: Suchbegriff (bis 16 Zeichen, sonst Auslassung), Quelle (wenn nicht „Alle“) als Farbpunkt mit Name, aktive Filter als Mini-Symbole und das Ergebnis „12/652“ in Mono. Höchstbreite min(360; 45 % der Liste), Überlauf als „+2“.
  Akzentrand AccentStrong und ein ruhender Hof (0,35) bleiben, solange gesucht oder gefiltert wird. Die Statuszeile meldet zusätzlich wie heute „Liste · 12 von 652 · …“. So bleibt kein aktiver Filter unbemerkt.
- Die Sortierung erscheint nicht in der Kapsel, weil sie nichts ausblendet.

AUSLÖSER
- Maus: Klick auf die Lasche klappt ein, Klick auf die Kapsel (eine einzige Schaltfläche) klappt aus. Rechtsklick auf eine freie Stelle der Leiste öffnet ein Kontextmenü mit „Werkzeugleiste ausblenden  Strg+Umschalt+F“.
- Tastatur: Strg+Umschalt+F schaltet um und wird gemerkt („F“ wie Filter, neben Strg+F; heute nicht belegt). Enter oder Leertaste auf Lasche bzw. Kapsel.
- Suchkürzel Strg+F im eingeklappten Zustand:
  • Die Leiste fährt vorübergehend aus (260 ms), der Fokus geht in die Suche.
  • Sie fährt 600 ms, nachdem der Fokus die Leiste verlassen hat, wieder ein, sofern die Maus nicht darüber steht. Der Suchbegriff steht dann in der Kapsel.
  • Esc in der leeren Suche klappt sofort ein.
  • Ein Klick auf die Lasche während dieser Phase lässt die Leiste offen.
  • Strg+F bei ausgeklappter Leiste verhält sich wie heute.
- Automatik: Es gibt keine. Kein Einklappen beim Scrollen, weil ihr beim Titelbereich „Größe bleibt“ gewählt habt. Höchstens später als Option in „Darstellung“ („Beim Scrollen einklappen“, standardmäßig aus).

MERKEN
- Eine globale Einstellung LibraryToolbarCollapsed (Standard: ausgeklappt). Sie wird beim Umschalten sofort gespeichert, beim Start ohne Animation wiederhergestellt und gilt für alle drei Ansichten.
- Das vorübergehende Ausklappen per Strg+F wird nicht gemerkt.

ZIEHGRIFF UND LUFTREGEL
- Ausgeklappt bleibt alles wie heute: über der Leiste so viel Luft wie unter ihr (Liste 10, Raster 22). Der Griff sitzt auf der Kante.
- Eingeklappt verschwindet die Leiste zusammen mit ihrer oberen Luft. Unter der Kante bleibt genau die bisherige untere Luft: Liste 10 px bis zur Karte, Raster 22 px (10 + 12 Innenabstand) bis zur ersten Kachelreihe.
- Ein- und Ausklappen ändert den Titelbereich nie:
  • ListHeroRange, GridHeroHeight und das Regal rechnen immer mit der ausgeklappten Höhe der Leiste (ToolbarArea.HeightFor), auch im eingeklappten Zustand.
  • Der gemerkte Anteil bleibt, der Griff hat denselben Bereich, nichts springt.
  • Die Kapsel liegt in der Z-Reihenfolge über dem Griff-Band. Ein Klick rechts auf der Kante trifft deshalb die Kapsel. Ziehen und Doppelklick („Standardhöhe“) funktionieren auf dem Rest der Kante wie bisher.

RASTER
- Wie die Liste. Die Kapsel liegt auf der Bildkante, die +50 bzw. +98 DIP gehen an die Kacheln (sichtbar mehr von der zweiten Reihe).
- Der Titelbereich bleibt, auch in niedrigen Fenstern.

REGAL
- Die Leiste liegt hier schon im Bild über der Kachelreihe. Einklappen spart also keine Höhe, sondern macht das Titelbild frei („Kino“).
- Die Kapsel bleibt dort, wo die Leiste endete: rechtsbündig am 32-px-Rand und senkrecht mittig in der früheren Leistenzeile. Wegen des kleinsten Titelbereichs von 440 DIP liegt sie immer ≥ 18 px unter der Automatik-Karte.
- Titelbild, Kachelreihe, Knöpfe und Logo bleiben stehen; es gibt keine zweite Bewegung.
- Fehlerhinweise und Vorschläge (NoticeArea) bleiben in allen Ansichten unter der Kante sichtbar.

### animationSpec

GRUNDSATZ: Alles sind Einmal-Übergänge. Sie werden über MotionController.Transition() und OneShotAllowed gesteuert, laufen also nicht bei minimiertem oder unsichtbarem Fenster und nicht während ein Spiel läuft (dann gilt sofort der Endzustand). Es gibt keinen Dauer-Effekt. Jede Animation hat FillBehavior=Stop und setzt die Endwerte selbst, damit danach keine Uhr übrig bleibt (das Effekt-Protokoll und phase10-gpu.ps1 prüfen auf 0).

STUFE VOLL: EINKLAPPEN (≈ 340 ms)
1. t = 0: Der Fokus wird übergeben (siehe Barrierefreiheit), die Leiste ist ab dann nicht mehr anklickbar (IsHitTestVisible = false).
2. Blöcke gestaffelt von links nach rechts (Suche zuerst, sie „fließen“ in die Lasche):
   • Versatz je Block 16 ms, Dauer je 140 ms, CubicEase EaseIn.
   • Opacity 1 → 0, TranslateX 0 → +20, ScaleX/Y 1 → 0,95.
   • RenderTransformOrigin (1; 0,5).
3. Band, ab 80 ms für 260 ms, CubicEase EaseInOut:
   • Neues Element ToolbarBand.Progress 1 → 0. Die gemeldete Höhe ist Höhe · p.
   • Der Inhalt wird mit −(1 − p) · Höhe nach oben angeordnet und am Band abgeschnitten (ClipToBounds). Die Leiste gleitet also unter die Bildkante.
   • Liste bzw. Raster rücken über das Layout nach.
4. Lasche wird zur Kapsel, ab 120 ms für 260 ms, ExponentialEase EaseOut mit Exponent 6 (wie der Titelbereich):
   • TranslateY von der Zeilenmitte auf die Kante: −31 DIP in Liste und Raster, 0 im Regal.
   • Breite 24 → Kapselbreite. Das Element liegt als Überlagerung in einer Canvas, das Layout betrifft also nur sich selbst.
   • Chevron dreht sich um 180° (⌃ wird ⌄).
   • Die Marken blenden ab 260 ms für 120 ms ein: Opacity 0 → 1, TranslateX −8 → 0.
5. Lichtkante (nur Liste und Raster, dort gibt es die Kante):
   • Ein 2 px hoher und 180 DIP breiter Streifen mit linearem Verlauf (transparent → AccentText → transparent) läuft auf der Bildkante vom linken Rand bis zur Kapsel: TranslateX, ab 40 ms für 300 ms. Opacity als Keyframes 0 → 0,9 → 0. Das greift die Lichtsprache des HDR-Moments auf.
   • Bei der Ankunft leuchtet der Hof der Kapsel auf (geschichtete Fläche wie der Halo von Lib.Chip, kein Effekt): 0 → 0,8 → Ruhewert, 280 ms. Der Ruhewert ist 0,35 bei aktiven Filtern, sonst 0.
6. Am Ende: Leiste Visibility = Collapsed, alle Transforms zurück auf neutral.

STUFE VOLL: AUSKLAPPEN (≈ 380 ms)
- 0–80 ms: Marken ausblenden.
- 40–300 ms: Kapsel wird zur Lasche (Breite, Chevron 180° → 0°, TranslateY zurück).
- 0–300 ms: Lichtkante von der Kapsel nach links.
- 60–340 ms: Band 0 → 1, ExponentialEase EaseOut (Exponent 6).
- Blöcke von rechts nach links (Aktionen zuerst), Start 140 ms, Versatz 16 ms, je 160 ms, CubicEase EaseOut: Opacity 0 → 1, TranslateX +20 → 0, Scale 0,95 → 1.
- Ausklappen per Strg+F ist dieselbe Folge, auf 260 ms gestaucht.

STUFE REDUZIERT (EffectPolicy: höchstens 0,3 s; gilt auch, wenn die Windows-Option „Animationseffekte“ aus ist)
- Kein Versatz, keine Lichtkante, kein Drehen und kein Skalieren.
- Band 200 ms CubicEase EaseOut, die Leiste als Ganzes Opacity in 120 ms.
- Kapsel und Lasche blenden in 150 ms über, der Chevron wechselt ohne Drehung.

STUFE AUS (oder ohne Hardware-Darstellung): Endzustand sofort, ohne jede Animation.

KLEINSTE EFFEKTE AN DEN KACHELN
- Den An-Zustand blendet das bestehende Einblenden in 0,2 s ein (MultiDataTrigger wie beim Chip).
- Plakette „Pop“ 180 ms, nur Voll.
- Tooltip-Fade nur bei LibraryMotion.IsEnabled.

GPU-KOSTEN
- Animiert werden nur Opacity, RenderTransform und ein rechteckiger Clip an ≤ 15 kleinen Elementen, dazu ein 2 × 180 px großer Verlauf. Das ist reine Komposition.
- Kein BlurEffect, kein DropShadowEffect, keine Mischmodi, keine großflächige OpacityMask, kein BitmapCache nötig.
- Die Höhe des Titelbereichs bleibt, das große Bild wird also weder neu dekodiert noch neu skaliert.
- Die einzige spürbare Arbeit ist CPU: ≈ 16 Layout-Durchläufe der virtualisierten Liste bzw. des Rasters (12–20 sichtbare Elemente) während der 260 ms des Bands.
- Bildrate: Einmal-Übergänge mit 60 fps. Die Grenze von 30 fps gilt nur für Dauer-Effekte; wer sie strikt will, setzt DesiredFrameRate = 30.
- Messen mit dem vorhandenen Bildzeit-Protokoll. Dauert ein Layout-Frame länger als 8 ms, gibt es eine FLIP-Ersatzlösung: Endlayout sofort setzen, BodyArea mit TranslateTransform +50 → 0 und Clip, also null Layout pro Frame.

### accessibility

TASTATUR
- Tab-Reihenfolge ausgeklappt: Ziehgriff → Suche → Quelle → Sortierung → 3 Filter-Kacheln → Ansicht → ⟳ → ◉ → ＋ → Lasche → Hinweise → Liste.
- Eingeklappt: Ziehgriff → Kapsel → Hinweise → Liste. Die eingeklappte Leiste ist Visibility = Collapsed, also weder im Tab-Weg noch im UIA-Baum. Während der Animation ist sie nicht treffbar (IsHitTestVisible = false).
- Lasche und Kapsel sind EIN Steuerelement. Es liegt als Überlagerung nach der Leiste; in der Leiste hält ein leerer 24 × 40-Platzhalter die Stelle frei. Klappt man mit Maus oder Tastatur um, behält dieses Element den Fokus, der Fokus geht also nie verloren.
- Steht der Tastaturfokus beim Einklappen per Kürzel in der Leiste (etwa in der Suche), wandert er vor dem Start der Animation auf die Kapsel.
- Beim automatischen Wiedereinklappen nach Strg+F hat der Fokus die Leiste bereits verlassen (Liste, Kachel); er bleibt dort.
- Strg+F führt immer in die Suche. Ist die Leiste eingeklappt, fährt sie zuerst aus und der Fokus wird erst nach dem Layout gesetzt (Dispatcher-Priorität Loaded).
- Strg+Umschalt+F schaltet um, Enter oder Leertaste auf dem Element ebenfalls. Esc in der leeren Suche klappt nur bei vorübergehendem Ausklappen ein.
- Neues Kürzel in den Tastenhinweisen der Statuszeile ergänzen („Strg+Umschalt+F Leiste“, nur dort, wo die Hinweise ohnehin stehen).

SCREENREADER
- Das Element bekommt einen eigenen AutomationPeer mit dem ExpandCollapse-Muster (Zustand Erweitert/Reduziert).
  • Name „Werkzeugleiste“, AutomationId „ToolbarToggle“, HelpText „Ein- und ausblenden: Strg+Umschalt+F. Suchen: Strg+F“.
  • ItemStatus fasst zusammen, z. B. „Gefiltert: HDR-fähig, Quelle Steam, Suche „gears“ – 12 von 652 Spielen“ bzw. „Keine Filter“.
- Ändert sich die Zusammenfassung im eingeklappten Zustand, meldet sie AutomationProperties.LiveSetting = Polite.
- Die Leiste selbst behält ihren ToolBar-Peer (LibraryToolbar).
- Filter-Kacheln: Name bleibt der volle Satz („Nur Spiele mit HDR automatisch“ usw.). Das Toggle-Muster meldet an/aus. ItemStatus „{n} Spiele“, HelpText = Tooltip-Erklärung.
- Ansichtssymbole und ＋ behalten ihre Namen („Liste“, „Raster“, „Regal“, „Spiel hinzufügen“), auch wenn der Text fehlt.

SICHTBARKEIT
- Der An-Zustand ist nicht nur Farbe: gefülltes statt umrandetes Mini-Etikett, Akzentrahmen, Hof.
- Fokusrahmen 2 px FocusRing auf Kacheln, Lasche und Kapsel.
- Tooltips erscheinen auch bei Tastaturfokus (ShowsToolTipOnKeyboardFocus, nach 1200 ms) und schließen mit Esc. Sie bleiben, solange Maus oder Fokus da sind (WCAG 1.4.13).
- Trefferflächen: Kacheln 40 × 40, Lasche 24 × 40, Kapsel mindestens 133 × 22 sichtbar, dazu eine unsichtbare Trefferfläche auf 24 px Höhe erweitert (WCAG 2.5.8).
- Hoher Kontrast: Glasflächen fallen auf Systemfarben zurück. Die Plakette hat immer einen Rand.
- Bewegung: EffectPolicy-Stufen (Voll/Reduziert/Aus) und die Windows-Option „Animationseffekte“ werden beachtet.

### risks

1. „Alles bleibt ausgeschrieben“: Die zusätzliche Empfehlung (Ansicht nur mit Symbolen, ＋ statt „Spiel hinzufügen“) widerspricht eurer früheren Vorgabe, deshalb brauche ich eure Freigabe. Ohne sie gilt nur V1. Dann bleibt die Leiste bei 1920 × 1080 @110 % um 0,5 DIP (gemessen ≈ 5) zweizeilig und im Startfenster 1600 × 1000 DIP sowieso. Abhilfe: Zahlen zeigen (siehe Breitenrechnung); als Zwischenweg V2 (nur die Ansicht als Symbole) wählen, einzeilig ab 1619 DIP.
2. Mini-Etiketten „NEU“ und „HDR“ könnten wie „wieder Text“ wirken. Rückfall: Fluent-Glyphen. Die heutigen Glyphen (Sonne, Stern, Monitor) sind aber mehrdeutig, besonders die Sonne, die es auch bei „HDR einschalten“ gibt.
3. Bei eingeklappter Leiste vergisst man aktive Filter. Abhilfe: Die Kapsel zeigt alle Marken, Akzentrand und Hof, dazu nennt die Statuszeile „12 von 652“, und der Screenreader meldet den Zustand.
4. Vorübergehendes Ausklappen per Strg+F mit automatischem Wiedereinklappen könnte überraschen. Die einfachere Alternative wäre: Strg+F klappt dauerhaft aus. Das entscheidet ihr. Ein Einklappen beim Scrollen biete ich bewusst nicht an (wegen „Größe bleibt“).
5. In Fenstern unter ≈ 1500 DIP Breite (1280 × 800, 1366 × 768, 1600 × 1000 @110 %) bleibt die ausgeklappte Leiste zweizeilig, weil der Detailbereich fest 428 DIP nimmt.
   • Dort ist das Einklappen die Antwort (+98 DIP, ≈ +2 Listenzeilen).
   • Eine Suche-Basis von 236 → 190 würde 1600 × 1000 @110 % mit nur 4 DIP Rest einzeilig machen. Das ist zu knapp und wäre beim Tippen anfällig, deshalb nicht empfohlen.
   • Die zweizeilige Notlösung teilt sich „Suche + Quelle | Sortierung + Rest“.
6. Die Kapsel sitzt nah an den Knöpfen des Titelbereichs (≥ 3 px) und im Band des Ziehgriffs.
   • Rechts auf der Kante trifft ein Klick die Kapsel, nicht den Griff.
   • Abhilfe: Der Griff bleibt in der Mitte, seine Linie endet vor der Kapsel, ihr Tooltip erklärt sie. Falls es zu eng wirkt, bekommt der kompakte Titelbereich unten 4 px mehr Innenrand.
7. Die Plakette ragt 5 px in die 10 px Luft über der Leiste und optisch in die Luftregel. Alternative: Plakette innen unten rechts, das Symbol rückt dann 3 px nach links oben.
8. Leistung: Das Band verursacht etwa 16 Layout-Durchläufe der Liste. Wird es messbar ruckelig (> 8 ms pro Frame), gibt es die FLIP-Variante ohne Layout pro Frame.
9. Ein Tooltip nach 1,2 s fühlt sich für Neulinge träge an. Abhilfe: Folge-Tooltips nach 400 ms, Tooltips auch bei Tastaturfokus. Die Mini-Etiketten erklären sich weitgehend selbst.
10. Der Zustand gilt global für alle Ansichten. Wer im Regal „Kino“ will, in der Liste aber die Leiste, bräuchte einen Zustand je Ansicht. Das lässt sich später einfach nachrüsten.
11. Neues Kürzel Strg+Umschalt+F: In anderen Programmen hat es eine andere Bedeutung (Word: Fett). Hier ist es frei und liegt logisch neben Strg+F.

### effort

Mittel: etwa 2 bis 2,5 Arbeitstage samt Prüfungen und Screenshots (Kacheln und Tooltips ≈ 0,5; Ein-/Ausklappen mit Animation, Layout, Barrierefreiheit und Merken ≈ 1–1,5; Tests, Screenshots und Prüfschritte ≈ 0,5).

Betroffene Dateien (Worktree s11-bar):
- src/HdrPilot.App/MainWindow.xaml: Chips werden zu Lib.FilterTile im Filter-Block. Ansicht nur mit Symbolen (Labels ViewList/Grid/ShelfLabel entfallen, Grow 0). ＋-Kachel. Aktionen-Block mit Platzhalter für die Lasche. Überlagerung Lasche/Kapsel. ToolbarBand um ToolbarArea. Tooltips und Kontextmenü der Leiste.
- src/HdrPilot.App/Themes/Library.xaml: neue Stile Lib.FilterTile, Lib.TileBadge, Lib.RichToolTip, Lib.ToolbarTab/Lib.EdgeCapsule und Lib.EdgeGlint; Mini-Symbole (Schalter, NEU, HDR).
- src/HdrPilot.App/Views/Library/ToolbarBand.cs (neu, oder in LibraryControls.cs): Decorator mit Progress-DP und Clip, dazu ein Peer mit ExpandCollapse für das Umschaltelement.
- src/HdrPilot.App/Views/Library/ToolbarFlowPanel.cs: höchstens Feinschliff. Die Blöcke sind normale Kinder; prüfen, dass Grow 0 in Zeilen nur aus festen Elementen nicht gleichmäßig streckt.
- src/HdrPilot.App/Views/Library/ShowcaseLayout.cs: eingeklappte Varianten von ToolbarTop und der Lage von ListArea/Trenner. Der Titelbereich rechnet immer mit der ausgeklappten Höhe.
- src/HdrPilot.App/MainWindow.xaml.cs:
  • ToolbarHeight() wird ToolbarArea.HeightFor() für den Titelbereich bzw. 0 für die Lage.
  • Der SizeChanged-Handler darf beim Einklappen keinen Sprung des Titelbereichs auslösen.
  • HandleKey: Strg+Umschalt+F, Strg+F vorübergehend, Esc.
  • Fokusübergabe, Animationsfolge, Lage der Kapsel je Ansicht.
- src/HdrPilot.App/ViewModels/Library/LibraryViewModel.cs: IsToolbarCollapsed, Zusammenfassung der Filter, Texte für ItemStatus.
- src/HdrPilot.Core/Config/AppConfig.cs: neue Einstellung LibraryToolbarCollapsed.
- src/HdrPilot.App/Resources/LibraryStrings.resx (+ Designer): Tooltip-Titel, Erklärungen und Fußzeilen, Kapseltexte, Kürzel.
- tests/HdrPilot.App.Tests:
  • ToolbarFlowLayoutTests mit den neuen Breiten und den Zeilen je Fenstergröße (die Tabelle aus der Breitenrechnung als Prüfung).
  • ShowcaseLayout: eingeklappt, Titelbereich unverändert.
  • HandleKey für die neuen Kürzel.
  • UIA: ExpandCollapse, Fokus nie verloren, Kacheln mit ItemStatus.
- Snapshot-Szenen „eingeklappt“ für Liste, Raster und Regal bei 1280, 1366, 1920 und 3440 (hell/dunkel). GPU- und Uhren-Prüfung (phase10-gpu.ps1) nach dem Umschalten.
- docs/design/showcase-hero/README.md und docs/entscheidungen.md: neue Entscheidung zur Leiste („Filter als Kacheln, Ein-/Ausklappen“).

## Konzept 4 (adaptive): Fließleiste mit Naht-Kapsel

### filterButtons

EINSCHÄTZUNG: Ich stimme zu, mit drei Ergänzungen.

Was dafür spricht:
1. Platz. Die drei Chips brauchen heute 133 + 95 + 139 = 367 DIP, als Symbole 3 × 40 = 120 DIP. Das macht 247 DIP frei, der größte einzelne Gewinn für eine einzeilige Leiste.
2. Ruhe und Stabilität. Heute hängt die Breite der Chips an den Zahlen: „Neu 652“ ist 13 DIP breiter als „Neu 1“. Nach dem Neu-Einlesen kann die Leiste deshalb allein wegen einer Zahl umbrechen. Feste 40 DIP beenden das.
3. Einheitliche Bausteine. Gleiche Größe, Radius und Glasfläche wie „Neu einlesen“ und „gesehen“.
4. Filter sind Schalter, die man selten wechselt und schnell lernt.

Ergänzung 1: Die Zahlen nicht verlieren. Sie wandern in eine kleine Zähler-Plakette an der Ecke.
Ergänzung 2: 1–2 s Verzögerung sind für den ersten Kontakt zu lang. Man hält still und denkt, es passiert nichts. Mein Vorschlag:
- 1,0 s für den ersten Tooltip (das untere Ende des Wunsches),
- danach 150 ms, solange man von Symbol zu Symbol fährt („warm“),
- dieselbe Erklärung auch bei Tastaturfokus.
Ergänzung 3: Die Symbole müssen ohne Text eindeutig sein. Sonne und Monitor lesen sich beide als „HDR“, und der Stern liest sich als „Favorit“.

SPEZIFIKATION
- Größe und Lage: 40 × 40 DIP, Radius 12, Glas Lib.FieldFrame. Grow = 0: nie gestreckt, auch nicht im Umbruch. 8 DIP Abstand. Reihenfolge Automatik, Neu, HDR-fähig direkt hinter „Sortierung“ (linke Gruppe). Drei einzelne Knöpfe, kein gemeinsames Segment: Ein Segment sähe aus wie die Ansicht (eine Wahl), Filter lassen sich aber frei kombinieren.
- Symbole (14-DIP-Glyphe bzw. 15-DIP-LineIcon, Strich 1,8, exakt mittig: bei 110/125/150 % in 44/50/60 px, also auf ganzen Pixeln):
  - Automatik: Sonne E706, wie „HDR einschalten“.
  - Neu: eigenes LineIcon „Funkeln“ (Stern mit 4 Zacken) statt E734.
  - HDR-fähig: Wortmarke „HDR“ (8,5 px Bold in einem 20 × 13 abgerundeten Rahmen, Strich 1,2), wie die HDR-Kennzeichen der Liste, statt Monitor E7F4.
- Zustände:
  - Ruhe: Symbol TextSecondary.
  - Hover: Fläche Surface2, Symbol Text.
  - Gedrückt: Fläche 0,8; Stufe Voll zusätzlich Scale 0,96.
  - AN: Fläche C1 18 %, Rahmen AccentStrong 1 px, Hof AccentSoft 0,55 (Margin −4), Symbol AccentText. „An“ unterscheidet sich also über Fläche, Rahmen, Hof und Farbe, nicht nur über die Farbe.
  - Tastaturfokus: FocusRing 2 px außen (Margin −3, Radius 14), klar anders als „an“.
- Zähler-Plakette (Teil der Vorlage, nicht klickbar, kein eigenes UIA-Element):
  - Lage oben rechts, Margin 0,−6,−4,0. Sie ragt 6 px in die 10-px-Luft und 4 px in den 8-px-Abstand; 4 px bleiben zum Nachbarn. Weil sie nicht klickbar ist, bleibt der Ziehgriff darüber bedienbar.
  - Höhe 16, MinWidth 16, Radius 8, Innenabstand 4,0, Mono 9,5 SemiBold.
  - Aus: Fläche Surface2 mit 1 px LineStrong, Text TextSecondary. An: Hp.Brush.Accent mit TextOnAccent.
  - Sichtbar, wenn die Zahl > 0 ist oder der Filter an ist. Dann zeigt sie auch „0“, und die leere Liste ist erklärt. Ab 1000 steht „999+“. Für die heutigen Daten: Automatik ohne Plakette, Neu „1“, HDR-fähig „36“.
- Tooltip: eigener Stil Lib.ToolTip.
  - Aussehen: Menüfläche, Rahmen LineStrong, Radius 10, Innenabstand 12,9, MaxWidth 300. Schatten als Lib.PopupShadow-Flächen, kein DropShadowEffect.
  - Drei Zeilen: Titel 12,5 Bold, Erklärung 12 TextSecondary, Zustand Mono 10,5.
  - Automatik: „Nur HDR automatisch“ / „Zeigt nur Spiele, bei denen HDRpilot HDR beim Start einschaltet.“ / „0 Spiele · Filter aus“.
  - Neu: „Nur neue Spiele“ / „Beim Einlesen gefunden und noch nicht angesehen. Sie schalten HDR erst, wenn du es einschaltest.“ / „1 Spiel · Filter aus“.
  - HDR-fähig: „Nur HDR-fähige Spiele“ / „HDR laut PCGamingWiki, auch eingeschränkt.“ / „36 Spiele · Filter aus“.
- ToolTipService: InitialShowDelay 1000, BetweenShowDelay 150, ShowDuration 30000 (bleibt, solange die Maus darauf ist), ShowOnDisabled True, ShowsToolTipOnKeyboardFocus True, Placement Top mit VerticalOffset −6. Oberhalb verdeckt der Tooltip das Bild statt der Liste; WPF klappt nach unten, wenn oben kein Platz ist. Esc schließt.
  Das gilt einheitlich für alle reinen Symbolknöpfe der Leiste: Neu einlesen, gesehen und Ansicht, Hinzufügen bzw. Sortierung, sobald deren Beschriftung weicht. Kürzel stehen in der Zustandszeile („Strg+1“ usw.).
- Tastatur und Screenreader: Es bleibt ein ToggleButton (Leertaste/Enter). Name wie heute (Filter_OnlySelectedName usw.), die Zahl als AutomationProperties.ItemStatus („36 Spiele“), die Erklärung als HelpText. Tab-Reihenfolge wie sichtbar.

### widthAnalysis

GRUNDLAGE
Alle Werte in DIP, aus dem Code (MainWindow.xaml, Library.xaml, ToolbarFlowPanel.cs) und den Messungen in toolbar\rvf-probe-sweep.txt und measure-text.ps1.
- Abstand: Gap = 8 (Vorgabe des Panels, in der XAML nicht gesetzt), RowGap = 8.
- Das Panel rundet natürliche Breiten auf ganze DIP auf und teilt mit Floor(L).

Breiten der Elemente:
- Suche: Basis 236, in einer Zeile bis 320 (SingleRowMaxWidth), Grow 4.
- Quelle: Basis 188. Inhalt „● Alle 652“: 12+8+9+22,4+10+14+18,5+34 ≈ 128.
- Sortierung: Basis 196. Inhalt „Zuletzt gespielt“: 12+13+9+91,1+34 ≈ 159. Nur Symbol: 12+13+34 = 59 → 60.
- Chip: Innenabstand 11+10, Symbol 13, Abstand 8, Text, Zähler (8+14+Ziffern Mono 10,5 zu je 6,15).
  - Automatik(0): 132,4 → 133
  - Neu(1): 94,9 → 95 (mit 652: 107,2 → 108)
  - HDR-fähig(36): 138,8 → 139
- Ansicht: Rahmen 2 × (3+1) = 8, Zellen 10+10+15+6+Text.
  - Mit Beschriftung: Liste 67, Raster 77, Regal 73 → 225.
  - Nur gewählte beschriftet: 8+38+38+77 = 161 (Höchstwert, gerechnet mit „Raster“).
  - Nur Symbole: 8 + 3 × 38 = 122.
- Neu einlesen 40, gesehen 40.
- Spiel hinzufügen: 142,0 → 143. Nur „+“: 40.

Summen:
- Heute: 236+188+196+133+95+139+225+40+40+143 = 1435, plus 9 × 8 = 72, ergibt 1507.
- Filter als Symbole: 1435 − 367 + 120 = 1188, plus 72, ergibt 1260 (−247).

LISTENBREITE L (Detailbereich in Standardbreite, FitDetailsColumn)
- Root = W − 2. L = W − 2 − 64 − 24 − P mit P = clamp(round(0,23·(W−2)); 340; 396).
- Daraus: W ≤ 1480 → L = W − 430; W ≥ 1724 → L = W − 486.
- Die Probe bestätigt das: 1000 → 570, 1600 → 1142, 1987 → 1501.
- Detailbereich ganz breit (MaxWidth 560, ListMinWidth 400): L = max(400; W − 650).
- Regal: W − 66.

| Größe | W | L Standard | L schmalste | Regal |
|---|---|---|---|---|
| 1280×800 @110 % | 1163,6 | 733 | 513 | 1097 |
| 1366×768 @110 % | 1241,8 | 811 | 591 | 1175 |
| 1600×1000 px @110 % | 1454,5 | 1024 | 804 | 1388 |
| Standardfenster 1600 DIP | 1600 | 1142 | 950 | 1534 |
| 1920×1080 @110 % | 1745,5 | 1259 | 1095 | 1679 |
| 3440×1440 @110 % | 3127,3 | 2641 | 2477 | 3061 |
| Mindestfenster | 1000 | 570 | 400 | 934 |

ZEILEN JE VARIANTE (Spalten: L Standard 733 / 811 / 1024 / 1142 / 1259 / 2641)
- a) Heute, 1507. Zwei Zeilen ab 777 (Teilung „Suche … Automatik | Rest“), drei darunter: 3 / 2 / 2 / 2 / 2 / 1. Das passt zu den Renderings.
- b) Nur Filter als Symbole, 1260. Zwei Zeilen ab 636 (Teilung nach „Sortierung“: 636 | 616): 2 / 2 / 2 / 2 / 2 / 1. Bei 1920 @110 % fehlt genau 1 DIP; Symbol-Filter allein genügen also nicht.
- c) b + Ansicht und Hinzufügen nur als Symbol, Felder fest: 236+188+196+120+122+40+40+40 = 982, plus 72 = 1054: 2 / 2 / 2 / 1 / 1 / 1.
- d) Vorschlag Stufenleiter (eine Zeile ab 798): 2 (S0 mit allen Beschriftungen) / 1 (S4) / 1 (S2) / 1 (S0) / 1 (S0) / 1 (S0, Suche 320).

STUFENLEITER, eine Zeile
Feste Teile: Filter 120 + Neu einlesen 40 + gesehen 40 + 72 Abstände = 272. Die Felder Suche, Quelle und Sortierung haben je drei Breiten (bequem / Minimum / Höchstwert in einer Zeile):
- Suche 236 / 168 / 320
- Quelle 188 / 136 / 188
- Sortierung 196 / 160 / 196
- Summe bequem 620, Minimum 464.

| Stufe | Rechnung | eine Zeile ab L |
|---|---|---|
| S0 alle Beschriftungen | 272 + 225 + 143 = 640, + 464 | 1104 |
| S1 nur gewählte Ansicht beschriftet | 640 − 64 = 576, + 464 | 1040 |
| S2 Ansicht nur Symbole | 576 − 39 = 537, + 464 | 1001 |
| S3 Hinzufügen nur „+“ | 537 − 103 = 434, + 464 | 898 |
| S4 Sortierung nur Symbol | 434 + 60 + Suche/Quelle-Minimum 304 | 798 |

- S0: bequem ab 1260, Suche wächst bis 320 ab 1344. Die Felder geben in S0 anteilig nach: k = (1260 − L)/156, Suche 236 − 68k, Quelle 188 − 52k, Sortierung 196 − 36k. Bei 1142 sind das 185 / 149 / 169; der Hinweis „Strg+F“ weicht bei Suche < 204, also L < 1187.
- Unter 798: zwei Zeilen.

ZWEI ZEILEN
Umbruch nur an Gruppengrenzen, die Beschriftungen kehren zurück.
- Zeile 1: Suche | Quelle | Sortierung = 464 + 16 = 480 (S4: 380).
- Zeile 2: Filter | Ansicht | Neu einlesen, gesehen | Hinzufügen = 120 + Ansicht + 80 + Hinzufügen + 6 × 8. Das ergibt S0 616, S1 552, S2 513, S3 410.
- Zwei Zeilen also mit S0 ab 616, S1 ab 552, S2 ab 513, S3 ab 480, S4 ab 410.
- Drei Zeilen (Suche|Quelle · Sortierung|Filter · Ansicht|Aktionen|Hinzufügen) nur bei L 400–409, also im Mindestfenster mit ganz breitem Detailbereich: S1 ab 408, sonst S2.

ERGEBNIS, Standard-Detailbereich, 110 %
| Größe | L | Ergebnis |
|---|---|---|
| 3440 | 2641 | S0, Suche 320, Spacer 1297 |
| 1920 | 1259 | S0 mit allen Beschriftungen |
| Standardfenster | 1142 | S0 mit allen Beschriftungen |
| 1600 px | 1024 | S2, 23 DIP Spacer |
| 1366 | 811 | S4, eine Zeile, 13 DIP Spacer |
| 1280 | 733 | zwei Zeilen S0 (Zeile 2 von 616 auf 733 gestreckt) |
| Mindestfenster | 570 | zwei Zeilen S1 |

ERGEBNIS, schmalste Liste (Detailbereich 560)
| W | L | Ergebnis |
|---|---|---|
| 1000 | 400 | drei Zeilen S2 |
| 1163,6 | 513 | zwei Zeilen S2 (genau passend; Grenzfall, bei Pixelrundung evtl. S3) |
| 1241,8 | 591 | zwei Zeilen S1 |
| 1454,5 | 804 | eine Zeile S4 (von schmaler kommend wegen der Hysterese bis 830 noch zwei Zeilen) |
| 1600 | 950 | S3 |
| 1745,5 | 1095 | S1 |
| 3127 | 2477 | S0 |

- Regal: W − 66 ≥ 934, also bei jeder erlaubten Fenstergröße eine Zeile (Mindestfenster S3, 1280 → S1, ab 1366 → S0). Der Sonderfall „Leiste neben der Automatik-Karte“ tritt damit praktisch nicht mehr auf.
- Andere Skalierungen, maximiert: 1366 @100 % → 936 (S3); 1280 @100 % bzw. 1920 @150 % → 850 (S4); 1920 @125 % → 1093 (S1); 1920 @100 % → 1434 (S0, Suche 320); 2560 @125 % → 1562; 3840 @150 % → 2074 (S0).

PIXELRUNDUNG
Gerechnet wird in Gerätepixeln, s = Skalierung:
- Abstand round(8s) = 8 / 9 / 10 / 12 px bei 100 / 110 / 125 / 150 %, überall gleich.
- Höhe 40 DIP = 40 / 44 / 50 / 60 px, exakt.
- Luft round(10s, AwayFromZero) = 10 / 11 / 13 / 15 px, oben und unten gleich. Dafür zuerst die Naht runden, dann davon ausgehend.
- Feste Breiten werden auf ganze px aufgerundet (Ansicht 225 DIP = 247,5 → 248 px bei 110 %).
- Restpixel gehen an Felder bzw. den Zwischenraum, nie an feste Knöpfe.
- Die Schwellen steigen dadurch um bis zu etwa 3 DIP. Bei 110 % sind das 9 Abstände × 0,18 plus Aufrundung; an der Zeilenzahl der Tabelle ändert das nichts, abgesehen vom genannten Grenzfall 513. In 1920 @110 % geben die Felder nur rund 4 px nach.
- Heute liegen die Kanten bei 110/125 % auf Bruchteilen von Pixeln. Daher bekommt die Leiste UseLayoutRounding, oder das Panel rundet selbst.

### hideShowConcept

GRUNDIDEE
Die Leiste lässt sich auf die Naht falten. Die Naht ist die Unterkante des Titelbilds, auf der schon der Ziehgriff sitzt. Alles, was die Grenze zwischen Bild und Liste betrifft, liegt auf der Grenze:
- in der Mitte der Griff (Höhe ziehen),
- rechts die Lasche (Leiste einklappen) bzw. eingeklappt die Kapsel.
Vorbild ist „Menüband reduzieren“ in Office (Strg+F1, Pfeil an der Ecke).

AUSGEKLAPPT (Liste, 1920×1080 @110 %, Stufe S0)
```
│   GEARS OF WAR  E-DAY        [▷ Starten] [☀ Beim Start …]         │
│                                                                  │ ← 16 px freier Innenrand
╞════════════════════════════ ▬▬▬ ═══════════════════════ ╭─⌃─╮ ══╡ ← Naht: Griff Mitte, Lasche rechts
│                                                         ╰───╯    │   (6 px über der Leiste)
│[🔍 Suchen…  Strg+F][● Alle 652 ⌄][🕘 Zuletzt gespielt ⌄][☀][✦¹][HDR³⁶]  [▤ Liste ▦ ▥][⟳][👁][＋ Spiel hinzufügen]│
│                                                                  │ ← 10 px Luft
│┌ SPIEL · 652 ─────────────────── ZULETZT GESPIELT ── HDR ── AUTO ┐│
```
Lasche: 32 × 16 sichtbar, Trefferfläche 32 × 20, Radius 8, Glas mit Rahmen Line, Pfeil E70E 9 px. Beim Überfahren wächst sie nach links zu „⌃ Leiste ausblenden  Strg+F1“.

EINGEKLAPPT
```
│   GEARS OF WAR  E-DAY        [▷ Starten] [☀ Beim Start …]         │
╞════════════════════════════ ▬▬▬ ════╭ 🔍 „gears“ · HDR · 36 von 652  ✕ │ ⌄ ╮╡ ← Kapsel 24 px, mittig auf der Naht
│                                     ╰──────────────────────────────────╯│
│┌ SPIEL · 36 ─────────── (Liste 38 px höher) ──────────────────────┐│ ← 10 px Luft unter der Kapsel
```
- Ohne Filter steht in der Kapsel nur „╭ 🔍 Suchen  Strg+F │ ⌄ ╮“.
- Mit Filter zeigt die Kapsel: den Suchbegriff (höchstens 140 px, mit Auslassung), die Quelle, wenn sie nicht „Alle“ ist (Farbpunkt + Name), aktive Filter als ihre Symbole in Akzentfarbe, „36 von 652“ (Mono) und ✕ (alles zurücksetzen). Ihr Rahmen ist dann AccentStrong mit Hof wie ein eingeschalteter Filter. Es gibt also nie einen unsichtbaren Filter; die Statuszeile nennt ihn zusätzlich.
- Breite höchstens bis 12 px vor den Griff (L/2 − 38); was nicht passt, wird zu „+2“.

AUSLÖSER
- Maus: Klick auf die Lasche klappt ein, Klick auf die Kapsel klappt aus (beides wird gemerkt). Das ✕ setzt Suche und Filter zurück, die Leiste bleibt eingeklappt. Rechtsklick auf die freie Fläche der Leiste oder auf die Kapsel öffnet ein Kontextmenü „Leiste ausblenden / einblenden  Strg+F1“; das ist der gleichwertig große Weg zur kleinen Lasche.
- Tastatur:
  - Strg+F1 schaltet um und merkt den Zustand.
  - Enter/Leertaste auf der Kapsel klappt dauerhaft aus.
  - Strg+F bei eingeklappter Leiste klappt sie nur vorübergehend aus („Spähen“) und setzt den Fokus in die Suche. Verlässt der Fokus die Leiste (Enter, Esc, Klick in die Liste), ist kein Aufklappmenü offen und die Maus nicht über der Leiste, faltet sie sich nach 600 ms zurück. Der Suchbegriff steht dann in der Kapsel.
  - Strg+1/2/3 wirken weiter.
- Automatisch: nichts. Kein Ausblenden beim Scrollen, denn der Nutzer hat für den Titelbereich „Größe bleibt“ gewählt. Höchstens später als Option, die standardmäßig aus ist.
- Gemerkt wird der Zustand in AppSettings.LibraryToolbarCollapsed (für alle Ansichten), wie LibraryHeroRatio. Beim Start wird er ohne Animation hergestellt.

ZIEHGRIFF UND LUFTREGEL
- Der Griff bleibt in der Mitte; Lasche und Kapsel liegen rechts und nie über ihm. Der Griff bleibt überall sonst ziehbar; die Kapsel liegt nur über ihrem eigenen Stück.
- Ausgeklappt gilt 10 px Luft über und unter der Leiste unverändert. Die Lasche ist wie der Griff ein Element der Naht: 12 px in den garantiert freien 16-px-Innenrand, 4 px in die Luft.
- Eingeklappt bleibt 10 px Luft unter der Kapsel. Die Liste beginnt bei Naht + 22 statt + 60, gewinnt also 38 px. ListHeroRange erlaubt einen 38 px höheren Titelbereich, die gemerkte Höhe bleibt aber, wie sie ist: Die Liste bekommt den Platz, das Bild wächst nicht von selbst.

RASTER
- Die Leiste beginnt 22 px unter der Naht. Die Lasche sitzt 6 px über ihrer rechten Ecke, also in der Luft.
- Eingeklappt liegt die Kapsel auf der Naht, und die erste Kachelreihe beginnt 22 px darunter: Kacheln bei Naht + 34 statt + 84, also 50 px Gewinn.
- GridHeroHeight rechnet dann mit 12 statt mit der Leistenhöhe; in niedrigen Fenstern darf der Titelbereich wieder auf clamp(H·0,24) wachsen, in derselben Bewegung.

REGAL
- Die Leiste liegt im Bild über der Kachelreihe. Die Lasche sitzt 6 px über ihrer rechten Ecke im Bild.
- Eingeklappt sitzt die Kapsel rechts dort, wo die Leiste endete (Unterkante 10 px über der Naht). Die Kachelreihe bleibt unberührt.
- Titel und Knöpfe rücken 40 px tiefer (LargeHeroInset 74 → 34): mehr Bühne, das Logo darf größer werden.

FOKUS
- Liegt der Fokus beim Einklappen in der Leiste, geht er auf die Kapsel.
- Beim Ausklappen per Kapsel geht er auf das zuletzt fokussierte Element der Leiste, sonst auf die Suche.
- Tab-Reihenfolge: Titelbereich → Griff → Leiste (Lasche als letztes Element) → Liste; eingeklappt Titelbereich → Griff → Kapsel → Liste.

### animationSpec

GRUNDREGELN
- Dauer: Alle Dauern laufen über MotionController.Transition() bzw. LibraryMotion.Enabled. Voll heißt volle Dauer. Reduziert heißt höchstens 300 ms und hier ganz ohne Bewegung, nur Überblenden. Aus, minimiert, unsichtbar, laufendes Spiel oder keine Hardware-Darstellung heißt 0, also sofort.
- Windows „Animationseffekte“ aus ergibt laut EffectPolicy mindestens Reduziert, also keine Bewegung.
- Nur Einmal-Übergänge, keine Dauer-Animation: In Ruhe zeichnet die Leiste 0 Bilder, die 30-fps-Grenze wird nicht berührt.

WANN ANIMIERT, WANN SOFORT (Entscheidung)
- Die Felder Suche, Quelle und Sortierung folgen der Breite immer direkt, ohne Animation. Eine Animation liefe dem Fensterrand hinterher (Gummiband-Effekt).
- Diskrete Schritte (Beschriftung, Hinweis „Strg+F“, Zeilenzahl) werden animiert, wenn der Nutzer selbst zieht: Fensterrand zwischen WM_ENTERSIZEMOVE und WM_EXITSIZEMOVE oder Detail-Trenner. Er schaut dabei hin und bewegt langsam, ein Sprung fiele auf.
- Sofort bei Sprüngen des Fensters: Maximieren, Wiederherstellen, Snap/Win+Pfeil, DPI- oder Monitorwechsel, erstes Anordnen, Rückkehr aus Minimiert, Breitenänderung > 64 DIP in einem Schritt außerhalb des Ziehens. Dabei springt ohnehin das ganze Fenster (DWM animiert den Rahmen); eine nachlaufende Bewegung wirkte wie Verzögerung.
- Ansichtswechsel: Die Bewegung läuft mit der Bewegung der Ansicht mit (Kurve wie HeroMotion).
- Hysterese:
  - Beschriftungen: kompakter sofort bei L < Schwelle, großzügiger erst bei L ≥ Schwelle + 24 DIP.
  - Hinweis „Strg+F“: 12 DIP (weicht bei Suche < 204 DIP, kommt bei ≥ 216 zurück).
  - Zeilen: 1 → 2 bei L < 798, 2 → 1 erst bei ≥ 830; 3 → 2 erst bei ≥ 442.
  - Neue Ziele lenken eine laufende Bewegung um (HandoffBehavior.SnapshotAndReplace, Start im aktuellen Zwischenwert). Höchstens ein Zeilenwechsel gleichzeitig; der letzte Zustand gewinnt.

1. BESCHRIFTUNG WEICHT (Ansicht S1/S2, Hinzufügen S3, Sortierung S4)
Umsetzung: Decorator „CollapsibleLabel“ mit Progress 0–1. Er misst die Textbreite × Progress, zeichnet den Text in voller Breite und schneidet ihn mit einer RectangleGeometry ab; der Abstand 6/8 DIP zum Symbol schrumpft mit. Es gibt kein Umbrechen und kein Überlaufen.
- Voll, weichen: Deckkraft 1 → 0 in 0–90 ms (linear), Text X 0 → −4, Breite in 40–200 ms CubicEase EaseOut.
- Voll, zurück: Breite in 0–160 ms, Deckkraft 80–200 ms, Text −4 → 0.
- Voll, S1 beim Ansichtswechsel: Die alte Zelle schließt, die neue öffnet sich (220 ms); die Akzent-Pille gleitet in X und Breite (240 ms, ExponentialEase EaseOut, Exponent 5). Die Pille gleitet in allen Stufen.
- Reduziert: Breite sofort, Inhalt des Knopfs 120 ms überblendet, Pille überblendet.
- Aus: sofort.

2. HINWEIS „STRG+F“
- Voll: Deckkraft 120 ms; Innenabstand rechts 70 → 12 in 160 ms CubicEase EaseOut.
- Reduziert: Deckkraft 100 ms, Abstand sofort.
- Aus: sofort.

3. ZEILENWECHSEL 1 ↔ 2 (nur Liste und Raster; das Regal bleibt immer einzeilig)
- Voll: Das ToolbarFlowPanel hält alte und neue Anordnung und interpoliert über eine Uhr (MorphProgress 0 → 1) Lage und Breite jedes Elements: 300 ms ExponentialEase EaseOut, Exponent 6 (Kurve des Titelbereichs).
  - Elemente, die in Zeile 2 wechseln, laufen in Leserichtung je 16 ms versetzt (höchstens 96 ms).
  - Die Höhe der Leiste geht 40 ↔ 88; Liste bzw. Raster darunter folgen in derselben Kurve.
  - Zurückkehrende Beschriftungen blenden in 120–300 ms ein, beim Zusammenführen in 0–100 ms aus.
  - Greift die Grenze des Titelbereichs (ListHeroRange, GridHeroHeight), bewegt er sich in denselben 300 ms statt 800. Dafür rechnet MainWindow mit der Zielhöhe der Leiste statt mit ActualHeight, sonst würde die Animation des Titelbereichs je Bild neu gestartet.
- Reduziert: Leiste 80 ms aus, neue Anordnung, 120 ms ein; der Inhalt darunter springt einmal.
- Aus: sofort.

4. EINKLAPPEN (Voll, 420 ms)
- 0–200 ms: Die Elemente falten zur Lasche: Deckkraft 1 → 0, X +12 px, ScaleX 1 → 0,96, von links nach rechts je 14 ms versetzt (CubicEase EaseIn).
- 60–380 ms: Liste bzw. Raster gleiten nach oben (Höhe der Leistenzeile → Kapselgeometrie, ExponentialEase EaseOut, Exponent 6).
- 140–380 ms: Die Lasche wird zur Kapsel: Höhe 16 → 24, Breite 32 → Inhalt (wächst nach links), Y auf die Naht, CubicEase EaseOut.
- 300–420 ms: Der Inhalt der Kapsel blendet ein.
- Danach einmal ein Glanz über den Rahmen der Kapsel: Versatz eines LinearGradientBrush, 360 ms, nur bei Stufe Voll und aktivem Fenster.
- Ausklappen umgekehrt: Kapsel → Lasche in 0–180 ms, Liste gleitet in 60–380 ms herunter, Elemente erscheinen von rechts nach links je 14 ms versetzt (Deckkraft 0 → 1, X +12 → 0).
- Reduziert: Leiste und Kapsel überblenden in 150 ms, der Inhalt springt.
- Aus: sofort.

5. LASCHE BEIM ÜBERFAHREN
- Voll: Breite 32 → Text in 160 ms CubicEase EaseOut, Text blendet in 60–160 ms ein.
- Reduziert: Breite sofort, Text in 100 ms.
- Aus: sofort.

6. FILTER EINSCHALTEN
- Voll: vorhandene Überblendung der An-Fläche (200 ms, Hp.Ease.Out), Hof 0 → 0,55 in 200 ms, Symbol-„Pop“ mit Scale 0,88 → 1,06 → 1 in 240 ms.
- Reduziert: nur die An-Fläche in 150 ms.
- Aus: sofort.
- Zähler ändert sich (nur aktives Fenster): Stufe Voll Text 120 ms überblendet und Plakette Scale 1 → 1,15 → 1 in 180 ms; Reduziert 100 ms Überblendung.

7. TOOLTIP
- Voll: Deckkraft 0 → 1 und Y 4 → 0 in 140 ms CubicEase EaseOut.
- Reduziert: Deckkraft 100 ms.
- Aus: keine Animation.

PIXEL
- Während einer Bewegung TextOptions.TextHintingMode = Animated, danach wieder Auto.
- Das letzte Bild ist genau die gerundete Endlage, damit am Ende nichts um 1 px nachrutscht.
- Während der Interpolation keine Layoutrundung, sonst stuft sich die Bewegung in 1-px-Schritten (Zittern).

GPU-KOSTEN
- Kein BlurEffect, DropShadowEffect, Mischmodus oder OpacityMask. Abgeschnitten wird per RectangleGeometry; die Schatten sind die vorhandenen PopupShadow-Flächen.
- Bewegt werden Deckkraft und Transforms kleiner Flächen. Layout je Bild läuft nur in der Leiste; nur bei Zeilenwechsel und Einklappen kommt für 300–420 ms der Inhalt darunter dazu. Die virtualisierte Liste ordnet dabei nur die sichtbaren Container an; diesen Fall im phase10-gpu-Gate beim Ziehen messen. Falls er zu teuer ist: Höhe sofort setzen und nur den Inhalt per TranslateTransform von alt nach 0 schieben.
- In Ruhe, minimiert, inaktiv mit „Im Hintergrund anhalten“ und während eines Spiels: 0 Uhren, weil Transition() dann 0 liefert.

### accessibility

- Namen bleiben fest:
  - Filter: Filter_OnlySelectedName, Filter_OnlyNewName, Filter_OnlyHdrCapableName.
  - Ansicht: „Liste“, „Raster“, „Regal“.
  - „Spiel hinzufügen“, „Spiele sortieren“ (Wert = gewählte Sortierung).
  Wenn eine Beschriftung weicht, ändert sich für Bildschirmleser nichts; nur ein Text-Decorator schrumpft.
- Zahlen: als AutomationProperties.ItemStatus („36 Spiele“), die Erklärung des Tooltips als HelpText. Der Bildschirmleser liest dann zum Beispiel „Nur HDR-fähige Spiele, Umschalter, ein, 36 Spiele“. Die Plakette ist Teil der Vorlage und kein eigenes UIA-Element: Ein TextBlock mit TemplatedParent ist kein Control-Element.
- Fokus geht nie verloren: Elemente werden nur angeordnet, nie entfernt oder umgehängt (Garantie des ToolbarFlowPanel bleibt). Die Sortierung „nur Symbol“ ist dieselbe ComboBox. Das Aufklappmenü zeigt weiter die vollen Texte (MinWidth 236).
- Beim Einklappen geht ein Fokus in der Leiste auf die Kapsel. Beim Ausklappen geht er auf das zuletzt fokussierte Element, sonst auf die Suche. Strg+F führt immer in die Suche.
- Tastatur:
  - Leertaste/Enter schalten Filter (ToggleButton).
  - Strg+1/2/3 wählen die Ansicht.
  - Strg+F1 blendet die Leiste ein oder aus (Office-Vorbild, in WPF frei).
  - Die Kapsel öffnet mit Enter/Leertaste. Das Kontextmenü der Leiste erreicht man mit Umschalt+F10.
  - Tooltips erscheinen auch bei Tastaturfokus (ShowsToolTipOnKeyboardFocus) nach 1,0 s; Esc schließt sie.
- UIA für die Kapsel: eigener Peer mit ExpandCollapsePattern. Name „Werkzeugleiste“, Zustand eingeklappt/ausgeklappt, ItemStatus „Suche „gears“, HDR-fähig, 36 von 652“, HelpText „Strg+F1 blendet die Werkzeugleiste ein oder aus“.
- UIA für die Lasche: Name „Werkzeugleiste ausblenden“, HelpText Strg+F1.
- Beim Umschalten meldet AutomationPeer.RaiseNotificationEvent einmal „Werkzeugleiste ausgeblendet“ bzw. „eingeblendet“ (MostRecent). Für Zählerwechsel gibt es keine Live-Region, sonst würde jedes Einlesen angesagt.
- Zielgrößen:
  - Filter- und Symbolknöpfe 40 × 40.
  - Kapsel 24 hoch.
  - Lasche sichtbar 32 × 16, Trefferfläche 32 × 20. Sie liegt unter 24 DIP; dafür gibt es das gleichwertige Kontextmenü und das Kürzel (WCAG 2.5.8, Ausnahme „gleichwertig“).
- Kontrast: Symbole TextSecondary auf Glas mindestens 3:1 (1.4.11), Plakette und Tooltip-Text mindestens 4,5:1 in Hell und Dunkel. „An“ ist nicht nur Farbe (Fläche, Rahmen, Hof). Der Fokusring bleibt klar von „an“ unterscheidbar.
- Bewegung: Windows „Animationseffekte“ aus ergibt Reduziert, also keine Verschiebung, Skalierung oder Staffelung, nur Überblendungen bis 150 ms. Stufe Aus und Remotedesktop ohne Hardware-Darstellung: alles sofort.
- Die Kapsel zeigt aktive Filter immer an. Eine gefilterte Liste ist so nie unerklärt; die Statuszeile nennt sie zusätzlich.

### risks

1. Weichende Beschriftungen. Der Nutzer mochte schon das alte Einklappen nicht. Abhilfe:
   - Zuerst geben die Felder nach; die Beschriftungen bleiben bis L 1104. Das deckt 1920×1080 @110 % maximiert und das Standardfenster ab.
   - Sie weichen in kleinen, animierten Schritten. Bei S1 bleibt die gewählte Ansicht beschriftet.
   - Nie werden Filter in ein Menü gefaltet.
   - In zwei Zeilen kommen alle Beschriftungen zurück.
   - Optional im Kontextmenü „Beschriftungen immer zeigen“ (dann früher zwei Zeilen).
2. Bedeutung der Symbole (Automatik gegen HDR-fähig, Stern als Favorit). Abhilfe: eigene Symbole (Funkeln, Wortmarke „HDR“), Zähler-Plakette, Tooltip nach 1,0 s. Vor dem Bau im Prototyp mit dem Nutzer prüfen.
3. Zwischenraum nach einer Stufe. Er öffnet sich (S1 64, S2 39, S3 103, S4 100 DIP) und schließt sich beim weiteren Verkleinern wieder. Er wirkt wie der Zwischenraum im breiten Fenster; die Felder wachsen beim Schmalerziehen nie. Will der Nutzer lieber „immer gefüllt“: Schalter im Panel, dann füllen die Felder auf. Sie wachsen beim Weichen der Beschriftungen kurz, bis zu 100 DIP: unruhiger, dafür ohne Lücke.
4. Unruhe beim Ziehen. Abhilfe: Nur Schritte werden animiert, Hysterese 24/32 DIP, höchstens ein Zeilenwechsel gleichzeitig, Sprünge des Fensters sofort, Reduziert ohne Bewegung. Die Last beim Ziehen wird im GPU-Gate gemessen. Fallback, wenn die Liste je Bild zu teuer ist: nur verschieben statt neu messen.
5. Spähen per Strg+F (Zurückfalten nach 600 ms) könnte überraschen. Es greift nur, wenn der Nutzer selbst eingeklappt hat; ein Klick auf die Kapsel klappt dauerhaft aus. Alternative: Strg+F klappt dauerhaft aus.
6. Lasche zu klein oder übersehen. Abhilfe: Beim Überfahren wächst sie mit Text; Kontextmenü und Strg+F1; das Kürzel steht in den Tastenhinweisen der Statuszeile (ab 1500 DIP sichtbar). Es gibt keinen Erststart-Hinweis.
7. Vergessene Filter bei eingeklappter Leiste. Abhilfe: Kapsel in Akzentfarbe mit „36 von 652“ und ✕, dazu die Statuszeile.
8. Grenzfälle mit 0–1 DIP Spielraum:
   - 1920 @110 %: 1259 gegen 1260.
   - Schmalste Liste bei 1280 @110 %: 513 gegen 513.
   Durch die Rechnung in Gerätepixeln verschieben sich die Schwellen um bis zu 3 DIP. Abhilfe: Tests je Skalierung (100/110/125/150 %) mit festen Erwartungswerten.
9. Plakette ragt in Luft und Zeilenabstand (6 bzw. 4 px). Sie ist nicht klickbar; Griff und Nachbarknopf bleiben bedienbar. Bei zwei Zeilen bleiben 2 px zum oberen Element; notfalls Margin −5.
10. Layoutrundung gegen Animation. Gerundete Zwischenwerte zittern um 1 px. Daher bewegt ohne Rundung, Endlage gerundet, TextHintingMode Animated.
11. Strg+F1: F1 ist Hilfe, Strg+F1 ist in WPF frei. Wer Office nicht kennt, findet es über Tooltip und Kontextmenü.
12. Kein Ausblenden beim Scrollen. Wer das erwartet (iOS-Stil), vermisst es vielleicht; es bleibt bewusst aus wegen „Größe bleibt“ und könnte später als Option kommen.

### effort

Aufwand gesamt: mittel bis groß, etwa 2–3 Arbeitstage. Ungefähr 1 200–1 600 Zeilen einschließlich Tests; das meiste in Panel, Stilen und Fensterlogik. Risiko mittel: Animation und Layout greifen ineinander. Vorschlag: zuerst Stufenleiter und Pixelrechnung als reine Funktion mit Tests, dann Morph, dann Ein-/Ausklappen.

Alle Dateien unter .worktrees\s11-bar:

- src\HdrPilot.App\Views\Library\ToolbarFlowPanel.cs (größter Teil, etwa 350–450 Zeilen):
  - Stufenleiter: Minimum, bequeme Breite und Höchstwert je Element, Stufen S0–S4 mit Schwellen.
  - Hysterese-Zustand (24/32 DIP); Umbruch nur an Gruppengrenzen (neue angehängte Eigenschaft Group statt freier Teilung).
  - Rechnung in Gerätepixeln (DpiScale, Abstand round(8s)).
  - Morph über MorphProgress (alte und neue Anordnung interpoliert).
  - TargetHeight für MainWindow statt ActualHeight.
  - Die reine Funktion Layout() bleibt ohne WPF testbar.
- src\HdrPilot.App\Views\Library\LibraryControls.cs: neuer Decorator CollapsibleLabel (Progress, Clip), etwa 80 Zeilen.
- src\HdrPilot.App\MainWindow.xaml (etwa 150 Zeilen):
  - Chips → Symbol-Umschalter mit Plakette.
  - CollapsibleLabel in Ansicht, Hinzufügen und Sortiervorlage.
  - Lasche, Kapsel, Kontextmenü, ToolTipService-Werte.
- src\HdrPilot.App\Themes\Library.xaml (etwa 200 Zeilen): Lib.ToolbarToggle, Lib.CountBadge, Lib.ToolTip, Lib.ToolbarTab, Lib.ToolbarCapsule, gleitende Pille in Lib.SegmentFrame/SegmentRadio, Symbole „Funkeln“ und „HDR“.
- src\HdrPilot.App\MainWindow.xaml.cs (etwa 200 Zeilen):
  - HwndSource-Hook WM_ENTERSIZEMOVE (0x0231) und WM_EXITSIZEMOVE (0x0232), Sprung-Erkennung.
  - Zielhöhe der Leiste in RefreshHeroHeight, ApplyVerticalLayout und GridRow.
  - Ein-/Ausklappen, Strg+F1 und Spähen mit Strg+F in HandleKey, Fokusübergabe, UIA-Notification.
- src\HdrPilot.App\Views\Library\ShowcaseLayout.cs (etwa 60 Zeilen):
  - Konstanten CapsuleHeight 24, TabHeight 16, TabOffset 6.
  - ToolbarTop, ListHeroRange, GridHeroHeight und LargeHeroInset mit dem Zustand „eingeklappt“.
  - ShelfToolbarBesideCard bleibt als Sicherheitsnetz.
- Einstellungen: AppSettings und LibraryViewModel bekommen LibraryToolbarCollapsed (etwa 30 Zeilen).
- Texte: src\HdrPilot.App\Resources\LibraryStrings.resx und Designer, etwa 20 neue Texte (Tooltips, Kapsel, Lasche, Meldungen).
- Tests und Gates:
  - ToolbarFlowLayoutTests: Schwellen 1104/1040/1001/898/798, zwei Zeilen 616/552/513/480/410, Hysterese, Rundung bei 100/110/125/150 %.
  - ShowcaseLayoutTests für die eingeklappte Geometrie.
  - UI-Tests: Namen, ItemStatus, Fokus beim Ein-/Ausklappen, Strg+F1.
  - Bildschirmfotos je Größe, Ansicht und Thema.
  - GPU/CPU-Messung beim Ziehen des Fensterrands.
- Danach die Doku nachziehen: docs\design\showcase-hero\README.md (Abschnitt Werkzeugleiste) und GATES.md.

