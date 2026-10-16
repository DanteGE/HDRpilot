# Gate G10.9: Grafikkartenlast der Bibliothek „Showcase Hero“ am echten Desktop mit eingeschaltetem Bildschirm, gemessen an
# einer eigenen Testinstanz (eigene Ablage, echte Bibliothek eingelesen und alle Spiele auf „HDR automatisch“ aus, nur
# Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben). Gemessen wird die Busy-Zeit der Grafik-Engines dieses
# Prozesses über WMI (Win32_PerfRawData_GPUPerformanceCounters_GPUEngine, sprachunabhängig, mit Nachkommastellen; nur
# ohne Rohdaten die ganzzahligen Prozente), je Probe die ausgelastetste Engine-Art (wie der Task-Manager), einmal pro
# Sekunde.
#   Teil 1, ohne Bildschirmwerte prüfbar:
#   - Inaktiv (Dialog „Spiel hinzufügen“ davor, er hat keine Dauer-Effekte; der Fokus steht auf einem Knopf, damit kein
#     blinkender Cursor die Zeichenschleife wachhält) und minimiert: die Statuszeile meldet die Pause, CPU der Instanz
#     höchstens 2 % eines Kerns (fängt eine durchlaufende Zeichenschleife; das Nachladen der Bilder nach dem Öffnen kostet
#     allein bis etwa 1,5 %), mit Bildschirm auch: Grafikkarte unter 1 %
#   Teil 2, braucht den eingeschalteten Bildschirm:
#   - Offenes, aktives Fenster in Ruhe (Dauer-Effekte laufen): Liste bei 1600×1000, Regal und Raster bei 3440×1440 (der
#     größte bewegte Titelbereich): je Mittel unter 5 %, nie über 20 %
#   - HDR-Moment (--test-hdr-moment, ohne HDR-Schaltung, mit heller Fassung): Spitze unter 30 %
#   Teil 3, ohne Bildschirm prüfbar:
#   - Fenster geschlossen: nach 10 s Aufräumen (Bilder freigeben, index.json schreiben) 30 s lang CPU höchstens 0,5 % eines
#     Kerns wie im Infobereich ohne Fenster, Handles höchstens +20 (mit Bildschirm auch: keine Grafiklast); der Leerlauf
#     der Überwachung wie bisher (phase3-idle.ps1, mit -SkipIdleGate aus)
# Ohne aktiven Bildschirm (gesperrt, Bildschirm aus oder schlafend, keine Zähler, kein messbarer Bildaufbau) laufen Teil 1
# und 3 trotzdem; das Gate meldet dann UMGEBUNG (Exit-Code 2) und gilt nicht als bestanden, ein Fehler in Teil 1 oder 3
# meldet Exit-Code 1. Schaltet kein HDR, startet kein Spiel.
param(
    [int]$IdleSeconds = 20,
    [int]$QuietSeconds = 8,
    [int]$ClosedSettleSeconds = 10,
    [int]$ClosedSeconds = 30,
    [int]$WideWidth = 3440,
    [int]$WideHeight = 1440,
    [double]$MaxIdleMean = 5,
    [double]$MaxIdlePeak = 20,
    [double]$MaxMomentPeak = 30,
    [double]$MaxQuiet = 1,
    [double]$MaxPausedCpu = 2,
    [double]$MaxClosedCpu = 0.5,
    [int]$MaxClosedHandleGrowth = 20,
    [switch]$SkipIdleGate
)
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
$umgebung = $null
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

# Rohdaten der Engines des Prozesses: Busy-Zeit (100 ns) und Zeitstempel je Engine-Art; leer ohne Zähler
function Gpu-Raw([int]$processId) {
    $result = @{}
    $rows = @(Get-CimInstance -ClassName Win32_PerfRawData_GPUPerformanceCounters_GPUEngine -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "pid_$($processId)_*" })
    foreach ($row in $rows) {
        $type = ($row.Name -split 'engtype_')[-1]
        if (-not $result.ContainsKey($type)) { $result[$type] = [pscustomobject]@{ Busy = [double]0; Time = [double]$row.Timestamp_Sys100NS } }
        $result[$type].Busy += [double]$row.UtilizationPercentage
    }
    return $result
}

