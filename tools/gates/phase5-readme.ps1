# Gate G5.2: README mit Einrichtung, Funktionsweise, bekannten Grenzen und Fehlersuche, abgeglichen mit dem Quelltext.
# Prüft Abschnitte, Befehlszeilen-Schalter der App, die Hilfe der Kommandozeile, Standardwerte, Ablageorte und alle fett
# gesetzten Oberflächen-Bezeichnungen gegen die resx-Dateien (inkl. SetupStrings.resx), dazu Schalter, Exit-Codes und
# Orte von Setup und Entfernen (E83), die Exit-Codes, die stilles Entfernen ohne ERGEBNIS-Zeile direkt liefert (E84),
# und den Standard des Neustart-Puffers in der Einstellungstabelle und in jedem Absatz, der ihn als Ursache oder Abhilfe
# nennt (E85). Gegenprobe: eine absichtlich verfälschte README muss genau die eingebauten Fehler liefern.
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
$devOnlySwitches = @('--snapshot', '--prepare', '--ready-event', # Entwicklerwerkzeug (E54) und interne Absprache Wrapper/Instanz (E75)
    '--snapshot-sizes', '--snapshot-scenes', '--snapshot-themes', '--snapshot-display', '--artwork-report', '--test-hdr-moment', # Bildschirmfotos, Messung und Prüfung der Neugestaltung (E89)
    '--test-toolbar-search') # Prüfung der Werkzeugleiste (G10.5): Strg+F ohne Tastatureingabe
