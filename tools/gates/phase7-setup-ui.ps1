# Gate G7.3: Oberfläche von Setup und Entfernen per UI-Automation (nur Invoke-/Toggle-Muster, keine Maus, keine Tastatur)
# an einer eigenen Testablage (--test-root). Fenster nur über AutomationId und die Prozess-ID des selbst gestarteten
# Prozesses. Texte werden gegen SetupStrings.resx geprüft.
#   - Probe: Setup-Datei und installierte EXE laufen wirklich im Setup-Modus (harmloser Aufruf, Exitcode 2)
#   - Setup: Titel mit Version, Programmordner, Hinweis auf die erhaltene Ablage, Optionen (Autostart an, Desktop aus,
#     danach starten an), "danach starten" aus, Installieren, Fortschritt und Ergebnis, Fertig, Exitcode 0
#   - Entfernen: Rückfrage (Daten löschen standardmäßig aus), Warnung vor der Steam-Startoption eines Spiels (Steam-Ordner
#     der Testablage), Bestätigen; die Fortsetzung läuft als neuer Prozess aus %TEMP%\HDRpilot-Uninstall-* (laut
#     Setup-Protokoll dieses Laufs), zeigt Fortschritt und Ergebnis; danach ist alles entfernt, die Ablage bleibt
#   - G9.4: vor dem Entfernen ein nachgestellter Absturz mit gesetztem Auto-HDR-Schalter (Testwurzel der Ablage, Merker
#     autohdr-global.json): Der Fortschritt zeigt den Schritt, danach ist der globale Wert genau der alte, der Merker weg
#   - Aktualisieren einer älteren, nicht eingetragenen EXE (wie beim Nutzer): Titel "aktualisieren", Version alt → neu,
#     Knopf Aktualisieren, Autostart wie bisher an, danach starten aus (lief nicht), Ergebnis, Exitcode 0
# Die App läuft in diesem Gate nie; die Ablage (config.json, Kanarien-Datei) muss nach jedem Schritt byte-gleich sein.
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

# Fortsetzung DIESES Laufs: Temp-Ordner laut eigenem Setup-Protokoll, Prozess darin mit --test-root dieses Testordners.
function Find-Continuation([long]$logOffset) {
    foreach ($dir in Get-OwnUninstallTempDirs (Get-LogSince $layout.SetupLog $logOffset)) {
        [void]$tempSeen.Add($dir)
        foreach ($p in @(Get-ProcessesUnder 'HDRpilot-Uninstall' $dir)) {
            if (Test-OwnTestProcess $p.Id $layout) { return $p }
        }
    }
    return $null
}

# Ablage byte-gleich mit dem Stand zu Beginn (die App läuft in diesem Gate nie).
function Assert-ConfigUnchanged([string]$step, [string[]]$before) {
    $diff = @(Compare-TreeHashes $before @(Get-TreeHashes $layout.Config))
    foreach ($line in $diff) { Note "     $line" }
    $hasConfig = @($before | Where-Object { $_ -like 'config.json|*' }).Count -eq 1
    Check ($hasConfig -and $diff.Count -eq 0) ("{0}: Ablage byte-gleich, alle {1} Dateien unter Config (mit config.json) haben dieselbe SHA-256" -f $step, @($before).Count)
}

