# Gate Phase 1 (Negativfälle am echten System): eigene Exit-Codes, klare Meldung, keine Ausnahme.
# - Bildschirm, den es nicht gibt -> Exit 3
# - Falscher Aufruf -> Exit 2
# - Ist der Hauptbildschirm gerade nicht HDR-fähig (etwa Bild-in-Bild), wird zusätzlich "hdr on" geprüft -> Exit 4
param([string]$Exe = (Join-Path $PSScriptRoot '..\..\src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'))
$ErrorActionPreference = 'Continue'
$ok = $true

function Run([string[]]$a) {
    $psi = New-Object Diagnostics.ProcessStartInfo $Exe
    $psi.Arguments = ($a | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
    $p = [Diagnostics.Process]::Start($psi)
    $o = $p.StandardOutput.ReadToEnd(); $e = $p.StandardError.ReadToEnd(); $p.WaitForExit()
    return [pscustomobject]@{ Code = $p.ExitCode; Out = $o; Err = $e }
}

$r = Run @('hdr', 'on', '--display', '\\?\DISPLAY#GIBTESNICHT#0')
Write-Output "nicht vorhandener Bildschirm: exit=$($r.Code) stderr=$($r.Err.Trim())"
$ok = $ok -and $r.Code -eq 3 -and $r.Err -notmatch 'Exception|Unerwarteter'

$r = Run @('hdr', 'blinken')
Write-Output "falscher Aufruf: exit=$($r.Code) stderr=$($r.Err.Trim() -replace "`r?`n", ' | ')"
$ok = $ok -and $r.Code -eq 2

$status = @(& $Exe hdr status --json | Out-String | ConvertFrom-Json)
$nonHdr = $status | Where-Object { -not $_.hdrSupported } | Select-Object -First 1
if ($nonHdr) {
    $r = Run @('hdr', 'on', '--display', $nonHdr.monitorDevicePath)
    Write-Output "Bildschirm ohne HDR ($($nonHdr.name)): exit=$($r.Code) stderr=$($r.Err.Trim())"
    $ok = $ok -and $r.Code -eq 4 -and $r.Err -match 'unterstützt kein HDR' -and $r.Err -notmatch 'Exception|Unerwarteter'
    if ($ok) { Write-Output 'PHASE1_NONHDR_REAL_OK' }
} else {
    Write-Output 'Kein Bildschirm ohne HDR aktiv: Fall "nicht HDR-fähig" am echten System hier nicht prüfbar (Unit-Tests decken ihn ab).'
}

if ($ok) { Write-Output 'PHASE1_NEGATIVE_OK'; exit 0 }
exit 1
