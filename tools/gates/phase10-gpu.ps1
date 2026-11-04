# Gate G10.9: Grafikkartenlast der Bibliothek „Showcase Hero“ am echten Desktop mit eingeschaltetem Bildschirm, gemessen an
# einer eigenen Testinstanz (eigene Ablage, echte Bibliothek eingelesen und alle Spiele auf „HDR automatisch“ aus, nur
# Fenster dieser Prozess-ID, keine Maus- oder Tastatureingaben). Gemessen wird die Busy-Zeit der Grafik-Engines dieses
# Prozesses über WMI (Win32_PerfRawData_GPUPerformanceCounters_GPUEngine, sprachunabhängig, mit Nachkommastellen; nur
# ohne Rohdaten die ganzzahligen Prozente), je Probe die ausgelastetste Engine-Art (wie der Task-Manager), einmal pro
# Sekunde (im HDR-Moment alle 250 ms).
#   Teil 1, ohne Bildschirmwerte prüfbar:
#   - Gemessen wird erst, wenn das Laden der Bilder nach dem Öffnen fertig ist (Protokoll der Testinstanz auf Stufe Debug:
#     „Bilder: nichts mehr in Arbeit.“ nach dem letzten „Bilder: Laden beginnt.“, seit 2 s unverändert).
#   - Inaktiv (Dialog „Spiel hinzufügen“ davor, er hat keine Dauer-Effekte; der Fokus steht auf einem Knopf, damit kein
#     blinkender Cursor die Zeichenschleife wachhält) und minimiert: die Statuszeile meldet die Pause, das Protokoll meldet
#     0 Uhren der Dauer-Effekte („Effekte: … Uhren 0“; ein nur angehaltenes Storyboard hielte seine Uhren und WPF taktete
#     weiter, gemessen 1,5 bis 1,9 %), CPU der Instanz höchstens 1 % eines Kerns, mit Bildschirm auch: Grafikkarte unter 1 %
#   Teil 2, braucht den eingeschalteten Bildschirm:
#   - Offenes, aktives Fenster in Ruhe (Dauer-Effekte laufen): Liste bei 1600×1000, Regal und Raster bei 3440×1440 (der
#     größte bewegte Titelbereich): je Mittel unter 5 %, nie über 20 %
#   - HDR-Moment (--test-hdr-moment, ohne HDR-Schaltung, mit heller Fassung): Spitze in 1 s, Median über 3 Momente, unter
#     35 % (Entscheidung des Nutzers 2026-11-04, E97). Spitze = höchste Last in einem beliebig liegenden Fenster von 1 s
#     (Rohwerte alle 250 ms von kurz vor dem Auslösen bis 4 s danach, Fenster nach den Zeitstempeln der Zähler, Busy-Zeit
#     zwischen zwei Rohwerten gleichmäßig verteilt); das Mittel über den Moment steht nur im Protokoll, ohne Grenze.
#     Zwischen zwei Messungen 4,5 s Pause.
#   Teil 3, ohne Bildschirm prüfbar:
#   - Fenster geschlossen: nach 10 s Aufräumen (Bilder freigeben, index.json schreiben) 30 s lang CPU höchstens 0,5 % eines
#     Kerns wie im Infobereich ohne Fenster, Handles höchstens +20 (mit Bildschirm auch: keine Grafiklast); der Leerlauf
#     der Überwachung wie bisher (phase3-idle.ps1, mit -SkipIdleGate aus)
# Ohne aktiven Bildschirm (gesperrt, Bildschirm aus oder schlafend, keine Zähler, kein messbarer Bildaufbau) laufen Teil 1
# und 3 trotzdem; das Gate meldet dann UMGEBUNG (Exit-Code 2) und gilt nicht als bestanden, ein Fehler in Teil 1 oder 3
# meldet Exit-Code 1. Verliert das Testfenster während einer Messung in Teil 2 den Vordergrund (jemand arbeitet am PC),
# wird es einmal neu aktiviert und die Messung wiederholt; bleibt es nicht aktiv, ebenso UMGEBUNG mit dem Prozess im
# Vordergrund. Werte über den Grenzen bleiben FEHLER. Schaltet kein HDR, startet kein Spiel.
# -ColorsFollowGame (G15.7): dieselbe Messung mit eingeschaltetem „Farben folgen dem Spiel“
# (settings.appearance.colorsFollowGame in der Testablage).
param(
    [int]$IdleSeconds = 20,
    [int]$QuietSeconds = 8,
    [int]$ClosedSettleSeconds = 10,
    [int]$ClosedSeconds = 30,
    [int]$WideWidth = 3440,
    [int]$WideHeight = 1440,
    [double]$MaxIdleMean = 5,
    [double]$MaxIdlePeak = 20,
    [double]$MaxMomentPeak = 35,
    [ValidateRange(1, 9)][int]$MomentRuns = 3,
    [ValidateRange(0.5, 10)][double]$MomentSeconds = 2.5,
    [double]$MaxQuiet = 1,
    [double]$MaxPausedCpu = 1,
    [int]$LoadTimeoutSeconds = 60,
    [double]$MaxClosedCpu = 0.5,
    [int]$MaxClosedHandleGrowth = 20,
    [switch]$SkipIdleGate,
    [switch]$ColorsFollowGame
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

# Anteil der ausgelastetsten Engine-Art zwischen zwei Rohwerten (wie in Gpu-Measure); 0 ohne gemeinsame Zeitspanne
function Gpu-Share($from, $to) {
    $peak = 0.0
    foreach ($type in $to.Keys) {
        if ($from.ContainsKey($type) -and $to[$type].Time -gt $from[$type].Time) {
            $share = 100.0 * ($to[$type].Busy - $from[$type].Busy) / ($to[$type].Time - $from[$type].Time)
            if ($share -gt $peak) { $peak = $share }
        }
    }
    return [Math]::Max(0.0, $peak)
}

# Zeitstempel eines Rohwerts (100 ns), 0 ohne Engine
function Gpu-Time($raw) { $t = 0.0; foreach ($v in $raw.Values) { if ($v.Time -gt $t) { $t = $v.Time } }; return $t }

# Busy-Zeit einer Engine-Art zum Zeitpunkt $at (100 ns), linear zwischen den beiden Rohwerten davor und danach (die
# Busy-Zeit eines Abstands gleichmäßig verteilt); $null außerhalb der Messung oder wenn einem der beiden die Art fehlt
function Gpu-Interpolate([double[]]$times, [double[]]$busy, [bool[]]$has, [double]$at) {
    for ($i = 1; $i -lt $times.Count; $i++) {
        if ($times[$i] -ge $at) {
            if ($at -lt $times[$i - 1] -or -not $has[$i - 1] -or -not $has[$i]) { return $null }
            return $busy[$i - 1] + ($busy[$i] - $busy[$i - 1]) * ($at - $times[$i - 1]) / ($times[$i] - $times[$i - 1])
        }
    }
    return $null
}

# Auswertung der Rohwerte eines HDR-Moments (rechnet nur, misst nicht). Es zählen nur Rohwerte mit Engines des Prozesses
# und mit neuerem Zeitstempel als der vorige. Spitze: höchste Last der ausgelastetsten Engine-Art in einem beliebig
# liegenden Fenster von genau 1 s nach den Zeitstempeln der Zähler; zwischen zwei Rohwerten gilt die Busy-Zeit als
# gleichmäßig verteilt, dann liegt das höchste Fenster mit Anfang oder Ende auf einem Rohwert, und nur diese werden
# gerechnet. Ein Fenster „ab einem Rohwert bis zum ersten mindestens 1 s späteren“ wäre bei wenigen Millisekunden
# Versatz der Zeitstempel bis zu 1,25 s lang und die Spitze zu niedrig (mit erzeugten Rohwerten 26,6 statt 30 %,
# gleichmäßig verteilt 29,9 %). Mittel: Busy-Zeit vom ersten bis zum letzten Rohwert je Sekunde Moment; die Ruhelast
# der übrigen Messzeit ist eingerechnet, das Mittel also eher etwas zu hoch. Weniger als zwei Rohwerte: alles 0, weniger
# als 1 s Messung: Spitze 0.
function Gpu-MomentStats($readings, [double]$momentSeconds) {
    $valid = New-Object Collections.Generic.List[object]
    foreach ($r in $readings) {
        if ($null -eq $r -or $r.Count -eq 0) { continue }
        if ($valid.Count -gt 0 -and (Gpu-Time $r) -le (Gpu-Time $valid[$valid.Count - 1])) { continue }
        $valid.Add($r)
    }
    $stats = [pscustomobject]@{ Readings = $valid.Count; Span = 0.0; MaxGapMs = 0.0; Peak = 0.0; PeakFrom = 0.0; Mean = 0.0 }
    $n = $valid.Count
    if ($n -lt 2) { return $stats }
    $times = New-Object double[] $n
    for ($i = 0; $i -lt $n; $i++) { $times[$i] = Gpu-Time $valid[$i] }
    for ($i = 1; $i -lt $n; $i++) { $stats.MaxGapMs = [Math]::Max($stats.MaxGapMs, ($times[$i] - $times[$i - 1]) / 1e4) }
    # Fenster von 1 s: jeder Rohwert als Anfang und jeder Rohwert als Ende
    $windows = New-Object Collections.Generic.List[double[]]
    foreach ($t in $times) {
        if ($t + 1e7 -le $times[$n - 1]) { $windows.Add([double[]]@($t, ($t + 1e7))) }
        if ($t - 1e7 -ge $times[0]) { $windows.Add([double[]]@(($t - 1e7), $t)) }
    }
    $types = @($valid | ForEach-Object { $_.Keys } | Sort-Object -Unique)
    foreach ($type in $types) {
        $busy = New-Object double[] $n; $has = New-Object bool[] $n
        for ($i = 0; $i -lt $n; $i++) { if ($valid[$i].ContainsKey($type)) { $busy[$i] = $valid[$i][$type].Busy; $has[$i] = $true } }
        foreach ($w in $windows) {
            $b0 = Gpu-Interpolate $times $busy $has $w[0]
            $b1 = Gpu-Interpolate $times $busy $has $w[1]
            if ($null -eq $b0 -or $null -eq $b1) { continue }
            $share = 100.0 * ($b1 - $b0) / ($w[1] - $w[0])
            if ($share -gt $stats.Peak) { $stats.Peak = $share; $stats.PeakFrom = ($w[0] - $times[0]) / 1e7 }
        }
    }
    $stats.Span = ($times[$n - 1] - $times[0]) / 1e7
    $stats.Mean = (Gpu-Share $valid[0] $valid[$n - 1]) * $stats.Span / $momentSeconds
    return $stats
}

# Ein HDR-Moment, unabhängig davon, wo er in einer Sekundenprobe beginnt (E97): ein Rohwert vor dem Auslösen ($trigger
# startet den Moment), dann Rohwerte alle 250 ms bis 4 s nach dem Auslösen (der Moment dauert 2,5 s und beginnt etwa
# 0,2 s nach dem Auslösen). Dauert eine Abfrage länger als ein Takt, entfällt nur der verpasste Takt. Ohne Rohdaten wie
# bisher vier Sekundenproben nach dem Auslösen (ganze Prozente, Spitze dann vom Raster der Sekunden abhängig).
function Gpu-Moment([int]$processId, [string]$label, [scriptblock]$trigger, [double]$seconds = 4, [int]$stepMs = 250) {
    if (-not $rawCounters) {
        & $trigger | Out-Null
        return Gpu-Measure $processId ([int][Math]::Ceiling($seconds)) ($label + ' (ohne Rohdaten: Sekundenproben)')
    }
    $readings = New-Object Collections.Generic.List[object]
    $readings.Add((Gpu-Raw $processId))
    & $trigger | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $end = $seconds * 1000
    $next = [double]$stepMs
    while ($true) {
        $wait = [int]($next - $sw.ElapsedMilliseconds)
        if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
        $readings.Add((Gpu-Raw $processId))
        $elapsed = $sw.ElapsedMilliseconds
        if ($elapsed -ge $end) { break }
        $next = [Math]::Min($end, ([Math]::Floor($elapsed / $stepMs) + 1) * $stepMs)
    }
    $s = Gpu-MomentStats $readings $MomentSeconds
    Note ("{0}: {1} Rohwerte über {2:N2} s (Abstand höchstens {3:N0} ms), Spitze in 1 s {4:N2} % (Fenster ab {5:N2} s), Mittel über den Moment ({6} s) {7:N2} %" -f $label, $s.Readings, $s.Span, $s.MaxGapMs, $s.Peak, $s.PeakFrom, $MomentSeconds, $s.Mean) | Out-Host
    return [pscustomobject]@{ Mean = [double]$s.Mean; Peak = [double]$s.Peak }
}

# Median (bei gerader Anzahl das Mittel der beiden mittleren Werte), 0 ohne Werte
function Median-Of([double[]]$values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0.0 }
    $mid = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$mid] }
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
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

