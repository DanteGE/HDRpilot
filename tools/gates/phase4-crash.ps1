# Gate Phase 4: Wiederanlauf mit der App (wie G3.5, aber mit HDRpilot.exe statt hdrpilot watch).
# App bei laufendem Stellvertreter hart beenden (wie Task-Manager), Stellvertreter schließen, App neu starten:
# HDR wird zurückgestellt. Danach den Leerlauf der App messen (CPU, Handles, Speicher) ohne offenes Fenster.
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

$cfg = New-TestConfig 'phase4-crash' -Import
$game = New-StandInGame $cfg 'Stellvertreter Crash' $true
$reg = Get-PrimaryRegistryKey
if (-not $reg.Primary.hdrSupported) { Write-Output 'ABBRUCH: Hauptbildschirm meldet kein HDR'; exit 3 }
$initial = Test-HdrOn $reg
& $CliExe hdr off | Out-Null
try {
    $app = Start-App $cfg @('--minimized')
    Check ([bool](Wait-Log $cfg 'startet\. Konfiguration' 15000)) 'App läuft'
    Start-Sleep -Seconds 1
    $g = Start-Process $game.Exe -PassThru; [void]$Started.Add($g)
    $on = Wait-Hdr $reg $true 5000
    Note "HDR an nach $on ms"
    Check ($on -ge 0 -and $on -le 2000) 'HDR innerhalb von 2 s an (App, Weg A)'
    Start-Sleep -Seconds 1
    Stop-Process -Id $app.Id -Force
    Note "App hart beendet (PID $($app.Id))"
    Check (Test-Path (Join-Path $cfg 'session-state.json')) 'Gemerkter Zustand liegt auf der Platte'
    Stop-Process -Id $g.Id -Force
    Start-Sleep -Seconds 2
    Check (Test-HdrOn $reg) 'HDR bleibt nach dem Absturz zunächst an'
    $app2 = Start-App $cfg @('--minimized')
    $off = Wait-Hdr $reg $false 15000
    Note "HDR aus $off ms nach Neustart der App"
    Check ($off -ge 0) 'HDR nach Neustart zurückgestellt'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ((Test-Path (Join-Path $cfg 'session-state.json')) -and $sw.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 50 }
    Check (-not (Test-Path (Join-Path $cfg 'session-state.json'))) 'Gemerkter Zustand gelöscht'

    # Leerlauf der App ohne Fenster: 10 s Anlauf, dann 60 s messen.
    Start-Sleep -Seconds 10
    $p = Get-Process -Id $app2.Id
    $cpu0 = $p.TotalProcessorTime; $h0 = $p.HandleCount; $t0 = Get-Date; $ws0 = $p.WorkingSet64; $pv0 = $p.PrivateMemorySize64
    Start-Sleep -Seconds 60
    $p.Refresh()
    $pct = ($p.TotalProcessorTime - $cpu0).TotalMilliseconds / ((Get-Date) - $t0).TotalMilliseconds * 100
    Note ("App im Leerlauf: CPU {0:N3} % eines Kerns, Handles {1} -> {2}, Arbeitssatz {3:N1} -> {4:N1} MB, privat {5:N1} MB" -f $pct, $h0, $p.HandleCount, ($ws0 / 1MB), ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB))
    Check ($pct -le 0.5) 'CPU im Leerlauf <= 0,5 % eines Kerns'
    Check (($p.HandleCount - $h0) -le 20) 'keine wachsenden Handles'
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    $app2.WaitForExit(10000) | Out-Null
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally {
    Stop-Started
    & $CliExe hdr $(if ($initial) { 'on' } else { 'off' }) | Out-Null
}
if ($ok) { Write-Output 'PHASE4_CRASH_OK'; exit 0 }
exit 1
