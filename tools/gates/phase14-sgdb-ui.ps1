# Gate G14.1–G14.9 und G14.11: SteamGridDB-Suche in „Bild wählen…“ (E93) per UI-Automation an einer eigenen Testinstanz
# (eigene Ablage, nur Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben, nur Muster Invoke, Toggle,
# ExpandCollapse, SelectionItem und Value) und Bildschirmfotos.
# Kein Netz: HDRPILOT_TEST_STEAMGRIDDB=fixture schaltet den Testdaten-Lieferanten ein (nur mit --config-dir). Er antwortet
# mit erzeugten Daten (Verläufe und Bänder, Autoren „Testautor 01“ bis „Testautor 72“, keine Spielbilder) und schreibt jede
# Anfrage als Zeile in <Ablage>\sgdb-fixture.log („GET <Rechner> <Pfad> schluessel=ja|nein“, nie den Schlüssel).
# Testablage: zwei erfundene Spiele, „HDRpilot-Prüfspiel Nebelfahrt“ (eigenes Spiel, harmloses Ersatzprogramm) und
# „HDRpilot-Prüfspiel Kupferwald“ (Steam, erfundene AppID 9990150, nicht installiert). Geprüft wird:
#   A  Schlüssel bei ausgeschaltetem Schalter eingeben und prüfen, genau eine Anfrage, danach kein automatischer Lauf
#   B  vor dem Klick auf „Suchen“ keine Anfrage (Reiter, Spielwahl auf- und zuklappen, Text im Suchfeld)
#   C  Suche und „Mehr laden“ (24, 48, 70, Ende), nur die Rechner von SteamGridDB, nie http
#   D  je Reiter ein Klick; E  Übernehmen (Cover); F  Zurücksetzen; G  Abbrechen; H  Spiel korrigieren und merken
#   I  keine Bilder; J  zu viele Anfragen und langsame Antworten; K  abgelehnter Schlüssel, Sprung zu den Einstellungen
#   L  keine echten Anfragen und keine Lecks; M  Bildschirmfotos (22 Bilder)
# HDR wird nie geschaltet: Die globale Einstellung zeigt auf einen Bildschirm, den es nicht gibt (das Protokoll darf keine
# „HDR-Schaltung“ enthalten). Die Bildschirmfotos landen nie im Projektordner, sondern in -ShotsDir.
#   PHASE14_SGDB_UI_OK
param([string]$ShotsDir = (Join-Path ([IO.Path]::GetTempPath()) 'hdrpilot-phase14-sgdb-ui'))
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

# Texte aus den Ressourcen der App (nie abgeschrieben): Platzhalter {0} werden zu * für -like
$resDir = Join-Path $root 'src\HdrPilot.App\Resources'
$resCache = @{}
function Res([string]$file, [string]$name) {
    if (-not $resCache.ContainsKey($file)) { $resCache[$file] = [xml](Get-Content -Raw -Encoding UTF8 (Join-Path $resDir "$file.resx")) }
    $node = @($resCache[$file].root.data | Where-Object { $_.name -eq $name }) | Select-Object -First 1
    if (-not $node) { throw "Ressource $file.$name fehlt" }
    return [string]$node.value
}
function Res-Pattern([string]$file, [string]$name) { [Management.Automation.WildcardPattern]::Escape((Res $file $name)) -replace '\{\d+(:[^}]*)?\}', '*' }
function Res-Like([string]$text, [string]$file, [string]$name) { return [bool]($text -and $text -like (Res-Pattern $file $name)) }

# Anfrageprotokoll des Lieferanten
function Fixture-Lines { $t = Read-Shared $fixtureLog; if ($t) { @($t -split "`r?`n" | Where-Object { $_ }) } else { @() } }
function New-Lines([int]$from) { $all = Fixture-Lines; if ($all.Count -gt $from) { @($all[$from..($all.Count - 1)]) } else { @() } }
# „Eingeschwungen“: 2 s lang keine neue Zeile (höchstens 30 s)
function Wait-Settled {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $last = (Fixture-Lines).Count
    $quiet = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 30000) {
        Start-Sleep -Milliseconds 200
        $now = (Fixture-Lines).Count
        if ($now -ne $last) { $last = $now; $quiet.Restart() } elseif ($quiet.ElapsedMilliseconds -ge 2000) { return $true }
    }
    return $false
}

# Alle Namen im Fenster (Statuszeilen, Hinweise, Knöpfe), für die Prüfung von Meldungen
function Texts($el) {
    if (-not $el) { return @() }
    @($el.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { [string]$_.Current.Name } | Where-Object { $_ })
}
function Has-Res($el, [string]$file, [string]$name) { $p = Res-Pattern $file $name; return [bool](@(Texts $el | Where-Object { $_ -like $p }).Count) }
function Wait-Res($el, [string]$file, [string]$name, [int]$timeoutMs = 8000) { Wait-Until { Has-Res $el $file $name } $timeoutMs }

