# Gate Phase 4: Oberfläche per UI-Automation bedienen (eigene Test-Ablage, echte Bibliothek eingelesen).
#   PHASE4_UI_OK       Hauptfenster, Suche, Filter, Auswahl, Detail-Schalter, Liste-Schalter, Einstellungen bedienbar
#                      (auch Neustart-Puffer in Minuten und Sekunden, G7.6/E85: 30:05 wird 30:00, 0:45 gespeichert)
#   PHASE4_PERSIST_OK  geänderte Einstellungen überleben einen Neustart der App
# Schaltet kein HDR und startet kein Spiel.
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true; $persistOk = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

$cfg = New-TestConfig 'phase4-ui' -Import
$gameKey = 'Gog:1423049311'   # Cyberpunk 2077 (GOG)
try {
    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $win) 'Hauptfenster geöffnet'
    if (-not $win) { throw 'Hauptfenster fehlt' }

    foreach ($id in 'HdrStatus', 'ToggleHdrButton', 'PauseToggle', 'SettingsButton', 'SearchBox', 'SourceFilter', 'OnlySelectedToggle', 'OnlyNewToggle', 'RescanButton', 'AddGameButton', 'MarkAllSeenButton', 'GameList') {
        Check ($null -ne (Find-Id $win $id 8000)) "Element $id vorhanden"
    }
    Note ("HdrStatus: " + (Get-Text (Find-Id $win 'HdrStatus')))

    # Suche
    $search = Find-Id $win 'SearchBox'
    Set-Text $search 'Cyberpunk'
    Start-Sleep -Milliseconds 800
    $list = Find-Id $win 'GameList'
    $items = Get-ListItems $list
    Check ($items.Count -ge 1 -and ($items | Where-Object { $_.Current.Name -match 'Cyberpunk 2077' })) "Suche 'Cyberpunk' zeigt Cyberpunk 2077 ($($items.Count) Treffer)"
    Check (-not ($items | Where-Object { $_.Current.Name -notmatch 'Cyberpunk' })) 'Suche filtert andere Spiele aus'

    # Auswahl und Detail-Schalter
    $item = $items | Where-Object { $_.Current.Name -match 'Cyberpunk 2077' } | Select-Object -First 1
    Select-El $item
    $details = Find-Id $win 'DetailsPanel'
    Check ($null -ne $details) 'Detailbereich sichtbar'
    Check ((Get-Text (Find-Id $win 'DetailName')) -match 'Cyberpunk') 'Detailbereich zeigt das Spiel'
    foreach ($id in 'DetailHdrAuto', 'DetailDisplay', 'DetailRestore', 'DetailExecutables', 'AddExeButton', 'RemoveExeButton', 'RelearnExeButton', 'LaunchButton', 'CreateDesktopShortcutButton', 'CreateStartMenuShortcutButton') {
        Check ($null -ne (Find-Id $win $id 3000)) "Detail-Element $id vorhanden"
    }
    $before = (Find-GameConfig (Read-Config $cfg) $gameKey).hdrAuto
    $state = Toggle-El (Find-Id $win 'DetailHdrAuto')
    Start-Sleep -Milliseconds 700
    $after = (Find-GameConfig (Read-Config $cfg) $gameKey)
    Check ($after.hdrAuto -ne $before) "Detail-Schalter HDR automatisch schreibt Konfiguration ($before -> $($after.hdrAuto))"
    Check ($after.isNew -eq $false) 'Ansehen/Schalten setzt "neu" zurück'
    $restoreBefore = $after.restoreAfter
    [void](Toggle-El (Find-Id $win 'DetailRestore'))
    Start-Sleep -Milliseconds 700
    Check ((Find-GameConfig (Read-Config $cfg) $gameKey).restoreAfter -ne $restoreBefore) 'Detail-Schalter Zurückstellen schreibt Konfiguration'
    [void](Toggle-El (Find-Id $win 'DetailRestore'))

    # Schalter direkt in der Liste
    $rowToggle = Find-Id $item 'HdrAutoToggle'
    Check ($null -ne $rowToggle) 'Schalter in der Listenzeile vorhanden'
    if ($rowToggle) {
        $b = (Find-GameConfig (Read-Config $cfg) $gameKey).hdrAuto
        [void](Toggle-El $rowToggle)
        Start-Sleep -Milliseconds 700
        Check ((Find-GameConfig (Read-Config $cfg) $gameKey).hdrAuto -ne $b) 'Listen-Schalter schreibt Konfiguration'
    }

    # Filter nach Quelle
    Set-Text $search ''
    Start-Sleep -Milliseconds 600
    $filter = Find-Id $win 'SourceFilter'
    $ec = $null
    if ($filter.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$ec)) { $ec.Expand(); Start-Sleep -Milliseconds 400 }
    $entries = @($filter.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))))
    $gog = $entries | Where-Object { $_.Current.Name -match 'GOG' } | Select-Object -First 1
    Check ($null -ne $gog) "Quellenfilter bietet GOG an ($($entries.Count) Einträge: $(($entries | ForEach-Object { $_.Current.Name }) -join ', '))"
    if ($gog) {
        Select-El $gog
        if ($ec) { try { $ec.Collapse() } catch {} }
        Start-Sleep -Milliseconds 800
        $names = Get-ListItems (Find-Id $win 'GameList') | ForEach-Object { $_.Current.Name }
        Check ($names.Count -eq 5) "Filter GOG zeigt 5 Spiele ($($names -join ', '))"
    }

    # Einstellungen
    Invoke-El (Find-Id $win 'SettingsButton')
    $set = Find-Window 'SettingsWindow' $app.Id 10000
    if (-not $set) { $set = Find-Id $win 'SettingsWindow' 3000 }
    Check ($null -ne $set) 'Einstellungen geöffnet'
    if ($set) {
        foreach ($id in 'TargetDisplays', 'PollInterval', 'GracePeriod', 'RestartBufferMinutes', 'RestartBufferSeconds', 'LaunchTimeout', 'AutostartToggle', 'NotificationsToggle', 'HotkeyFallbackToggle', 'SearchFolders', 'AddSearchFolderButton', 'RemoveSearchFolderButton', 'LogLevel', 'HelperPatterns', 'ResetHelperPatternsButton', 'OpenLogFolderButton', 'SaveSettingsButton', 'CancelSettingsButton') {
            Check ($null -ne (Find-Id $set $id 3000)) "Einstellung $id vorhanden"
        }
        Set-Range (Find-Id $set 'GracePeriod') 15
        Set-Range (Find-Id $set 'PollInterval') 750

        # Neustart-Puffer (G7.6): Die Testablage startet mit 0:00 (TestConfigSeed). 30:05 gibt es nicht (höchstens
        # 30:00), danach 0:45 einstellen und speichern.
        $bufMin = Find-Id $set 'RestartBufferMinutes'
        $bufSec = Find-Id $set 'RestartBufferSeconds'
        Check ((Get-Range $bufMin) -eq 0 -and (Get-Range $bufSec) -eq 0) ("Neustart-Puffer aus der Testablage: {0}:{1:00}" -f (Get-Range $bufMin), (Get-Range $bufSec))
        Set-Range $bufMin 30
        Set-Range $bufSec 5
        Start-Sleep -Milliseconds 300
        Check ((Get-Range $bufMin) -eq 30 -and (Get-Range $bufSec) -eq 0) ("Neustart-Puffer 30:05 auf 30:00 begrenzt ({0}:{1:00})" -f (Get-Range $bufMin), (Get-Range $bufSec))
        Set-Range $bufMin 0
        Set-Range $bufSec 45
        Start-Sleep -Milliseconds 300
        $pill = $set.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, '0:45 min')))
        Check ($null -ne $pill) 'Neustart-Puffer zeigt 0:45 min'

        $notif = Find-Id $set 'NotificationsToggle'
        if ((Get-Toggle $notif) -eq 'On') { [void](Toggle-El $notif) }
        Invoke-El (Find-Id $set 'SaveSettingsButton')
        Start-Sleep -Milliseconds 800
        $s = (Read-Config $cfg).settings
        Check ($s.gracePeriodSeconds -eq 15) "Nachlaufzeit gespeichert ($($s.gracePeriodSeconds))"
        Check ($s.pollIntervalMs -eq 750) "Abfrageintervall gespeichert ($($s.pollIntervalMs))"
        Check ($s.restartBufferSeconds -eq 45) "Neustart-Puffer 0:45 als restartBufferSeconds gespeichert ($($s.restartBufferSeconds))"
        Check ($s.notifications -eq $false) 'Benachrichtigungen aus gespeichert'
    }

    # Pause über weitergereichten Befehl, dann Beenden über zweiten Aufruf
    $p2 = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--pause') -PassThru -Wait
    Check ($p2.ExitCode -eq 0 -and (Wait-Log $cfg 'Automatik pausiert' 5000)) 'Zweiter Aufruf --pause wird weitergereicht'
    $p3 = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -PassThru -Wait
    Check ($app.WaitForExit(10000)) 'Zweiter Aufruf --exit beendet die laufende Instanz'

    # Neustart: Einstellungen noch da?
    $app2 = Start-App $cfg @('--settings')
    $set2 = Find-Window 'SettingsWindow' $app2.Id 15000
    if ($set2) {
        $g = Get-Range (Find-Id $set2 'GracePeriod')
        $pi = Get-Range (Find-Id $set2 'PollInterval')
        $n = Get-Toggle (Find-Id $set2 'NotificationsToggle')
        $bm = Get-Range (Find-Id $set2 'RestartBufferMinutes')
        $bs = Get-Range (Find-Id $set2 'RestartBufferSeconds')
        if ($g -ne 15 -or $pi -ne 750 -or $n -ne 'Off' -or $bm -ne 0 -or $bs -ne 45) { $persistOk = $false }
        Note ("Nach Neustart: Nachlaufzeit=$g, Intervall=$pi, Benachrichtigungen=$n, Neustart-Puffer={0}:{1:00}" -f $bm, $bs)
        Invoke-El (Find-Id $set2 'CancelSettingsButton')
    } else { $persistOk = $false; Note 'FEHLER Einstellungen nach Neustart nicht geöffnet' }
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    $app2.WaitForExit(10000) | Out-Null
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false; $persistOk = $false }
finally { Stop-Started }

if ($ok) { Write-Output 'PHASE4_UI_OK' }
if ($persistOk -and $ok) { Write-Output 'PHASE4_PERSIST_OK' }
if ($ok -and $persistOk) { exit 0 } else { exit 1 }
