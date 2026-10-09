# Gate G7.3: Oberfläche von Setup und Entfernen per UI-Automation (nur Invoke-/Toggle-Muster, keine Maus, keine Tastatur)
# an einer eigenen Testablage (--test-root). Fenster nur über AutomationId und die Prozess-ID des selbst gestarteten
# Prozesses. Texte werden gegen SetupStrings.resx geprüft.
#   - Setup: Titel mit Version, Programmordner, Hinweis auf die erhaltene Ablage, Optionen (Autostart an, Desktop aus,
#     danach starten an), "danach starten" aus, Installieren, Fortschritt und Ergebnis, Fertig, Exitcode 0
#   - Entfernen: Rückfrage (Daten löschen standardmäßig aus), Bestätigen; die Fortsetzung läuft als neuer Prozess aus
#     %TEMP%\HDRpilot-Uninstall-*, zeigt Fortschritt und Ergebnis; danach ist alles entfernt, die Ablage bleibt
# Der echte Stand wird vorher und nachher verglichen (wie phase7-setup.ps1).
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
. (Join-Path $PSScriptRoot 'Phase7Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

$version = Get-ProjectVersion
$setup = Get-SetupPath $version
$layout = $null
$tempSeen = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
$realBefore = Get-RealState
Note ("Echter Stand vorher erfasst ({0} Angaben)" -f $realBefore.Count)

function Test-Shown($el) {
    if (-not $el) { return $false }
    try { return -not $el.Current.IsOffscreen } catch { return $false }
}

# Wartet, bis das Element sichtbar und bedienbar ist (etwa der Fertig-Knopf nach dem Installieren).
function Wait-Shown($parent, [string]$automationId, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $el = Find-Id $parent $automationId 200
        try { if ($el -and -not $el.Current.IsOffscreen -and $el.Current.IsEnabled) { return $el } } catch {}
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Get-IdText($parent, [string]$automationId) {
    $el = Find-Id $parent $automationId 2000
    if (-not $el) { return $null }
    return [string](Get-Text $el)
}

# Alle Namen unterhalb eines Elements (Rohansicht), etwa die Schritte in der Fortschrittsliste.
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
    return ,$names
}

function Find-Continuation([string[]]$before) {
    foreach ($dir in Get-UninstallTempDirs) {
        if ($dir -in $before) { continue }
        [void]$tempSeen.Add($dir)
        $p = @(Get-ProcessesUnder 'HDRpilot-Uninstall' $dir) | Select-Object -First 1
        if ($p) { return $p }
    }
    return $null
}

try {
    Note '--- Voraussetzungen'
    Check (-not (Test-Elevated)) 'Prüflauf ohne Adminrechte (Skript nicht erhöht)'
    Update-SetupFile $setup | ForEach-Object { Note $_ }
    $bundle = Test-SetupBundle $setup $PortableDir
    foreach ($p in $bundle.Problems) { Note "  - $p" }
    Check ($bundle.Problems.Count -eq 0 -and $bundle.AsInvoker) ("Setup-Datei {0} geprüft (Abschluss, Prüfsummen, Paket, Manifest asInvoker)" -f (Split-Path $setup -Leaf))
    if ($bundle.Problems.Count -gt 0) { throw 'Setup-Datei unbrauchbar' }
    $missing = @(Get-MissingSetupMode)
    Check ($missing.Count -eq 0) ('Setup-Modus im Quelltext vorhanden' + $(if ($missing) { ': fehlt ' + ($missing -join '; ') } else { '' }))
    if ($missing) { throw 'Setup-Modus fehlt: die Setup-Datei liefe als normale App mit der echten Ablage, deshalb kein Start' }
    $s = Read-Resx 'SetupStrings'
    $portableHash = Get-Sha (Join-Path $PortableDir 'HDRpilot.exe')

    $layout = New-SetupTestLayout 'HDRpilot-Gate7UI'
    Note "Testablage $($layout.Root), Registry $($layout.RegistryBranch), Instanz $($layout.InstanceId)"
    Check (-not (Test-Path $layout.RegistryBranch)) 'Registry-Zweig der Testablage ist neu'
    $canaryFile = Join-Path $layout.Config 'canary.bin'
    $canary = Initialize-SetupTestConfig $layout.Config

    Note '--- Setup-Fenster'
    $proc = Start-Exe $setup @('--test-root', $layout.Root)
    $win = Find-Window 'SetupWindow' $proc.Id 30000
    Check ($null -ne $win) "Setup-Fenster geöffnet (SetupWindow, PID $($proc.Id))"
    if (-not $win) { throw 'Setup-Fenster fehlt' }
    Check ($win.Current.Name -ceq $s.SetupWindowTitle) "Fenstertitel: $($win.Current.Name)"
    $title = Get-IdText $win 'SetupTitle'
    Check ($title -ceq [string]::Format($s.SetupTitleInstall, $version)) "Überschrift mit Version: $title"
    Note ("     Versionszeile: {0}" -f (Get-IdText $win 'SetupVersionInfo'))
    $dir = Get-IdText $win 'SetupInstallDir'
    Check (Test-SamePath $dir $layout.InstallDir) "Programmordner: $dir"
    $note = Get-IdText $win 'SetupDataNote'
    Check ((Test-Contains $note $layout.Config) -and (Test-Template $note $s.SetupDataNote)) "Hinweis, dass Einstellungen erhalten bleiben: $note"
    Check (-not (Test-Shown (Find-Id $win 'SetupRunningNote' 500))) 'Kein Hinweis auf laufendes HDRpilot (läuft nicht)'
    Check (-not (Test-Shown (Find-Id $win 'SetupAllowDowngrade' 500))) 'Kein Ankreuzfeld für ältere Version (Erstinstallation)'
    $expected = [ordered]@{ SetupAutostart = 'On'; SetupDesktopShortcut = 'Off'; SetupLaunchAfter = 'On' }
    foreach ($id in $expected.Keys) {
        $box = Find-Id $win $id 2000
        $state = if ($box) { [string](Get-Toggle $box) } else { 'fehlt' }
        Check ($box -and $state -eq $expected[$id] -and $box.Current.Name -ceq $s[$id]) ("Option {0} '{1}': {2} (erwartet {3})" -f $id, $(if ($box) { $box.Current.Name } else { '' }), $state, $expected[$id])
    }
    $install = Find-Id $win 'SetupInstallButton' 2000
    Check ($install -and $install.Current.Name -ceq $s.SetupInstallButton -and $install.Current.IsEnabled) ("Knopf {0}" -f $(if ($install) { $install.Current.Name } else { 'fehlt' }))
    $cancel = Find-Id $win 'SetupCancelButton' 2000
    Check ($cancel -and $cancel.Current.Name -ceq $s.SetupCancelButton) ("Knopf {0}" -f $(if ($cancel) { $cancel.Current.Name } else { 'fehlt' }))

    $launch = Find-Id $win 'SetupLaunchAfter' 2000
    $launchState = if ($launch) { [string](Toggle-El $launch) } else { 'fehlt' }
    Check ($launchState -eq 'Off') "HDRpilot danach starten ausgeschaltet ($launchState)"
    if (-not $install) { throw 'Installieren-Knopf fehlt' }
    Invoke-El $install
    $close = Wait-Shown $win 'SetupCloseButton' 120000
    Check ($null -ne $close -and $close.Current.Name -ceq $s.SetupCloseButton) ("Fertig-Knopf erscheint nach dem Installieren ({0})" -f $(if ($close) { $close.Current.Name } else { 'fehlt' }))
    $result = Get-IdText $win 'SetupResultText'
    Check ($result -ceq [string]::Format($s.SetupDoneInstall, $version)) "Ergebnis: $result"
    $progress = Find-Id $win 'SetupProgress' 2000
    $steps = if ($progress) { (Get-AllNames $progress) -join ' | ' } else { '' }
    Check ($progress -and (Test-Contains $steps $s.SetupStepCopyFiles) -and (Test-Contains $steps $s.SetupStepRegister)) "Fortschritt zeigt die Schritte: $steps"
    Check (-not (Test-Shown (Find-Id $win 'SetupRetryButton' 500))) 'Kein Erneut-versuchen-Knopf nach Erfolg'

    Check ((Get-Sha $layout.Exe) -eq $portableHash) 'Installierte HDRpilot.exe byte-gleich mit der portablen'
    $lnk = Get-ShortcutInfo $layout.StartMenuLnk
    Check ($lnk -and (Test-SamePath $lnk.Target $layout.Exe)) 'Startmenü-Verknüpfung zeigt auf die installierte EXE'
    Check ((Get-RegValue $layout.UninstallKey 'DisplayVersion') -eq $version) "Eintrag unter Installierte Apps (Test-Zweig) mit Version $version"
    Check (Test-Contains (Get-RegValue $layout.RunKey $RunValueName) $layout.Exe) 'Autostart im Test-Zweig an (Vorbelegung übernommen)'
    Check (-not (Test-Path -LiteralPath $layout.DesktopLnk)) 'Keine Desktop-Verknüpfung (Vorbelegung aus)'
    Check (-not (Test-InstanceRunning $layout.InstanceId)) 'HDRpilot nicht gestartet (Option ausgeschaltet)'
    if ($close) { Invoke-El $close }
    $exited = $proc.WaitForExit(15000)
    Check ($exited -and $proc.ExitCode -eq 0) ("Setup nach Fertig beendet, Exitcode {0}" -f $(if ($exited) { $proc.ExitCode } else { 'läuft noch' }))

    Note '--- Entfernen: Rückfrage'
    $tempBefore = @(Get-UninstallTempDirs)
    $first = Start-Exe $layout.Exe @('--uninstall', '--test-root', $layout.Root)
    $uwin = Find-Window 'UninstallWindow' $first.Id 30000
    Check ($null -ne $uwin) "Rückfrage zum Entfernen geöffnet (UninstallWindow, PID $($first.Id))"
    if (-not $uwin) { throw 'Rückfrage fehlt' }
    Check ($uwin.Current.Name -ceq $s.UninstallWindowTitle) "Fenstertitel: $($uwin.Current.Name)"
    $utitle = Get-IdText $uwin 'UninstallTitle'
    Check ($utitle -ceq [string]::Format($s.UninstallTitle, $version)) "Überschrift: $utitle"
    $removeData = Find-Id $uwin 'UninstallRemoveData' 2000
    $removeState = if ($removeData) { [string](Get-Toggle $removeData) } else { 'fehlt' }
    Check ($removeData -and $removeState -eq 'Off' -and $removeData.Current.Name -ceq $s.UninstallRemoveData) ("Daten löschen standardmäßig aus: '{0}' {1}" -f $(if ($removeData) { $removeData.Current.Name } else { '' }), $removeState)
    $steam = Find-Id $uwin 'UninstallSteamWarning' 500
    if (Test-Shown $steam) {
        $steamText = [string](Get-Text $steam)
        Check ((($steamText -replace '\s+', ' ').Trim()).StartsWith(($s.UninstallSteamWarning -replace '\s+', ' ').Trim())) "Warnung vor Steam-Startoptionen (es gibt Einträge): $steamText"
    } else {
        Note '     Keine Warnung vor Steam-Startoptionen (keine Einträge gefunden)'
    }
    $confirm = Find-Id $uwin 'UninstallConfirmButton' 2000
    Check ($confirm -and $confirm.Current.Name -ceq $s.UninstallConfirmButton) ("Knopf {0}" -f $(if ($confirm) { $confirm.Current.Name } else { 'fehlt' }))
    $ucancel = Find-Id $uwin 'UninstallCancelButton' 2000
    Check ($ucancel -and $ucancel.Current.Name -ceq $s.UninstallCancelButton) ("Knopf {0}" -f $(if ($ucancel) { $ucancel.Current.Name } else { 'fehlt' }))
    if (-not $confirm) { throw 'Entfernen-Knopf fehlt' }
    Invoke-El $confirm

    Note '--- Entfernen: Fortsetzung aus dem Temp-Ordner'
    $cont = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $cont -and $sw.ElapsedMilliseconds -lt 30000) { $cont = Find-Continuation $tempBefore; if (-not $cont) { Start-Sleep -Milliseconds 100 } }
    $firstExited = $first.WaitForExit(30000)
    Check ($firstExited -and $first.ExitCode -eq 0) ("Erste Instanz aus dem Programmordner beendet sich, Exitcode {0}" -f $(if ($firstExited) { $first.ExitCode } else { 'läuft noch' }))
    Check ($null -ne $cont) ("Fortsetzung läuft als neuer Prozess {0} (PID {1})" -f $(if ($cont) { $cont.Path } else { 'fehlt' }), $(if ($cont) { $cont.Id } else { '' }))
    if (-not $cont) { throw 'Fortsetzung fehlt' }
    $null = $cont.Handle # Handle halten, damit der Exitcode lesbar bleibt
    $pwin = Find-Window 'UninstallProgressWindow' $cont.Id 30000
    Check ($null -ne $pwin) 'Fortschrittsfenster der Fortsetzung (UninstallProgressWindow)'
    if (-not $pwin) { throw 'Fortschrittsfenster fehlt' }
    $uclose = Wait-Shown $pwin 'UninstallCloseButton' 90000
    Check ($null -ne $uclose -and $uclose.Current.Name -ceq $s.UninstallCloseButton) ("Fertig-Knopf nach dem Entfernen ({0})" -f $(if ($uclose) { $uclose.Current.Name } else { 'fehlt' }))
    $uresult = Get-IdText $pwin 'UninstallResultText'
    Check ((Test-Contains $uresult $s.UninstallDone) -and (Test-Contains $uresult ([string]::Format($s.UninstallDoneDataKept, $layout.Config)))) "Ergebnis: $uresult"
    $uprogress = Find-Id $pwin 'UninstallProgress' 2000
    $usteps = if ($uprogress) { (Get-AllNames $uprogress) -join ' | ' } else { '' }
    Check ($uprogress -and (Test-Contains $usteps $s.UninstallStepRemoveFiles) -and (Test-Contains $usteps $s.UninstallStepUnregister)) "Fortschritt zeigt die Schritte: $usteps"
    if ($uclose) { Invoke-El $uclose }
    $contExited = $cont.WaitForExit(20000)
    Check $contExited ("Fortsetzung nach Fertig beendet (Exitcode {0})" -f $(if ($contExited) { $cont.ExitCode } else { 'läuft noch' }))

    Check (-not (Test-Path $layout.UninstallKey)) 'Eintrag unter Installierte Apps (Test-Zweig) entfernt'
    Check (-not (Test-Path -LiteralPath $layout.InstallDir)) 'Programmordner entfernt'
    Check (-not (Test-Path -LiteralPath $layout.StartMenuLnk) -and -not (Test-Path -LiteralPath $layout.DesktopLnk)) 'Verknüpfungen entfernt'
    Check ($null -eq (Get-RegValue $layout.RunKey $RunValueName)) 'Autostart-Wert entfernt'
    Check ((Test-Path -LiteralPath (Join-Path $layout.Config 'config.json')) -and (Get-Sha $canaryFile) -eq $canary) 'Ablage bleibt: config.json vorhanden, Kanarien-Datei unverändert'
    $contDirs = @($tempSeen)
    $gone = Wait-Until { @($contDirs | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0 } 20000
    Check ($contDirs.Count -ge 1 -and $gone) ("Temp-Ordner der Fortsetzung hat sich innerhalb von 20 s selbst entfernt ({0})" -f ($contDirs -join ', '))
}
catch {
    Note "FEHLER Abbruch: $($_.Exception.Message)"
    $ok = $false
}
finally {
    Note '--- Aufräumen'
    if ($layout) {
        foreach ($n in @(Stop-TestLeftovers $layout @($tempSeen))) { Note $n; if ($n -like 'FEHLER*') { $ok = $false } }
        foreach ($n in @(Remove-TestLayout $layout @($tempSeen))) { Note $n; if ($n -like 'FEHLER*') { $ok = $false } }
    }
    $diff = @(Compare-RealState $realBefore (Get-RealState))
    foreach ($line in $diff) { Note "  - $line" }
    Check ($diff.Count -eq 0) ("Echter Stand unverändert ({0} Angaben)" -f $realBefore.Count)
}
if ($ok) { Write-Output 'PHASE7_SETUP_UI_OK'; exit 0 }
Write-Output 'PHASE7_SETUP_UI_FAILED'
exit 1
