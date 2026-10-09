# Gate G7.2: Setup Ende zu Ende mit der echten Setup-Datei, still (--quiet) und immer in einer eigenen Testablage
# (--test-root: Programmordner, Startmenü, Desktop, Ablage und ein eigener Registry-Zweig, siehe SetupLocations).
#   (0) Probe: Setup-Datei und installierte EXE laufen wirklich im Setup-Modus (harmloser Aufruf, Exitcode 2), erst
#       danach startet etwas echt
#   (a) Erstinstallation ohne Start: EXE byte-gleich mit der portablen, Beigaben, Startmenü-Verknüpfung, Eintrag unter
#       "Installierte Apps" im Test-Zweig, Autostart an, keine Desktop-Verknüpfung; Ablage byte-gleich
#   (b) Neuinstallation über die laufende App: alte Instanz endet über den Befehlsweg mit Exitcode 0, neue startet im
#       Infobereich, Ablage (Kanarien-Datei, Spieleliste) unverändert
#   (c) Optionen ändern: Desktop-Verknüpfung an, Autostart aus, danach nicht starten
#   (d) Entfernen still: kehrt sofort zurück, die Kopie im Temp-Ordner entfernt alles und danach sich selbst; Ablage
#       byte-gleich
#   (e) Aktualisieren einer älteren, nicht eingetragenen EXE (Weg vom portablen HDRpilot: EXE von Hand im Programmordner,
#       Autostart zeigt darauf, kein Eintrag): EXE und Beigaben byte-gleich ersetzt, Autostart bleibt, Eintrag neu;
#       Ablage byte-gleich
#   (f) erneut installieren mit Start (lief vorher nicht: mit Fenster), dann mit --remove-data entfernen (beendet die
#       laufende App, entfernt den vorhandenen Autostart-Wert): Ablage weg
#   (g) ohne Adminrechte: Skript nicht erhöht, Manifest asInvoker
# "Ablage byte-gleich": SHA-256 aller Dateien unter der Test-Ablage (config.json, Kanarien-Datei, Spiel, Protokolle) vor
# und nach dem Setup, jeweils ohne laufende Instanz (E83: Einstellungen fasst der Setup nie an). Der echte Stand (Eintrag,
# Autostart, Startmenü, installierte EXE, config.json, laufende Instanz) wird vorher und nachher verglichen. Keine Maus-
# oder Tastatureingaben, kein Spielstart, kein Netz.
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

function Invoke-Setup([string[]]$switches) { Invoke-Exe $setup (@($switches) + @('--test-root', $layout.Root)) 180000 }

function Get-Entry($values, [string]$name) { if ($values -and $values.ContainsKey($name)) { $values[$name].Value } else { $null } }

# Ablage byte-gleich mit dem Stand $before (aus Get-TreeHashes, ohne laufende Instanz erfasst).
function Assert-ConfigUnchanged([string]$step, [string[]]$before) {
    $diff = @(Compare-TreeHashes $before @(Get-TreeHashes $layout.Config))
    foreach ($line in $diff) { Note "     $line" }
    $hasConfig = @($before | Where-Object { $_ -like 'config.json|*' }).Count -eq 1
    Check ($hasConfig -and $diff.Count -eq 0) ("{0} Ablage byte-gleich: alle {1} Dateien unter Config (mit config.json) haben dieselbe SHA-256" -f $step, @($before).Count)
}

