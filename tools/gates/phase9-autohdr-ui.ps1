# Gate G9.1 (Oberfläche) und G9.3 (Start über HDRpilot): „Windows Auto HDR“ per UI-Automation an einer eigenen
# Testinstanz (eigene Ablage mit drei Testspielen; nur Fenster dieser Prozess-ID; keine Maus- oder Tastatureingaben).
# Auto HDR schreibt dabei nur in die Testwurzel HKCU\Software\HDRpilot.Tests\AutoHdr\Phase9Ui-<PID>
# (--autohdr-test-root); den echten Schlüssel HKCU\Software\Microsoft\DirectX\UserGpuPreferences liest das Skript nur und
# prüft am Ende, dass er Byte für Byte gleich geblieben ist.
#   - Abschnitt „BILD“ unter „HDR automatisch“: Schalter DetailAutoHdr schreibt autoHdr und lässt hdrAuto stehen; das
#     Schalten allein ändert in Windows nichts (Testwurzel unverändert)
#   - Hinweis „wirkt nur beim Start über HDRpilot“ immer sichtbar; Hinweis „HDR automatisch aus“ folgt hdrAuto, nicht
#     autoHdr; Hinweis auf eigenes HDR (Native/Limited) nur dort, Schalter dort trotzdem bedienbar
#   - Kennzeichen AUTO HDR nur in der Zeile des Spiels, jede Zeile hat genau einen Schalter (HdrAutoToggle)
#   - Einstellungen ohne Notlösung und ohne „Auto-HDR-Einträge zurücknehmen“
#   - Start über HDRpilot („Mit HDR starten“): Das Testspiel ist ein harmloser Fühler (AutoHdrProbe.exe, liest beim
#     Prozessstart nur den globalen Wert der Testwurzel, schreibt ihn in eine Datei und wartet 4 s). Er muss
#     AutoHDREnable=1 sehen (gesetzt vor dem Spielprozess, im Protokoll vor dem Start-Befehl); nach Spielende und
#     Nachlaufzeit ist der Wert wieder genau der alte und autohdr-global.json weg
# HDR wird nie geschaltet: Spiel und globale Einstellung zeigen auf einen Bildschirm, den es nicht gibt (das Protokoll
# muss „nicht angeschlossen“ und keine einzige „HDR-Schaltung“ enthalten). Hinweise und Tastenkürzel-Notlösung sind aus.
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
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

function Shown($el) { try { return $null -ne $el -and -not $el.Current.IsOffscreen } catch { return $false } }

function Normalize([string]$s) { ($s -replace '\s+', ' ').Trim() }

function Read-LibraryText([string]$name) {
    $path = Join-Path $root 'src\HdrPilot.App\Resources\LibraryStrings.resx'
    $data = ([xml][IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)).root.data | Where-Object { $_.name -eq $name }
    if (-not $data) { throw "LibraryStrings.$name fehlt" }
    return [string]$data.value
}

# --- Registry: nur lesen (echter Schlüssel) oder Testwurzel ---
$realSub = 'Software\Microsoft\DirectX\UserGpuPreferences'
$testName = "Phase9Ui-$PID"
$testSub = "Software\HDRpilot.Tests\AutoHdr\$testName"
$globalName = 'DirectXUserGlobalSettings'
$globalSeed = 'AutoHDREnable=0;SwapEffectUpgradeEnable=1;'
$globalOn = 'AutoHDREnable=1;SwapEffectUpgradeEnable=1;'
$fakeDisplay = '\\?\DISPLAY#HDRP9000#phase9-autohdr-gibt-es-nicht'

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

