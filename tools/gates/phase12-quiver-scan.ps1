# Gate G12.4: Quiver Launcher am echten System, nur lesend (E91).
#   - Die gebaute Kommandozeile liest nur den Quiver Launcher ein ("scan --source Quiver") mit einer leeren eigenen Ablage im
#     Temp-Ordner (--config-dir), zweimal: in dieser Shell und außerhalb des Containers der Claude-App (Win32_Process.Create
#     über WMI, E84), dort mit dem echten Velopack-Protokoll unter %LOCALAPPDATA%\velopack. Beide Male muss
#     Quiver:Black-BlackXBERecomp mit <Quiver>\Apps\Black-BlackXBERecomp\BLACK.exe kommen (Start direkt, Arbeitsordner der
#     Spielordner, ohne Argumente), ohne Fehler; die Ablage bleibt leer.
#   - Vorher und nachher: Dateiliste des Quiver-Ordners (relativer Name, Größe, Änderungszeit, Attribute; nur Einträge der
#     Ordner, keine Datei wird geöffnet, höchstens 20000) muss gleich sein. Das Abbild mit 7,8 GB wird so nie gelesen.
#   - Startet weder Quiver noch ein Spiel, schaltet kein HDR, schreibt nur in -WorkDir.
#   PHASE12_QUIVER_SCAN_OK
param(
    [string]$QuiverFolder = '',
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'hdrpilot-phase12-quiver-scan'))
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$cliExe = Join-Path $root 'src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'
$expectedKey = 'Quiver:Black-BlackXBERecomp'
$script:ok = $true
function Note([string]$text) { Write-Output ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text) }
function Check([bool]$condition, [string]$text) { if ($condition) { Note "ok   $text" } else { Note "FEHLER $text"; $script:ok = $false } }

# Ordner „Downloads“ wie der Scanner: bekannter Ordner aus User Shell Folders, sonst %USERPROFILE%\Downloads
function Downloads-Folder {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders', $false)
    try { $raw = if ($key) { $key.GetValue('{374DE290-123F-4565-9164-39C4925E467B}') } else { $null } } finally { if ($key) { $key.Close() } }
    if ($raw) { return [Environment]::ExpandEnvironmentVariables([string]$raw) }
    return Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads'
}

# Dateiliste ohne Inhalte: je Eintrag relativer Pfad, Größe, Änderungszeit (UTC-Ticks), Attribute; sortiert
function Snapshot-Folder([string]$folder) {
    $items = @(Get-ChildItem -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue | Select-Object -First 20000)
    $lines = foreach ($i in $items) {
        $rel = $i.FullName.Substring($folder.Length).TrimStart('\')
        $len = if ($i.PSIsContainer) { '-' } else { $i.Length }
        '{0}|{1}|{2}|{3}' -f $rel, $len, $i.LastWriteTimeUtc.Ticks, [int]$i.Attributes
    }
    return @($lines | Sort-Object)
}

# Führt eine .cmd außerhalb des Containers aus (WMI) und wartet auf ihre Fertig-Datei (wie phase11-migration.ps1).
function Invoke-Outside([string]$name, [string[]]$lines) {
    $cmd = Join-Path $WorkDir "$name.cmd"
    $done = Join-Path $WorkDir "$name.fertig"
    Remove-Item $done -ErrorAction SilentlyContinue
    $body = @('@echo off', 'rem Nur lesen: Quiver einlesen mit leerer eigener Ablage (außerhalb des Containers der Claude-App).') + $lines + @("echo fertig> `"$done`"")
    [IO.File]::WriteAllLines($cmd, $body, [Text.Encoding]::Default)
    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "cmd.exe /d /c `"$cmd`""; CurrentDirectory = $WorkDir }
    if ($result.ReturnValue -ne 0) { throw "Win32_Process.Create scheiterte ($($result.ReturnValue))" }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $done) -and $sw.ElapsedMilliseconds -lt 120000) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path $done)) { throw "$name.cmd wurde außerhalb des Containers nicht fertig" }
}

# Prüft die Ausgabe eines Laufs; das Spiel steht danach in $script:game (Ausgaben der Prüfungen gehen ins Protokoll)
function Check-Scan([string]$label, [string]$json, [string]$ablage) {
    $script:game = $null
    $data = $null
    try { $data = $json | ConvertFrom-Json } catch { Check $false "$label`: Ausgabe ist kein JSON ($($_.Exception.Message))"; return }
    $scanner = @($data.scanners) | Where-Object { $_.source -eq 'Quiver' } | Select-Object -First 1
    $games = @($data.games)
    $game = $games | Where-Object { $_.key -eq $expectedKey } | Select-Object -First 1
    $errors = @($scanner.errors)
    Check ($null -ne $scanner -and $scanner.launcherDetected -and @($data.scanners).Count -eq 1) ("{0}: nur der Quiver-Scanner, Launcher erkannt ({1} ms)" -f $label, $scanner.durationMs)
    Check ($errors.Count -eq 0) ("{0}: ohne Fehler{1}" -f $label, $(if ($errors) { ': ' + (($errors | ForEach-Object { "$($_.message) $($_.path)" }) -join ' | ') } else { '' }))
    Check ($games.Count -ge 1 -and @($games | Where-Object { $_.source -ne 'Quiver' }).Count -eq 0) ("{0}: {1} Quiver-Spiel(e): {2}" -f $label, $games.Count, (($games | ForEach-Object { '{0} „{1}“' -f $_.key, $_.name }) -join ', '))
    Check ($null -ne $game) "$label`: $expectedKey gefunden"
    if ($game) {
        $exe = Join-Path $QuiverFolder 'Apps\Black-BlackXBERecomp\BLACK.exe'
        Check ($game.launchCommand -eq $exe -and @($game.executables) -contains $exe) ("{0}: Start-EXE {1}" -f $label, $game.launchCommand)
        Check ($game.launchWorkingDirectory -eq (Split-Path $exe) -and $game.installDir -eq (Split-Path $exe) -and -not $game.launchArguments) ("{0}: Arbeitsordner und Installationsordner {1}, ohne Argumente" -f $label, $game.launchWorkingDirectory)
        Note ('{0}: Name „{1}“, zuletzt gespielt {2}, Cover {3}' -f $label, $game.name, $game.lastPlayed, (@($game.coverCandidates) -join ', '))
        $cover = Join-Path $QuiverFolder 'Cache\Images\8C8FC6677FB84678288E4615E4E470A5'
        if (Test-Path -LiteralPath $cover) { Check (@($game.coverCandidates) -contains $cover) "$label`: Cover aus dem Zwischenspeicher von Quiver ($cover)" }
    }
    Check (@(Get-ChildItem -LiteralPath $ablage -Force -ErrorAction SilentlyContinue).Count -eq 0) "$label`: eigene Ablage bleibt leer (nichts geschrieben)"
    $script:game = $game
}