# Ohne Rohdaten: formatierte Prozente (ganze Zahlen) wie bisher
function Gpu-Formatted([int]$processId) {
    $rows = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "pid_$($processId)_*" })
    if ($rows.Count -eq 0) { return 0.0 }
    $byType = $rows | Group-Object { ($_.Name -split 'engtype_')[-1] } | ForEach-Object { ($_.Group | Measure-Object UtilizationPercentage -Sum).Sum }
    return [double](($byType | Measure-Object -Maximum).Maximum)
}

$script:rawCounters = @(Get-CimInstance -ClassName Win32_PerfRawData_GPUPerformanceCounters_GPUEngine -ErrorAction SilentlyContinue | Select-Object -First 1).Count -gt 0

function Gpu-Measure([int]$processId, [int]$seconds, [string]$label) {
    $values = New-Object Collections.Generic.List[double]
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $previous = if ($rawCounters) { Gpu-Raw $processId } else { $null }
    for ($i = 0; $i -lt $seconds; $i++) {
        $wait = [int](($i + 1) * 1000 - $sw.ElapsedMilliseconds)
        if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
        if ($rawCounters) {
            $now = Gpu-Raw $processId
            $peak = 0.0
            foreach ($type in $now.Keys) {
                if ($previous.ContainsKey($type) -and $now[$type].Time -gt $previous[$type].Time) {
                    $share = 100.0 * ($now[$type].Busy - $previous[$type].Busy) / ($now[$type].Time - $previous[$type].Time)
                    if ($share -gt $peak) { $peak = $share }
                }
            }
            $values.Add([Math]::Max(0.0, $peak))
            $previous = $now
        } else {
            $values.Add((Gpu-Formatted $processId))
        }
    }
    $mean = ($values | Measure-Object -Average).Average
    $peakAll = ($values | Measure-Object -Maximum).Maximum
    # Out-Host: sonst landete die Zeile im Rückgabewert der Funktion statt im Protokoll
    Note ("{0}: {1} Proben, Mittel {2:N2} %, Spitze {3:N2} % ({4})" -f $label, $values.Count, $mean, $peakAll, (($values | ForEach-Object { '{0:N1}' -f $_ }) -join ' ')) | Out-Host
    return [pscustomobject]@{ Mean = [double]$mean; Peak = [double]$peakAll }
}

# CPU der Instanz in % eines Kerns während einer Messung der Grafikkarte
function Cpu-During([int]$processId, [scriptblock]$measure) {
    $proc = Get-Process -Id $processId
    $cpu0 = $proc.TotalProcessorTime; $t0 = Get-Date
    $result = & $measure
    $proc.Refresh()
    $cpu = ($proc.TotalProcessorTime - $cpu0).TotalMilliseconds / ((Get-Date) - $t0).TotalMilliseconds * 100
    return [pscustomobject]@{ Cpu = $cpu; Gpu = $result }
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

function Effects-Text($win) { $e = Find-Id $win 'EffectsState' 3000; if ($e) { [string](Get-Text $e) } else { '' } }

function Resize-To($win, [int]$w, [int]$h) {
    $tp = $null
    if ($win.TryGetCurrentPattern([Windows.Automation.TransformPattern]::Pattern, [ref]$tp) -and $tp.Current.CanResize) { $tp.Resize($w, $h); Start-Sleep -Milliseconds 1500 }
    $r = $win.Current.BoundingRectangle
    Note ("Fenstergröße {0:N0}×{1:N0}" -f $r.Width, $r.Height)
}

# --- Umgebung ---
if (Get-Process -Name 'LogonUI' -ErrorAction SilentlyContinue) { Note 'UMGEBUNG Sitzung gesperrt (Anmeldebildschirm aktiv)'; Write-Output 'PHASE10_GPU_UMGEBUNG'; exit 2 }
$counters = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction SilentlyContinue | Select-Object -First 1).Count -gt 0
if (-not $counters) { $umgebung = 'Keine Leistungsindikatoren der Grafikkarte (GPUEngine)' }
Note ("Grafik-Zähler: {0}" -f $(if ($rawCounters) { 'Rohdaten (Busy-Zeit, Nachkommastellen)' } elseif ($counters) { 'nur formatierte Prozente (ganze Zahlen)' } else { 'keine' }))
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.SystemInformation]::MonitorCount -lt 1) { $umgebung = 'Kein Bildschirm angeschlossen' }