function Expand($el, [bool]$open) {
    $p = $null
    if ($el -and $el.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
        if ($open) { $p.Expand() } else { $p.Collapse() }
        Start-Sleep -Milliseconds 400
        return $true
    }
    return $false
}
function Is-Selected($el) { $p = $null; $el -and $el.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p) -and $p.Current.IsSelected }

function Results($dialog) { $list = Find-Id $dialog 'ArtworkSgdbResults' 500; if ($list) { @(Get-ListItems $list) } else { @() } }
function Wait-Results($dialog, [int]$count, [int]$timeoutMs = 15000) { Wait-Until { (Results $dialog).Count -eq $count } $timeoutMs }

function Select-Game($win, [string]$name) {
    $row = @(Get-ListItems (Find-Id $win 'GameList' 3000) | Where-Object { $_.Current.Name -eq $name }) | Select-Object -First 1
    if ($row) { Select-El $row }
    return [bool](Wait-Until { $d = Find-Id $win 'DetailName' 500; $d -and $d.Current.Name -eq $name } 4000)
}

# Öffnet „Bild wählen…“ für ein Spiel; der Dialog oder null
function Open-Picker($win, [string]$name) {
    if (-not (Select-Game $win $name)) { return $null }
    Invoke-El (Find-Id $win 'ChooseArtworkButton' 3000)
    return Find-Top $app.Id 'ArtworkPickerDialog' 8000
}

# Schließt den Dialog über „Abbrechen“ (oder ist er schon zu) und prüft das Ende der Sitzung (Vorschau-Threads 0)
function Close-Picker($dialog, [bool]$expectSession = $true) {
    $before = ([regex]::Matches((Read-Log $cfg), [regex]::Escape($threadsLine))).Count
    if ($dialog -and (Find-Top $app.Id 'ArtworkPickerDialog' 300)) { Invoke-El (Find-Id $dialog 'ArtworkCancelButton' 2000) }
    Check (Wait-Until { -not (Find-Top $app.Id 'ArtworkPickerDialog' 300) } 5000) 'Dialog geschlossen'
    if ($expectSession) {
        Check (Wait-Until { ([regex]::Matches((Read-Log $cfg), [regex]::Escape($threadsLine))).Count -gt $before } 5000) ('Nach dem Schließen: „{0}“' -f $threadsLine)
    }
}

# Spielwahl aufklappen und nach einem Namen suchen
function Match-Search($dialog, [string]$term) {
    $button = Find-Id $dialog 'ArtworkSgdbMatchButton' 3000
    $p = $null
    if ($button -and $button.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) -and $p.Current.ExpandCollapseState -ne 'Expanded') { [void](Expand $button $true) }
    $box = Find-Id $dialog 'ArtworkSgdbMatchSearch' 3000
    if ($box) { Set-Text $box $term }
    $go = Find-Id $dialog 'ArtworkSgdbMatchSearchButton' 2000
    if ($go) { Invoke-El $go }
}
function Match-Entries($dialog) {
    $list = Find-Id $dialog 'ArtworkSgdbMatchList' 3000
    if (-not $list) { return @() }
    $byType = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)
    @($list.FindAll([Windows.Automation.TreeScope]::Descendants, $byType))
}
function Match-Entry($dialog, [scriptblock]$filter) {
    $found = $null
    [void](Wait-Until { $script:found = @(Match-Entries $dialog | Where-Object $filter) | Select-Object -First 1; $null -ne $script:found } 8000)
    return $script:found
}

function Open-Settings {
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--settings') | Out-Null
    return Find-Top $app.Id 'SettingsWindow' 20000
}
function Close-Settings($set) {
    if ($set) { Invoke-El (Find-Id $set 'CancelSettingsButton' 2000) }
    [void](Wait-Until { -not (Find-Top $app.Id 'SettingsWindow' 300) } 5000)
}

