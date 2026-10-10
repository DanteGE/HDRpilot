# Gate G6.8: Das Tray-Menü bleibt nach einem echten Rechtsklick auf das Symbol offen und lässt sich bedienen.
# Anlass: Unter Windows 11 schloss es sich nach etwa 150 ms wieder (das nicht aktivierbare Menü-Popup wurde in den
# Vordergrund gezwungen, Windows holte das vorherige Fenster zurück; E82).
# Geprüft mit echter Maus und Tastatur (SendInput) an einer Testinstanz mit eigener Ablage. Vor jedem Rechtsklick ist ein
# anderes Programm aktiv (ein eigenes Hilfsfenster dieses Skripts), wie beim Nutzer:
#   1. Rechtsklick öffnet das Menü, nach 2 s ist es noch offen und HDRpilot im Vordergrund
#   2. Symbol und Tooltip ändern sich bei offenem Menü (Pause von außen, wie der Countdown des Neustart-Puffers)
#   3. Esc schließt es und gibt den Vordergrund an die Taskleiste zurück
#   4. Alt+F4 (WM_CLOSE) an das unsichtbare Hilfsfenster zerstört es nicht, das nächste Menü bleibt offen
#   5. ein Klick in ein anderes Programm (das Hilfsfenster des Skripts) schließt es
#   6. ein Mausklick auf „Spieleliste öffnen“ öffnet das Hauptfenster
#   7. Tastatur: Die Versionszeile unter dem Kopf zeigt die Dateiversion der EXE und ist nicht aktiv (G8.1). Pfeil nach
#      oben wählt „HDRpilot beenden“, nie die Versionszeile; Eingabe beendet die Testinstanz
# Liegt das Symbol im Überlauf (Standard für neue Programme unter Windows 11), öffnet das Skript zuerst den Überlauf.
# Bewegt die Maus und klickt: nur am echten Desktop ausführen. Vor jedem Klick prüft es das Fenster unter dem Zeiger
# (nur Taskleiste, Überlauf, Testinstanz oder das eigene Hilfsfenster) und bricht sonst ab; Tasten gehen nur raus, wenn
# die Testinstanz im Vordergrund ist.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, WindowsBase
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
    // Oberstes Fenster einer Klasse (Titel egal; aus PowerShell käme $null als "" an und suchte nach leerem Titel).
    public static IntPtr FindTop(string cls) { return FindWindow(cls, null); }
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);

    public static bool PerMonitorAware() { return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) == 2; }
    public static uint PidOf(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
    public static uint ForegroundPid() { return PidOf(GetForegroundWindow()); }
    public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static string ForegroundClass() { return ClassOf(GetForegroundWindow()); }

    // Sichtbar und nicht von DWM verborgen (der Überlauf von Windows 11 bleibt nach dem Schließen teils „sichtbar“).
    public static bool Shown(IntPtr h)
    {
        if (h == IntPtr.Zero || !IsWindowVisible(h)) return false;
        int cloaked;
        return DwmGetWindowAttribute(h, 14, out cloaked, 4) != 0 || cloaked == 0;
    }

    public static List<IntPtr> VisibleWindowsOf(int pid)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { if (PidOf(h) == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    public static IntPtr FindWindowOf(int pid, string title)
    {
        var found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            if (PidOf(h) != pid) return true;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            if (sb.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
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

    // Oberstes Fenster unter einem Punkt: Klasse und Prozess.
    public static IntPtr RootAt(int x, int y) { return GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2); }

    static INPUT Mouse(int flags, int dx, int dy) { var i = new INPUT { type = 0 }; i.u.mi.dwFlags = flags; i.u.mi.dx = dx; i.u.mi.dy = dy; return i; }
    static INPUT Key(short vk, bool up) { var i = new INPUT { type = 1 }; i.u.ki.wVk = vk; i.u.ki.dwFlags = up ? 2 : 0; return i; }
    static void Send(params INPUT[] inputs) { SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))); }

    // Klick nur, wenn das oberste Fenster unter dem Punkt erlaubt ist (Prozess oder Fensterklasse). Bewegung und Drücken
    // in einem Aufruf (keine Lücke, in der der Nutzer die Maus woanders hinschiebt), Taste etwa 70 ms gehalten.
    // Rückgabe: null bei Erfolg, sonst der Grund für den Abbruch.
    public static string ClickAt(int x, int y, bool right, int[] allowedPids, string[] allowedClasses)
    {
        var root = RootAt(x, y);
        var pid = (int)PidOf(root);
        var cls = ClassOf(root);
        if (Array.IndexOf(allowedPids, pid) < 0 && Array.IndexOf(allowedClasses, cls) < 0)
            return string.Format("unter ({0},{1}) liegt ein fremdes Fenster: {2} (PID {3})", x, y, cls, pid);
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        int ax = (int)((x - vx) * 65535L / Math.Max(1, vw - 1)), ay = (int)((y - vy) * 65535L / Math.Max(1, vh - 1));
        const int move = 0x0001 | 0x8000 | 0x4000;
        Send(Mouse(move, ax, ay), Mouse(right ? 0x0008 : 0x0002, 0, 0));
        Thread.Sleep(70);
        Send(Mouse(right ? 0x0010 : 0x0004, 0, 0));
        return null;
    }

    // Taste nur, wenn das Vordergrundfenster zum erwarteten Prozess gehört (nie in fremde Fenster tippen).
    public static bool KeyTo(int pid, short vk)
    {
        if (ForegroundPid() != pid) return false;
        Send(Key(vk, false), Key(vk, true));
        return true;
    }
}
'@
[void][TrayInput]::SetThreadDpiAwarenessContext([IntPtr]-4) # pro Thread: PowerShell legt die Prozess-Einstellung selbst fest
if (-not [TrayInput]::PerMonitorAware()) { Write-Output 'ABBRUCH: Skript läuft nicht DPI-bewusst pro Bildschirm (Koordinaten wären verschoben).'; Write-Output 'PHASE6_TRAYMENU_FAILED'; exit 3 }

