# Gate: Veröffentlichung auf GitHub (Vorgaben des Nutzers 2026-10-20). Keine lokalen Pfade, keine Spuren von
# Claude-Sitzungen und keine persönlichen Kennungen in den Dateien, die Git verfolgt, und mit -History auch nicht in
# der Geschichte.
#
# Durchsucht alle verfolgten Dateien des Arbeitsstands (mit -Rev stattdessen einen Commit oder Baum, ohne ihn
# auszuchecken; mit -History jede Blob-Version und jeden Pfadnamen, die von den genannten Refs aus erreichbar sind,
# dazu Autor, Committer und Nachricht jedes Commits), Text als UTF-8 und zusätzlich URL-dekodiert (%5C …),
# Binärdateien als Bytes und zusätzlich als UTF-16LE, nach:
#   1 Profilpfaden <Laufwerk>:\Users\<Name> (jeder Trenner, auch /c/Users/<Name> und URL-kodiert), außer allgemeinen
#     Platzhaltern (Test, Public, Default, einzelne Buchstaben …; %USERPROFILE% und <Platzhalter> passen ohnehin nicht)
#   2 Ordnern und Kennungen von Claude-Sitzungen: Temp-Ordner von Claude, Projektordner-Namen, Sitzungs-ID vor dem
#     Scratchpad, Scratchpad-Pfade, Ordner der Desktop-App, Worktrees unter .claude, 8.3-Kurzformen
#   3 privaten Links auf claude.ai (Artefakt, Freigabe, Chat, Projekt)
#   4 der früheren Mail-Adresse
#   5 echten Installationsordnern des Prüfrechners (etwa den Steam-Bibliotheken): erste und zweite Ordnerebene mit
#     Laufwerk; dazu unverwechselbare Ordnernamen der Laufwerke auch ohne Laufwerksbuchstaben (als ganzes Wort)
#   6 weiteren Kennungen als ganzes Wort, als Versionsnummer (ab zwei Teilen), als Zahl mit Tausendertrennzeichen
#     („12 345“, „1,234,567“; verglichen ohne Trennzeichen) oder als Zeitpunkt (JJJJ-MM-TT hh:mm:ss, auch mit T oder
#     Punkten, und auf die Minute genau JJJJ-MM-TT hh:mm): Vorname, Rechnername, GOG-Benutzer-ID, PnP-Instanz und
#     MonitorDataStore-Schlüssel des Bildschirms, Adapter-Adresse, Epic-InstallationGuid, ID des früheren privaten Links,
#     persönliche Angaben und Treiberstand aus der früheren Umgebungsbeschreibung, Werte, die 1:1 aus Dateien des
#     Prüfrechners kopiert waren (Zeitstempel, Größen, Dateinamen und Größen aus Zwischenspeichern, Build-Kennungen,
#     Versionen, Bild-Hashes); dazu Benutzer- und Rechnername des Rechners, auf dem das Gate gerade läuft
#   7 Stichproben aus den entfernten Spielelisten (Titel und Kennungen, die nur dort standen, als ganzes Wort): Sie
#     finden alte Fassungen der Listen, die beim Umschreiben der Geschichte übrig geblieben wären
#   8 echten Spielordnern an den Standardorten der Launcher (vor der Veröffentlichung durch erfundene Spiele ersetzt),
#     auch ohne Laufwerk: direkt hinter einem Launcher-Ordner (…\steamapps\common\<Spiel>, XboxGames\<Spiel>,
#     EA Games\<Spiel>, GOG Galaxy\Games\<Spiel>, …\<Spiel>), direkt vor einem Merkmal der Launcher (<Spiel>\__Installer,
#     <Spiel>\Content, <Spiel>\.egstore), am Anfang eines relativen Pfads mit mindestens einer weiteren Ebene
#     (<Spiel>/bin/<exe>.exe wie in den Profillisten des Grafiktreibers, <Spiel>\<exe>.exe, ..\<Spiel>\<Ordner>\; Fund 1
#     der Prüfung vor der Veröffentlichung, Runde 3) und als getrennte Zeichenketten (Path.Combine(x, "XboxGames", "<Spiel>"),
#     ["steamapps", "common", "<Spiel>"], Join-Path); so auch in Commit-Nachrichten. Titel im Fließtext bleiben erlaubt,
#     ebenso ein Name hinter einem anderen Ordner (Documents\<Spiel>\…, ein erfundener Ordner D:\Spielordner\<Spiel>).
#     Allgemeine Wörter, die zugleich Ordnernamen sind, zählen nur in der Schreibweise des Ordners.
#   9 SHA-256 (Kleinschreibung, UTF-8) eines Werts der privaten Liste, in jeder Schreibweise der Hexziffern: Hashwerte
#     bekannter Namen lassen sich mit einer Wortliste zurückrechnen (Fund 1 der Prüfung vor der Veröffentlichung,
#     Runde 2: frühere Fassungen dieses Skripts enthielten solche Listen)
# Die Werte zu 4 bis 9 stehen nicht in diesem Skript, sondern nur als SHA-256 in einer privaten Liste außerhalb des
# Repositorys (Zeilen „Gruppe<Tab>SHA-256“): -PrivateList, sonst die Umgebungsvariable HDRPILOT_PUBLISH_LIST, sonst
# hdrpilot-publish\publish-check-list.tsv im gemeinsamen Git-Ordner des Repositorys. Fehlt sie, schlägt das Gate fehl.
# Mit -History zusätzlich: Autor und Committer jedes Commits nur Dante_GE (oder DanteGE) mit der Noreply-Adresse von
# GitHub, als Committer auch GitHub selbst (Commits über die Weboberfläche); die Nachrichten wie Dateien geprüft.
#
# Positivkontrolle: Eine Datei im Temp-Ordner mit eingeschleusten Kontrollwerten muss in jeder Gruppe genau die
# erwarteten Treffer liefern; erlaubte Schreibweisen (%USERPROFILE%, C:\Users\Test, <Notizordner>, Dante_GE, die
# Noreply-Adresse von GitHub, erfundene Ordner wie D:\Steam-Bibliothek) dürfen keinen liefern. Die Kontrollwerte und
# ihre Hashwerte entstehen erst zur Laufzeit aus Teilen. Mit -History zusätzlich ein Wegwerf-Repository im Temp-Ordner
# mit eingeschleusten Werten nur in alten Versionen, in einer Nachricht und bei Autor und Committer: Die Prüfung der
# Geschichte muss genau diese finden.
# Marker: PUBLISH_CHECK_OK nur mit gelesener privater Liste, ohne Treffer und mit bestandener Positivkontrolle.
param(
    [string]$Root = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    # Commit oder Baum (etwa aus „git merge-tree --write-tree“) statt des Arbeitsstands
    [string]$Rev = '',
    # weitere Dateien prüfen (für eigene Gegenproben außerhalb des Repositorys), mehrere durch „;“ getrennt
    [string[]]$ExtraFile = @(),
    # Geschichte statt Arbeitsstand (G13.5): Refs, deren ganze Geschichte geprüft wird, mehrere durch „;“ getrennt;
    # „*“ steht für alle Refs (git --all)
    [string[]]$History = @(),
    # private Liste der gesuchten Werte (nie im Repository); Standard siehe oben
    [string]$PrivateList = ''
)
$ErrorActionPreference = 'Stop'
$script:ok = $true
function Note([string]$text) { Write-Output ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text) }
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

