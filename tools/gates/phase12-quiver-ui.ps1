# Gate G12.5: Quiver Launcher in der Bibliothek (E91) per UI-Automation an einer eigenen Testinstanz (eigene Ablage, nur
# Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben) und Bildschirmfotos.
# Testablage: ein nachgebauter Quiver-Ordner (Velopack-Aufbau mit current\sq.version, settings.json, apps.json) im Ordner der
# Ablage, als gelernter Ordner (settings.quiverFolders) eingetragen. Sein einziges Spiel ist ein harmloses Prüfprogramm
# (kein Spiel: schreibt beim Start Arbeitsordner, eigenen Pfad und Zahl der Argumente in eine Datei neben sich und wartet
# 5 s), sein Cover ein erzeugtes PNG im Bildzwischenspeicher von Quiver (Cache\Images\<MD5 der Kartenadresse>, ohne Endung).
# Beim Start liest die Testinstanz auch die echten Launcher nur lesend ein (dabei auch den echten Quiver-Ordner, falls es ihn gibt).
#   - Das Einlesen übernimmt das Spiel als Quiver:HDRpilot-Quiver-Pruefspiel: Start-EXE das Prüfprogramm, Arbeitsordner sein
#     Ordner, Cover-Hinweis die Datei im Zwischenspeicher
#   - Quelle „Quiver“ im Quellenfilter (Liste zeigt danach nur Quiver-Spiele) und in der eingeklappten Kapsel (ItemStatus)
#   - Das Cover kommt aus dem Zwischenspeicher (art\index.json: Herkunft Quiver, genau diese Datei)
#   - „Starten“ startet das Prüfprogramm direkt (Arbeitsordner = sein Ordner, ohne Argumente), die Sitzung erkennt seinen
#     Prozess, merkt die EXE und endet nach der Nachlaufzeit
#   - Bildschirmfotos (Liste, Raster, Regal, Regal mit Schublade, gefiltert, Kapsel) bei 1280×800, 1920×1080 und 3440×1440,
#     hell und dunkel, mit einer Ablage aus dem Prüfspiel und einigen weiteren Spielen
# HDR wird nie geschaltet: Die globale Einstellung zeigt auf einen Bildschirm, den es nicht gibt (das Protokoll muss
# „nicht angeschlossen“ und keine einzige „HDR-Schaltung“ enthalten). Hinweise und Tastenkürzel-Notlösung sind aus. Startet
# weder Quiver noch ein Spiel. Die Bildschirmfotos enthalten Spielbilder und landen nie im Projektordner, sondern in -ShotsDir.
#   PHASE12_QUIVER_UI_OK
param([string]$ShotsDir = (Join-Path ([IO.Path]::GetTempPath()) 'hdrpilot-phase12-quiver-ui'))
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

function Wait-Until([scriptblock]$condition, [int]$timeoutMs = 8000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return [bool](& $condition)
}

