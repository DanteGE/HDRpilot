# Gate: vollständiger Neubau ohne Warnungen. -warnaserror macht jede Warnung zum Fehler,
# Exit-Code 0 heißt also: keine einzige Warnung. Zusätzlich wird die Zusammenfassung geprüft.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $root
try {
    $out = dotnet build HdrPilot.slnx --no-incremental -warnaserror -nologo -clp:Summary | Out-String
    $code = $LASTEXITCODE
    $warn = [regex]::Match($out, '(\d+)\s+Warn')
    $err = [regex]::Match($out, '(\d+)\s+(Fehler|Error)')
    $warnings = if ($warn.Success) { [int]$warn.Groups[1].Value } else { -1 }
    $errors = if ($err.Success) { [int]$err.Groups[1].Value } else { -1 }
    Write-Output "dotnet build exit=$code Warnungen=$warnings Fehler=$errors"
    if ($code -eq 0 -and $warnings -eq 0 -and $errors -eq 0) {
        Write-Output 'BUILD_OK_0_WARNINGS'
        exit 0
    }
    Write-Output $out
    exit 1
} finally {
    Pop-Location
}