function Image-Info([string]$file) {
    if (-not (Test-Path $file)) { return $null }
    $stream = [IO.File]::OpenRead($file)
    try {
        $frame = [Windows.Media.Imaging.BitmapDecoder]::Create($stream, 'None', 'OnLoad').Frames[0]
        $bgra = New-Object Windows.Media.Imaging.FormatConvertedBitmap($frame, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
        $stride = $bgra.PixelWidth * 4
        $pixels = New-Object byte[] ($stride * $bgra.PixelHeight)
        $bgra.CopyPixels($pixels, $stride, 0)
        # Streuung der Helligkeit über eine Stichprobe (leeres Bild: fast 0)
        $sum = 0.0; $sq = 0.0; $n = 0
        for ($i = 0; $i -lt $pixels.Length; $i += 4 * 97) {
            $l = 0.299 * $pixels[$i + 2] + 0.587 * $pixels[$i + 1] + 0.114 * $pixels[$i]
            $sum += $l; $sq += $l * $l; $n++
        }
        $mean = $sum / [Math]::Max(1, $n)
        return [pscustomobject]@{ Width = $frame.PixelWidth; Height = $frame.PixelHeight; Variance = ($sq / [Math]::Max(1, $n)) - $mean * $mean }
    } finally { $stream.Dispose() }
}

$fakeDisplay = '\\?\DISPLAY#HDRP14000#phase14-sgdb-gibt-es-nicht'
$nebel = 'HDRpilot-Prüfspiel Nebelfahrt'
$kupfer = 'HDRpilot-Prüfspiel Kupferwald'
$kupferKey = 'Steam:9990150'
$validKey = 'HDRPILOT-FIXTURE-0001'
$laterRejected = 'HDRPILOT-FIXTURE-SPAETER-ABGELEHNT'
$threadsLine = 'Prüfung: SteamGridDB-Suche beendet, Vorschau-Threads 0'
$settingsLine = 'Prüfung: Einstellungen bei SectionArtwork, Fokus SteamGridDbKey'
$cfg = New-TestConfig 'phase14-sgdb'
$seed = Join-Path $root 'tools\out\phase14-sgdb-seed'
$fixtureLog = Join-Path $cfg 'sgdb-fixture.log'
$keyFile = Join-Path $cfg 'steamgriddb-key.dat'
$indexFile = Join-Path $cfg 'art\index.json'
$customDir = Join-Path $cfg 'art\custom'
$app = $null
$previousVariable = $env:HDRPILOT_TEST_STEAMGRIDDB
try {
    $env:HDRPILOT_TEST_STEAMGRIDDB = 'fixture'

    # --- Testablage: zwei erfundene Spiele; nie echtes HDR (Bildschirm, den es nicht gibt) ---
    $manual = New-StandInGame $cfg $nebel $false
    $config = Read-Config $cfg
    $steamGame = [pscustomobject][ordered]@{
        source = 'Steam'; sourceId = '9990150'; name = $kupfer; installDir = (Join-Path $cfg "nicht-installiert\$kupfer")
        knownExecutables = @(); detectedExecutables = @(); excludedExecutables = @(); extraExecutables = @(); launchCommand = 'steam://rungameid/9990150'
        hdrAuto = $false; autoHdr = $false; restoreAfter = $true; isNew = $false; isInstalled = $false; iconCandidates = @()
    }
    $config.games = @(@($config.games) + $steamGame)
    $config.settings | Add-Member -NotePropertyName 'targetDisplays' -NotePropertyValue @($fakeDisplay) -Force
    $config.settings | Add-Member -NotePropertyName 'notifications' -NotePropertyValue $false -Force
    $config.settings | Add-Member -NotePropertyName 'hotkeyFallback' -NotePropertyValue $false -Force
    [IO.File]::WriteAllText((Join-Path $cfg 'config.json'), ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $config = Read-Config $cfg
    $safe = @($config.settings.targetDisplays).Count -eq 1 -and $config.settings.targetDisplays[0] -eq $fakeDisplay -and $config.settings.hotkeyFallback -eq $false
    Check $safe "Kein echter Bildschirm: globale Einstellung zeigt auf $fakeDisplay, Notlösung Tastenkürzel aus"
    if (-not $safe) { throw 'Testablage zeigt auf einen echten Bildschirm, Abbruch vor jedem Start' }
    $nebelKey = $manual.Key
    Note "Testablage: $nebelKey, $kupferKey (nicht installiert); Anfrageprotokoll $fixtureLog"

    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $win) 'Hauptfenster der Testinstanz geöffnet'
    if (-not $win) { throw 'Hauptfenster fehlt' }
    Check ($null -ne (Wait-Log $cfg 'Einlesen: \d+ Spiele' 90000)) 'Einlesen beim Start fertig'
    Check ((Fixture-Lines).Count -eq 0) 'Beim Start keine Anfrage an SteamGridDB'

    # --- A. Schlüssel bei ausgeschaltetem Schalter ---
    $set = Open-Settings
    Check ($null -ne $set) 'Einstellungen geöffnet'
    if (-not $set) { throw 'Einstellungen fehlen' }
    $toggle = Find-Id $set 'SteamGridDbToggle' 3000
    Check ($toggle -and (Get-Toggle $toggle) -eq 'Off') 'Schalter „Automatisch laden“ ist aus'
    Check ($null -ne (Find-Id $set 'SteamGridDbAutoLabel' 2000)) 'Sichtbare Beschriftung SteamGridDbAutoLabel vorhanden'
    $keyBox = Find-Id $set 'SteamGridDbKey' 2000
    $checkButton = Find-Id $set 'SteamGridDbCheckButton' 2000
    Check ($keyBox -and $keyBox.Current.IsEnabled -and $checkButton -and $checkButton.Current.IsEnabled) 'Schlüsselfeld und „Prüfen“ bei ausgeschaltetem Schalter bedienbar'
    if ($keyBox) { Set-Text $keyBox $validKey }
    if ($checkButton) { Invoke-El $checkButton }
    $status = $null
    Check (Wait-Until { $script:status = Find-Id $set 'SteamGridDbStatus' 500; $script:status -and (Res-Like $script:status.Current.Name 'ShellStrings' 'SteamGridDbCheckValid') } 10000) ('Status nach „Prüfen“: „{0}“' -f $(if ($script:status) { $script:status.Current.Name }))
    Check (Test-Path -LiteralPath $keyFile) 'Schlüsseldatei in der Testablage gespeichert'
    $lines = Fixture-Lines
    Check ($lines.Count -eq 1 -and $lines[0] -like '* www.steamgriddb.com /api/v2/search/autocomplete/HDRpilot schluessel=ja') ('Genau eine Anfrage: {0}' -f ($lines -join ' | '))
    Start-Sleep -Seconds 5
    Check ((Get-Toggle (Find-Id $set 'SteamGridDbToggle' 2000)) -eq 'Off' -and (Fixture-Lines).Count -eq 1) 'Schalter weiter aus, kein automatischer Lauf (5 s keine weitere Anfrage)'
    Close-Settings $set

    # --- B. Keine Anfrage vor dem Klick ---
    $count = (Fixture-Lines).Count
    $dialog = Open-Picker $win $nebel
    Check ($null -ne $dialog) ('„Bild wählen…“ für {0} geöffnet' -f $nebel)
    if (-not $dialog) { throw 'Dialog fehlt' }
    foreach ($id in 'ArtworkSgdbSection', 'ArtworkSgdbMatchButton', 'ArtworkSgdbSearchButton') { Check ($null -ne (Find-Id $dialog $id 3000)) "Dialog-Element $id vorhanden" }
    foreach ($tab in 'ArtworkTabLogo', 'ArtworkTabCover', 'ArtworkTabHero') { Select-El (Find-Id $dialog $tab 2000) }
    $matchButton = Find-Id $dialog 'ArtworkSgdbMatchButton' 2000
    Check (Expand $matchButton $true) 'Spielwahl aufgeklappt (ExpandCollapse)'
    $box = Find-Id $dialog 'ArtworkSgdbMatchSearch' 2000
    if ($box) { Set-Text $box 'Nur getippt, nicht gesucht' }
    [void](Expand $matchButton $false)
    Start-Sleep -Seconds 2
    Check ($null -ne (Find-Top $app.Id 'ArtworkPickerDialog' 500) -and (Fixture-Lines).Count -eq $count) 'Dialog offen, Reiter und Spielwahl ohne eine einzige Anfrage'

    # --- C. Suche und „Mehr laden“ (Titelbild) ---
    Invoke-El (Find-Id $dialog 'ArtworkSgdbSearchButton' 2000)
    Check (Wait-Results $dialog 24) ('Suche: {0} Ergebnisse' -f (Results $dialog).Count)
    $items = Results $dialog
    Check ($items.Count -ge 5 -and $items[0].Current.Name -eq 'Titelbild von Testautor 01, 1920 × 620 Pixel') ('Erste Kachel: „{0}“' -f $(if ($items) { $items[0].Current.Name }))
    Check ($items.Count -ge 5 -and $items[4].Current.Name -eq 'Titelbild, Autor unbekannt, 1920 × 620 Pixel') ('Fünfte Kachel: „{0}“' -f $(if ($items.Count -ge 5) { $items[4].Current.Name }))
    [void](Wait-Settled)
    $new = New-Lines $count
    $api = @($new | Where-Object { $_ -like '* www.steamgriddb.com /api/*' })
    $thumbs = @($new | Where-Object { $_ -like '* cdn2.steamgriddb.com /thumb/*' })
    Check ($api.Count -eq 2 -and @($api | Where-Object { $_ -like '*/search/autocomplete/*' }).Count -eq 1 -and @($api | Where-Object { $_ -like '*/heroes/game/9101?*page=0*' }).Count -eq 1 -and @($api | Where-Object { $_ -notlike '*schluessel=ja' }).Count -eq 0) ('Schnittstelle: eine Suche und heroes/game/9101 Seite 0, mit Schlüssel ({0})' -f ($api -join ' | '))
    Check ($thumbs.Count -eq 22 -and @($thumbs | Where-Object { $_ -notlike '*schluessel=nein' }).Count -eq 0) ("Vorschauen: $($thumbs.Count) (erwartet 22), ohne Schlüssel")
    $apiBefore = @(Fixture-Lines | Where-Object { $_ -like '* www.steamgriddb.com /api/*' }).Count
    Invoke-El (Find-Id $dialog 'ArtworkSgdbMoreButton' 3000)
    Check (Wait-Results $dialog 48) ('„Mehr laden“: {0} Ergebnisse' -f (Results $dialog).Count)
    Check (@(Fixture-Lines | Where-Object { $_ -like '* www.steamgriddb.com /api/*' }).Count -eq $apiBefore) 'Ohne neue Anfrage an die Schnittstelle (Seite 0 reicht)'
    Invoke-El (Find-Id $dialog 'ArtworkSgdbMoreButton' 3000)
    Check (Wait-Results $dialog 70) ('„Mehr laden“: {0} Ergebnisse' -f (Results $dialog).Count)
    Check (@(Fixture-Lines | Where-Object { $_ -like '*/heroes/game/9101?*page=1*' }).Count -eq 1) 'Eine neue Anfrage: Seite 1'
    Check ($null -ne (Find-Id $dialog 'ArtworkSgdbMoreButton' 2000)) '„Mehr laden“ noch da'
    Invoke-El (Find-Id $dialog 'ArtworkSgdbMoreButton' 3000)
    Check (Wait-Until { @(Fixture-Lines | Where-Object { $_ -like '*/heroes/game/9101?*page=2*' }).Count -eq 1 } 10000) 'Eine neue Anfrage: Seite 2'
    Check (Wait-Res $dialog 'LibraryStrings' 'Picker_SgdbEnd') 'Status: alle Ergebnisse gezeigt (Picker_SgdbEnd)'
    Check ((Results $dialog).Count -eq 70 -and $null -eq (Find-Id $dialog 'ArtworkSgdbMoreButton' 500)) 'Weiter 70 Ergebnisse, „Mehr laden“ weg'
    $foreign = @(Fixture-Lines | Where-Object { $_ -notmatch '^GET (www\.steamgriddb\.com|cdn2\.steamgriddb\.com) ' -or $_ -like '*http://*' })
    Check ($foreign.Count -eq 0) ('Nur www.steamgriddb.com und cdn2.steamgriddb.com, nie http ({0})' -f ($foreign -join ' | '))

    # --- D. Je Reiter ein Klick ---
    [void](Wait-Settled)
    $count = (Fixture-Lines).Count
    Select-El (Find-Id $dialog 'ArtworkTabCover' 2000)
    Check ($null -ne (Find-Id $dialog 'ArtworkSgdbSearchButton' 3000)) 'Cover: wieder „Suchen“ (bereit)'
    Start-Sleep -Seconds 2
    Check ((Fixture-Lines).Count -eq $count) 'Reiterwechsel ohne Anfrage'
    Close-Picker $dialog

    # --- E. Übernehmen (Cover) ---
    $dialog = Open-Picker $win $nebel
    Select-El (Find-Id $dialog 'ArtworkTabCover' 2000)
    Invoke-El (Find-Id $dialog 'ArtworkSgdbSearchButton' 3000)
    Check (Wait-Results $dialog 24) 'Cover: 24 Ergebnisse'
    [void](Wait-Settled)
    $count = (Fixture-Lines).Count
    Select-El (Results $dialog)[2]
    Invoke-El (Find-Id $dialog 'ArtworkApplyButton' 2000)
    Check (Wait-Until { -not (Find-Top $app.Id 'ArtworkPickerDialog' 300) } 15000) '„Übernehmen“ schließt den Dialog'
    $cover = $null
    Check (Wait-Until { $g = Find-GameConfig (Read-Config $cfg) $nebelKey; $script:cover = if ($g -and $g.artwork) { [string]$g.artwork.cover } else { $null }; $script:cover -like 'custom:*-cover.jpg' } 8000) "Ablage: artwork.cover = $cover"
    $coverFile = if ($cover) { Join-Path $customDir $cover.Substring(7) } else { $null }
    $info = if ($coverFile) { Image-Info $coverFile } else { $null }
    Check ($info -and $info.Width -eq 600 -and $info.Height -eq 900) ('Datei {0}: {1} × {2}' -f $coverFile, $(if ($info) { $info.Width }), $(if ($info) { $info.Height }))
    $full = @(New-Lines $count | Where-Object { $_ -notlike '* cdn2.steamgriddb.com /thumb/*' })
    Check ($full.Count -eq 1 -and $full[0] -like '* cdn2.steamgriddb.com /cover/fixture-3.png schluessel=nein') ('Genau ein volles Bild: {0}' -f ($full -join ' | '))
    Check (Wait-Until { $m = Find-Id $win 'StatusMessage' 500; $m -and (Res-Like $m.Current.Name 'LibraryStrings' 'Picker_Applied') } 5000) 'Statusleiste: Bilder übernommen'
    $set = Open-Settings
    $statusText = if ($set) { (Find-Id $set 'SteamGridDbStatus' 3000).Current.Name } else { '' }
    Check ($statusText -like 'Automatisches Laden aus · letzte Suche*') ('Einstellungen: „{0}“' -f $statusText)
    Close-Settings $set

    # --- F. Zurücksetzen ---
    $dialog = Open-Picker $win $nebel
    Select-El (Find-Id $dialog 'ArtworkTabCover' 2000)
    $customLabel = Res 'ArtworkStrings' 'Candidate_CustomFile'
    Check (Wait-Until { @(Get-ListItems (Find-Id $dialog 'ArtworkCandidates' 500) | Where-Object { $_.Current.Name -like "*$customLabel*" -and (Is-Selected $_) }).Count -eq 1 } 6000) ('Cover: „{0}“ gewählt (aktuell)' -f $customLabel)
    Invoke-El (Find-Id $dialog 'ArtworkResetButton' 2000)
    Invoke-El (Find-Id $dialog 'ArtworkApplyButton' 2000)
    Check (Wait-Until { -not (Find-Top $app.Id 'ArtworkPickerDialog' 300) } 8000) 'Zurücksetzen und „Übernehmen“ schließen den Dialog'
    Check (Wait-Until { $g = Find-GameConfig (Read-Config $cfg) $nebelKey; -not ($g.artwork -and $g.artwork.cover) } 6000) 'artwork.cover entfernt'
    Check (Wait-Until { -not (Test-Path -LiteralPath $coverFile) } 6000) 'Eigene Datei gelöscht'

    # --- G. Abbrechen ---
    $configBefore = Read-Shared (Join-Path $cfg 'config.json')
    $customBefore = @(Get-ChildItem $customDir -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) -join ','
    $dialog = Open-Picker $win $nebel
    Select-El (Find-Id $dialog 'ArtworkTabCover' 2000)
    Invoke-El (Find-Id $dialog 'ArtworkSgdbSearchButton' 3000)
    Check (Wait-Results $dialog 24) 'Cover: 24 Ergebnisse'
    Select-El (Results $dialog)[3]
    Close-Picker $dialog
    Start-Sleep -Seconds 1
    $customAfter = @(Get-ChildItem $customDir -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) -join ','
    Check ((Read-Shared (Join-Path $cfg 'config.json')) -eq $configBefore -and $customAfter -eq $customBefore) 'Abbrechen: Ablage und art\custom unverändert'
    Check (@(Fixture-Lines | Where-Object { $_ -like '*/cover/fixture-4.png*' }).Count -eq 0) 'Kein volles Bild fixture-4 geladen'

    # --- H. Spiel korrigieren ---
    $dialog = Open-Picker $win $nebel
    Match-Search $dialog 'OhneTreffer'
    Check (Wait-Res $dialog 'LibraryStrings' 'Picker_SgdbNoHits') 'Spielwahl „OhneTreffer“: kein Spiel gefunden'
    Match-Search $dialog $nebel
    $entry = Match-Entry $dialog { $_.Current.Name -like '*Neuauflage*' }
    Check ($null -ne $entry) 'Treffer 9102 („… Neuauflage“) in der Liste'
    $count = (Fixture-Lines).Count
    if ($entry) { Invoke-El $entry }
    Check (Wait-Until { @(New-Lines $count | Where-Object { $_ -like '*/heroes/game/9102?*page=0*' }).Count -eq 1 } 10000) 'Wahl lädt heroes/game/9102 Seite 0'
    [void](Wait-Settled)
    Invoke-El (Find-Id $dialog 'ArtworkApplyButton' 2000)
    Check (Wait-Until { -not (Find-Top $app.Id 'ArtworkPickerDialog' 300) } 8000) '„Übernehmen“ ohne Bildwechsel schließt den Dialog'
    Check (Wait-Until { $t = Read-Shared $indexFile; $t -and ($e = ($t | ConvertFrom-Json).games.$nebelKey) -and $e.steamGridDb -and $e.steamGridDb.gameId -eq 9102 } 8000) 'art\index.json: gameId 9102 gemerkt'
    Check (Wait-Until { $m = Find-Id $win 'StatusMessage' 500; $m -and (Res-Like $m.Current.Name 'LibraryStrings' 'Picker_MatchRemembered') } 5000) 'Statusleiste: Spiel gemerkt'

    $dialog = Open-Picker $win $kupfer
    Check ($null -ne $dialog) ('„Bild wählen…“ für {0} geöffnet' -f $kupfer)
    Match-Search $dialog $kupfer
    $entry = Match-Entry $dialog { $_.Current.Name -like "$kupfer*" -and $_.Current.Name -notlike '*Neuauflage*' -and $_.Current.Name -notlike "$kupfer II*" }
    if ($entry) { Invoke-El $entry }
    [void](Wait-Settled)
    Invoke-El (Find-Id $dialog 'ArtworkApplyButton' 2000)
    Check (Wait-Until { -not (Find-Top $app.Id 'ArtworkPickerDialog' 300) } 8000) 'Steam-Spiel: „Übernehmen“ schließt den Dialog'
    Start-Sleep -Seconds 1
    $t = Read-Shared $indexFile
    $kEntry = if ($t) { ($t | ConvertFrom-Json).games.$kupferKey } else { $null }
    Check (-not ($kEntry -and $kEntry.steamGridDb -and $kEntry.steamGridDb.gameId)) 'Steam-Spiel: keine gameId gemerkt (AppID genügt)'

    # --- I. Keine Bilder ---
    $dialog = Open-Picker $win $nebel
    Match-Search $dialog $nebel
    $entry = Match-Entry $dialog { $_.Current.Name -like "$nebel II*" }
    if ($entry) { Invoke-El $entry }
    Check (Wait-Res $dialog 'LibraryStrings' 'Picker_SgdbNoImages' 10000) 'Treffer 9103: keine passenden Bilder'
    Close-Picker $dialog

    # --- J. Zu viele Anfragen, langsame Antworten ---
    $dialog = Open-Picker $win $nebel
    Match-Search $dialog 'Zuviel'
    Check (Wait-Res $dialog 'LibraryStrings' 'Picker_SgdbRateLimited' 10000) '„Zuviel“: zu viele Anfragen'
    $retry = Find-Id $dialog 'ArtworkSgdbRetryButton' 1000
    Check (-not $retry -or -not $retry.Current.IsEnabled) '„Erneut versuchen“ gesperrt'
    Close-Picker $dialog
    $dialog = Open-Picker $win $nebel
    Match-Search $dialog 'Langsam'
    Check (Wait-Res $dialog 'LibraryStrings' 'Picker_SgdbSlow' 6000) '„Langsam“: nach höchstens 6 s „antwortet langsam“'
    Check (Wait-Res $dialog 'LibraryStrings' 'Picker_SgdbTimeout' 20000) '„Langsam“: nach höchstens 20 s Zeitüberschreitung'
    Close-Picker $dialog

    # --- K. Abgelehnter Schlüssel ---
    $set = Open-Settings
    Invoke-El (Find-Id $set 'SteamGridDbRemoveKeyButton' 3000)
    Check (Wait-Until { -not (Test-Path -LiteralPath $keyFile) } 5000) '„Schlüssel entfernen“ löscht die Schlüsseldatei'
    Close-Settings $set
    $dialog = Open-Picker $win $nebel
    $link = Find-Id $dialog 'ArtworkSgdbSettingsLink' 3000
    Check ($null -ne $link -and $null -eq (Find-Id $dialog 'ArtworkSgdbSearchButton' 500)) 'Ohne Schlüssel: Hinweis mit Link, kein „Suchen“'
    $logBefore = (Read-Log $cfg).Length
    if ($link) { Invoke-El $link }
    Check (Wait-Until { -not (Find-Top $app.Id 'ArtworkPickerDialog' 300) } 5000) 'Link schließt den Dialog'
    $set = Find-Top $app.Id 'SettingsWindow' 10000
    Check ($null -ne $set) 'Einstellungen öffnen sich'
    Check (Wait-Until { (Read-Log $cfg).Substring($logBefore).Contains($settingsLine) } 5000) ('Protokoll: „{0}“' -f $settingsLine)
    if ($set) {
        Set-Text (Find-Id $set 'SteamGridDbKey' 2000) $laterRejected
        Invoke-El (Find-Id $set 'SteamGridDbCheckButton' 2000)
        Check (Wait-Until { Test-Path -LiteralPath $keyFile } 10000) 'Später abgelehnter Schlüssel: „Prüfen“ speichert ihn'
        Close-Settings $set
    }
    $dialog = Open-Picker $win $nebel
    Invoke-El (Find-Id $dialog 'ArtworkSgdbSearchButton' 3000)
    Check (Wait-Res $dialog 'LibraryStrings' 'Picker_SgdbUnauthorized' 10000) 'Suche: Schlüssel abgelehnt'
    Check ($null -ne (Find-Id $dialog 'ArtworkSgdbSettingsLink' 2000)) 'Mit Link zu den Einstellungen'
    Close-Picker $dialog
    $set = Open-Settings
    $statusText = if ($set) { (Find-Id $set 'SteamGridDbStatus' 3000).Current.Name } else { '' }
    Check ((Res-Like $statusText 'ShellStrings' 'SteamGridDbStatusError') -and $statusText -like '*abgelehnt*') ('Einstellungen: „{0}“' -f $statusText)
    Close-Settings $set

    # --- L. Kein echtes Netz, keine Lecks ---
    $log = Read-Log $cfg
    $fixtureText = (Fixture-Lines) -join "`n"
    Check (-not $log.Contains('SteamGridDB nicht erreichbar')) 'Protokoll: nie „SteamGridDB nicht erreichbar“'
    Check (-not $log.Contains($validKey) -and -not $log.Contains($laterRejected) -and -not $fixtureText.Contains($validKey) -and -not $fixtureText.Contains($laterRejected)) 'Kein Schlüssel in Protokoll und Anfrageprotokoll'
    Check (-not $log.Contains('Testautor')) 'Keine Autorennamen im Protokoll'
    Check ($log.Contains('ist nicht angeschlossen') -and -not $log.Contains('HDR-Schaltung um')) 'HDR nie geschaltet'

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(15000)) 'Testinstanz beendet'

    # --- M. Bildschirmfotos: die Ablage des Laufs (Schlüssel gespeichert, Schalter aus), nur erfundene Spiele ---
    if (Test-Path $seed) { Remove-Item $seed -Recurse -Force }
    Copy-Item $cfg $seed -Recurse
    Remove-Item (Join-Path $seed 'logs') -Recurse -Force -ErrorAction SilentlyContinue
    $shots = Join-Path $ShotsDir ('lauf-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $dialogScenes = 'ohne-schluessel', 'bereit', 'titelbild', 'cover', 'logo', 'spielwahl', 'fehler', 'uebernehmen', 'klein', 'kontrast' | ForEach-Object { "hauptfenster-bildwahl-sgdb-$_" }
    $scenes = @($dialogScenes) + 'einstellungen-bilder-sgdb'
    $snap = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$seed`"", '--snapshot', "`"$shots`"", '--snapshot-scenes', ($scenes -join ','), '--snapshot-themes', 'dunkel,hell') -PassThru -Wait
    Check ($snap.ExitCode -eq 0) "Bildschirmfotos gerendert (Exit-Code $($snap.ExitCode))"
    Add-Type -AssemblyName System.Drawing
    $g = [Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
    $scale = $g.DpiX / 96.0
    $g.Dispose()
    $pngs = @(Get-ChildItem $shots -Filter '*.png' -ErrorAction SilentlyContinue)
    Check ($pngs.Count -eq 22) "22 Bilder ($($pngs.Count))"
    foreach ($scene in $scenes) {
        foreach ($theme in 'dunkel', 'hell') {
            $info = Image-Info (Join-Path $shots "$scene-$theme.png")
            $what = if ($info) { '{0}x{1}, Streuung {2:N0}' -f $info.Width, $info.Height, $info.Variance } else { 'fehlt' }
            Check ($info -and $info.Variance -gt 25) "Bild $scene $theme nicht leer ($what)"
            if ($info -and $scene -like 'hauptfenster-*') {
                Check ([Math]::Abs($info.Width / $scale - 880) -le 2) ("Bild $scene $theme 880 DIP breit ({0:N1} DIP bei Skalierung {1:N2})" -f ($info.Width / $scale), $scale)
            }
            if ($info -and $scene -like '*-klein') {
                Check ($info.Height / $scale -le 592.5) ("Bild $scene $theme höchstens 592 DIP hoch ({0:N1})" -f ($info.Height / $scale))
            }
        }
    }
    Note "Bildschirmfotos: $shots"
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"; $ok = $false }
finally {
    Stop-Started
    $env:HDRPILOT_TEST_STEAMGRIDDB = $previousVariable
}
if ($ok) { Write-Output 'PHASE14_SGDB_UI_OK'; exit 0 }
exit 1