$appSwitches = @([regex]::Matches($appArgs, '"(--[a-z-]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique | Where-Object { $_ -notin $devOnlySwitches })

$config = Read-Utf8 'src\HdrPilot.Core\Config\AppConfig.cs'
function Default([string]$name) { [int]([regex]::Match($config, "$name \{ get; set; \} = (\d+);").Groups[1].Value) }
$defaults = @{ Poll = Default 'PollIntervalMs'; Grace = Default 'GracePeriodSeconds'; Launch = Default 'LaunchTimeoutSeconds' }

# Neustart-Puffer (E85): Standard aus SessionManagerOptions (gilt, solange restartBufferSeconds fehlt), in der Tabelle m:ss
$bufferMatch = [regex]::Match((Read-Utf8 'src\HdrPilot.Core\Sessions\SessionManagerOptions.cs'), 'RestartBuffer \{ get; init; \} = TimeSpan\.From(Minutes|Seconds)\((\d+)\);')
$bufferSeconds = [int]$bufferMatch.Groups[2].Value * $(if ($bufferMatch.Groups[1].Value -eq 'Minutes') { 60 } else { 1 })
$defaults.Buffer = '{0}:{1:00}' -f [Math]::Floor($bufferSeconds / 60), ($bufferSeconds % 60)
# im Fließtext: "Standard 1 Minute", "Standard 2 Minuten" oder "Standard 1:30 min"
$defaults.BufferText = if ($bufferSeconds % 60 -ne 0) { "$($defaults.Buffer) min" } elseif ($bufferSeconds -eq 60) { '1 Minute' } else { '{0} Minuten' -f ($bufferSeconds / 60) }

$logger = Read-Utf8 'src\HdrPilot.Core\Logging\RollingFileLoggerProvider.cs'
$retainDays = [regex]::Match($logger, 'int retainDays = (\d+)').Groups[1].Value
$maxMb = [regex]::Match($logger, 'long maxFileBytes = (\d+) \* 1024 \* 1024').Groups[1].Value

$autostart = Read-Utf8 'src\HdrPilot.App\Services\AutostartService.cs'
$runKey = [regex]::Match($autostart, 'RunKeyPath = @"([^"]+)"').Groups[1].Value
$runValue = [regex]::Match($autostart, 'ValueName = "([^"]+)"').Groups[1].Value

$paths = Read-Utf8 'src\HdrPilot.Core\Config\AppPaths.cs'
$fileNames = @([regex]::Matches($paths, 'Path\.Combine\((?:ConfigDirectory|DataDirectory), "([^"]+)"\)') | ForEach-Object { $_.Groups[1].Value })

# Setup und Entfernen (E83): Schalter, Exit-Codes, Name der Setup-Datei, Programmordner, Protokoll, Eintrag
$setupArgs = Read-Utf8 'src\HdrPilot.App\Setup\SetupArguments.cs'
$setupDevOnly = @('--uninstall-continue', '--wait-pid') # intern: die Kopie im Temp-Ordner setzt das Entfernen fort
$setupSwitches = @([regex]::Matches($setupArgs, '"(--[a-z-]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique | Where-Object { $_ -notin $setupDevOnly })
$setupExitCodes = @([regex]::Matches($setupArgs, 'public const int \w+ = (\d+);') | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object)
# Stilles Entfernen kehrt sofort zurück; diese Codes liefert der erste Aufruf selbst (Kopie nicht gestartet, Aufruf
# falsch, Container), ohne Kopie und damit ohne ERGEBNIS-Zeile im Protokoll
$directUninstallCodes = @('Failed', 'BadArguments', 'Container' | ForEach-Object { [regex]::Match($setupArgs, "public const int $_ = (\d+);").Groups[1].Value } | Where-Object { $_ })
$props = [xml](Read-Utf8 'Directory.Build.props')
$productVersion = [Version](@($props.Project.PropertyGroup) | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
$setupFile = 'HDRpilot-Setup-{0}.{1}.{2}.exe' -f $productVersion.Major, $productVersion.Minor, [Math]::Max(0, $productVersion.Build)
$locations = Read-Utf8 'src\HdrPilot.App\Setup\SetupLocations.cs'
$programDir = '%LOCALAPPDATA%\' + (@([regex]::Match($locations, 'LocalApplicationData\), ("[^)]+)\)').Groups[1].Value -split ',\s*' | ForEach-Object { $_.Trim('"') }) -join '\')
$uninstallKey = 'HKCU\' + [regex]::Match($locations, 'WindowsRegistryRoot = @"([^"]+)"').Groups[1].Value + '\Uninstall\' + [regex]::Match($locations, 'UninstallKeyName = "([^"]+)"').Groups[1].Value
$setupLog = '%TEMP%\' + [regex]::Match((Read-Utf8 'src\HdrPilot.App\Setup\SetupLog.cs'), 'GetTempPath\(\), "([^"]+)"').Groups[1].Value

$uiLabels = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
$missingResx = @()
foreach ($resx in 'src\HdrPilot.App\Resources\ShellStrings.resx', 'src\HdrPilot.App\Resources\LibraryStrings.resx', 'src\HdrPilot.App\Resources\CommonStrings.resx', 'src\HdrPilot.App\Resources\SetupStrings.resx', 'src\HdrPilot.App\Resources\ArtworkStrings.resx') {
    if (-not (Test-Path (Join-Path $root $resx))) { $missingResx += $resx; continue }
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
Check ($defaults.Poll -gt 0 -and $defaults.Grace -gt 0 -and $defaults.Launch -gt 0 -and $bufferMatch.Success) ("Standardwerte: Intervall {0} ms, Nachlauf {1} s, Startfrist {2} s, Neustart-Puffer {3}" -f $defaults.Poll, $defaults.Grace, $defaults.Launch, $defaults.Buffer)
Check ($helpUsage.Count -ge 8 -and $helpExitPairs.Count -eq 7) ("Hilfe: {0} Aufrufzeilen, {1} Exit-Codes" -f $helpUsage.Count, $helpExitPairs.Count)
Check ($uiLabels.Count -gt 200 -and $missingResx.Count -eq 0) ("Oberflächen-Texte aus resx: {0}{1}" -f $uiLabels.Count, $(if ($missingResx) { ', fehlt: ' + ($missingResx -join ', ') } else { ' (inkl. SetupStrings.resx)' }))
Check ($setupSwitches.Count -ge 8 -and $setupExitCodes.Count -ge 7) ("Setup-Schalter aus SetupArguments.cs: {0}; Exit-Codes {1}" -f ($setupSwitches -join ' '), ($setupExitCodes -join ' '))
Check ($directUninstallCodes.Count -eq 3) ("Stilles Entfernen, Exit-Codes direkt vom ersten Aufruf: {0}" -f ($directUninstallCodes -join ' '))
Check ($programDir -eq '%LOCALAPPDATA%\Programs\HDRpilot' -and $uninstallKey -like 'HKCU\Software\*\Uninstall\HDRpilot' -and $setupLog -like '%TEMP%\*.log') ("Setup-Orte: {0}, {1}, {2}, {3}" -f $setupFile, $programDir, $uninstallKey, $setupLog)

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

    # Setup und Entfernen: eigener Unterabschnitt unter "## Befehlszeile", alle Schalter (in Code-Abschnitten, auch
    # hinter dem Programmnamen) dokumentiert, keine erfundenen, Exit-Codes wie in SetupExitCodes
    $commandLine = [regex]::Match($text, '(?ms)^## Befehlszeile\s*$(.*?)(?=^## |\z)').Groups[1].Value
    $setupSection = [regex]::Match($commandLine, '(?ms)^### Setup-Datei und Entfernen\s*$(.*?)(?=^### |\z)')
    if (-not $setupSection.Success) { [void]$problems.Add('Abschnitt fehlt: ### Setup-Datei und Entfernen (unter ## Befehlszeile)') }
    $setupDocumented = @([regex]::Matches($setupSection.Groups[1].Value, '`([^`]+)`') | ForEach-Object { [regex]::Matches($_.Groups[1].Value, '(?<![\w-])(--[a-z][a-z-]*)') } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    foreach ($s in $setupSwitches) { if ($s -notin $setupDocumented) { [void]$problems.Add("Setup-Schalter nicht dokumentiert: $s") } }
    foreach ($s in $setupDocumented) { if ($s -notin $setupSwitches) { [void]$problems.Add("Setup-Schalter gibt es nicht: $s") } }
    $setupExitLine = Normalize ([regex]::Match($setupSection.Groups[1].Value, '(?s)Exit-Codes:(.*?)(\r?\n\r?\n|\z)').Groups[1].Value)
    $readmeExitCodes = @([regex]::Matches($setupExitLine, '(?:^|, )(\d+) ') | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object)
    if (($readmeExitCodes -join ',') -ne ($setupExitCodes -join ',')) { [void]$problems.Add("Setup-Exit-Codes weichen ab: README $($readmeExitCodes -join ',') statt $($setupExitCodes -join ',')") }
    # Zeile zu --quiet beim Entfernen: nennt jeden Code, der ohne ERGEBNIS-Zeile direkt zurückkommt, als "<Code> (…)"
    $quietUninstallRow = [regex]::Match($setupSection.Groups[1].Value, '(?m)^\| `--quiet` \| Entfernen[^\r\n]*').Value
    foreach ($c in $directUninstallCodes) {
        if ($quietUninstallRow -notmatch "(?<![\w-])$c \(") { [void]$problems.Add("Stilles Entfernen: Exit-Code $c kommt direkt vom ersten Aufruf, fehlt in der Zeile zu --quiet") }
    }

    # Kommandozeile: Aufrufzeilen identisch mit der Hilfe, Exit-Codes vollständig
    $cliBlock = [regex]::Match($text, '(?s)### cli\\hdrpilot\.exe.*?```\r?\n(.*?)```').Groups[1].Value
    $readmeUsage = @($cliBlock -split "`r?`n" | Where-Object { $_ -cmatch '^hdrpilot ' } | ForEach-Object { Normalize $_ })
    foreach ($u in $helpUsage) { if ($u -notin $readmeUsage) { [void]$problems.Add("CLI-Aufruf fehlt oder weicht ab: $u") } }
    foreach ($u in $readmeUsage) { if ($u -notin $helpUsage) { [void]$problems.Add("CLI-Aufruf nicht in der Hilfe: $u") } }
    foreach ($pair in $helpExitPairs) { if ($flat -notlike "*$pair*") { [void]$problems.Add("Exit-Code fehlt: $pair") } }

    # Standardwerte und Ablageorte
    foreach ($fact in @("| Abfrageintervall | Wie oft nach Spielstarts gesehen wird | $($defaults.Poll) ms |", "Nachlaufzeit ($($defaults.Grace) s)", "| $($defaults.Launch) s |",
                        "höchstens $maxMb MB je Datei, $retainDays Tage", "HKCU\$runKey", "Wert ``$runValue``",
                        "``$setupFile``", "``$programDir", "``$uninstallKey``", "``$setupLog``")) {
        if ($text.IndexOf($fact, [StringComparison]::Ordinal) -lt 0) { [void]$problems.Add("Fakt fehlt oder weicht ab: $fact") }
    }
    foreach ($name in $fileNames) {
        if ($text -notmatch [regex]::Escape($name)) { [void]$problems.Add("Ablageort fehlt: $name") }
    }
    if ($text -notmatch "(?m)^\| Neustart-Puffer \|[^\r\n]*\| $([regex]::Escape($defaults.Buffer)) \|\r?$") { [void]$problems.Add("Fakt fehlt oder weicht ab: Neustart-Puffer $($defaults.Buffer)") }
    # Jeder Absatz und Listenpunkt in "So funktioniert es", "Bekannte Grenzen" und "Fehlersuche", der den Neustart-Puffer
    # nennt, nennt auch seinen Standard: Mit 3:00 fing er etwa den Neustart von Dead Island 2 (96 s) ab, mit 1:00 nicht.
    foreach ($section in 'So funktioniert es', 'Bekannte Grenzen', 'Fehlersuche') {
        $body = [regex]::Match($text, '(?ms)^## ' + [regex]::Escape($section) + '\s*$(.*?)(?=^## |\z)').Groups[1].Value
        foreach ($block in [regex]::Split($body, '(?:\r?\n){2,}|\r?\n(?=- )')) {
            $b = Normalize $block
            if ($b.Contains('Neustart-Puffer') -and -not $b.Contains("Standard $($defaults.BufferText)")) {
                $title = [regex]::Match($b, '\*\*([^*]+?):?\*\*').Groups[1].Value
                [void]$problems.Add("Neustart-Puffer ohne Standard $($defaults.BufferText): $section, $(if ($title) { $title } else { $b.Substring(0, [Math]::Min(40, $b.Length)) })")
            }
        }
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
# Setup (E83): Bezeichnung nur aus SetupStrings.resx, Schalter des Setups (jede Nennung, auch im Text zu --quiet),
# Protokollort
$broken = $broken.Replace('**Verknüpfung auf dem Desktop**', '**Desktop-Symbol**').Replace('`--no-launch`', '`--no-start`').Replace("``$setupLog``", '`%TEMP%\HDRpilot.log`')
# Stilles Entfernen (E84): die Zeile in der alten Fassung, die nur die ERGEBNIS-Zeile versprach
$broken = [regex]::Replace($broken, '(?m)^\| `--quiet` \| Entfernen[^\r\n]*', '| `--quiet` | Entfernen ohne Fenster: kehrt sofort zurück, das Ergebnis steht danach als `ERGEBNIS: <Exit-Code>` im Protokoll |')
# Neustart-Puffer (E85): alter Standard in der Tabelle
$broken = $broken.Replace("| $($defaults.Buffer) |", '| 3:00 |')
$expected = @('Bezeichnung nicht in der Oberfläche: Neu laden', 'App-Schalter nicht dokumentiert: --rescan', 'App-Schalter gibt es nicht: --reload',
              "Fakt fehlt oder weicht ab: Nachlaufzeit ($($defaults.Grace) s)", 'Abschnitt fehlt: Fehlersuche', 'CLI-Aufruf fehlt oder weicht ab: hdrpilot games list [--json]', 'CLI-Aufruf nicht in der Hilfe: hdrpilot games list',
              'Bezeichnung nicht in der Oberfläche: Desktop-Symbol', 'Setup-Schalter nicht dokumentiert: --no-launch', 'Setup-Schalter gibt es nicht: --no-start', "Fakt fehlt oder weicht ab: ``$setupLog``",
              "Fakt fehlt oder weicht ab: Neustart-Puffer $($defaults.Buffer)")
$expected += @($directUninstallCodes | ForEach-Object { "Stilles Entfernen: Exit-Code $_ kommt direkt vom ersten Aufruf, fehlt in der Zeile zu --quiet" })
$found = Test-Readme $broken
$missed = @($expected | Where-Object { $_ -notin $found })
Check ($missed.Count -eq 0 -and $found.Count -eq $expected.Count) ("Gegenprobe: {0} von {1} eingebauten Fehlern erkannt, {2} Meldungen insgesamt{3}" -f ($expected.Count - $missed.Count), $expected.Count, $found.Count, $(if ($missed) { '; übersehen: ' + ($missed -join ' | ') } else { '' }))

# Gegenprobe Neustart-Puffer im Fließtext (E85, eigene README, weil die erste "Fehlersuche" umbenennt): der Listenpunkt
# zu Dead Island 2 wieder ohne Standard (Stand vor der Korrektur), in den beiden anderen Absätzen der alte Standard
$brokenBuffer = [regex]::Replace($readme, '(?s)(Der Neustart-Puffer hilft beim zweiten Versuch)[^;]*;[^;]*;', '$1;')
$brokenBuffer = $brokenBuffer.Replace("Standard $($defaults.BufferText)", 'Standard 3 Minuten')
$expectedBuffer = @("Neustart-Puffer ohne Standard $($defaults.BufferText): So funktioniert es, Neustart-Puffer",
                    "Neustart-Puffer ohne Standard $($defaults.BufferText): Bekannte Grenzen, Umschalten während des Spielstarts",
                    "Neustart-Puffer ohne Standard $($defaults.BufferText): Fehlersuche, HDR bleibt nach dem Spiel an")
$foundBuffer = Test-Readme $brokenBuffer
$missedBuffer = @($expectedBuffer | Where-Object { $_ -notin $foundBuffer })
Check ($missedBuffer.Count -eq 0 -and $foundBuffer.Count -eq $expectedBuffer.Count -and $brokenBuffer -match 'beim zweiten Versuch; sicherer' -and $brokenBuffer -notmatch 'Standard 3 Minuten;') ("Gegenprobe Neustart-Puffer im Fließtext: {0} von {1} erkannt, {2} Meldungen insgesamt{3}" -f ($expectedBuffer.Count - $missedBuffer.Count), $expectedBuffer.Count, $foundBuffer.Count, $(if ($missedBuffer) { '; übersehen: ' + ($missedBuffer -join ' | ') } else { '' }))

if ($ok) { Write-Output 'PHASE5_README_OK'; exit 0 }
Write-Output 'PHASE5_README_FAILED'
exit 1
