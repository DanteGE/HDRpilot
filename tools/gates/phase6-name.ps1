# Gate G6.4: Produktname "HDRpilot" überall sichtbar, keine sichtbaren Reste "HdrPilot".
# Geprüft: EXE-Name und Produktname des Builds, alle resx-Werte, Texte in XAML, Zeichenketten im C#-Code (ohne interne
# Namen: Einzelinstanz, Ereignisse, Fensterklassen, Verschlüsselungs-Kennung, Symboldatei), README, Kommandozeilen-
# Hilfe, Kopf der Lizenzhinweise. Gegenprobe: die Suche findet einen eingebauten Rest.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ok = $true
function Note([string]$text) { Write-Output ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text) }
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

# Interne Namen, die bewusst bleiben (Groß-/Kleinschreibung zählt bei Kernel-Objekten; E-Entscheidung Umbenennung)
$internal = 'HdrPilot\.(Commands|Instance|Prepared|Config|TrayMenuOwner|PCGamingWiki|Core|App|Cli|Tests)\b|HdrPilot\\\.Prepared|HdrPilot\.ico|HdrPilot\.slnx'

function Find-Old([string]$text) {
    $hits = @()
    foreach ($m in [regex]::Matches($text, 'HdrPilot')) {
        $context = $text.Substring($m.Index, [Math]::Min(40, $text.Length - $m.Index))
        if ($context -notmatch "^($internal)") { $hits += $context }
    }
    return ,$hits
}

# 1. EXE und Produktname
$exe = Join-Path $root 'src\HdrPilot.App\bin\Debug\net10.0-windows\HDRpilot.exe'
$file = Get-Item $exe -ErrorAction SilentlyContinue
Check ($null -ne $file -and $file.Name -ceq 'HDRpilot.exe') "EXE heißt HDRpilot.exe ($($file.Name))"
Check ($file -and $file.VersionInfo.ProductName -ceq 'HDRpilot') "Produktname '$($file.VersionInfo.ProductName)'"

# 2. Ressourcen: nur die Werte
$resxHits = @()
foreach ($f in Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.resx | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    foreach ($d in ([xml](Get-Content $f.FullName -Raw -Encoding UTF8)).root.data) { foreach ($h in (Find-Old $d.value)) { $resxHits += "$($f.Name):$($d.name): $h" } }
}
Check ($resxHits.Count -eq 0) ("resx-Werte ohne 'HdrPilot' ({0} Reste) {1}" -f $resxHits.Count, ($resxHits | Select-Object -First 3) -join ' | ')

# 3. XAML: Textattribute
$xamlHits = @()
foreach ($f in Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.xaml | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $text = Get-Content $f.FullName -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($text, '(?<![\w:.])(Text|Content|Header|ToolTip|Title|PlaceholderText|Description|Label|AutomationProperties\.Name|AutomationProperties\.HelpText)="([^"]*)"')) {
        foreach ($h in (Find-Old $m.Groups[2].Value)) { $xamlHits += "$($f.Name): $h" }
    }
}
Check ($xamlHits.Count -eq 0) ("XAML-Texte ohne 'HdrPilot' ({0} Reste)" -f $xamlHits.Count)

# 4. C#-Zeichenketten außerhalb von Kommentaren
$csHits = @()
foreach ($f in Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $n = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
        $n++
        $t = $line.TrimStart()
        if ($t.StartsWith('//') -or $t.StartsWith('using ') -or $t.StartsWith('namespace ')) { continue }
        $code = ($line -split '//')[0]
        foreach ($m in [regex]::Matches($line, '"(?:[^"\\]|\\.)*"')) { foreach ($h in (Find-Old $m.Value)) { $csHits += "$($f.Name):${n}: $h" } }
        if (-not $line.Contains('"') -and $code -match 'HdrPilot' -and $code -notmatch $internal -and $code -notmatch '\b(class|record|struct|interface|enum)\b|HdrPilot\.\w+\.') {
            foreach ($h in (Find-Old $code)) { $csHits += "${f}:${n} (Rohtext): $h" }
        }
    }
}
Check ($csHits.Count -eq 0) ("C#-Zeichenketten ohne 'HdrPilot' ({0} Reste) {1}" -f $csHits.Count, ($csHits | Select-Object -First 3) -join ' | ')

# 5. README (erlaubt: Projektdatei, Hinweis auf Namensräume und auf die alten Ordner)
$readme = [IO.File]::ReadAllText((Join-Path $root 'README.md'), [Text.Encoding]::UTF8)
$readmeClean = $readme.Replace('HdrPilot.slnx', '').Replace('`HdrPilot.*`', '').Replace('Ordner `HdrPilot`', '')
$readmeHits = Find-Old $readmeClean
Check ($readmeHits.Count -eq 0) ("README ohne 'HdrPilot' ({0} Reste) {1}" -f $readmeHits.Count, ($readmeHits | Select-Object -First 3) -join ' | ')
Check ($readme -cmatch '^# HDRpilot') 'README-Überschrift HDRpilot'

# 6. Kommandozeilen-Hilfe und Lizenzkopf
$cli = Join-Path $root 'src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'
$help = & $cli --help | Out-String
Check ($help -cmatch 'HDRpilot' -and (Find-Old $help).Count -eq 0) 'Kommandozeilen-Hilfe nennt HDRpilot'
$publish = [IO.File]::ReadAllText((Join-Path $root 'tools\publish.ps1'), [Text.Encoding]::UTF8)
Check ($publish -cmatch 'HDRpilot – Lizenzhinweise' -and $publish -cmatch "Exe = 'HDRpilot\.exe'") 'Auslieferung: HDRpilot.exe und Lizenzkopf HDRpilot'
# Ganzer Text der Lizenzhinweise: im Skript und, falls schon veröffentlicht, in der ausgelieferten Datei
$noticeBlock = [regex]::Match($publish, '(?s)\$notice = @\((.*?)\r?\n\[IO\.File\]::WriteAllText').Groups[1].Value
$noticeHits = @()
foreach ($h in (Find-Old $noticeBlock)) { $noticeHits += "publish.ps1: $h" }
$shipped = Join-Path $root 'artifacts\publish\HDRpilot\THIRD-PARTY-NOTICES.txt'
if (Test-Path $shipped) { foreach ($h in (Find-Old ([IO.File]::ReadAllText($shipped, [Text.Encoding]::UTF8)))) { $noticeHits += "ausgeliefert: $h" } }
Check ($noticeBlock.Length -gt 1000 -and $noticeHits.Count -eq 0) ("Lizenzhinweise ohne 'HdrPilot' ({0} Reste{1}) {2}" -f $noticeHits.Count, $(if (Test-Path $shipped) { ', inkl. ausgelieferter Datei' } else { '' }), (($noticeHits | Select-Object -First 3) -join ' | '))

# 7. Gegenprobe
Check ((Find-Old 'Willkommen bei HdrPilot, Local\HdrPilot.Instance.x').Count -eq 1) 'Gegenprobe: Rest wird gefunden, interner Name nicht'

if ($ok) { Write-Output 'PHASE6_NAME_OK'; exit 0 }
exit 1
