# Gate G8.3: Eigenes Symbol per UI-Automation an einer eigenen Testinstanz (eigene Ablage, echte Bibliothek eingelesen,
# nur Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben). Einen Dateidialog kann UI-Automation nicht bedienen:
# Mit --config-dir nimmt "Symbol ändern…" die Datei aus HDRPILOT_TEST_ICON_FILE (AppServices.TestIconFileVariable).
#   - Großes Symbol im Detailbereich öffnet das Menü; "Symbol zurücksetzen" ist ohne eigenes Symbol gesperrt
#   - "Symbol ändern…" legt <Ablage>\icons\<Name>.png mit 96 x 96 px an und trägt customIcon in config.json ein
#   - danach ist "Symbol zurücksetzen" frei; es entfernt Eintrag und Datei wieder
#   - die Liste hat beim Anzeigen die automatische Wahl gemerkt (icon-cache.json in der Ablage)
# Schaltet kein HDR, startet kein Spiel.
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
Add-Type -AssemblyName System.Drawing
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

# Menü-Einträge liegen in einem eigenen Popup-Fenster derselben Prozess-ID
function Find-MenuItem([int]$processId, [string]$automationId, [int]$timeoutMs = 5000) {
    $byPid = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $byId = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        foreach ($top in $UiaRoot.FindAll([Windows.Automation.TreeScope]::Children, $byPid)) {
            $e = $top.FindFirst([Windows.Automation.TreeScope]::Descendants, $byId)
            if ($e) { return $e }
        }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

function Open-IconMenu($win) {
    $button = Find-Id $win 'DetailIconButton' 3000
    if (-not $button) { return $false }
    Invoke-El $button
    Start-Sleep -Milliseconds 400
    return $true
}

function Wait-Until([scriptblock]$condition, [int]$timeoutMs = 8000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return [bool](& $condition)
}

function Image-Size([string]$path) {
    $stream = New-Object IO.MemoryStream(, [IO.File]::ReadAllBytes($path))
    try { $img = [Drawing.Image]::FromStream($stream); try { return "{0}x{1}" -f $img.Width, $img.Height } finally { $img.Dispose() } }
    finally { $stream.Dispose() }
}

$cfg = New-TestConfig 'phase8-icons' -Import
try {
    # Testbild 200 x 120 (wird auf ein 96-px-Quadrat eingepasst)
    $source = Join-Path $cfg 'testbild.png'
    $bmp = New-Object Drawing.Bitmap 200, 120
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.Clear([Drawing.Color]::FromArgb(255, 30, 144, 255)); $g.FillEllipse([Drawing.Brushes]::Gold, 60, 10, 80, 100)
    $g.Dispose(); $bmp.Save($source, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

    $env:HDRPILOT_TEST_ICON_FILE = $source
    $app = Start-App $cfg @('--show')
    Remove-Item Env:\HDRPILOT_TEST_ICON_FILE
    $win = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $win) 'Hauptfenster der Testinstanz geöffnet'
    if (-not $win) { throw 'Hauptfenster fehlt' }

    $list = Find-Id $win 'GameList' 8000
    $search = Find-Id $win 'SearchBox'
    Set-Text $search 'Gears of War'
    Start-Sleep -Milliseconds 800
    $item = Get-ListItems $list | Select-Object -First 1
    if (-not $item) { Set-Text $search ''; Start-Sleep -Milliseconds 800; $item = Get-ListItems $list | Select-Object -First 1 }
    Check ($null -ne $item) "Spiel in der Liste: $($item.Current.Name)"
    if (-not $item) { throw 'kein Spiel in der Liste' }
    Select-El $item
    $name = $item.Current.Name
    $game = @((Read-Config $cfg).games) | Where-Object { $name -like "*$($_.name)*" } | Sort-Object { $_.name.Length } -Descending | Select-Object -First 1
    $key = "$($game.source):$($game.sourceId)"
    Note "gewählt: $name ($key)"

    Check (Wait-Until { Test-Path (Join-Path $cfg 'icon-cache.json') }) 'Automatische Symbolwahl gemerkt (icon-cache.json in der Testablage)'

    # Menü über das große Symbol: "Symbol zurücksetzen" gesperrt
    Check (Open-IconMenu $win) 'Großes Symbol (DetailIconButton) vorhanden und ausgelöst'
    $change = Find-MenuItem $app.Id 'ChangeIconMenuItem'
    $reset = Find-MenuItem $app.Id 'ResetIconMenuItem'
    Check ($null -ne $change -and $null -ne $reset) 'Menü mit ChangeIconMenuItem und ResetIconMenuItem offen'
    if ($reset) { Check (-not $reset.Current.IsEnabled) 'Symbol zurücksetzen ohne eigenes Symbol gesperrt' }
    if ($change) { Check ($change.Current.Name -eq 'Symbol ändern…') "Eintrag heißt '$($change.Current.Name)'" }

    # Symbol ändern
    if ($change) { Invoke-El $change }
    $saved = Wait-Until { $c = Find-GameConfig (Read-Config $cfg) $key; $null -ne $c.customIcon }
    $custom = (Find-GameConfig (Read-Config $cfg) $key).customIcon
    Check $saved "config.json: customIcon = '$custom'"
    $png = if ($custom) { Join-Path (Join-Path $cfg 'icons') $custom } else { $null }
    Check ($png -and (Test-Path $png)) "PNG im Symbolordner der Testablage: $png"
    if ($png -and (Test-Path $png)) { $size = Image-Size $png; Check ($size -eq '96x96') "PNG ist 96 x 96 px ($size)" }
    $status = Find-Id $win 'StatusMessage' 3000
    Check ($status -and (Get-Text $status) -match 'zeigt jetzt dein Symbol') "Hinweis: $(if ($status) { Get-Text $status })"

    # Symbol zurücksetzen
    Check (Open-IconMenu $win) 'Menü erneut geöffnet'
    $reset = Find-MenuItem $app.Id 'ResetIconMenuItem'
    Check ($reset -and $reset.Current.IsEnabled) 'Symbol zurücksetzen mit eigenem Symbol frei'
    if ($reset) { Invoke-El $reset }
    $cleared = Wait-Until { $null -eq (Find-GameConfig (Read-Config $cfg) $key).customIcon }
    Check $cleared 'config.json: customIcon entfernt'
    Check ($png -and -not (Test-Path $png)) 'PNG gelöscht'

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally { Remove-Item Env:\HDRPILOT_TEST_ICON_FILE -ErrorAction SilentlyContinue; Stop-Started }
if ($ok) { Write-Output 'PHASE8_ICONS_OK'; exit 0 }
exit 1
