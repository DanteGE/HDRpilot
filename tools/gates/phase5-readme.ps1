# Gate G5.2: README mit Einrichtung, Funktionsweise, bekannten Grenzen und Fehlersuche, abgeglichen mit dem Quelltext.
# Prüft Abschnitte, Befehlszeilen-Schalter der App, die Hilfe der Kommandozeile, Standardwerte, Ablageorte und alle fett
# gesetzten Oberflächen-Bezeichnungen gegen die resx-Dateien (inkl. SetupStrings.resx), dazu Schalter, Exit-Codes und
# Orte von Setup und Entfernen (E83), die Exit-Codes, die stilles Entfernen ohne ERGEBNIS-Zeile direkt liefert (E84),
# und den Standard des Neustart-Puffers in der Einstellungstabelle und in jedem Absatz, der ihn als Ursache oder Abhilfe
# nennt (E85). Gegenprobe: eine absichtlich verfälschte README muss genau die eingebauten Fehler liefern.
# Seit 2026-10-27 (E95) zwei Fassungen: README.de.md gegen die neutralen (deutschen) resx und die deutsche Hilfe,
# README.md gegen die .en.resx und die englische Hilfe, jede mit eigenen Abschnittsnamen und eigenen Gegenproben.
# Seit 2026-11-04 (E96) steht der vollständige Text in den Handbüchern: Die Prüfung oben läuft gegen docs/handbuch.md
# (deutsch) und docs/manual.md (englisch), dazu deren Verweise und Anker. README.md und README.de.md sind kurz und
# bekommen eine leichte Prüfung: Pflichtabschnitte, höchstens 200 Zeilen, jedes relative Verweis- und Bildziel
# vorhanden (Bilder als PNG, Anker in den Handbüchern), Pflichtverweise auf Handbuch, andere Sprache, Lizenz und Releases
# samt Online-Verweis auf das Handbuch, fett gesetzte Bezeichnungen wie in den resx derselben Sprache, keine deutschen
# Überschriften in README.md; jede mit eigener Gegenprobe.
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

