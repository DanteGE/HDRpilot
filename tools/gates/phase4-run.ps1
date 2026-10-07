# Gate Phase 4: nur eine Instanz; ein zweiter Aufruf mit --run reicht an die laufende Instanz weiter (Weg B).
# Mit einem Stellvertreter-Spiel (HDR automatisch AUS, damit nur Weg B schaltet) wird belegt:
#   - der zweite Aufruf endet sofort mit 0, die erste Instanz startet das Spiel
#   - HDR war an, bevor der Spielprozess startete (Registry-Zeitpunkt gegen Prozess-Startzeit, dazu Protokoll)
#   - nach Spielende und Nachlaufzeit wird zurückgestellt
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

$cfg = New-TestConfig 'phase4-run'
$game = New-StandInGame $cfg 'Stellvertreter Run' $false
$reg = Get-PrimaryRegistryKey
if (-not $reg.Primary.hdrSupported) { Write-Output 'ABBRUCH: Hauptbildschirm meldet kein HDR'; exit 3 }
$initial = Test-HdrOn $reg
& $CliExe hdr off | Out-Null
try {
    $app = Start-App $cfg @('--minimized')
    Check ([bool](Wait-Log $cfg 'startet\. Konfiguration' 15000)) 'Erste Instanz läuft (minimiert)'
    Start-Sleep -Seconds 1

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $second = Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--run', $game.Key) -PassThru
    $hdrOnAt = $null
    $procStart = $null
    while ($sw.ElapsedMilliseconds -lt 20000 -and ($null -eq $hdrOnAt -or $null -eq $procStart)) {
        if ($null -eq $hdrOnAt -and (Test-HdrOn $reg)) { $hdrOnAt = Get-Date }
        $gp = Get-Process -Name $game.ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($gp -and $null -eq $procStart) { $procStart = $gp.StartTime; [void]$Started.Add($gp) }
        Start-Sleep -Milliseconds 10
    }
    Check ($second.WaitForExit(10000) -and $second.ExitCode -eq 0) "Zweiter Aufruf beendet sich mit 0 ($($second.ExitCode))"
    Check ($null -ne $procStart) 'Stellvertreter wurde über die erste Instanz gestartet'
    Check ($null -ne $hdrOnAt) 'HDR wurde eingeschaltet'
    if ($procStart -and $hdrOnAt) {
        $lead = ($procStart - $hdrOnAt).TotalMilliseconds
        Note ("HDR an (Registry) {0:HH:mm:ss.fff}, Spielprozess gestartet {1:HH:mm:ss.fff}, Vorlauf {2:N0} ms" -f $hdrOnAt, $procStart, $lead)
        Check ($lead -gt 0) 'HDR war vor dem Prozessstart aktiv (unabhängig gemessen)'
    }

    $log = Wait-Log $cfg 'Spielprozess erkannt um' 15000
    Check ([bool]$log) 'Protokoll meldet den erkannten Spielprozess'
    if ($log) {
        $t1 = [regex]::Match($log, 'HDR-Schritt für "[^"]+" abgeschlossen um ([\d\-: .]+)\.').Groups[1].Value
        $t2 = [regex]::Match($log, 'Start-Befehl ausgeführt um ([\d\-: .]+):').Groups[1].Value
        $t3 = [regex]::Match($log, 'Spielprozess erkannt um ([\d\-: .]+):').Groups[1].Value
        Note "Protokoll: HDR bestätigt $t1 | Start-Befehl $t2 | Prozess erkannt $t3"
        if ($t1 -and $t2 -and $t3) {
            $fmt = 'yyyy-MM-dd HH:mm:ss.fff'; $ci = [Globalization.CultureInfo]::InvariantCulture; $d1 = [datetime]::ParseExact($t1.Trim(), $fmt, $ci); $d2 = [datetime]::ParseExact($t2.Trim(), $fmt, $ci); $d3 = [datetime]::ParseExact($t3.Trim(), $fmt, $ci)
            Check ($d1 -le $d2 -and $d2 -le $d3) 'Zeitstempel in der Reihenfolge HDR bestätigt <= Start <= Prozess erkannt'
        } else { Check $false 'alle drei Zeitstempel im Protokoll' }
        Check ($log -match 'HDR-Schaltung .*Auslöser: Start über die App .*vorher: aus \| nachher: an') 'Schaltzeile mit Auslöser "Start über die App"'
    }

    Stop-Process -Name $game.ProcessName -Force
    $off = Wait-Hdr $reg $false 16000
    Note "HDR aus $off ms nach Spielende"
    Check ($off -ge 9000 -and $off -le 13500) 'Zurückgestellt nach der Nachlaufzeit'

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Beenden über weitergereichten Befehl'
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally {
    Stop-Started
    & $CliExe hdr $(if ($initial) { 'on' } else { 'off' }) | Out-Null
}
if ($ok) { Write-Output 'PHASE4_SINGLE_INSTANCE_OK'; Write-Output 'PHASE4_RUN_OK'; exit 0 }
exit 1