function Read-Shared([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $fs = New-Object IO.FileStream($path, 'Open', 'Read', 'ReadWrite, Delete')
    $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
    try { return $sr.ReadToEnd() } finally { $sr.Dispose() }
}

function Read-Log([string]$cfg) {
    (Get-ChildItem (Join-Path $cfg 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Read-Shared $_.FullName }) -join "`n"
}

function Detail-Name($win) { try { $d = Find-Id $win 'DetailName' 500; if ($d) { [string]$d.Current.Name } else { $null } } catch { $null } }
function Row-Names($win) { try { @(Get-ListItems (Find-Id $win 'GameList' 2000) | ForEach-Object { [string]$_.Current.Name }) } catch { @() } }

# Cover für das Prüfspiel: 600 × 900, Verlauf mit Schrift (erzeugt, kein Spielbild), als PNG ohne Endung wie bei Quiver
function Write-Cover([string]$path) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $brush = New-Object Windows.Media.LinearGradientBrush ([Windows.Media.Color]::FromRgb(0x2A, 0x0A, 0x3D)), ([Windows.Media.Color]::FromRgb(0xF4, 0x72, 0xB6)), 60.0
    $dc.DrawRectangle($brush, $null, (New-Object Windows.Rect 0, 0, 600, 900))
    $face = New-Object Windows.Media.Typeface 'Segoe UI'
    foreach ($line in @(@('HDRpilot', 300, 56), @('QUIVER', 420, 96), @('PRÜFSPIEL', 540, 72))) {
        $text = New-Object Windows.Media.FormattedText ($line[0], [Globalization.CultureInfo]::InvariantCulture, [Windows.FlowDirection]::LeftToRight, $face, [double]$line[2], [Windows.Media.Brushes]::White)
        $dc.DrawText($text, (New-Object Windows.Point ((600 - $text.Width) / 2), $line[1]))
    }
    $dc.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap 600, 900, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

# Prüfprogramm (kein Spiel): Arbeitsordner, eigener Pfad und Zahl der Argumente in eine Datei neben sich, dann 5 s warten
function New-ProbeExe([string]$target) {
    $probe = Join-Path $root 'tools\out\phase12\QuiverPruefprogramm.exe'
    if (-not (Test-Path $probe)) {
        New-Item -ItemType Directory -Force (Split-Path $probe) | Out-Null
        Add-Type -OutputAssembly $probe -OutputType WindowsApplication -TypeDefinition @'
public static class QuiverPruefprogramm {
    public static int Main(string[] args) {
        string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string dir = System.IO.Path.GetDirectoryName(self);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "pruefprogramm-start.txt"), System.Environment.CurrentDirectory + "\n" + self + "\n" + args.Length);
        System.Threading.Thread.Sleep(5000);
        return 0;
    }
}
'@
    }
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $probe $target
}

function Md5Upper([string]$text) {
    $md5 = [Security.Cryptography.MD5]::Create()
    try { return [BitConverter]::ToString($md5.ComputeHash([Text.Encoding]::ASCII.GetBytes($text))).Replace('-', '') } finally { $md5.Dispose() }
}

function Png-Size([string]$file) {
    if (-not (Test-Path $file)) { return $null }
    $stream = [IO.File]::OpenRead($file)
    try { $frame = [Windows.Media.Imaging.BitmapDecoder]::Create($stream, 'None', 'OnLoad').Frames[0]; return '{0}x{1}' -f $frame.PixelWidth, $frame.PixelHeight } finally { $stream.Dispose() }
}

