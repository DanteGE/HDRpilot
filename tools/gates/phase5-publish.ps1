# Gate G5.1: Einzelne, eigenständige EXE veröffentlicht und lauffähig, ohne installierte .NET-Runtime.
# Startet die veröffentlichte EXE als Kopie außerhalb des Repositorys, ohne erreichbare .NET-Installation
# (DOTNET_ROOT ins Leere, dotnet aus PATH entfernt), und prüft die geladenen Module. Gegenprobe: dieselbe
# Modulprüfung erkennt beim normalen (runtime-abhängigen) Debug-Build die installierte Runtime.
# Dazu die Setup-Datei daneben (Phase 7, E83): Format, Prüfsummen, Paketinhalt, Manifest, mit Gegenproben.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root 'artifacts\publish\HDRpilot'
$work = Join-Path $env:TEMP 'HdrPilot-gate5'
$debugApp = Join-Path $root 'src\HdrPilot.App\bin\Debug\net10.0-windows\HDRpilot.exe'
$script:ok = $true

function Note([string]$text) { Write-Output ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text) }
function Check([bool]$condition, [string]$text) {
    if ($condition) { Note "ok   $text" } else { Note "FEHLER $text"; $script:ok = $false }
}

function Wait-Log([string]$cfg, [string]$pattern, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $text = Get-ChildItem (Join-Path $cfg 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object {
            $fs = New-Object IO.FileStream($_.FullName, 'Open', 'Read', 'ReadWrite, Delete')
            $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
            $sr.ReadToEnd(); $sr.Dispose()
        } | Out-String
        if ($text -match $pattern) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

function Get-Machine([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    return [BitConverter]::ToUInt16($bytes, $pe + 4)
}

# Startet eine App-EXE mit eigener Ablage, wartet auf die laufende Automatik, liefert die Modulpfade und beendet sie.
function Get-AppModules([string]$exe, [string]$cfg) {
    if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }
    New-Item -ItemType Directory -Force $cfg | Out-Null
    $p = Start-Process $exe -ArgumentList @('--config-dir', "`"$cfg`"", '--minimized') -PassThru
    $started = Wait-Log $cfg 'Einlesen:' 90000
    $modules = @()
    if ($started -and -not $p.HasExited) {
        $modules = @((Get-Process -Id $p.Id).Modules | ForEach-Object { $_.FileName })
    }
    Start-Process $exe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    $exited = $p.WaitForExit(15000)
    if (-not $exited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    return [pscustomobject]@{ Started = $started; Exited = $exited; ExitCode = $(if ($exited) { $p.ExitCode } else { -1 }); Modules = $modules }
}

$installedRuntime = '\\dotnet\\(shared|host)\\'

# 1. Veröffentlichen
& (Join-Path $root 'tools\publish.ps1') -Output $out | ForEach-Object { Note $_ }
Check ($LASTEXITCODE -eq 0) 'Veröffentlichung ohne Fehler'

# 2. Inhalt, Architektur, Version
$files = @(Get-ChildItem $out -Recurse -File | ForEach-Object { $_.FullName.Substring($out.Length + 1) } | Sort-Object)
Check (($files -join ',') -eq 'cli\hdrpilot.exe,HDRpilot.exe,README.md,THIRD-PARTY-NOTICES.txt') ("Ausgabe enthält genau HDRpilot.exe, cli\hdrpilot.exe, README.md, THIRD-PARTY-NOTICES.txt (gefunden: {0})" -f ($files -join ', '))
$notices = Join-Path $out 'THIRD-PARTY-NOTICES.txt'
$noticeText = if (Test-Path $notices) { [IO.File]::ReadAllText($notices) } else { '' }
$mitCount = ([regex]::Matches($noticeText, 'MIT License')).Count
Check ($mitCount -ge 2 -and $noticeText -match 'Microsoft\.NETCore\.App\.Runtime\.win-x64 10\.' -and $noticeText -match 'Microsoft\.WindowsDesktop\.App\.Runtime\.win-x64 10\.' -and $noticeText -match 'Microsoft\.Extensions\.Logging\.Abstractions' -and $noticeText.Length -gt 50000) ("Lizenzhinweise: {0} MIT-Lizenztexte, {1:N0} Zeichen" -f $mitCount, $noticeText.Length)
$appExe = Join-Path $out 'HDRpilot.exe'
$cliExe = Join-Path $out 'cli\hdrpilot.exe'
foreach ($exe in $appExe, $cliExe) {
    if (Test-Path $exe) {
        $machine = Get-Machine $exe
        Check ($machine -eq 0x8664) ("{0}: x64 (Machine 0x{1:X4}), {2:N1} MB" -f (Split-Path $exe -Leaf), $machine, ((Get-Item $exe).Length / 1MB))
    }
}
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$productVersion = (Get-Item $appExe).VersionInfo.ProductVersion
Check ($productVersion -like "$version*") "Produktversion $productVersion passt zu $version"

# 2b. Setup-Datei (E83, Format wie src\HdrPilot.App\Setup\SetupBundle.cs): Abschluss passt zur Dateigröße, der EXE-Teil
# ist byte-gleich mit der portablen EXE (Prüfsumme im Abschluss und tatsächlich), das Paket enthält genau die drei
# Beigaben, das Manifest verlangt keine Adminrechte. Gegenproben: ein gekipptes Byte im EXE-Teil fällt bei der
# Prüfsumme durch, ein abgeschnittenes Ende beim Abschluss, die portable EXE hat gar keinen.
. (Join-Path $PSScriptRoot 'Phase7Common.ps1')
$appInfo = (Get-Item $appExe).VersionInfo
$setupVersion = '{0}.{1}.{2}' -f $appInfo.FileMajorPart, $appInfo.FileMinorPart, $appInfo.FileBuildPart
Check ($setupVersion -eq (Get-ProjectVersion)) "Version der Setup-Datei $setupVersion (aus HDRpilot.exe) passt zu $version"
$setup = Join-Path (Split-Path $out -Parent) "HDRpilot-Setup-$setupVersion.exe"
$setupFiles = @(Get-ChildItem (Split-Path $out -Parent) -Filter 'HDRpilot-Setup-*.exe' -File | ForEach-Object { $_.Name })
Check ($setupFiles.Count -eq 1 -and $setupFiles[0] -eq (Split-Path $setup -Leaf)) ("Genau eine Setup-Datei neben dem portablen Ordner: {0}" -f ($setupFiles -join ', '))
$bundle = Test-SetupBundle $setup $out
foreach ($p in $bundle.Problems) { Note "  - $p" }
function Test-NoProblem($result, [string]$prefix) { @($result.Problems | Where-Object { $_.StartsWith($prefix) }).Count -eq 0 }
Check ((Test-Path $setup) -and (Test-NoProblem $bundle 'Setup-Datei') -and (Test-NoProblem $bundle 'Abschluss')) ("Setup-Datei {0}: Kennung HDRPILOT-SETUP01, EXE {1:N0} + Paket {2:N0} + 64 Bytes = Dateigröße" -f (Split-Path $setup -Leaf), $bundle.ExeLength, $bundle.PayloadLength)
Check ($bundle.ExeHash -and (Test-NoProblem $bundle 'EXE-Teil: SHA-256')) ("EXE-Teil SHA256 {0} = Abschluss = portable HDRpilot.exe" -f $bundle.ExeHash)
Check (@($bundle.Entries).Count -eq 3 -and (Test-NoProblem $bundle 'Paket')) ("Paket enthält genau {0}, inhaltsgleich mit der Auslieferung" -f (@($bundle.Entries) -join ', '))
Check ($bundle.AsInvoker -and (Test-NoProblem $bundle 'EXE-Teil: Manifest')) 'EXE-Teil enthält das Manifest mit asInvoker, keine erhöhten Rechte'
if ($bundle.ExeLength -gt 0) {
    $controlDir = Join-Path $work 'setup-gegenprobe'
    if (Test-Path $controlDir) { Remove-Item $controlDir -Recurse -Force }
    New-Item -ItemType Directory -Force $controlDir | Out-Null
    $flipped = Join-Path $controlDir 'gekippt.exe'
    Copy-Item $setup $flipped
    [HdrPilotGates.SetupFile]::FlipByte($flipped, [long][Math]::Floor($bundle.ExeLength / 2))
    $flippedResult = Test-SetupBundle $flipped $out
    Check ((Test-NoProblem $flippedResult 'Abschluss') -and $flippedResult.Problems -contains 'EXE-Teil: SHA-256 weicht vom Abschluss ab') ("Gegenprobe gekipptes Byte im EXE-Teil: Prüfsumme fällt durch ({0})" -f ($flippedResult.Problems -join '; '))
    $truncated = Join-Path $controlDir 'abgeschnitten.exe'
    [HdrPilotGates.SetupFile]::CopyPrefix($setup, $truncated, (Get-Item $setup).Length - 1000)
    $truncatedResult = Test-SetupBundle $truncated $out
    Check (-not (Test-NoProblem $truncatedResult 'Abschluss')) ("Gegenprobe abgeschnittenes Ende: Abschluss fällt durch ({0})" -f ($truncatedResult.Problems -join '; '))
    $plain = Test-SetupBundle $appExe $out
    Check (-not (Test-NoProblem $plain 'Abschluss')) ("Gegenprobe portable HDRpilot.exe: kein Abschluss, startet nie als Setup ({0})" -f ($plain.Problems -join '; '))
    Remove-Item $controlDir -Recurse -Force
}

# 3. Gegenprobe: Die Modulprüfung erkennt eine installierte Runtime (Debug-Build ist runtime-abhängig).
if (Test-Path $debugApp) {
    $control = Get-AppModules $debugApp (Join-Path $work 'control-config')
    $controlHits = @($control.Modules | Where-Object { $_ -match $installedRuntime })
    Check ($control.Started -and $controlHits.Count -gt 0) ("Gegenprobe Debug-Build: {0} Module aus der installierten Runtime erkannt (z. B. {1})" -f $controlHits.Count, ($controlHits | Select-Object -First 1))
} else {
    Check $false "Gegenprobe: Debug-Build fehlt ($debugApp), erst tools\gates\build.ps1 ausführen"
}

# 4. Veröffentlichte EXE als Kopie außerhalb des Repositorys, ohne erreichbare .NET-Installation
$run = Join-Path $work 'run'
if (Test-Path $run) { Remove-Item $run -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $run 'cli') | Out-Null
Copy-Item $appExe $run
Copy-Item $cliExe (Join-Path $run 'cli')
$env:DOTNET_ROOT = Join-Path $work 'kein-dotnet'
${env:DOTNET_ROOT(x86)} = $env:DOTNET_ROOT
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = (($env:PATH -split ';') | Where-Object { $_ -and $_ -notmatch '\\dotnet' }) -join ';'
Check ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) 'dotnet ist im Prüflauf nicht erreichbar'

$runApp = Join-Path $run 'HDRpilot.exe'
$runCli = Join-Path $run 'cli\hdrpilot.exe'
$cfg = Join-Path $work 'config'
if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }

$ErrorActionPreference = 'Continue' # Kommandozeile darf auf stderr schreiben
$importOutput = & $runCli games import --config-dir $cfg 2>&1 | Out-String
Check ($LASTEXITCODE -eq 0 -and (Test-Path (Join-Path $cfg 'config.json'))) ("Kommandozeile läuft eigenständig: games import exit={0}" -f $LASTEXITCODE)
$statusOutput = & $runCli hdr status 2>&1 | Out-String
Check ($LASTEXITCODE -eq 0) ("Kommandozeile: hdr status exit={0}" -f $LASTEXITCODE)
$ErrorActionPreference = 'Stop'

$result = Get-AppModules $runApp $cfg
$runtimeHits = @($result.Modules | Where-Object { $_ -match $installedRuntime })
$extracted = @($result.Modules | Where-Object { $_ -match '\\\.net\\HDRpilot\\' })
Check $result.Started 'Veröffentlichte App startet und liest die Bibliothek ein'
Check ($result.Modules.Count -gt 0 -and $runtimeHits.Count -eq 0) ("Keine Module aus einer installierten Runtime ({0} Module geladen, Treffer: {1})" -f $result.Modules.Count, $runtimeHits.Count)
Check ($result.Modules.Count -gt 0 -and $result.Modules[0] -like '*\HdrPilot-gate5\run\HDRpilot.exe') ("Hauptmodul ist die kopierte EXE: {0}" -f ($result.Modules | Select-Object -First 1))
Check ($extracted.Count -gt 0) ("Native WPF-Bibliotheken aus der EXE entpackt: {0}" -f (($extracted | ForEach-Object { Split-Path $_ -Leaf }) -join ', '))
Check ($result.Exited -and $result.ExitCode -eq 0) ("Beenden über --exit, Exitcode {0}" -f $result.ExitCode)

if ($ok) { Write-Output 'PHASE5_PUBLISH_OK'; exit 0 }
Write-Output 'PHASE5_PUBLISH_FAILED'
exit 1
