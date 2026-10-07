# Gate Phase 3 am echten System mit Stellvertreter-EXEs in einem als Spiel eingetragenen Ordner.
# Szenarien:
#   single    Start schaltet HDR in <= 2 s ein, Ende nach der Nachlaufzeit (10 s) wieder aus
#   two       zwei Stellvertreter: erst nach dem Ende des zweiten wird zurückgestellt
#   alreadyon war HDR vorher an, bleibt es an
#   crash     Überwachung hart beenden (wie Task-Manager), Stellvertreter schließen, neu starten: HDR wird zurückgestellt
# HDR-Zustand wird unabhängig über den MonitorDataStore (Ablage der Windows-Einstellungen) gemessen.
param(
    [ValidateSet('single', 'two', 'alreadyon', 'crash')][string]$Scenario = 'single',
    [string]$Cli = (Join-Path $PSScriptRoot '..\..\src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe')
)
$ErrorActionPreference = 'Stop'
$Cli = (Resolve-Path $Cli).Path
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$work = Join-Path $root 'tools\out\phase3'
$cfg = Join-Path $work "config-$Scenario"
$started = New-Object System.Collections.ArrayList
$ok = $true

function Note([string]$text) { Write-Output ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text) }
function Fail([string]$text) { $script:ok = $false; Note "FEHLER: $text" }

# --- Stellvertreter bauen: kleine Windows-EXE, die nur wartet (kein Fenster, keine Last) ---
New-Item -ItemType Directory -Force $work | Out-Null
$standIn = Join-Path $work 'StandIn.exe'
if (-not (Test-Path $standIn)) {
    Add-Type -OutputAssembly $standIn -OutputType WindowsApplication -TypeDefinition @'
public static class StandIn {
    public static int Main(string[] args) {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 3600;
        System.Threading.Thread.Sleep(seconds * 1000);
        return 0;
    }
}
'@
}
if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }
$dirA = Join-Path $cfg 'games\Stellvertreter A'
$dirB = Join-Path $cfg 'games\Stellvertreter B'
New-Item -ItemType Directory -Force $dirA, $dirB | Out-Null
$exeA = Join-Path $dirA 'StandInA.exe'; Copy-Item $standIn $exeA
$exeB = Join-Path $dirB 'StandInB.exe'; Copy-Item $standIn $exeB

$keyA = (& $Cli games add --config-dir $cfg --path $dirA --name 'Stellvertreter A' --hdr on | Select-Object -Last 1).Trim()
$keyB = (& $Cli games add --config-dir $cfg --path $dirB --name 'Stellvertreter B' --hdr on | Select-Object -Last 1).Trim()
Note "Eingetragen: $keyA ($dirA), $keyB ($dirB)"

# --- Hauptbildschirm und unabhängige Messung ---
$primary = @(& $Cli hdr status --json | Out-String | ConvertFrom-Json) | Where-Object { $_.primary } | Select-Object -First 1
if (-not $primary.hdrSupported) { Write-Output 'ABBRUCH: Hauptbildschirm meldet kein HDR'; exit 3 }
$edid = ($primary.monitorDevicePath -split '#')[1]
$regKey = Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MonitorDataStore' |
    Where-Object { $_.PSChildName -like "$edid*" -and $null -ne (Get-ItemProperty $_.PSPath).HDREnabled } | Select-Object -First 1
function HdrOn { [int](Get-ItemProperty $regKey.PSPath).HDREnabled -eq 1 }
function WaitHdr([bool]$target, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if ((HdrOn) -eq $target) { return $sw.ElapsedMilliseconds }
        Start-Sleep -Milliseconds 20
    }
    return -1
}
$initial = [bool]$primary.hdrEnabled