# Sprachfassungen der Handbücher (E96, vorher der READMEs): Datei, Texte, Hilfe, die Wörter, an denen die Prüfung
# Abschnitte und Fakten erkennt, und die Pflichtverweise (Rückweg zur README und zum Handbuch der anderen Sprache)
$german = Read-UiLabels ''
$english = Read-UiLabels '.en'
$profiles = @(
    [pscustomobject]@{
        Name = 'docs/handbuch.md'; Language = 'de'; Ui = $german; Help = (Read-CliHelp 'de'); Required = @('../README.de.md', 'manual.md')
        Sections = @('Voraussetzungen', 'Einrichtung', 'Bedienung', 'So funktioniert es', 'Speicherorte', 'Befehlszeile', 'Bekannte Grenzen', 'Fehlersuche', 'Entfernen')
        CommandLine = 'Befehlszeile'; SetupHeading = 'Setup-Datei und Entfernen'; ExitCodesLabel = 'Exit-Codes:'; QuietUninstall = 'Entfernen'
        PollRow = "| Abfrageintervall | Wie oft nach Spielstarts gesehen wird | $($defaults.Poll) ms |"; GraceFact = "Nachlaufzeit ($($defaults.Grace) s)"
        LogFact = "höchstens $maxMb MB je Datei, $retainDays Tage"; RunValueFact = "Wert ``$runValue``"
        BufferRow = 'Neustart-Puffer'; BufferWord = 'Neustart-Puffer'; BufferText = $defaults.BufferTextDe
        BufferSections = @('So funktioniert es', 'Bekannte Grenzen', 'Fehlersuche')
        UiSections = 'Einrichtung|Bedienung|Entfernen'; SettingsHeading = 'Einstellungen'; SettingsHeader = 'Einstellung'; AdvancedSuffix = ' (Erweitert)'
    },
    [pscustomobject]@{
        Name = 'docs/manual.md'; Language = 'en'; Ui = $english; Help = (Read-CliHelp 'en'); Required = @('../README.md', 'handbuch.md')
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

# --- Prüfung eines Handbuchs (bis E96 der README) in einer Sprachfassung, liefert die Liste der Probleme (jedes einmal) ---
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

# --- Verweise (E96): relative Ziele samt Bildern und Ankern, Verweise auf Bildziele, Online-Verweise auf dieses
# Repository. Code-Blöcke, Code-Spannen und HTML-Kommentare enthalten keine Verweise. ---
$repoUrl = 'https://github.com/DanteGE/HDRpilot'
$rootPrefix = $root.TrimEnd('\') + '\'
function Remove-Fences([string]$text) {
    $t = [regex]::Replace($text, '(?ms)^[ \t]*```.*?^[ \t]*```[^\r\n]*', '')
    return [regex]::Replace($t, '(?s)<!--.*?-->', '')
}
function Remove-Code([string]$text) { [regex]::Replace((Remove-Fences $text), '`[^`\r\n]*`', '') }
# Überschriften ohne Code-Blöcke; Code-Spannen behalten ihren Text (wie bei GitHub)
function Get-Headings([string]$text) { @([regex]::Matches(((Remove-Fences $text) -replace '`', ''), '(?m)^#{1,6}[ \t]+(.+?)[ \t]*\r?$') | ForEach-Object { $_.Groups[1].Value }) }
# Anker wie GitHub: klein, ohne Satzzeichen (außer - und _), Leerzeichen zu -, Wiederholungen mit -1, -2 …
function Get-Anchors([string]$text) {
    $set = New-Object System.Collections.Generic.HashSet[string]
    $seen = @{}
    foreach ($m in [regex]::Matches(((Remove-Fences $text) -replace '`', ''), '(?m)^#{1,6}[ \t]+(.+?)[ \t]*#*[ \t]*\r?$')) {
        $slug = ([regex]::Replace($m.Groups[1].Value.Trim().ToLowerInvariant(), '[^\p{L}\p{Nd}\s_-]', '')) -replace '\s', '-'
        if ($seen.ContainsKey($slug)) { $seen[$slug]++; $slug = "$slug-$($seen[$slug])" } else { $seen[$slug] = 0 }
        [void]$set.Add($slug)
    }
    return ,$set
}
function Get-Links([string]$text) {
    $t = Remove-Code $text
    $targets = New-Object System.Collections.ArrayList
    $images = New-Object System.Collections.ArrayList
    $undefined = New-Object System.Collections.ArrayList
    foreach ($m in [regex]::Matches($t, '(!?)\[[^\]\r\n]*\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)')) {
        [void]$targets.Add($m.Groups[2].Value)
        if ($m.Groups[1].Value) { [void]$images.Add($m.Groups[2].Value) }
    }
    $definitions = @{}
    foreach ($m in [regex]::Matches($t, '(?m)^ {0,3}\[([^\]\r\n]+)\]:[ \t]*<?([^\s>]+)>?')) {
        $definitions[$m.Groups[1].Value.ToLowerInvariant()] = $m.Groups[2].Value
        [void]$targets.Add($m.Groups[2].Value)
    }
    foreach ($m in [regex]::Matches($t, '(!?)\[[^\]\r\n]*\]\[([^\]\r\n]+)\]')) {
        $id = $m.Groups[2].Value.ToLowerInvariant()
        if (-not $definitions.ContainsKey($id)) { [void]$undefined.Add($m.Groups[2].Value) }
        elseif ($m.Groups[1].Value) { [void]$images.Add($definitions[$id]) }
    }
    foreach ($m in [regex]::Matches($t, '<(https?://[^>\s]+)>|(?:src|href)="([^"]+)"')) {
        [void]$targets.Add($(if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }))
    }
    return [pscustomobject]@{ Targets = @($targets | Select-Object -Unique); Images = @($images); Undefined = @($undefined | Select-Object -Unique) }
}
# Ziel eines relativen Verweises als vollständiger Pfad und als Anzeige relativ zum Repository (mit /)
function Resolve-Target([string]$fromFile, [string]$path) {
    $full = [IO.Path]::GetFullPath((Join-Path (Split-Path (Join-Path $root $fromFile) -Parent) ([uri]::UnescapeDataString($path) -replace '/', '\')))
    $inside = $full.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
    return [pscustomobject]@{ Full = $full; Inside = $inside; Shown = $(if ($inside) { $full.Substring($rootPrefix.Length) -replace '\\', '/' } else { $path }) }
}
function Test-Links([string]$text, [string]$file) {
    $problems = New-Object System.Collections.ArrayList
    $links = Get-Links $text
    foreach ($id in $links.Undefined) { [void]$problems.Add("Bildverweis ohne Ziel: $id") }
    foreach ($target in $links.Targets) {
        if ($target -match '^(?i)https?://') {
            $online = [regex]::Match($target, '^' + [regex]::Escape($repoUrl) + '/(?:blob|tree)/main/([^#?]+)')
            if ($online.Success -and -not (Test-Path -LiteralPath (Join-Path $root ([uri]::UnescapeDataString($online.Groups[1].Value) -replace '/', '\')))) {
                [void]$problems.Add("Online-Verweis auf eine fehlende Datei: $($online.Groups[1].Value)")
            }
            continue
        }
        if ($target -match '^(?i)[a-z][a-z0-9+.-]*:') { continue } # mailto: und Ähnliches
        $path, $anchor = $target -split '#', 2
        if ($path) {
            $resolved = Resolve-Target $file $path
            if (-not $resolved.Inside -or -not (Test-Path -LiteralPath $resolved.Full)) { [void]$problems.Add("Verweisziel fehlt: $($resolved.Shown)"); continue }
            $anchorText = if ($anchor -and $resolved.Full -like '*.md') { [IO.File]::ReadAllText($resolved.Full, [Text.Encoding]::UTF8) } else { $null }
            $shown = $resolved.Shown
        } else {
            $anchorText = $text; $shown = $file
        }
        if ($null -ne $anchorText -and $anchor -and -not (Get-Anchors $anchorText).Contains($anchor.ToLowerInvariant())) { [void]$problems.Add("Anker fehlt: $shown#$anchor") }
    }
    foreach ($image in $links.Images) {
        if ($image -match '^(?i)[a-z][a-z0-9+.-]*:' -or $image -notmatch '(?i)\.png(#.*)?$') { continue }
        $resolved = Resolve-Target $file (($image -split '#', 2)[0])
        if (-not ($resolved.Inside -and (Test-Path -LiteralPath $resolved.Full -PathType Leaf))) { continue } # schon als fehlendes Ziel gemeldet
        $head = New-Object byte[] 8
        $stream = [IO.File]::OpenRead($resolved.Full)
        try { $read = $stream.Read($head, 0, 8) } finally { $stream.Dispose() }
        if ($read -ne 8 -or (($head | ForEach-Object { $_ }) -join ',') -ne '137,80,78,71,13,10,26,10') { [void]$problems.Add("Bild ist kein PNG: $($resolved.Shown)") }
    }
    return ,@($problems | Select-Object -Unique)
}
# Pflichtverweise: ein Verweis (ohne Anker) mit genau diesem Ziel muss vorkommen
function Test-Required([string]$text, [string[]]$required) {
    $present = @((Get-Links $text).Targets | ForEach-Object { ($_ -split '#', 2)[0] })
    return ,@($required | Where-Object { $_ -notin $present } | ForEach-Object { "Pflichtverweis fehlt: $_" })
}
# Listen der Prüffunktionen (je ein Feld als ein Objekt geliefert) zu einer Liste zusammenfügen
function Join-Problems([object[]]$first, [object[]]$second) { return ,@(@($first) + @($second) | Where-Object { $_ }) }

foreach ($p in $profiles) {
    Note "--- $($p.Name) ($($p.Language), Handbuch)"
    $readmePath = Join-Path $root $p.Name
    Check (Test-Path $readmePath) "$($p.Name) vorhanden"
    $readme = [IO.File]::ReadAllText($readmePath, [Text.Encoding]::UTF8)
    $problems = Test-Readme $readme $p
    foreach ($x in $problems) { Note "  - $x" }
    Check ($problems.Count -eq 0) ("{0} stimmt mit dem Quelltext überein ({1} Probleme)" -f $p.Name, $problems.Count)

    # Verweise nach dem Umzug nach docs/ (E96): LICENSE, README, anderes Handbuch, Anker
    $linkProblems = Join-Problems (Test-Links $readme $p.Name) (Test-Required $readme $p.Required)
    foreach ($x in $linkProblems) { Note "  - $x" }
    Check ($linkProblems.Count -eq 0) ("{0}: Verweise, Anker und Pflichtverweise ({1}) stimmen ({2} Probleme)" -f $p.Name, ($p.Required -join ', '), $linkProblems.Count)

    $b = Get-Broken $readme $p
    $found = Test-Readme $b.Text $p
    $missed = @($b.Expected | Where-Object { $_ -notin $found })
    Check ($missed.Count -eq 0 -and $found.Count -eq $b.Expected.Count) ("{0} Gegenprobe: {1} von {2} eingebauten Fehlern erkannt, {3} Meldungen insgesamt{4}" -f $p.Name, ($b.Expected.Count - $missed.Count), $b.Expected.Count, $found.Count, $(if ($missed) { '; übersehen: ' + ($missed -join ' | ') } else { '' }))

    $bb = Get-BrokenBuffer $readme $p
    $foundBuffer = Test-Readme $bb.Text $p
    $missedBuffer = @($bb.Expected | Where-Object { $_ -notin $foundBuffer })
    Check ($missedBuffer.Count -eq 0 -and $foundBuffer.Count -eq $bb.Expected.Count -and $bb.Valid) ("{0} Gegenprobe Neustart-Puffer im Fließtext: {1} von {2} erkannt, {3} Meldungen insgesamt{4}" -f $p.Name, ($bb.Expected.Count - $missedBuffer.Count), $bb.Expected.Count, $foundBuffer.Count, $(if ($missedBuffer) { '; übersehen: ' + ($missedBuffer -join ' | ') } else { '' }))

    # Gegenprobe Verweise: LICENSE und README wie vor dem Umzug (ohne ../), Anker im anderen Handbuch, den es nicht gibt
    $brokenLinks = $readme.Replace('](../LICENSE)', '](LICENSE)').Replace("]($($p.Required[0]))", "]($($p.Required[0].Substring(3)))").Replace("]($($p.Required[1]))", "]($($p.Required[1])#gibt-es-nicht)")
    $expectedLinks = @('Verweisziel fehlt: docs/LICENSE', "Verweisziel fehlt: docs/$($p.Required[0].Substring(3))", "Pflichtverweis fehlt: $($p.Required[0])", "Anker fehlt: docs/$($p.Required[1])#gibt-es-nicht")
    $foundLinks = Join-Problems (Test-Links $brokenLinks $p.Name) (Test-Required $brokenLinks $p.Required)
    $missedLinks = @($expectedLinks | Where-Object { $_ -notin $foundLinks })
    Check ($missedLinks.Count -eq 0 -and $foundLinks.Count -eq $expectedLinks.Count) ("{0} Gegenprobe Verweise: {1} von {2} erkannt, {3} Meldungen insgesamt{4}" -f $p.Name, ($expectedLinks.Count - $missedLinks.Count), $expectedLinks.Count, $foundLinks.Count, $(if ($missedLinks) { '; übersehen: ' + ($missedLinks -join ' | ') } else { '' }))
}

# Gegenprobe über Kreuz: das deutsche Handbuch gegen die englische Fassung muss scheitern (die Prüfung hängt an der Sprache)
$crossProblems = Test-Readme ([IO.File]::ReadAllText((Join-Path $root 'docs/handbuch.md'), [Text.Encoding]::UTF8)) $profiles[1]
Check ($crossProblems.Count -gt 0) ("Gegenprobe über Kreuz: docs/handbuch.md gegen die englischen Texte liefert {0} Probleme" -f $crossProblems.Count)

# --- Kurze READMEs (E96): Startseite auf GitHub und installierte Kopie ---
$maxShortLines = 200
# Wörter, an denen eine deutsche Überschrift zu erkennen ist (dazu Umlaute und ß); keine, die auch englisch vorkommen
# („was“, „die“, „so“, „mit“ wie MIT-Lizenz)
$germanHeadingWords = 'und|oder|ohne|für|der|das|dem|den|des|ein|eine|von|zum|zur|auf|aus|macht|funktioniert|funktionen|bilder|bildschirmfotos|herunterladen|installieren|schnellstart|voraussetzungen|datenschutz|netz|dokumentation|lizenz|lizenzen|fremdbestandteilen|einrichtung|bedienung|speicherorte|befehlszeile|bekannte|grenzen|fehlersuche|entfernen|aktualisieren|spielebibliothek|infobereich|einstellungen|quelltext|bauen|handbuch|liste'
function Get-LineCount([string]$text) { @($text.TrimEnd("`r", "`n") -split '\r?\n').Count }
function Test-GermanHeading([string]$heading) { $heading -match '[äöüÄÖÜß]' -or $heading -match "(?i)\b($germanHeadingWords)\b" }

$shortProfiles = @(
    [pscustomobject]@{
        Name = 'README.md'; Language = 'en'; Ui = $english; EnglishHeadings = $true
        Sections = @('What it does', 'Features', 'Screenshots', 'Download and install', 'Quick start', 'Requirements', 'Privacy and network', 'Documentation', 'License')
        Required = @('docs/manual.md', 'README.de.md', "$repoUrl/blob/main/docs/manual.md", 'LICENSE', "$repoUrl/releases")
    },
    [pscustomobject]@{
        Name = 'README.de.md'; Language = 'de'; Ui = $german; EnglishHeadings = $false
        Sections = @('Was es macht', 'Funktionen', 'Bildschirmfotos', 'Herunterladen und installieren', 'Schnellstart', 'Voraussetzungen', 'Datenschutz und Netz', 'Dokumentation', 'Lizenz')
        Required = @('docs/handbuch.md', 'README.md', "$repoUrl/blob/main/docs/handbuch.md", 'LICENSE', "$repoUrl/releases")
    }
)

function Test-ShortReadme([string]$text, $sp) {
    $problems = New-Object System.Collections.ArrayList
    if ($text -cnotmatch '^# HDRpilot\r?\n') { [void]$problems.Add('Titel fehlt: # HDRpilot') }
    foreach ($section in $sp.Sections) {
        if ($text -notmatch "(?m)^## $([regex]::Escape($section))\s*$") { [void]$problems.Add("Abschnitt fehlt: $section") }
    }
    foreach ($word in 'TODO', 'FIXME', 'TBD', 'XXX') {
        if ($text -cmatch "\b$word\b") { [void]$problems.Add("Platzhalter: $word") }
    }
    $lines = Get-LineCount $text
    if ($lines -gt $maxShortLines) { [void]$problems.Add("Zu lang: $lines Zeilen (höchstens $maxShortLines)") }
    $images = @((Get-Links $text).Images)
    if ($images.Count -lt 3) { [void]$problems.Add("Zu wenige Bildschirmfotos: $($images.Count)") }
    foreach ($x in (Test-Links $text $sp.Name)) { [void]$problems.Add($x) }
    foreach ($x in (Test-Required $text $sp.Required)) { [void]$problems.Add($x) }
    # Name der Setup-Datei, falls genannt, wie die Produktversion
    foreach ($m in [regex]::Matches($text, 'HDRpilot-Setup-\d+\.\d+\.\d+\.exe')) {
        if ($m.Value -ne $setupFile) { [void]$problems.Add("Setup-Datei weicht ab: $($m.Value) statt $setupFile") }
    }
    # Fett gesetzt ist nur, was die Oberfläche derselben Sprache so zeigt (Absatztitel mit Doppelpunkt ausgenommen)
    foreach ($m in [regex]::Matches((Remove-Code $text), '\*\*([^*]+)\*\*')) {
        $label = Normalize $m.Groups[1].Value
        if ($label.EndsWith(':')) { continue }
        foreach ($part in @($label -split ' / ')) {
            if (-not $sp.Ui.Labels.Contains($part)) { [void]$problems.Add("Bezeichnung nicht in der Oberfläche: $part") }
        }
    }
    if ($sp.EnglishHeadings) {
        foreach ($h in (Get-Headings $text)) { if (Test-GermanHeading $h) { [void]$problems.Add("Deutsch in einer Überschrift: $h") } }
    }
    return ,@($problems | Select-Object -Unique)
}

# Gegenprobe je Sprache: Bezeichnung, Abschnitt (in README.md deutsch benannt), fehlendes Bild, Bildverweis ohne Ziel,
# Verweis auf die andere Sprache, Online-Verweis auf das Handbuch und ein Anker, den es im Handbuch nicht gibt
function Get-BrokenShort([string]$text, $sp) {
    if ($sp.Language -eq 'de') {
        $broken = $text.Replace('**HDR automatisch**', '**HDR beim Start**').Replace('## Schnellstart', '## Loslegen').Replace('(README.md)', '(README.md.bak)')
        $broken = $broken.Replace('blob/main/docs/handbuch.md', 'blob/main/docs/handbuh.md').Replace('docs/handbuch.md#fehlersuche', 'docs/handbuch.md#troubleshooting')
        $expected = @('Bezeichnung nicht in der Oberfläche: HDR beim Start', 'Abschnitt fehlt: Schnellstart', 'Verweisziel fehlt: README.md.bak', 'Pflichtverweis fehlt: README.md',
                      'Online-Verweis auf eine fehlende Datei: docs/handbuh.md', "Pflichtverweis fehlt: $repoUrl/blob/main/docs/handbuch.md", 'Anker fehlt: docs/handbuch.md#troubleshooting')
    } else {
        $broken = $text.Replace('**HDR on launch**', '**HDR on start**').Replace('## Quick start', '## Schnellstart').Replace('(README.de.md)', '(README.de.md.bak)')
        $broken = $broken.Replace('blob/main/docs/manual.md', 'blob/main/docs/manuel.md').Replace('docs/manual.md#troubleshooting', 'docs/manual.md#fehlersuche')
        $expected = @('Bezeichnung nicht in der Oberfläche: HDR on start', 'Abschnitt fehlt: Quick start', 'Deutsch in einer Überschrift: Schnellstart', 'Verweisziel fehlt: README.de.md.bak', 'Pflichtverweis fehlt: README.de.md',
                      'Online-Verweis auf eine fehlende Datei: docs/manuel.md', "Pflichtverweis fehlt: $repoUrl/blob/main/docs/manual.md", 'Anker fehlt: docs/manual.md#fehlersuche')
    }
    $broken = $broken.Replace('docs/images/library-shelf.png', 'docs/images/library-shelve.png').Replace('][shot-grid]', '][shot-gird]')
    $expected += @('Verweisziel fehlt: docs/images/library-shelve.png', 'Bildverweis ohne Ziel: shot-gird')
    return [pscustomobject]@{ Text = $broken; Expected = $expected }
}

foreach ($sp in $shortProfiles) {
    Note "--- $($sp.Name) ($($sp.Language), kurz)"
    $shortPath = Join-Path $root $sp.Name
    Check (Test-Path $shortPath) "$($sp.Name) vorhanden"
    $short = [IO.File]::ReadAllText($shortPath, [Text.Encoding]::UTF8)
    $shortProblems = Test-ShortReadme $short $sp
    foreach ($x in $shortProblems) { Note "  - $x" }
    $shortLinks = Get-Links $short
    Check ($shortProblems.Count -eq 0) ("{0}: {1} Zeilen, {2} Abschnitte, {3} Bilder, {4} Verweisziele, Pflichtverweise und Bezeichnungen stimmen ({5} Probleme)" -f $sp.Name, (Get-LineCount $short), $sp.Sections.Count, $shortLinks.Images.Count, $shortLinks.Targets.Count, $shortProblems.Count)

    $bs = Get-BrokenShort $short $sp
    $foundShort = Test-ShortReadme $bs.Text $sp
    $missedShort = @($bs.Expected | Where-Object { $_ -notin $foundShort })
    Check ($missedShort.Count -eq 0 -and $foundShort.Count -eq $bs.Expected.Count) ("{0} Gegenprobe: {1} von {2} eingebauten Fehlern erkannt, {3} Meldungen insgesamt{4}" -f $sp.Name, ($bs.Expected.Count - $missedShort.Count), $bs.Expected.Count, $foundShort.Count, $(if ($missedShort) { '; übersehen: ' + ($missedShort -join ' | ') } else { '' }))
}

# Gegenproben der leichten Prüfung: Jede Überschrift der deutschen README gilt als deutsch (sonst fände die Prüfung der
# englischen nichts), und die deutsche README scheitert gegen die englischen Texte.
$shortGerman = [IO.File]::ReadAllText((Join-Path $root 'README.de.md'), [Text.Encoding]::UTF8)
$germanHeadings = @(Get-Headings $shortGerman | Where-Object { $_ -ne 'HDRpilot' })
$notDetected = @($germanHeadings | Where-Object { -not (Test-GermanHeading $_) })
Check ($germanHeadings.Count -ge $shortProfiles[1].Sections.Count -and $notDetected.Count -eq 0) ("Gegenprobe Überschriften: {0} von {1} Überschriften der README.de.md als deutsch erkannt{2}" -f ($germanHeadings.Count - $notDetected.Count), $germanHeadings.Count, $(if ($notDetected) { '; übersehen: ' + ($notDetected -join ' | ') } else { '' }))
$shortCross = Test-ShortReadme $shortGerman $shortProfiles[0]
Check ($shortCross.Count -gt 0) ("Gegenprobe über Kreuz: README.de.md gegen die englischen Texte liefert {0} Probleme" -f $shortCross.Count)

if ($ok) { Write-Output 'PHASE5_README_OK'; exit 0 }
Write-Output 'PHASE5_README_FAILED'
exit 1
