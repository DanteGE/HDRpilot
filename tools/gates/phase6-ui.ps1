# Gate G6.1: Feedback zur Bibliothek per UI-Automation an einer eigenen Testinstanz (eigene Ablage, echte Bibliothek
# eingelesen, nur Fenster dieser Prozess-ID). Schaltet kein HDR, startet kein Spiel, fasst die Zwischenablage nicht an.
#   - Starten-Bereich steht über dem Schalter "HDR automatisch"
#   - Knopf "Steam-Startoption kopieren" nur bei Steam-Spielen
#   - Sortierung "Zuletzt gespielt": Reihenfolge nach LastPlayed, Auswahl gespeichert und nach Neustart wieder da
#   - Trenner zwischen den Spalten vorhanden; bei schmalem Fenster bleiben Liste und Details vollständig sichtbar
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }
function Rect($el) { $el.Current.BoundingRectangle }

$cfg = New-TestConfig 'phase6-ui' -Import
try {
    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $win) 'Hauptfenster der Testinstanz geöffnet'
    if (-not $win) { throw 'Hauptfenster fehlt' }
    foreach ($id in 'SortOrder', 'LibrarySplitter', 'GameList', 'SearchBox') { Check ($null -ne (Find-Id $win $id 8000)) "Element $id vorhanden" }

    $search = Find-Id $win 'SearchBox'
    $list = Find-Id $win 'GameList'

    # Steam-Spiel: Starten über Automatik, Steam-Knopf sichtbar
    Set-Text $search 'Dying Light: The Beast'
    Start-Sleep -Milliseconds 800
    $item = Get-ListItems $list | Where-Object { $_.Current.Name -match 'Dying Light: The Beast' } | Select-Object -First 1
    Check ($null -ne $item) 'Steam-Spiel gefunden'
    if ($item) {
        Select-El $item
        $launch = Find-Id $win 'LaunchButton' 3000
        $auto = Find-Id $win 'DetailHdrAuto' 3000
        Check ($launch -and $auto -and (Rect $launch).Top -lt (Rect $auto).Top) ("Starten steht über der Automatik (Start {0:N0}, Automatik {1:N0})" -f (Rect $launch).Top, (Rect $auto).Top)
        $steam = Find-Id $win 'CopySteamLaunchOptionButton' 3000
        Check ($null -ne $steam -and -not $steam.Current.IsOffscreen -and $steam.Current.IsEnabled) 'Steam-Knopf bei Steam-Spiel sichtbar'
    }

    # GOG-Spiel: kein Steam-Knopf
    Set-Text $search 'Cyberpunk 2077'
    Start-Sleep -Milliseconds 800
    $item = Get-ListItems $list | Where-Object { $_.Current.Name -match 'Cyberpunk 2077' } | Select-Object -First 1
    if ($item) {
        Select-El $item
        Start-Sleep -Milliseconds 400
        $steam = Find-Id $win 'CopySteamLaunchOptionButton' 1500
        Check ($null -eq $steam -or $steam.Current.IsOffscreen) 'Kein Steam-Knopf bei GOG-Spiel'
    } else { Check $false 'GOG-Spiel gefunden' }
    Set-Text $search ''
    Start-Sleep -Milliseconds 600

    # Sortierung "Zuletzt gespielt"
    $sort = Find-Id $win 'SortOrder'
    $ec = $null
    if ($sort.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$ec)) { $ec.Expand() }
    # Die Einträge entstehen erst mit dem Aufklappen: bis zu 3 s darauf warten statt fest 400 ms
    $entries = @(); $byLast = $null
    for ($try = 0; $try -lt 30 -and -not $byLast; $try++) {
        Start-Sleep -Milliseconds 100
        $entries = @($sort.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))))
        $byLast = $entries | Where-Object { $_.Current.Name -match 'Zuletzt gespielt' } | Select-Object -First 1
    }
    Check ($null -ne $byLast) "Sortierung bietet 'Zuletzt gespielt' an ($(($entries | ForEach-Object { $_.Current.Name }) -join ', '))"
    if ($byLast) {
        Select-El $byLast
        if ($ec) { try { $ec.Collapse() } catch {} }
        Start-Sleep -Milliseconds 1200
        $config = Read-Config $cfg
        Check ($config.settings.librarySortOrder -match 'LastPlayed') "Sortierung gespeichert ($($config.settings.librarySortOrder))"
        $byName = @{}
        foreach ($g in @($config.games)) { $byName[$g.name] = $g.lastPlayed }
        $names = @(Get-ListItems (Find-Id $win 'GameList') | Select-Object -First 8 | ForEach-Object { $_.Current.Name })
        $times = @(foreach ($n in $names) { $k = $byName.Keys | Where-Object { $n -like "*$_*" } | Sort-Object Length -Descending | Select-Object -First 1; if ($k -and $byName[$k]) { [datetimeoffset]$byName[$k] } else { $null } })
        $withTime = @($times | Where-Object { $null -ne $_ })
        $sorted = $true
        for ($i = 1; $i -lt $withTime.Count; $i++) { if ($withTime[$i] -gt $withTime[$i - 1]) { $sorted = $false } }
        Check ($withTime.Count -ge 3 -and $sorted -and $null -ne $times[0]) ("Oberste Spiele nach zuletzt gespielt geordnet: " + (($names | Select-Object -First 4) -join ' > '))
    }

    # Schmales Fenster: Liste und Details bleiben im Fenster
    $tp = $null
    if ($win.TryGetCurrentPattern([Windows.Automation.TransformPattern]::Pattern, [ref]$tp) -and $tp.Current.CanResize) {
        $tp.Resize(1000, 700)
        Start-Sleep -Milliseconds 800
        $w = Rect $win; $l = Rect (Find-Id $win 'GameList'); $d = Rect (Find-Id $win 'DetailsPanel' 3000); $s = Rect (Find-Id $win 'LibrarySplitter')
        Note ("Fenster {0:N0} px: Liste {1:N0}-{2:N0}, Trenner {3:N0}, Details {4:N0}-{5:N0}" -f $w.Width, $l.Left, $l.Right, $s.Left, $d.Left, $d.Right)
        Check ($l.Right -le $s.Left + 2 -and $s.Right -le $d.Left + 2 -and $d.Right -le $w.Right + 1 -and $l.Width -ge 300 -and $d.Width -ge 300) 'Bei 1000 px: Liste | Trenner | Details nebeneinander und vollständig im Fenster'
    } else { Check $false 'Fenster lässt sich per UI-Automation in der Größe ändern' }

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'

    # Neustart: Sortierung noch da?
    $app2 = Start-App $cfg @('--show')
    $win2 = Find-Window 'MainWindow' $app2.Id
    if ($win2) {
        $sort2 = Find-Id $win2 'SortOrder'
        $sel = $null
        $text = if ($sort2.TryGetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern, [ref]$sel)) { ($sel.Current.GetSelection() | ForEach-Object { $_.Current.Name }) -join '' } else { $sort2.Current.Name }
        Check ($text -match 'Zuletzt gespielt') "Sortierung nach Neustart: '$text'"
    } else { Check $false 'Hauptfenster nach Neustart' }
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally { Stop-Started }
if ($ok) { Write-Output 'PHASE6_UI_OK'; exit 0 }
exit 1
