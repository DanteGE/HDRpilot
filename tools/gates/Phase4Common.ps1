# Gemeinsame Hilfen für die Phase-4-Gates (wird per Dot-Sourcing geladen).
$ErrorActionPreference = 'Stop'
$script:root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$script:AppExe = Join-Path $root 'src\HdrPilot.App\bin\Debug\net10.0-windows\HdrPilot.exe'
$script:CliExe = Join-Path $root 'src\HdrPilot.Cli\bin\Debug\net10.0-windows\hdrpilot.exe'
$script:Started = New-Object System.Collections.ArrayList

function Note([string]$text) { Write-Output ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text) }

function New-TestConfig([string]$name, [switch]$Import) {
    $dir = Join-Path $root "tools\out\$name"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    if ($Import) { & $CliExe games import --config-dir $dir | Out-Null }
    return $dir
}

function New-StandInGame([string]$cfg, [string]$name, [bool]$hdr) {
    $standIn = Join-Path $root 'tools\out\phase3\StandIn.exe'
    if (-not (Test-Path $standIn)) {
        New-Item -ItemType Directory -Force (Split-Path $standIn) | Out-Null
        Add-Type -OutputAssembly $standIn -OutputType WindowsApplication -TypeDefinition @'
public static class StandIn {
    public static int Main(string[] args) {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 3600;
        System.Threading.Thread.Sleep(seconds * 1000);
        return 0;
    }
}
'@
    }
    $dir = Join-Path $cfg "games\$name"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $exe = Join-Path $dir (($name -replace '\s', '') + '.exe')
    Copy-Item $standIn $exe
    $key = (& $CliExe games add --config-dir $cfg --path $dir --name $name --hdr $(if ($hdr) { 'on' } else { 'off' }) | Select-Object -Last 1).Trim()
    return [pscustomobject]@{ Key = $key; Exe = $exe; Dir = $dir; ProcessName = [IO.Path]::GetFileNameWithoutExtension($exe) }
}

function Start-App([string]$cfg, [string[]]$extra = @()) {
    $p = Start-Process $AppExe -ArgumentList (@('--config-dir', "`"$cfg`"") + $extra) -PassThru
    [void]$Started.Add($p)
    return $p
}

function Wait-Log([string]$cfg, [string]$pattern, [int]$timeoutMs = 15000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $text = Get-ChildItem (Join-Path $cfg 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object {
            $fs = New-Object IO.FileStream($_.FullName, 'Open', 'Read', 'ReadWrite, Delete')
            $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
            $sr.ReadToEnd(); $sr.Dispose()
        } | Out-String
        if ($text -match $pattern) { return $text }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

function Get-PrimaryRegistryKey {
    $primary = @(& $CliExe hdr status --json | Out-String | ConvertFrom-Json) | ForEach-Object { $_ } | Where-Object { $_.primary } | Select-Object -First 1
    $edid = ($primary.monitorDevicePath -split '#')[1]
    $key = Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MonitorDataStore' |
        Where-Object { $_.PSChildName -like "$edid*" -and $null -ne (Get-ItemProperty $_.PSPath).HDREnabled } | Select-Object -First 1
    return [pscustomobject]@{ Primary = $primary; Key = $key }
}

function Test-HdrOn($reg) { [int](Get-ItemProperty $reg.Key.PSPath).HDREnabled -eq 1 }

function Wait-Hdr($reg, [bool]$target, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if ((Test-HdrOn $reg) -eq $target) { return $sw.ElapsedMilliseconds }
        Start-Sleep -Milliseconds 20
    }
    return -1
}

function Stop-Started {
    foreach ($p in $Started) { try { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } } catch {} }
}

# --- UI Automation ---
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$script:UiaRoot = [Windows.Automation.AutomationElement]::RootElement

# Nur Fenster der eigenen Testinstanz: Läuft daneben das echte HdrPilot des Nutzers mit offenem Fenster, darf das
# Skript es nie bedienen (ist am 2026-10-07 passiert und hat echte Einstellungen geändert).
function Find-Window([string]$automationId, [int]$processId, [int]$timeoutMs = 20000) {
    if ($processId -le 0) { throw "Find-Window braucht die Prozess-ID der Testinstanz ($automationId)." }
    $cond = New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)),
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $w = $UiaRoot.FindFirst([Windows.Automation.TreeScope]::Children, $cond)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Find-Id($parent, [string]$automationId, [int]$timeoutMs = 5000) {
    $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $e = $parent.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { return $e }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

function Invoke-El($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Toggle-El($el) { $p = $el.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern); $p.Toggle(); Start-Sleep -Milliseconds 300; return $p.Current.ToggleState }
function Get-Toggle($el) { $el.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState }
function Set-Text($el, [string]$value) { $el.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($value) }
function Get-Text($el) {
    $vp = $null
    if ($el.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) { return $vp.Current.Value }
    return $el.Current.Name
}
function Set-Range($el, [double]$value) {
    $rp = $null
    if ($el.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$rp)) { $rp.SetValue($value); return }
    Set-Text $el ([string]$value)
}
function Get-Range($el) {
    $rp = $null
    if ($el.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$rp)) { return $rp.Current.Value }
    return [double](Get-Text $el)
}
function Get-ListItems($list) {
    $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)
    return @($list.FindAll([Windows.Automation.TreeScope]::Children, $cond))
}
function Select-El($el) { $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
function Read-Config([string]$cfg) {
    $fs = New-Object IO.FileStream((Join-Path $cfg 'config.json'), 'Open', 'Read', 'ReadWrite, Delete')
    $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8); $t = $sr.ReadToEnd(); $sr.Dispose()
    return $t | ConvertFrom-Json
}
function Find-GameConfig($config, [string]$key) {
    $parts = $key -split ':', 2
    return @($config.games) | ForEach-Object { $_ } | Where-Object { $_.source -eq $parts[0] -and $_.sourceId -eq $parts[1] } | Select-Object -First 1
}