# ---- Private Liste und Kontrollwerte (SHA-256) --------------------------------------------------------------------
# Wort, Versionsnummer oder Zeitpunkt (als SHA-256) → Gruppe der Meldung; dazu Mail-Adressen, Installationsordner
# („<laufwerk>:\ordner“ bzw. „<laufwerk>:\ordner\unterordner“, klein, mit einfachem Backslash), Spielordner (klein bzw.
# in der Schreibweise des Ordners) und alle Werte zusammen (Gruppe 9). Dieses Skript enthält keinen dieser Hashwerte.
$wordGroups = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$mailHashes = New-Object 'System.Collections.Generic.HashSet[string]'
$rootHashes = New-Object 'System.Collections.Generic.HashSet[string]'
$gameFolderHashes = New-Object 'System.Collections.Generic.HashSet[string]'
$gameFolderExact = New-Object 'System.Collections.Generic.HashSet[string]'
$privateHashes = New-Object 'System.Collections.Generic.HashSet[string]'
$wordGroupNames = @('Kennung', 'echter Ordnername', 'Titel aus entfernter Spieleliste', 'kopierter Wert')
# Liefert $false für eine unbekannte Gruppe
function Add-Private([string]$group, [string]$hash) {
    if ($wordGroupNames -contains $group) { $wordGroups[$hash] = $group }
    elseif ($group -eq 'Mail-Adresse') { [void]$mailHashes.Add($hash) }
    elseif ($group -eq 'Installationsordner') { [void]$rootHashes.Add($hash) }
    elseif ($group -eq 'Spielordner') { [void]$gameFolderHashes.Add($hash) }
    elseif ($group -eq 'Spielordner exakt') { [void]$gameFolderExact.Add($hash) }
    else { return $false }
    [void]$privateHashes.Add($hash)
    return $true
}

$sha = [Security.Cryptography.SHA256]::Create()
function Get-Hash([string]$s) { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s.ToLowerInvariant())))).Replace('-', '').ToLowerInvariant() }
function Get-HashExact([string]$s) { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s)))).Replace('-', '').ToLowerInvariant() }

$git = (Get-Command git -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $git) { $git = Join-Path $env:ProgramFiles 'Git\cmd\git.exe' }

# Private Liste: -PrivateList, sonst HDRPILOT_PUBLISH_LIST, sonst hdrpilot-publish\publish-check-list.tsv im gemeinsamen
# Git-Ordner (bei Worktrees der Ordner des Hauptrepositorys)
$listSource = 'Parameter -PrivateList'
if (-not $PrivateList -and $env:HDRPILOT_PUBLISH_LIST) { $PrivateList = $env:HDRPILOT_PUBLISH_LIST; $listSource = 'HDRPILOT_PUBLISH_LIST' }
if (-not $PrivateList) {
    $listSource = 'hdrpilot-publish im gemeinsamen Git-Ordner'
    $common = @(& $git -c core.longpaths=true -C $Root rev-parse --path-format=absolute --git-common-dir) | Select-Object -First 1
    if ($LASTEXITCODE -eq 0 -and $common) { $PrivateList = Join-Path $common.Trim() 'hdrpilot-publish\publish-check-list.tsv' }
}
$listCounts = [ordered]@{}
$listError = ''
if ($PrivateList -and (Test-Path -LiteralPath $PrivateList -PathType Leaf)) {
    $lineNo = 0
    foreach ($line in [IO.File]::ReadAllLines($PrivateList, [Text.Encoding]::UTF8)) {
        $lineNo++
        if (-not $line.Trim() -or $line.StartsWith('#')) { continue }
        $fields = $line.TrimEnd("`r") -split "`t"
        if ($fields.Count -ne 2 -or $fields[1] -cnotmatch '^[0-9a-f]{64}$' -or -not (Add-Private $fields[0] $fields[1])) { $listError = "Zeile $lineNo unlesbar"; break }
        $listCounts[$fields[0]] = 1 + [int]$listCounts[$fields[0]]
    }
}
else { $listError = "nicht gefunden ($listSource)" }

# Kontrollwerte der Positivkontrolle: entstehen erst hier aus Teilen, damit dieses Skript bei der eigenen Prüfung keinen
# Treffer liefert
$controlValues = [ordered]@{
    # Runde 3: eine Zahl mit Tausendertrennzeichen und ein Zeitpunkt auf die Minute
    'Kennung'                          = @(('publishcheck' + 'kontrolle4711'), ('98.0.76.' + '5432'), ('98' + '.76'), ('2031-02-03 04:05' + ':06'),
                                           ('4711' + '0815'), ('2031-02-03 05' + ':06'))
    'echter Ordnername'                = @(('publishcheck' + 'ordner4711'))
    'Titel aus entfernter Spieleliste' = @(('publishcheck' + 'titel4711'))
    'Mail-Adresse'                     = @(('kontrolle@publish-check' + '.invalid'))
    'Installationsordner'              = @(('q:\publish-check-' + 'kontrolle'))
    'Spielordner'                      = @(('publishcheck' + 'spiel4711'), ("publishcheck's " + 'spiel4712'))
}
foreach ($g in $controlValues.Keys) { foreach ($v in $controlValues[$g]) { [void](Add-Private $g (Get-Hash $v)) } }
[void](Add-Private 'Spielordner exakt' (Get-HashExact ('PUBLISHCHECK' + 'EXAKT')))

# Benutzer- und Rechnername dieses Rechners (ab drei Zeichen, keine Allerweltsnamen)
$generic = @('user', 'admin', 'administrator', 'test', 'tester', 'public', 'default', 'runner', 'builder', 'owner', 'localhost')
foreach ($n in @($env:USERNAME, $env:COMPUTERNAME)) {
    if ($n -and $n.Length -ge 3 -and $generic -notcontains $n.ToLowerInvariant()) { [void](Add-Private 'Kennung' (Get-Hash $n)) }
}