# Laden der Bilder fertig: im Protokoll steht nach dem letzten „Laden beginnt“ ein „nichts mehr in Arbeit“, und 2 s später
# ist es noch so. Liefert die Wartezeit in Sekunden, -1 bei Zeitüberschreitung
function Wait-ArtworkIdle([string]$cfg, [int]$timeoutSeconds) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $quietAt = -2; $since = 0.0
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSeconds) {
        $log = Read-Log $cfg
        $begin = $log.LastIndexOf('Bilder: Laden beginnt.')
        $idle = $log.LastIndexOf('Bilder: nichts mehr in Arbeit.')
        if ($idle -gt $begin -or ($begin -lt 0 -and $idle -lt 0)) {
            # dieselbe letzte Meldung „nichts mehr in Arbeit“ seit 2 s: fertig
            if ($quietAt -ne $idle) { $quietAt = $idle; $since = $sw.Elapsed.TotalSeconds }
            elseif ($sw.Elapsed.TotalSeconds - $since -ge 2) { return [Math]::Round($sw.Elapsed.TotalSeconds, 1) }
        } else { $quietAt = -2 }
        Start-Sleep -Milliseconds 250
    }
    return -1
}

# Letzte Meldung der Effekte im Protokoll (Stufe, Grund des Anhaltens, laufende und angehaltene Dauer-Effekte, Uhren)
function Last-Effects([string]$cfg) {
    $found = [regex]::Matches((Read-Log $cfg), 'Effekte: Stufe (\w+), Pause (\w+), Dauer-Effekte laufen (\d+), angehalten (\d+), Uhren (\d+)')
    if ($found.Count -eq 0) { return $null }
    $m = $found[$found.Count - 1]
    return [pscustomobject]@{ Level = $m.Groups[1].Value; Pause = $m.Groups[2].Value; Running = [int]$m.Groups[3].Value; Paused = [int]$m.Groups[4].Value; Clocks = [int]$m.Groups[5].Value; Text = $m.Value }
}

