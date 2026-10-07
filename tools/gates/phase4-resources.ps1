# Gate Phase 4: Oberflächentexte stehen in Ressourcendateien.
# (1) Keine festen Texte in XAML-Attributen (Text, Content, Header, ToolTip, Title, AutomationProperties.Name ...).
# (2) Jeder in XAML/C# verwendete Ressourcen-Schlüssel existiert in der passenden .resx.
# (3) Verdächtige deutsche Zeichenketten in C#-Dateien der App werden aufgelistet (Prüfung von Hand, Protokollzeilen erlaubt).
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$app = Join-Path $root 'src\HdrPilot.App'
$ok = $true

$attr = '(?<![\w.])(Text|Content|Header|ToolTip|Title|PlaceholderText|Description|Label|AutomationProperties\.Name|AutomationProperties\.HelpText)="(?<v>[^"]*)"'
$allowed = @('HdrPilot', 'HDR')
$xamlFiles = Get-ChildItem $app -Recurse -Filter *.xaml | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
foreach ($f in $xamlFiles) {
    $text = Get-Content $f.FullName -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($text, $attr)) {
        $v = $m.Groups['v'].Value
        if ($v -match '^\s*\{' -or $v -match '^(&#x[0-9A-Fa-f]+;|\s)*$' -or $v -notmatch '[A-Za-zÄÖÜäöüß]' -or $allowed -contains $v.Trim()) { continue }
        $line = ($text.Substring(0, $m.Index) -split "`n").Count
        Write-Output ("FESTER TEXT: {0}:{1}: {2}" -f $f.FullName.Substring($root.Length + 1), $line, $m.Value)
        $ok = $false
    }
}

# Schlüssel aus allen .resx lesen
$keys = @{}
foreach ($r in Get-ChildItem (Join-Path $app 'Resources') -Filter *.resx) {
    if ($r.Name -match '\.\w{2}(-\w+)?\.resx$') { continue } # Sprachvarianten
    [xml]$x = Get-Content $r.FullName -Raw -Encoding UTF8
    $cls = [IO.Path]::GetFileNameWithoutExtension($r.Name)
    $keys[$cls] = @($x.root.data | ForEach-Object { $_.name })
    Write-Output ("{0}: {1} Schlüssel" -f $cls, $keys[$cls].Count)
}

$uses = 0
$sources = Get-ChildItem $app -Recurse -Include *.xaml, *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
foreach ($f in $sources) {
    $text = Get-Content $f.FullName -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($text, '\b(?<cls>\w+Strings)\.(?<key>[A-Z]\w*)\b')) {
        $cls = $m.Groups['cls'].Value; $key = $m.Groups['key'].Value
        if (-not $keys.ContainsKey($cls)) { continue }
        if ($key -in @('ResourceManager', 'Culture')) { continue }
        $uses++
        if ($keys[$cls] -notcontains $key) { Write-Output "FEHLENDER SCHLÜSSEL: $cls.$key in $($f.Name)"; $ok = $false }
    }
}
Write-Output "Verwendungen von Ressourcen-Schlüsseln: $uses"

$suspect = 0
foreach ($f in Get-ChildItem $app -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $lines = Get-Content $f.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l -match '^\s*//' -or $l -match '^\s*///' -or $l -match '\.Log(Information|Warning|Error|Debug|Trace|Critical)\(|_logger\.|logger\.Log' -or $l -match 'Exception\(') { continue }
        foreach ($m in [regex]::Matches($l, '"([^"\\]|\\.)*[äöüÄÖÜß]([^"\\]|\\.)*"|"[A-ZÄÖÜ][a-zäöüß]+ [a-zäöüß]+ [^"]*"')) {
            Write-Output ("PRÜFEN: {0}:{1}: {2}" -f $f.FullName.Substring($root.Length + 1), ($i + 1), $m.Value)
            $suspect++
        }
    }
}
Write-Output "Verdächtige C#-Zeichenketten: $suspect (von Hand prüfen; erlaubt sind Protokoll- und Ausnahmetexte)"

if ($ok -and $uses -gt 0) { Write-Output 'PHASE4_RESOURCES_OK'; exit 0 }
exit 1