# ---- Muster -----------------------------------------------------------------------------------------------------
$S = '(?:\\{1,4}|/)'
$nameChar = '[^\\/\s"''`<>|:*?,;)\]%$&{}]'
$userPath = New-Object regex ("(?<![A-Za-z])[A-Za-z]:$S+Users$S+(?<name>$nameChar+)|(?<![\w/])/[A-Za-z]/Users/(?<name>$nameChar+)"), 'IgnoreCase'
$allowedNames = @('Public', 'Default', 'All', 'Test', 'Tester', 'User', 'Benutzer', 'Name', 'Tom')
$claudePatterns = @(
    @('Temp-Ordner von Claude', "Temp$S+claude(?:$S|\b)"),
    @('Claude-Projektordner', 'C-{2}Users-'),
    @('Claude-Sitzungsordner', 'scratch-work(?:spaces?)\b'),
    @('Sitzungs-ID vor dem Scratchpad', "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$S+scratchpad"),
    @('Scratchpad-Pfad', "scratchpad$S|${S}scratchpad(?![\w-])"),
    @('Ordner der Claude-App', "AppData$S+Roaming$S+Claude\b"),
    @('Worktree unter .claude', "\.claude$S+worktrees"),
    @('8.3-Kurzform', 'C-{2}USE~\d|SCRATC~\d'),
    @('privater claude.ai-Link', 'claude\.ai/(?:public/)?(?:artifacts?|share|chat|projects?)/')
) | ForEach-Object { , @($_[0], (New-Object regex ($_[1]), 'IgnoreCase')) }
$mailRegex = New-Object regex '[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}'
$wordRegex = New-Object regex '\w+'
# Versionsnummern mit mindestens zwei Teilen (etwa ein Treiberstand), als Ganzes
$versionRegex = New-Object regex '(?<![\w.])\d+(?:\.\d+)+(?![\w]|\.\d)'
# Zahlen mit Tausendertrennzeichen (Leerzeichen, geschützte Leerzeichen, Punkt, Komma, Apostroph), verglichen ohne
# Trennzeichen: „12 345 Byte“ wie „12345“ (Runde 3: Größe einer Datei aus einem Zwischenspeicher des Prüfrechners)
$groupedNumberRegex = New-Object regex ('(?<![\d.,])\d{1,3}(?:[ .,''' + [char]0xA0 + [char]0x202F + ']\d{3})+(?!\d|[.,]\d)')
# Zeitpunkte JJJJ-MM-TT hh:mm:ss (auch mit T sowie Punkten als Zeittrenner), verglichen als „JJJJ-MM-TT hh:mm:ss“ und
# auf die Minute genau als „JJJJ-MM-TT hh:mm“ (Runde 3: Änderungszeit einer Datei, ohne Sekunden notiert)
$dateTimeRegex = New-Object regex '(?<!\d)(?<d>\d{4}-\d\d-\d\d)[ T](?<t>\d\d[:.]\d\d)(?:[:.](?<s>\d\d)(?!\d)|(?![:.]?\d))'
# Gruppe 9: SHA-256 als 64 Hexziffern in beliebiger Schreibweise
$hexRegex = New-Object regex '(?<![0-9A-Fa-f])[0-9A-Fa-f]{64}(?![0-9A-Fa-f])'
$part = '[^\\/"''`<>|:*?,;\])\x00-\x1f\x7f]+'
# Vorausschau statt Verbrauch: Ein Ordnername mit Leerzeichen darf keinen folgenden Laufwerkspfad verschlucken.
$rootRegex = New-Object regex ("(?<![A-Za-z])(?<d>[A-Za-z]):(?=$S+(?<a>$part)(?:$S+(?<b>$part))?)")
$percentRegex = New-Object regex '%[0-9A-Fa-f]{2}'
# Gruppe 8: Ordnername hinter einem Launcher-Ordner (auch „...\“ oder „…\“ als gekürzte Wurzel) bzw. vor einem Merkmal;
# ein Apostroph gehört zum Namen („Assassin's …“), am Ende eines Worts schneidet Find-GameFolders ihn ab
$segment = '[^\\/"`<>|:*?\r\n]{1,120}'
$ellipsis = [string][char]0x2026
$gameRootRegex = New-Object regex ("(?:steamapps$S+common|XboxGames|EA Games|Origin Games|GOG Galaxy$S+Games|Epic Games|Ubisoft Game Launcher$S+games|Rockstar Games|Riot Games|Battlestate Games|\.\.\.|$ellipsis)$S+(?<seg>$segment)"), 'IgnoreCase'
$gameMarkerRegex = New-Object regex ("(?<seg>$segment)$S+(?:__Installer|Content|\.egstore)(?![\w.-])"), 'IgnoreCase'
# Runde 3, Fund 1: Ordnername am Anfang eines relativen Pfads („<spiel>/bin/<exe>.exe“ aus einer Profilliste des
# Grafiktreibers). Der Trenner wird gesucht, das Stück davor per Rückschau gelesen: höchstens 120 Zeichen ohne Trenner,
# davor kein Trenner, kein Wortzeichen, kein Punkt und kein Bindestrich, oder davor „.\“ bzw. „..\“ (so zählt nur die
# erste Ebene; „Documents\<Spiel>\…“ und „D:\Spielordner\<Spiel>\…“ bleiben erlaubt). Danach muss eine weitere Ebene
# folgen: ein Name mit Trenner dahinter, eine Datei mit Endung, ein Name am Ende eines Codeabschnitts oder einer
# Zeichenkette, oder (mit Leerzeichen) bis zu 60 Zeichen vor dem nächsten Trenner. „Spiel A/Teil 2“ im Fließtext zählt nicht.
$relNext = "(?:[^\s\\/""``'<>|:*?]+(?:$S|\.[A-Za-z0-9]{1,5}(?!\w)|(?=[``""']))|[^\\/""``<>|:*?\r\n]{1,60}$S)"
$gameRelativeRegex = New-Object regex ("(?<=(?:(?<![\w\\/.-])|(?<![\w\\/.-])\.{1,2}$S+)(?<seg>$segment))$S+(?=$relNext)")
# getrennte Zeichenketten: Launcher-Ordner als eigene Zeichenkette und die Zeichenketten, die nur durch Kommas getrennt
# folgen (Argumente, Feldeinträge; ein JSON-Schlüssel beendet die Folge, weil ein Doppelpunkt folgt), oder eine
# Zeichenkette als Argument eines Pfadaufrufs. „Steam“ und „Software“ fehlen absichtlich: "source": "Steam" und
# Registry-Schlüssel sind Daten, kein Pfad.
$rootLiteralRegex = New-Object regex '(?<q>["''])(?:XboxGames|steamapps|common|EA Games|Origin Games|GOG Galaxy|Games|Epic Games|Ubisoft Game Launcher|Battle\.net|Rockstar Games|Riot Games|Battlestate Games|Codemasters|Program Files(?: \(x86\))?|SteamLibrary)\k<q>(?<seq>(?:\s*,\s*(?<r>["''])[^"''\r\n]{1,80}\k<r>)+)', 'IgnoreCase'
$pathCallRegex = New-Object regex '\bPath\.(?:Combine|Join)\s*\(|\bJoin-Path\b|\bos\.path\.join\s*\(', 'IgnoreCase'
$literalRegex = New-Object regex '(?<q>["''])(?<v>[^"''\r\n]{1,80})\k<q>'

function Get-LineNumber([string]$text, [int]$index) { ([regex]::Matches($text.Substring(0, $index), "`n")).Count + 1 }
function Mask([string]$s) { if ($s.Length -le 2) { return '…' } return $s.Substring(0, 2) + '…' }

