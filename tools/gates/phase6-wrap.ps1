# Gate G6.2: Steam-Startoption "<exe>" --wrap -- %command% (so wie Steam sie ausführt, mit SteamAppId in der Umgebung).
#   Fall 1, Instanz läuft: HDR ist bestätigt an, bevor der gestartete Prozess existiert (Registry gegen Prozess-Startzeit);
#           der Wrapper endet mit 0, kurz nachdem das Spiel läuft (hält also kein Handle darauf); Rückstellung nach Spielende.
#   Fall 2, keine Instanz: das Spiel startet trotzdem sofort, HDR bleibt unverändert, der Wrapper startet kein HdrPilot.
# Stellvertreter-Spiel als Steam-Eintrag mit erfundener AppID in einer eigenen Test-Ablage; schaltet den echten Bildschirm.
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

$appId = '999999001'
$cfg = New-TestConfig 'phase6-wrap'
$game = New-StandInGame $cfg 'Stellvertreter Wrap' $false
# Den eigenen Eintrag zum Steam-Spiel machen: der Wrapper ordnet über SteamAppId zu ("Steam:<id>").
$configPath = Join-Path $cfg 'config.json'
$manualId = ($game.Key -split ':', 2)[1]
$json = [IO.File]::ReadAllText($configPath)
$json = $json.Replace('"source": "Manual"', '"source": "Steam"').Replace("`"sourceId`": `"$manualId`"", "`"sourceId`": `"$appId`"")
[IO.File]::WriteAllText($configPath, $json, (New-Object Text.UTF8Encoding $false))
$key = "Steam:$appId"

$reg = Get-PrimaryRegistryKey
if (-not $reg.Primary.hdrSupported) { Write-Output 'ABBRUCH: Hauptbildschirm meldet kein HDR'; exit 3 }
$initial = Test-HdrOn $reg
& $CliExe hdr off | Out-Null

function Invoke-Wrap {
    $env:SteamAppId = $appId
    try {
        return Start-Process $AppExe -WorkingDirectory (Split-Path $game.Exe) -PassThru -ArgumentList @('--config-dir', "`"$cfg`"", '--wrap', '--', "`"$($game.Exe)`"", '600')
    }
    finally { $env:SteamAppId = $null }
}

function Watch-Start($wrap, [int]$timeoutMs) {
    $r = [pscustomobject]@{ HdrOnAt = $null; ProcStart = $null; WrapExitAt = $null }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs -and ($null -eq $r.ProcStart -or $null -eq $r.WrapExitAt)) {
        if ($null -eq $r.HdrOnAt -and (Test-HdrOn $reg)) { $r.HdrOnAt = Get-Date }
        if ($null -eq $r.WrapExitAt -and $wrap.HasExited) { $r.WrapExitAt = Get-Date }
        $gp = Get-Process -Name $game.ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($gp -and $null -eq $r.ProcStart) { $r.ProcStart = $gp.StartTime; [void]$Started.Add($gp) }
        Start-Sleep -Milliseconds 10
    }
    return $r
}

try {
    # Fall 1: HdrPilot läuft
    $app = Start-App $cfg @('--minimized')
    Check ([bool](Wait-Log $cfg 'Einlesen:' 30000)) 'Instanz läuft und hat eingelesen'
    Start-Sleep -Seconds 1
    $wrap = Invoke-Wrap
    $r = Watch-Start $wrap 20000
    Check ($wrap.HasExited -and $wrap.ExitCode -eq 0) "Wrapper endet mit 0 ($($wrap.ExitCode))"
    Check ($null -ne $r.ProcStart) 'Spiel wurde gestartet'
    Check ($null -ne $r.HdrOnAt) 'HDR wurde eingeschaltet'
    if ($r.ProcStart -and $r.HdrOnAt) {
        $lead = ($r.ProcStart - $r.HdrOnAt).TotalMilliseconds
        Note ("HDR an (Registry) {0:HH:mm:ss.fff}, Spielprozess gestartet {1:HH:mm:ss.fff}, Vorlauf {2:N0} ms" -f $r.HdrOnAt, $r.ProcStart, $lead)
        Check ($lead -ge 1000) 'HDR war mindestens 1 s vor dem Prozessstart aktiv (Ruhezeit nach dem Umschalten)'
    }
    if ($r.ProcStart -and $r.WrapExitAt) {
        $after = ($r.WrapExitAt - $r.ProcStart).TotalMilliseconds
        Note ("Wrapper beendet {0:N0} ms nach dem Spielstart" -f $after)
        Check ($after -lt 3000 -and -not (Get-Process -Id $wrap.Id -ErrorAction SilentlyContinue)) 'Wrapper hat sich direkt nach dem Spielstart beendet (hält kein Handle)'
    }
    $log = Wait-Log $cfg 'Spielprozess erkannt um' 15000
    Check ([bool]$log) 'Instanz erkennt den Spielprozess'
    Check ($log -match 'Auslöser: Start über Steam-Startoption .*vorher: aus \| nachher: an') 'Schaltzeile mit Auslöser "Start über Steam-Startoption"'
    $wrapLog = Join-Path $cfg 'logs\wrap.log'
    Check ((Test-Path $wrapLog) -and ((Get-Content $wrapLog -Raw) -match $appId)) 'wrap.log enthält den Lauf'

    Stop-Process -Name $game.ProcessName -Force
    $off = Wait-Hdr $reg $false 200000
    Note "HDR aus $off ms nach Spielende"
    Check ($off -ge 9000) 'Zurückgestellt nach Spielende (Nachlaufzeit oder Neustart-Puffer)'

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Instanz beendet'

    # Fall 2: keine Instanz
    $before = @(Get-Process -Name 'HDRpilot' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $wrap2 = Invoke-Wrap
    $r2 = Watch-Start $wrap2 8000
    Check ($null -ne $r2.ProcStart) 'Ohne Instanz startet das Spiel trotzdem'
    Check ($wrap2.HasExited -and $wrap2.ExitCode -eq 0) "Wrapper endet mit 0 ($($wrap2.ExitCode))"
    if ($r2.ProcStart) {
        $delay = ($r2.ProcStart - $wrap2.StartTime).TotalMilliseconds
        Note ("Spiel {0:N0} ms nach dem Wrapper gestartet" -f $delay)
        Check ($delay -lt 3000) 'Ohne Instanz kein Warten'
    }
    Check (-not (Test-HdrOn $reg)) 'HDR blieb aus'
    $extra = @(Get-Process -Name 'HDRpilot' -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $before })
    Check ($extra.Count -eq 0) 'Wrapper hat kein HdrPilot gestartet'
    Stop-Process -Name $game.ProcessName -Force -ErrorAction SilentlyContinue
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally {
    Stop-Started
    Get-Process -Name $game.ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    & $CliExe hdr $(if ($initial) { 'on' } else { 'off' }) | Out-Null
}
if ($ok) { Write-Output 'PHASE6_WRAP_OK'; exit 0 }
exit 1