function Is-Active($win, [int]$processId) {
    try { $f = [Windows.Automation.AutomationElement]::FocusedElement; return $null -ne $f -and $f.Current.ProcessId -eq $processId } catch { return $false }
}

# Name des Prozesses im Vordergrund (Fokus laut UI-Automation, nur gelesen)
function Foreground-Name {
    try {
        $f = [Windows.Automation.AutomationElement]::FocusedElement
        if ($f) { $p = Get-Process -Id $f.Current.ProcessId -ErrorAction SilentlyContinue; if ($p) { return $p.ProcessName } }
    } catch {}
    return 'unbekannt'
}

# Messung bei aktivem Testfenster. Verliert es dabei den Vordergrund (jemand arbeitet am PC und wechselt die Anwendung),
# wird es einmal wie beim Start (SetFocus über UI-Automation) neu aktiviert und die Messung wiederholt; bleibt es nicht
# aktiv, liegt das an der Umgebung, nicht an der App: UMGEBUNG mit dem Prozess im Vordergrund (Exit-Code 2), Rückgabe
# $null. Die Grenzwerte prüft der Aufrufer (FEHLER).
function Measure-Active([scriptblock]$measure, [string]$label) {
    $result = & $measure
    if (Is-Active $win $app.Id) { return $result }
    Note ('{0}: Fenster verlor während der Messung den Vordergrund an „{1}“, einmal neu aktivieren und wiederholen' -f $label, (Foreground-Name)) | Out-Host
    try { $win.SetFocus() } catch {}
    Start-Sleep -Seconds 1
    if (Is-Active $win $app.Id) {
        $result = & $measure
        if (Is-Active $win $app.Id) { return $result }
    }
    $script:umgebung = 'Fenster der Testinstanz verlor während der Messung „{0}“ den Vordergrund an „{1}“ (andere Anwendung im Vordergrund)' -f $label, (Foreground-Name)
    return $null
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
    # Stufe Debug: Effekte (Uhren) und Bilderdienst (Laden fertig) melden sich im Protokoll
    $config.settings | Add-Member -NotePropertyName logLevel -NotePropertyValue 'Debug' -Force
    if ($ColorsFollowGame) {
        if (-not $config.settings.PSObject.Properties['appearance'] -or $null -eq $config.settings.appearance) { $config.settings | Add-Member -NotePropertyName appearance -NotePropertyValue ([pscustomobject]@{}) -Force }
        $config.settings.appearance | Add-Member -NotePropertyName colorsFollowGame -NotePropertyValue $true -Force
    }
    Note ("Farben folgen dem Spiel: {0}" -f [bool]$ColorsFollowGame)
    [IO.File]::WriteAllText((Join-Path $cfg 'config.json'), ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    Note "Spiele in der Testablage: $(@($config.games).Count)"

    $app = Start-App $cfg @('--show')
    $win = Find-Window 'MainWindow' $app.Id
    if (-not $win) { throw 'Hauptfenster fehlt' }
    Resize-To $win 1600 1000
    try { $win.SetFocus() } catch {}
    Start-Sleep -Seconds 4   # Einblenden, JIT nicht mitmessen
    # Laden der Bilder (sichtbare Zeilen, Titelbild, vorab die helle Fassung) abwarten, bevor gemessen wird
    $loaded = Wait-ArtworkIdle $cfg $LoadTimeoutSeconds
    Check ($loaded -ge 0) ("Laden der Bilder nach dem Öffnen fertig (nach weiteren {0} s, höchstens {1} s)" -f $loaded, $LoadTimeoutSeconds)
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
        $fx = Last-Effects $cfg
        Note ("Protokoll Effekte (Dialog davor): {0}" -f $(if ($fx) { $fx.Text } else { 'keine Meldung' }))
        Check ($null -ne $fx -and $fx.Pause -eq 'Background' -and $fx.Clocks -eq 0) ('Inaktiv: angehaltene Dauer-Effekte ohne Uhren ({0})' -f $(if ($fx) { "$($fx.Paused) angehalten, Uhren $($fx.Clocks)" } else { 'keine Meldung' }))
        Check ((Wait-ArtworkIdle $cfg 10) -ge 0) 'Inaktiv: kein Laden der Bilder während der Messung'
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
        # Nach dem Minimieren übersetzt der Laufzeit-Compiler (Debug-Build, ohne ReadyToRun) noch einige Sekunden oft
        # gerufene Methoden neu (Thread „.NET Tiered Compilation Worker“, gemessen 3,4 % in den ersten 6 s, 0,5 bis 0,8 %
        # in den nächsten 6 s, danach höchstens 0,3 %); das ist kein Zeichnen und wird nicht mitgemessen. Eine
        # durchlaufende Zeichenschleife bliebe dagegen.
        Start-Sleep -Seconds 12
        $text = Effects-Text $win
        Note "Statuszeile Effekte (minimiert): $text"
        Check ($text -match 'minimiert') ('Minimiert: Statuszeile meldet die Pause („{0}“)' -f $text)
        $fx = Last-Effects $cfg
        Note ("Protokoll Effekte (minimiert): {0}" -f $(if ($fx) { $fx.Text } else { 'keine Meldung' }))
        Check ($null -ne $fx -and $fx.Pause -eq 'Hidden' -and $fx.Clocks -eq 0 -and $fx.Running -eq 0) ('Minimiert: keine Dauer-Effekte und keine Uhren ({0})' -f $(if ($fx) { "laufen $($fx.Running), Uhren $($fx.Clocks)" } else { 'keine Meldung' }))
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

        $idle = Measure-Active { Cpu-During $app.Id { Gpu-Measure $app.Id $IdleSeconds 'Liste 1600×1000 in Ruhe' } } 'Liste 1600×1000 in Ruhe'
        if ($idle) { Note ("Liste in Ruhe: CPU {0:N2} % eines Kerns (zeigt, dass die Dauer-Effekte zeichnen)" -f $idle.Cpu) }
    }

    if (-not $umgebung) {
        # HDR-Moment ohne HDR-Schaltung, mit heller Fassung (die Instanz lädt sie vor dem Abspielen). Mehrere Momente,
        # Median (E97): eine einzelne Messung schwankte mit festen Sekundenproben um etwa ±2 Prozentpunkte. Jeder Moment
        # wird bei aktivem Fenster gemessen wie die Ruhe (Measure-Active: bei Fokusverlust einmal wiederholt, sonst UMGEBUNG)
        $logBefore = (Read-Log $cfg).Length
        $runs = @()
        for ($n = 1; $n -le $MomentRuns; $n++) {
            if ($n -gt 1) { Start-Sleep -Seconds 4 }   # voriger Moment ganz vorbei, Grafikkarte wieder in Ruhe
            $what = "HDR-Moment {0}/{1}" -f $n, $MomentRuns
            $before = (Read-Log $cfg).Length
            $one = Measure-Active { Gpu-Moment $app.Id $what { Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--test-hdr-moment') | Out-Null } } $what
            if (-not $one) { break }   # Vordergrund verloren: UMGEBUNG
            Start-Sleep -Milliseconds 500
            $part = Read-Log $cfg
            $part = $part.Substring([Math]::Min($before, $part.Length))
            $one | Add-Member -NotePropertyName Kind -NotePropertyValue $(if ($part -match 'HDR-Moment gespielt \((\w+), helle Fassung (\w+)\)') { $Matches[1] + ', helle Fassung ' + $Matches[2] } else { '?' })
            $runs += $one
        }
    }

    if (-not $umgebung) {
        $moment = [pscustomobject]@{ Peak = (Median-Of @($runs | ForEach-Object { $_.Peak })); Mean = (Median-Of @($runs | ForEach-Object { $_.Mean })) }
        Note ("HDR-Moment, Median über {0}: Spitze in 1 s {1:N2} %, Mittel über den Moment {2:N2} % (nur protokolliert; Spitzen {3}; Mittel {4})" -f $runs.Count, $moment.Peak, $moment.Mean, (($runs | ForEach-Object { '{0:N2}' -f $_.Peak }) -join ' '), (($runs | ForEach-Object { '{0:N2}' -f $_.Mean }) -join ' '))
        $log = Read-Log $cfg
        $log = $log.Substring([Math]::Min($logBefore, $log.Length))
        $kind = @($runs | ForEach-Object { $_.Kind } | Sort-Object -Unique) -join ' / '
        Note "HDR-Moment laut Protokoll: $kind"
        if ($moment.Peak -le 0 -and $idle.Gpu.Peak -le 0) { $umgebung = 'Kein messbarer Bildaufbau (Bildschirm aus oder im Energiesparmodus?)' }
    }

    if (-not $umgebung) {
        Check ($kind -eq 'Full, helle Fassung da') ('HDR-Moment in Stufe „Voll“ mit heller Fassung gespielt ({0})' -f $kind)
        Check ($idle.Gpu.Mean -lt $MaxIdleMean -and $idle.Gpu.Peak -le $MaxIdlePeak) ("Liste in Ruhe: Mittel {0:N2} % < {1} %, Spitze {2:N2} % <= {3} %" -f $idle.Gpu.Mean, $MaxIdleMean, $idle.Gpu.Peak, $MaxIdlePeak)
        Check ($moment.Peak -lt $MaxMomentPeak) ("HDR-Moment: Spitze in 1 s, Median über {0} Momente, {1:N2} % unter {2} % (Mittel über den Moment {3:N2} %, nur protokolliert)" -f $runs.Count, $moment.Peak, $MaxMomentPeak, $moment.Mean)
        Check (-not $log.Contains('HDR-Schaltung')) 'Keine HDR-Schaltung im Protokoll'

        # Größter bewegter Titelbereich: Regal und Raster im Vollbild des Nutzers
        Resize-To $win $WideWidth $WideHeight
        foreach ($view in @(@('ViewShelfButton', 'Regal'), @('ViewGridButton', 'Raster'))) {
            Select-El (Find-Id $win $view[0] 3000)
            try { $win.SetFocus() } catch {}
            Start-Sleep -Seconds 4   # Wechsel der Ansicht, Bilder in der neuen Größe
            $what = "{0} {1}×{2} in Ruhe" -f $view[1], $WideWidth, $WideHeight
            $wide = Measure-Active { Cpu-During $app.Id { Gpu-Measure $app.Id $IdleSeconds $what } } $what
            if (-not $wide) { break }   # Vordergrund verloren: UMGEBUNG
            Note ("{0} in Ruhe: CPU {1:N2} % eines Kerns" -f $view[1], $wide.Cpu)
            Check ($wide.Gpu.Mean -lt $MaxIdleMean -and $wide.Gpu.Peak -le $MaxIdlePeak) ("{0}: Mittel {1:N2} % < {2} %, Spitze {3:N2} % <= {4} %" -f $what, $wide.Gpu.Mean, $MaxIdleMean, $wide.Gpu.Peak, $MaxIdlePeak)
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
    Note "UMGEBUNG $umgebung (Teil 1 und 3 bestanden, die Grafikkarte ist so nicht messbar)"
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
