# Gate G9.1 (Oberfläche) und G9.4 (Einstellungen): „Windows Auto HDR“ per UI-Automation an einer eigenen Testinstanz
# (eigene Ablage mit drei Stellvertreter-Spielen, die nie starten; nur Fenster dieser Prozess-ID; keine Maus- oder
# Tastatureingaben). Auto HDR schreibt dabei nur in die Testwurzel HKCU\Software\HDRpilot.Tests\AutoHdr\Phase9Ui-<PID>
# (--autohdr-test-root); den echten Schlüssel HKCU\Software\Microsoft\DirectX\UserGpuPreferences liest das Skript nur
# und prüft am Ende, dass er Byte für Byte gleich geblieben ist.
#   - Abschnitt „BILD“ unter „HDR automatisch“, Schalter DetailAutoHdr schreibt autoHdr und lässt hdrAuto stehen
#   - Hinweis „HDR automatisch aus“ folgt hdrAuto, nicht autoHdr; Hinweis auf eigenes HDR (Native/Limited) nur dort,
#     Schalter dort trotzdem bedienbar
#   - Kennzeichen AUTO HDR nur in der Zeile des Spiels, jede Zeile hat genau einen Schalter (HdrAutoToggle)
#   - Einstellungen: Notlösung Standard aus, Abbrechen verwirft, Speichern schreibt; „Auto-HDR-Einträge zurücknehmen“
#     meldet ein Ergebnis
#   - Registry der Testwurzel (G9.2/G9.4 im Zusammenspiel): Einschalten trägt AutoHDREnable=2097 ein und lässt
#     „GpuPreference=2;“ stehen, Ausschalten und „zurücknehmen“ stellen genau den alten Wert her, ein vorher fehlender
#     Wert verschwindet wieder, der globale Wert bleibt ohne laufendes Spiel unberührt
# -UiOnly: nur die Oberfläche (für einen Stand ohne Auto-HDR-Automatik); die Registry-Prüfungen der Einträge je Spiel
# werden dann nur notiert und das Skript meldet PHASE9_AUTOHDR_UI_PARTIAL_OK statt PHASE9_AUTOHDR_UI_OK.
# Schaltet kein HDR, startet kein Spiel.
param([switch]$UiOnly)
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }
# Prüfung, die die Automatik von Auto HDR braucht: mit -UiOnly nur notiert
function EngineCheck([bool]$cond, [string]$what) {
    if ($UiOnly) { Note ("note $what (ohne Automatik nicht geprüft: {0})" -f $(if ($cond) { 'wäre ok' } else { 'nicht erfüllt' })) } else { Check $cond $what }
}

function Wait-Until([scriptblock]$condition, [int]$timeoutMs = 8000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return [bool](& $condition)
}

function Shown($el) { try { return $null -ne $el -and -not $el.Current.IsOffscreen } catch { return $false } }

# --- Registry: nur lesen (echter Schlüssel) oder Testwurzel ---
$realSub = 'Software\Microsoft\DirectX\UserGpuPreferences'
$testName = "Phase9Ui-$PID"
$testSub = "Software\HDRpilot.Tests\AutoHdr\$testName"
$globalName = 'DirectXUserGlobalSettings'
$globalSeed = 'AutoHDREnable=0;SwapEffectUpgradeEnable=1;'

function Snapshot-Key([string]$sub) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub, $false)
    if (-not $key) { return '<Schlüssel fehlt>' }
    try {
        $lines = foreach ($n in ($key.GetValueNames() | Sort-Object)) {
            $kind = $key.GetValueKind($n)
            $v = $key.GetValue($n, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($v -is [byte[]]) { $v = [Convert]::ToBase64String($v) } elseif ($v -is [string[]]) { $v = $v -join '|' }
            "$n|$kind|$v"
        }
        return ($lines -join "`n")
    } finally { $key.Close() }
}

function Get-TestValue([string]$name) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testSub, $false)
    if (-not $key) { return $null }
    try { return $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } finally { $key.Close() }
}

