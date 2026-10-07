# Gate Phase 3: Überwachung im Leerlauf sparsam. Echte Spielbibliothek eingelesen (alle Spiele HDR aus),
# 60 s messen: CPU-Zeit (Anteil eines Kerns) und Handle-Zahl der Überwachung.
param(
    [string]$Cli = (Join-Path $PSScriptRoot '..\..\src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'),
    [int]$Seconds = 60,
    [double]$MaxCpuPercent = 0.5,
    [int]$MaxHandleGrowth = 20
)
$ErrorActionPreference = 'Stop'
$Cli = (Resolve-Path $Cli).Path
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$cfg = Join-Path $root 'tools\out\phase3\config-idle'
if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }
New-Item -ItemType Directory -Force $cfg | Out-Null
& $Cli games import --config-dir $cfg | Select-Object -First 1 | ForEach-Object { Write-Output "Import: $_" }
$games = @(& $Cli games list --config-dir $cfg --json | Out-String | ConvertFrom-Json)
Write-Output "Spiele in der Konfiguration: $($games.Count)"

$out = Join-Path $cfg 'watch.out'
$p = Start-Process $Cli -ArgumentList @('watch', '--config-dir', "`"$cfg`"") -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError (Join-Path $cfg 'watch.err') -PassThru
try {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not ((Test-Path $out) -and ((Get-Content $out -Raw -ErrorAction SilentlyContinue) -match 'Intervall \d+ ms'))) {
        if ($sw.ElapsedMilliseconds -gt 15000) { throw 'Überwachung startet nicht' }
        Start-Sleep -Milliseconds 100
    }
    Start-Sleep -Seconds 10   # Anlaufphase (JIT, erste Abfrage aller Prozesse) nicht mitmessen
    $proc = Get-Process -Id $p.Id
    $cpu0 = $proc.TotalProcessorTime; $h0 = $proc.HandleCount; $ws0 = $proc.WorkingSet64; $t0 = Get-Date
    Start-Sleep -Seconds $Seconds
    $proc.Refresh()
    $cpu1 = $proc.TotalProcessorTime; $h1 = $proc.HandleCount; $ws1 = $proc.WorkingSet64; $t1 = Get-Date
    $wall = ($t1 - $t0).TotalMilliseconds
    $cpuMs = ($cpu1 - $cpu0).TotalMilliseconds
    $pct = $cpuMs / $wall * 100
    Write-Output ("Messdauer {0:N1} s: CPU-Zeit {1:N0} ms = {2:N3} % eines Kerns ({3} logische Kerne)" -f ($wall / 1000), $cpuMs, $pct, [Environment]::ProcessorCount)
    Write-Output ("Handles: {0} -> {1} (Zuwachs {2}); Arbeitsspeicher {3:N1} MB -> {4:N1} MB" -f $h0, $h1, ($h1 - $h0), ($ws0 / 1MB), ($ws1 / 1MB))
    if ($pct -le $MaxCpuPercent -and ($h1 - $h0) -le $MaxHandleGrowth) { Write-Output 'PHASE3_IDLE_OK'; exit 0 }
    exit 1
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
