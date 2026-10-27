# Gate G5.2: README mit Einrichtung, Funktionsweise, bekannten Grenzen und Fehlersuche, abgeglichen mit dem Quelltext.
# Prüft Abschnitte, Befehlszeilen-Schalter der App, die Hilfe der Kommandozeile, Standardwerte, Ablageorte und alle fett
# gesetzten Oberflächen-Bezeichnungen gegen die resx-Dateien (inkl. SetupStrings.resx), dazu Schalter, Exit-Codes und
# Orte von Setup und Entfernen (E83), die Exit-Codes, die stilles Entfernen ohne ERGEBNIS-Zeile direkt liefert (E84),
# und den Standard des Neustart-Puffers in der Einstellungstabelle und in jedem Absatz, der ihn als Ursache oder Abhilfe
# nennt (E85). Gegenprobe: eine absichtlich verfälschte README muss genau die eingebauten Fehler liefern.
# Seit 2026-10-27 (E95) zwei Fassungen: README.de.md gegen die neutralen (deutschen) resx und die deutsche Hilfe,
# README.md gegen die .en.resx und die englische Hilfe, jede mit eigenen Abschnittsnamen und eigenen Gegenproben.
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
    '--test-toolbar-search', # Prüfung der Werkzeugleiste (G10.5): Strg+F ohne Tastatureingabe
    '--settings-probe', # Prüfung der Verträglichkeit (G11.3): Kopie der Ablage wie die Einstellungen speichern
    '--snapshot-language', '--restarted') # Sprache der Bildschirmfotos und interner Neustart nach dem Wechsel der Sprache (E95)