# Präfixe eines Ordnernamens an Leerzeichen („Spiele liegen“ → „Spiele“, „Spiele liegen“), höchstens sechs Wörter
function Get-Prefixes([string]$name) {
    $words = @($name.Trim() -split ' +' | Where-Object { $_ })
    $list = @()
    for ($i = 1; $i -le [Math]::Min(6, $words.Count); $i++) { $list += ($words[0..($i - 1)] -join ' ').TrimEnd('.', ' ') }
    return $list
}
# Endstücke an Leerzeichen („unter Spiel 6“ → „6“, „Spiel 6“, „unter Spiel 6“), höchstens sechs Wörter, ohne Satzzeichen davor
function Get-Suffixes([string]$name) {
    $words = @($name.Trim() -split ' +' | Where-Object { $_ })
    $list = @()
    $lead = [char[]]@(0x28, 0x5B, 0x7B, 0x201E, 0x201C, 0xBB, 0xAB, 0x27, 0x20)   # ( [ { „ “ » « ' und Leerzeichen
    for ($i = 1; $i -le [Math]::Min(6, $words.Count); $i++) { $list += ($words[($words.Count - $i)..($words.Count - 1)] -join ' ').TrimStart($lead) }
    return $list
}
# Gruppe 8 in einem Text: je Fundstelle höchstens ein Treffer (Zeile und Ordnername; dieselbe Stelle kann auf mehrere
# Formen passen, etwa „<Spiel>\__Installer“ als Merkmal und als relativer Pfad)
function Find-GameFolders([string]$text, $found) {
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $add = {
        param([int]$index, [string]$value)
        $line = Get-LineNumber $text $index
        if ($seen.Add(('{0}|{1}' -f $line, $value.ToLowerInvariant()))) { [void]$found.Add(('{0}: echter Spielordner am Standardort: {1}' -f $line, (Mask $value))) }
    }
    $trail = [char[]]@(0x2E, 0x2C, 0x3B, 0x29, 0x5D, 0x7D, 0x201C, 0x27, 0x20)   # . , ; ) ] } “ ' und Leerzeichen
    # ein Spielordner: klein geschrieben in der Liste oder (allgemeine Wörter) genau in der Schreibweise des Ordners;
    # die Antwort je Kandidat bleibt im Zwischenspeicher (der relative Pfad prüft sehr viele Stücke)
    $isGame = {
        param([string]$c)
        $r = $false
        if (-not $script:gameCache.TryGetValue($c, [ref]$r)) {
            $r = $gameFolderHashes.Contains((Get-Hash $c)) -or $gameFolderExact.Contains((Get-HashExact $c))
            $script:gameCache[$c] = $r
        }
        $r
    }
    foreach ($m in $gameRootRegex.Matches($text)) {
        $g = $m.Groups['seg']
        foreach ($c in (Get-Prefixes $g.Value)) { $c = $c.TrimEnd($trail); if ($c -and (& $isGame $c)) { & $add $g.Index $c; break } }
    }
    foreach ($m in $gameMarkerRegex.Matches($text)) {
        $g = $m.Groups['seg']
        foreach ($c in (Get-Suffixes $g.Value)) { if ($c -and (& $isGame $c)) { & $add $g.Index $c; break } }
    }
    foreach ($m in $gameRelativeRegex.Matches($text)) {
        $g = $m.Groups['seg']
        foreach ($c in (Get-Suffixes $g.Value)) { if ($c -and (& $isGame $c)) { & $add $g.Index $c; break } }
    }
    # Stücke mit Zeichenketten: Folge hinter dem Launcher-Ordner bzw. Argumente eines Pfadaufrufs bis „;“, „)“ oder Zeilenende
    $pieces = @()
    foreach ($m in $rootLiteralRegex.Matches($text)) { $g = $m.Groups['seq']; $pieces += , @($g.Index, $g.Value) }
    foreach ($m in $pathCallRegex.Matches($text)) {
        $start = $m.Index + $m.Length
        $piece = $text.Substring($start, [Math]::Min(400, $text.Length - $start))
        $cut = $piece.IndexOfAny([char[]]";)`r`n")
        if ($cut -ge 0) { $piece = $piece.Substring(0, $cut) }
        $pieces += , @($start, $piece)
    }
    foreach ($p in $pieces) {
        foreach ($l in $literalRegex.Matches($p[1])) {
            $v = $l.Groups['v'].Value.Trim()
            if ($v -and (& $isGame $v)) { & $add ($p[0] + $l.Index) $v }
        }
    }
}

# Gruppe eines Worts oder einer Versionsnummer in Kleinschreibung ('' = unbedenklich); Find-Problems fragt zuerst den
# Zwischenspeicher, damit nicht jedes Wort einen Funktionsaufruf kostet
function Get-WordGroup([string]$w) {
    $h = Get-Hash $w
    $group = if ($wordGroups.ContainsKey($h)) { $wordGroups[$h] } else { '' }
    $script:wordCache[$w] = $group
    return $group
}
$script:wordCache = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$script:gameCache = New-Object 'System.Collections.Generic.Dictionary[string,bool]'