$VK_ESCAPE = 0x1B; $VK_UP = 0x26; $VK_RETURN = 0x0D; $WM_CLOSE = 0x0010
$ShellClasses = @('Shell_TrayWnd', 'TopLevelWindowForOverflowXamlIsland')

function Center($r) { return @([int](($r.Left + $r.Right) / 2), [int](($r.Top + $r.Bottom) / 2)) }
function Click-At([int[]]$pt, [int[]]$pids, [switch]$Right) {
    $why = [TrayInput]::ClickAt($pt[0], $pt[1], [bool]$Right, $pids, $ShellClasses)
    if ($why) { throw "Klick abgebrochen: $why" }
}
function Taskbar-Rect { $r = New-Object TrayInput+RECT; [void][TrayInput]::GetWindowRect([TrayInput]::FindTop('Shell_TrayWnd'), [ref]$r); return $r }
function Overflow-Open { return [TrayInput]::Shown([TrayInput]::FindTop('TopLevelWindowForOverflowXamlIsland')) }
function Pump([int]$ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } }

# Eigenes Hilfsfenster als „anderes Programm“ im Vordergrund (beim Nutzer war es der Browser), per echtem Klick aktiviert.
$decoy = New-Object System.Windows.Forms.Form
$decoy.Text = 'G6.8 Hilfsfenster'; $decoy.TopMost = $true; $decoy.ShowInTaskbar = $false; $decoy.FormBorderStyle = 'FixedToolWindow'
$decoy.StartPosition = 'Manual'; $decoy.Size = New-Object System.Drawing.Size(260, 90)
$bar0 = Taskbar-Rect
$decoy.Location = New-Object System.Drawing.Point(($bar0.Left + 80), ($bar0.Top - 420))
function Activate-Decoy {
    if (-not $decoy.Visible) { $decoy.Show(); Pump 300 }
    $r = New-Object TrayInput+RECT; [void][TrayInput]::GetWindowRect($decoy.Handle, [ref]$r)
    Click-At @([int](($r.Left + $r.Right) / 2), [int]($r.Top + 60)) @($PID)
    Pump 300
    return [TrayInput]::ForegroundPid() -eq $PID
}

# Fenster des Menüs: sichtbares Fenster der Testinstanz mit dem Menü (UI Automation, AutomationId TrayMenu).
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
        Pump 25
    }
    return $null
}

