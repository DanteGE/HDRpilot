# Gate G11.2: Daten nicht installierter Spiele entfernen und ausblenden (E90) per UI-Automation an einer eigenen Testinstanz
# (eigene Ablage, nur Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben) und Bildschirmfotos.
# Testablage: zwei nicht installierte Launcher-Spiele mit erfundenen IDs (Steam auf einem nicht verbundenen Laufwerk, mit
# „HDR automatisch“ und zwei gemerkten EXE-Dateien; Epic in einem fehlenden Ordner auf einem verbundenen Laufwerk) und ein
# eigenes Spiel (Ordner mit Stellvertreter-EXE). Beim Start liest die Testinstanz die echte Bibliothek nur lesend ein; diese
# Spiele kommen installiert und ohne „HDR automatisch“ dazu.
#   - Menüeintrag „Einstellungen entfernen…“ und Knopf ganz unten nur bei den beiden nicht installierten Launcher-Spielen,
#     nicht beim eigenen Spiel (das behält „Aus der Bibliothek entfernen“) und nicht bei einem installierten
#   - Rückfrage: nennt, was verloren geht („HDR automatisch“ ist an, 2 gemerkte EXE-Dateien), warnt beim nicht verbundenen
#     Laufwerk („vielleicht noch installiert“), beim anderen Spiel nicht; Fokus auf „Abbrechen“ (UI-Automation im aktiven
#     Fenster, sonst der Fokus, den die Rückfrage beim Aktivieren wiederherstellt, aus dem Protokoll der Testinstanz)
#   - „Abbrechen“ lässt die Testablage byteweise gleich; „Entfernen“ nimmt genau dieses Spiel heraus (alle übrigen Einträge
#     unverändert) und wählt den Nachbarn
#   - Einstellungen › Bibliothek: „zurzeit 2“; ausblenden und speichern senkt die Zahl der Spiele um 2, wieder einblenden
#     hebt sie zurück
#   - Sammel-Entfernen (mit der Testablage von vorher): die Rückfrage nennt genau die beiden Spiele, danach fehlen genau sie
#   - Bildschirmfotos (Knopf ganz unten, Kontextmenü, Einstellungskarte) bei 1280×800, 1920×1080 und 3440×1440, hell und
#     dunkel, dazu beide Rückfragen in ihrer eigenen Größe
# Schaltet kein HDR, startet kein Spiel, legt keine Verknüpfung an. Die Bildschirmfotos enthalten Spielbilder und landen
# deshalb nie im Projektordner, sondern in -ShotsDir (Standard: Temp-Ordner).
param([string]$ShotsDir = (Join-Path ([IO.Path]::GetTempPath()) 'hdrpilot-phase11-ui'))
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
Add-Type -AssemblyName PresentationCore
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

# Elemente in eigenen Fenstern derselben Prozess-ID (Kontextmenü, Rückfrage, Einstellungen)
function Find-Top([int]$processId, [string]$automationId, [int]$timeoutMs = 5000) {
    $byPid = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $byId = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do {
        foreach ($top in $UiaRoot.FindAll([Windows.Automation.TreeScope]::Children, $byPid)) {
            if ($top.Current.AutomationId -eq $automationId) { return $top }
            $e = $top.FindFirst([Windows.Automation.TreeScope]::Descendants, $byId)
            if ($e) { return $e }
        }
        Start-Sleep -Milliseconds 100
    } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
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

function Detail-Name($win) { try { $d = Find-Id $win 'DetailName' 500; if ($d) { [string]$d.Current.Name } else { $null } } catch { $null } }
function Shown-Items($list) {
    $r = $list.Current.BoundingRectangle
    return @(Get-ListItems $list | Where-Object { $b = $_.Current.BoundingRectangle; -not $b.IsEmpty -and $b.Width -gt 0 -and $b.Bottom -gt $r.Top -and $b.Top -lt $r.Bottom -and -not $_.Current.IsOffscreen })
}
function Row-Names($win) { try { @(Shown-Items (Find-Id $win 'GameList' 2000) | ForEach-Object { [string]$_.Current.Name }) } catch { @() } }
# Texte unter einem Element (etwa die Aufzählung der Rückfrage)
function Texts($el) {
    $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)
    return @($el.FindAll([Windows.Automation.TreeScope]::Descendants, $cond) | ForEach-Object { [string]$_.Current.Name } | Where-Object { $_ })
}