# Treffer in einem Text: Liste von "Zeile: Gruppe: Ausschnitt"
function Find-Problems([string]$text) {
    $found = New-Object System.Collections.ArrayList
    foreach ($m in $userPath.Matches($text)) {
        $name = $m.Groups['name'].Value
        if ($name.Length -le 1 -or ($allowedNames | Where-Object { $_ -ieq $name })) { continue }
        [void]$found.Add(('{0}: Profilpfad: {1}' -f (Get-LineNumber $text $m.Index), $m.Value))
    }
    foreach ($p in $claudePatterns) {
        foreach ($m in $p[1].Matches($text)) { [void]$found.Add(('{0}: {1}: {2}' -f (Get-LineNumber $text $m.Index), $p[0], $m.Value)) }
    }
    foreach ($m in $mailRegex.Matches($text)) {
        if ($mailHashes.Contains((Get-Hash $m.Value))) { [void]$found.Add(('{0}: Mail-Adresse: {1}' -f (Get-LineNumber $text $m.Index), (Mask $m.Value))) }
    }
    foreach ($m in $rootRegex.Matches($text)) {
        $drive = $m.Groups['d'].Value.ToUpperInvariant() + ':\'
        $hit = $null
        foreach ($a in (Get-Prefixes $m.Groups['a'].Value)) {
            if ($rootHashes.Contains((Get-Hash ($drive + $a)))) { $hit = $drive + $a; break }
            if ($m.Groups['b'].Success -and $a -eq $m.Groups['a'].Value.Trim()) {
                foreach ($b in (Get-Prefixes $m.Groups['b'].Value)) {
                    if ($rootHashes.Contains((Get-Hash ($drive + $a + '\' + $b)))) { $hit = $drive + $a + '\' + $b; break }
                }
            }
            if ($hit) { break }
        }
        if ($hit) { [void]$found.Add(('{0}: echter Installationsordner: {1}' -f (Get-LineNumber $text $m.Index), (Mask $hit.Substring(3)))) }
    }
    foreach ($m in $wordRegex.Matches($text)) {
        if ($m.Length -lt 3) { continue }
        $w = $m.Value.ToLowerInvariant()
        $group = $null
        if (-not $script:wordCache.TryGetValue($w, [ref]$group)) { $group = Get-WordGroup $w }
        if ($group) { [void]$found.Add(('{0}: {1}: {2}' -f (Get-LineNumber $text $m.Index), $group, (Mask $m.Value))) }
    }
    foreach ($m in $versionRegex.Matches($text)) {
        $w = $m.Value
        $group = $null
        if (-not $script:wordCache.TryGetValue($w, [ref]$group)) { $group = Get-WordGroup $w }
        if ($group) { [void]$found.Add(('{0}: {1}: {2}' -f (Get-LineNumber $text $m.Index), $group, (Mask $m.Value))) }
    }
    # Zeitpunkte: mit Sekunden als Ganzes und auf die Minute, ohne Sekunden auf die Minute; je Fundstelle ein Treffer
    foreach ($m in $dateTimeRegex.Matches($text)) {
        $minute = $m.Groups['d'].Value + ' ' + $m.Groups['t'].Value.Replace('.', ':')
        $keys = if ($m.Groups['s'].Success) { @(($minute + ':' + $m.Groups['s'].Value), $minute) } else { @($minute) }
        foreach ($w in $keys) {
            $group = $null
            if (-not $script:wordCache.TryGetValue($w, [ref]$group)) { $group = Get-WordGroup $w }
            if ($group) { [void]$found.Add(('{0}: {1}: {2}' -f (Get-LineNumber $text $m.Index), $group, (Mask $m.Value))); break }
        }
    }
    # Zahlen mit Tausendertrennzeichen, verglichen ohne Trennzeichen
    foreach ($m in $groupedNumberRegex.Matches($text)) {
        $w = [regex]::Replace($m.Value, '\D', '')
        $group = $null
        if (-not $script:wordCache.TryGetValue($w, [ref]$group)) { $group = Get-WordGroup $w }
        if ($group) { [void]$found.Add(('{0}: {1}: {2}' -f (Get-LineNumber $text $m.Index), $group, (Mask $m.Value))) }
    }
    # Gruppe 9: Hashwert eines Werts der privaten Liste (oder eines Kontrollwerts)
    foreach ($m in $hexRegex.Matches($text)) {
        if ($privateHashes.Contains($m.Value.ToLowerInvariant())) { [void]$found.Add(('{0}: Hashwert einer privaten Angabe: {1}' -f (Get-LineNumber $text $m.Index), (Mask $m.Value))) }
    }
    Find-GameFolders $text $found
    return ,$found
}

# Inhalt lesen: Text als UTF-8 (BOM beachtet), dazu URL-dekodiert, wenn er %XX enthält; mit Nullbytes (Binärdatei,
# UTF-16) stattdessen Bytes 1:1 und UTF-16LE
function Get-Texts([byte[]]$bytes) {
    if ([Array]::IndexOf($bytes, [byte]0) -ge 0) {
        return , @([Text.Encoding]::GetEncoding(28591).GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes))
    }
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    $texts = @($text)
    if ($percentRegex.IsMatch($text)) {
        try { $decoded = [Uri]::UnescapeDataString($text); if ($decoded -cne $text) { $texts += $decoded } } catch { }
    }
    return , $texts
}
function Find-InBytes([byte[]]$bytes) {
    $all = New-Object System.Collections.ArrayList
    foreach ($t in (Get-Texts $bytes)) { foreach ($p in (Find-Problems $t)) { if (-not $all.Contains($p)) { [void]$all.Add($p) } } }
    return ,$all
}
function Find-InFile([string]$path) { return ,(Find-InBytes ([IO.File]::ReadAllBytes($path))) }

# ---- Geschichte -------------------------------------------------------------------------------------------------
$noreply = '115689511+DanteGE@users.noreply.github.com'

# Git-Objekte über „git cat-file --batch“ (ohne --buffer: Ausgabe nach jedem Objekt geleert, also Frage und Antwort
# abwechselnd, ohne Verklemmung). Die Kennungen gehen als ASCII direkt in den Datenstrom, ohne Vorspann.
function Open-ObjectReader([string]$repo) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $git
    $psi.Arguments = ('-c core.longpaths=true -C "{0}" cat-file --batch' -f $repo)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.CreateNoWindow = $true
    # .NET Framework legt um stdin einen StreamWriter mit Console.InputEncoding und AutoFlush an; bei UTF-8 schriebe er
    # sofort ein BOM vor die erste Kennung. Deshalb für den Start UTF-8 ohne BOM.
    $oldInput = $null
    try { $oldInput = [Console]::InputEncoding; [Console]::InputEncoding = New-Object Text.UTF8Encoding $false } catch { $oldInput = $null }
    try { return [Diagnostics.Process]::Start($psi) }
    finally { if ($oldInput) { try { [Console]::InputEncoding = $oldInput } catch { } } }
}
function Read-GitObject($reader, [string]$id) {
    $request = [Text.Encoding]::ASCII.GetBytes($id + "`n")
    $reader.StandardInput.BaseStream.Write($request, 0, $request.Length)
    $reader.StandardInput.BaseStream.Flush()
    $out = $reader.StandardOutput.BaseStream
    $header = New-Object Text.StringBuilder
    while ($true) {
        $b = $out.ReadByte()
        if ($b -lt 0) { throw "git cat-file: Ausgabe endet vor $id" }
        if ($b -eq 10) { break }
        [void]$header.Append([char]$b)
    }
    $parts = $header.ToString().Split(' ')
    if ($parts.Count -ne 3) { throw ("git cat-file: {0}" -f $header) }
    $size = [int]$parts[2]
    $buffer = New-Object byte[] $size
    $read = 0
    while ($read -lt $size) {
        $n = $out.Read($buffer, $read, $size - $read)
        if ($n -le 0) { throw "git cat-file: $id zu kurz" }
        $read += $n
    }
    [void]$out.ReadByte()
    return [pscustomobject]@{ Type = $parts[1]; Bytes = $buffer }
}
function Close-ObjectReader($reader) {
    try { $reader.StandardInput.Close(); if (-not $reader.WaitForExit(10000)) { $reader.Kill() } } catch { }
}

# Erlaubte Identitäten: „Name <Mail>“
function Test-Identity([string]$role, [string]$name, [string]$mail) {
    if ($mail -ceq $noreply -and ($name -ceq 'Dante_GE' -or $name -ceq 'DanteGE')) { return $true }
    return ($role -eq 'committer' -and $name -ceq 'GitHub' -and $mail -ceq 'noreply@github.com')
}

# Prüft alle Commits (Autor, Committer, Nachricht), Tags und Blob-Versionen, die von den Refs aus erreichbar sind.
# Liefert Treffer, Zahlen und die geprüften Pfade.
function Find-InHistory([string]$repo, [string[]]$refs) {
    $problems = New-Object System.Collections.ArrayList
    foreach ($r in $refs) {
        if ($r -eq '*') { continue }
        & $git -c core.longpaths=true -C $repo rev-parse --verify --quiet ($r + '^{commit}') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Ref $r gibt es nicht in $repo" }
    }
    # „*“ = alle Refs des Repositorys (git --all: Zweige, Tags, entfernte Zweige)
    $refs = @($refs | ForEach-Object { if ($_ -eq '*') { '--all' } else { $_ } })
    $lines = @(& $git -c core.longpaths=true -C $repo rev-list --objects @refs)
    if ($LASTEXITCODE -ne 0) { throw 'git rev-list fehlgeschlagen' }
    $stats = [ordered]@{ commit = 0; tag = 0; tree = 0; blob = 0; path = 0 }
    # Pfadnamen selbst: jeder Pfad, der in einem Commit vorkommt (rev-list nennt einen Blob nur unter seinem ersten Pfad)
    $paths = @(& $git -c core.quotePath=false -c core.longpaths=true -C $repo log --format= --name-only --no-renames -m @refs | Where-Object { $_ } | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'git log --name-only fehlgeschlagen' }
    foreach ($path in $paths) {
        $stats['path']++
        foreach ($p in (Find-Problems $path)) { [void]$problems.Add(('Pfadname {0}:{1}' -f $path, $p)) }
    }
    $reader = Open-ObjectReader $repo
    try {
        foreach ($line in $lines) {
            $id = $line.Substring(0, [Math]::Min(40, $line.Length))
            $path = if ($line.Length -gt 41) { $line.Substring(41) } else { '' }
            $obj = Read-GitObject $reader $id
            $stats[$obj.Type]++
            $short = $id.Substring(0, 7)
            switch ($obj.Type) {
                'blob' {
                    foreach ($p in (Find-InBytes $obj.Bytes)) { [void]$problems.Add(('{0}@{1}:{2}' -f $path, $short, $p)) }
                }
                { $_ -eq 'commit' -or $_ -eq 'tag' } {
                    $text = [Text.Encoding]::UTF8.GetString($obj.Bytes)
                    $split = $text.IndexOf("`n`n")
                    $head = if ($split -ge 0) { $text.Substring(0, $split) } else { $text }
                    $message = if ($split -ge 0) { $text.Substring($split + 2) } else { '' }
                    $people = [regex]::Matches($head, '(?m)^(author|committer|tagger) (.*) <([^>]*)> \d+ [+-]\d{4}$')
                    $expected = if ($obj.Type -eq 'commit') { 2 } else { 1 }
                    if ($people.Count -ne $expected) { [void]$problems.Add(('{0} {1}: Kopf nicht lesbar ({2} Personenzeilen)' -f $obj.Type, $short, $people.Count)) }
                    foreach ($m in $people) {
                        $role = $m.Groups[1].Value
                        if (-not (Test-Identity $role $m.Groups[2].Value $m.Groups[3].Value)) {
                            [void]$problems.Add(('{0} {1}: {2} nicht erlaubt: {3} <{4}>' -f $obj.Type, $short, $role, (Mask $m.Groups[2].Value), (Mask $m.Groups[3].Value)))
                        }
                    }
                    foreach ($p in (Find-Problems $message)) { [void]$problems.Add(('{0} {1} Nachricht:{2}' -f $obj.Type, $short, $p)) }
                }
            }
        }
    }
    finally {
        Close-ObjectReader $reader
    }
    return [pscustomobject]@{ Problems = $problems; Stats = $stats }
}

# ---- Positivkontrolle -------------------------------------------------------------------------------------------
# Die Kontrollwerte entstehen erst hier aus Teilen, damit dieses Skript bei der eigenen Prüfung keinen Treffer liefert.
# Je Zeile die erwarteten Gruppen (genau diese, keine weiteren)
$planted = @(
    @(@('Profilpfad'), ('C:\Users\' + 'Kontrollperson\Desktop\spiel.exe')),
    @(@('Profilpfad'), ('/c/Users/' + 'Kontrollperson/Downloads')),
    @(@('Profilpfad'), ('file:///C%3A%5CUsers%5C' + 'Kontrollperson%5CDesktop')),
    @(@('Temp-Ordner von Claude'), ('%LOCALAPPDATA%\Temp\' + 'claude\x')),
    @(@('Claude-Projektordner'), ('C-' + '-Users-Kontrolle-Projekt')),
    @(@('Claude-Sitzungsordner'), ('scratch-' + 'workspaces')),
    @(@('Sitzungs-ID vor dem Scratchpad', 'Scratchpad-Pfad'), ('0123abcd-0000-4000-8000-' + '0123456789ab\scratch' + 'pad')),
    @(@('Ordner der Claude-App'), ('AppData\Roaming\' + 'Claude\logs')),
    @(@('Worktree unter .claude'), ('.claude\' + 'worktrees\kontrolle')),
    @(@('privater claude.ai-Link'), ('https://claude.' + 'ai/artifact/Kontrolle123')),
    @(@('Mail-Adresse'), ('kontrolle@publish-check' + '.invalid')),
    @(@('echter Installationsordner'), ('Q:\PUBLISH-CHECK-' + 'KONTROLLE\Spiel\spiel.exe')),
    @(@('echter Installationsordner'), ('"q:/publish-check-' + 'kontrolle liegt hier"')),
    @(@('Kennung'), ('Wort publishcheck' + 'kontrolle4711 im Text')),
    @(@('Kennung'), ('Treiber 98.0.76.' + '5432 installiert')),
    @(@('echter Ordnername'), ('Ordner „Spiele publishcheck' + 'ordner4711“ ohne Laufwerk')),
    @(@('Titel aus entfernter Spieleliste'), ('| 4711 | Publishcheck' + 'titel4711 | 2 GB |')),
    # Gruppe 8: hinter einem Launcher-Ordner, als getrennte Zeichenketten, vor einem Merkmal (wie in einer Nachricht)
    @(@('echter Spielordner am Standardort'), ('steamapps\common\Publishcheck' + 'spiel4711\bin\spiel.exe')),
    @(@('echter Spielordner am Standardort'), ('var dir = Path.Combine(root, "XboxGames", "Publishcheck' + 'spiel4711", "Content");')),
    @(@('echter Spielordner am Standardort'), ('Am Zielrechner liegen unter Publishcheck' + 'spiel4711\__Installer\DLC die Manifeste')),
    # Runde 2 der Prüfung vor der Veröffentlichung: Versionsnummer mit zwei Teilen, Zeitpunkt (mit T und Punkten),
    # Hashwert eines Werts der privaten Liste (groß geschrieben), Spielordner in der Schreibweise des Ordners, Apostroph
    @(@('Kennung'), ('Treiber 98' + '.76 installiert')),
    @(@('Kennung'), ('zuletzt gespielt 2031-02-03T04.05' + '.06 laut Datei')),
    @(@('Hashwert einer privaten Angabe'), ('Prüfsumme ' + (Get-Hash ('publishcheck' + 'kontrolle4711')).ToUpperInvariant() + ' im Text')),
    @(@('echter Spielordner am Standardort'), ('steamapps\common\PUBLISHCHECK' + 'EXAKT\spiel.exe')),
    @(@('echter Spielordner am Standardort'), ("steamapps\common\Publishcheck's " + 'Spiel4712\x.exe')),
    # Runde 3: Spielordner am Anfang eines relativen Pfads (wie in einer Profilliste des Grafiktreibers, auch mit ..\ davor
    # und Leerzeichen in der nächsten Ebene), Zahl mit Tausendertrennzeichen (Leerzeichen, Komma), Zeitpunkt auf die
    # Minute (ohne und mit Sekunden)
    @(@('echter Spielordner am Standardort'), ('Profil-Eintrag `publishcheck' + 'spiel4711/bin/spiel.exe` im Treiber')),
    @(@('echter Spielordner am Standardort'), ('..\Publishcheck' + 'spiel4711\Data Files\x')),
    @(@('echter Spielordner am Standardort'), ('Ordner `Publishcheck' + 'spiel4711/bin` im Profil')),
    @(@('echter Spielordner am Standardort'), ('Start über Publishcheck' + 'spiel4711\spiel.exe direkt')),
    @(@('Kennung'), ('Größe 47 110 ' + '815 Byte')),
    @(@('Kennung'), ('Größe 47,110,' + '815 bytes')),
    @(@('Kennung'), ('geändert 2031-02-03 05' + ':06.')),
    @(@('Kennung'), ('geändert 2031-02-03T05' + ':06:59 laut Datei'))
)
$allowed = @('%USERPROFILE%\Desktop', 'C:\Users\Test\AppData\Local', 'C:\Users\Public\Desktop', 'C:\Users\U\x', '<Notizordner>\proto',
    'Dante_GE', '115689511+DanteGE@users.noreply.github.com', 'noreply@anthropic.com', 'D:\Steam-Bibliothek\steamapps',
    'D:\Spielordner\Mein Spiel', 'https://claude.com/claude-code', '.claude/', 'C:\Program Files (x86)\Steam', 'Scratchpad der Sitzung',
    'file:///C%3A%5CUsers%5CTest%5CDesktop', 'Xbox Game Pass', 'Version 1.5.0.0, Build 26300.9550', 'D:\Xbox Bibliothek',
    # Gruppe 8: der Titel allein, ein Einstellungsordner unter Documents und ein erfundenes Spiel bleiben erlaubt
    ('var game = new GameConfig { Name = "Publishcheck' + 'spiel4711" };'), ('Documents\Publishcheck' + 'spiel4711\settings.ini'),
    ('{ "Games": [ { "source": "Steam", "name": "Publishcheck' + 'spiel4711" } ] }'),
    'Path.Combine(root, "XboxGames", "Wolkenhof", "Content");',
    # Runde 2: allgemeines Wort in anderer Schreibweise, fremder Hashwert, anderer Zeitpunkt, andere Versionsnummer
    ('steamapps\common\Publishcheck' + 'exakt\spiel.exe'), ('Prüfsumme ' + ('0123456789abcdef' * 4) + ' im Text'),
    ('zuletzt gespielt 2031-02-03 04:05' + ':07'), ('Treiber 98' + '.77'),
    # Runde 3: Titel mit Schrägstrich im Fließtext, Name hinter einem anderen Ordner, andere Zahl, anderer Zeitpunkt
    ('Titel Publishcheck' + 'spiel4711/Teil 2 im Fließtext'), ('Spielstand unter Documents\Publishcheck' + 'spiel4711\Saved\x.sav'),
    ('Größe 47 110 ' + '816 Byte'), ('geändert 2031-02-03 05' + ':07.'))
$listTotal = 0; foreach ($k in $listCounts.Keys) { $listTotal += $listCounts[$k] }
$listMissing = @('Kennung', 'Installationsordner', 'Spielordner') | Where-Object { -not $listCounts[$_] }
Check (-not $listError -and -not $listMissing) ("Private Liste: {0}" -f $(if ($listError) { $listError } elseif ($listMissing) { 'ohne ' + ($listMissing -join ', ') } else { "$listTotal Werte (" + (($listCounts.Keys | ForEach-Object { '{0} {1}' -f $_, $listCounts[$_] }) -join ', ') + ')' }))
$control = Join-Path ([IO.Path]::GetTempPath()) ('hdrpilot-publish-check-' + [guid]::NewGuid().ToString('N') + '.txt')
try {
    [IO.File]::WriteAllText($control, ((@($planted | ForEach-Object { $_[1] }) + $allowed) -join "`r`n"), (New-Object Text.UTF8Encoding $false))
    $controlHits = Find-InFile $control
    $missing = @()
    $expectedHits = 0
    for ($i = 0; $i -lt $planted.Count; $i++) {
        $line = $i + 1
        foreach ($group in $planted[$i][0]) {
            $expectedHits++
            if (-not ($controlHits | Where-Object { $_ -like "${line}: ${group}:*" })) { $missing += "Zeile ${line} ($group)" }
        }
    }
    $falsePositives = @($controlHits | Where-Object { [int]($_ -split ':')[0] -gt $planted.Count })
    Check ($missing.Count -eq 0 -and $controlHits.Count -eq $expectedHits) ("Positivkontrolle: {0} eingeschleuste Werte, {1} von {2} erwarteten Treffern, {3} Treffer insgesamt{4}" -f $planted.Count, ($expectedHits - $missing.Count), $expectedHits, $controlHits.Count, $(if ($missing) { '; übersehen: ' + ($missing -join ', ') } else { '' }))
    Check ($falsePositives.Count -eq 0) ("Gegenprobe: {0} erlaubte Schreibweisen ohne Treffer{1}" -f $allowed.Count, $(if ($falsePositives) { '; Treffer: ' + ($falsePositives -join ' | ') } else { '' }))
    # UTF-16LE (wie in Binärdateien): derselbe eingeschleuste Profilpfad und ein Ordnername ohne Laufwerk
    [IO.File]::WriteAllText($control, ('C:\Users\' + 'Kontrollperson\x' + "`0" + 'publishcheck' + 'ordner4711'), (New-Object Text.UnicodeEncoding $false, $false))
    $utf16 = Find-InFile $control
    Check (@($utf16 | Where-Object { $_ -like '*Profilpfad*' }).Count -eq 1 -and @($utf16 | Where-Object { $_ -like '*echter Ordnername*' }).Count -eq 1) 'Positivkontrolle UTF-16LE: Profilpfad und Ordnername ohne Laufwerk erkannt'
}
finally {
    Remove-Item -LiteralPath $control -Force -ErrorAction SilentlyContinue
}

# Positivkontrolle der Geschichte: Wegwerf-Repository mit zwei Commits. Alt: eingeschleuster Profilpfad, Titel in einer
# UTF-16-Datei, ein Ordnername im Pfad einer Datei, ein Spielordner als getrennte Zeichenketten, fremder Autor und
# Committer. Neu (Spitze, sauber): erlaubte Identität, eingeschleuster Claude-Ordner und ein Spielordner vor __Installer
# nur in der Nachricht. Erwartet genau diese acht Treffer (Autor, Committer, Profilpfad, Titel, Spielordner in der
# Datei, Pfadname, zweimal Nachricht).
function Test-HistoryControl {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('hpc-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $saved = @{}
    $names = 'GIT_AUTHOR_NAME', 'GIT_AUTHOR_EMAIL', 'GIT_AUTHOR_DATE', 'GIT_COMMITTER_NAME', 'GIT_COMMITTER_EMAIL', 'GIT_COMMITTER_DATE', 'GIT_INDEX_FILE'
    foreach ($n in $names) { $saved[$n] = [Environment]::GetEnvironmentVariable($n) }
    try {
        & $git -c core.longpaths=true init -q $dir
        if ($LASTEXITCODE -ne 0) { throw 'git init fehlgeschlagen' }
        $files = @{
            'alt.txt'   = [Text.Encoding]::UTF8.GetBytes('Pfad C:\Users\' + 'Kontrollperson\Spiele' + "`n")
            'neu.txt'   = [Text.Encoding]::UTF8.GetBytes("sauber`n")
            'alt.bin'   = [Text.Encoding]::Unicode.GetBytes('Titel Publishcheck' + 'titel4711' + "`0")
            'notiz.txt' = [Text.Encoding]::UTF8.GetBytes("Notiz`n")
            'alt.cs'    = [Text.Encoding]::UTF8.GetBytes('var dir = Path.Combine(root, "XboxGames", "Publishcheck' + 'spiel4711");' + "`n")
        }
        $blobs = @{}
        foreach ($k in $files.Keys) {
            $f = Join-Path $dir ('kontrolle-' + $k)
            [IO.File]::WriteAllBytes($f, $files[$k])
            $blob = @(& $git -c core.longpaths=true -C $dir hash-object -w --no-filters -- $f)
            if ($LASTEXITCODE -ne 0 -or $blob.Count -ne 1) { throw "git hash-object fehlgeschlagen ($k)" }
            $blobs[$k] = $blob[0].Trim()
            Remove-Item -LiteralPath $f -Force
        }
        $env:GIT_INDEX_FILE = Join-Path $dir '.git\kontrolle-index'
        & $git -c core.longpaths=true -C $dir update-index --add --cacheinfo ('100644,{0},daten.txt' -f $blobs['alt.txt']) --cacheinfo ('100644,{0},daten.bin' -f $blobs['alt.bin']) --cacheinfo ('100644,{0},alt/publishcheck{1}/notiz.txt' -f $blobs['notiz.txt'], 'ordner4711') --cacheinfo ('100644,{0},daten.cs' -f $blobs['alt.cs'])
        $tree1 = (& $git -c core.longpaths=true -C $dir write-tree | Select-Object -First 1).Trim()
        $env:GIT_AUTHOR_NAME = 'Kontrolle'; $env:GIT_AUTHOR_EMAIL = 'kontrolle@publish-check' + '.invalid'
        $env:GIT_COMMITTER_NAME = 'Kontrolle'; $env:GIT_COMMITTER_EMAIL = 'kontrolle@publish-check' + '.invalid'
        $env:GIT_AUTHOR_DATE = '1767265200 +0100'; $env:GIT_COMMITTER_DATE = '1767265200 +0100'
        $c1 = (& $git -c core.longpaths=true -C $dir commit-tree $tree1 -m 'Kontrolle alt' | Select-Object -First 1).Trim()
        & $git -c core.longpaths=true -C $dir update-index --force-remove daten.bin daten.cs ('alt/publishcheck{0}/notiz.txt' -f 'ordner4711')
        & $git -c core.longpaths=true -C $dir update-index --cacheinfo ('100644,{0},daten.txt' -f $blobs['neu.txt'])
        $tree2 = (& $git -c core.longpaths=true -C $dir write-tree | Select-Object -First 1).Trim()
        $env:GIT_AUTHOR_NAME = 'Dante_GE'; $env:GIT_AUTHOR_EMAIL = $noreply
        $env:GIT_COMMITTER_NAME = 'Dante_GE'; $env:GIT_COMMITTER_EMAIL = $noreply
        $c2 = (& $git -c core.longpaths=true -C $dir commit-tree $tree2 -p $c1 -m ('Notiz in %LOCALAPPDATA%\Temp\' + 'claude\x, Manifeste unter Publishcheck' + 'spiel4711\__Installer\DLC') | Select-Object -First 1).Trim()
        & $git -c core.longpaths=true -C $dir update-ref refs/heads/kontrolle $c2
        if ($LASTEXITCODE -ne 0 -or -not $c1 -or -not $c2) { throw 'Wegwerf-Repository unvollständig' }
        foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n, $saved[$n]) }
        $result = Find-InHistory $dir @('kontrolle')
        $p = @($result.Problems)
        $s1 = $c1.Substring(0, 7); $s2 = $c2.Substring(0, 7)
        $expect = @(
            "commit ${s1}: author nicht erlaubt*", "commit ${s1}: committer nicht erlaubt*",
            "daten.txt@*:1: Profilpfad:*", "daten.bin@*:1: Titel aus entfernter Spieleliste:*",
            "daten.cs@*:1: echter Spielordner am Standardort:*",
            "commit ${s2} Nachricht:1: Temp-Ordner von Claude:*", "commit ${s2} Nachricht:1: echter Spielordner am Standardort:*",
            "Pfadname alt/publishcheck*:1: echter Ordnername:*"
        )
        $missingControl = @($expect | Where-Object { $e = $_; -not ($p | Where-Object { $_ -like $e }) })
        $okStats = $result.Stats['commit'] -eq 2 -and $result.Stats['blob'] -eq 5 -and $result.Stats['path'] -eq 4
        return [pscustomobject]@{
            Ok = ($missingControl.Count -eq 0 -and $p.Count -eq $expect.Count -and $okStats)
            Text = ('{0} Commits, {1} Blob-Versionen, {6} Pfadnamen, {2} von {3} erwarteten Treffern, {4} insgesamt{5}' -f $result.Stats['commit'], $result.Stats['blob'], ($expect.Count - $missingControl.Count), $expect.Count, $p.Count, $(if ($missingControl) { '; übersehen: ' + ($missingControl -join ', ') } elseif ($p.Count -ne $expect.Count) { '; zusätzlich: ' + ($p -join ' | ') } else { '' }), $result.Stats['path'])
        }
    }
    finally {
        foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n, $saved[$n]) }
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# ---- Dateien ----------------------------------------------------------------------------------------------------
$oldEncoding = [Console]::OutputEncoding
$work = $null
try {
    [Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
    $problems = New-Object System.Collections.ArrayList
    $missingFiles = @()
    $refs = @($History | ForEach-Object { $_ -split ';' } | Where-Object { $_ })
    if ($refs) {
        try { $historyControl = Test-HistoryControl }
        catch { $historyControl = [pscustomobject]@{ Ok = $false; Text = 'Abbruch: ' + $_.Exception.Message } }
        Check $historyControl.Ok ('Positivkontrolle der Geschichte (Wegwerf-Repository): ' + $historyControl.Text)
        $result = Find-InHistory $Root $refs
        foreach ($p in $result.Problems) { [void]$problems.Add($p) }
        $st = $result.Stats
        Check ($st['commit'] -gt 0 -and $st['blob'] -gt 100) ("Geschichte {0}: {1} Commits (Autor, Committer, Nachricht), {2} Tags, {3} Bäume, {4} Blob-Versionen, {5} Pfadnamen gelesen" -f ($refs -join ', '), $st['commit'], $st['tag'], $st['tree'], $st['blob'], $st['path'])
        $what = 'Geschichte'
    }
    else {
        if ($Rev) {
            $work = Join-Path ([IO.Path]::GetTempPath()) ('hdrpilot-publish-check-' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory $work | Out-Null
            $tarFile = Join-Path $work 'baum.tar'
            & $git -C $Root archive --format=tar -o $tarFile $Rev
            if ($LASTEXITCODE -ne 0) { throw "git archive $Rev fehlgeschlagen" }
            $base = Join-Path $work 'baum'
            New-Item -ItemType Directory $base | Out-Null
            & (Join-Path $env:SystemRoot 'System32\tar.exe') -xf $tarFile -C $base
            if ($LASTEXITCODE -ne 0) { throw 'tar fehlgeschlagen' }
            $listing = (& $git -C $Root ls-tree -r --name-only -z $Rev) -join "`n"
            $what = "Baum $Rev"
        }
        else {
            $base = $Root
            $listing = (& $git -C $Root ls-files -z) -join "`n"
            $what = 'Arbeitsstand'
        }
        if ($LASTEXITCODE -ne 0) { throw 'git-Dateiliste fehlgeschlagen' }
        $files = @($listing -split "`0" | ForEach-Object { $_.Trim("`n") } | Where-Object { $_ })
        foreach ($f in $files) {
            $path = Join-Path $base ($f -replace '/', '\')
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $missingFiles += $f; continue }
            foreach ($p in (Find-InFile $path)) { [void]$problems.Add("${f}:$p") }
        }
    }
    # Mit -File kommt eine Liste als eine Zeichenkette an: mehrere Zusatzdateien durch „;“ trennen
    $extra = @($ExtraFile | ForEach-Object { $_ -split ';' } | Where-Object { $_ })
    foreach ($x in $extra) {
        if (-not (Test-Path -LiteralPath $x -PathType Leaf)) { $missingFiles += $x; continue }
        foreach ($p in (Find-InFile $x)) { [void]$problems.Add("${x}:$p") }
    }
    foreach ($p in ($problems | Select-Object -First 300)) { Note "  - $p" }
    if ($problems.Count -gt 300) { Note ("  … und {0} weitere" -f ($problems.Count - 300)) }
    if (-not $refs) {
        Check ($files.Count -gt 100 -and $missingFiles.Count -eq 0) ("{0}: {1} verfolgte Dateien gelesen{2}" -f $what, $files.Count, $(if ($missingFiles) { ', fehlen: ' + (($missingFiles | Select-Object -First 5) -join ', ') } else { '' }))
    }
    elseif ($missingFiles) {
        Check $false ('Zusatzdateien fehlen: ' + (($missingFiles | Select-Object -First 5) -join ', '))
    }
    Check ($problems.Count -eq 0) ("Keine lokalen Pfade, Claude-Spuren oder persönlichen Kennungen ({0} Treffer{1})" -f $problems.Count, $(if ($extra) { ", mit $($extra.Count) Zusatzdatei(en)" } else { '' }))
}
finally {
    [Console]::OutputEncoding = $oldEncoding
    if ($work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($script:ok) { Write-Output 'PUBLISH_CHECK_OK'; exit 0 }
Write-Output 'PUBLISH_CHECK_FAILED'
exit 1