# Entfernen still starten und auf das Ergebnis der Kopie im Temp-Ordner warten (Zeile "ERGEBNIS: <Exit-Code>" im
# Setup-Protokoll) und darauf, dass $done gilt. Merkt sich die Temp-Ordner HDRpilot-Uninstall-*, die dieser Lauf laut
# eigenem Setup-Protokoll angelegt hat (nie die eines parallelen Laufs).
function Invoke-QuietUninstall([string[]]$extra, [scriptblock]$done) {
    $offset = Get-FileLength $layout.SetupLog
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $first = Start-Exe $layout.Exe (@('--uninstall', '--quiet') + @($extra) + @('--test-root', $layout.Root))
    $created = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    $firstSeconds = $null
    $result = $null
    $finished = $false
    while ($sw.ElapsedMilliseconds -lt 90000) {
        $log = Get-LogSince $layout.SetupLog $offset
        foreach ($dir in Get-OwnUninstallTempDirs $log) { [void]$created.Add($dir); [void]$tempSeen.Add($dir) }
        if ($null -eq $firstSeconds -and $first.HasExited) { $firstSeconds = $sw.Elapsed.TotalSeconds }
        $m = [regex]::Match($log, 'ERGEBNIS: (-?\d+)')
        if ($m.Success) { $result = [int]$m.Groups[1].Value }
        if ($null -ne $firstSeconds -and $null -ne $result -and (& $done)) { $finished = $true; break }
        Start-Sleep -Milliseconds 100
    }
    return [pscustomobject]@{
        FirstExited = $first.HasExited
        FirstSeconds = $firstSeconds
        FirstExitCode = $(if ($first.HasExited) { $first.ExitCode } else { $null })
        Result = $result
        Finished = $finished
        Seconds = $sw.Elapsed.TotalSeconds
        TempDirs = @($created)
    }
}