# Rechtsklick auf das Symbol, vorher ist das Hilfsfenster (anderes Programm) aktiv. Liegt das Symbol im geschlossenen
# Überlauf, meldet Windows den Pfeil. Ob Pfeil oder Symbol, zeigt ein Linksklick: beim Pfeil öffnet sich der Überlauf (dann
# dort rechts klicken), beim eigenen Symbol passiert nichts (HDRpilot reagiert nur auf Doppelklick); dann das Hilfsfenster
# wieder aktivieren, damit der Rechtsklick wie beim Nutzer aus einem anderen Programm heraus kommt.
function Open-Menu([int]$processId) {
    if (-not (Activate-Decoy)) { throw 'Hilfsfenster ließ sich nicht in den Vordergrund klicken.' }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ((Overflow-Open) -and $sw.ElapsedMilliseconds -lt 1500) { Pump 50 } # der Klick ins Hilfsfenster schließt den Überlauf
    $r = New-Object TrayInput+RECT
    if (-not [TrayInput]::FindIcon($processId, 1, [ref]$r)) { throw 'Tray-Symbol der Testinstanz nicht gefunden.' }
    $c = Center $r
    if (Overflow-Open) {
        # Überlauf blieb offen: das Symbol liegt sichtbar darin
        $script:IconPlace = 'Überlauf (war offen)'
        Click-At $c @() -Right
        return Wait-Menu $processId $true 3000
    }
    Click-At $c @()
    Pump 900
    if (Overflow-Open) {
        $before = $r
        [void][TrayInput]::FindIcon($processId, 1, [ref]$r)
        if ($r.Left -eq $before.Left -and $r.Top -eq $before.Top) { throw 'Symbol im Überlauf nicht gefunden.' }
        $c = Center $r
        $script:IconPlace = 'Überlauf'
    } else {
        if (-not (Activate-Decoy)) { throw 'Hilfsfenster ließ sich nicht wieder in den Vordergrund klicken.' }
        $script:IconPlace = 'Taskleiste'
    }
    Click-At $c @() -Right
    return Wait-Menu $processId $true 3000
}