$cfg = New-TestConfig 'phase10-gpu' -Import
$inactive = $null; $minimized = $null
try {
    $config = Read-Config $cfg
    foreach ($game in @($config.games)) { $game.hdrAuto = $false; if ($game.PSObject.Properties['autoHdr']) { $game.autoHdr = $false } }
    [IO.File]::WriteAllText((Join-Path $cfg 'config.json'), ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    Note "Spiele in der Testablage: $(@($config.games).Count)"

    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    if (-not $win) { throw 'Hauptfenster fehlt' }
    Resize-To $win 1600 1000
    try { $win.SetFocus() } catch {}
    Start-Sleep -Seconds 12   # Laden der Bilder, Einblenden, JIT nicht mitmessen
    if (-not (Is-Active $win $app.Id)) { try { $win.SetFocus() } catch {}; Start-Sleep -Seconds 1 }
    $active = Is-Active $win $app.Id
    if (-not $active) { $umgebung = 'Fenster der Testinstanz lässt sich nicht aktivieren (kein interaktiver Desktop?)' }
    $effects = Effects-Text $win
    Note "Statuszeile Effekte (aktiv): $effects"
    if ($active) { Check ($effects -match '^Effekte Voll') ('Aktives Fenster: Effekte laufen („{0}“)' -f $effects) }

    # ===== Teil 1: Pause im Hintergrund und minimiert (ohne Bildschirmwerte prüfbar) =====
    Invoke-El (Find-Id $win 'AddGameButton')
    $dialog = Find-Id $win 'AddGameDialog' 6000
    if (-not $dialog) { $dialog = Find-Window 'AddGameDialog' $app.Id 4000 }
    if ($dialog) {
        try { (Find-Id $dialog 'AddGameCancel' 3000).SetFocus() } catch {}   # kein blinkender Cursor im Eingabefeld
        Start-Sleep -Seconds 3   # Einblenden des Dialogs (einmalig, 0,22 s) nicht mitmessen
        $text = Effects-Text $win
        Note "Statuszeile Effekte (Dialog davor): $text"
        Check ($text -match 'Hintergrund') ('Inaktiv: Statuszeile meldet die Pause im Hintergrund („{0}“)' -f $text)
        $inactive = Cpu-During $app.Id { Gpu-Measure $app.Id $QuietSeconds 'Inaktiv (Dialog „Spiel hinzufügen“ davor)' }
        Note ("Inaktiv: CPU {0:N2} % eines Kerns" -f $inactive.Cpu)
        Check ($inactive.Cpu -le $MaxPausedCpu) ("Inaktiv: CPU {0:N2} % <= {1} % (Dauer-Effekte angehalten)" -f $inactive.Cpu, $MaxPausedCpu)
        $cancel = Find-Id $dialog 'AddGameCancel' 3000
        if ($cancel) { Invoke-El $cancel } else { $dp = $null; if ($dialog.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$dp)) { $dp.Close() } }
        Start-Sleep -Seconds 1
    } else { Check $false 'Dialog „Spiel hinzufügen“ der Testinstanz geöffnet' }

    $wp = $null
    if ($win.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$wp)) {
        $wp.SetWindowVisualState([Windows.Automation.WindowVisualState]::Minimized)
        Start-Sleep -Seconds 2
        $text = Effects-Text $win
        Note "Statuszeile Effekte (minimiert): $text"
        Check ($text -match 'minimiert') ('Minimiert: Statuszeile meldet die Pause („{0}“)' -f $text)
        $minimized = Cpu-During $app.Id { Gpu-Measure $app.Id $QuietSeconds 'Minimiert' }
        Note ("Minimiert: CPU {0:N2} % eines Kerns" -f $minimized.Cpu)
        Check ($minimized.Cpu -le $MaxPausedCpu) ("Minimiert: CPU {0:N2} % <= {1} % (keine Bildaufrufe)" -f $minimized.Cpu, $MaxPausedCpu)
        $wp.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
        Start-Sleep -Seconds 2
        try { $win.SetFocus() } catch {}
        Start-Sleep -Seconds 1
    } else { Check $false 'Fenster lässt sich per UI-Automation minimieren' }

    # ===== Teil 2: Grafikkarte bei offenem Fenster (braucht den eingeschalteten Bildschirm) =====
    # Ein ausgeschalteter oder schlafender Bildschirm meldet über DXGI 0 nits (die Kapsel nennt dann keinen Wert); dann
    # misst die Grafikkarte nicht, was der Nutzer sähe
    $peakEl = Find-Id $win 'NitsPeak' 3000
    $peakText = if ($peakEl) { [string]$peakEl.Current.Name } else { '' }
    Note ('Bildschirm-Kapsel: „{0}“' -f $peakText)
    if (-not $umgebung -and $peakText -notmatch '\d') { $umgebung = 'Der Bildschirm meldet keine Helligkeitswerte (DXGI 0 nits: ausgeschaltet oder im Ruhezustand)' }
    if (-not $umgebung -and -not (Is-Active $win $app.Id)) { $umgebung = 'Fenster nach dem Wiederherstellen nicht aktiv' }

    if (-not $umgebung) {
        if ($inactive) { Check ($inactive.Gpu.Peak -lt $MaxQuiet) ("Inaktiv: Grafikkarte Spitze {0:N2} % < {1} %" -f $inactive.Gpu.Peak, $MaxQuiet) }
        if ($minimized) { Check ($minimized.Gpu.Peak -lt $MaxQuiet) ("Minimiert: Grafikkarte Spitze {0:N2} % < {1} %" -f $minimized.Gpu.Peak, $MaxQuiet) }

        $idle = Cpu-During $app.Id { Gpu-Measure $app.Id $IdleSeconds 'Liste 1600×1000 in Ruhe' }
        Note ("Liste in Ruhe: CPU {0:N2} % eines Kerns (zeigt, dass die Dauer-Effekte zeichnen)" -f $idle.Cpu)
        Check (Is-Active $win $app.Id) 'Fenster blieb während der Messung aktiv'

        # HDR-Moment ohne HDR-Schaltung, mit heller Fassung (die Instanz lädt sie vor dem Abspielen)
        $logBefore = (Read-Log $cfg).Length
        Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--test-hdr-moment') | Out-Null
        $moment = Gpu-Measure $app.Id 4 'HDR-Moment'
        Start-Sleep -Milliseconds 500
        $log = Read-Log $cfg
        $log = $log.Substring([Math]::Min($logBefore, $log.Length))
        $kind = if ($log -match 'HDR-Moment gespielt \((\w+), helle Fassung (\w+)\)') { $Matches[1] + ', helle Fassung ' + $Matches[2] } else { '?' }
        Note "HDR-Moment laut Protokoll: $kind"
        if ($moment.Peak -le 0 -and $idle.Gpu.Peak -le 0) { $umgebung = 'Kein messbarer Bildaufbau (Bildschirm aus oder im Energiesparmodus?)' }
    }

    if (-not $umgebung) {
        Check ($kind -eq 'Full, helle Fassung da') ('HDR-Moment in Stufe „Voll“ mit heller Fassung gespielt ({0})' -f $kind)
        Check ($idle.Gpu.Mean -lt $MaxIdleMean -and $idle.Gpu.Peak -le $MaxIdlePeak) ("Liste in Ruhe: Mittel {0:N2} % < {1} %, Spitze {2:N2} % <= {3} %" -f $idle.Gpu.Mean, $MaxIdleMean, $idle.Gpu.Peak, $MaxIdlePeak)
        Check ($moment.Peak -lt $MaxMomentPeak) ("HDR-Moment: Spitze {0:N2} % < {1} %" -f $moment.Peak, $MaxMomentPeak)
        Check (-not $log.Contains('HDR-Schaltung')) 'Keine HDR-Schaltung im Protokoll'

        # Größter bewegter Titelbereich: Regal und Raster im Vollbild des Nutzers
        Resize-To $win $WideWidth $WideHeight
        foreach ($view in @(@('ViewShelfButton', 'Regal'), @('ViewGridButton', 'Raster'))) {
            Select-El (Find-Id $win $view[0] 3000)
            try { $win.SetFocus() } catch {}
            Start-Sleep -Seconds 4   # Wechsel der Ansicht, Bilder in der neuen Größe
            $wide = Cpu-During $app.Id { Gpu-Measure $app.Id $IdleSeconds ("{0} {1}×{2} in Ruhe" -f $view[1], $WideWidth, $WideHeight) }
            Note ("{0} in Ruhe: CPU {1:N2} % eines Kerns" -f $view[1], $wide.Cpu)
            Check ((Is-Active $win $app.Id) -and $wide.Gpu.Mean -lt $MaxIdleMean -and $wide.Gpu.Peak -le $MaxIdlePeak) ("{0} {1}×{2} in Ruhe: Mittel {3:N2} % < {4} %, Spitze {5:N2} % <= {6} %" -f $view[1], $WideWidth, $WideHeight, $wide.Gpu.Mean, $MaxIdleMean, $wide.Gpu.Peak, $MaxIdlePeak)
        }
    }

    # ===== Teil 3: geschlossen, die Instanz läuft im Infobereich weiter (ohne Bildschirm prüfbar) =====
    $wp = $null
    if ($win.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$wp)) {
        $wp.Close()
        Start-Sleep -Seconds $ClosedSettleSeconds   # Aufräumen nach dem Schließen nicht mitmessen
        Check (-not $app.HasExited) 'Fenster geschlossen, Instanz läuft im Infobereich weiter'
        $proc = Get-Process -Id $app.Id
        $h0 = $proc.HandleCount
        $closed = Cpu-During $app.Id { Gpu-Measure $app.Id $ClosedSeconds 'Geschlossen' }
        $proc.Refresh()
        Note ("Geschlossen: CPU {0:N2} % eines Kerns, Handles {1} -> {2}" -f $closed.Cpu, $h0, $proc.HandleCount)
        if (-not $umgebung) { Check ($closed.Gpu.Peak -lt $MaxQuiet) ("Geschlossen: Grafikkarte Spitze {0:N2} % < {1} %" -f $closed.Gpu.Peak, $MaxQuiet) }
        Check ($closed.Cpu -le $MaxClosedCpu) ("Geschlossen: CPU {0:N2} % <= {1} %" -f $closed.Cpu, $MaxClosedCpu)
        Check (($proc.HandleCount - $h0) -le $MaxClosedHandleGrowth) ("Geschlossen: Handles +{0} <= {1}" -f ($proc.HandleCount - $h0), $MaxClosedHandleGrowth)
    } else { Check $false 'Fenster lässt sich per UI-Automation schließen' }

    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally { Stop-Started }

if (-not $ok) { exit 1 }
if ($umgebung) {
    Note "UMGEBUNG $umgebung (Teil 1 und 3 bestanden, die Grafikkarte ist ohne aktiven Bildschirm nicht messbar)"
    Write-Output 'PHASE10_GPU_UMGEBUNG'
    exit 2
}

# Leerlauf der Überwachung bei geschlossenem Fenster wie bisher
if (-not $SkipIdleGate) {
    $idleGate = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'phase3-idle.ps1')
    $idleGate | ForEach-Object { Note "  phase3-idle: $_" }
    Check ($LASTEXITCODE -eq 0 -and ($idleGate -contains 'PHASE3_IDLE_OK')) 'Leerlauf wie bisher (phase3-idle.ps1: PHASE3_IDLE_OK)'
}
if ($ok) { Write-Output 'PHASE10_GPU_OK'; exit 0 }
exit 1
