# Gate Phase 1: "hdrpilot hdr status" listet jeden aktiven Bildschirm mit Name, HDR-fähig ja/nein, HDR an/aus.
# Gegenprobe: Anzahl aktiver Bildschirme laut .NET (System.Windows.Forms.Screen) und HDREnabled aus dem MonitorDataStore.
param([string]$Exe = (Join-Path $PSScriptRoot '..\..\src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
# Eigene Testablage mit deutscher Sprache (E95): Ohne --config-dir läse hdr die Sprache der Ausgaben aus der echten Ablage
# oder nähme ohne sie die Anzeigesprache von Windows, und die deutschen Feldnamen unten fehlten. hdr liest daraus nur die
# Sprache und schreibt nichts hinein (geprüft).
. (Join-Path $PSScriptRoot 'TestConfigSeed.ps1')
$cfgDir = Join-Path ([IO.Path]::GetTempPath()) ('HDRpilot-Gate1-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
Initialize-TestConfig $cfgDir 'de'
$seed = [IO.File]::ReadAllText((Join-Path $cfgDir 'config.json'))
try {
    $text = & $Exe hdr status --config-dir $cfgDir | Out-String
    $codeText = $LASTEXITCODE
    $rows = @(& $Exe hdr status --json --config-dir $cfgDir | Out-String | ConvertFrom-Json)
    $codeJson = $LASTEXITCODE
    $entries = @(Get-ChildItem $cfgDir -Force -Recurse).Count
    $untouched = ($entries -eq 1) -and ([IO.File]::ReadAllText((Join-Path $cfgDir 'config.json')) -eq $seed)
} finally {
    Remove-Item $cfgDir -Recurse -Force -ErrorAction SilentlyContinue
}
$screens = [System.Windows.Forms.Screen]::AllScreens.Count
Write-Output $text.TrimEnd()
if (-not $untouched) { Write-Output "FEHLER: hdr status hat die Testablage verändert ($entries Einträge)" }
$ok = ($codeText -eq 0) -and ($codeJson -eq 0) -and ($rows.Count -eq $screens) -and ($rows.Count -ge 1) -and $untouched
foreach ($r in $rows) {
    $line = ($text -split "`n") | Where-Object { $_ -match [regex]::Escape($r.name) } | Select-Object -First 1
    $hasFields = $text -match 'HDR-fähig: (ja|nein)' -and $text -match 'HDR: (an|aus)'
    $edid = ($r.monitorDevicePath -split '#')[1]
    $reg = Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MonitorDataStore' |
        Where-Object { $_.PSChildName -like "$edid*" } |
        ForEach-Object { (Get-ItemProperty $_.PSPath).HDREnabled } | Where-Object { $null -ne $_ }
    $regState = if (@($reg).Count -eq 1) { [bool]@($reg)[0] } else { $null }
    $regOk = ($null -eq $regState) -or ($regState -eq [bool]$r.hdrEnabled)
    Write-Output ("Bildschirm '{0}': name im Text={1}, Felder={2}, hdrSupported={3}, hdrEnabled={4}, Registry HDREnabled={5}" -f $r.name, [bool]$line, $hasFields, $r.hdrSupported, $r.hdrEnabled, $regState)
    $ok = $ok -and [bool]$line -and $hasFields -and $regOk -and ($null -ne $r.hdrSupported) -and ($null -ne $r.hdrEnabled)
}
Write-Output "aktive Bildschirme: hdrpilot=$($rows.Count), Windows=$screens"
if ($ok) { Write-Output 'PHASE1_STATUS_OK'; exit 0 }
exit 1
