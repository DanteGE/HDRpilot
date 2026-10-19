# Gate G10.5: Bibliothek „Showcase Hero“ per UI-Automation an einer eigenen Testinstanz (eigene Ablage, echte Bibliothek
# eingelesen und alle Spiele auf „HDR automatisch“ aus, nur Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben)
# und Bildschirmfotos der drei Ansichten, der gescrollten Liste und der eingeklappten Leiste bei 1280×800, 1920×1080 und
# 3440×1440, hell und dunkel.
#   - Ansichten in der Reihenfolge Liste, Raster, Regal; Liste ist der Standard, die Wahl wird gemerkt (auch nach Neustart)
#   - Liste: virtualisiert, nach Spalten sortierbar (gespeichert), Schalter „HDR automatisch“ je Zeile, Vorschau Bild/Symbol
#   - Liste: Höhe des Titelbereichs per Ziehgriff HeroSplitter (Wunsch des Nutzers 2026-10-16): Standard ein Drittel der
#     Fensterhöhe, per UI-Automation gesetzt (RangeValue, keine echte Eingabe), gemerkt (auch nach Neustart), Auslösen
#     (Invoke, wie Doppelklick) setzt zurück; Raster und Regal ohne Griff und mit ihrer bisherigen Höhe
#   - Werkzeugleiste „Fließleiste mit Lichtnaht“ (Wunsch des Nutzers 2026-10-17): Suche, Quelle, Sortierung, drei Filter
#     als Symbole mit Zahl (Toggle, Zahl im ItemStatus), Ansicht und Aktionen als Symbole, kein „Filter“-Menü; sie gibt
#     stufenlos nach und bricht erst in der schmalen Liste an der Gruppengrenze in zwei Zeilen um, bündig mit der Liste,
#     8 px Abstand. Geprüft bei 1600×1000 (eine Zeile) und im schmalen Fenster (1000×700, zwei Zeilen); Titelbereich, Griff
#     und Regal rechnen mit ihrer tatsächlichen Höhe
#   - Lasche ToolbarToggle (ExpandCollapse): Leiste aus- und einblenden, Zustand gemerkt (libraryToolbarCollapsed, auch nach
#     Neustart); eingeklappt ist die Leiste nicht mehr im Automationsbaum, die Kapsel sitzt mittig auf der Bildkante 10 px
#     über der Liste (der Titelbereich springt nicht) bzw. im Regal in der früheren Leistenzeile unter der Automatik-Karte,
#     nennt die Filter („Gefiltert: …“) und setzt sie zurück; ausgeklappt gibt es kein unsichtbares „zurücksetzen“
#   - Strg+F bei eingeklappter Leiste über die Testinstanz (--test-toolbar-search, keine Tastatureingabe): dauerhaft
#     ausgeklappt, gemerkt, Fokus auf der Suche (Prüfung 2026-10-19)
#   - Pixelraster und Schwellen (Prüfung 2026-10-19): Symbolknöpfe und Abstände genau round(40 bzw. 8 · Skalierung)
#     Bildschirmpixel, Zahl der Zeilen nach der Listenbreite (806/410) bei jeder Skalierung, auch bei 1366×768
#   - Raster: gleich große Kacheln über die ganze Breite, keine leere Spalte rechts, nichts ragt hinaus, bei jeder Größe
#   - Regal: Kachelreihe mit Blättern, Karte „Automatik“, Details als Schublade (öffnen und schließen)
#   - Detailbereich mit allen bisherigen Funktionen und „BILDER“; Kontextmenü des Spiels; „Bild wählen…“ mit eigener Datei
#     (HDRPILOT_TEST_ART_FILE, nur mit --config-dir; übernommen wird nicht, das Speichern eigener Bilder prüft G10.3)
# Schaltet kein HDR, startet kein Spiel. Die Bildschirmfotos enthalten Spielbilder und landen deshalb nie im Projektordner,
# sondern in -ShotsDir (Standard: Temp-Ordner).
param([string]$ShotsDir = (Join-Path ([IO.Path]::GetTempPath()) 'hdrpilot-phase10-ui'))
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
Add-Type -AssemblyName System.Drawing, PresentationCore
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }
function Rect($el) { $el.Current.BoundingRectangle }
# Skalierung des Bildschirms aus der DPI des Testfensters (nur lesend)
Add-Type -Namespace HdrPilotGate -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);'

# Elemente in eigenen Fenstern derselben Prozess-ID (Kontextmenü, Dialog „Bild wählen…“)
function Find-Top([int]$processId, [string]$automationId, [int]$timeoutMs = 5000) {
    $byPid = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $byId = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        foreach ($top in $UiaRoot.FindAll([Windows.Automation.TreeScope]::Children, $byPid)) {
            if ($top.Current.AutomationId -eq $automationId) { return $top }
            $e = $top.FindFirst([Windows.Automation.TreeScope]::Descendants, $byId)
            if ($e) { return $e }
        }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

function Wait-Until([scriptblock]$condition, [int]$timeoutMs = 8000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return [bool](& $condition)
}

function Is-Selected($el) { $p = $null; return $el.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p) -and $p.Current.IsSelected }

# Sichtbare, angeordnete Einträge einer Liste (virtualisierte haben kein Rechteck)
function Shown-Items($list) {
    $r = Rect $list
    return @(Get-ListItems $list | Where-Object { $b = Rect $_; -not $b.IsEmpty -and $b.Width -gt 0 -and $b.Bottom -gt $r.Top -and $b.Top -lt $r.Bottom -and -not $_.Current.IsOffscreen })
}

function Resize-Window($win, [int]$w, [int]$h) {
    $tp = $null
    if ($win.TryGetCurrentPattern([Windows.Automation.TransformPattern]::Pattern, [ref]$tp) -and $tp.Current.CanResize) {
        $tp.Resize($w, $h); Start-Sleep -Milliseconds 1200; return $true
    }
    return $false
}

# Raster prüfen: gleich große Kacheln, nichts ragt hinaus, rechts höchstens Rundungsrest (keine Platz für eine weitere Spalte)
function Check-Grid($win, [string]$label) {
    $grid = Find-Id $win 'GameGrid' 3000
    if (-not $grid) { Check $false "$label`: Raster vorhanden"; return }
    $g = Rect $grid
    $tiles = Shown-Items $grid
    $widths = @($tiles | ForEach-Object { [math]::Round((Rect $_).Width) } | Sort-Object -Unique)
    $heights = @($tiles | ForEach-Object { [math]::Round((Rect $_).Height) } | Sort-Object -Unique)
    $firstRow = @($tiles | Where-Object { [math]::Abs((Rect $_).Top - (Rect $tiles[0]).Top) -lt 2 })
    $cols = $firstRow.Count
    $tileW = if ($tiles.Count) { (Rect $tiles[0]).Width } else { 0 }
    $right = ($firstRow | ForEach-Object { (Rect $_).Right } | Measure-Object -Maximum).Maximum
    $left = ($firstRow | ForEach-Object { (Rect $_).Left } | Measure-Object -Minimum).Minimum
    Note ("{0}: Raster {1:N0} px breit, {2} Kacheln sichtbar, {3} Spalten, Kachel {4} x {5}, links {6:N0}, rechts frei {7:N0} px" -f $label, $g.Width, $tiles.Count, $cols, ($widths -join '/'), ($heights -join '/'), ($left - $g.Left), ($g.Right - $right))
    Check ($tiles.Count -ge $cols -and $cols -ge 2) "$label`: Kacheln angeordnet ($cols Spalten)"
    Check ($widths.Count -le 2 -and ($widths | Measure-Object -Maximum).Maximum - ($widths | Measure-Object -Minimum).Minimum -le 1) "$label`: alle Kacheln gleich breit ($($widths -join '/'))"
    Check ($heights.Count -le 2 -and ($heights | Measure-Object -Maximum).Maximum - ($heights | Measure-Object -Minimum).Minimum -le 1) "$label`: alle Kacheln gleich hoch ($($heights -join '/'))"
    Check ($tileW -gt 0 -and [math]::Abs((Rect $tiles[0]).Height / $tileW - 1.5) -lt 0.03) "$label`: Seitenverhältnis 2:3"
    Check ($right -le $g.Right + 1 -and $left -ge $g.Left - 1) "$label`: keine Kachel ragt aus dem Raster"
    Check (($g.Right - $right) -lt $tileW) "$label`: rechts keine leere Spalte (frei $([math]::Round($g.Right - $right)) px < Kachel $([math]::Round($tileW)) px)"
}

