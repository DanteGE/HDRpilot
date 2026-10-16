# Gate G10.5: Bibliothek „Showcase Hero“ per UI-Automation an einer eigenen Testinstanz (eigene Ablage, echte Bibliothek
# eingelesen und alle Spiele auf „HDR automatisch“ aus, nur Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben)
# und Bildschirmfotos der drei Ansichten bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel.
#   - Ansichten in der Reihenfolge Liste, Raster, Regal; Liste ist der Standard, die Wahl wird gemerkt (auch nach Neustart)
#   - Liste: virtualisiert, nach Spalten sortierbar (gespeichert), Schalter „HDR automatisch“ je Zeile, Vorschau Bild/Symbol
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

    # --- Neustart: Ansicht gemerkt ---
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'
    $app2 = Start-App $cfg @('--show')
    $win2 = Find-Window 'MainWindow' $app2.Id
    Check ($win2 -and (Is-Selected (Find-Id $win2 'ViewShelfButton' 8000))) 'Nach Neustart wieder Regal'
    if ($win2) {
        Select-El (Find-Id $win2 'ViewListButton')
        Check (Wait-Until { $null -eq (Read-Config $cfg).settings.libraryView }) 'Zurück zur Liste: Standard, nichts gespeichert'
    }
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app2.WaitForExit(10000)) 'Testinstanz beendet'
    Check (@((Read-Config $cfg).games | Where-Object { $_.hdrAuto -or $_.autoHdr }).Count -eq 0) 'Am Ende hat kein Spiel „HDR automatisch“ oder „Windows Auto HDR“ an'

    # --- Bildschirmfotos: drei Ansichten, drei Größen, hell und dunkel ---
    $shots = Join-Path $ShotsDir ('lauf-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $snap = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--snapshot', "`"$shots`"", '--snapshot-sizes', 'review', '--snapshot-scenes', 'hauptfenster-liste,hauptfenster-raster,hauptfenster-regal', '--snapshot-themes', 'dunkel,hell') -PassThru -Wait
    Check ($snap.ExitCode -eq 0) "Bildschirmfotos gerendert (Exit-Code $($snap.ExitCode))"
    foreach ($view in 'liste', 'raster', 'regal') {
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
