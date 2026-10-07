# Gate G6.8: Das Tray-Menü bleibt nach einem echten Rechtsklick auf das Symbol offen und lässt sich bedienen.
# Anlass: Unter Windows 11 schloss es sich nach etwa 150 ms wieder (das nicht aktivierbare Menü-Popup wurde in den
# Vordergrund gezwungen, Windows holte das vorherige Fenster zurück; E82).
# Geprüft mit echter Maus und Tastatur (SendInput) an einer Testinstanz mit eigener Ablage:
#   1. Rechtsklick öffnet das Menü, nach 2 s ist es noch offen und HDRpilot im Vordergrund
#   2. Esc schließt es
#   3. ein Klick auf eine freie Stelle der Taskleiste (andere App) schließt es
#   4. ein Mausklick auf „Spieleliste öffnen“ öffnet das Hauptfenster
#   5. Tastatur: Pfeil nach oben wählt „HDRpilot beenden“, Eingabe beendet die Testinstanz
# Liegt das Symbol im Überlauf (Standard für neue Programme unter Windows 11), öffnet das Skript zuerst den Überlauf.
# Bewegt die Maus und klickt: nur am echten Desktop ausführen. Tasten gehen nur raus, wenn die Testinstanz im Vordergrund
# ist; geklickt wird nur auf das Symbol, den Überlauf-Pfeil, eine leere Taskleistenfläche und Einträge des eigenen Menüs.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class TrayInput
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct NOTIFYICONIDENTIFIER { public int cbSize; public IntPtr hWnd; public int uID; public Guid guidItem; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy, mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public short wVk, wScan; public int dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public int type; public INPUTUNION u; }
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);

    public static uint PidOf(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
    public static uint ForegroundPid() { return PidOf(GetForegroundWindow()); }

    public static List<IntPtr> VisibleWindowsOf(int pid)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { if (PidOf(h) == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    // Symbol eines NotifyIcon (Fenster der App, uID); liegt es im geschlossenen Überlauf, meldet Windows den Pfeil.
    public static bool FindIcon(int pid, int uid, out RECT rect)
    {
        rect = new RECT();
        var all = new List<IntPtr>();
        EnumWindows((h, l) => { if (PidOf(h) == pid) all.Add(h); return true; }, IntPtr.Zero);
        foreach (var h in all)
        {
            var id = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)), hWnd = h, uID = uid };
            if (Shell_NotifyIconGetRect(ref id, out rect) == 0) return true;
        }
        return false;
    }

    static INPUT Mouse(int flags) { var i = new INPUT { type = 0 }; i.u.mi.dwFlags = flags; return i; }
    static INPUT Key(short vk, bool up) { var i = new INPUT { type = 1 }; i.u.ki.wVk = vk; i.u.ki.dwFlags = up ? 2 : 0; return i; }
    static void Send(params INPUT[] inputs) { SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))); }
    public static void LeftClick() { Send(Mouse(0x0002), Mouse(0x0004)); }
    public static void RightClick() { Send(Mouse(0x0008), Mouse(0x0010)); }

    // Taste nur, wenn das Vordergrundfenster zum erwarteten Prozess gehört (nie in fremde Fenster tippen).
    public static bool KeyTo(int pid, short vk)
    {
        if (ForegroundPid() != pid) return false;
        Send(Key(vk, false), Key(vk, true));
        return true;
    }
}
'@
[void][TrayInput]::SetProcessDpiAwarenessContext([IntPtr]-4)
Add-Type -AssemblyName WindowsBase

$VK_ESCAPE = 0x1B; $VK_UP = 0x26; $VK_RETURN = 0x0D

function Center($r) { return @([int](($r.Left + $r.Right) / 2), [int](($r.Top + $r.Bottom) / 2)) }
function Click-At([int[]]$pt, [switch]$Right) {
    [void][TrayInput]::SetCursorPos($pt[0], $pt[1]); Start-Sleep -Milliseconds 120
    if ($Right) { [TrayInput]::RightClick() } else { [TrayInput]::LeftClick() }
}
function Taskbar-Rect { $r = New-Object TrayInput+RECT; [void][TrayInput]::GetWindowRect([TrayInput]::FindWindow('Shell_TrayWnd', $null), [ref]$r); return $r }
function Overflow-Open { $h = [TrayInput]::FindWindow('TopLevelWindowForOverflowXamlIsland', $null); return $h -ne [IntPtr]::Zero -and [TrayInput]::IsWindowVisible($h) }