function Assert-Uninstalled([string]$step, $run, [string]$what) {
    Check ($run.FirstExited -and $run.FirstExitCode -eq 0 -and $run.FirstSeconds -lt 30) ("{0} HDRpilot.exe --uninstall --quiet {1}: erste Instanz kehrt sofort zurück, Exitcode {2} nach {3:N1} s" -f $step, $what, $run.FirstExitCode, $run.FirstSeconds)
    Check ($run.Result -eq 0) ("{0} Setup-Protokoll der Kopie: ERGEBNIS: {1} (alles erledigt nach {2:N1} s)" -f $step, $run.Result, $run.Seconds)
    Check (-not (Test-Path $layout.UninstallKey)) "$step Eintrag unter Installierte Apps (Test-Zweig) entfernt"
    $left = @($InstalledFiles | Where-Object { Test-Path -LiteralPath (Join-Path $layout.InstallDir $_) })
    Check ($left.Count -eq 0 -and -not (Test-Path -LiteralPath $layout.InstallDir)) ("$step Programmdateien und Programmordner entfernt" + $(if ($left) { ' (noch da: ' + ($left -join ', ') + ')' } else { '' }))
    Check (-not (Test-Path -LiteralPath $layout.StartMenuLnk) -and -not (Test-Path -LiteralPath $layout.DesktopLnk)) "$step Startmenü- und Desktop-Verknüpfung entfernt"
    Check ($null -eq (Get-RegValue $layout.RunKey $RunValueName)) "$step kein Autostart-Wert im Test-Zweig"
    Check (-not (Test-InstanceRunning $layout.InstanceId)) "$step HDRpilot läuft nicht"
    Check ($run.TempDirs.Count -ge 1) ("{0} Kopie zum Entfernen lief aus dem Temp-Ordner (laut Setup-Protokoll: {1})" -f $step, ($run.TempDirs -join ', '))
    $gone = Wait-Until { @($run.TempDirs | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0 } 20000
    Check ($run.TempDirs.Count -ge 1 -and $gone) "$step Temp-Ordner der Kopie hat sich innerhalb von 20 s selbst entfernt"
}

try {
    Note '--- Voraussetzungen'
    Check (-not (Test-Elevated)) '(g) Prüflauf ohne Adminrechte: Skript läuft nicht erhöht, Setup und App erben das Token'
    Check (Test-CompareRealStateCase) 'Gegenprobe: Vergleich des echten Stands erkennt eine Änderung nur in der Groß-/Kleinschreibung'
    if (-not (Test-Path $CliExe)) { throw "Kommandozeile fehlt ($CliExe), erst tools\gates\build.ps1 ausführen" }
    Update-SetupFile $setup | ForEach-Object { Note $_ }
    $bundle = Test-SetupBundle $setup $PortableDir
    foreach ($p in $bundle.Problems) { Note "  - $p" }
    Check ($bundle.Problems.Count -eq 0) ("Setup-Datei {0}: Abschluss, SHA-256 des EXE-Teils = portable EXE, Paket {1}" -f (Split-Path $setup -Leaf), (@($bundle.Entries) -join ', '))
    Check $bundle.AsInvoker '(g) Manifest der Setup-EXE: asInvoker, keine erhöhten Rechte'
    if ($bundle.Problems.Count -gt 0) { throw 'Setup-Datei unbrauchbar' }
    $missing = @(Get-MissingSetupMode)
    Check ($missing.Count -eq 0) ('Setup-Modus im Quelltext vorhanden' + $(if ($missing) { ': fehlt ' + ($missing -join '; ') } else { '' }))
    if ($missing) { throw 'Setup-Modus fehlt: die Setup-Datei liefe als normale App mit der echten Ablage, deshalb kein Start' }
    $portableHashes = @{}
    foreach ($f in $InstalledFiles) { $portableHashes[$f] = Get-Sha (Join-Path $PortableDir $f) }

    $layout = New-SetupTestLayout 'HDRpilot-Gate7'
    Note "Testablage $($layout.Root), Registry $($layout.RegistryBranch), Instanz $($layout.InstanceId)"
    Check (-not (Test-Path $layout.RegistryBranch)) 'Registry-Zweig der Testablage ist neu'

    Note '--- (0) Probe: Setup-Modus zur Laufzeit'
    $probe = @(Test-SetupModeAtRuntime $layout $setup (Join-Path $PortableDir 'HDRpilot.exe'))
    foreach ($p in $probe) { Note "  - $p" }
    Check ($probe.Count -eq 0) '(0) Setup-Datei und HDRpilot.exe --uninstall laufen im Setup-Modus (mit --quiet und unbekannten Schaltern: Exitcode 2, Setup-Protokoll der Testablage, keine App gestartet)'
    if ($probe) { throw 'Setup-Modus wirkt zur Laufzeit nicht: die Setup-Datei liefe als normale App, deshalb kein echter Start' }

    $canaryFile = Join-Path $layout.Config 'canary.bin'
    $canary = Initialize-SetupTestConfig $layout.Config
    $game = New-StandInGame $layout.Config 'Gate7 StandIn' $false
    Check ($game.Key -and $null -ne (Find-GameConfig (Read-Config $layout.Config) $game.Key)) "Ablage vorbereitet: Kanarien-Datei, Hinweise aus, Stellvertreter-Spiel $($game.Key)"

    Note '--- (a) Erstinstallation still, ohne Start'
    $cfgBefore = @(Get-TreeHashes $layout.Config)
    $a = Invoke-Setup @('--quiet', '--no-launch')
    Check ($a.Exited -and $a.ExitCode -eq 0) ("(a) Setup --quiet --no-launch: Exitcode {0} nach {1:N1} s" -f $a.ExitCode, $a.Seconds)
    foreach ($f in $InstalledFiles) {
        $h = Get-Sha (Join-Path $layout.InstallDir $f)
        Check ($h -and $h -eq $portableHashes[$f]) "(a) $f installiert, byte-gleich mit der Auslieferung"
    }
    $lnk = Get-ShortcutInfo $layout.StartMenuLnk
    Check ($lnk -and (Test-SamePath $lnk.Target $layout.Exe)) ("(a) Startmenü-Verknüpfung zeigt auf die installierte EXE ({0})" -f $(if ($lnk) { $lnk.Target } else { 'fehlt' }))
    $u = Get-RegValues $layout.UninstallKey
    Check ($null -ne $u) "(a) Eintrag unter Installierte Apps im Test-Zweig vorhanden"
    if ($u) { foreach ($name in @($u.Keys | Sort-Object)) { Note ("     {0} = {1} ({2})" -f $name, $u[$name].Value, $u[$name].Kind) } }
    Check ((Get-Entry $u 'DisplayName') -ceq 'HDRpilot') '(a) DisplayName HDRpilot'
    Check ((Get-Entry $u 'DisplayVersion') -eq $version) "(a) DisplayVersion $version"
    Check (([string](Get-Entry $u 'Publisher')).Trim().Length -gt 0) '(a) Publisher gesetzt'
    Check (Test-SamePath (Get-Entry $u 'InstallLocation') $layout.InstallDir) '(a) InstallLocation ist der Programmordner'
    $us = [string](Get-Entry $u 'UninstallString')
    Check ((Test-Contains $us $layout.Exe) -and $us -match '(^|\s)--uninstall(\s|$)' -and $us -match '--test-root') '(a) UninstallString: installierte EXE mit --uninstall und --test-root'
    $qs = [string](Get-Entry $u 'QuietUninstallString')
    Check ((Test-Contains $qs $layout.Exe) -and $qs -match '(^|\s)--uninstall(\s|$)' -and $qs -match '--quiet' -and $qs -match '--test-root') '(a) QuietUninstallString: mit --uninstall --quiet und --test-root'
    Check ($u -and $u.ContainsKey('NoModify') -and $u['NoModify'].Kind -eq 'DWord' -and [int]$u['NoModify'].Value -eq 1) '(a) NoModify = 1 (DWORD)'
    Check ($u -and $u.ContainsKey('EstimatedSize') -and [long]$u['EstimatedSize'].Value -gt 100000) ("(a) EstimatedSize {0} KB (> 100000)" -f (Get-Entry $u 'EstimatedSize'))
    $run = Get-RegValue $layout.RunKey $RunValueName
    Check ((Test-Contains $run $layout.Exe) -and $run -match '--minimized') "(a) Autostart im Test-Zweig an (neu: Standard an): $run"
    Check (-not (Test-Path -LiteralPath $layout.DesktopLnk)) '(a) keine Desktop-Verknüpfung (neu: Standard aus)'
    Check (-not (Test-InstanceRunning $layout.InstanceId)) '(a) --no-launch: HDRpilot läuft danach nicht'
    Assert-ConfigUnchanged '(a)' $cfgBefore

    Note '--- (b) Neuinstallation über die laufende App'
    $starts = Get-StartCount $layout.Config
    $app = Start-Exe $layout.Exe @('--config-dir', $layout.Config, '--minimized')
    $up = Wait-Until { (Get-StartCount $layout.Config) -gt $starts -and [HdrPilotGates.SetupFile]::CommandWindowExists($layout.InstanceId) } 90000
    Check $up ("(b) installierte App gestartet (PID {0}, Protokoll: startet, Befehlsfenster bereit)" -f $app.Id)
    $starts = Get-StartCount $layout.Config
    $b = Invoke-Setup @('--quiet')
    Check ($b.Exited -and $b.ExitCode -eq 0) ("(b) Setup --quiet bei laufender App (gleiche Version): Exitcode {0} nach {1:N1} s" -f $b.ExitCode, $b.Seconds)
    $oldGone = $app.WaitForExit(15000)
    Check ($oldGone -and $app.ExitCode -eq 0) ("(b) alte Instanz über den Befehlsweg sauber beendet (Exitcode {0})" -f $(if ($oldGone) { $app.ExitCode } else { 'läuft noch' }))
    $restarted = Wait-Until { (Get-StartCount $layout.Config) -gt $starts -and (Test-InstanceRunning $layout.InstanceId) } 60000
    Check $restarted "(b) neue Instanz gestartet (Protokoll: startet, Mutex Local\HdrPilot.Instance.$($layout.InstanceId))"
    $procs = @(Get-ProcessesUnder 'HDRpilot' $layout.InstallDir)
    Check ($procs.Count -eq 1 -and $procs[0].Id -ne $app.Id) ("(b) genau ein Prozess aus dem Programmordner, neue PID ({0})" -f (($procs | ForEach-Object { $_.Id }) -join ', '))
    if ($procs.Count -eq 1) {
        $cmd = [string](Get-CimInstance Win32_Process -Filter "ProcessId = $($procs[0].Id)").CommandLine
        Check ($cmd -match '--minimized' -and $cmd -match '--config-dir' -and (Test-Contains $cmd $layout.Config)) "(b) wieder im Infobereich gestartet (lief vorher), mit der Test-Ablage: $cmd"
    }
    Check ((Get-Sha $canaryFile) -eq $canary) '(b) Kanarien-Datei in der Ablage unverändert'
    Check ($null -ne (Find-GameConfig (Read-Config $layout.Config) $game.Key)) '(b) Stellvertreter-Spiel noch in config.json'
    Check ((Get-Sha $layout.Exe) -eq $portableHashes['HDRpilot.exe']) '(b) installierte EXE weiter byte-gleich mit der portablen'
    Check (Test-Contains (Get-RegValue $layout.RunKey $RunValueName) $layout.Exe) '(b) Autostart bleibt an (bisheriger Stand)'
    Check (-not (Test-Path -LiteralPath $layout.DesktopLnk)) '(b) Desktop-Verknüpfung bleibt aus (bisheriger Stand)'

    Note '--- (c) Optionen ändern: Desktop-Verknüpfung an, Autostart aus, nicht starten'
    $c = Invoke-Setup @('--quiet', '--desktop-shortcut', 'on', '--autostart', 'off', '--no-launch')
    Check ($c.Exited -and $c.ExitCode -eq 0) ("(c) Setup --quiet --desktop-shortcut on --autostart off --no-launch: Exitcode {0} nach {1:N1} s" -f $c.ExitCode, $c.Seconds)
    $desk = Get-ShortcutInfo $layout.DesktopLnk
    Check ($desk -and (Test-SamePath $desk.Target $layout.Exe)) ("(c) Desktop-Verknüpfung zeigt auf die installierte EXE ({0})" -f $(if ($desk) { $desk.Target } else { 'fehlt' }))
    Check ($null -eq (Get-RegValue $layout.RunKey $RunValueName)) '(c) Autostart-Wert im Test-Zweig entfernt'
    Check (Test-Path -LiteralPath $layout.StartMenuLnk) '(c) Startmenü-Verknüpfung bleibt'
    $stopped = Wait-Until { -not (Test-InstanceRunning $layout.InstanceId) -and @(Get-ProcessesUnder 'HDRpilot' $layout.InstallDir).Count -eq 0 } 10000
    Check $stopped '(c) laufende App für den Setup beendet (kein Prozess aus dem Programmordner) und wegen --no-launch nicht neu gestartet'
    Check ((Get-Sha $canaryFile) -eq $canary) '(c) Kanarien-Datei unverändert'

    Note '--- (d) Entfernen still, Ablage bleibt'
    $removed = {
        -not (Test-Path $layout.UninstallKey) -and -not (Test-Path -LiteralPath $layout.InstallDir) -and
        -not (Test-Path -LiteralPath $layout.StartMenuLnk) -and -not (Test-Path -LiteralPath $layout.DesktopLnk) -and
        -not (Test-InstanceRunning $layout.InstanceId)
    }
    $cfgBefore = @(Get-TreeHashes $layout.Config)
    $d = Invoke-QuietUninstall @() $removed
    Assert-Uninstalled '(d)' $d 'ohne --remove-data'
    Assert-ConfigUnchanged '(d)' $cfgBefore
    Check ((Get-Sha $canaryFile) -eq $canary -and $null -ne (Find-GameConfig (Read-Config $layout.Config) $game.Key)) '(d) Ablage bleibt: Kanarien-Datei unverändert, Stellvertreter-Spiel noch in der Spieleliste'

    Note '--- (e) Aktualisieren einer älteren, nicht eingetragenen EXE (wie beim Nutzer), ohne Start'
    $older = Initialize-OlderInstall $layout
    Check ($older.Version -eq '1.0.0' -and $older.ExeHash -ne $portableHashes['HDRpilot.exe']) ("(e) vorher: Attrappe HDRpilot.exe mit Dateiversion {0} und alte Beigaben im Programmordner" -f $older.Version)
    Check (-not (Test-Path $layout.UninstallKey) -and (Test-Contains (Get-RegValue $layout.RunKey $RunValueName) $layout.Exe)) '(e) vorher: kein Eintrag unter Installierte Apps, Autostart-Wert zeigt auf die EXE'
    $cfgBefore = @(Get-TreeHashes $layout.Config)
    $offset = Get-FileLength $layout.SetupLog
    $e = Invoke-Setup @('--quiet', '--no-launch')
    Check ($e.Exited -and $e.ExitCode -eq 0) ("(e) Setup --quiet --no-launch über Version {0}: Exitcode {1} nach {2:N1} s" -f $older.Version, $e.ExitCode, $e.Seconds)
    $eLog = Get-LogSince $layout.SetupLog $offset
    Check ($eLog -match 'Stand: Update\b') ("(e) Setup-Protokoll: {0}" -f $(if ($eLog -match '(Stand: Update[^\r\n]*)') { $Matches[1] } else { 'kein Stand: Update' }))
    foreach ($f in $InstalledFiles) {
        $h = Get-Sha (Join-Path $layout.InstallDir $f)
        Check ($h -and $h -eq $portableHashes[$f]) "(e) $f ersetzt, byte-gleich mit der Auslieferung"
    }
    $leftovers = @(Get-ChildItem -LiteralPath $layout.InstallDir -Recurse -File | ForEach-Object { $_.FullName.Substring($layout.InstallDir.Length + 1) } | Where-Object { $_ -notin $InstalledFiles })
    Check ($leftovers.Count -eq 0) ("(e) keine Reste der Aktualisierung (.new/.old) im Programmordner{0}" -f $(if ($leftovers) { ': ' + ($leftovers -join ', ') } else { '' }))
    Check ((Get-RegValue $layout.UninstallKey 'DisplayVersion') -eq $version) "(e) Eintrag unter Installierte Apps angelegt, DisplayVersion $version"
    $run = Get-RegValue $layout.RunKey $RunValueName
    Check ((Test-Contains $run $layout.Exe) -and $run -match '--minimized') "(e) Autostart bleibt an (vorbelegt aus dem bisherigen Wert): $run"
    Check (Test-Path -LiteralPath $layout.StartMenuLnk) '(e) Startmenü-Verknüpfung angelegt'
    Check (-not (Test-Path -LiteralPath $layout.DesktopLnk)) '(e) keine Desktop-Verknüpfung (bisher keine)'
    Check (-not (Test-InstanceRunning $layout.InstanceId)) '(e) --no-launch: HDRpilot läuft danach nicht'
    Assert-ConfigUnchanged '(e)' $cfgBefore

    Note '--- (f) Erneut installieren (lief vorher nicht: mit Fenster), dann mit --remove-data entfernen'
    $starts = Get-StartCount $layout.Config
    $f1 = Invoke-Setup @('--quiet')
    Check ($f1.Exited -and $f1.ExitCode -eq 0 -and (Get-Sha $layout.Exe) -eq $portableHashes['HDRpilot.exe']) ("(f) erneut installiert: Exitcode {0} nach {1:N1} s" -f $f1.ExitCode, $f1.Seconds)
    $launched = Wait-Until { (Get-StartCount $layout.Config) -gt $starts -and (Test-InstanceRunning $layout.InstanceId) } 60000
    $procs = @(Get-ProcessesUnder 'HDRpilot' $layout.InstallDir)
    $cmd = if ($procs.Count -eq 1) { [string](Get-CimInstance Win32_Process -Filter "ProcessId = $($procs[0].Id)").CommandLine } else { '' }
    Check ($launched -and $cmd -match '--show' -and (Test-Contains $cmd $layout.Config)) "(f) App startet danach mit Fenster (lief vorher nicht) und mit der Test-Ablage: $cmd"
    Check ((Get-Sha $canaryFile) -eq $canary) '(f) erhaltene Ablage weiter benutzt (Kanarien-Datei unverändert)'
    $run = Get-RegValue $layout.RunKey $RunValueName
    Check ((Test-Contains $run $layout.Exe) -and $run -match '--minimized') "(f) vor dem Entfernen: Autostart-Wert im Test-Zweig vorhanden: $run"
    $f2 = Invoke-QuietUninstall @('--remove-data') { (& $removed) -and -not (Test-Path -LiteralPath $layout.Config) }
    Assert-Uninstalled '(f)' $f2 'mit --remove-data'
    Check (-not (Test-Path -LiteralPath $layout.Config)) '(f) --remove-data: Ablage (Einstellungen, Spieleliste, Protokolle) gelöscht'
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
    Check ($diff.Count -eq 0) ("Echter Stand unverändert (Installierte Apps, Autostart, Startmenü, Desktop, installierte EXE, config.json, laufende Instanz: {0} Angaben)" -f $realBefore.Count)
}
if ($ok) { Write-Output 'PHASE7_SETUP_OK'; exit 0 }
Write-Output 'PHASE7_SETUP_FAILED'
exit 1