function Set-TestValue([string]$name, [string]$value) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($testSub)
    try { $key.SetValue($name, $value, [Microsoft.Win32.RegistryValueKind]::String) } finally { $key.Close() }
}

function Show([object]$v) { if ($null -eq $v) { '<fehlt>' } else { "'$v'" } }

# --- Bibliothek ---
function Find-Row($win, [string]$name, [int]$timeoutMs = 5000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do {
        $row = Get-ListItems (Find-Id $win 'GameList' 8000) | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1
        if ($row) { return $row }
        Start-Sleep -Milliseconds 150
    } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
    return $null
}

function Select-Game($win, [string]$name) {
    $row = Find-Row $win $name
    if ($row) { Select-El $row; Start-Sleep -Milliseconds 300 }
    return $row
}

function Badge-Shown($row) { return Shown ($(if ($row) { Find-Id $row 'AutoHdrBadge' 1000 })) }

# Detailbereich so weit scrollen, dass der Schalter „Windows Auto HDR“ samt Hinweisen darunter zu sehen ist
function Show-Picture($win) {
    $panel = Find-Id $win 'DetailsPanel'
    $sp = $null
    if (-not $panel -or -not $panel.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern, [ref]$sp) -or -not $sp.Current.VerticallyScrollable) { return }
    for ($p = 0; $p -le 100; $p += 5) {
        $sp.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $p)
        Start-Sleep -Milliseconds 120
        $switch = Find-Id $win 'DetailAutoHdr' 500
        if (Shown $switch) {
            $r = $switch.Current.BoundingRectangle; $pr = $panel.Current.BoundingRectangle
            if ($r.Bottom + 220 -le $pr.Bottom) { return }
        }
    }
}

function Hint-Shown($win, [string]$id) { Show-Picture $win; return Shown (Find-Id $win $id 1000) }

# Einstellungen öffnen; das Fenster hängt je nach Besitzer unter dem Desktop oder unter dem Hauptfenster (wie phase4-ui)
function Open-Settings($win, [int]$processId) {
    Invoke-El (Find-Id $win 'SettingsButton')
    $set = Find-Window 'SettingsWindow' $processId 5000
    if (-not $set) { $set = Find-Id $win 'SettingsWindow' 5000 }
    return $set
}

