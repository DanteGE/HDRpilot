# Gate Phase 2: Jeder Scanner (jede Datei src\HdrPilot.Core\Games\Scanners\*Scanner.cs) hat eine Testklasse mit
# Pflicht-Tests (fehlender Launcher, defekte Daten, unvollständige Installation), nutzt Beispieldateien oder die
# Registry-Attrappe, und alle Scanner-Tests sind grün.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scannerFiles = @(Get-ChildItem (Join-Path $root 'src\HdrPilot.Core\Games\Scanners') -Filter '*Scanner.cs' -File)
if ($scannerFiles.Count -eq 0) { Write-Output 'Keine Scanner gefunden'; exit 1 }

Push-Location $root
try {
    $list = dotnet test tests\HdrPilot.Tests\HdrPilot.Tests.csproj --list-tests -nologo | Out-String
} finally { Pop-Location }

$ok = $true
foreach ($f in $scannerFiles) {
    $name = [IO.Path]::GetFileNameWithoutExtension($f.Name)          # z. B. SteamScanner
    $testFile = Join-Path $root "tests\HdrPilot.Tests\Scanners\$($name)Tests.cs"
    if (-not (Test-Path $testFile)) { $ok = $false; Write-Output "FEHLER $name`: Testdatei fehlt ($testFile)"; continue }
    $tests = @([regex]::Matches($list, "HdrPilot\.Tests\.Scanners\.$($name)Tests\.(\w+)") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $src = Get-Content $testFile -Raw
    $missing = @($tests | Where-Object { $_ -like 'Missing_launcher_returns_empty_without_errors*' }).Count -ge 1
    $broken = @($tests | Where-Object { $_ -like 'Broken_*' }).Count
    $incomplete = @($tests | Where-Object { $_ -like 'Incomplete_*' }).Count
    $usesFakes = ($src -match 'AddFixture|AddFile') -or ($src -match 'FakeRegistry')
    $line = "{0,-20} Tests={1,3} fehlenderLauncher={2} defekt={3} unvollständig={4} Attrappen={5}" -f $name, $tests.Count, $missing, $broken, $incomplete, $usesFakes
    Write-Output $line
    if (-not $missing -or $broken -lt 1 -or $incomplete -lt 1 -or -not $usesFakes -or $tests.Count -lt 5) { $ok = $false; Write-Output "FEHLER $name`: Pflicht-Tests oder Attrappen fehlen" }
}

$run = powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test.ps1') -Filter 'FullyQualifiedName~HdrPilot.Tests.Scanners' -MinTests 30 | Out-String
Write-Output ($run -split "`r?`n" | Where-Object { $_ -match 'gesamt=|TESTS_OK' })
if ($run -notmatch 'TESTS_OK') { $ok = $false }

if ($ok) { Write-Output 'PHASE2_SCANNER_TESTS_OK'; exit 0 }
exit 1