$fakeDisplay = '\\?\DISPLAY#HDRP12000#phase12-quiver-gibt-es-nicht'
$folderName = 'HDRpilot-Quiver-Pruefspiel'
$gameName = 'HDRpilot Quiver-Prüfspiel'
$key = "Quiver:$folderName"
$iconUrl = 'https://cdn2.steamgriddb.com/grid/0123456789abcdef0123456789abcdef.png'
$cfg = New-TestConfig 'phase12-quiver'
$seed = Join-Path $root 'tools\out\phase12-quiver-seed'
$quiver = Join-Path $cfg 'quiver\QuiverLauncher-Pruefung'
$gameDir = Join-Path $quiver "Apps\$folderName"
$exe = Join-Path $gameDir 'QuiverPruefspiel.exe'
$probeOut = Join-Path $gameDir 'pruefprogramm-start.txt'
$fixtures = Join-Path $root 'tests\HdrPilot.Tests\Fixtures\Quiver'
$app = $null
try {
    # --- Nachgebauter Quiver-Ordner ---
    New-Item -ItemType Directory -Force (Join-Path $quiver 'current') | Out-Null
    Copy-Item (Join-Path $fixtures 'sq.version') (Join-Path $quiver 'current\sq.version')
    Copy-Item (Join-Path $fixtures 'settings.json') (Join-Path $quiver 'settings.json')
    $apps = '{ "apps": [ { "name": "NAME", "project": "HDRpilot-Prüfung", "folderName": "FOLDER", "installPath": null, "appIconUrl": "URL", "tags": [ "prüfung" ] } ] }'
    [IO.File]::WriteAllText((Join-Path $quiver 'apps.json'), $apps.Replace('NAME', $gameName).Replace('FOLDER', $folderName).Replace('URL', $iconUrl), (New-Object Text.UTF8Encoding $false))
    New-ProbeExe $exe
    [IO.File]::WriteAllText((Join-Path $gameDir 'version.txt'), 'V1')
    [IO.File]::WriteAllText((Join-Path $gameDir 'LastPlayed.txt'), '2026-10-19 21:00:00')
    $card = 'https://quiverlauncher.com/cdn-cgi/image/width=480,quality=70,format=auto,fit=scale-down/' + $iconUrl
    $cover = Join-Path $quiver ('Cache\Images\' + (Md5Upper $card))
    Write-Cover $cover
    Note ("Quiver-Ordner: {0}; Cover {1} ({2} Byte)" -f $quiver, (Split-Path $cover -Leaf), (Get-Item $cover).Length)

    # --- Ablage: gelernter Quiver-Ordner; nie echtes HDR (Bildschirm, den es nicht gibt), kurze Nachlaufzeit ---
    $config = Read-Config $cfg
    $config.settings | Add-Member -NotePropertyName 'quiverFolders' -NotePropertyValue @($quiver) -Force
    $config.settings | Add-Member -NotePropertyName 'targetDisplays' -NotePropertyValue @($fakeDisplay) -Force
    $config.settings | Add-Member -NotePropertyName 'gracePeriodSeconds' -NotePropertyValue 2 -Force
    $config.settings | Add-Member -NotePropertyName 'notifications' -NotePropertyValue $false -Force
    $config.settings | Add-Member -NotePropertyName 'hotkeyFallback' -NotePropertyValue $false -Force
    [IO.File]::WriteAllText((Join-Path $cfg 'config.json'), ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $config = Read-Config $cfg
    $safe = @($config.settings.targetDisplays).Count -eq 1 -and $config.settings.targetDisplays[0] -eq $fakeDisplay -and $config.settings.hotkeyFallback -eq $false
    Check $safe "Kein echter Bildschirm: globale Einstellung zeigt auf $fakeDisplay, Notlösung Tastenkürzel aus"
    if (-not $safe) { throw 'Testablage zeigt auf einen echten Bildschirm, Abbruch vor jedem Start' }

    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $win) 'Hauptfenster der Testinstanz geöffnet'
    if (-not $win) { throw 'Hauptfenster fehlt' }
    Check ($null -ne (Wait-Log $cfg 'Einlesen: \d+ Spiele' 90000)) 'Einlesen beim Start fertig'

    # --- Übernahme in die Ablage ---
    $game = $null
    [void](Wait-Until { $script:game = Find-GameConfig (Read-Config $cfg) $key; $null -ne $script:game } 10000)
    $game = $script:game
    Check ($null -ne $game) "Spiel $key in der Ablage"
    if ($game) {
        Check ($game.name -eq $gameName -and $game.source -eq 'Quiver' -and $game.installDir -eq $gameDir) ('Name „{0}“, Quelle {1}, Installationsordner {2}' -f $game.name, $game.source, $game.installDir)
        Check ($game.launchCommand -eq $exe -and $game.launchWorkingDirectory -eq $gameDir -and -not $game.launchArguments) ('Start-Befehl {0}, Arbeitsordner {1}, ohne Argumente' -f $game.launchCommand, $game.launchWorkingDirectory)
        Check (@($game.coverCandidates).Count -eq 1 -and $game.coverCandidates[0] -eq $cover) ('Cover-Hinweis: {0}' -f (@($game.coverCandidates) -join ', '))
        Check ($game.isNew -and -not $game.hdrAuto -and -not $game.autoHdr) 'Neu, „HDR automatisch“ und Auto HDR aus'
    }
    $others = @((Read-Config $cfg).games | Where-Object { $_.source -eq 'Quiver' -and "$($_.source):$($_.sourceId)" -ne $key })
    Note ('Weitere Quiver-Spiele des Rechners (nur gelesen): {0}' -f $(if ($others) { ($others | ForEach-Object { $_.name }) -join ', ' } else { 'keine' }))

    # --- Quellenfilter „Quiver“ ---
    $filter = Find-Id $win 'SourceFilter' 5000
    $expand = $null
    Check ($null -ne $filter -and $filter.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expand)) 'Quellenfilter SourceFilter da'
    $option = $null
    if ($expand) {
        $expand.Expand()
        [void](Wait-Until { $script:option = @(Get-ListItems $filter | Where-Object { $_.Current.Name -like 'Quiver (*)' }) | Select-Object -First 1; $null -ne $script:option } 4000)
        $option = $script:option
        $names = @(Get-ListItems $filter | ForEach-Object { $_.Current.Name })
        Check ($null -ne $option -and $names[-1] -eq $option.Current.Name) ('Quelle „Quiver“ im Filter, als letzte: {0}' -f ($names -join ', '))
        if ($option) { Select-El $option }
        $expand.Collapse()
        Start-Sleep -Milliseconds 600
    }
    $rows = Row-Names $win
    Check ($rows -contains $gameName -and $rows.Count -eq 1 + $others.Count) ('Gefiltert: nur Quiver-Spiele in der Liste ({0})' -f ($rows -join ', '))
    $toggle = Find-Id $win 'ToolbarToggle' 3000
    Check ($toggle -and $toggle.Current.ItemStatus -match '^Gefiltert: .*Quiver') ('Leiste nennt den Filter: „{0}“' -f $(if ($toggle) { $toggle.Current.ItemStatus }))

    # --- Kapsel: eingeklappt nennt sie die Quelle ---
    $toggleExpand = $null
    if ($toggle -and $toggle.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$toggleExpand)) {
        $toggleExpand.Collapse()
        Check (Wait-Until { $toggleExpand.Current.ExpandCollapseState -eq 'Collapsed' } 4000) 'Leiste eingeklappt (Kapsel)'
        Check ((Find-Id $win 'ToolbarToggle' 2000).Current.ItemStatus -match '^Gefiltert: .*Quiver') ('Kapsel nennt die Quelle: „{0}“' -f (Find-Id $win 'ToolbarToggle' 2000).Current.ItemStatus)
        $toggleExpand.Expand()
        Check (Wait-Until { $toggleExpand.Current.ExpandCollapseState -eq 'Expanded' } 4000) 'Leiste wieder ausgeklappt'
    } else { Check $false 'Lasche ToolbarToggle mit ExpandCollapse' }

    # --- Prüfspiel wählen, Cover aus dem Zwischenspeicher ---
    $row = @(Get-ListItems (Find-Id $win 'GameList' 3000) | Where-Object { $_.Current.Name -eq $gameName }) | Select-Object -First 1
    if ($row) { Select-El $row }
    Check (Wait-Until { (Detail-Name $win) -eq $gameName } 4000) ('Gewählt: „{0}“' -f (Detail-Name $win))
    $indexFile = Join-Path $cfg 'art\index.json'
    $located = $null
    [void](Wait-Until {
        $text = Read-Shared $indexFile
        if (-not $text) { return $false }
        $entry = ($text | ConvertFrom-Json).games.$key
        $script:located = if ($entry) { $entry.located.cover } else { $null }
        return $null -ne $script:located
    } 15000)
    $located = $script:located
    $coverText = if ($located) { '{0} ({1}, {2} × {3})' -f $located.path, $located.source, $located.w, $located.h } else { 'nicht im Verzeichnis' }
    Check ($located -and $located.path -eq $cover -and $located.source -eq 'Quiver') ('Cover aus dem Zwischenspeicher von Quiver: ' + $coverText)

    # --- „Starten“: Prüfprogramm direkt, Arbeitsordner = sein Ordner; die Sitzung erkennt es ---
    $launch = Find-Id $win 'LaunchButton' 3000
    Check ($launch -and $launch.Current.IsEnabled) 'Knopf „Starten“ bereit'
    $logBefore = (Read-Log $cfg).Length
    Remove-Item -LiteralPath $probeOut -ErrorAction SilentlyContinue
    if ($launch) { Invoke-El $launch }
    $started = Wait-Until { Test-Path -LiteralPath $probeOut } 20000
    $lines = if ($started) { @([IO.File]::ReadAllText($probeOut) -split "`n") } else { @() }
    Check ($started -and $lines[0] -eq $gameDir) ('Prüfprogramm gestartet, Arbeitsordner {0}' -f $(if ($lines) { $lines[0] } else { '<nicht gestartet>' }))
    $probeText = if ($lines.Count -ge 3) { '{0} ({1} Argumente)' -f $lines[1], $lines[2].Trim() } else { 'keine Angaben' }
    Check ($lines.Count -ge 3 -and $lines[1] -eq $exe -and $lines[2].Trim() -eq '0') ('Direkt die EXE, ohne Argumente: ' + $probeText)
    $detected = Wait-Log $cfg 'Spielprozess erkannt um [^\r\n]*QuiverPruefspiel\.exe' 15000
    Check ($null -ne $detected) 'Sitzung erkennt den Prozess des Prüfprogramms'
    Check ($null -ne (Wait-Log $cfg ([regex]::Escape("Spiel-EXEs für $key gemerkt")) 10000)) 'EXE für das Spiel gemerkt'
    Check ($null -ne (Wait-Log $cfg ('Sitzung endet um [^\r\n]*' + [regex]::Escape('"' + $gameName + '"')) 30000)) 'Sitzung endet nach der Nachlaufzeit'
    $log = Read-Log $cfg
    $log = $log.Substring([Math]::Min($logBefore, $log.Length))
    Check ($log.Contains('Start-Befehl ausgeführt') -and $log.Contains($exe)) 'Protokoll: Start-Befehl mit der EXE ausgeführt'
    Check ($log.Contains('ist nicht angeschlossen') -and -not $log.Contains('HDR-Schaltung um')) 'HDR nie geschaltet: Bildschirm „nicht angeschlossen“, keine HDR-Schaltung im Protokoll'
    $after = Find-GameConfig (Read-Config $cfg) $key
    Check (@($after.detectedExecutables) -contains $exe -and $null -ne $after.lastPlayed) ('Ablage: gemerkte EXE {0}, zuletzt gespielt {1}' -f (@($after.detectedExecutables) -join ', '), $after.lastPlayed)

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(15000)) 'Testinstanz beendet'

    # --- Bildschirmfotos: Ablage aus dem Prüfspiel und einigen weiteren Spielen (ohne andere Quiver-Spiele) ---
    if (Test-Path $seed) { Remove-Item $seed -Recurse -Force }
    New-Item -ItemType Directory -Force $seed | Out-Null
    $full = Read-Config $cfg
    $pick = @($full.games | Where-Object { "$($_.source):$($_.sourceId)" -eq $key }) + @($full.games | Where-Object { $_.source -ne 'Quiver' -and $_.isInstalled } | Sort-Object name | Select-Object -First 14)
    $full.games = $pick
    [IO.File]::WriteAllText((Join-Path $seed 'config.json'), ($full | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $shots = Join-Path $ShotsDir ('lauf-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $scenes = 'hauptfenster-quiver-liste', 'hauptfenster-quiver-raster', 'hauptfenster-quiver-regal', 'hauptfenster-quiver-regal-details', 'hauptfenster-quiver-gefiltert', 'hauptfenster-quiver-kapsel'
    $snap = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$seed`"", '--snapshot', "`"$shots`"", '--snapshot-sizes', 'review', '--snapshot-scenes', ($scenes -join ','), '--snapshot-themes', 'dunkel,hell') -PassThru -Wait
    Check ($snap.ExitCode -eq 0) "Bildschirmfotos gerendert (Exit-Code $($snap.ExitCode), $($pick.Count) Spiele in der Ablage)"
    foreach ($scene in $scenes) {
        foreach ($size in '1280x800', '1920x1080', '3440x1440') {
            foreach ($theme in 'dunkel', 'hell') {
                $dims = Png-Size (Join-Path $shots "$scene-$size-$theme.png")
                Check ($dims -eq $size) "Bild $scene $size $theme ($dims)"
            }
        }
    }
    Note "Bildschirmfotos: $shots"
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"; $ok = $false }
finally {
    Stop-Started
    Get-Process -Name 'QuiverPruefspiel' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$cfg*" } | Stop-Process -Force -ErrorAction SilentlyContinue
}
if ($ok) { Write-Output 'PHASE12_QUIVER_UI_OK'; exit 0 }
exit 1
