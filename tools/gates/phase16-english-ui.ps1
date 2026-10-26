# Gate G16.4: Englische Oberfläche (E95) an Testinstanzen mit eigener Ablage, nur per UI-Automation (Invoke-, Selection-
# und ExpandCollapse-Muster), ohne Maus und Tastatur, ohne HDR-Schaltung und ohne Netz. Fenster nur über die Prozess-ID
# der eigenen Testinstanz. Die erwarteten Texte kommen aus den .en.resx bzw. den neutralen (deutschen) resx.
#   - Ablage mit "language": "en": Spielebibliothek, Einstellungen (samt Vorschau des Tray-Menüs) und Kommandozeile englisch
#   - Sprachwahl: „Deutsch“ wählen, „Restart now“: Die alte Instanz endet, eine neue startet mit --restarted, zeigt die
#     Einstellungen auf Deutsch und hat "language": "de" gespeichert; die Bibliothek ist dann deutsch
#   - Ablage ohne config.json: Die Kommandozeile legt sie mit der Sprache von Windows an (Deutsch bei Deutsch, sonst Englisch)
#   - Setup (Testablage --test-root, --ui-language en): Fenster, Überschrift und Knöpfe englisch; „Cancel“ ändert nichts
#   - Bildschirmfotos englisch (Liste, Einstellungen, Bild wählen) mit „-en“ im Namen, deutsche ohne
#   PHASE16_ENGLISH_UI_OK
param([string]$ShotsDir = (Join-Path $env:TEMP 'HDRpilot-Gates\phase16-english'), [switch]$SkipSetup)
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
. (Join-Path $PSScriptRoot 'Phase7Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

function Test-Shown($el) {
    if (-not $el) { return $false }
    try { return -not $el.Current.IsOffscreen } catch { return $false }
}

function Wait-Shown($parent, [string]$automationId, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $el = Find-Id $parent $automationId 200
        try { if ($el -and -not $el.Current.IsOffscreen -and $el.Current.IsEnabled) { return $el } } catch {}
        Start-Sleep -Milliseconds 200
    }
    return $null
}

# Alle Namen unterhalb eines Elements (Rohansicht), etwa die Zeilen der Vorschau des Tray-Menüs.
function Get-AllNames($el) {
    $names = New-Object System.Collections.ArrayList
    $walker = [Windows.Automation.TreeWalker]::RawViewWalker
    $stack = New-Object System.Collections.Stack
    $stack.Push($el)
    while ($stack.Count -gt 0 -and $names.Count -lt 500) {
        $current = $stack.Pop()
        try { [void]$names.Add($current.Current.Name) } catch {}
        $child = $walker.GetFirstChild($current)
        while ($child) { $stack.Push($child); $child = $walker.GetNextSibling($child) }
    }
    return $names
}

# Name des Elements mit dieser Kennung gegen den erwarteten Text (genau, Groß-/Kleinschreibung zählt).
function Check-Name($parent, [string]$automationId, [string]$expected, [string]$label) {
    $el = Find-Id $parent $automationId 3000
    $name = if ($el) { $el.Current.Name } else { '(fehlt)' }
    Check ($el -and $name -ceq $expected) ("{0}: {1} = '{2}' (erwartet '{3}')" -f $label, $automationId, $name, $expected)
}

# Wählt in einer ComboBox den Eintrag mit diesem Namen (aufklappen, wählen, zuklappen).
function Select-ComboItem($combo, [string]$name) {
    $expand = $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 400
    $cond = New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)),
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $name)))
    $item = $combo.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
    if ($item) { Select-El $item }
    try { $expand.Collapse() } catch {}
    Start-Sleep -Milliseconds 300
    return $null -ne $item
}

# Prozess der Testinstanz, die ein Neustart gestartet hat: per WMI über die Befehlszeile (kein Handle auf fremde Prozesse).
function Find-RestartedInstance([string]$cfg, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $hit = Get-CimInstance Win32_Process -Filter "Name = 'HDRpilot.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and $_.CommandLine -match '--restarted' -and (Test-Contains $_.CommandLine $cfg) } | Select-Object -First 1
        if ($hit) { return [int]$hit.ProcessId }
        Start-Sleep -Milliseconds 250
    }
    return 0
}

function Stop-Instance([string]$cfg) {
    $p = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -PassThru
    [void]$p.WaitForExit(15000)
}