# Fenster des Menüs: neues sichtbares Fenster der Testinstanz mit dem Menü (UI Automation, AutomationId TrayMenu).
function Find-MenuWindow([int]$processId) {
    $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'TrayMenu')
    foreach ($h in [TrayInput]::VisibleWindowsOf($processId)) {
        try {
            $el = [Windows.Automation.AutomationElement]::FromHandle($h)
            if ($el.Current.AutomationId -in 'MainWindow', 'SettingsWindow') { continue }
            if ($el.Current.AutomationId -eq 'TrayMenu' -or $el.FindFirst([Windows.Automation.TreeScope]::Children, $cond)) { return $h }
        } catch { }
    }
    return [IntPtr]::Zero
}
function Menu-Element([IntPtr]$hwnd) {
    try { return [Windows.Automation.AutomationElement]::FromHandle($hwnd) } catch { return $null } # schon geschlossen
}
function Wait-Menu([int]$processId, [bool]$open, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $h = Find-MenuWindow $processId
        if (($h -ne [IntPtr]::Zero) -eq $open) { return [pscustomobject]@{ Hwnd = $h; Ms = $sw.ElapsedMilliseconds } }
        Start-Sleep -Milliseconds 25
    }
    return $null
}

# Rechtsklick auf das Symbol; liegt es im Überlauf, zuerst den Überlauf öffnen.
function Open-Menu([int]$processId) {
    $r = New-Object TrayInput+RECT
    if (-not [TrayInput]::FindIcon($processId, 1, [ref]$r)) { throw 'Tray-Symbol der Testinstanz nicht gefunden.' }
    $bar = Taskbar-Rect
    if ($r.Top -ge $bar.Top -and -not (Overflow-Open)) {
        # Symbol oder Überlauf-Pfeil in der Taskleiste: Linksklick öffnet beim Pfeil den Überlauf, beim Symbol passiert nichts.
        Click-At (Center $r)
        Start-Sleep -Milliseconds 800
        if (Overflow-Open) { [void][TrayInput]::FindIcon($processId, 1, [ref]$r) }
    }
    Click-At (Center $r) -Right
    return Wait-Menu $processId $true 3000
}

# Freie Stelle der Taskleiste links vom Infobereich (UI Automation meldet dort nur die Taskleiste selbst).
function Find-FreeTaskbarSpot([int]$rightOf) {
    $bar = Taskbar-Rect
    $y = [int](($bar.Top + $bar.Bottom) / 2)
    for ($x = $rightOf - 60; $x -gt $bar.Left + 200; $x -= 24) {
        $el = [Windows.Automation.AutomationElement]::FromPoint((New-Object Windows.Point($x, $y)))
        if ($el -and $el.Current.ControlType -eq [Windows.Automation.ControlType]::Pane -and $el.Current.ProcessId -ne $PID) { return @($x, $y) }
    }
    return $null
}

