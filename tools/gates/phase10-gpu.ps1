# Gate G10.9: Grafikkartenlast der Bibliothek „Showcase Hero“ am echten Desktop mit eingeschaltetem Bildschirm, gemessen an
# einer eigenen Testinstanz (eigene Ablage, echte Bibliothek eingelesen und alle Spiele auf „HDR automatisch“ aus, nur
# Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben). Gemessen wird die Auslastung der Grafik-Engines dieses
# Prozesses über WMI (Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine, sprachunabhängig), je Probe die
# ausgelastetste Engine-Art (wie der Task-Manager), einmal pro Sekunde.
#   - Offenes, aktives Fenster in Ruhe (Dauer-Effekte laufen): Mittel unter 5 %, nie über 20 %
#   - HDR-Moment (--test-hdr-moment, ohne HDR-Schaltung): Spitze unter 30 %
#   - Minimiert und inaktiv (Einstellungen davor): keine Bildaufrufe (unter 1 %)
#   - Fenster geschlossen: nach 10 s Aufräumen (Bilder freigeben, index.json schreiben) 30 s lang keine Grafiklast, CPU
#     höchstens 0,5 % eines Kerns wie im Infobereich ohne Fenster, Handles höchstens +20; der Leerlauf der Überwachung
#     wie bisher (phase3-idle.ps1, mit -SkipIdleGate aus)
# Ohne aktiven Bildschirm (gesperrt, Bildschirm aus oder schlafend, keine Zähler, kein messbarer Bildaufbau) meldet das
# Gate UMGEBUNG (Exit-Code 2) und gilt nicht als bestanden. Schaltet kein HDR, startet kein Spiel.
param(
    [int]$IdleSeconds = 20,
    [int]$QuietSeconds = 8,
    [int]$ClosedSettleSeconds = 10,
    [int]$ClosedSeconds = 30,
    [double]$MaxIdleMean = 5,
    [double]$MaxIdlePeak = 20,
    [double]$MaxMomentPeak = 30,
    [double]$MaxQuiet = 1,
    [double]$MaxClosedCpu = 0.5,
    [int]$MaxClosedHandleGrowth = 20,
    [switch]$SkipIdleGate
)
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }
function Umgebung([string]$why) {
    Note "UMGEBUNG $why"
    Stop-Started
    Write-Output 'PHASE10_GPU_UMGEBUNG'
    exit 2
}

# Eine Probe: Auslastung je Engine-Art des Prozesses summiert, davon die höchste (0, solange der Prozess nichts zeichnet)
function Gpu-Probe([int]$processId) {
    $rows = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "pid_$($processId)_*" })
    if ($rows.Count -eq 0) { return 0.0 }
    $byType = $rows | Group-Object { ($_.Name -split 'engtype_')[-1] } | ForEach-Object { ($_.Group | Measure-Object UtilizationPercentage -Sum).Sum }
    return [double](($byType | Measure-Object -Maximum).Maximum)
}

function Gpu-Measure([int]$processId, [int]$seconds, [string]$label) {
    $values = New-Object Collections.Generic.List[double]
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $seconds; $i++) {
        $values.Add((Gpu-Probe $processId))
        $wait = [int](($i + 1) * 1000 - $sw.ElapsedMilliseconds)
        if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
    }
    $mean = ($values | Measure-Object -Average).Average
    $peak = ($values | Measure-Object -Maximum).Maximum
    # Out-Host: sonst landete die Zeile im Rückgabewert der Funktion statt im Protokoll
    Note ("{0}: {1} Proben, Mittel {2:N2} %, Spitze {3:N2} % ({4})" -f $label, $values.Count, $mean, $peak, (($values | ForEach-Object { '{0:N0}' -f $_ }) -join ' ')) | Out-Host
    return [pscustomobject]@{ Mean = [double]$mean; Peak = [double]$peak }
}

# Protokolle der Testablage (die App schreibt gerade hinein)
function Read-Log([string]$cfg) {
    $parts = foreach ($file in Get-ChildItem (Join-Path $cfg 'logs') -Filter '*.log' -ErrorAction SilentlyContinue) {
        $fs = New-Object IO.FileStream($file.FullName, 'Open', 'Read', 'ReadWrite, Delete')
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8); $sr.ReadToEnd(); $sr.Dispose()
    }
    return ($parts -join '')
}