$before = Snapshot-Key $realSub
Note ("Echter Schlüssel vorher (nur gelesen): {0} Werte" -f @($before -split "`n" | Where-Object { $_ -and $_ -notlike '<*' }).Count)
$cfg = New-TestConfig 'phase9-autohdr'
try {
    # Drei Stellvertreter-Spiele mit HDR automatisch aus; sie werden nie gestartet
    $a = New-StandInGame $cfg 'Auto HDR Test' $false
    $b = New-StandInGame $cfg 'Natives HDR Test' $false
    $c = New-StandInGame $cfg 'Teilweise HDR Test' $false
    $config = Read-Config $cfg
    $exeA = @((Find-GameConfig $config $a.Key).knownExecutables)[0]
    $exeB = @((Find-GameConfig $config $b.Key).knownExecutables)[0]
    Check ($exeA -and $exeB -and $a.Key -ne $b.Key) "Testspiele angelegt: $($a.Key) ($exeA), $($b.Key), $($c.Key)"

    # HDR laut PCGamingWiki (Zwischenspeicher der Testablage): B eigenes HDR, C eingeschränkt, A unbekannt
    $support = '{ "version": 1, "games": { "' + $b.Key + '": { "status": "Native" }, "' + $c.Key + '": { "status": "Limited" } } }'
    [IO.File]::WriteAllText((Join-Path $cfg 'hdr-support.json'), $support, (New-Object Text.UTF8Encoding $false))

    # Testwurzel vorbelegen: A hat schon einen Windows-Eintrag, B keinen; globaler Wert wie auf dem Testrechner
    Set-TestValue $exeA 'GpuPreference=2;'
    Set-TestValue $globalName $globalSeed

    $app = Start-App $cfg @('--show', '--autohdr-test-root', $testName)
    $win = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $win) 'Hauptfenster der Testinstanz geöffnet'
    if (-not $win) { throw 'Hauptfenster fehlt' }

    # Die Instanz liest beim Start auch die echten Launcher ein (nur lesend): Suche auf die drei Testspiele eingrenzen
    Set-Text (Find-Id $win 'SearchBox' 8000) 'HDR Test'
    Start-Sleep -Milliseconds 800

    # --- Spiel A: ohne eigenes HDR, HDR automatisch aus ---
    $rowA = Select-Game $win 'Auto HDR Test'
    $rowB = Find-Row $win 'Natives HDR Test'
    Check ($null -ne $rowA -and $null -ne $rowB) ("Spiele A und B in der Liste ({0})" -f ((Get-ListItems (Find-Id $win 'GameList')) | ForEach-Object { $_.Current.Name }) -join ', ')
    $toggle = Find-Id $win 'DetailAutoHdr' 5000
    Check ($null -ne $toggle -and $toggle.Current.IsEnabled -and (Get-Toggle $toggle) -eq 'Off') 'A: Schalter DetailAutoHdr vorhanden, bedienbar, aus'
    Check ($toggle -and $toggle.Current.Name -eq 'Windows Auto HDR') "A: Schalter heißt '$($toggle.Current.Name)'"
    Check (Hint-Shown $win 'DetailAutoHdrNeedsHdrHint') 'A: Hinweis „HDR automatisch aus“ sichtbar'
    Check (-not (Hint-Shown $win 'DetailAutoHdrNativeHint')) 'A: kein Hinweis auf eigenes HDR (laut Wiki unbekannt)'
    Check (-not (Hint-Shown $win 'DetailAutoHdrStatus')) 'A: kein Stand, solange aus'
    Check (-not (Badge-Shown $rowA)) 'A: kein Kennzeichen AUTO HDR in der Zeile'

    [void](Toggle-El $toggle)
    $on = Wait-Until { $g = Find-GameConfig (Read-Config $cfg) $a.Key; $g.autoHdr -eq $true }
    $g = Find-GameConfig (Read-Config $cfg) $a.Key
    Check ($on -and $g.hdrAuto -eq $false) "A eingeschaltet: config.json autoHdr=$($g.autoHdr), hdrAuto=$($g.hdrAuto)"
    Check (Wait-Until { Badge-Shown (Find-Row $win 'Auto HDR Test') }) 'A: Kennzeichen AUTO HDR erscheint in seiner Zeile'
    Check (-not (Badge-Shown (Find-Row $win 'Natives HDR Test'))) 'B: kein Kennzeichen (Gegenprobe)'
    # Im ganzen Fenster genau ein Kennzeichen, und das in der Zeile von A (keine Spalte, nichts im Kopf der Liste)
    $idBadge = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'AutoHdrBadge')
    $badges = @($win.FindAll([Windows.Automation.TreeScope]::Descendants, $idBadge) | Where-Object { Shown $_ })
    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
    $owners = @($badges | ForEach-Object { $up = $walker.GetParent($_); while ($up -and $up.Current.ControlType -ne [Windows.Automation.ControlType]::ListItem) { $up = $walker.GetParent($up) }; if ($up) { $up.Current.Name } })
    Check ($badges.Count -eq 1 -and $owners.Count -eq 1 -and $owners[0] -eq 'Auto HDR Test') ("Sichtbare Kennzeichen: {0}, in den Zeilen: {1}" -f $badges.Count, ($owners -join ', '))
    Check ($badges.Count -eq 1 -and $badges[0].Current.Name -eq 'Windows Auto HDR für dieses Spiel an') "Kennzeichen heißt '$(if ($badges.Count) { $badges[0].Current.Name })'"
    $statusShown = Hint-Shown $win 'DetailAutoHdrStatus'
    $status = Find-Id $win 'DetailAutoHdrStatus' 1000
    Check ($statusShown -and $status.Current.Name) "A: Stand sichtbar: '$(if ($status) { $status.Current.Name })'"
    $written = Wait-Until { (Get-TestValue $exeA) -ceq 'GpuPreference=2;AutoHDREnable=2097;' }
    EngineCheck $written ("Testwurzel: A eingetragen, vorhandenes Token bleibt ({0})" -f (Show (Get-TestValue $exeA)))
    EngineCheck (Test-Path (Join-Path $cfg 'autohdr-state.json')) 'autohdr-state.json in der Testablage'
    if (-not $UiOnly) {
        Check (Wait-Until { (Get-Text (Find-Id $win 'DetailAutoHdrStatus' 1000)) -match 'eingetragen' }) "A: Stand nennt den Eintrag: '$(Get-Text (Find-Id $win 'DetailAutoHdrStatus' 1000))'"
    }

    # Hinweis folgt „HDR automatisch“, nicht Auto HDR
    $hdrAuto = Find-Id $win 'DetailHdrAuto'
    [void](Toggle-El $hdrAuto)
    Check (Wait-Until { (Find-GameConfig (Read-Config $cfg) $a.Key).hdrAuto -eq $true }) 'A: HDR automatisch an (kein Spiel läuft, schaltet nichts)'
    Check (-not (Hint-Shown $win 'DetailAutoHdrNeedsHdrHint')) 'A: Hinweis „HDR automatisch aus“ verschwindet'
    Check ((Find-GameConfig (Read-Config $cfg) $a.Key).autoHdr -eq $true -and (Get-Toggle $toggle) -eq 'On') 'A: autoHdr bleibt an'
    [void](Toggle-El (Find-Id $win 'DetailHdrAuto'))
    Check (Wait-Until { (Find-GameConfig (Read-Config $cfg) $a.Key).hdrAuto -eq $false }) 'A: HDR automatisch wieder aus'
    Check (Hint-Shown $win 'DetailAutoHdrNeedsHdrHint') 'A: Hinweis wieder sichtbar'

    # Platz: „Windows Auto HDR“ unter „HDR automatisch“ (beide gleichzeitig sichtbar scrollen)
    $panel = Find-Id $win 'DetailsPanel'; $sp = $null
    $placed = $false
    if ($panel -and $panel.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern, [ref]$sp)) {
        for ($p = 0; $p -le 100 -and -not $placed; $p += 5) {
            if ($sp.Current.VerticallyScrollable) { $sp.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $p); Start-Sleep -Milliseconds 120 }
            $h = Find-Id $win 'DetailHdrAuto' 500; $t = Find-Id $win 'DetailAutoHdr' 500
            if ((Shown $h) -and (Shown $t)) {
                Check ($t.Current.BoundingRectangle.Top -gt $h.Current.BoundingRectangle.Bottom) ("DetailAutoHdr (y={0:0}) liegt unter DetailHdrAuto (y={1:0})" -f $t.Current.BoundingRectangle.Top, $h.Current.BoundingRectangle.Top)
                $placed = $true
            }
        }
    }
    Check $placed 'Beide Schalter gleichzeitig sichtbar für den Vergleich'

    # Ausschalten: alter Wert genau zurück
    [void](Toggle-El (Find-Id $win 'DetailAutoHdr'))
    Check (Wait-Until { (Find-GameConfig (Read-Config $cfg) $a.Key).autoHdr -eq $false }) 'A ausgeschaltet: config.json autoHdr=false'
    Check (Wait-Until { -not (Badge-Shown (Find-Row $win 'Auto HDR Test')) }) 'A: Kennzeichen wieder weg'
    $restored = Wait-Until { (Get-TestValue $exeA) -ceq 'GpuPreference=2;' }
    EngineCheck $restored ("Testwurzel: A genau wiederhergestellt ({0})" -f (Show (Get-TestValue $exeA)))
    EngineCheck (Wait-Until { -not (Test-Path (Join-Path $cfg 'autohdr-state.json')) }) 'autohdr-state.json ohne Einträge gelöscht'

    # Jede Zeile genau ein Schalter, kein DetailAutoHdr in der Liste; Kennzeichen nur in Zeilen
    $checkBox = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::CheckBox)
    $rows = Get-ListItems (Find-Id $win 'GameList')
    foreach ($row in $rows) {
        $boxes = @($row.FindAll([Windows.Automation.TreeScope]::Descendants, $checkBox))
        Check ($boxes.Count -eq 1 -and $boxes[0].Current.AutomationId -eq 'HdrAutoToggle' -and -not (Find-Id $row 'DetailAutoHdr' 200)) ("Zeile '{0}': genau ein Schalter ({1})" -f $row.Current.Name, (($boxes | ForEach-Object { $_.Current.AutomationId }) -join ', '))
    }
    Check ($rows.Count -eq 3) "Drei Testspiele in der gefilterten Liste ($($rows.Count))"
    $detailSwitches = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'DetailAutoHdr')
    Check (@($win.FindAll([Windows.Automation.TreeScope]::Descendants, $detailSwitches)).Count -eq 1) 'Genau ein Schalter „Windows Auto HDR“ im Fenster (im Detailbereich)'

    # --- Spiel B: eigenes HDR laut PCGamingWiki ---
    [void](Select-Game $win 'Natives HDR Test')
    $nativeShown = Hint-Shown $win 'DetailAutoHdrNativeHint'
    $nativeText = Get-Text (Find-Id $win 'DetailAutoHdrNativeHint' 1000)
    Check ($nativeShown -and $nativeText -match 'eigenes HDR' -and $nativeText -notmatch 'eingeschränkt') "B: Hinweis auf eigenes HDR: '$nativeText'"
    $toggleB = Find-Id $win 'DetailAutoHdr'
    Check ($toggleB -and $toggleB.Current.IsEnabled) 'B: Schalter trotzdem bedienbar'
    [void](Toggle-El $toggleB)
    Check (Wait-Until { (Find-GameConfig (Read-Config $cfg) $b.Key).autoHdr -eq $true }) 'B eingeschaltet: config.json autoHdr=true'
    EngineCheck (Wait-Until { (Get-TestValue $exeB) -ceq 'AutoHDREnable=2097;' }) ("Testwurzel: B neu eingetragen ({0})" -f (Show (Get-TestValue $exeB)))
    [void](Toggle-El (Find-Id $win 'DetailAutoHdr'))
    Check (Wait-Until { (Find-GameConfig (Read-Config $cfg) $b.Key).autoHdr -eq $false }) 'B ausgeschaltet: config.json autoHdr=false'
    EngineCheck (Wait-Until { $null -eq (Get-TestValue $exeB) }) ("Testwurzel: B-Wert wieder gelöscht ({0})" -f (Show (Get-TestValue $exeB)))

    # --- Spiel C: eingeschränktes eigenes HDR ---
    [void](Select-Game $win 'Teilweise HDR Test')
    $limitedShown = Hint-Shown $win 'DetailAutoHdrNativeHint'
    $limitedText = Get-Text (Find-Id $win 'DetailAutoHdrNativeHint' 1000)
    Check ($limitedShown -and $limitedText -match 'eingeschränktes eigenes HDR') "C: Hinweis auf eingeschränktes HDR: '$limitedText'"
    Check ((Find-Id $win 'DetailAutoHdr').Current.IsEnabled) 'C: Schalter bedienbar'

    # A wieder an: Stoff für „Auto-HDR-Einträge zurücknehmen“
    [void](Select-Game $win 'Auto HDR Test')
    [void](Toggle-El (Find-Id $win 'DetailAutoHdr'))
    Check (Wait-Until { (Find-GameConfig (Read-Config $cfg) $a.Key).autoHdr -eq $true }) 'A wieder eingeschaltet'
    EngineCheck (Wait-Until { (Get-TestValue $exeA) -ceq 'GpuPreference=2;AutoHDREnable=2097;' }) 'Testwurzel: A wieder eingetragen'

    # --- Einstellungen ---
    $set = Open-Settings $win $app.Id
    Check ($null -ne $set) 'Einstellungen geöffnet'
    if ($set) {
        $fallback = Find-Id $set 'AutoHdrGlobalFallbackToggle' 3000
        Check ($fallback -and (Get-Toggle $fallback) -eq 'Off') 'Notlösung ab Werk aus'
        Check ($fallback -and $fallback.Current.Name -match 'experimentell') "Notlösung heißt '$($fallback.Current.Name)'"
        [void](Toggle-El $fallback)
        Invoke-El (Find-Id $set 'CancelSettingsButton')
        Start-Sleep -Milliseconds 800
        Check ((Read-Config $cfg).settings.autoHdrGlobalFallback -ne $true) 'Abbrechen: Notlösung nicht gespeichert'

        $set = Open-Settings $win $app.Id
        $fallback = Find-Id $set 'AutoHdrGlobalFallbackToggle' 3000
        Check ((Get-Toggle $fallback) -eq 'Off') 'Nach Abbrechen wieder aus'
        [void](Toggle-El $fallback)
        Invoke-El (Find-Id $set 'SaveSettingsButton')
        Check (Wait-Until { (Read-Config $cfg).settings.autoHdrGlobalFallback -eq $true }) 'Speichern: autoHdrGlobalFallback=true'

        $set = Open-Settings $win $app.Id
        Check ((Get-Toggle (Find-Id $set 'AutoHdrGlobalFallbackToggle' 3000)) -eq 'On') 'Wieder geöffnet: Notlösung an'
        Start-Sleep -Milliseconds 1500
        Check ((Get-TestValue $globalName) -ceq $globalSeed) ("Globaler Wert ohne laufendes Spiel unberührt, auch mit Notlösung an ({0})" -f (Show (Get-TestValue $globalName)))

        $rollback = Find-Id $set 'AutoHdrRollbackButton' 3000
        Check ($rollback -and $rollback.Current.IsEnabled -and $rollback.Current.Name -eq 'Auto-HDR-Einträge zurücknehmen') "Knopf '$(if ($rollback) { $rollback.Current.Name })' bereit"
        Invoke-El $rollback
        $answered = Wait-Until { $m = Find-Id $set 'AutoHdrRollbackMessage' 500; (Shown $m) -and $m.Current.Name }
        $message = Get-Text (Find-Id $set 'AutoHdrRollbackMessage' 1000)
        Check $answered "Rücknahme meldet: '$message'"
        EngineCheck ($message -match 'Erledigt') 'Meldung: erledigt'
        EngineCheck (Wait-Until { (Find-GameConfig (Read-Config $cfg) $a.Key).autoHdr -eq $false }) 'Rücknahme schaltet A aus (config.json)'
        EngineCheck (Wait-Until { (Get-TestValue $exeA) -ceq 'GpuPreference=2;' }) ("Testwurzel: A nach der Rücknahme genau wie vorher ({0})" -f (Show (Get-TestValue $exeA)))
        EngineCheck (-not (Badge-Shown (Find-Row $win 'Auto HDR Test'))) 'A: Kennzeichen nach der Rücknahme weg'
        Note ("Nach der Rücknahme: A autoHdr={0}, Wert {1}" -f (Find-GameConfig (Read-Config $cfg) $a.Key).autoHdr, (Show (Get-TestValue $exeA)))
        Invoke-El (Find-Id $set 'CancelSettingsButton')
    }

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'
    Check ((Get-TestValue $globalName) -ceq $globalSeed) ("Globaler Wert nach dem Beenden unverändert ({0})" -f (Show (Get-TestValue $globalName)))
    Note ("Testwurzel HKCU\{0}:`n{1}" -f $testSub, (Snapshot-Key $testSub))
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally {
    Stop-Started
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testSub, $false) } catch { Note "Testwurzel nicht gelöscht: $($_.Exception.Message)" }
}

$after = Snapshot-Key $realSub
Check ($after -ceq $before) 'Echter Schlüssel UserGpuPreferences unverändert (nur gelesen)'
Check ($null -eq [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testSub)) 'Testwurzel aufgeräumt'
if ($ok) { Remove-Item $cfg -Recurse -Force -ErrorAction SilentlyContinue }
if ($ok -and -not $UiOnly) { Write-Output 'PHASE9_AUTOHDR_UI_OK'; exit 0 }
if ($ok) { Write-Output 'PHASE9_AUTOHDR_UI_PARTIAL_OK'; exit 0 }
exit 1
