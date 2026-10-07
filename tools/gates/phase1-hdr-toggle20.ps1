# Gate Phase 1: 20 HDR-Wechsel in Folge am echten Bildschirm.
# Prüft nach jedem Wechsel (1) Exit-Code 0, (2) "hdr status --json" zeigt den Zielzustand und
# (3) unabhängig davon den Wert HDREnabled, den die Windows-Einstellungen im MonitorDataStore ablegen.
# Stellt am Ende den Ausgangszustand wieder her. Gibt PHASE1_TOGGLE20_OK nur aus, wenn alles stimmt.
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'),
    [int]$Count = 20
)
$ErrorActionPreference = 'Stop'

function Get-Status {
    $json = & $Exe hdr status --json
    if ($LASTEXITCODE -ne 0) { throw "hdr status Exit-Code $LASTEXITCODE" }
    $rows = @($json | Out-String | ConvertFrom-Json)
    $primary = $rows | Where-Object { $_.primary } | Select-Object -First 1
    if (-not $primary) { $primary = $rows[0] }
    return $primary
}

function Get-RegistryHdr([string]$devicePath) {
    # monitorDevicePath: \\?\DISPLAY#MSI3DD0#... -> EDID-Kennung MSI3DD0
    $edid = ($devicePath -split '#')[1]
    $keys = Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MonitorDataStore' |
        Where-Object { $_.PSChildName -like "$edid*" }
    $values = foreach ($k in $keys) { $p = Get-ItemProperty $k.PSPath; if ($null -ne $p.HDREnabled) { [int]$p.HDREnabled } }
    $values = @($values)
    if ($values.Count -ne 1) { return $null }   # nicht eindeutig: keine unabhängige Aussage möglich
    return $values[0]
}

$start = Get-Status
if (-not $start.hdrSupported) { Write-Output "ABBRUCH: Hauptbildschirm meldet kein HDR (Standby?)"; exit 3 }
$initial = [bool]$start.hdrEnabled
$path = $start.monitorDevicePath
Write-Output ("Start: {0}, HDR {1}, Modus {2}, Registry HDREnabled={3}" -f $start.name, $initial, $start.activeColorMode, (Get-RegistryHdr $path))

$failures = 0
$times = @()
$target = -not $initial
for ($i = 1; $i -le $Count; $i++) {
    $verb = if ($target) { 'on' } else { 'off' }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $out = & $Exe hdr $verb 2>&1
    $code = $LASTEXITCODE
    $sw.Stop()
    $times += $sw.ElapsedMilliseconds
    $s = Get-Status
    $reg = Get-RegistryHdr $path
    $modeOk = if ($target) { $s.activeColorMode -eq 'Hdr' } else { $s.activeColorMode -ne 'Hdr' }
    $regOk = ($null -eq $reg) -or ([bool]$reg -eq $target)
    $ok = ($code -eq 0) -and ([bool]$s.hdrEnabled -eq $target) -and $modeOk -and $regOk -and ($null -ne $reg)
    if (-not $ok) { $failures++ }
    Write-Output ("{0,2}. hdr {1,-3} exit={2} {3,4} ms | status hdrEnabled={4} modus={5} | Registry HDREnabled={6} | {7}" -f `
        $i, $verb, $code, $sw.ElapsedMilliseconds, $s.hdrEnabled, $s.activeColorMode, $reg, $(if ($ok) { 'ok' } else { "FEHLER: $out" }))
    $target = -not $target
}

# Ausgangszustand wiederherstellen
$final = Get-Status
if ([bool]$final.hdrEnabled -ne $initial) {
    & $Exe hdr $(if ($initial) { 'on' } else { 'off' }) | Out-Null
    $final = Get-Status
}
Write-Output ("Ende: HDR {0}, Modus {1} (Ausgangszustand {2})" -f $final.hdrEnabled, $final.activeColorMode, $initial)
Write-Output ("Dauer je Wechsel inkl. Prozessstart: min {0} ms, max {1} ms, Mittel {2:N0} ms" -f ($times | Measure-Object -Minimum).Minimum, ($times | Measure-Object -Maximum).Maximum, ($times | Measure-Object -Average).Average)

if ($failures -eq 0 -and [bool]$final.hdrEnabled -eq $initial) {
    Write-Output "PHASE1_TOGGLE20_OK ($Count Wechsel ohne Fehler)"
    exit 0
}
Write-Output "PHASE1_TOGGLE20_FEHLGESCHLAGEN ($failures Fehler)"
exit 1