# Inhalt der Testablage, gelesen mit Freigabe zum Ersetzen (sonst scheitert das Speichern der App mit „Zugriff verweigert“)
function Config-Bytes([string]$cfg) {
    $fs = New-Object IO.FileStream((Join-Path $cfg 'config.json'), 'Open', 'Read', 'ReadWrite, Delete')
    try { $ms = New-Object IO.MemoryStream; $fs.CopyTo($ms); return , $ms.ToArray() } finally { $fs.Dispose() }
}
function Hash([byte[]]$bytes) { [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)).Replace('-', '') }
# Wartet, bis die Testablage $quietMs lang unverändert ist (die App schreibt nach dem Einlesen und nach Änderungen)
function Wait-Quiet([string]$cfg, [int]$quietMs = 2000, [int]$timeoutMs = 30000) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); $last = Hash (Config-Bytes $cfg); $since = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        Start-Sleep -Milliseconds 250
        $now = Hash (Config-Bytes $cfg)
        if ($now -ne $last) { $last = $now; $since.Restart() } elseif ($since.ElapsedMilliseconds -ge $quietMs) { return $true }
    }
    return $false
}
function Game-Json($game) { $game | ConvertTo-Json -Depth 20 -Compress }

# Freier Laufwerksbuchstabe: Dort liegt das erste Testspiel („Laufwerk nicht verbunden“)
function Free-DriveLetter {
    $used = @([IO.DriveInfo]::GetDrives() | ForEach-Object { $_.Name.Substring(0, 1).ToUpperInvariant() })
    foreach ($letter in 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'P', 'O', 'N') {
        if ($used -notcontains $letter -and -not (Test-Path ($letter + ':\'))) { return $letter }
    }
    throw 'Kein freier Laufwerksbuchstabe'
}

# Zahl aller Spiele aus der Statuszeile („Liste · 3 von 652 · sortiert nach …“)
function Total-Count($win) {
    $s = Find-Id $win 'ViewSummary' 1000
    if (-not $s) { return -1 }
    $m = [regex]::Match([string]$s.Current.Name, '(\d+) von (\d+)')
    if ($m.Success) { return [int]$m.Groups[2].Value } else { return -1 }
}

function Search($win, [string]$text) {
    Set-Text (Find-Id $win 'SearchBox' 3000) $text
    Start-Sleep -Milliseconds 500
}

function Select-Game($win, [string]$name) {
    $row = @(Shown-Items (Find-Id $win 'GameList' 3000) | Where-Object { $_.Current.Name -eq $name }) | Select-Object -First 1
    if (-not $row) { return $false }
    Select-El $row
    return (Wait-Until { (Detail-Name $win) -eq $name } 3000)
}

# Kontextmenü des gewählten Spiels über „···“ im Kopf der Details (dasselbe Menü wie in Zeilen und Kacheln)
function Open-Menu($win, [int]$processId) {
    Invoke-El (Find-Id $win 'ProfileMenuButton' 3000)
    return Find-Top $processId 'ContextOpenFolder' 3000
}
# Menü ohne Wirkung schließen: „Bild wählen…“ öffnen und abbrechen (schreibt nichts)
function Close-Menu([int]$processId) {
    $choose = Find-Top $processId 'ChooseArtworkMenuItem' 2000
    if ($choose) { Invoke-El $choose }
    $picker = Find-Top $processId 'ArtworkPickerDialog' 8000
    if ($picker) { Invoke-El (Find-Id $picker 'ArtworkCancelButton' 3000); [void](Wait-Until { -not (Find-Top $processId 'ArtworkPickerDialog' 300) } 4000) }
}

# Fokus der offenen Rückfrage: im aktiven Fenster laut UI-Automation, sonst laut Protokoll der Testinstanz (Fokus, den sie
# beim Aktivieren wiederherstellt); liefert die Kennung des Elements
function Dialog-Focus($dlg, [string]$cfg, [int]$seen) {
    $cancel = Find-Id $dlg 'ConfirmCancel' 2000
    if ($cancel -and $cancel.Current.HasKeyboardFocus) { return 'ConfirmCancel (Tastaturfokus)' }
    $pattern = 'Prüfung: Rückfrage „[^“]*“ offen, Fokus (\S+)\.'
    [void](Wait-Until { $t = Wait-Log $cfg $pattern 500; $t -and [regex]::Matches($t, $pattern).Count -gt $seen } 4000)
    $text = Wait-Log $cfg $pattern 500
    if (-not $text) { return 'nicht im Protokoll' }
    $all = [regex]::Matches($text, $pattern)
    if ($all.Count -le $seen) { return 'nicht im Protokoll' }
    return $all[$all.Count - 1].Groups[1].Value + ' (Protokoll)'
}
function Log-Count([string]$cfg, [string]$pattern) { $t = Wait-Log $cfg $pattern 200; if ($t) { [regex]::Matches($t, $pattern).Count } else { 0 } }

$cfg = New-TestConfig 'phase11-ui'
$seed = Join-Path $root 'tools\out\phase11-ui-seed'
$nameA = 'HDRpilot Testspiel Laufwerk'
$nameB = 'HDRpilot Testspiel Ordner'
$nameManual = 'HDRpilot Eigenes Spiel'
$dialogPattern = 'Prüfung: Rückfrage „[^“]*“ offen, Fokus (\S+)\.'
$shots = $null
try {
    # --- Testablage: eigenes Spiel über die Kommandozeile (wie „Spiel hinzufügen“), dazu zwei nicht installierte ---
    $manual = New-StandInGame $cfg $nameManual $false
    $letter = Free-DriveLetter
    $dirA = "$($letter):\SteamLibrary\steamapps\common\$nameA"
    $config = Read-Config $cfg
    $gameA = [pscustomobject][ordered]@{
        source = 'Steam'; sourceId = '990001304'; name = $nameA; installDir = $dirA
        knownExecutables = @(); detectedExecutables = @("$dirA\Binaries\Win64\Testspiel-Win64-Shipping.exe", "$dirA\Testspiel.exe")
        excludedExecutables = @(); extraExecutables = @(); launchCommand = 'steam://rungameid/990001304'
        hdrAuto = $true; autoHdr = $false; restoreAfter = $true; isNew = $false; isInstalled = $false; iconCandidates = @()
    }
    $gameB = [pscustomobject][ordered]@{
        source = 'Epic'; sourceId = 'hdrpilot-testspiel-ordner'; name = $nameB; installDir = (Join-Path $cfg "nicht-mehr-da\$nameB")
        knownExecutables = @(); detectedExecutables = @(); excludedExecutables = @(); extraExecutables = @()
        hdrAuto = $false; autoHdr = $false; restoreAfter = $true; isNew = $false; isInstalled = $false; iconCandidates = @()
    }
    $config.games = @(@($config.games) + $gameA + $gameB)
    [IO.File]::WriteAllText((Join-Path $cfg 'config.json'), ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $keyA = 'Steam:990001304'; $keyB = 'Epic:hdrpilot-testspiel-ordner'
    Note "Testablage: $keyA auf $($letter): (nicht verbunden), $keyB, eigenes Spiel $($manual.Key)"

    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $win) 'Hauptfenster der Testinstanz geöffnet'
    if (-not $win) { throw 'Hauptfenster fehlt' }
    Check ($null -ne (Wait-Log $cfg 'Einlesen: \d+ Spiele' 90000)) 'Einlesen beim Start fertig'
    Check (Wait-Quiet $cfg) 'Testablage danach unverändert'
    $config = Read-Config $cfg
    $a = Find-GameConfig $config $keyA; $b = Find-GameConfig $config $keyB; $m = Find-GameConfig $config $manual.Key
    Check ($a -and -not $a.isInstalled -and $b -and -not $b.isInstalled -and $m -and $m.isInstalled) ("Nach dem Einlesen: beide Testspiele nicht installiert, das eigene installiert ({0} Spiele)" -f @($config.games).Count)
    New-Item -ItemType Directory -Force $seed | Out-Null
    Get-ChildItem $seed -File -ErrorAction SilentlyContinue | Remove-Item -Force
    [IO.File]::WriteAllBytes((Join-Path $seed 'config.json'), (Config-Bytes $cfg))

    # --- Menüeintrag und Knopf: nur bei nicht installierten Launcher-Spielen ---
    Search $win 'HDRpilot'
    Check (Wait-Until { (Row-Names $win).Count -eq 3 }) ('Suche „HDRpilot“: die drei Testspiele ({0})' -f ((Row-Names $win) -join ', '))
    foreach ($case in @(@($nameA, $true), @($nameB, $true), @($nameManual, $false))) {
        $name = $case[0]; $removable = $case[1]
        Check (Select-Game $win $name) ('„{0}“ gewählt' -f $name)
        $button = Find-Id $win 'RemoveSettingsButton' $(if ($removable) { 3000 } else { 800 })
        $removeGame = Find-Id $win 'RemoveGameButton' $(if ($removable) { 800 } else { 3000 })
        Check ($removable -eq ($null -ne $button) -and $removable -ne ($null -ne $removeGame)) ('„{0}“: Knopf „Einstellungen entfernen…“ {1}, „Aus der Bibliothek entfernen“ {2}' -f $name, $(if ($button) { 'da' } else { 'fehlt' }), $(if ($removeGame) { 'da' } else { 'fehlt' }))
        if ($button) { Check ($button.Current.IsEnabled -and $button.Current.Name -eq 'Einstellungen entfernen…') ('„{0}“: Knopf frei und benannt' -f $name) }
        $notice = Find-Id $win 'DetailNotInstalled' 1500
        if ($removable) { Check ($notice -and $notice.Current.Name -match 'ganz unten') ('Hinweis „nicht mehr gefunden“ nennt das Entfernen ganz unten ({0})' -f $(if ($notice) { $notice.Current.Name })) }
        if (-not $removable) {
            Check ($null -ne (Open-Menu $win $app.Id)) ('„{0}“: Kontextmenü offen' -f $name)
            Check ($null -eq (Find-Top $app.Id 'ContextRemoveSettings' 800)) ('„{0}“: kein Menüeintrag „Einstellungen entfernen…“' -f $name)
            Close-Menu $app.Id
        }
    }

    # --- Rückfrage über das Kontextmenü: Inhalt, Fokus, „Abbrechen“ lässt die Testablage byteweise gleich ---
    foreach ($case in @(@($nameA, $true), @($nameB, $false))) {
        $name = $case[0]; $driveMissing = $case[1]
        [void](Select-Game $win $name)
        Check (Wait-Quiet $cfg 1500) 'Testablage ruht'
        $before = Config-Bytes $cfg
        $seenDialogs = Log-Count $cfg $dialogPattern
        [void](Open-Menu $win $app.Id)
        $entry = Find-Top $app.Id 'ContextRemoveSettings' 2000
        Check ($null -ne $entry -and $entry.Current.IsEnabled) ('„{0}“: Menüeintrag „Einstellungen entfernen…“ da und frei' -f $name)
        if (-not $entry) { Close-Menu $app.Id; continue }
        Invoke-El $entry
        $dlg = Find-Top $app.Id 'ConfirmDialog' 8000
        Check ($null -ne $dlg) ('„{0}“: Rückfrage geöffnet' -f $name)
        if (-not $dlg) { continue }
        $title = [string](Find-Id $dlg 'ConfirmTitle' 2000).Current.Name
        $details = Texts (Find-Id $dlg 'ConfirmDetails' 2000)
        $warning = Find-Id $dlg 'ConfirmWarning' $(if ($driveMissing) { 2000 } else { 600 })
        Check ($title -eq ('Einstellungen von „{0}“ entfernen?' -f $name)) ('Titel: {0}' -f $title)
        if ($driveMissing) {
            Check (($details -contains '„HDR automatisch“ ist an') -and ($details -contains '2 gemerkte EXE-Dateien')) ('Nennt, was verloren geht: ' + ($details -join ' | '))
            Check ($warning -and $warning.Current.Name -eq "Laufwerk $($letter): ist nicht verbunden. Das Spiel ist vielleicht noch installiert.") ('Warnt beim nicht verbundenen Laufwerk: ' + $(if ($warning) { $warning.Current.Name } else { 'keine Warnung' }))
        } else {
            Check ($details -contains 'Nur der Eintrag in der Bibliothek, keine eigenen Einstellungen') ('Ohne eigene Einstellungen: ' + ($details -join ' | '))
            Check ($null -eq $warning) 'Laufwerk verbunden: keine Warnung'
        }
        Check ($null -eq (Find-Id $dlg 'ConfirmOption' 500)) 'Ohne HDRpilot-Verknüpfungen kein Häkchen'
        $focus = Dialog-Focus $dlg $cfg $seenDialogs
        Check ($focus -like 'ConfirmCancel*') ('Fokus auf „Abbrechen“: {0}' -f $focus)
        Invoke-El (Find-Id $dlg 'ConfirmCancel' 2000)
        Check (Wait-Until { -not (Find-Top $app.Id 'ConfirmDialog' 300) }) 'Abbrechen schließt die Rückfrage'
        Start-Sleep -Milliseconds 1500
        Check ((Hash $before) -eq (Hash (Config-Bytes $cfg))) ('„Abbrechen“: Testablage byteweise gleich ({0})' -f $name)
        Check ((Detail-Name $win) -eq $name) ('„{0}“ bleibt gewählt' -f $name)
    }

    # --- Installiertes Launcher-Spiel: weder Knopf noch Menüeintrag ---
    Search $win ''
    Start-Sleep -Milliseconds 600
    $installed = @(Row-Names $win | Where-Object { $_ -notlike 'HDRpilot*' }) | Select-Object -First 1
    if ($installed) {
        Check (Select-Game $win $installed) ('Installiertes Spiel „{0}“ gewählt' -f $installed)
        Check ($null -eq (Find-Id $win 'RemoveSettingsButton' 800) -and $null -eq (Find-Id $win 'RemoveGameButton' 500)) ('„{0}“: kein Knopf zum Entfernen' -f $installed)
        Check ($null -ne (Open-Menu $win $app.Id) -and $null -eq (Find-Top $app.Id 'ContextRemoveSettings' 800)) ('„{0}“: kein Menüeintrag „Einstellungen entfernen…“' -f $installed)
        Close-Menu $app.Id
    } else { Note 'UMGEBUNG: keine installierten Spiele in der echten Bibliothek, Prüfung am installierten Spiel übersprungen' }

    # --- Einstellungen › Bibliothek: ausblenden und speichern senkt die Zahl um 2, einblenden hebt sie zurück ---
    # Die Einstellungen gehören zum offenen Hauptfenster (Owner): UI-Automation führt sie unter ihm, nicht auf oberster Ebene.
    $total0 = Total-Count $win
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--settings') | Out-Null
    $set = Find-Top $app.Id 'SettingsWindow' 20000
    Check ($null -ne $set) 'Einstellungen geöffnet'
    if ($set) {
        Invoke-El (Find-Id $set 'NavLibrary' 3000)
        $count = Find-Id $set 'UninstalledCount' 3000
        Check ($count -and $count.Current.Name -eq 'Nicht installierte Spiele zurzeit: 2') ('Karte „Bibliothek“: ' + $(if ($count) { $count.Current.Name }))
        $toggle = Find-Id $set 'HideUninstalledToggle' 3000
        Check ((Get-Toggle $toggle) -eq 'Off') 'Standard: nicht ausgeblendet'
        Check ((Toggle-El $toggle) -eq 'On') 'Ausblenden eingeschaltet'
        Check (-not (Read-Config $cfg).settings.hideUninstalledGames) 'Vor „Speichern“ nichts gespeichert'
        Invoke-El (Find-Id $set 'SaveSettingsButton' 2000)
        Check (Wait-Until { (Read-Config $cfg).settings.hideUninstalledGames -eq $true }) 'Gespeichert (hideUninstalledGames)'
        Check (Wait-Until { (Total-Count $win) -eq $total0 - 2 } 5000) ("Zahl der Spiele sinkt um 2: {0} → {1}" -f $total0, (Total-Count $win))
        Search $win 'HDRpilot'
        Check (Wait-Until { (Row-Names $win) -join '|' -eq $nameManual } 4000) ('Ausgeblendet: Suche „HDRpilot“ zeigt nur das eigene Spiel ({0})' -f ((Row-Names $win) -join ', '))
        Search $win ''
        Start-Sleep -Milliseconds 1200

        Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--settings') | Out-Null
        $set = Find-Top $app.Id 'SettingsWindow' 20000
        Invoke-El (Find-Id $set 'NavLibrary' 3000)
        $toggle = Find-Id $set 'HideUninstalledToggle' 3000
        Check ((Get-Toggle $toggle) -eq 'On') 'Wieder geöffnet: ausgeblendet'
        [void](Toggle-El $toggle)
        Invoke-El (Find-Id $set 'SaveSettingsButton' 2000)
        Check (Wait-Until { (Read-Config $cfg).settings.hideUninstalledGames -eq $false }) 'Eingeblendet und gespeichert'
        Check (Wait-Until { (Total-Count $win) -eq $total0 } 5000) ("Zahl der Spiele wieder {0} ({1})" -f $total0, (Total-Count $win))
    }

    # --- „Entfernen“ über den Knopf ganz unten: genau dieses Spiel weniger, alles andere unverändert, Nachbar gewählt ---
    Search $win 'HDRpilot Testspiel'
    Check (Wait-Until { (Row-Names $win).Count -eq 2 }) ('Suche „HDRpilot Testspiel“: {0}' -f ((Row-Names $win) -join ', '))
    Check (Select-Game $win $nameA) ('„{0}“ gewählt' -f $nameA)
    Check (Wait-Quiet $cfg 1500) 'Testablage ruht'
    $beforeGames = @((Read-Config $cfg).games)
    Invoke-El (Find-Id $win 'RemoveSettingsButton' 3000)
    $dlg = Find-Top $app.Id 'ConfirmDialog' 8000
    Check ($dlg -and (Find-Id $dlg 'ConfirmTitle' 2000).Current.Name -eq ('Einstellungen von „{0}“ entfernen?' -f $nameA)) 'Knopf ganz unten öffnet dieselbe Rückfrage'
    if ($dlg) { Invoke-El (Find-Id $dlg 'ConfirmOk' 2000) }
    Check (Wait-Until { $null -eq (Find-GameConfig (Read-Config $cfg) $keyA) } 6000) ('„{0}“ entfernt' -f $nameA)
    Start-Sleep -Milliseconds 1000
    $afterGames = @((Read-Config $cfg).games)
    $changed = @($beforeGames | Where-Object { "$($_.source):$($_.sourceId)" -ne $keyA } | Where-Object {
        $key = "$($_.source):$($_.sourceId)"; $now = Find-GameConfig @{ games = $afterGames } $key
        -not $now -or (Game-Json $now) -ne (Game-Json $_) })
    Check ($afterGames.Count -eq $beforeGames.Count - 1 -and $changed.Count -eq 0) ("Genau ein Spiel weniger ({0} → {1}), alle übrigen unverändert ({2} geändert)" -f $beforeGames.Count, $afterGames.Count, $changed.Count)
    Check (Wait-Until { (Detail-Name $win) -eq $nameB } 3000) ('Nachbar gewählt: „{0}“' -f (Detail-Name $win))
    $status = Find-Id $win 'StatusMessage' 2000
    Check ($status -and $status.Current.Name -eq ('Einstellungen von „{0}“ entfernt.' -f $nameA)) ('Rückmeldung: ' + $(if ($status) { $status.Current.Name }))

    # --- Sammel-Entfernen mit der Testablage von vorher (beide Spiele nicht installiert) ---
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(15000)) 'Testinstanz beendet'
    [IO.File]::Copy((Join-Path $seed 'config.json'), (Join-Path $cfg 'config.json'), $true)
    $scans = Log-Count $cfg 'Einlesen: \d+ Spiele'
    $app = Start-App $cfg @('--settings')
    $set = Find-Window 'SettingsWindow' $app.Id
    Check ($null -ne $set) 'Neu gestartet mit der Testablage von vorher, Einstellungen offen'
    [void](Wait-Until { (Log-Count $cfg 'Einlesen: \d+ Spiele') -gt $scans } 90000)
    Check (Wait-Quiet $cfg) 'Einlesen fertig, Testablage ruht'
    if ($set) {
        Invoke-El (Find-Id $set 'NavLibrary' 3000)
        $beforeGames = @((Read-Config $cfg).games)
        Invoke-El (Find-Id $set 'RemoveUninstalledButton' 3000)
        $dlg = Find-Top $app.Id 'ConfirmDialog' 8000
        Check ($null -ne $dlg) 'Sammel-Entfernen: Rückfrage geöffnet'
        if ($dlg) {
            $title = [string](Find-Id $dlg 'ConfirmTitle' 2000).Current.Name
            $details = Texts (Find-Id $dlg 'ConfirmDetails' 2000)
            Check ($title -eq 'Einstellungen von 2 nicht installierten Spielen entfernen?') "Titel: $title"
            Check ($details.Count -eq 2 -and $details[0] -like "$nameA · *" -and $details[1] -eq $nameB) ('Nennt genau die beiden Spiele: ' + ($details -join ' | '))
            $warning = Find-Id $dlg 'ConfirmWarning' 2000
            Check ($warning -and $warning.Current.Name -like "Laufwerk $($letter): ist nicht verbunden.*") ('Warnung: ' + $(if ($warning) { $warning.Current.Name }))
            Invoke-El (Find-Id $dlg 'ConfirmOk' 2000)
        }
        Check (Wait-Until { $c = Read-Config $cfg; $null -eq (Find-GameConfig $c $keyA) -and $null -eq (Find-GameConfig $c $keyB) } 8000) 'Beide Spiele entfernt'
        Start-Sleep -Milliseconds 1000
        $afterGames = @((Read-Config $cfg).games)
        $changed = @($beforeGames | Where-Object { "$($_.source):$($_.sourceId)" -notin $keyA, $keyB } | Where-Object {
            $key = "$($_.source):$($_.sourceId)"; $now = Find-GameConfig @{ games = $afterGames } $key
            -not $now -or (Game-Json $now) -ne (Game-Json $_) })
        Check ($afterGames.Count -eq $beforeGames.Count - 2 -and $changed.Count -eq 0) ("Nur sie entfernt ({0} → {1}), alle übrigen unverändert ({2} geändert)" -f $beforeGames.Count, $afterGames.Count, $changed.Count)
        $message = Find-Id $set 'LibraryMessage' 3000
        Check ($message -and $message.Current.Name -eq 'Einstellungen von 2 Spielen entfernt.') ('Rückmeldung in der Karte: ' + $(if ($message) { $message.Current.Name }))
        Check ((Find-Id $set 'UninstalledCount' 2000).Current.Name -eq 'Nicht installierte Spiele zurzeit: 0') 'Karte zählt danach 0'
        Invoke-El (Find-Id $set 'CancelSettingsButton' 2000)
    }
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(15000)) 'Testinstanz beendet'
    Check (@((Read-Config $cfg).games | Where-Object { $_.hdrAuto -or $_.autoHdr }).Count -eq 0) 'Am Ende hat kein Spiel „HDR automatisch“ oder „Windows Auto HDR“ an'

    # --- Bildschirmfotos mit der Testablage von vorher ---
    $shots = Join-Path $ShotsDir ('lauf-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $scenes = 'hauptfenster-nicht-installiert', 'hauptfenster-kontextmenue-nicht-installiert', 'einstellungen-bibliothek'
    $dialogs = 'hauptfenster-rueckfrage-entfernen', 'hauptfenster-rueckfrage-entfernen-verknuepfungen', 'einstellungen-rueckfrage-alle'
    $snap = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$seed`"", '--snapshot', "`"$shots`"", '--snapshot-sizes', 'review', '--snapshot-scenes', (($scenes + $dialogs) -join ','), '--snapshot-themes', 'dunkel,hell') -PassThru -Wait
    Check ($snap.ExitCode -eq 0) "Bildschirmfotos gerendert (Exit-Code $($snap.ExitCode))"
    function Png-Size([string]$file) {
        if (-not (Test-Path $file)) { return $null }
        $stream = [IO.File]::OpenRead($file)
        try { $frame = [System.Windows.Media.Imaging.BitmapDecoder]::Create($stream, 'None', 'OnLoad').Frames[0]; return '{0}x{1}' -f $frame.PixelWidth, $frame.PixelHeight } finally { $stream.Dispose() }
    }
    foreach ($scene in $scenes) {
        foreach ($size in '1280x800', '1920x1080', '3440x1440') {
            foreach ($theme in 'dunkel', 'hell') {
                $dims = Png-Size (Join-Path $shots "$scene-$size-$theme.png")
                Check ($dims -eq $size) "Bild $scene $size $theme ($dims)"
            }
        }
    }
    foreach ($scene in $dialogs) {
        foreach ($theme in 'dunkel', 'hell') {
            $dims = Png-Size (Join-Path $shots "$scene-$theme.png")
            Check ($dims -and [int]($dims -split 'x')[0] -ge 500) "Bild $scene $theme in eigener Größe ($dims)"
        }
    }
    Note "Bildschirmfotos: $shots"
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"; $ok = $false }
finally { Stop-Started }
if ($ok) { Write-Output 'PHASE11_UI_OK'; exit 0 }
exit 1