function StartWatch([string]$tag) {
    $out = Join-Path $cfg "watch-$tag.out"
    $p = Start-Process $Cli -ArgumentList @('watch', '--config-dir', "`"$cfg`"") -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError (Join-Path $cfg "watch-$tag.err") -PassThru
    [void]$started.Add($p)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 15000) {
        if ((Test-Path $out) -and ((Get-Content $out -Raw -ErrorAction SilentlyContinue) -match 'Intervall \d+ ms')) { Note "Überwachung $tag läuft (PID $($p.Id))"; return $p }
        Start-Sleep -Milliseconds 100
    }
    throw "Überwachung $tag startet nicht"
}
function StartGame([string]$exe) { $p = Start-Process $exe -PassThru; [void]$started.Add($p); Note "Stellvertreter gestartet: $(Split-Path $exe -Leaf) PID $($p.Id)"; return $p }
function StopGame($p) { Stop-Process -Id $p.Id -Force; Note "Stellvertreter beendet: PID $($p.Id)" }
function LogText { Get-ChildItem (Join-Path $cfg 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { Get-Content $_.FullName -Encoding UTF8 } | Out-String }
# Die Protokollzeile entsteht erst nach der Bestätigung (erneutes Lesen), also etwas nach dem Registry-Wechsel.
function WaitLog([string]$pattern, [int]$timeoutMs = 4000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if ((LogText) -match $pattern) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

try {
    if ($Scenario -eq 'alreadyon') { & $Cli hdr on | Out-Null } else { & $Cli hdr off | Out-Null }
    Note "Ausgangszustand HDR an: $(HdrOn)"

    switch ($Scenario) {
        'single' {
            $w = StartWatch '1'
            $a = StartGame $exeA
            $t = WaitHdr $true 5000
            Note "HDR an nach $t ms"
            if ($t -lt 0 -or $t -gt 2000) { Fail "HDR nicht innerhalb von 2 s an ($t ms)" }
            Start-Sleep -Seconds 3
            StopGame $a
            $off = WaitHdr $false 16000
            Note "HDR aus nach $off ms (Nachlaufzeit 10 s)"
            if ($off -lt 9500 -or $off -gt 13000) { Fail "Zurückstellen nicht nach der Nachlaufzeit ($off ms)" }
            if (-not (WaitLog 'HDR-Schaltung .*Auslöser: Spielstart erkannt .*vorher: aus \| nachher: an')) { Fail 'Protokoll ohne Einschaltzeile' }
            if (-not (WaitLog 'HDR-Schaltung .*Auslöser: Spielende .*vorher: an \| nachher: aus')) { Fail 'Protokoll ohne Ausschaltzeile' }
            if ($ok) { Write-Output 'PHASE3_SINGLE_OK' }
        }
        'two' {
            $w = StartWatch '1'
            $a = StartGame $exeA
            $t = WaitHdr $true 5000; Note "HDR an nach $t ms"
            if ($t -lt 0 -or $t -gt 2000) { Fail "HDR nicht innerhalb von 2 s an ($t ms)" }
            $b = StartGame $exeB
            Start-Sleep -Seconds 2
            StopGame $a
            $early = WaitHdr $false 14000
            if ($early -ge 0) { Fail "HDR ging nach dem Ende des ersten Stellvertreters aus ($early ms)" } else { Note 'HDR bleibt 14 s nach dem Ende von A an (B läuft)' }
            StopGame $b
            $off = WaitHdr $false 16000; Note "HDR aus $off ms nach dem Ende von B"
            if ($off -lt 9500 -or $off -gt 13000) { Fail "Zurückstellen nach B nicht nach der Nachlaufzeit ($off ms)" }
            if ($ok) { Write-Output 'PHASE3_TWO_OK' }
        }
        'alreadyon' {
            if (-not (HdrOn)) { Fail 'HDR ließ sich für den Test nicht vorab einschalten' }
            $w = StartWatch '1'
            $a = StartGame $exeA
            Start-Sleep -Seconds 3
            StopGame $a
            $off = WaitHdr $false 15000
            if ($off -ge 0) { Fail "HDR wurde ausgeschaltet ($off ms), obwohl es vorher an war" } else { Note 'HDR bleibt 15 s nach dem Ende an' }
            if (-not (WaitLog 'war schon an')) { Fail 'Protokoll ohne Hinweis "war schon an"' }
            if ($ok) { Write-Output 'PHASE3_ALREADYON_OK' }
        }
        'crash' {
            $w = StartWatch '1'
            $a = StartGame $exeA
            $t = WaitHdr $true 5000; Note "HDR an nach $t ms"
            Start-Sleep -Seconds 1
            Stop-Process -Id $w.Id -Force   # wie "Task beenden" im Task-Manager (TerminateProcess)
            Note "Überwachung hart beendet (PID $($w.Id))"
            $state = Join-Path $cfg 'session-state.json'
            if (-not (Test-Path $state)) { Fail 'Gemerkter Zustand fehlt auf der Platte' } else {
                $s = Get-Content $state -Raw | ConvertFrom-Json
                Note "Gemerkt: WasHdrOn=$($s.displays[0].wasHdrOn) Spiele=$($s.displays[0].games -join ',')"
                if ($s.displays[0].wasHdrOn) { Fail 'falscher gemerkter Zustand' }
            }
            StopGame $a
            Start-Sleep -Seconds 2
            if (-not (HdrOn)) { Fail 'HDR ging ohne laufende Überwachung aus (unerwartet)' } else { Note 'HDR bleibt nach dem Absturz zunächst an (erwartet)' }
            $sw = [Diagnostics.Stopwatch]::StartNew()
            $w2 = StartWatch '2'
            $off = WaitHdr $false 10000
            Note "HDR aus $off ms nach Bereitschaft der neu gestarteten Überwachung ($($sw.ElapsedMilliseconds) ms seit Startbefehl)"
            if ($off -lt 0) { Fail 'HDR wurde nach dem Neustart nicht zurückgestellt' }
            # Die Datei verschwindet erst nach der Bestätigung des Zurückstellens.
            $gone = [Diagnostics.Stopwatch]::StartNew()
            while ((Test-Path $state) -and $gone.ElapsedMilliseconds -lt 4000) { Start-Sleep -Milliseconds 50 }
            if (Test-Path $state) { Fail 'Gemerkter Zustand wurde nicht gelöscht' } else { Note "Gemerkter Zustand gelöscht ($($sw.ElapsedMilliseconds) ms seit Startbefehl)" }
            if (-not (WaitLog 'Kein ausgewähltes Spiel läuft mehr')) { Fail 'Protokoll ohne Wiederanlauf-Zeile' }
            if ($ok) { Write-Output 'PHASE3_CRASH_OK' }
        }
    }
}
finally {
    foreach ($p in $started) { try { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } } catch {} }
    Start-Sleep -Milliseconds 300
    & $Cli hdr $(if ($initial) { 'on' } else { 'off' }) | Out-Null
    Note "Ausgangszustand wiederhergestellt: HDR an = $(HdrOn)"
    Write-Output '--- Protokollauszug (HDR-Schaltungen, Sitzungen) ---'
    (LogText) -split "`r?`n" | Where-Object { $_ -match 'HDR-Schaltung|Sitzung|Spielprozess|Wiederanlauf|Kein ausgewähltes|schon an|Nachlaufzeit' } | ForEach-Object { Write-Output $_ }
}
if ($ok) { exit 0 } else { exit 1 }