function Is-Active($win, [int]$processId) {
    try { $f = [Windows.Automation.AutomationElement]::FocusedElement; return $null -ne $f -and $f.Current.ProcessId -eq $processId } catch { return $false }
}

# --- Umgebung ---
if (Get-Process -Name 'LogonUI' -ErrorAction SilentlyContinue) { Umgebung 'Sitzung gesperrt (Anmeldebildschirm aktiv)' }
$probe = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction SilentlyContinue | Select-Object -First 1)
if ($probe.Count -eq 0) { Umgebung 'Keine Leistungsindikatoren der Grafikkarte (GPUEngine)' }
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.SystemInformation]::MonitorCount -lt 1) { Umgebung 'Kein Bildschirm angeschlossen' }

$cfg = New-TestConfig 'phase10-gpu' -Import
try {
    $config = Read-Config $cfg
    foreach ($game in @($config.games)) { $game.hdrAuto = $false; if ($game.PSObject.Properties['autoHdr']) { $game.autoHdr = $false } }
    [IO.File]::WriteAllText((Join-Path $cfg 'config.json'), ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    Note "Spiele in der Testablage: $(@($config.games).Count)"

    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    if (-not $win) { throw 'Hauptfenster fehlt' }
    $tp = $null
    if ($win.TryGetCurrentPattern([Windows.Automation.TransformPattern]::Pattern, [ref]$tp) -and $tp.Current.CanResize) { $tp.Resize(1600, 1000) }
    try { $win.SetFocus() } catch {}
    Start-Sleep -Seconds 6   # Laden der Bilder, Einblenden, JIT nicht mitmessen
    if (-not (Is-Active $win $app.Id)) { try { $win.SetFocus() } catch {}; Start-Sleep -Seconds 1 }
    if (-not (Is-Active $win $app.Id)) { Umgebung 'Fenster der Testinstanz lässt sich nicht aktivieren (kein interaktiver Desktop?)' }
    $effects = Get-Text (Find-Id $win 'EffectsState' 3000)
    Note "Statuszeile Effekte: $effects"
    # Ein ausgeschalteter oder schlafender Bildschirm meldet über DXGI 0 nits (die Kapsel zeigt dann „—“); dann misst
    # die Grafikkarte nicht, was der Nutzer sähe
    $peakEl = Find-Id $win 'NitsPeak' 3000
    $peakText = if ($peakEl) { [string]$peakEl.Current.Name } else { '' }
    Note ('Bildschirm-Kapsel: Spitze „{0}“' -f $peakText)
    if ($peakText -notmatch '\d') { Umgebung 'Der Bildschirm meldet keine Helligkeitswerte (DXGI 0 nits: ausgeschaltet oder im Ruhezustand)' }

    # --- Offenes Fenster in Ruhe ---
    $proc = Get-Process -Id $app.Id; $cpuStart = $proc.TotalProcessorTime; $timeStart = Get-Date
    $idle = Gpu-Measure $app.Id $IdleSeconds 'Offen in Ruhe'
    $proc.Refresh()
    Note ("Offen in Ruhe: CPU {0:N2} % eines Kerns (zeigt, dass die Dauer-Effekte zeichnen)" -f (($proc.TotalProcessorTime - $cpuStart).TotalMilliseconds / ((Get-Date) - $timeStart).TotalMilliseconds * 100))
    Check (Is-Active $win $app.Id) 'Fenster blieb während der Messung aktiv'

    # --- HDR-Moment ohne HDR-Schaltung ---
    $logBefore = (Read-Log $cfg).Length
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--test-hdr-moment') | Out-Null
    $moment = Gpu-Measure $app.Id 4 'HDR-Moment'
    $log = Read-Log $cfg
    $log = $log.Substring([Math]::Min($logBefore, $log.Length))
    $kind = if ($log -match 'HDR-Moment gespielt \((\w+)\)') { $Matches[1] } else { '?' }
    Note "HDR-Moment laut Protokoll: $kind"
    if ($moment.Peak -le 0 -and $idle.Peak -le 0) { Umgebung 'Kein messbarer Bildaufbau (Bildschirm aus oder im Energiesparmodus?)' }
    Check ($kind -eq 'Full') ('HDR-Moment in Stufe „Voll“ gespielt ({0})' -f $kind)
    Check ($idle.Mean -lt $MaxIdleMean -and $idle.Peak -le $MaxIdlePeak) ("Offen in Ruhe: Mittel {0:N2} % < {1} %, Spitze {2:N2} % <= {3} %" -f $idle.Mean, $MaxIdleMean, $idle.Peak, $MaxIdlePeak)
    Check ($moment.Peak -lt $MaxMomentPeak) ("HDR-Moment: Spitze {0:N2} % < {1} %" -f $moment.Peak, $MaxMomentPeak)
    Check (-not $log.Contains('HDR-Schaltung')) 'Keine HDR-Schaltung im Protokoll'

    # --- Inaktiv: Einstellungen der Testinstanz davor ---
    Invoke-El (Find-Id $win 'SettingsButton')
    $settings = Find-Window 'SettingsWindow' $app.Id 4000
    if (-not $settings) { $settings = Find-Id $win 'SettingsWindow' 6000 }   # als eigenes Fenster des Hauptfensters
    if ($settings) {
        Start-Sleep -Seconds 3
        $inactive = Gpu-Measure $app.Id $QuietSeconds 'Inaktiv (Einstellungen davor)'
        Check ($inactive.Peak -lt $MaxQuiet) ("Inaktiv: Spitze {0:N2} % < {1} % (Dauer-Effekte angehalten)" -f $inactive.Peak, $MaxQuiet)
        $cancel = Find-Id $settings 'CancelSettingsButton' 3000
        if ($cancel) { Invoke-El $cancel } else { $wp = $null; if ($settings.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$wp)) { $wp.Close() } }
        Start-Sleep -Seconds 1
    } else { Check $false 'Einstellungen der Testinstanz geöffnet' }

    # --- Minimiert ---
    $wp = $null
    if ($win.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$wp)) {
        $wp.SetWindowVisualState([Windows.Automation.WindowVisualState]::Minimized)
        Start-Sleep -Seconds 2
        $min = Gpu-Measure $app.Id $QuietSeconds 'Minimiert'
        Check ($min.Peak -lt $MaxQuiet) ("Minimiert: Spitze {0:N2} % < {1} % (keine Bildaufrufe)" -f $min.Peak, $MaxQuiet)

        # --- Geschlossen: Instanz läuft im Infobereich weiter ---
        $wp.Close()
        Start-Sleep -Seconds $ClosedSettleSeconds   # Aufräumen nach dem Schließen nicht mitmessen
        Check (-not $app.HasExited) 'Fenster geschlossen, Instanz läuft im Infobereich weiter'
        $proc = Get-Process -Id $app.Id
        $cpu0 = $proc.TotalProcessorTime; $t0 = Get-Date; $h0 = $proc.HandleCount
        $closed = Gpu-Measure $app.Id $ClosedSeconds 'Geschlossen'
        $proc.Refresh()
        $cpuPct = ($proc.TotalProcessorTime - $cpu0).TotalMilliseconds / ((Get-Date) - $t0).TotalMilliseconds * 100
        Note ("Geschlossen: CPU {0:N2} % eines Kerns, Handles {1} -> {2}" -f $cpuPct, $h0, $proc.HandleCount)
        Check ($closed.Peak -lt $MaxQuiet) ("Geschlossen: Spitze {0:N2} % < {1} %" -f $closed.Peak, $MaxQuiet)
        Check ($cpuPct -le $MaxClosedCpu) ("Geschlossen: CPU {0:N2} % <= {1} %" -f $cpuPct, $MaxClosedCpu)
        Check (($proc.HandleCount - $h0) -le $MaxClosedHandleGrowth) ("Geschlossen: Handles +{0} <= {1}" -f ($proc.HandleCount - $h0), $MaxClosedHandleGrowth)
    } else { Check $false 'Fenster lässt sich per UI-Automation minimieren' }

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally { Stop-Started }

# Leerlauf der Überwachung bei geschlossenem Fenster wie bisher
if ($ok -and -not $SkipIdleGate) {
    $idleGate = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'phase3-idle.ps1')
    $idleGate | ForEach-Object { Note "  phase3-idle: $_" }
    Check ($LASTEXITCODE -eq 0 -and ($idleGate -contains 'PHASE3_IDLE_OK')) 'Leerlauf wie bisher (phase3-idle.ps1: PHASE3_IDLE_OK)'
}
if ($ok) { Write-Output 'PHASE10_GPU_OK'; exit 0 }
exit 1