# Fühler als Testspiel: liest beim Start nur den globalen Wert unter HKCU\Software\HDRpilot.Tests\… (jeder andere
# Schlüssel wird abgelehnt), schreibt ihn in eine Datei und wartet dann. Keine Fenster, kein Netz, keine Eingaben.
function New-ProbeGame([string]$cfg, [string]$name) {
    $probe = Join-Path $root 'tools\out\phase9\AutoHdrProbe.exe'
    if (-not (Test-Path $probe)) {
        New-Item -ItemType Directory -Force (Split-Path $probe) | Out-Null
        Add-Type -OutputAssembly $probe -OutputType WindowsApplication -TypeDefinition @'
public static class AutoHdrProbe {
    public static int Main(string[] args) {
        if (args.Length < 3 || !args[0].StartsWith(@"Software\HDRpilot.Tests\", System.StringComparison.OrdinalIgnoreCase)) { return 2; }
        string value = "<fehlt>";
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(args[0], false)) {
            if (key != null) { object v = key.GetValue("DirectXUserGlobalSettings"); if (v != null) { value = v.ToString(); } }
        }
        System.IO.File.WriteAllText(args[1], value);
        System.Threading.Thread.Sleep(int.Parse(args[2]) * 1000);
        return 0;
    }
}
'@
    }
    $dir = Join-Path $cfg "games\$name"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $exe = Join-Path $dir (($name -replace '\s', '') + '.exe')
    Copy-Item $probe $exe
    $key = (& $CliExe games add --config-dir $cfg --path $dir --name $name --hdr off | Select-Object -Last 1).Trim()
    return [pscustomobject]@{ Key = $key; Exe = $exe; Dir = $dir }
}

function Set-Json($obj, [string]$name, $value) { $obj | Add-Member -NotePropertyName $name -NotePropertyValue $value -Force }

function Read-Log([string]$cfg) {
    (Get-ChildItem (Join-Path $cfg 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object {
        $fs = New-Object IO.FileStream($_.FullName, 'Open', 'Read', 'ReadWrite, Delete')
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        $sr.ReadToEnd(); $sr.Dispose()
    }) -join "`n"
}

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
            if ($r.Bottom + 260 -le $pr.Bottom) { return }
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
$probeFile = Join-Path $cfg 'probe-autohdr.txt'
try {
    # Drei Testspiele mit HDR automatisch aus: A ist der Fühler (wird gestartet), B und C werden nie gestartet
    $a = New-ProbeGame $cfg 'Auto HDR Test'
    $b = New-StandInGame $cfg 'Natives HDR Test' $false
    $c = New-StandInGame $cfg 'Teilweise HDR Test' $false
    $config = Read-Config $cfg
    Check ($a.Key -and $b.Key -and $c.Key -and $a.Key -ne $b.Key -and (Find-GameConfig $config $a.Key)) "Testspiele angelegt: $($a.Key) ($($a.Exe)), $($b.Key), $($c.Key)"

    # Nie echtes HDR: Spiel A und die globale Einstellung zeigen auf einen Bildschirm, den es nicht gibt. Start-Befehl:
    # der Fühler mit Testwurzel, Ausgabedatei und 4 s Laufzeit. Kurze Nachlaufzeit, Hinweise und Notlösung aus.
    Set-Json $config.settings 'targetDisplays' @($fakeDisplay)
    Set-Json $config.settings 'gracePeriodSeconds' 2
    Set-Json $config.settings 'notifications' $false
    Set-Json $config.settings 'hotkeyFallback' $false
    $gameA = Find-GameConfig $config $a.Key
    Set-Json $gameA 'display' $fakeDisplay
    Set-Json $gameA 'launchCommand' $a.Exe
    Set-Json $gameA 'launchArguments' ('"{0}" "{1}" 4' -f $testSub, $probeFile)
    [IO.File]::WriteAllText((Join-Path $cfg 'config.json'), ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $config = Read-Config $cfg
    $gameA = Find-GameConfig $config $a.Key
    $safe = $gameA.display -eq $fakeDisplay -and @($config.settings.targetDisplays).Count -eq 1 -and $config.settings.targetDisplays[0] -eq $fakeDisplay -and $config.settings.hotkeyFallback -eq $false
    Check $safe "Kein echter Bildschirm: Spiel A und globale Einstellung zeigen auf $fakeDisplay, Notlösung Tastenkürzel aus"
    if (-not $safe) { throw 'Testablage zeigt auf einen echten Bildschirm, Abbruch vor jedem Start' }

    # HDR laut PCGamingWiki (Zwischenspeicher der Testablage): B eigenes HDR, C eingeschränkt, A unbekannt
    $support = '{ "version": 1, "games": { "' + $b.Key + '": { "status": "Native" }, "' + $c.Key + '": { "status": "Limited" } } }'
    [IO.File]::WriteAllText((Join-Path $cfg 'hdr-support.json'), $support, (New-Object Text.UTF8Encoding $false))

    # Testwurzel vorbelegen: globaler Wert wie auf dem Testrechner
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
    Check ($toggle -and $toggle.Current.Name -eq (Read-LibraryText 'Details_AutoHdr')) "A: Schalter heißt '$($toggle.Current.Name)'"
    $startHint = Read-LibraryText 'Details_AutoHdrStartHint'
    Check ((Hint-Shown $win 'DetailAutoHdrStartHint') -and (Normalize (Get-Text (Find-Id $win 'DetailAutoHdrStartHint' 1000))) -eq (Normalize $startHint)) "A: Hinweis sichtbar, auch vor dem Einschalten: '$startHint'"
    Check ($startHint -match 'über HDRpilot' -and $startHint -match 'Verknüpfung' -and $startHint -match 'Steam-Startoption') 'Hinweis nennt Starten, Verknüpfung und Steam-Startoption'
    Check (Hint-Shown $win 'DetailAutoHdrNeedsHdrHint') 'A: Hinweis „HDR automatisch aus“ sichtbar'
    Check (-not (Hint-Shown $win 'DetailAutoHdrNativeHint')) 'A: kein Hinweis auf eigenes HDR (laut Wiki unbekannt)'
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
    Check ($badges.Count -eq 1 -and $badges[0].Current.Name -eq (Read-LibraryText 'Badge_AutoHdrTooltip')) "Kennzeichen heißt '$(if ($badges.Count) { $badges[0].Current.Name })'"
    Start-Sleep -Milliseconds 1000
    Check ((Get-TestValue $globalName) -ceq $globalSeed -and -not (Test-Path (Join-Path $cfg 'autohdr-global.json'))) ("Einschalten allein ändert in Windows nichts: Testwurzel {0}, kein autohdr-global.json" -f (Show (Get-TestValue $globalName)))
    Check (-not (Find-Id $win 'DetailAutoHdrStatus' 300)) 'Kein Stand je EXE mehr im Abschnitt BILD'

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

    # Jede Zeile genau ein Schalter, kein DetailAutoHdr in der Liste
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
    Check ($nativeShown -and (Normalize $nativeText) -eq (Normalize (Read-LibraryText 'Details_AutoHdrNative'))) "B: Hinweis auf eigenes HDR: '$nativeText'"
    $toggleB = Find-Id $win 'DetailAutoHdr'
    Check ($toggleB -and $toggleB.Current.IsEnabled) 'B: Schalter trotzdem bedienbar'
    [void](Toggle-El $toggleB)
    Check (Wait-Until { (Find-GameConfig (Read-Config $cfg) $b.Key).autoHdr -eq $true }) 'B eingeschaltet: config.json autoHdr=true'
    [void](Toggle-El (Find-Id $win 'DetailAutoHdr'))
    Check (Wait-Until { (Find-GameConfig (Read-Config $cfg) $b.Key).autoHdr -eq $false }) 'B ausgeschaltet: config.json autoHdr=false'

    # --- Spiel C: eingeschränktes eigenes HDR ---
    [void](Select-Game $win 'Teilweise HDR Test')
    $limitedShown = Hint-Shown $win 'DetailAutoHdrNativeHint'
    $limitedText = Get-Text (Find-Id $win 'DetailAutoHdrNativeHint' 1000)
    Check ($limitedShown -and (Normalize $limitedText) -eq (Normalize (Read-LibraryText 'Details_AutoHdrNativeLimited'))) "C: Hinweis auf eingeschränktes HDR: '$limitedText'"
    Check ((Find-Id $win 'DetailAutoHdr').Current.IsEnabled) 'C: Schalter bedienbar'

    # --- Einstellungen: keine Notlösung, kein „zurücknehmen“ ---
    $set = Open-Settings $win $app.Id
    Check ($null -ne $set) 'Einstellungen geöffnet'
    if ($set) {
        $ids = @($set.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ })
        Check ($ids -contains 'SaveSettingsButton' -and -not ($ids | Where-Object { $_ -like '*AutoHdr*' })) ("Einstellungen ohne Auto-HDR-Elemente ({0} AutomationIds, keine mit AutoHdr)" -f $ids.Count)
        Invoke-El (Find-Id $set 'CancelSettingsButton')
        Start-Sleep -Milliseconds 500
    }
    Check ($null -eq (Read-Config $cfg).settings.autoHdrGlobalFallback) 'config.json ohne autoHdrGlobalFallback'
    Check ((Get-TestValue $globalName) -ceq $globalSeed) ("Testwurzel vor dem Start unverändert ({0})" -f (Show (Get-TestValue $globalName)))

    # --- Start über HDRpilot: globaler Schalter vor dem Spielprozess, danach zurück ---
    [void](Select-Game $win 'Auto HDR Test')
    Check ((Find-GameConfig (Read-Config $cfg) $a.Key).autoHdr -eq $true) 'A hat „Windows Auto HDR“ an'
    $launch = Find-Id $win 'LaunchButton' 3000
    Check ($launch -and $launch.Current.IsEnabled) 'Knopf „Mit HDR starten“ bereit'
    $logBefore = (Read-Log $cfg).Length
    Invoke-El $launch
    $probed = Wait-Until { Test-Path -LiteralPath $probeFile } 15000
    $seen = if ($probed) { [IO.File]::ReadAllText($probeFile).Trim() } else { $null }
    Check ($seen -ceq $globalOn) ("Spielprozess sah beim Start AutoHDREnable=1 in der Testwurzel: {0}" -f (Show $seen))
    Check ((Get-TestValue $globalName) -ceq $globalOn -and (Test-Path (Join-Path $cfg 'autohdr-global.json'))) ("Während des Spiels: Testwurzel {0}, autohdr-global.json gemerkt" -f (Show (Get-TestValue $globalName)))
    $restored = Wait-Until { (Get-TestValue $globalName) -ceq $globalSeed -and -not (Test-Path (Join-Path $cfg 'autohdr-global.json')) } 30000
    Check $restored ("Nach Spielende und Nachlaufzeit genau zurückgestellt: {0}, autohdr-global.json weg" -f (Show (Get-TestValue $globalName)))
    $log = (Read-Log $cfg)
    $log = $log.Substring([Math]::Min($logBefore, $log.Length))
    $setAt = $log.IndexOf('AutoHDREnable=1 gesetzt für „Auto HDR Test“ (Start über HDRpilot)', [StringComparison]::Ordinal)
    $launchedAt = $log.IndexOf('Start-Befehl ausgeführt', [StringComparison]::Ordinal)
    Check ($setAt -ge 0 -and $launchedAt -gt $setAt) ("Protokoll: Auto HDR gesetzt (Stelle {0}) vor dem Start-Befehl (Stelle {1})" -f $setAt, $launchedAt)
    Check ($log.Contains('zurückgestellt auf „' + $globalSeed + '“ (Spielende)')) 'Protokoll: zurückgestellt auf den alten Wert (Spielende)'
    Check ($log.Contains('ist nicht angeschlossen') -and -not $log.Contains('HDR-Schaltung um')) 'HDR nie geschaltet: Bildschirm „nicht angeschlossen“, keine HDR-Schaltung im Protokoll'

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'
    Check ((Get-TestValue $globalName) -ceq $globalSeed) ("Globaler Wert nach dem Beenden unverändert ({0})" -f (Show (Get-TestValue $globalName)))
    Note ("Testwurzel HKCU\{0}:`n{1}" -f $testSub, (Snapshot-Key $testSub))
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally {
    Stop-Started
    Get-Process -Name 'AutoHdrTest' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$cfg*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testSub, $false) } catch { Note "Testwurzel nicht gelöscht: $($_.Exception.Message)" }
}

$after = Snapshot-Key $realSub
Check ($after -ceq $before) 'Echter Schlüssel UserGpuPreferences unverändert (nur gelesen)'
Check ($null -eq [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testSub)) 'Testwurzel aufgeräumt'
if ($ok) { Remove-Item $cfg -Recurse -Force -ErrorAction SilentlyContinue }
if ($ok) { Write-Output 'PHASE9_AUTOHDR_UI_OK'; exit 0 }
exit 1
