# Gate G5.2: README mit Einrichtung, Funktionsweise, bekannten Grenzen und Fehlersuche, abgeglichen mit dem Quelltext.
# Prüft Abschnitte, Befehlszeilen-Schalter der App, die Hilfe der Kommandozeile, Standardwerte, Ablageorte und alle fett
# gesetzten Oberflächen-Bezeichnungen gegen die resx-Dateien. Gegenprobe: eine absichtlich verfälschte README muss
# genau die eingebauten Fehler liefern.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$cliExe = Join-Path $root 'src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'
$script:ok = $true

function Note([string]$text) { Write-Output ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text) }
function Check([bool]$condition, [string]$text) {
    if ($condition) { Note "ok   $text" } else { Note "FEHLER $text"; $script:ok = $false }
}
function Read-Utf8([string]$relative) { [IO.File]::ReadAllText((Join-Path $root $relative), [Text.Encoding]::UTF8) }
function Normalize([string]$s) { ($s -replace '\s+', ' ').Trim() }

# --- Fakten aus dem Quelltext ---
$appArgs = Read-Utf8 'src\HdrPilot.App\AppArguments.cs'
$devOnlySwitches = @('--snapshot', '--prepare', '--ready-event') # Entwicklerwerkzeug (E54) und interne Absprache Wrapper/Instanz (E75)
$appSwitches = @([regex]::Matches($appArgs, '"(--[a-z-]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique | Where-Object { $_ -notin $devOnlySwitches })

$config = Read-Utf8 'src\HdrPilot.Core\Config\AppConfig.cs'
function Default([string]$name) { [int]([regex]::Match($config, "$name \{ get; set; \} = (\d+);").Groups[1].Value) }
$defaults = @{ Poll = Default 'PollIntervalMs'; Grace = Default 'GracePeriodSeconds'; Launch = Default 'LaunchTimeoutSeconds' }

$logger = Read-Utf8 'src\HdrPilot.Core\Logging\RollingFileLoggerProvider.cs'
$retainDays = [regex]::Match($logger, 'int retainDays = (\d+)').Groups[1].Value
$maxMb = [regex]::Match($logger, 'long maxFileBytes = (\d+) \* 1024 \* 1024').Groups[1].Value

$autostart = Read-Utf8 'src\HdrPilot.App\Services\AutostartService.cs'
$runKey = [regex]::Match($autostart, 'RunKeyPath = @"([^"]+)"').Groups[1].Value
$runValue = [regex]::Match($autostart, 'ValueName = "([^"]+)"').Groups[1].Value

$paths = Read-Utf8 'src\HdrPilot.Core\Config\AppPaths.cs'
$fileNames = @([regex]::Matches($paths, 'Path\.Combine\((?:ConfigDirectory|DataDirectory), "([^"]+)"\)') | ForEach-Object { $_.Groups[1].Value })

$uiLabels = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
foreach ($resx in 'src\HdrPilot.App\Resources\ShellStrings.resx', 'src\HdrPilot.App\Resources\LibraryStrings.resx', 'src\HdrPilot.App\Resources\CommonStrings.resx') {
    ([xml](Read-Utf8 $resx)).root.data | ForEach-Object { [void]$uiLabels.Add((Normalize $_.value)) }
}

$ErrorActionPreference = 'Continue'
$helpText = (& $cliExe --help 2>&1 | Out-String)
$helpExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Check ($helpExit -eq 0 -and $helpText -match 'hdrpilot hdr status') "Hilfe der Kommandozeile gelesen (exit=$helpExit)"
$helpUsage = @($helpText -split "`r?`n" | Where-Object { $_ -cmatch '^hdrpilot ' } | ForEach-Object { Normalize $_ })
$helpExitCodes = Normalize ((($helpText -split "`r?`n") | Where-Object { $_ -match 'Exit-Codes|^\s+\d+ ' }) -join ' ')
$helpExitPairs = @([regex]::Matches($helpExitCodes, '(\d) ([^,]+?)(?=,|$)') | ForEach-Object { Normalize ($_.Groups[1].Value + ' ' + $_.Groups[2].Value) })

Check ($appSwitches.Count -ge 9) ("App-Schalter aus AppArguments.cs: {0}" -f ($appSwitches -join ' '))
Check ($defaults.Poll -gt 0 -and $defaults.Grace -gt 0 -and $defaults.Launch -gt 0) ("Standardwerte: Intervall {0} ms, Nachlauf {1} s, Startfrist {2} s" -f $defaults.Poll, $defaults.Grace, $defaults.Launch)
Check ($helpUsage.Count -ge 8 -and $helpExitPairs.Count -eq 7) ("Hilfe: {0} Aufrufzeilen, {1} Exit-Codes" -f $helpUsage.Count, $helpExitPairs.Count)
Check ($uiLabels.Count -gt 200) ("Oberflächen-Texte aus resx: {0}" -f $uiLabels.Count)

# --- Prüfung einer README, liefert die Liste der Probleme ---
function Test-Readme([string]$text) {
    $problems = New-Object System.Collections.ArrayList
    $flat = Normalize $text

    foreach ($section in 'Voraussetzungen', 'Einrichtung', 'Bedienung', 'So funktioniert es', 'Speicherorte', 'Befehlszeile', 'Bekannte Grenzen', 'Fehlersuche', 'Entfernen') {
        if ($text -notmatch "(?m)^## $([regex]::Escape($section))\s*$") { [void]$problems.Add("Abschnitt fehlt: $section") }
    }
    foreach ($word in 'TODO', 'FIXME', 'TBD', 'XXX') {
        if ($text -cmatch "\b$word\b") { [void]$problems.Add("Platzhalter: $word") }
    }

    # App-Schalter: alle dokumentiert, keine erfundenen
    $appSection = [regex]::Match($text, '(?s)### HDRpilot\.exe(.*?)### ').Groups[1].Value
    $documented = @([regex]::Matches($appSection, '`(--[a-z-]+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    foreach ($s in $appSwitches) { if ($s -notin $documented) { [void]$problems.Add("App-Schalter nicht dokumentiert: $s") } }
    foreach ($s in $documented) { if ($s -notin $appSwitches) { [void]$problems.Add("App-Schalter gibt es nicht: $s") } }

    # Kommandozeile: Aufrufzeilen identisch mit der Hilfe, Exit-Codes vollständig
    $cliBlock = [regex]::Match($text, '(?s)### cli\\hdrpilot\.exe.*?```\r?\n(.*?)```').Groups[1].Value
    $readmeUsage = @($cliBlock -split "`r?`n" | Where-Object { $_ -cmatch '^hdrpilot ' } | ForEach-Object { Normalize $_ })
    foreach ($u in $helpUsage) { if ($u -notin $readmeUsage) { [void]$problems.Add("CLI-Aufruf fehlt oder weicht ab: $u") } }
    foreach ($u in $readmeUsage) { if ($u -notin $helpUsage) { [void]$problems.Add("CLI-Aufruf nicht in der Hilfe: $u") } }
    foreach ($pair in $helpExitPairs) { if ($flat -notlike "*$pair*") { [void]$problems.Add("Exit-Code fehlt: $pair") } }

    # Standardwerte und Ablageorte
    foreach ($fact in @("| Abfrageintervall | Wie oft nach Spielstarts gesehen wird | $($defaults.Poll) ms |", "Nachlaufzeit ($($defaults.Grace) s)", "| $($defaults.Launch) s |",
                        "höchstens $maxMb MB je Datei, $retainDays Tage", "HKCU\$runKey", "Wert ``$runValue``")) {
        if ($text.IndexOf($fact, [StringComparison]::Ordinal) -lt 0) { [void]$problems.Add("Fakt fehlt oder weicht ab: $fact") }
    }
    foreach ($name in $fileNames) {
        if ($text -notmatch [regex]::Escape($name)) { [void]$problems.Add("Ablageort fehlt: $name") }
    }

    # Fett gesetzte Bezeichnungen in Einrichtung, Bedienung und Entfernen (dort ist Fettdruck immer ein Oberflächen-Text)
    # und die erste Spalte der Einstellungstabelle müssen Oberflächen-Texte sein.
    $uiSections = @([regex]::Matches($text, '(?ms)^## (?:Einrichtung|Bedienung|Entfernen)\s*$(.*?)(?=^## |\z)') | ForEach-Object { $_.Groups[1].Value }) -join "`n"
    $labels = @([regex]::Matches($uiSections, '\*\*([^*]+)\*\*') | ForEach-Object { $_.Groups[1].Value })
    $settingsTable = [regex]::Match($text, '(?s)### Einstellungen(.*?)\r?\n\r?\n[^|]').Groups[1].Value
    $labels += @([regex]::Matches($settingsTable, '(?m)^\| ([^|]+?) \|') | ForEach-Object { $_.Groups[1].Value -replace ' \(Erweitert\)$', '' } | Where-Object { $_ -notin 'Einstellung', '---' })
    foreach ($label in $labels) {
        $l = Normalize $label
        $parts = @($l -split ' / ')
        foreach ($part in $parts) {
            if (-not $uiLabels.Contains($part)) { [void]$problems.Add("Bezeichnung nicht in der Oberfläche: $part") }
        }
    }
    return ,$problems
}

$readmePath = Join-Path $root 'README.md'
Check (Test-Path $readmePath) 'README.md vorhanden'
$readme = [IO.File]::ReadAllText($readmePath, [Text.Encoding]::UTF8)
$problems = Test-Readme $readme
foreach ($p in $problems) { Note "  - $p" }
Check ($problems.Count -eq 0) ("README stimmt mit dem Quelltext überein ({0} Probleme)" -f $problems.Count)

# Gegenprobe: eingebaute Fehler müssen gefunden werden
$broken = $readme.Replace('**Neu einlesen**', '**Neu laden**').Replace('| `--rescan` | Spiele neu einlesen |', '| `--reload` | Spiele neu einlesen |').Replace("Nachlaufzeit ($($defaults.Grace) s)", 'Nachlaufzeit (5 s)').Replace('## Fehlersuche', '## Hilfe').Replace('hdrpilot games list [--json]', 'hdrpilot games list')
$expected = @('Bezeichnung nicht in der Oberfläche: Neu laden', 'App-Schalter nicht dokumentiert: --rescan', 'App-Schalter gibt es nicht: --reload',
              "Fakt fehlt oder weicht ab: Nachlaufzeit ($($defaults.Grace) s)", 'Abschnitt fehlt: Fehlersuche', 'CLI-Aufruf fehlt oder weicht ab: hdrpilot games list [--json]', 'CLI-Aufruf nicht in der Hilfe: hdrpilot games list')
$found = Test-Readme $broken
$missed = @($expected | Where-Object { $_ -notin $found })
Check ($missed.Count -eq 0 -and $found.Count -eq $expected.Count) ("Gegenprobe: {0} von {1} eingebauten Fehlern erkannt, {2} Meldungen insgesamt{3}" -f ($expected.Count - $missed.Count), $expected.Count, $found.Count, $(if ($missed) { '; übersehen: ' + ($missed -join ' | ') } else { '' }))

if ($ok) { Write-Output 'PHASE5_README_OK'; exit 0 }
Write-Output 'PHASE5_README_FAILED'
exit 1