# Werkzeugleiste prüfen (Wunsch des Nutzers 2026-10-17, „Fließleiste mit Lichtnaht“): alle zehn Elemente sichtbar und im
# Fenster, kein „Filter“-Menü; Filter, Neu einlesen, gesehen und hinzufügen als Symbole 40 px breit, die Ansicht 122 px,
# alles 40 px hoch; Suche, Quelle und Sortierung nie schmaler als 168/136/160 (die Sortierung als Symbol 60). Eine Zeile:
# links und rechts bündig mit der Leiste und diese mit der Liste, 8 px zwischen den Elementen, vor der Ansicht der freie
# Platz (mindestens 24, mit der Sortierung als Symbol 16 px). Zwei Zeilen (Umbruch an der Gruppengrenze): oben Suche, Quelle
# und Sortierung bündig gefüllt, unten links die Filter, rechts bündig die Aktionen; drei Zeilen nur in der schmalsten
# Liste (Filter, dann Aktionen). 8 px zwischen den Zeilen (Bildschirmpixel, je 2 px Spielraum für die Rundung). Die Zahl
# der Zeilen steht danach in $script:BarRows.
function Check-Toolbar($win, [string]$label, [double]$scale) {
    $script:BarRows = 0
    $bar = Find-Id $win 'LibraryToolbar' 3000
    if (-not $bar) { Check $false "$label`: Werkzeugleiste LibraryToolbar vorhanden"; return }
    $b = Rect $bar; $w = Rect $win
    $ids = @('SearchBox', 'SourceFilter', 'SortOrder', 'OnlySelectedToggle', 'OnlyNewToggle', 'OnlyHdrCapableToggle', 'ViewSegment', 'RescanButton', 'MarkAllSeenButton', 'AddGameButton')
    $items = @($ids | ForEach-Object { Find-Id $bar $_ 2000 })
    $missing = @(for ($i = 0; $i -lt $ids.Count; $i++) { if (-not $items[$i] -or $items[$i].Current.IsOffscreen -or (Rect $items[$i]).Width -le 0) { $ids[$i] } })
    Check ($missing.Count -eq 0) ("$label`: alle zehn Elemente in der Werkzeugleiste sichtbar" + $(if ($missing.Count) { " (fehlt: $($missing -join ', '))" } else { '' }))
    if ($missing.Count) { return }
    $boxes = @($items | ForEach-Object { Rect $_ })
    $outside = @(for ($i = 0; $i -lt $ids.Count; $i++) { $r = $boxes[$i]; if ($r.Left -lt $w.Left - 1 -or $r.Right -gt $w.Right + 1 -or $r.Bottom -gt $w.Bottom + 1) { $ids[$i] } })
    Check ($outside.Count -eq 0) ("$label`: alles im Fenster" + $(if ($outside.Count) { " (ragt hinaus: $($outside -join ', '))" } else { '' }))

    # Größen: Symbole 40, Ansicht 122, alles 40 hoch; die Felder nie unter ihrem Minimum (unter 480 px Liste teilen sich
    # Suche und Quelle den Rest, dort nicht geprüft)
    $dip = @($boxes | ForEach-Object { [math]::Round($_.Width / $scale, 1) })
    $icons = @(3, 4, 5, 7, 8, 9 | Where-Object { [math]::Abs($dip[$_] - 40) -gt 1 } | ForEach-Object { $ids[$_] })
    Check ($icons.Count -eq 0) ("$label`: Filter und Aktionen als Symbole 40 px ({0})" -f ((3, 4, 5, 7, 8, 9 | ForEach-Object { $dip[$_] }) -join '/'))
    Check ([math]::Abs($dip[6] - 122) -le 1) ("$label`: Ansicht 122 px ({0})" -f $dip[6])
    $tall = @(for ($i = 0; $i -lt $ids.Count; $i++) { if ([math]::Abs($boxes[$i].Height / $scale - 40) -gt 1) { $ids[$i] } })
    Check ($tall.Count -eq 0) ("$label`: alle Elemente 40 px hoch" + $(if ($tall.Count) { " (abweichend: $($tall -join ', '))" } else { '' }))
    $sortIcon = [math]::Abs($dip[2] - 60) -le 1
    if ($b.Width / $scale -ge 480) {
        Check ($dip[0] -ge 167 -and $dip[1] -ge 135 -and ($sortIcon -or $dip[2] -ge 159)) ("$label`: Suche {0}, Quelle {1}, Sortierung {2} px (mindestens 168/136/160, Sortierung als Symbol 60)" -f $dip[0], $dip[1], $dip[2])
    }
    Check ($null -eq (Find-Id $win 'FilterMenuButton' 300)) ($label + ': kein „Filter“-Menü')

    # Zeilen: nach der Oberkante gruppiert, in der Reihenfolge der Oberfläche
    $rows = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt $ids.Count; $i++) {
        if ($rows.Count -eq 0 -or [math]::Abs($boxes[$i].Top - $boxes[$rows[$rows.Count - 1][0]].Top) -gt 3) { [void]$rows.Add((New-Object System.Collections.ArrayList)) }
        [void]$rows[$rows.Count - 1].Add($i)
    }
    $gap = 8 * $scale
    $flush = $true; $gaps = $true; $order = $true
    # Erwartete Zeilen: eine (alle zehn), zwei (Felder | Filter und Aktionen) oder drei (Felder | Filter | Aktionen)
    $shape = (($rows | ForEach-Object { $_.Count }) -join '+')
    if ($shape -notin '10', '3+7', '3+3+4') { $order = $false }
    foreach ($row in $rows) {
        $first = $ids[$row[0]]; $last = $ids[$row[$row.Count - 1]]
        # Links bündig beginnt jede Zeile außer der dritten (nur Aktionen), rechts bündig endet jede außer der der Filter
        if ($first -ne 'ViewSegment' -and [math]::Abs($boxes[$row[0]].Left - $b.Left) -gt 2) { $flush = $false }
        if ($last -ne 'OnlyHdrCapableToggle' -and [math]::Abs($boxes[$row[$row.Count - 1]].Right - $b.Right) -gt 2) { $flush = $false }
        for ($k = 1; $k -lt $row.Count; $k++) {
            $d = $boxes[$row[$k]].Left - $boxes[$row[$k - 1]].Right
            if ($d -lt 0) { $order = $false }
            # vor der Ansicht liegt der freie Platz (eine Zeile: Gruppenabstand)
            if ($ids[$row[$k]] -eq 'ViewSegment') {
                $least = if ($rows.Count -eq 1) { $(if ($sortIcon) { 16 } else { 24 }) * $scale } else { $gap }
                if ($d -lt $least - 2) { $gaps = $false }
            } elseif ([math]::Abs($d - $gap) -gt 2) { $gaps = $false }
        }
    }
    for ($r = 1; $r -lt $rows.Count; $r++) {
        $d = $boxes[$rows[$r][0]].Top - $boxes[$rows[$r - 1][0]].Bottom
        if ([math]::Abs($d - $gap) -gt 2) { $gaps = $false }
    }
    # Pixelraster (Prüfung 2026-10-19, auch bei 110 %): alle Symbolknöpfe genau round(40 · Skalierung) Bildschirmpixel breit
    # (vorher 44 und 45 px), alle Abstände genau round(8 · Skalierung) (je höchstens ein halber Pixel Messrundung)
    $buttonPx = [math]::Round(40 * $scale, [MidpointRounding]::AwayFromZero)
    $gapPx = [math]::Round(8 * $scale, [MidpointRounding]::AwayFromZero)
    $widths = @(3, 4, 5, 7, 8, 9 | ForEach-Object { $boxes[$_].Width })
    $exact = @($widths | Where-Object { [math]::Abs($_ - $buttonPx) -gt 0.5 }).Count -eq 0
    foreach ($row in $rows) {
        for ($k = 1; $k -lt $row.Count; $k++) {
            if ($ids[$row[$k]] -ne 'ViewSegment' -and [math]::Abs(($boxes[$row[$k]].Left - $boxes[$row[$k - 1]].Right) - $gapPx) -gt 0.5) { $exact = $false }
        }
    }
    Check $exact ("$label`: Pixelraster: Symbolknöpfe {0} px ({1}), Abstände {2} px" -f $buttonPx, (($widths | ForEach-Object { [math]::Round($_, 2) }) -join '/'), $gapPx)
    # Zeilen nach den Schwellen der Vorschau, geräteunabhängig und bei jeder Skalierung dieselben (Prüfung 2026-10-19: ein
    # Fenster 1366×768 bei 110 % war zweizeilig): eine Zeile ab einer Listenbreite von 806, darunter zwei, unter 410 drei
    # (ohne Ziehen, also ohne Hysterese; direkt an einer Schwelle nicht geprüft, dort entscheidet die Messrundung)
    $listDip = $b.Width / $scale
    $expectRows = if ($listDip -ge 806) { 1 } elseif ($listDip -ge 410) { 2 } else { 3 }
    if ([math]::Abs($listDip - 806) -gt 1 -and [math]::Abs($listDip - 410) -gt 1) {
        Check ($rows.Count -eq $expectRows) ("$label`: {0} Zeile(n) bei einer Listenbreite von {1:N1} px (Schwellen 806/410: {2})" -f $rows.Count, $listDip, $expectRows)
    }
    $list = Find-Id $win 'GameList' 1000
    $alignedWithList = $true
    if ($list -and -not $list.Current.IsOffscreen) {
        $l = Rect $list
        # Die Liste liegt im Rahmen ihrer Karte (1 px): Leiste und Karte haben dieselben Kanten
        $alignedWithList = [math]::Abs($l.Left - $b.Left) -le $scale + 2 -and [math]::Abs($l.Right - $b.Right) -le $scale + 2
    }
    Note ("{0}: Werkzeugleiste {1:N0} x {2:N0} px, {3} Zeile(n): {4}" -f $label, $b.Width, $b.Height, $rows.Count, (($rows | ForEach-Object { ($_ | ForEach-Object { $ids[$_] }) -join ' ' }) -join ' | '))
    Check ($flush -and $order) "$label`: Zeilen bündig mit der Leiste (Felder links und rechts, Filter links, Aktionen rechts), Reihenfolge wie in der Oberfläche ($shape)"
    Check $gaps "$label`: 8 px zwischen den Elementen und zwischen den Zeilen, vor der Ansicht der Gruppenabstand"
    Check $alignedWithList "$label`: Werkzeugleiste so breit wie die Liste"
    Check ([math]::Abs($b.Height / $scale - (($rows.Count * 48) - 8)) -le 1.5) ("$label`: Höhe {0:N1} px für {1} Zeile(n)" -f ($b.Height / $scale), $rows.Count)
    $script:BarRows = $rows.Count
}

