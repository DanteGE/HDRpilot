# Gate G11.3: Verträglichkeit an einer Kopie der echten Ablage (E90).
#   - Die echte config.json wird nur gelesen: Ein Prozess außerhalb des Containers der Claude-App (Win32_Process.Create über
#     WMI, E84) bildet ihre SHA-256-Prüfsumme und kopiert sie in -WorkDir; am Ende bildet er die Prüfsumme noch einmal. Sie muss
#     gleich sein (und gleich der Kopie). Die Sicht dieser Shell auf %APPDATA%\HDRpilot kann eine veraltete Kopie des
#     Containers sein und wird deshalb nie benutzt.
#   - Die Kopie lädt die neue App in einer eigenen Ablage (--config-dir) über --settings-probe: ohne Automatik, Einlesen und
#     Fenster, ohne Ladewarnung und ohne Sicherung als „defekt“; sie zählt die nicht installierten Spiele (wie die Karte
#     „Bibliothek“) und speichert wie die Einstellungen zweimal, erst unverändert, dann mit umgeschaltetem
#     „Nicht installierte Spiele ausblenden“. Je entfernbares Spiel nennt der Bericht die Spur einer Installation, wie die
#     Rückfrage des Sammel-Entfernens sie prüft (nur lesend in den echten Spielordnern: Laufwerk, EXE, Steam-Manifest).
#   - Unverändert gespeichert steht jeder Wert der Kopie unverändert in der Datei (nur Felder, die die alte Version nicht
#     kannte, kommen mit Standardwerten dazu); umgeschaltet unterscheidet sich die Datei davon nur in
#     settings.hideUninstalledGames.
#   PHASE11_MIGRATION_OK
param([string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'hdrpilot-phase11-migration'))
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$appExe = Join-Path $root 'src\HdrPilot.App\bin\Debug\net10.0-windows\HDRpilot.exe'
$script:ok = $true
function Note([string]$text) { Write-Output ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text) }
function Check([bool]$condition, [string]$text) { if ($condition) { Note "ok   $text" } else { Note "FEHLER $text"; $script:ok = $false } }

# Echte Ablage, als Pfad (das Lesen übernimmt der Prozess außerhalb des Containers)
$realConfig = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'HDRpilot\config.json'

# Führt eine .cmd außerhalb des Containers aus (WMI) und wartet auf ihre Fertig-Datei.
function Invoke-Outside([string]$name, [string[]]$lines) {
    $cmd = Join-Path $WorkDir "$name.cmd"
    $done = Join-Path $WorkDir "$name.fertig"
    Remove-Item $done -ErrorAction SilentlyContinue
    $body = @('@echo off', 'rem Nur lesen: Prüfsumme und Kopie der echten HDRpilot-Konfiguration (außerhalb des Containers der Claude-App).') + $lines + @("echo fertig> `"$done`"")
    [IO.File]::WriteAllLines($cmd, $body, [Text.Encoding]::Default)
    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "cmd.exe /d /c `"$cmd`""; CurrentDirectory = $WorkDir }
    if ($result.ReturnValue -ne 0) { throw "Win32_Process.Create scheiterte ($($result.ReturnValue))" }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $done) -and $sw.ElapsedMilliseconds -lt 60000) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path $done)) { throw "$name.cmd wurde außerhalb des Containers nicht fertig" }
}

# SHA-256 aus der Ausgabe von certutil (zweite Zeile, Hex mit oder ohne Leerzeichen)
function Read-CertutilHash([string]$file) {
    $m = [regex]::Match([IO.File]::ReadAllText($file), '(?m)^([0-9a-fA-F]{2}( ?[0-9a-fA-F]{2}){31})\s*$')
    if ($m.Success) { return $m.Groups[1].Value.Replace(' ', '').ToUpperInvariant() }
    return $null
}
function File-Hash([string]$file) { (Get-FileHash $file -Algorithm SHA256).Hash.ToUpperInvariant() }

# Unterschiede zweier JSON-Bäume (ConvertFrom-Json) als Pfade wie „settings.hideUninstalledGames“ oder „games[3].name“
function Compare-Json($left, $right, [string]$path) {
    $isObject = { param($v) $v -is [System.Management.Automation.PSCustomObject] }
    if ((& $isObject $left) -and (& $isObject $right)) {
        $names = @($left.PSObject.Properties.Name) + @($right.PSObject.Properties.Name) | Sort-Object -Unique
        foreach ($n in $names) {
            $p = if ($path) { "$path.$n" } else { $n }
            $l = $left.PSObject.Properties[$n]; $r = $right.PSObject.Properties[$n]
            if (-not $l -or -not $r) { $p; continue }
            Compare-Json $l.Value $r.Value $p
        }
        return
    }

    if ($left -is [array] -and $right -is [array]) {
        if ($left.Count -ne $right.Count) { $path; return }
        for ($i = 0; $i -lt $left.Count; $i++) { Compare-Json $left[$i] $right[$i] "$path[$i]" }
        return
    }

    if (($left | ConvertTo-Json -Compress -Depth 30) -cne ($right | ConvertTo-Json -Compress -Depth 30)) { $path }
}

