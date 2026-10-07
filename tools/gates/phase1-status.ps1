# Gate Phase 1: "hdrpilot hdr status" listet jeden aktiven Bildschirm mit Name, HDR-fähig ja/nein, HDR an/aus.
# Gegenprobe: Anzahl aktiver Bildschirme laut .NET (System.Windows.Forms.Screen) und HDREnabled aus dem MonitorDataStore.
param([string]$Exe = (Join-Path $PSScriptRoot '..\..\src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$text = & $Exe hdr status | Out-String
$codeText = $LASTEXITCODE
$rows = @(& $Exe hdr status --json | Out-String | ConvertFrom-Json)
$codeJson = $LASTEXITCODE
$screens = [System.Windows.Forms.Screen]::AllScreens.Count
Write-Output $text.TrimEnd()
$ok = ($codeText -eq 0) -and ($codeJson -eq 0) -and ($rows.Count -eq $screens) -and ($rows.Count -ge 1)
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
