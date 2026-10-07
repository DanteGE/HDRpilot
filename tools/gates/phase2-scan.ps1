# Gate Phase 2: "hdrpilot scan" gibt alle gefundenen Spiele als gültiges JSON aus.
# Prüft je Eintrag Quelle, ID, Name, existierenden Installationsordner, existierende EXEs und Start-Befehl,
# eindeutige Schlüssel, und je Scanner einen Bericht (Launcher erkannt, Fehlerliste).
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'),
    [string[]]$ExpectedSources = @('Steam', 'Epic', 'Xbox', 'Gog', 'Ubisoft', 'EA')
)
$ErrorActionPreference = 'Stop'
$sw = [Diagnostics.Stopwatch]::StartNew()
$raw = & $Exe scan | Out-String
$code = $LASTEXITCODE
$sw.Stop()
if ($code -ne 0) { Write-Output "scan exit=$code"; exit 1 }
$data = $raw | ConvertFrom-Json
$ok = $true
$games = @($data.games)
$scanners = @($data.scanners)

$keys = @{}
foreach ($g in $games) {
    $problems = @()
    if (-not $g.source) { $problems += 'Quelle fehlt' }
    if (-not $g.sourceId) { $problems += 'ID fehlt' }
    if (-not $g.name) { $problems += 'Name fehlt' }
    if (-not $g.installDir -or -not (Test-Path -LiteralPath $g.installDir -PathType Container)) { $problems += "Ordner fehlt: $($g.installDir)" }
    if (-not $g.launchCommand) { $problems += 'Start-Befehl fehlt' }
    foreach ($e in @($g.executables)) { if ($e -and -not (Test-Path -LiteralPath $e -PathType Leaf)) { $problems += "EXE fehlt: $e" } }
    if ($keys.ContainsKey($g.key)) { $problems += 'doppelter Schlüssel' } else { $keys[$g.key] = $true }
    if ($problems.Count -gt 0) { $ok = $false; Write-Output ("FEHLER {0} '{1}': {2}" -f $g.key, $g.name, ($problems -join '; ')) }
}

foreach ($s in $scanners) {
    $count = @($games | Where-Object { $_.source -eq $s.source }).Count
    $errs = @($s.errors)
    Write-Output ("{0,-10} erkannt={1,-5} Spiele={2,3} Fehler={3} {4} ms" -f $s.source, $s.launcherDetected, $count, $errs.Count, $s.durationMs)
    foreach ($e in $errs) { Write-Output ("           Fehler: {0} {1}" -f $e.message, $e.path) }
    if ($count -ne $s.gameCount) { $ok = $false; Write-Output "           FEHLER: gameCount=$($s.gameCount) passt nicht zu $count Einträgen" }
}

foreach ($src in $ExpectedSources) {
    $s = $scanners | Where-Object { $_.source -eq $src }
    if (-not $s) { $ok = $false; Write-Output "FEHLER: kein Scanner für $src" }
    elseif (-not $s.launcherDetected) { $ok = $false; Write-Output "FEHLER: Launcher $src installiert, aber nicht erkannt" }
}

Write-Output ("Spiele gesamt: {0}, Scanner: {1}, Laufzeit {2} ms" -f $games.Count, $scanners.Count, $sw.ElapsedMilliseconds)
if ($ok -and $games.Count -gt 0) { Write-Output 'PHASE2_SCAN_OK'; exit 0 }
exit 1