# Steam-Spiel mit HDRpilot-Startoption wie beim Nutzer: Name in config.json (Quelle Steam), Startoption in
# <Testordner>\Steam\userdata\<Konto>\config\localconfig.vdf (im Prüflauf liest das Entfernen nur diesen Steam-Ordner).
function Add-SteamLaunchOption([string]$appId, [string]$name) {
    $file = Join-Path $layout.Config 'config.json'
    $json = [IO.File]::ReadAllText($file) | ConvertFrom-Json
    $json.games = @($json.games) + @([pscustomobject]@{ source = 'Steam'; sourceId = $appId; name = $name; hdrAuto = $false })
    [IO.File]::WriteAllText($file, ($json | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $vdf = Join-Path $layout.Root 'Steam\userdata\12345678\config\localconfig.vdf'
    New-Item -ItemType Directory -Force (Split-Path $vdf) | Out-Null
    $option = ('"{0}" --wrap -- %command%' -f $layout.Exe).Replace('\', '\\').Replace('"', '\"')
    $template = @'
"UserLocalConfigStore"
{
	"Software"
	{
		"Valve"
		{
			"Steam"
			{
				"apps"
				{
					"APPID"
					{
						"LaunchOptions"		"OPTION"
						"LastPlayed"		"1759000000"
					}
				}
			}
		}
	}
}
'@
    [IO.File]::WriteAllText($vdf, $template.Replace('APPID', $appId).Replace('OPTION', $option), (New-Object Text.UTF8Encoding $false))
}

try {
    Note '--- Voraussetzungen'
    Check (-not (Test-Elevated)) 'Prüflauf ohne Adminrechte (Skript nicht erhöht)'
    Check (Test-CompareRealStateCase) 'Gegenprobe: Vergleich des echten Stands erkennt eine Änderung nur in der Groß-/Kleinschreibung'
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
    $probe = @(Test-SetupModeAtRuntime $layout $setup (Join-Path $PortableDir 'HDRpilot.exe'))
    foreach ($p in $probe) { Note "  - $p" }
    Check ($probe.Count -eq 0) 'Probe: Setup-Datei und HDRpilot.exe --uninstall laufen im Setup-Modus (mit --quiet und unbekannten Schaltern: Exitcode 2, Setup-Protokoll, keine App)'
    if ($probe) { throw 'Setup-Modus wirkt zur Laufzeit nicht: die Setup-Datei liefe als normale App, deshalb kein Fenster-Start' }

    $canary = Initialize-SetupTestConfig $layout.Config
    $steamGame = 'Gate7 Steam-Spiel'
    Add-SteamLaunchOption '4711' $steamGame
    $autoHdr = Initialize-AutoHdrTestState $layout # G9.4: Spiel mit „Windows Auto HDR“, globaler Wert der Testwurzel
    $cfgStart = @(Get-TreeHashes $layout.Config)
    Note ("Ablage vorbereitet: Kanarien-Datei {0}, Steam-Spiel 4711 mit HDRpilot-Startoption, {1} Dateien" -f $canary.Substring(0, 8), $cfgStart.Count)

    Note '--- Setup-Fenster'
    $proc = Start-Exe $setup @('--test-root', $layout.Root, '--ui-language', 'de')
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
    # Testablage: Einstellungen und Daten im selben Ordner, der Hinweis nennt ihn nur einmal (SetupDataNoteSingle).
    Check ((Test-Contains $note $layout.Config) -and (Test-Template $note $s.SetupDataNoteSingle)) "Hinweis, dass Einstellungen erhalten bleiben: $note"
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
    Assert-ConfigUnchanged 'Nach dem Installieren' $cfgStart

    Note '--- Entfernen: Rückfrage'
    # G9.4: wie nach einem Absturz von HDRpilot mitten in einer Sitzung (Schalter an, Merker in der Ablage)
    Set-AutoHdrCrashLeftover $autoHdr
    Check ((Get-RegValue $autoHdr.Key $AutoHdrGlobalName) -ceq $autoHdr.On -and (Test-Path -LiteralPath $autoHdr.GlobalFile)) 'Absturz mit gesetztem Auto-HDR-Schalter nachgestellt (Testwurzel an, autohdr-global.json)'
    $logOffset = Get-FileLength $layout.SetupLog
    $first = Start-Exe $layout.Exe @('--uninstall', '--test-root', $layout.Root, '--ui-language', 'de')
    $uwin = Find-Window 'UninstallWindow' $first.Id 30000
    Check ($null -ne $uwin) "Rückfrage zum Entfernen geöffnet (UninstallWindow, PID $($first.Id))"
    if (-not $uwin) { throw 'Rückfrage fehlt' }
    Check ($uwin.Current.Name -ceq $s.UninstallWindowTitle) "Fenstertitel: $($uwin.Current.Name)"
    $utitle = Get-IdText $uwin 'UninstallTitle'
    Check ($utitle -ceq [string]::Format($s.UninstallTitle, $version)) "Überschrift: $utitle"
    $removeData = Find-Id $uwin 'UninstallRemoveData' 2000
    $removeState = if ($removeData) { [string](Get-Toggle $removeData) } else { 'fehlt' }
    Check ($removeData -and $removeState -eq 'Off' -and $removeData.Current.Name -ceq $s.UninstallRemoveData) ("Daten löschen standardmäßig aus: '{0}' {1}" -f $(if ($removeData) { $removeData.Current.Name } else { '' }), $removeState)
    $steam = Find-Id $uwin 'UninstallSteamWarning' 2000
    $steamText = if ($steam) { [string](Get-Text $steam) } else { '' }
    Check ((Test-Shown $steam) -and (($steamText -replace '\s+', ' ').Trim()).StartsWith(($s.UninstallSteamWarning -replace '\s+', ' ').Trim(), [StringComparison]::Ordinal)) "Warnung vor Steam-Startoptionen sichtbar: $steamText"
    $gameCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $steamGame)
    $gameEl = $uwin.FindFirst([Windows.Automation.TreeScope]::Descendants, $gameCondition)
    Check (Test-Shown $gameEl) "Warnung nennt sichtbar das Spiel mit HDRpilot-Startoption ($steamGame, Name aus config.json)"
    $confirm = Find-Id $uwin 'UninstallConfirmButton' 2000
    Check ($confirm -and $confirm.Current.Name -ceq $s.UninstallConfirmButton) ("Knopf {0}" -f $(if ($confirm) { $confirm.Current.Name } else { 'fehlt' }))
    $ucancel = Find-Id $uwin 'UninstallCancelButton' 2000
    Check ($ucancel -and $ucancel.Current.Name -ceq $s.UninstallCancelButton) ("Knopf {0}" -f $(if ($ucancel) { $ucancel.Current.Name } else { 'fehlt' }))
    if (-not $confirm) { throw 'Entfernen-Knopf fehlt' }
    Invoke-El $confirm

    Note '--- Entfernen: Fortsetzung aus dem Temp-Ordner'
    $cont = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $cont -and $sw.ElapsedMilliseconds -lt 30000) { $cont = Find-Continuation $logOffset; if (-not $cont) { Start-Sleep -Milliseconds 100 } }
    $firstExited = $first.WaitForExit(30000)
    Check ($firstExited -and $first.ExitCode -eq 0) ("Erste Instanz aus dem Programmordner beendet sich, Exitcode {0}" -f $(if ($firstExited) { $first.ExitCode } else { 'läuft noch' }))
    Check ($null -ne $cont) ("Fortsetzung läuft als neuer Prozess {0} (PID {1}, Befehlszeile mit --test-root dieses Laufs)" -f $(if ($cont) { $cont.Path } else { 'fehlt' }), $(if ($cont) { $cont.Id } else { '' }))
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
    Check (Test-Contains $usteps $s.UninstallStepRestoreAutoHdr) ("Fortschritt zeigt den Schritt ""{0}"" (G9.4)" -f $s.UninstallStepRestoreAutoHdr)
    Check (-not (Test-Contains $uresult $s.UninstallAutoHdrNotRestored)) 'Ergebnis ohne Hinweis auf einen nicht zurückgestellten Auto-HDR-Schalter'
    $autoHdrValue = Get-RegValue $autoHdr.Key $AutoHdrGlobalName
    Check ($autoHdrValue -ceq $autoHdr.Off -and -not (Test-Path -LiteralPath $autoHdr.GlobalFile)) ("Auto HDR zurückgestellt: globaler Wert wieder genau ""{0}"" (ist ""{1}""), autohdr-global.json gelöscht" -f $autoHdr.Off, $autoHdrValue)
    if ($uclose) { Invoke-El $uclose }
    $contExited = $cont.WaitForExit(20000)
    Check $contExited ("Fortsetzung nach Fertig beendet (Exitcode {0})" -f $(if ($contExited) { $cont.ExitCode } else { 'läuft noch' }))

    Check (-not (Test-Path $layout.UninstallKey)) 'Eintrag unter Installierte Apps (Test-Zweig) entfernt'
    Check (-not (Test-Path -LiteralPath $layout.InstallDir)) 'Programmordner entfernt'
    Check (-not (Test-Path -LiteralPath $layout.StartMenuLnk) -and -not (Test-Path -LiteralPath $layout.DesktopLnk)) 'Verknüpfungen entfernt'
    Check ($null -eq (Get-RegValue $layout.RunKey $RunValueName)) 'Autostart-Wert entfernt'
    Assert-ConfigUnchanged 'Nach dem Entfernen ohne Haken' $cfgStart
    $contDirs = @($tempSeen)
    $gone = Wait-Until { @($contDirs | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0 } 20000
    Check ($contDirs.Count -ge 1 -and $gone) ("Temp-Ordner der Fortsetzung hat sich innerhalb von 20 s selbst entfernt ({0})" -f ($contDirs -join ', '))

    Note '--- Setup-Fenster: Aktualisieren einer älteren, nicht eingetragenen Version (wie beim Nutzer)'
    $older = Initialize-OlderInstall $layout
    Check ($older.Version -eq '1.0.0' -and -not (Test-Path $layout.UninstallKey) -and (Test-Contains (Get-RegValue $layout.RunKey $RunValueName) $layout.Exe)) ("Vorher: Attrappe HDRpilot.exe {0} im Programmordner, Autostart-Wert zeigt darauf, kein Eintrag" -f $older.Version)
    $proc = Start-Exe $setup @('--test-root', $layout.Root, '--ui-language', 'de')
    $win = Find-Window 'SetupWindow' $proc.Id 30000
    Check ($null -ne $win) "Setup-Fenster geöffnet (SetupWindow, PID $($proc.Id))"
    if (-not $win) { throw 'Setup-Fenster zum Aktualisieren fehlt' }
    $title = Get-IdText $win 'SetupTitle'
    Check ($title -ceq $s.SetupTitleUpdate) "Überschrift: $title"
    $versionLine = Get-IdText $win 'SetupVersionInfo'
    Check ($versionLine -ceq [string]::Format($s.SetupVersionUpdate, $older.Version, $version)) "Version alt → neu: $versionLine"
    Check (Test-SamePath (Get-IdText $win 'SetupInstallDir') $layout.InstallDir) 'Programmordner wie bisher'
    Check (Test-Template (Get-IdText $win 'SetupDataNote') $s.SetupDataNoteSingle) 'Hinweis, dass Einstellungen erhalten bleiben'
    Check (-not (Test-Shown (Find-Id $win 'SetupRunningNote' 500))) 'Kein Hinweis auf laufendes HDRpilot (die frühere Version läuft nicht)'
    Check (-not (Test-Shown (Find-Id $win 'SetupAllowDowngrade' 500))) 'Kein Ankreuzfeld für ältere Version (Aktualisierung)'
    $expected = [ordered]@{ SetupAutostart = 'On'; SetupDesktopShortcut = 'Off'; SetupLaunchAfter = 'Off' }
    foreach ($id in $expected.Keys) {
        $box = Find-Id $win $id 2000
        $state = if ($box) { [string](Get-Toggle $box) } else { 'fehlt' }
        Check ($box -and $state -eq $expected[$id]) ("Option {0}: {1} (erwartet {2}: wie bisher, lief nicht)" -f $id, $state, $expected[$id])
    }
    $update = Find-Id $win 'SetupInstallButton' 2000
    Check ($update -and $update.Current.Name -ceq $s.SetupUpdateButton -and $update.Current.IsEnabled) ("Knopf {0} (statt {1})" -f $(if ($update) { $update.Current.Name } else { 'fehlt' }), $s.SetupInstallButton)
    if (-not $update) { throw 'Aktualisieren-Knopf fehlt' }
    Invoke-El $update
    $close = Wait-Shown $win 'SetupCloseButton' 120000
    Check ($null -ne $close) 'Fertig-Knopf erscheint nach dem Aktualisieren'
    $result = Get-IdText $win 'SetupResultText'
    Check ($result -ceq [string]::Format($s.SetupDoneUpdate, $version)) "Ergebnis: $result"
    Check ((Get-Sha $layout.Exe) -eq $portableHash) 'HDRpilot.exe ersetzt, byte-gleich mit der portablen'
    Check ((Get-RegValue $layout.UninstallKey 'DisplayVersion') -eq $version) "Eintrag unter Installierte Apps angelegt, Version $version"
    Check (Test-Contains (Get-RegValue $layout.RunKey $RunValueName) $layout.Exe) 'Autostart bleibt an'
    Check (-not (Test-InstanceRunning $layout.InstanceId)) 'HDRpilot nicht gestartet (Option war aus)'
    if ($close) { Invoke-El $close }
    $exited = $proc.WaitForExit(15000)
    Check ($exited -and $proc.ExitCode -eq 0) ("Setup nach Fertig beendet, Exitcode {0}" -f $(if ($exited) { $proc.ExitCode } else { 'läuft noch' }))
    Assert-ConfigUnchanged 'Nach dem Aktualisieren' $cfgStart
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
