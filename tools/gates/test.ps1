# Gate: alle Unit-Tests grün, keiner übersprungen (übersprungen hieße z. B.: wingdi.h fehlt).
param([string]$Filter = '', [int]$MinTests = 1)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $root
try {
    $testArgs = @('test', 'HdrPilot.slnx', '-nologo')
    if ($Filter) { $testArgs += @('--filter', $Filter) }
    $out = & dotnet @testArgs | Out-String
    $code = $LASTEXITCODE
    $m = [regex]::Match($out, '(Fehler|Failed):\s*(\d+),\s*(erfolgreich|Passed):\s*(\d+),\s*(übersprungen|Skipped):\s*(\d+),\s*(gesamt|Total):\s*(\d+)')
    if (-not $m.Success) { Write-Output $out; Write-Output 'Zusammenfassung nicht gefunden'; exit 1 }
    $failed = [int]$m.Groups[2].Value; $passed = [int]$m.Groups[4].Value; $skipped = [int]$m.Groups[6].Value; $total = [int]$m.Groups[8].Value
    Write-Output "dotnet test exit=$code gesamt=$total erfolgreich=$passed fehler=$failed übersprungen=$skipped filter='$Filter'"
    if ($code -eq 0 -and $failed -eq 0 -and $skipped -eq 0 -and $passed -ge $MinTests -and $passed -eq $total) {
        Write-Output "TESTS_OK $passed"
        exit 0
    }
    Write-Output $out
    exit 1
} finally {
    Pop-Location
}
