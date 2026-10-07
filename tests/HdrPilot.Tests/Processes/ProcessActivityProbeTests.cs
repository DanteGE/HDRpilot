using System.Diagnostics;
using System.Runtime.InteropServices;
using HdrPilot.Core.Native;
using HdrPilot.Core.Processes;
using HdrPilot.Tests.Hdr;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Processes;

/// <summary>
/// CPU-Zeit und Fenster ohne Handle auf fremde Prozesse (Hinweis „Spiel hängt“). Geprüft am eigenen Prozess und an
/// einem Kindprozess ohne Fenster; die Lage der Zeitfelder zusätzlich gegen winternl.h.
/// </summary>
public sealed partial class ProcessActivityProbeTests
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_MINIMIZE = 0x20000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [Fact]
    public void Time_fields_fill_the_end_of_reserved1_in_winternl_header()
    {
        Assert.Equal(88, Marshal.SizeOf<NtNative.SYSTEM_PROCESS_INFORMATION_TIMES>());
        var path = HeaderFiles.Find("winternl.h");
        Assert.SkipWhen(path is null, "winternl.h nicht gefunden (.sdk\\).");
        var full = CHeaderLayout.Parse(File.ReadAllText(path!)).Layout("SYSTEM_PROCESS_INFORMATION");

        // Header: NextEntryOffset, NumberOfThreads, BYTE Reserved1[48], ImageName ... (phnt: ... CreateTime, UserTime, KernelTime, ImageName).
        var reserved = full.Slots.Single(s => s.Names.Contains("Reserved1"));
        var image = full.Slots.Single(s => s.Names.Contains("ImageName"));
        Assert.Equal(8, reserved.Offset);
        Assert.Equal(48, reserved.Size);
        Assert.Equal(reserved.Offset + reserved.Size, image.Offset);

        var create = Offset(nameof(NtNative.SYSTEM_PROCESS_INFORMATION_TIMES.CreateTime));
        var user = Offset(nameof(NtNative.SYSTEM_PROCESS_INFORMATION_TIMES.UserTime));
        var kernel = Offset(nameof(NtNative.SYSTEM_PROCESS_INFORMATION_TIMES.KernelTime));
        Assert.Equal(image.Offset - 24, create); // drei LARGE_INTEGER direkt vor ImageName
        Assert.Equal(create + 8, user);
        Assert.Equal(user + 8, kernel);
        Assert.Equal(image.Offset, kernel + 8);
        Assert.True(create >= reserved.Offset);

        foreach (var name in new[] { "NextEntryOffset", "UniqueProcessId" })
        {
            Assert.Equal(full.Slots.Single(s => s.Names.Contains(name)).Offset, Offset(name));
        }

        static int Offset(string field) => (int)Marshal.OffsetOf<NtNative.SYSTEM_PROCESS_INFORMATION_TIMES>(field);
    }

    [Fact]
    public void Create_time_of_own_process_matches_GetProcessTimes()
    {
        using var probe = new NtProcessActivityProbe();
        using var me = Process.GetCurrentProcess();
        var totalBefore = me.TotalProcessorTime;

        var sample = probe.SampleCpu(new HashSet<int> { Environment.ProcessId })[Environment.ProcessId];

        me.Refresh();
        var totalAfter = me.TotalProcessorTime;
        var expected = me.StartTime.ToUniversalTime().ToFileTimeUtc();
        Assert.True(Math.Abs(sample.CreateTime - expected) < TimeSpan.TicksPerMillisecond, $"CreateTime {sample.CreateTime}, erwartet {expected}");

        // Benutzer- plus Kernelzeit wie GetProcessTimes (eine Zeitscheibe Spielraum für die Abrechnung der Threads).
        var slack = TimeSpan.FromMilliseconds(20).Ticks;
        Assert.InRange(sample.CpuTime, totalBefore.Ticks - slack, totalAfter.Ticks + slack);
        Assert.True(probe.HoldsBuffer);

        probe.ReleaseBuffers();
        Assert.False(probe.HoldsBuffer);
    }

    [Fact]
    public void Cpu_time_grows_while_the_process_works()
    {
        using var probe = new NtProcessActivityProbe();
        using var me = Process.GetCurrentProcess();
        var ids = new HashSet<int> { Environment.ProcessId };
        var before = probe.SampleCpu(ids)[Environment.ProcessId];
        var cpuBefore = me.TotalProcessorTime;

        // Arbeiten, bis der Prozess sicher 200 ms CPU-Zeit verbraucht hat (höchstens 10 s).
        var sw = Stopwatch.StartNew();
        var x = 0.0;
        do
        {
            for (var i = 0; i < 100_000; i++)
            {
                x += Math.Sqrt(i);
            }

            me.Refresh();
        }
        while (me.TotalProcessorTime - cpuBefore < TimeSpan.FromMilliseconds(200) && sw.Elapsed < TimeSpan.FromSeconds(10));

        var after = probe.SampleCpu(ids)[Environment.ProcessId];

        Assert.True(x > 0);
        Assert.Equal(before.CreateTime, after.CreateTime);
        Assert.True(after.CpuTime - before.CpuTime >= TimeSpan.FromMilliseconds(150).Ticks, $"Zuwachs {(after.CpuTime - before.CpuTime) / TimeSpan.TicksPerMillisecond} ms");
    }

    [Fact]
    public void Unknown_and_exited_processes_are_missing()
    {
        using var probe = new NtProcessActivityProbe();
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        var childId = child.Id;
        child.WaitForExit();

        var result = probe.SampleCpu(new HashSet<int> { 0, childId, Environment.ProcessId });

        Assert.Equal([Environment.ProcessId], result.Keys);
        Assert.Empty(probe.SampleCpu(new HashSet<int>()));
    }

    [Fact]
    public void Visible_and_minimized_windows_count_hidden_ones_and_windowless_processes_do_not()
    {
        using var probe = new NtProcessActivityProbe();
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var me = Environment.ProcessId;
            var both = new HashSet<int> { me, child.Id };
            var hidden = CreateTestWindow(WS_POPUP);
            try
            {
                // Der Testprozess hat sonst kein Hauptfenster (Konsole ohne Fenster): ein verstecktes zählt nicht.
                Assert.Empty(probe.WithVisibleWindow(both));
            }
            finally
            {
                DestroyWindow(hidden);
            }

            foreach (var style in new[] { WS_POPUP | WS_VISIBLE, WS_POPUP | WS_VISIBLE | WS_MINIMIZE })
            {
                var window = CreateTestWindow(style);
                try
                {
                    Assert.Equal([me], probe.WithVisibleWindow(both)); // sichtbar oder minimiert: zählt
                }
                finally
                {
                    DestroyWindow(window);
                }
            }

            Assert.Empty(probe.WithVisibleWindow(new HashSet<int>()));
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
        }
    }

    /// <summary>Unsichtbar außerhalb des Bildschirms, ohne Taskleisteneintrag und ohne den Fokus zu nehmen.</summary>
    private static nint CreateTestWindow(int style)
    {
        var hwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "STATIC", "HdrPilot-Test", style, -32000, -32000, 1, 1, 0, 0, 0, 0);
        Assert.NotEqual(0, hwnd);
        return hwnd;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);
}