try {
    if (-not $QuiverFolder) { $QuiverFolder = Join-Path (Downloads-Folder) 'QuiverLauncher-win-Portable' }
    Check (Test-Path $cliExe) "Kommandozeile gebaut: $cliExe"
    Check (Test-Path -LiteralPath (Join-Path $QuiverFolder 'current\sq.version')) "Quiver-Ordner: $QuiverFolder"
    if (-not (Test-Path -LiteralPath $QuiverFolder)) { throw 'Quiver-Ordner fehlt' }
    if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
    New-Item -ItemType Directory -Force $WorkDir | Out-Null

    $before = Snapshot-Folder $QuiverFolder
    Note ("Dateiliste vorher: {0} Einträge (nur Namen, Größen, Zeitstempel)" -f $before.Count)
    Check ($before.Count -gt 0 -and $before.Count -lt 20000) 'Dateiliste vollständig und begrenzt'

    # --- In dieser Shell ---
    $ablage1 = Join-Path $WorkDir 'ablage-shell'
    New-Item -ItemType Directory -Force $ablage1 | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $json1 = (& $cliExe scan --source Quiver --config-dir $ablage1 2> (Join-Path $WorkDir 'shell.err')) | Out-String
    $code1 = $LASTEXITCODE
    $sw.Stop()
    [IO.File]::WriteAllText((Join-Path $WorkDir 'scan-shell.json'), $json1, (New-Object Text.UTF8Encoding $false))
    Check ($code1 -eq 0) ("In dieser Shell: Exit-Code {0}, {1} ms" -f $code1, $sw.ElapsedMilliseconds)
    Check-Scan 'In dieser Shell' $json1 $ablage1
    $game1 = $script:game

    # --- Außerhalb des Containers (echte Sicht auf %LOCALAPPDATA%) ---
    $ablage2 = Join-Path $WorkDir 'ablage-aussen'
    New-Item -ItemType Directory -Force $ablage2 | Out-Null
    $out2 = Join-Path $WorkDir 'scan-aussen.json'
    $log = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'velopack\velopack_QuiverLauncher.log'
    Invoke-Outside 'scan-aussen' @(
        "if exist `"$log`" (echo Velopack-Protokoll vorhanden> `"$WorkDir\velopack.txt`") else (echo Velopack-Protokoll fehlt> `"$WorkDir\velopack.txt`")",
        "`"$cliExe`" scan --source Quiver --config-dir `"$ablage2`" > `"$out2`" 2> `"$WorkDir\aussen.err`"",
        "echo %ERRORLEVEL%> `"$WorkDir\aussen.exit`"")
    $code2 = [int]([IO.File]::ReadAllText((Join-Path $WorkDir 'aussen.exit')).Trim())
    Note ('Außerhalb des Containers: ' + [IO.File]::ReadAllText((Join-Path $WorkDir 'velopack.txt')).Trim())
    Check ($code2 -eq 0) "Außerhalb des Containers: Exit-Code $code2"
    Check-Scan 'Außerhalb des Containers' ([IO.File]::ReadAllText($out2, [Text.Encoding]::UTF8)) $ablage2
    $game2 = $script:game
    if ($game1 -and $game2) { Check (($game1 | ConvertTo-Json -Compress -Depth 5) -eq ($game2 | ConvertTo-Json -Compress -Depth 5)) 'Beide Läufe liefern dasselbe Spiel' }

    # --- Quiver-Ordner unverändert ---
    $after = Snapshot-Folder $QuiverFolder
    $changed = @(Compare-Object $before $after | ForEach-Object { '{0} {1}' -f $_.SideIndicator, $_.InputObject })
    Check ($changed.Count -eq 0) ("Dateiliste des Quiver-Ordners vorher und nachher gleich ({0} Einträge, {1} Unterschiede{2})" -f $after.Count, $changed.Count, $(if ($changed) { ': ' + (($changed | Select-Object -First 10) -join ' | ') } else { '' }))
    Note "Arbeitsordner: $WorkDir"
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"; $script:ok = $false }
if ($script:ok) { Write-Output 'PHASE12_QUIVER_SCAN_OK'; exit 0 }
exit 1