$cfg = New-TestConfig 'phase6-traymenu'
$saved = New-Object TrayInput+POINT; [void][TrayInput]::GetCursorPos([ref]$saved)
try {
    $app = Start-App $cfg @('--minimized')
    $sw = [Diagnostics.Stopwatch]::StartNew(); $icon = New-Object TrayInput+RECT
    while (-not [TrayInput]::FindIcon($app.Id, 1, [ref]$icon) -and $sw.ElapsedMilliseconds -lt 20000) { Start-Sleep -Milliseconds 200 }
    Check ($sw.ElapsedMilliseconds -lt 20000) "Testinstanz (PID $($app.Id)) mit Tray-Symbol gestartet"
    Start-Sleep -Milliseconds 1500

    # 1. Menü bleibt offen
    $m = Open-Menu $app.Id
    Check ($null -ne $m) ("Rechtsklick öffnet das Menü{0}" -f $(if ($m) { " ($($m.Ms) ms)" } else { '' }))
    if ($m) {
        Start-Sleep -Milliseconds 2000
        Check ([TrayInput]::IsWindowVisible($m.Hwnd)) 'Menü nach 2 s noch offen'
        Check ([TrayInput]::ForegroundPid() -eq $app.Id) 'Testinstanz im Vordergrund, solange das Menü offen ist'

        # 1b. Symbol und Tooltip ändern sich bei offenem Menü (wie beim Countdown des Neustart-Puffers): Pause von außen
        Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--pause') -Wait
        Start-Sleep -Milliseconds 1500
        Check ([TrayInput]::IsWindowVisible($m.Hwnd) -and [TrayInput]::ForegroundPid() -eq $app.Id) 'Symbolwechsel (Pause von außen) bei offenem Menü: Menü bleibt offen'
        Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--resume') -Wait
        Start-Sleep -Milliseconds 500

        # 2. Esc
        Check ([TrayInput]::KeyTo($app.Id, $VK_ESCAPE)) 'Esc an die Testinstanz gesendet'
        Check ($null -ne (Wait-Menu $app.Id $false 1500)) 'Esc schließt das Menü'
    }

    # 3. Klick daneben (in die Taskleiste, also in eine andere App)
    $m = Open-Menu $app.Id
    Check ($null -ne $m) 'Menü erneut geöffnet'
    if ($m) {
        Start-Sleep -Milliseconds 400
        $r = New-Object TrayInput+RECT; [void][TrayInput]::GetWindowRect($m.Hwnd, [ref]$r)
        $bar = Taskbar-Rect
        $spot = Find-FreeTaskbarSpot ([Math]::Min($r.Left, $icon.Left))
        Check ($null -ne $spot) 'freie Stelle der Taskleiste gefunden'
        if ($spot) {
            Click-At $spot
            Check ($null -ne (Wait-Menu $app.Id $false 1500)) "Klick in die Taskleiste ($($spot[0]),$($spot[1])) schließt das Menü"
        }
    }

    # 4. Eintrag per Maus
    if (Overflow-Open) { Start-Sleep -Milliseconds 300 }
    $m = Open-Menu $app.Id
    Check ($null -ne $m) 'Menü für den Mausklick geöffnet'
    if ($m) {
        Start-Sleep -Milliseconds 400
        $menuEl = Menu-Element $m.Hwnd
        $item = if ($menuEl) { Find-Id $menuEl 'TrayOpen' 2000 } else { $null }
        Check ($null -ne $item) 'Eintrag „Spieleliste öffnen“ gefunden'
        if ($item) {
            $b = $item.Current.BoundingRectangle
            Click-At @([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2))
            Check ($null -ne (Find-Window 'MainWindow' $app.Id 8000)) 'Mausklick auf „Spieleliste öffnen“ öffnet das Hauptfenster'
            Check ($null -ne (Wait-Menu $app.Id $false 1500)) 'Menü danach geschlossen'
        }
    }

    # 5. Tastatur
    $m = Open-Menu $app.Id
    Check ($null -ne $m) 'Menü für die Tastatur geöffnet'
    if ($m) {
        Start-Sleep -Milliseconds 400
        Check ([TrayInput]::KeyTo($app.Id, $VK_UP)) 'Pfeil nach oben an die Testinstanz gesendet'
        Start-Sleep -Milliseconds 300
        # Welcher Eintrag den Tastaturfokus hat, meldet WPF über UI Automation (HasKeyboardFocus des Eintrags).
        $menuEl = Menu-Element $m.Hwnd
        $exitItem = if ($menuEl) { Find-Id $menuEl 'TrayExit' 2000 } else { $null }
        $exitFocused = $null -ne $exitItem -and $exitItem.Current.HasKeyboardFocus
        Check $exitFocused ('Pfeil nach oben wählt „HDRpilot beenden“ (Tastaturfokus: ' + $exitFocused + ')')
        if ($exitFocused) {
            Check ([TrayInput]::KeyTo($app.Id, $VK_RETURN)) 'Eingabe an die Testinstanz gesendet'
            Check ($app.WaitForExit(10000)) 'Eingabe beendet die Testinstanz über das Menü'
        } else {
            [void][TrayInput]::KeyTo($app.Id, $VK_ESCAPE)
        }
    }
}
catch {
    Note "FEHLER Abbruch: $($_.Exception.Message)" # etwa das Menü schloss sich, während es gelesen wurde
    $ok = $false
}
finally {
    [void][TrayInput]::SetCursorPos($saved.X, $saved.Y)
    Stop-Started
}

if ($ok) { Write-Output 'PHASE6_TRAYMENU_OK'; exit 0 }
Write-Output 'PHASE6_TRAYMENU_FAILED'
exit 1