$appSwitches = @([regex]::Matches($appArgs, '"(--[a-z-]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique | Where-Object { $_ -notin $devOnlySwitches })

$config = Read-Utf8 'src\HdrPilot.Core\Config\AppConfig.cs'
function Default([string]$name) { [int]([regex]::Match($config, "$name \{ get; set; \} = (\d+);").Groups[1].Value) }
$defaults = @{ Poll = Default 'PollIntervalMs'; Grace = Default 'GracePeriodSeconds'; Launch = Default 'LaunchTimeoutSeconds' }

# Neustart-Puffer (E85): Standard aus SessionManagerOptions (gilt, solange restartBufferSeconds fehlt), in der Tabelle m:ss
$bufferMatch = [regex]::Match((Read-Utf8 'src\HdrPilot.Core\Sessions\SessionManagerOptions.cs'), 'RestartBuffer \{ get; init; \} = TimeSpan\.From(Minutes|Seconds)\((\d+)\);')
$bufferSeconds = [int]$bufferMatch.Groups[2].Value * $(if ($bufferMatch.Groups[1].Value -eq 'Minutes') { 60 } else { 1 })
$defaults.Buffer = '{0}:{1:00}' -f [Math]::Floor($bufferSeconds / 60), ($bufferSeconds % 60)
# im Fließtext: "Standard 1 Minute", "Standard 2 Minuten" oder "Standard 1:30 min" bzw. "default 1 minute",
# "default 2 minutes" oder "default 1:30 min"
$defaults.BufferTextDe = if ($bufferSeconds % 60 -ne 0) { "Standard $($defaults.Buffer) min" } elseif ($bufferSeconds -eq 60) { 'Standard 1 Minute' } else { 'Standard {0} Minuten' -f ($bufferSeconds / 60) }
$defaults.BufferTextEn = if ($bufferSeconds % 60 -ne 0) { "default $($defaults.Buffer) min" } elseif ($bufferSeconds -eq 60) { 'default 1 minute' } else { 'default {0} minutes' -f ($bufferSeconds / 60) }

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

# Oberflächen-Texte je Sprache: neutral (deutsch) bzw. .en.resx derselben Dateien
$resxNames = 'ShellStrings', 'LibraryStrings', 'CommonStrings', 'SetupStrings', 'ArtworkStrings'
function Read-UiLabels([string]$suffix) {
    $set = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    $missing = @()
    foreach ($name in $resxNames) {
        $resx = "src\HdrPilot.App\Resources\$name$suffix.resx"
        if (-not (Test-Path (Join-Path $root $resx))) { $missing += $resx; continue }
        ([xml](Read-Utf8 $resx)).root.data | ForEach-Object { [void]$set.Add((Normalize $_.value)) }
    }
    return [pscustomobject]@{ Labels = $set; Missing = $missing }
}

# Hilfe der Kommandozeile in der Sprache der README (E95): eigene Ablage mit "language", nie die echte (deren Sprache
# kann eine andere sein).
function Read-CliHelp([string]$language) {
    $ErrorActionPreference = 'Continue'
    $helpDir = Join-Path ([IO.Path]::GetTempPath()) ('HDRpilot-Gate5-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force $helpDir | Out-Null
    [IO.File]::WriteAllText((Join-Path $helpDir 'config.json'), ('{ "version": 1, "settings": { "language": "' + $language + '" }, "games": [] }'), (New-Object Text.UTF8Encoding $false))
    $text = (& $cliExe --help --config-dir $helpDir 2>&1 | Out-String)
    $exit = $LASTEXITCODE
    Remove-Item $helpDir -Recurse -Force -ErrorAction SilentlyContinue
    $usage = @($text -split "`r?`n" | Where-Object { $_ -cmatch '^hdrpilot ' } | ForEach-Object { Normalize $_ })
    $codes = Normalize ((($text -split "`r?`n") | Where-Object { $_ -match 'Exit[- ]codes|^\s+\d+ ' }) -join ' ')
    $pairs = @([regex]::Matches($codes, '(\d) ([^,]+?)(?=,|$)') | ForEach-Object { Normalize ($_.Groups[1].Value + ' ' + $_.Groups[2].Value) })
    return [pscustomobject]@{ Text = $text; Exit = $exit; Usage = $usage; ExitPairs = $pairs }
}

# Sprachfassungen: Datei, Texte, Hilfe und die Wörter, an denen die Prüfung Abschnitte und Fakten erkennt
$german = Read-UiLabels ''
$english = Read-UiLabels '.en'
$profiles = @(
    [pscustomobject]@{
        Name = 'README.de.md'; Language = 'de'; Ui = $german; Help = (Read-CliHelp 'de')
        Sections = @('Voraussetzungen', 'Einrichtung', 'Bedienung', 'So funktioniert es', 'Speicherorte', 'Befehlszeile', 'Bekannte Grenzen', 'Fehlersuche', 'Entfernen')
        CommandLine = 'Befehlszeile'; SetupHeading = 'Setup-Datei und Entfernen'; ExitCodesLabel = 'Exit-Codes:'; QuietUninstall = 'Entfernen'
        PollRow = "| Abfrageintervall | Wie oft nach Spielstarts gesehen wird | $($defaults.Poll) ms |"; GraceFact = "Nachlaufzeit ($($defaults.Grace) s)"
        LogFact = "höchstens $maxMb MB je Datei, $retainDays Tage"; RunValueFact = "Wert ``$runValue``"
        BufferRow = 'Neustart-Puffer'; BufferWord = 'Neustart-Puffer'; BufferText = $defaults.BufferTextDe
        BufferSections = @('So funktioniert es', 'Bekannte Grenzen', 'Fehlersuche')
        UiSections = 'Einrichtung|Bedienung|Entfernen'; SettingsHeading = 'Einstellungen'; SettingsHeader = 'Einstellung'; AdvancedSuffix = ' (Erweitert)'
    },
    [pscustomobject]@{
        Name = 'README.md'; Language = 'en'; Ui = $english; Help = (Read-CliHelp 'en')
        Sections = @('Requirements', 'Installation', 'Usage', 'How it works', 'File locations', 'Command line', 'Known limitations', 'Troubleshooting', 'Uninstalling')
        CommandLine = 'Command line'; SetupHeading = 'Setup file and uninstall'; ExitCodesLabel = 'Exit codes:'; QuietUninstall = 'Uninstall'
        PollRow = "| Polling interval | How often HDRpilot checks for game launches | $($defaults.Poll) ms |"; GraceFact = "grace period ($($defaults.Grace) s)"
        LogFact = "at most $maxMb MB per file, $retainDays days"; RunValueFact = "value ``$runValue``"
        BufferRow = 'Restart buffer'; BufferWord = 'restart buffer'; BufferText = $defaults.BufferTextEn
        BufferSections = @('How it works', 'Known limitations', 'Troubleshooting')
        UiSections = 'Installation|Usage|Uninstalling'; SettingsHeading = 'Settings'; SettingsHeader = 'Setting'; AdvancedSuffix = ' (Advanced)'
    }
)

Check ($appSwitches.Count -ge 9) ("App-Schalter aus AppArguments.cs: {0}" -f ($appSwitches -join ' '))
Check ($defaults.Poll -gt 0 -and $defaults.Grace -gt 0 -and $defaults.Launch -gt 0 -and $bufferMatch.Success) ("Standardwerte: Intervall {0} ms, Nachlauf {1} s, Startfrist {2} s, Neustart-Puffer {3}" -f $defaults.Poll, $defaults.Grace, $defaults.Launch, $defaults.Buffer)
foreach ($p in $profiles) {
    Check ($p.Help.Exit -eq 0 -and $p.Help.Text -match 'hdrpilot hdr status') "Hilfe der Kommandozeile ($($p.Language)) gelesen (exit=$($p.Help.Exit))"
    Check ($p.Help.Usage.Count -ge 8 -and $p.Help.ExitPairs.Count -eq 7) ("Hilfe ({0}): {1} Aufrufzeilen, {2} Exit-Codes" -f $p.Language, $p.Help.Usage.Count, $p.Help.ExitPairs.Count)
    Check ($p.Ui.Labels.Count -gt 200 -and $p.Ui.Missing.Count -eq 0) ("Oberflächen-Texte aus resx ({0}): {1}{2}" -f $p.Language, $p.Ui.Labels.Count, $(if ($p.Ui.Missing) { ', fehlt: ' + ($p.Ui.Missing -join ', ') } else { ' (inkl. SetupStrings)' }))
}
Check (($profiles[0].Help.Usage -join '|') -ne ($profiles[1].Help.Usage -join '|')) 'Hilfe auf Deutsch und Englisch unterscheidet sich (beide Sprachen wirklich gelesen)'
Check ($setupSwitches.Count -ge 8 -and $setupExitCodes.Count -ge 7) ("Setup-Schalter aus SetupArguments.cs: {0}; Exit-Codes {1}" -f ($setupSwitches -join ' '), ($setupExitCodes -join ' '))
Check ($directUninstallCodes.Count -eq 3) ("Stilles Entfernen, Exit-Codes direkt vom ersten Aufruf: {0}" -f ($directUninstallCodes -join ' '))
Check ($programDir -eq '%LOCALAPPDATA%\Programs\HDRpilot' -and $uninstallKey -like 'HKCU\Software\*\Uninstall\HDRpilot' -and $setupLog -like '%TEMP%\*.log') ("Setup-Orte: {0}, {1}, {2}, {3}" -f $setupFile, $programDir, $uninstallKey, $setupLog)

# --- Prüfung einer README in einer Sprachfassung, liefert die Liste der Probleme (jedes einmal) ---
function Test-Readme([string]$text, $p) {
    $problems = New-Object System.Collections.ArrayList
    $flat = Normalize $text

    foreach ($section in $p.Sections) {
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

    # Setup und Entfernen: eigener Unterabschnitt unter der Befehlszeile, alle Schalter (in Code-Abschnitten, auch
    # hinter dem Programmnamen) dokumentiert, keine erfundenen, Exit-Codes wie in SetupExitCodes
    $commandLine = [regex]::Match($text, '(?ms)^## ' + [regex]::Escape($p.CommandLine) + '\s*$(.*?)(?=^## |\z)').Groups[1].Value
    $setupSection = [regex]::Match($commandLine, '(?ms)^### ' + [regex]::Escape($p.SetupHeading) + '\s*$(.*?)(?=^### |\z)')
    if (-not $setupSection.Success) { [void]$problems.Add("Abschnitt fehlt: ### $($p.SetupHeading) (unter ## $($p.CommandLine))") }
    $setupDocumented = @([regex]::Matches($setupSection.Groups[1].Value, '`([^`]+)`') | ForEach-Object { [regex]::Matches($_.Groups[1].Value, '(?<![\w-])(--[a-z][a-z-]*)') } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    foreach ($s in $setupSwitches) { if ($s -notin $setupDocumented) { [void]$problems.Add("Setup-Schalter nicht dokumentiert: $s") } }
    foreach ($s in $setupDocumented) { if ($s -notin $setupSwitches) { [void]$problems.Add("Setup-Schalter gibt es nicht: $s") } }
    $setupExitLine = Normalize ([regex]::Match($setupSection.Groups[1].Value, '(?s)' + [regex]::Escape($p.ExitCodesLabel) + '(.*?)(\r?\n\r?\n|\z)').Groups[1].Value)
    $readmeExitCodes = @([regex]::Matches($setupExitLine, '(?:^|, )(\d+) ') | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object)
    if (($readmeExitCodes -join ',') -ne ($setupExitCodes -join ',')) { [void]$problems.Add("Setup-Exit-Codes weichen ab: README $($readmeExitCodes -join ',') statt $($setupExitCodes -join ',')") }
    # Zeile zu --quiet beim Entfernen: nennt jeden Code, der ohne ERGEBNIS-Zeile direkt zurückkommt, als "<Code> (…)"
    $quietUninstallRow = [regex]::Match($setupSection.Groups[1].Value, '(?m)^\| `--quiet` \| ' + [regex]::Escape($p.QuietUninstall) + '[^\r\n]*').Value
    foreach ($c in $directUninstallCodes) {
        if ($quietUninstallRow -notmatch "(?<![\w-])$c \(") { [void]$problems.Add("Stilles Entfernen: Exit-Code $c kommt direkt vom ersten Aufruf, fehlt in der Zeile zu --quiet") }
    }

    # Kommandozeile: Aufrufzeilen identisch mit der Hilfe derselben Sprache, Exit-Codes vollständig
    $cliBlock = [regex]::Match($text, '(?s)### cli\\hdrpilot\.exe.*?```\r?\n(.*?)```').Groups[1].Value
    $readmeUsage = @($cliBlock -split "`r?`n" | Where-Object { $_ -cmatch '^hdrpilot ' } | ForEach-Object { Normalize $_ })
    foreach ($u in $p.Help.Usage) { if ($u -notin $readmeUsage) { [void]$problems.Add("CLI-Aufruf fehlt oder weicht ab: $u") } }
    foreach ($u in $readmeUsage) { if ($u -notin $p.Help.Usage) { [void]$problems.Add("CLI-Aufruf nicht in der Hilfe: $u") } }
    foreach ($pair in $p.Help.ExitPairs) { if ($flat -notlike "*$pair*") { [void]$problems.Add("Exit-Code fehlt: $pair") } }

    # Standardwerte und Ablageorte
    foreach ($fact in @($p.PollRow, $p.GraceFact, "| $($defaults.Launch) s |", $p.LogFact, "HKCU\$runKey", $p.RunValueFact,
                        "``$setupFile``", "``$programDir", "``$uninstallKey``", "``$setupLog``")) {
        if ($text.IndexOf($fact, [StringComparison]::Ordinal) -lt 0) { [void]$problems.Add("Fakt fehlt oder weicht ab: $fact") }
    }
    foreach ($name in $fileNames) {
        if ($text -notmatch [regex]::Escape($name)) { [void]$problems.Add("Ablageort fehlt: $name") }
    }
    if ($text -notmatch "(?m)^\| $([regex]::Escape($p.BufferRow)) \|[^\r\n]*\| $([regex]::Escape($defaults.Buffer)) \|\r?$") { [void]$problems.Add("Fakt fehlt oder weicht ab: $($p.BufferRow) $($defaults.Buffer)") }
    # Jeder Absatz und Listenpunkt in der Funktionsweise, den bekannten Grenzen und der Fehlersuche, der den
    # Neustart-Puffer nennt, nennt auch seinen Standard: Mit 3:00 fing er etwa den Neustart von Dead Island 2 (96 s) ab,
    # mit 1:00 nicht.
    foreach ($section in $p.BufferSections) {
        $body = [regex]::Match($text, '(?ms)^## ' + [regex]::Escape($section) + '\s*$(.*?)(?=^## |\z)').Groups[1].Value
        foreach ($block in [regex]::Split($body, '(?:\r?\n){2,}|\r?\n(?=- )')) {
            $b = Normalize $block
            if ($b.IndexOf($p.BufferWord, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $b.IndexOf($p.BufferText, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                $title = [regex]::Match($b, '\*\*([^*]+?):?\*\*').Groups[1].Value
                [void]$problems.Add("Neustart-Puffer ohne $($p.BufferText): $section, $(if ($title) { $title } else { $b.Substring(0, [Math]::Min(40, $b.Length)) })")
            }
        }
    }

    # Fett gesetzte Bezeichnungen in Einrichtung, Bedienung und Entfernen (dort ist Fettdruck immer ein Oberflächen-Text,
    # außer Absatztiteln mit Doppelpunkt wie „Sprache:“) und die erste Spalte der Einstellungstabelle müssen
    # Oberflächen-Texte derselben Sprache sein.
    $uiSections = @([regex]::Matches($text, '(?ms)^## (?:' + $p.UiSections + ')\s*$(.*?)(?=^## |\z)') | ForEach-Object { $_.Groups[1].Value }) -join "`n"
    $labels = @([regex]::Matches($uiSections, '\*\*([^*]+)\*\*') | ForEach-Object { $_.Groups[1].Value } | Where-Object { -not $_.EndsWith(':') })
    $settingsTable = [regex]::Match($text, '(?s)### ' + [regex]::Escape($p.SettingsHeading) + '(.*?)\r?\n\r?\n[^|]').Groups[1].Value
    $labels += @([regex]::Matches($settingsTable, '(?m)^\| ([^|]+?) \|') | ForEach-Object { $_.Groups[1].Value -replace ([regex]::Escape($p.AdvancedSuffix) + '$'), '' } | Where-Object { $_ -notin $p.SettingsHeader, '---' })
    foreach ($label in $labels) {
        $l = Normalize $label
        $parts = @($l -split ' / ')
        foreach ($part in $parts) {
            if (-not $p.Ui.Labels.Contains($part)) { [void]$problems.Add("Bezeichnung nicht in der Oberfläche: $part") }
        }
    }
    return ,@($problems | Select-Object -Unique)
}

# Gegenproben je Sprache: eingebaute Fehler müssen gefunden werden (Ersetzungen und erwartete Meldungen)
function Get-Broken([string]$readme, $p) {
    if ($p.Language -eq 'de') {
        $broken = $readme.Replace('**Neu einlesen**', '**Neu laden**').Replace('| `--rescan` | Spiele neu einlesen |', '| `--reload` | Spiele neu einlesen |').Replace($p.GraceFact, 'Nachlaufzeit (5 s)').Replace('## Fehlersuche', '## Hilfe')
        $broken = $broken.Replace('**Verknüpfung auf dem Desktop**', '**Desktop-Symbol**')
        $oldQuietRow = '| `--quiet` | Entfernen ohne Fenster: kehrt sofort zurück, das Ergebnis steht danach als `ERGEBNIS: <Exit-Code>` im Protokoll |'
        $labels = @('Neu laden', 'Desktop-Symbol')
    } else {
        $broken = $readme.Replace('**Rescan**', '**Reload**').Replace('| `--rescan` | Rescan games |', '| `--reload` | Rescan games |').Replace($p.GraceFact, 'grace period (5 s)').Replace('## Troubleshooting', '## Help')
        $broken = $broken.Replace('**Desktop shortcut**', '**Desktop icon**')
        $oldQuietRow = '| `--quiet` | Uninstall without a window: returns immediately, the result then appears in the log as `ERGEBNIS: <exit code>` |'
        $labels = @('Reload', 'Desktop icon')
    }
    $broken = $broken.Replace('hdrpilot games list [--json]', 'hdrpilot games list')
    # Setup (E83): Bezeichnung nur aus SetupStrings, Schalter des Setups (jede Nennung, auch im Text zu --quiet), Protokollort
    $broken = $broken.Replace('`--no-launch`', '`--no-start`').Replace("``$setupLog``", '`%TEMP%\HDRpilot.log`')
    # Stilles Entfernen (E84): die Zeile in der alten Fassung, die nur die ERGEBNIS-Zeile versprach
    $broken = [regex]::Replace($broken, '(?m)^\| `--quiet` \| ' + [regex]::Escape($p.QuietUninstall) + '[^\r\n]*', $oldQuietRow.Replace('$', '$$'))
    # Neustart-Puffer (E85): alter Standard in der Tabelle
    $broken = $broken.Replace("| $($defaults.Buffer) |", '| 3:00 |')
    $expected = @("Bezeichnung nicht in der Oberfläche: $($labels[0])", 'App-Schalter nicht dokumentiert: --rescan', 'App-Schalter gibt es nicht: --reload',
                  "Fakt fehlt oder weicht ab: $($p.GraceFact)", "Abschnitt fehlt: $($p.Sections[7])", 'CLI-Aufruf fehlt oder weicht ab: hdrpilot games list [--json]', 'CLI-Aufruf nicht in der Hilfe: hdrpilot games list',
                  "Bezeichnung nicht in der Oberfläche: $($labels[1])", 'Setup-Schalter nicht dokumentiert: --no-launch', 'Setup-Schalter gibt es nicht: --no-start', "Fakt fehlt oder weicht ab: ``$setupLog``",
                  "Fakt fehlt oder weicht ab: $($p.BufferRow) $($defaults.Buffer)")
    $expected += @($directUninstallCodes | ForEach-Object { "Stilles Entfernen: Exit-Code $_ kommt direkt vom ersten Aufruf, fehlt in der Zeile zu --quiet" })
    return [pscustomobject]@{ Text = $broken; Expected = $expected }
}

# Gegenprobe Neustart-Puffer im Fließtext (E85, eigene README, weil die erste die Fehlersuche umbenennt): der
# Listenpunkt zu Dead Island 2 wieder ohne Standard (Stand vor der Korrektur), in den beiden anderen Absätzen der alte
# Standard
function Get-BrokenBuffer([string]$readme, $p) {
    if ($p.Language -eq 'de') {
        $broken = [regex]::Replace($readme, '(?s)(Der Neustart-Puffer hilft beim zweiten Versuch)[^;]*;[^;]*;', '$1;')
        $broken = $broken.Replace($p.BufferText, 'Standard 3 Minuten')
        $removed = 'beim zweiten Versuch; sicherer'; $old = 'Standard 3 Minuten;'
        $titles = @('Neustart-Puffer', 'Umschalten während des Spielstarts', 'HDR bleibt nach dem Spiel an')
    } else {
        $broken = [regex]::Replace($readme, '(?s)(The restart buffer helps on the second attempt)[^;]*;[^;]*;', '$1;')
        $broken = $broken.Replace($p.BufferText, 'default 3 minutes')
        $removed = 'on the second attempt; safer'; $old = 'default 3 minutes; if you need'
        $titles = @('Restart buffer', 'Switching during game launch', 'HDR stays on after the game')
    }
    $expected = @(for ($i = 0; $i -lt 3; $i++) { "Neustart-Puffer ohne $($p.BufferText): $($p.BufferSections[$i]), $($titles[$i])" })
    return [pscustomobject]@{ Text = $broken; Expected = $expected; Valid = ($broken -match [regex]::Escape($removed) -and $broken -notmatch [regex]::Escape($old)) }
}

foreach ($p in $profiles) {
    Note "--- $($p.Name) ($($p.Language))"
    $readmePath = Join-Path $root $p.Name
    Check (Test-Path $readmePath) "$($p.Name) vorhanden"
    $readme = [IO.File]::ReadAllText($readmePath, [Text.Encoding]::UTF8)
    $problems = Test-Readme $readme $p
    foreach ($x in $problems) { Note "  - $x" }
    Check ($problems.Count -eq 0) ("{0} stimmt mit dem Quelltext überein ({1} Probleme)" -f $p.Name, $problems.Count)

    $b = Get-Broken $readme $p
    $found = Test-Readme $b.Text $p
    $missed = @($b.Expected | Where-Object { $_ -notin $found })
    Check ($missed.Count -eq 0 -and $found.Count -eq $b.Expected.Count) ("{0} Gegenprobe: {1} von {2} eingebauten Fehlern erkannt, {3} Meldungen insgesamt{4}" -f $p.Name, ($b.Expected.Count - $missed.Count), $b.Expected.Count, $found.Count, $(if ($missed) { '; übersehen: ' + ($missed -join ' | ') } else { '' }))

    $bb = Get-BrokenBuffer $readme $p
    $foundBuffer = Test-Readme $bb.Text $p
    $missedBuffer = @($bb.Expected | Where-Object { $_ -notin $foundBuffer })
    Check ($missedBuffer.Count -eq 0 -and $foundBuffer.Count -eq $bb.Expected.Count -and $bb.Valid) ("{0} Gegenprobe Neustart-Puffer im Fließtext: {1} von {2} erkannt, {3} Meldungen insgesamt{4}" -f $p.Name, ($bb.Expected.Count - $missedBuffer.Count), $bb.Expected.Count, $foundBuffer.Count, $(if ($missedBuffer) { '; übersehen: ' + ($missedBuffer -join ' | ') } else { '' }))
}

# Gegenprobe über Kreuz: die deutsche README gegen die englische Fassung muss scheitern (die Prüfung hängt an der Sprache)
$crossProblems = Test-Readme ([IO.File]::ReadAllText((Join-Path $root 'README.de.md'), [Text.Encoding]::UTF8)) $profiles[1]
Check ($crossProblems.Count -gt 0) ("Gegenprobe über Kreuz: README.de.md gegen die englischen Texte liefert {0} Probleme" -f $crossProblems.Count)

if ($ok) { Write-Output 'PHASE5_README_OK'; exit 0 }
Write-Output 'PHASE5_README_FAILED'
exit 1