$cfg = New-TestConfig 'phase10-ui' -Import
$shots = $null
try {
    # Echte Bibliothek, aber nichts schaltet HDR: alle Spiele auf „HDR automatisch“ und „Windows Auto HDR“ aus
    $config = Read-Config $cfg
    foreach ($game in @($config.games)) { $game.hdrAuto = $false; if ($game.PSObject.Properties['autoHdr']) { $game.autoHdr = $false } }
    [IO.File]::WriteAllText((Join-Path $cfg 'config.json'), ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $total = @($config.games).Count
    Note "Spiele in der Testablage: $total"

    # Testbild für „Eigene Datei…“ (erzeugt, kein Spielbild)
    $art = Join-Path $cfg 'testbild-titel.png'
    $bmp = New-Object Drawing.Bitmap 320, 120
    $gr = [Drawing.Graphics]::FromImage($bmp)
    $gr.Clear([Drawing.Color]::FromArgb(255, 40, 20, 90)); $gr.FillEllipse([Drawing.Brushes]::Orange, 200, 10, 90, 90)
    $gr.Dispose(); $bmp.Save($art, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $env:HDRPILOT_TEST_ART_FILE = $art

    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $win) 'Hauptfenster der Testinstanz geöffnet'
    if (-not $win) { throw 'Hauptfenster fehlt' }
    [void](Resize-Window $win 1600 1000)

    # --- Ansichten: Reihenfolge, Standard Liste ---
    $views = @('ViewListButton', 'ViewGridButton', 'ViewShelfButton' | ForEach-Object { Find-Id $win $_ 8000 })
    Check (@($views | Where-Object { $_ }).Count -eq 3) 'Schaltflächen Liste, Raster, Regal vorhanden'
    $x = @($views | ForEach-Object { (Rect $_).Left })
    Check ($x[0] -lt $x[1] -and $x[1] -lt $x[2]) ("Reihenfolge Liste, Raster, Regal ({0})" -f (($views | ForEach-Object { $_.Current.Name }) -join ', '))
    Check (Is-Selected $views[0]) 'Standard: Liste gewählt'
    Check ($null -eq (Read-Config $cfg).settings.libraryView) 'Standard wird nicht gespeichert (libraryView fehlt)'

    # --- Liste: virtualisiert, Spalten, Schalter je Zeile ---
    $list = Find-Id $win 'GameList' 5000
    $rows = Shown-Items $list
    $realized = @(Get-ListItems $list | Where-Object { -not (Rect $_).IsEmpty }).Count
    Note "Liste: $($rows.Count) Zeilen sichtbar, $realized erzeugt, $total Spiele"
    Check ($rows.Count -ge 5) 'Liste zeigt Zeilen'
    Check ($total -lt 60 -or $realized -lt $total / 2) "Liste virtualisiert ($realized von $total Zeilen erzeugt)"
    foreach ($id in 'ColumnSortName', 'ColumnSortLastPlayed', 'ColumnSortHdr', 'ThumbnailImageButton', 'ThumbnailIconButton', 'SortOrder', 'SearchBox', 'SourceFilter', 'NitsDisplayName', 'NitsPeak', 'NitsFullFrame', 'HdrStatus', 'ToggleHdrButton', 'PauseToggle', 'SettingsButton', 'RescanButton', 'AddGameButton', 'MarkAllSeenButton', 'AutomationState', 'ViewSummary', 'EffectsState') {
        Check ($null -ne (Find-Id $win $id 3000)) "Element $id vorhanden"
    }

    # --- Bildschirm-Kapsel (G10.7, Anmerkung 3): Skala von 0 bis zur Spitze bei eingeschaltetem HDR, nie fest 1000 ---
    # Die Kapsel selbst ist ein Grid ohne Automatisierungs-Peer; ihre Teile prüft die Liste oben. Die Werte liest die App
    # im Hintergrund, sobald das Fenster offen ist; ohne aktiven Bildschirm bleibt es bei „—“.
    [void](Wait-Until { $p = Find-Id $win 'NitsPeak' 500; $p -and $p.Current.Name -match '\d' } 4000)
    $peakEl = Find-Id $win 'NitsPeak' 3000
    $peakText = if ($peakEl) { [string]$peakEl.Current.Name } else { '' }
    # Der Name für Bildschirmleser nennt die Größe mit („Spitze in Nits: 456“), die Zahl steht dahinter
    Check ($peakText -match '^Spitze in Nits: ') ('Kapsel nennt für Bildschirmleser, was die Zahl ist („{0}“)' -f $peakText)
    $peakNumber = [regex]::Match($peakText, '\d+(?:[.,]\d+)?').Value
    $peakValue = 0.0
    if ([double]::TryParse(($peakNumber -replace ',', '.'), [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$peakValue) -and $peakValue -gt 0) {
        $scale = Find-Id $win 'NitsScale' 3000
        $range = $null
        if ($scale -and $scale.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$range)) {
            $max = $range.Current.Maximum
            Check ([Math]::Abs($max - $peakValue) -lt 0.5 -and $max -ne 1000) ("Nit-Skala endet an der Spitze: Maximum {0} = Spitze {1}" -f $max, $peakText)
        } else { Check $false 'Nit-Skala NitsScale mit Wertebereich vorhanden' }
    } else {
        Check ($null -eq (Find-Id $win 'NitsScale' 500)) ('Ohne Bildschirmwerte keine Skala (Spitze „{0}“)' -f $peakText)
        Note 'UMGEBUNG: keine Bildschirmwerte (Bildschirm aus oder noch nicht gelesen), Vergleich Skala/Spitze übersprungen'
    }

    Invoke-El (Find-Id $win 'ColumnSortLastPlayed')
    Check (Wait-Until { (Read-Config $cfg).settings.librarySortOrder -match 'LastPlayed' }) 'Spalte „Zuletzt gespielt“ sortiert und wird gespeichert'
    Invoke-El (Find-Id $win 'ColumnSortName')
    # Name ist der Standard und wird nicht gespeichert (librarySortOrder fehlt danach wieder)
    $sorted = Wait-Until { $null -eq (Read-Config $cfg).settings.librarySortOrder }
    Start-Sleep -Milliseconds 600
    $names = @(Shown-Items (Find-Id $win 'GameList') | Select-Object -First 6 | ForEach-Object { $_.Current.Name })
    $inOrder = $true
    # Die App sortiert mit ICU (Satzzeichen vor Ziffern vor Buchstaben); PowerShell 5.1 hat nur NLS: ordinal vergleichen
    for ($i = 1; $i -lt $names.Count; $i++) { if ([string]::Compare($names[$i - 1], $names[$i], [StringComparison]::OrdinalIgnoreCase) -gt 0) { $inOrder = $false } }
    Check ($sorted -and $inOrder) ('Spalte „Spiel“ sortiert nach Name (Standard, librarySortOrder entfällt): ' + ($names -join ' < '))
    Invoke-El (Find-Id $win 'ColumnSortLastPlayed')
    Check (Wait-Until { (Read-Config $cfg).settings.librarySortOrder -match 'LastPlayed' }) 'Wieder „Zuletzt gespielt“'

    $row = (Shown-Items (Find-Id $win 'GameList'))[0]
    Select-El $row
    $rowToggle = Find-Id $row 'HdrAutoToggle' 2000
    Check ($null -ne $rowToggle) 'Schalter „HDR automatisch“ in der Zeile'
    if ($rowToggle) {
        [void](Toggle-El $rowToggle)
        $on = Wait-Until { @((Read-Config $cfg).games | Where-Object { $_.hdrAuto }).Count -eq 1 }
        Check $on 'Zeilenschalter schreibt „HDR automatisch“'
        [void](Toggle-El $rowToggle)
        Check (Wait-Until { @((Read-Config $cfg).games | Where-Object { $_.hdrAuto }).Count -eq 0 }) 'Zeilenschalter wieder aus (kein Spiel mit HDR automatisch)'
    }

    Select-El (Find-Id $win 'ThumbnailIconButton')
    Check (Wait-Until { (Read-Config $cfg).settings.libraryThumbnail -eq 'Icon' }) 'Vorschau „Symbol“ gespeichert'
    Select-El (Find-Id $win 'ThumbnailImageButton')
    Check (Wait-Until { $null -eq (Read-Config $cfg).settings.libraryThumbnail }) 'Vorschau „Bild“ (Standard) gespeichert'

    # --- Liste: Höhe des Titelbereichs per Ziehgriff, nur über UI-Automation (RangeValue, keine Maus- oder Tastatureingabe) ---
    # Werte des Griffs in geräteunabhängigen Pixeln. Die Skalierung kommt aus der DPI des Fensters (aus den 20 px des Griffs
    # wäre sie wegen der Rundung der Bildschirmkoordinaten bis 5 % ungenau, nur ersatzweise); die Höhe des Fensterinhalts
    # ist sein größter Wert + 328 + Höhe der Werkzeugleiste (Luft, Kopf, fünf Zeilen, Statuszeile darunter; die Leiste in
    # ihrer tatsächlichen Höhe, eine bis drei Zeilen) und muss zur gemessenen Fensterhöhe passen (Rahmen und Ränder höchstens
    # 12 px). Lagen werden nur relativ verglichen, so zählen Rahmen und Rundung der Bildschirmkoordinaten nicht.
    $grip = Find-Id $win 'HeroSplitter' 3000
    Check ($null -ne $grip) 'Ziehgriff HeroSplitter in der Liste vorhanden'
    $scale = 1.0; $rootDip = 0.0; $heroSet = 0.0; $barOffset = 0.0
    $gripRange = $null
    if ($grip -and $grip.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$gripRange)) {
        $dpi = [HdrPilotGate.Dpi]::GetDpiForWindow([IntPtr]$win.Current.NativeWindowHandle)
        $scale = if ($dpi -gt 0) { $dpi / 96.0 } else { (Rect $grip).Height / 20 }
        $value = $gripRange.Current.Value
        $barDip = [math]::Round((Rect (Find-Id $win 'LibraryToolbar' 3000)).Height / $scale)
        $rootDip = $gripRange.Current.Maximum + 328 + $barDip
        $chrome = (Rect $win).Height / $scale - $rootDip
        Note ("Ziehgriff: Wert {0}, Bereich {1}–{2}, Werkzeugleiste {3} px hoch, Fensterinhalt {4} px hoch (Fenster {5:N1}), Skalierung {6:N2}" -f $value, $gripRange.Current.Minimum, $gripRange.Current.Maximum, $barDip, $rootDip, ((Rect $win).Height / $scale), $scale)
        Check ($grip.Current.Name -eq 'Höhe des Titelbereichs' -and $grip.Current.IsKeyboardFocusable) ('Ziehgriff benannt und mit Tastatur erreichbar („{0}“)' -f $grip.Current.Name)
        Check ($gripRange.Current.Minimum -eq 186 -and $chrome -ge 0 -and $chrome -le 12) ("Bereich: mindestens 186 px (Symbol-Kachel ganz), höchstens Fensterinhalt − 328 − Werkzeugleiste ({0} px; Rahmen {1:N1} px)" -f $barDip, $chrome)
        Check ([math]::Abs($value - [math]::Round($rootDip / 3)) -le 1) ('Standard: ein Drittel der Fensterhöhe ({0} von {1} px)' -f $value, $rootDip)
        Check ($null -eq (Read-Config $cfg).settings.libraryHeroRatio) 'Standardhöhe wird nicht gespeichert (libraryHeroRatio fehlt)'
        $gripTop = (Rect $grip).Top
        $listTop = (Rect (Find-Id $win 'GameList')).Top
        $heroSet = [math]::Round($gripRange.Current.Minimum + ($gripRange.Current.Maximum - $gripRange.Current.Minimum) * 0.6)
        $gripRange.SetValue($heroSet)
        # Griff und Liste rücken um genau die Änderung nach unten: Der Titelbereich ist so viel höher.
        $moved = { (((Rect $grip).Top - $gripTop) / $scale) - ($heroSet - $value) }
        $listMoved = { (((Rect (Find-Id $win 'GameList')).Top - $listTop) / $scale) - ($heroSet - $value) }
        Check (Wait-Until { [math]::Abs($gripRange.Current.Value - $heroSet) -lt 0.5 -and [math]::Abs((& $moved)) -le 1.5 -and [math]::Abs((& $listMoved)) -le 1.5 } 4000) ("Per UI-Automation von {0} auf {1} px gesetzt, Griff und Liste folgen (Abweichung {2:N1} / {3:N1} px)" -f $value, $heroSet, (& $moved), (& $listMoved))
        Check (Wait-Until { $r = (Read-Config $cfg).settings.libraryHeroRatio; $null -ne $r -and [math]::Abs([double]$r - $heroSet / $rootDip) -lt 0.0005 }) ("In der Konfiguration gemerkt (libraryHeroRatio {0}, erwartet {1:N4})" -f (Read-Config $cfg).settings.libraryHeroRatio, ($heroSet / $rootDip))
        Check ((Shown-Items (Find-Id $win 'GameList')).Count -ge 5) ("Darunter weiter Zeilen der Liste ({0})" -f (Shown-Items (Find-Id $win 'GameList')).Count)
        # Lage der Werkzeugleiste zum Fenster (in der Liste 10 px unter dem Titelbild): misst unten Raster und Regal
        $barOffset = (((Rect (Find-Id $win 'LibraryToolbar')).Top - (Rect $win).Top) / $scale) - ($heroSet + 10)
    } else { Check $false 'Ziehgriff mit Wertebereich (RangeValue)' }

    # --- Werkzeugleiste: eine Zeile im Startfenster, im schmalen Fenster zwei (Wunsch des Nutzers 2026-10-17) ---
    Check-Toolbar $win 'Fenster 1600x1000' $scale
    $wideRows = $BarRows
    Check ($wideRows -eq 1) ("Fenster 1600x1000: eine Zeile ({0})" -f $wideRows)
    if (Resize-Window $win 1000 700) {
        Check-Toolbar $win 'Schmales Fenster' $scale
        $narrowRows = $BarRows
        Check ($narrowRows -gt $wideRows -and $narrowRows -ge 2) ("Schmales Fenster: mehr Zeilen statt gefalteter Elemente ({0} statt {1})" -f $narrowRows, $wideRows)
        $grip1 = Find-Id $win 'HeroSplitter' 2000
        $range1 = $null
        if ($grip1 -and $grip1.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$range1)) {
            # Größter Titelbereich im schmalen Fenster nach der tatsächlichen Höhe der Leiste: Fensterinhalt − 328 − Leiste
            $bar1 = [math]::Round((Rect (Find-Id $win 'LibraryToolbar')).Height / $scale)
            $content1 = (Rect $win).Height / $scale - $range1.Current.Maximum - 328 - $bar1
            Check ($range1.Current.Maximum -eq 186 -or ($content1 -ge 0 -and $content1 -le 12)) ("Schmales Fenster: Bereich des Griffs bis {0} px bei {1} px hoher Werkzeugleiste (Rahmen {2:N1} px)" -f $range1.Current.Maximum, $bar1, $content1)
        }
    } else { Check $false 'Fenster lässt sich per UI-Automation in der Größe ändern' }
    # 1366×768 Bildschirmpixel (bei 110 % 1237×696, Liste etwa 807): Zeilen nach der Schwelle wie bei 100 %
    if (Resize-Window $win 1366 768) { Check-Toolbar $win 'Fenster 1366x768' $scale }
    [void](Resize-Window $win 1600 1000)
    Check-Toolbar $win 'Wieder 1600x1000' $scale
    Check ($BarRows -eq $wideRows) ('Wieder breit: dieselbe Zahl Zeilen wie vorher ({0})' -f $BarRows)

    # --- Werkzeugleiste aus- und einblenden über die Lasche (ExpandCollapse, keine Eingabe), Filter als Symbole ---
    $toggle = Find-Id $win 'ToolbarToggle' 3000
    $expand = $null
    Check ($toggle -and $toggle.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expand)) 'Lasche ToolbarToggle mit Aus- und Einklappen (ExpandCollapse)'
    if ($toggle -and $expand) {
        Check ($toggle.Current.Name -eq 'Werkzeugleiste' -and $expand.Current.ExpandCollapseState -eq 'Expanded' -and $toggle.Current.ItemStatus -eq 'Keine Filter') ('Lasche: Name „{0}“, {1}, Status „{2}“' -f $toggle.Current.Name, $expand.Current.ExpandCollapseState, $toggle.Current.ItemStatus)
        Check ($null -eq (Read-Config $cfg).settings.libraryToolbarCollapsed -or -not (Read-Config $cfg).settings.libraryToolbarCollapsed) 'Standard: ausgeklappt'

        # Filter „Neu“ als Symbol-Schalter: Zahl im ItemStatus, an und die Lasche nennt ihn
        $newFilter = Find-Id $win 'OnlyNewToggle' 2000
        $newStatus = if ($newFilter) { [string]$newFilter.Current.ItemStatus } else { '' }
        Check ($newStatus -match '\d') ('Filter „Neu“ nennt seine Zahl („{0}“)' -f $newStatus)
        $state = if ($newFilter) { Toggle-El $newFilter } else { $null }
        Check ($state -eq 'On' -and (Wait-Until { (Find-Id $win 'ToolbarToggle' 500).Current.ItemStatus -match '^Gefiltert: ' } 3000)) ('Filter „Neu“ an, die Lasche nennt ihn („{0}“)' -f (Find-Id $win 'ToolbarToggle' 500).Current.ItemStatus)
        Check ($null -eq (Find-Id $win 'ToolbarResetButton' 600)) 'Ausgeklappt und gefiltert: kein unsichtbares „Suche und Filter zurücksetzen“ im Automationsbaum'

        $barBefore = Rect (Find-Id $win 'LibraryToolbar' 2000)
        $listBefore = (Rect (Find-Id $win 'GameList' 2000)).Top
        $gripBefore = (Rect (Find-Id $win 'HeroSplitter' 2000)).Top
        $expand.Collapse()
        Check (Wait-Until { $expand.Current.ExpandCollapseState -eq 'Collapsed' }) ('Eingeklappt (Lasche meldet {0})' -f $expand.Current.ExpandCollapseState)
        Check (Wait-Until { (Read-Config $cfg).settings.libraryToolbarCollapsed -eq $true }) ('Gemerkt (libraryToolbarCollapsed {0})' -f (Read-Config $cfg).settings.libraryToolbarCollapsed)
        Start-Sleep -Milliseconds 1200
        Check ($null -eq (Find-Id $win 'LibraryToolbar' 600) -and $null -eq (Find-Id $win 'SearchBox' 300) -and $null -eq (Find-Id $win 'OnlyNewToggle' 300)) 'Eingeklappt: Leiste und ihre Elemente nicht mehr im Automationsbaum'
        $cap = Rect (Find-Id $win 'ToolbarToggle' 2000)
        $edge = $barBefore.Top - 10 * $scale
        $rise = ($listBefore - (Rect (Find-Id $win 'GameList' 2000)).Top) / $scale
        $grip = (Rect (Find-Id $win 'HeroSplitter' 2000)).Top
        Note ("Eingeklappt: Kapsel {0:N0} x {1:N0} px, Mitte {2:N1} px zur Bildkante, Liste {3:N1} px höher, Griff {4:N1} px verschoben" -f $cap.Width, $cap.Height, ((($cap.Top + $cap.Bottom) / 2 - $edge) / $scale), $rise, (($grip - $gripBefore) / $scale))
        Check ([math]::Abs($cap.Height / $scale - 24) -le 1 -and [math]::Abs(($cap.Top + $cap.Bottom) / 2 - $edge) -le 2 -and [math]::Abs($cap.Right - $barBefore.Right) -le 2) 'Kapsel 24 px hoch, mittig auf der Bildkante, rechtsbündig mit der Liste'
        Check ([math]::Abs($rise - ($barBefore.Height / $scale - 2)) -le 1.5) ("Liste rückt um die Leiste nach oben ({0:N1} px, erwartet {1:N0}), 10 px unter der Kapsel" -f $rise, ($barBefore.Height / $scale - 2))
        Check ([math]::Abs($grip - $gripBefore) -le 1.5) 'Titelbereich und Griff springen nicht'
        Check ((Find-Id $win 'ToolbarToggle' 500).Current.ItemStatus -match '^Gefiltert: ') 'Kapsel nennt die Filter'
        $reset = Find-Id $win 'ToolbarResetButton' 2000
        Check ($null -ne $reset) 'Kapsel mit „Filter zurücksetzen“'
        if ($reset) {
            Invoke-El $reset
            Check (Wait-Until { (Find-Id $win 'ToolbarToggle' 500).Current.ItemStatus -eq 'Keine Filter' } 3000) 'Zurücksetzen in der Kapsel: keine Filter mehr'
        }

        $expand.Expand()
        Check (Wait-Until { $expand.Current.ExpandCollapseState -eq 'Expanded' -and -not (Read-Config $cfg).settings.libraryToolbarCollapsed } 4000) 'Wieder ausgeklappt und gemerkt'
        Start-Sleep -Milliseconds 1000
        $newFilter = Find-Id $win 'OnlyNewToggle' 2000
        $off = $null
        Check ($newFilter -and $newFilter.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$off) -and $off.Current.ToggleState -eq 'Off') 'Ausgeklappt: Leiste wieder da, Filter „Neu“ aus'
        Check ([math]::Abs((Rect (Find-Id $win 'GameList' 2000)).Top - $listBefore) -le 1.5) 'Liste wieder an ihrem Platz'

        # Strg+F bei eingeklappter Leiste, ohne Tastatureingabe über die Testinstanz (--test-toolbar-search ruft dasselbe wie die
        # Taste): dauerhaft ausgeklappt und gemerkt, der Fokus auf der Suche. Ist das Fenster nicht aktiv, ist das der Fokus,
        # den es beim Aktivieren wiederherstellt; niemandem wird er genommen.
        # (Die Ablage wird hier erst gelesen, wenn die App fertig geschrieben hat: Liest das Gate, während sie die Datei
        # ersetzt, scheitert ihr Speichern mit „Zugriff verweigert“.)
        $expand.Collapse()
        Check (Wait-Until { $expand.Current.ExpandCollapseState -eq 'Collapsed' }) 'Für Strg+F wieder eingeklappt'
        Start-Sleep -Milliseconds 1500
        Check ((Read-Config $cfg).settings.libraryToolbarCollapsed -eq $true) 'Für Strg+F: eingeklappt gemerkt'
        Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--test-toolbar-search') | Out-Null
        $searchPattern = 'Prüfung: Strg\+F ausgeführt \((\S+)\), Werkzeugleiste (\S+), Fokus (\S+)\.'
        $searchLog = Wait-Log $cfg $searchPattern 8000
        $searchHit = if ($searchLog) { [regex]::Matches($searchLog, $searchPattern) | Select-Object -Last 1 } else { $null }
        Check ($searchHit -and $searchHit.Groups[1].Value -eq 'erledigt' -and $searchHit.Groups[2].Value -eq 'ausgeklappt' -and $searchHit.Groups[3].Value -eq 'SearchBox') ('Strg+F: ausgeklappt, Fokus auf der Suche („{0}“)' -f $(if ($searchHit) { $searchHit.Value } else { 'nicht im Protokoll' }))
        $searchExpanded = Wait-Until { $expand.Current.ExpandCollapseState -eq 'Expanded' } 4000
        Start-Sleep -Milliseconds 1500
        $searchStored = (Read-Config $cfg).settings.libraryToolbarCollapsed
        Check ($searchExpanded -and -not $searchStored) ('Strg+F: dauerhaft ausgeklappt und gemerkt (libraryToolbarCollapsed {0})' -f $searchStored)
        Check ($null -ne (Find-Id $win 'SearchBox' 3000)) 'Strg+F: Suche wieder im Automationsbaum'
    }

    # --- Detailbereich: alle bisherigen Funktionen und „BILDER“ ---
    foreach ($id in 'DetailsPanel', 'DetailName', 'LaunchButton', 'CreateDesktopShortcutButton', 'CreateStartMenuShortcutButton', 'DetailHdrAuto', 'DetailRestore', 'DetailDisplay', 'DetailAutoHdr', 'AddExeButton', 'RemoveExeButton', 'RelearnExeButton', 'ChooseArtworkButton', 'ArtworkTileHero', 'ArtworkTileLogo', 'ArtworkTileCover', 'DetailIconButton', 'LibrarySplitter', 'ProfileMenuButton') {
        Check ($null -ne (Find-Id $win $id 3000)) "Detail-Element $id vorhanden"
    }

    # Kontextmenü des Spiels (über „···“ im Kopf des Detailbereichs, dasselbe Menü wie in Zeilen und Kacheln)
    Invoke-El (Find-Id $win 'ProfileMenuButton')
    $menuIds = 'ContextLaunch', 'ContextHdrAuto', 'ContextRestore', 'ContextAutoHdr', 'ChooseArtworkMenuItem', 'ChangeIconMenuItem', 'ResetIconMenuItem', 'ContextDesktopShortcut', 'ContextStartMenuShortcut', 'ContextOpenFolder'
    $missing = @($menuIds | Where-Object { -not (Find-Top $app.Id $_ 1500) })
    Check ($missing.Count -eq 0) ("Kontextmenü mit allen Einträgen" + $(if ($missing.Count) { " (fehlt: $($missing -join ', '))" } else { '' }))
    $hdrItem = Find-Top $app.Id 'ContextHdrAuto' 1500
    if ($hdrItem) {
        Invoke-El $hdrItem
        Check (Wait-Until { @((Read-Config $cfg).games | Where-Object { $_.hdrAuto }).Count -eq 1 }) 'Kontextmenü „HDR automatisch“ wirkt auf das gewählte Spiel'
        Invoke-El (Find-Id $win 'ProfileMenuButton')
        $hdrItem = Find-Top $app.Id 'ContextHdrAuto' 2000
        if ($hdrItem) { Invoke-El $hdrItem }
        Check (Wait-Until { @((Read-Config $cfg).games | Where-Object { $_.hdrAuto }).Count -eq 0 }) 'Kontextmenü „HDR automatisch“ wieder aus'
    }

    # „Bild wählen…“: Reiter, Kandidaten, eigene Datei aus der Testvariable, Abbrechen ändert nichts
    $artworkBefore = (Read-Config $cfg | ConvertTo-Json -Depth 20).Length
    Invoke-El (Find-Id $win 'ChooseArtworkButton')
    $dialog = Find-Top $app.Id 'ArtworkPickerDialog' 8000
    Check ($null -ne $dialog) 'Dialog „Bild wählen…“ geöffnet'
    if ($dialog) {
        foreach ($id in 'ArtworkTabHero', 'ArtworkTabLogo', 'ArtworkTabCover', 'ArtworkCandidates', 'ArtworkPickFileButton', 'ArtworkResetButton', 'ArtworkCancelButton', 'ArtworkApplyButton') {
            Check ($null -ne (Find-Id $dialog $id 3000)) "Dialog-Element $id vorhanden"
        }
        Check (Is-Selected (Find-Id $dialog 'ArtworkTabHero')) 'Reiter „Titelbild“ gewählt'
        $cands = Find-Id $dialog 'ArtworkCandidates'
        Check (@(Get-ListItems $cands).Count -ge 1) ("Kandidaten angeboten ({0}, mindestens 'Automatisch')" -f @(Get-ListItems $cands).Count)
        Invoke-El (Find-Id $dialog 'ArtworkPickFileButton')
        Check (Wait-Until { @(Get-ListItems (Find-Id $dialog 'ArtworkCandidates') | Where-Object { $_.Current.Name -match 'testbild-titel' }).Count -eq 1 }) 'Eigene Datei aus HDRPILOT_TEST_ART_FILE als Kandidat gewählt'
        Select-El (Find-Id $dialog 'ArtworkTabCover')
        Check (Is-Selected (Find-Id $dialog 'ArtworkTabCover')) 'Reiter „Cover“ wählbar'
        Invoke-El (Find-Id $dialog 'ArtworkCancelButton')
        Check (Wait-Until { -not (Find-Top $app.Id 'ArtworkPickerDialog' 300) }) 'Abbrechen schließt den Dialog'
        Check ((Read-Config $cfg | ConvertTo-Json -Depth 20).Length -eq $artworkBefore -and -not (@((Read-Config $cfg).games | Where-Object { $_.artwork }).Count)) 'Abbrechen schreibt nichts'
    }

    # --- Raster: gleichmäßig bei verschiedenen Größen ---
    Select-El $views[1]
    Check (Wait-Until { (Read-Config $cfg).settings.libraryView -eq 'Grid' }) 'Raster gewählt und gespeichert'
    Start-Sleep -Milliseconds 1200
    Check ((Find-Id $win 'GameList' 500) -eq $null -or (Find-Id $win 'GameList' 500).Current.IsOffscreen) 'Liste ausgeblendet'
    # Raster: ohne Ziehgriff, Titelbereich clamp(H·0,24; 196; 260) unabhängig von der Höhe in der Liste (weniger nur in
    # niedrigen Fenstern, bis die erste Kachelreihe ganz zu sehen ist), Werkzeugleiste 22 px unter dem Titelbild (so viel
    # Luft wie darunter bis zur ersten Kachelreihe)
    Check ($null -eq (Find-Id $win 'HeroSplitter' 500)) 'Raster ohne Ziehgriff'
    if ($rootDip -gt 0) {
        $rasterHero = (((Rect (Find-Id $win 'LibraryToolbar')).Top - (Rect $win).Top) / $scale) - $barOffset - 22
        $rasterExpected = [math]::Round([math]::Min(260, [math]::Max(196, $rootDip * 0.24)))
        Check ([math]::Abs($rasterHero - $rasterExpected) -le 1.5) ("Raster: Titelbereich wie bisher ({0:N1} px, erwartet {1})" -f $rasterHero, $rasterExpected)
    }
    $gridTiles = @(Get-ListItems (Find-Id $win 'GameGrid') | Where-Object { -not (Rect $_).IsEmpty }).Count
    Check ($total -lt 120 -or $gridTiles -lt $total / 2) "Raster virtualisiert ($gridTiles von $total Kacheln erzeugt)"
    foreach ($size in @(@(1600, 1000), @(1280, 800), @(1920, 1080), @(1000, 700))) {
        if (Resize-Window $win $size[0] $size[1]) { Check-Grid $win ("Fenster {0}x{1}" -f $size[0], $size[1]) }
        else { Check $false 'Fenster lässt sich per UI-Automation in der Größe ändern' }
    }
    if (Resize-Window $win 3440 1440) {
        $w = Rect $win
        if ($w.Width -ge 3300) { Check-Grid $win ("Fenster {0:N0}x{1:N0}" -f $w.Width, $w.Height) }
        else { Note ("3440x1440 nicht möglich (Bildschirm erlaubt {0:N0}x{1:N0}); belegt durch die Bildschirmfotos unten" -f $w.Width, $w.Height) }
    }
    [void](Resize-Window $win 1600 1000)

    # --- Regal: Reihe, Blättern, Karte, Schublade ---
    Select-El $views[2]
    Check (Wait-Until { (Read-Config $cfg).settings.libraryView -eq 'Shelf' }) 'Regal gewählt und gespeichert'
    Start-Sleep -Milliseconds 1200
    # Regal wie bisher: ohne Ziehgriff, Werkzeugleiste 50 px über dem unteren Rand des großen Titelbereichs; umgebrochen
    # wächst sie nach oben (je weitere Zeile 48 px) und bleibt unter der Automatik-Karte, nie darauf
    Check ($null -eq (Find-Id $win 'HeroSplitter' 500)) 'Regal ohne Ziehgriff'
    Check-Toolbar $win 'Regal 1600x1000' $scale
    $shelfBar = Rect (Find-Id $win 'LibraryToolbar')
    $card = Find-Id $win 'AutomationCard' 2000
    if ($card) { $c = Rect $card; Check ($shelfBar.Top -ge $c.Bottom - 1 -or $shelfBar.Right -le $c.Left + 1) 'Regal: Werkzeugleiste unter oder neben der Automatik-Karte, nicht darauf' }
    if ($rootDip -gt 0) {
        $extra = [math]::Max(0, [math]::Round($shelfBar.Height / $scale) - 40)
        $shelfHero = ((($shelfBar.Top - (Rect $win).Top) / $scale) - $barOffset) + 50 + $extra
        $shelfExpected = [math]::Round([math]::Min([math]::Max([math]::Min([math]::Max($rootDip * 0.62, 500), 660), $rootDip - 462), [math]::Max(440 + $extra, $rootDip - 38 - 34 - 200)))
        Check ([math]::Abs($shelfHero - $shelfExpected) -le 1.5) ("Regal: Titelbereich wie bisher ({0:N1} px, erwartet {1}; Werkzeugleiste {2} px höher)" -f $shelfHero, $shelfExpected, $extra)
    }
    $shelf = Find-Id $win 'GameShelf' 3000
    Check ($null -ne $shelf -and (Shown-Items $shelf).Count -ge 3) "Regal zeigt eine Kachelreihe ($(if ($shelf) { (Shown-Items $shelf).Count }) Kacheln)"
    foreach ($id in 'CardHdrAuto', 'CardRestore', 'CardAutoHdr', 'DetailsDrawerButton', 'ShelfNextButton', 'ShelfPrevButton', 'ShelfPosition', 'HeroLaunchButton', 'HeroDesktopButton', 'HeroStartMenuButton') {
        Check ($null -ne (Find-Id $win $id 3000)) "Regal-Element $id vorhanden"
    }
    $pos0 = (Find-Id $win 'ShelfPosition').Current.Name
    Invoke-El (Find-Id $win 'ShelfNextButton')
    Start-Sleep -Milliseconds 1200
    $pos1 = (Find-Id $win 'ShelfPosition').Current.Name
    Check ($pos0 -ne $pos1 -and $pos1 -match '\d') ("Blättern im Regal: '{0}' -> '{1}'" -f $pos0, $pos1)
    Invoke-El (Find-Id $win 'ShelfPrevButton')
    Start-Sleep -Milliseconds 900
    Invoke-El (Find-Id $win 'DetailsDrawerButton')
    Start-Sleep -Milliseconds 900
    $drawer = Find-Id $win 'DetailsPanel' 2000
    Check ($drawer -and -not $drawer.Current.IsOffscreen -and (Rect $drawer).Right -le (Rect $win).Right + 1) 'Details als Schublade geöffnet und im Fenster'
    $close = Find-Id $win 'DrawerCloseButton' 2000
    if ($close) { Invoke-El $close; Start-Sleep -Milliseconds 900 }
    $drawer = Find-Id $win 'DetailsPanel' 800
    Check ($null -eq $drawer -or $drawer.Current.IsOffscreen -or (Rect $drawer).Left -ge (Rect $win).Right - 2) 'Schublade geschlossen'

    # Regal eingeklappt: die Kachelreihe bleibt, die Kapsel steht mittig in der früheren Leistenzeile, rechtsbündig, mindestens
    # 16 px unter der Automatik-Karte. Eingeklappt bleibt die Leiste für den Neustart.
    $toggle = Find-Id $win 'ToolbarToggle' 2000
    $expand = $null
    if ($toggle -and $toggle.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expand)) {
        $shelfTop = (Rect (Find-Id $win 'GameShelf' 2000)).Top
        $expand.Collapse()
        Check (Wait-Until { (Read-Config $cfg).settings.libraryToolbarCollapsed -eq $true } 4000) 'Regal: eingeklappt und gemerkt'
        Start-Sleep -Milliseconds 1200
        $cap = Rect (Find-Id $win 'ToolbarToggle' 2000)
        Note ("Regal eingeklappt: Kapsel {0:N1} px unter der Karte, Mitte {1:N1} px zur Mitte der früheren Leiste" -f $(if ($card) { ($cap.Top - (Rect $card).Bottom) / $scale } else { 0 }), ((($cap.Top + $cap.Bottom) - ($shelfBar.Top + $shelfBar.Bottom)) / 2 / $scale))
        Check ([math]::Abs(($cap.Top + $cap.Bottom) - ($shelfBar.Top + $shelfBar.Bottom)) / 2 -le 2 -and [math]::Abs($cap.Right - $shelfBar.Right) -le 2) 'Regal: Kapsel mittig in der früheren Leistenzeile, rechtsbündig'
        if ($card) { Check ($cap.Top - (Rect $card).Bottom -ge 16 * $scale - 2) 'Regal: Kapsel mindestens 16 px unter der Automatik-Karte' }
        Check ([math]::Abs((Rect (Find-Id $win 'GameShelf' 2000)).Top - $shelfTop) -le 1.5) 'Regal: die Kachelreihe bleibt, wo sie ist'
    } else { Check $false 'Regal: Lasche ToolbarToggle mit ExpandCollapse' }

    # --- Neustart: Ansicht und eingeklappte Leiste gemerkt ---
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'
    $app2 = Start-App $cfg @('--show')
    $win2 = Find-Window 'MainWindow' $app2.Id
    # Eingeklappt sind die Ansicht-Knöpfe nicht im Automationsbaum: erst die Leiste prüfen und ausklappen
    if ($win2) {
        $toggle2 = Find-Id $win2 'ToolbarToggle' 8000
        $expand2 = $null
        $collapsed2 = $toggle2 -and $toggle2.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expand2) -and $expand2.Current.ExpandCollapseState -eq 'Collapsed'
        Check ($collapsed2 -and $null -eq (Find-Id $win2 'LibraryToolbar' 600)) 'Nach Neustart wieder eingeklappt (ohne Bewegung, Leiste nicht im Automationsbaum)'
        if ($expand2) {
            $expand2.Expand()
            Check (Wait-Until { -not (Read-Config $cfg).settings.libraryToolbarCollapsed -and $null -ne (Find-Id $win2 'LibraryToolbar' 300) } 4000) 'Wieder ausgeklappt: Leiste da, libraryToolbarCollapsed aus'
        }
    }
    $shelfButton2 = if ($win2) { Find-Id $win2 'ViewShelfButton' 8000 } else { $null }
    Check ($shelfButton2 -and (Is-Selected $shelfButton2)) 'Nach Neustart wieder Regal'
    if ($win2) {
        Select-El (Find-Id $win2 'ViewListButton')
        Check (Wait-Until { $null -eq (Read-Config $cfg).settings.libraryView }) 'Zurück zur Liste: Standard, nichts gespeichert'

        # Gemerkte Höhe des Titelbereichs nach dem Neustart (gleiche Fenstergröße), dann Auslösen wie Doppelklick: Standard
        [void](Resize-Window $win2 1600 1000)
        $grip2 = Find-Id $win2 'HeroSplitter' 5000
        $range2 = $null
        if ($grip2 -and $heroSet -gt 0 -and $grip2.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$range2)) {
            Check (Wait-Until { [math]::Abs($range2.Current.Value - $heroSet) -le 1 } 4000) ("Nach Neustart wieder {0} px hoch (Griff {1})" -f $heroSet, $range2.Current.Value)
            $invoke2 = $null
            if ($grip2.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$invoke2)) {
                $invoke2.Invoke()
                $root2 = $range2.Current.Maximum + 328 + [math]::Round((Rect (Find-Id $win2 'LibraryToolbar' 3000)).Height / $scale)
                Check (Wait-Until { [math]::Abs($range2.Current.Value - $root2 / 3) -le 1.5 } 4000) ("Auslösen (wie Doppelklick) setzt auf ein Drittel zurück ({0} von {1:N0} px)" -f $range2.Current.Value, $root2)
                Check (Wait-Until { $null -eq (Read-Config $cfg).settings.libraryHeroRatio }) 'Zurückgesetzt: libraryHeroRatio entfällt in der Konfiguration'
            } else { Check $false 'Ziehgriff lässt sich auslösen (Invoke)' }
        } else { Check $false 'Ziehgriff nach Neustart vorhanden' }
    }
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app2.WaitForExit(10000)) 'Testinstanz beendet'
    Check (@((Read-Config $cfg).games | Where-Object { $_.hdrAuto -or $_.autoHdr }).Count -eq 0) 'Am Ende hat kein Spiel „HDR automatisch“ oder „Windows Auto HDR“ an'

    # --- Bildschirmfotos: drei Ansichten, die gescrollte Liste und die eingeklappte Leiste (gefiltert, mit Lichtnaht), drei
    #     Größen, hell und dunkel ---
    $shots = Join-Path $ShotsDir ('lauf-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $snap = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--snapshot', "`"$shots`"", '--snapshot-sizes', 'review', '--snapshot-scenes', 'hauptfenster-liste,hauptfenster-liste-gescrollt,hauptfenster-raster,hauptfenster-regal,hauptfenster-liste-eingeklappt-gefiltert,hauptfenster-raster-eingeklappt,hauptfenster-regal-eingeklappt-gefiltert', '--snapshot-themes', 'dunkel,hell') -PassThru -Wait
    Check ($snap.ExitCode -eq 0) "Bildschirmfotos gerendert (Exit-Code $($snap.ExitCode))"
    foreach ($view in 'liste', 'liste-gescrollt', 'raster', 'regal', 'liste-eingeklappt-gefiltert', 'raster-eingeklappt', 'regal-eingeklappt-gefiltert') {
        foreach ($size in '1280x800', '1920x1080', '3440x1440') {
            foreach ($theme in 'dunkel', 'hell') {
                $file = Join-Path $shots "hauptfenster-$view-$size-$theme.png"
                $dims = $null
                if (Test-Path $file) {
                    $stream = [IO.File]::OpenRead($file)
                    try { $frame = [System.Windows.Media.Imaging.BitmapDecoder]::Create($stream, 'None', 'OnLoad').Frames[0]; $dims = '{0}x{1}' -f $frame.PixelWidth, $frame.PixelHeight } finally { $stream.Dispose() }
                }
                Check ($dims -eq $size) "Bild $view $size $theme ($dims)"
            }
        }
    }
    Note "Bildschirmfotos: $shots"
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally {
    Remove-Item Env:\HDRPILOT_TEST_ART_FILE -ErrorAction SilentlyContinue
    Stop-Started
}
if ($ok) { Write-Output 'PHASE10_UI_OK'; exit 0 }
exit 1