# Gibt es den Pfad (aus Compare-Json) im Baum?
function Has-Path($tree, [string]$path) {
    $node = $tree
    foreach ($part in $path -split '\.') {
        $m = [regex]::Match($part, '^(?<name>[^\[]+)(\[(?<index>\d+)\])?$')
        $prop = if ($node -is [System.Management.Automation.PSCustomObject]) { $node.PSObject.Properties[$m.Groups['name'].Value] } else { $null }
        if (-not $prop) { return $false }
        $node = $prop.Value
        if ($m.Groups['index'].Success) {
            $i = [int]$m.Groups['index'].Value
            if (-not ($node -is [array]) -or $i -ge $node.Count) { return $false }
            $node = $node[$i]
        }
    }
    return $true
}

try {
    if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
    New-Item -ItemType Directory -Force $WorkDir | Out-Null
    $copy = Join-Path $WorkDir 'kopie\config.json'
    $ablage = Join-Path $WorkDir 'ablage'
    New-Item -ItemType Directory -Force (Split-Path $copy), $ablage | Out-Null
    Check (Test-Path $appExe) "App gebaut: $appExe"

    # --- Echte Ablage außerhalb des Containers: Prüfsumme und Kopie (nur lesen) ---
    Invoke-Outside 'vorher' @(
        "certutil -hashfile `"$realConfig`" SHA256 > `"$WorkDir\echt-vorher.txt`" 2>&1",
        "dir `"$realConfig`" > `"$WorkDir\echt-vorher-liste.txt`" 2>&1",
        "copy /Y `"$realConfig`" `"$copy`" > `"$WorkDir\kopie.log`" 2>&1")
    $hashBefore = Read-CertutilHash (Join-Path $WorkDir 'echt-vorher.txt')
    Check ($null -ne $hashBefore -and (Test-Path $copy)) ("Echte Ablage außerhalb des Containers gelesen und kopiert (SHA-256 {0})" -f $hashBefore)
    if (-not (Test-Path $copy)) { throw 'Keine Kopie der echten Ablage' }
    Check ((File-Hash $copy) -eq $hashBefore) 'Kopie gleicht der echten Datei'
    $original = [IO.File]::ReadAllText($copy, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $notInstalled = @($original.games | Where-Object { $_.isInstalled -eq $false })
    Note ("Kopie: {0} Spiele, {1} nicht installiert: {2}" -f @($original.games).Count, $notInstalled.Count, (($notInstalled | ForEach-Object { "$($_.name) ($($_.source))" }) -join ', '))
    $hadHide = $null -ne $original.settings.PSObject.Properties['hideUninstalledGames']
    Note ("hideUninstalledGames in der Kopie: {0}" -f $(if ($hadHide) { $original.settings.hideUninstalledGames } else { 'fehlt (ältere Version)' }))
    $shellView = if (Test-Path $realConfig) { File-Hash $realConfig } else { $null }
    if ($shellView -ne $hashBefore) { Note 'info Diese Shell sieht unter %APPDATA%\HDRpilot eine andere Datei (Container der Claude-App, E84); benutzt wird nur die Kopie von außen' }

    # --- Neue App lädt die Kopie in eigener Ablage, speichert unverändert und umgeschaltet ---
    Copy-Item $copy (Join-Path $ablage 'config.json')
    $report = Join-Path $WorkDir 'bericht.json'
    $probe = Start-Process $appExe -ArgumentList @('--config-dir', "`"$ablage`"", '--settings-probe', "`"$report`"") -PassThru -Wait
    Check ($probe.ExitCode -eq 0 -and (Test-Path $report)) "Prüflauf --settings-probe (Exit-Code $($probe.ExitCode))"
    if (-not (Test-Path $report)) { throw 'Kein Bericht' }
    $r = [IO.File]::ReadAllText($report, [Text.Encoding]::UTF8) | ConvertFrom-Json
    Check ($null -eq $r.loadWarning) ("Lädt ohne Warnung ({0})" -f $(if ($r.loadWarning) { $r.loadWarning } else { 'keine' }))
    $backups = @(Get-ChildItem $ablage -Filter 'config.json.*.bak' -ErrorAction SilentlyContinue)
    Check ($backups.Count -eq 0) ('Nichts als „defekt“ oder „neuere Version“ gesichert ({0} Sicherungen)' -f $backups.Count)
    Check ($r.games -eq @($original.games).Count) ("Alle Spiele geladen ({0})" -f $r.games)
    Check ($r.notInstalled -eq $notInstalled.Count -and $r.shownCount -eq $notInstalled.Count) ('Zählt die nicht installierten Spiele: {0} (Karte „Bibliothek“: zurzeit {1}): {2}' -f $r.notInstalled, $r.shownCount, (($r.notInstalledGames | ForEach-Object { $_.name }) -join ', '))
    $removable = @($notInstalled | Where-Object { $_.source -ne 'Manual' }).Count
    Check ($r.removable -eq $removable -and $r.shownRemovable -eq $removable) ("Davon entfernbar (Launcher): {0}" -f $r.removable)
    $traces = @($r.removableTraces | ForEach-Object { '{0}: {1}{2}' -f $_.name, $_.sign, $(if ($_.drive) { ' ' + $_.drive } else { '' }) })
    Check ($traces.Count -eq $r.removable) ('Spur einer Installation je entfernbares Spiel (wie die Rückfrage): ' + ($traces -join ', '))
    Check ($r.savedUnchanged -and $r.savedToggled) 'Zweimal gespeichert (unverändert, umgeschaltet)'
    Check ($r.hideAfter -eq (-not $r.hideBefore) -and $r.hideStored -eq $r.hideAfter) ("Umgeschaltet: hideUninstalledGames {0} → {1}" -f $r.hideBefore, $r.hideAfter)
    $logs = (Get-ChildItem (Join-Path $ablage 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8) }) -join "`n"
    Check ($logs -notmatch 'defekt|neueren Version|nicht lesbar') 'Protokoll ohne Ladefehler der Konfiguration'

    # --- Unterschiede ---
    $unchanged = [IO.File]::ReadAllText((Join-Path $WorkDir 'config.unveraendert.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
    $toggled = [IO.File]::ReadAllText((Join-Path $WorkDir 'config.umgeschaltet.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
    $lostOrChanged = @(Compare-Json $original $unchanged '' | Where-Object { Has-Path $original $_ })
    $added = @(Compare-Json $original $unchanged '' | Where-Object { -not (Has-Path $original $_) })
    Check ($lostOrChanged.Count -eq 0) ("Unverändert gespeichert: jeder Wert der Kopie unverändert ({0} abweichend{1})" -f $lostOrChanged.Count, $(if ($lostOrChanged) { ': ' + (($lostOrChanged | Select-Object -First 10) -join ', ') } else { '' }))
    Note ("Neu mit Standardwerten (Felder, die die Kopie nicht kannte): {0}" -f $(if ($added) { ($added | Select-Object -First 20) -join ', ' } else { 'keine' }))
    $diff = @(Compare-Json $unchanged $toggled '')
    Check (($diff -join ',') -eq 'settings.hideUninstalledGames') ("Nach Umschalten und Speichern unterscheidet sich die Datei nur in settings.hideUninstalledGames ({0})" -f ($diff -join ', '))
    $versusOriginal = @(Compare-Json $original $toggled '' | Where-Object { Has-Path $original $_ })
    Check (($versusOriginal | Where-Object { $_ -ne 'settings.hideUninstalledGames' }).Count -eq 0) ("Gegenüber der Kopie: keine andere Einstellung verloren oder geändert ({0})" -f $(if ($versusOriginal) { $versusOriginal -join ', ' } else { 'keine' }))

    # --- Echte Ablage danach unverändert ---
    Invoke-Outside 'nachher' @(
        "certutil -hashfile `"$realConfig`" SHA256 > `"$WorkDir\echt-nachher.txt`" 2>&1",
        "dir `"$realConfig`" > `"$WorkDir\echt-nachher-liste.txt`" 2>&1")
    $hashAfter = Read-CertutilHash (Join-Path $WorkDir 'echt-nachher.txt')
    Check ($null -ne $hashAfter -and $hashAfter -eq $hashBefore) ("Echte Ablage vorher und nachher gleich (SHA-256 {0} / {1})" -f $hashBefore, $hashAfter)
    if ($hashAfter -ne $hashBefore) {
        Note ('Liste vorher: ' + ((Get-Content (Join-Path $WorkDir 'echt-vorher-liste.txt') | Select-String 'config.json') -join ' '))
        Note ('Liste nachher: ' + ((Get-Content (Join-Path $WorkDir 'echt-nachher-liste.txt') | Select-String 'config.json') -join ' '))
    }
    Note "Arbeitsordner: $WorkDir"
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"; $script:ok = $false }
if ($script:ok) { Write-Output 'PHASE11_MIGRATION_OK'; exit 0 }
exit 1