function Set-ConfigLanguage([string]$cfg, [string]$language) {
    $file = Join-Path $cfg 'config.json'
    $json = [IO.File]::ReadAllText($file) | ConvertFrom-Json
    $json.settings | Add-Member -NotePropertyName 'language' -NotePropertyValue $language -Force
    $json.settings | Add-Member -NotePropertyName 'notifications' -NotePropertyValue $false -Force
    [IO.File]::WriteAllText($file, ($json | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
}

$libEn = Read-Resx 'LibraryStrings.en'
$libDe = Read-Resx 'LibraryStrings'
$shellEn = Read-Resx 'ShellStrings.en'
$shellDe = Read-Resx 'ShellStrings'
$setupEn = Read-Resx 'SetupStrings.en'
$realBefore = Get-RealState
$layout = $null
$restartedPid = 0

$cfgEn = New-TestConfig 'phase16-english'
$game = New-StandInGame $cfgEn 'Stellvertreter Englisch' $true
Set-ConfigLanguage $cfgEn 'en'
$cfgFresh = Join-Path $root 'tools\out\phase16-neu'
if (Test-Path $cfgFresh) { Remove-Item $cfgFresh -Recurse -Force }
New-Item -ItemType Directory -Force $ShotsDir | Out-Null
Get-ChildItem $ShotsDir -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force

try {
    Note '--- Kommandozeile'
    $list = (& $CliExe games list --config-dir $cfgEn | Out-String)
    Check (Test-Contains $list 'Games: 1, with HDR on launch: 1.') ("games list englisch: " + ($list.Trim() -split "`n" | Select-Object -Last 1))
    $help = (& $CliExe help --config-dir $cfgEn | Out-String)
    Check ($help.TrimStart().StartsWith('HDRpilot command line')) 'Hilfe der Kommandozeile englisch'

    # Neue Ablage: Sprache wie die Anzeigesprache von Windows (die Kommandozeile legt config.json an, startet keine App).
    $windowsLanguage = if ([Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'de') { 'de' } else { 'en' }
    & $CliExe games list --config-dir $cfgFresh | Out-Null
    $fresh = Read-Config $cfgFresh
    Check ($fresh.settings.language -eq $windowsLanguage) ("Neue Ablage: language = '{0}' (Anzeigesprache von Windows: {1})" -f $fresh.settings.language, [Globalization.CultureInfo]::CurrentUICulture.Name)

    Note '--- Bildschirmfotos'
    $snap = Start-Process $AppExe -ArgumentList @('--snapshot', "`"$ShotsDir`"", '--config-dir', "`"$cfgEn`"", '--snapshot-scenes', 'hauptfenster-liste,einstellungen-allgemein,hauptfenster-bildwahl', '--snapshot-themes', 'dunkel') -PassThru -Wait
    Check ($snap.ExitCode -eq 0) "Bildschirmfotos der englischen Ablage: exit $($snap.ExitCode)"
    foreach ($name in 'hauptfenster-liste-dunkel-en.png', 'einstellungen-allgemein-dunkel-en.png', 'hauptfenster-bildwahl-dunkel-en.png') {
        $file = Join-Path $ShotsDir $name
        Check ((Test-Path $file) -and (Get-Item $file).Length -gt 10000) "Foto $name"
    }
    Check (@(Get-ChildItem $ShotsDir -Filter '*-dunkel.png').Count -eq 0) 'Keine deutschen Dateinamen bei englischer Ablage'

    Note '--- Bibliothek englisch'
    $app = Start-App $cfgEn @('--show')
    $main = Find-Window 'MainWindow' $app.Id
    Check ($null -ne $main) "Bibliothek der Testinstanz geöffnet (PID $($app.Id))"
    if (-not $main) { throw 'Bibliothek fehlt' }
    Check ($main.Current.Name -ceq $libEn.Window_Title) "Fenstertitel: $($main.Current.Name)"
    $libraryChecks = [ordered]@{
        RescanButton = 'Rescan_Button'; AddGameButton = 'AddGame_Button'; SearchBox = 'Search_Name'; SettingsButton = 'Settings_Name'
        PauseToggle = 'Pause_Name'; SortOrder = 'Sort_Name'; ViewListButton = 'View_List'; ViewGridButton = 'View_Grid'; ViewShelfButton = 'View_Shelf'
        MarkAllSeenButton = 'MarkAllSeen_Name'; LibraryToolbar = 'Toolbar_Name'
    }
    foreach ($id in $libraryChecks.Keys) { Check-Name $main $id $libEn[$libraryChecks[$id]] 'Bibliothek' }
    $count = Find-Id $main 'HeaderCount' 3000
    $countText = if ($count) { Get-Text $count } else { '' }
    Check (Test-Contains $countText $libEn.Count_GamesOne) "Kopf zählt englisch: '$countText'"

    Note '--- Einstellungen englisch'
    $forward = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfgEn`"", '--settings') -PassThru -Wait
    $set = Find-Window 'SettingsWindow' $app.Id
    Check ($null -ne $set) "Einstellungen der Testinstanz geöffnet (weitergereicht, exit $($forward.ExitCode))"
    if (-not $set) { throw 'Einstellungen fehlen' }
    Check ($set.Current.Name -ceq $shellEn.SettingsWindowTitle) "Fenstertitel: $($set.Current.Name)"
    $settingsChecks = [ordered]@{
        SaveSettingsButton = 'SaveSettings'; CancelSettingsButton = 'CancelSettings'; NavGeneral = 'NavGeneral'; NavAppearance = 'NavAppearance'
        AutostartToggle = 'AutostartLabel'; NotificationsToggle = 'NotificationsLabel'; LanguageChoice = 'LanguageLabel'; LogLevel = 'LogLevelLabel'
    }
    foreach ($id in $settingsChecks.Keys) { Check-Name $set $id $shellEn[$settingsChecks[$id]] 'Einstellungen' }
    $combo = Find-Id $set 'LanguageChoice'
    Check ((Get-Text $combo) -ceq 'English') "Sprache gewählt: '$(Get-Text $combo)'"
    Check (-not (Test-Shown (Find-Id $set 'LanguageRestartButton' 500))) 'Kein Neustart-Hinweis, solange die Wahl der angezeigten Sprache entspricht'

    Invoke-El (Find-Id $set 'NavAppearanceTray')
    Start-Sleep -Milliseconds 800
    $preview = Find-Id $set 'TrayMenuPreview' 3000
    $names = if ($preview) { @(Get-AllNames $preview) } else { @() }
    Check ($preview -and $preview.Current.Name -ceq $shellEn.TrayMenuPreviewName) "Vorschau des Tray-Menüs: '$(if ($preview) { $preview.Current.Name })'"
    foreach ($key in 'TrayMenuAutoHdr', 'TrayMenuPause', 'TrayMenuOpen', 'TrayMenuExit') {
        Check ($names -ccontains $shellEn[$key]) ("Tray-Menü (Vorschau) zeigt '{0}'" -f $shellEn[$key])
    }
    Check (($names -ccontains $shellEn.TrayMenuToggleHdrOn) -or ($names -ccontains $shellEn.TrayMenuToggleHdrOff)) 'Tray-Menü (Vorschau) zeigt den HDR-Eintrag englisch'

    Note '--- Sprache wechseln und neu starten'
    Invoke-El (Find-Id $set 'NavGeneral')
    Start-Sleep -Milliseconds 500
    $combo = Find-Id $set 'LanguageChoice'
    Check (Select-ComboItem $combo 'Deutsch') 'Eintrag „Deutsch“ gewählt (Eigenbezeichnung)'
    $restart = Wait-Shown $set 'LanguageRestartButton' 5000
    Check ($restart -and $restart.Current.Name -ceq $shellEn.LanguageRestartNow) ("Knopf erscheint: '{0}'" -f $(if ($restart) { $restart.Current.Name } else { 'fehlt' }))
    $hint = Find-Id $set 'LanguageRestartHint' 2000
    Check ($hint -and (Get-Text $hint) -ceq $shellEn.LanguageRestartHint) 'Hinweis zum Neustart englisch'
    Check ((Read-Config $cfgEn).settings.language -eq 'en') 'Vor „Restart now“ ist noch nichts gespeichert'
    if (-not $restart) { throw 'Neustart-Knopf fehlt' }
    Invoke-El $restart
    $exited = $app.WaitForExit(30000)
    Check $exited "Alte Instanz beendet (PID $($app.Id))"
    $restartedPid = Find-RestartedInstance $cfgEn 30000
    Check ($restartedPid -gt 0) "Neue Instanz mit --restarted gestartet (PID $restartedPid)"
    Check ((Read-Config $cfgEn).settings.language -eq 'de') 'config.json: language = de'
    if ($restartedPid -le 0) { throw 'Keine neue Instanz' }
    $set = Find-Window 'SettingsWindow' $restartedPid 30000
    Check ($set -and $set.Current.Name -ceq $shellDe.SettingsWindowTitle) ("Einstellungen nach dem Neustart deutsch: '{0}'" -f $(if ($set) { $set.Current.Name } else { 'fehlen' }))
    if ($set) {
        Check-Name $set 'SaveSettingsButton' $shellDe.SaveSettings 'Einstellungen (de)'
        Check-Name $set 'LanguageChoice' $shellDe.LanguageLabel 'Einstellungen (de)'
        Check ((Get-Text (Find-Id $set 'LanguageChoice')) -ceq 'Deutsch') 'Sprache nach dem Neustart: Deutsch'
    }
    [void](Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfgEn`"", '--show') -PassThru -Wait)
    $main = Find-Window 'MainWindow' $restartedPid
    Check ($main -and $main.Current.Name -ceq $libDe.Window_Title) ("Bibliothek nach dem Neustart deutsch: '{0}'" -f $(if ($main) { $main.Current.Name } else { 'fehlt' }))
    if ($main) { Check-Name $main 'RescanButton' $libDe.Rescan_Button 'Bibliothek (de)' }
    Stop-Instance $cfgEn

    if (-not $SkipSetup) {
        Note '--- Setup englisch'
        Check (-not (Test-Elevated)) 'Prüflauf ohne Adminrechte (Skript nicht erhöht)'
        $version = Get-ProjectVersion
        $setup = Get-SetupPath $version
        Update-SetupFile $setup | ForEach-Object { Note $_ }
        $layout = New-SetupTestLayout 'HDRpilot-Gate16'
        $null = Initialize-SetupTestConfig $layout.Config
        $cfgStart = @(Get-TreeHashes $layout.Config)
        $proc = Start-Exe $setup @('--test-root', $layout.Root, '--ui-language', 'en')
        $win = Find-Window 'SetupWindow' $proc.Id 30000
        Check ($null -ne $win) "Setup-Fenster geöffnet (PID $($proc.Id))"
        if ($win) {
            Check ($win.Current.Name -ceq $setupEn.SetupWindowTitle) "Fenstertitel: $($win.Current.Name)"
            $title = Get-Text (Find-Id $win 'SetupTitle' 3000)
            Check ($title -ceq [string]::Format($setupEn.SetupTitleInstall, $version)) "Überschrift: $title"
            Check-Name $win 'SetupInstallButton' $setupEn.SetupInstallButton 'Setup'
            Check-Name $win 'SetupCancelButton' $setupEn.SetupCancelButton 'Setup'
            Check-Name $win 'SetupAutostart' $setupEn.SetupAutostart 'Setup'
            Check-Name $win 'SetupDesktopShortcut' $setupEn.SetupDesktopShortcut 'Setup'
            Invoke-El (Find-Id $win 'SetupCancelButton')
        }
        $exited = $proc.WaitForExit(15000)
        Check $exited ("Setup nach „Cancel“ beendet, Exitcode {0}" -f $(if ($exited) { $proc.ExitCode } else { 'läuft noch' }))
        Check (-not (Test-Path -LiteralPath $layout.Exe)) 'Nichts installiert'
        Check (@(Compare-TreeHashes $cfgStart @(Get-TreeHashes $layout.Config)).Count -eq 0) 'Ablage des Setups byte-gleich (der Setup fasst die Einstellungen nie an)'
    }
}
catch {
    Note "FEHLER Abbruch: $($_.Exception.Message)"
    $ok = $false
}
finally {
    Note '--- Aufräumen'
    # Läuft noch eine Instanz dieser Ablage (per WMI über die Befehlszeile), erst regulär beenden, dann hart.
    $own = @(Get-CimInstance Win32_Process -Filter "Name = 'HDRpilot.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and (Test-Contains $_.CommandLine $cfgEn) })
    if ($own.Count -gt 0) { Stop-Instance $cfgEn }
    Stop-Started
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'HDRpilot.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and (Test-Contains $_.CommandLine $cfgEn) })) {
        Note "FEHLER Rest-Prozess $($p.ProcessId) der Testablage hart beendet"
        $ok = $false
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
    if ($layout) {
        foreach ($n in @(Stop-TestLeftovers $layout @())) { Note $n; if ($n -like 'FEHLER*') { $ok = $false } }
        foreach ($n in @(Remove-TestLayout $layout @())) { Note $n; if ($n -like 'FEHLER*') { $ok = $false } }
    }
    $diff = @(Compare-RealState $realBefore (Get-RealState))
    foreach ($line in $diff) { Note "  - $line" }
    Check ($diff.Count -eq 0) ("Echter Stand unverändert ({0} Angaben)" -f $realBefore.Count)
}
if ($ok) { Write-Output 'PHASE16_ENGLISH_UI_OK'; exit 0 }
Write-Output 'PHASE16_ENGLISH_UI_FAILED'
exit 1