$cfg = New-TestConfig 'phase6-traymenu'
# Versionszeile (G8.1): „HDRpilot <Major.Minor.Build>“, dieselbe Zahl wie die Dateiversion der gebauten EXE
$exeVersion = (Get-Item $AppExe).VersionInfo
$expectedVersion = 'HDRpilot {0}.{1}.{2}' -f $exeVersion.FileMajorPart, $exeVersion.FileMinorPart, $exeVersion.FileBuildPart
# Ohne Hinweise: der Willkommens-Hinweis der leeren Bibliothek läge sonst als Toast über dem Infobereich.
$cfgFile = Join-Path $cfg 'config.json'
[IO.File]::WriteAllText($cfgFile, ([IO.File]::ReadAllText($cfgFile).Replace('"settings": {', '"settings": { "notifications": false,')), (New-Object Text.UTF8Encoding $false))
$saved = New-Object TrayInput+POINT; [void][TrayInput]::GetCursorPos([ref]$saved)
try {
    $app = Start-App $cfg @('--minimized')
    $sw = [Diagnostics.Stopwatch]::StartNew(); $icon = New-Object TrayInput+RECT
    while (-not [TrayInput]::FindIcon($app.Id, 1, [ref]$icon) -and $sw.ElapsedMilliseconds -lt 20000) { Start-Sleep -Milliseconds 200 }
    Check ($sw.ElapsedMilliseconds -lt 20000) "Testinstanz (PID $($app.Id)) mit Tray-Symbol gestartet"
    Pump 1500
    $pids = @($app.Id)

    # 1. Menü bleibt offen
    $m = Open-Menu $app.Id
    Check ($null -ne $m) ("Rechtsklick öffnet das Menü, während ein anderes Programm aktiv war{0} (Symbol in: {1})" -f $(if ($m) { " ($($m.Ms) ms)" } else { '' }), $script:IconPlace)
    if ($m) {
        Pump 2000
        Check ([TrayInput]::Shown($m.Hwnd)) 'Menü nach 2 s noch offen'
        Check ([TrayInput]::ForegroundPid() -eq $app.Id) 'Testinstanz im Vordergrund, solange das Menü offen ist'

        # 2. Symbolwechsel bei offenem Menü (Pause von außen)
        Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--pause') -Wait
        Pump 1500
        Check ([TrayInput]::Shown($m.Hwnd) -and [TrayInput]::ForegroundPid() -eq $app.Id) 'Symbolwechsel (Pause von außen) bei offenem Menü: Menü bleibt offen'
        Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--resume') -Wait
        Pump 500

        # 3. Esc, danach Vordergrund an die Taskleiste
        Check ([TrayInput]::KeyTo($app.Id, $VK_ESCAPE)) 'Esc an die Testinstanz gesendet'
        Check ($null -ne (Wait-Menu $app.Id $false 1500)) 'Esc schließt das Menü'
        Pump 500
        Check ([TrayInput]::ForegroundClass() -eq 'Shell_TrayWnd') ('Esc gibt den Vordergrund an die Taskleiste zurück (vorn: ' + [TrayInput]::ForegroundClass() + ')')
    }

    # 4. Alt+F4 an das Hilfsfenster (WM_CLOSE) zerstört es nicht
    $sink = [TrayInput]::FindWindowOf($app.Id, 'HdrPilot.TrayMenuOwner')
    Check ($sink -ne [IntPtr]::Zero) 'Hilfsfenster des Menüs gefunden'
    if ($sink -ne [IntPtr]::Zero) {
        [void][TrayInput]::PostMessage($sink, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)
        Pump 500
        Check ([TrayInput]::IsWindow($sink)) 'WM_CLOSE (wie Alt+F4) lässt das Hilfsfenster bestehen'
    }
    $m = Open-Menu $app.Id
    Pump 2000
    Check ($null -ne $m -and [TrayInput]::Shown($m.Hwnd) -and [TrayInput]::ForegroundPid() -eq $app.Id) 'nach WM_CLOSE: nächstes Menü öffnet sich und bleibt 2 s offen'

    # 5. Klick daneben, in ein anderes Programm (das Hilfsfenster dieses Skripts; sicher, weil eigenes Fenster)
    if ($m -and [TrayInput]::Shown($m.Hwnd)) {
        $r = New-Object TrayInput+RECT; [void][TrayInput]::GetWindowRect($decoy.Handle, [ref]$r)
        Click-At @([int](($r.Left + $r.Right) / 2), [int]($r.Top + 60)) @($PID)
        Check ($null -ne (Wait-Menu $app.Id $false 1500)) 'Klick in ein anderes Programm schließt das Menü'
    }

    # 6. Eintrag per Maus
    $m = Open-Menu $app.Id
    Check ($null -ne $m) 'Menü für den Mausklick geöffnet'
    if ($m) {
        Pump 400
        $menuEl = Menu-Element $m.Hwnd
        $item = if ($menuEl) { Find-Id $menuEl 'TrayOpen' 2000 } else { $null }
        Check ($null -ne $item) 'Eintrag „Spieleliste öffnen“ gefunden'
        if ($item) {
            $b = $item.Current.BoundingRectangle
            Click-At @([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) $pids
            Check ($null -ne (Find-Window 'MainWindow' $app.Id 8000)) 'Mausklick auf „Spieleliste öffnen“ öffnet das Hauptfenster'
            Check ($null -ne (Wait-Menu $app.Id $false 1500)) 'Menü danach geschlossen'
        }
    }

    # 7. Tastatur
    $m = Open-Menu $app.Id
    Check ($null -ne $m) 'Menü für die Tastatur geöffnet'
    if ($m) {
        Pump 400
        # Versionszeile vor dem ersten Tastendruck lesen: Text wie die Dateiversion der EXE, nicht aktiv (nicht anklickbar)
        $menuEl = Menu-Element $m.Hwnd
        $versionItem = if ($menuEl) { Find-Id $menuEl 'TrayVersion' 2000 } else { $null }
        $versionName = if ($versionItem) { $versionItem.Current.Name } else { '' }
        Check ($versionName -ceq $expectedVersion) "Versionszeile im Menü: '$versionName' (Dateiversion der EXE: '$expectedVersion')"
        Check ($null -ne $versionItem -and -not $versionItem.Current.IsEnabled) 'Versionszeile nicht aktiv'
        Check ([TrayInput]::KeyTo($app.Id, $VK_UP)) 'Pfeil nach oben an die Testinstanz gesendet'
        Pump 300
        # Welcher Eintrag den Tastaturfokus hat, meldet WPF über UI Automation (HasKeyboardFocus des Eintrags).
        $menuEl = Menu-Element $m.Hwnd
        $exitItem = if ($menuEl) { Find-Id $menuEl 'TrayExit' 2000 } else { $null }
        $exitFocused = $null -ne $exitItem -and $exitItem.Current.HasKeyboardFocus
        Check $exitFocused ('Pfeil nach oben wählt „HDRpilot beenden“ (Tastaturfokus: ' + $exitFocused + ')')
        $versionItem = if ($menuEl) { Find-Id $menuEl 'TrayVersion' 2000 } else { $null }
        Check ($null -ne $versionItem -and -not $versionItem.Current.HasKeyboardFocus) 'Versionszeile hat nach Pfeil nach oben keinen Tastaturfokus'
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
    $decoy.Close(); $decoy.Dispose()
    [void][TrayInput]::SetCursorPos($saved.X, $saved.Y)
    Stop-Started
}

if ($ok) { Write-Output 'PHASE6_TRAYMENU_OK'; exit 0 }
Write-Output 'PHASE6_TRAYMENU_FAILED'
exit 1
