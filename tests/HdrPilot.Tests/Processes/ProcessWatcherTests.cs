using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HdrPilot.Core.Native;
using HdrPilot.Core.Processes;
using HdrPilot.Tests.Hdr;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Processes;

public sealed class ProcessWatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Poll_reports_started_and_exited_from_difference()
    {
        var provider = new ListProvider();
        var watcher = new PollingProcessWatcher(provider, TimeSpan.FromSeconds(1));
        provider.Set(new ProcessInfo(1, "a.exe", @"C:\a.exe"), new ProcessInfo(2, "b.exe", @"C:\b.exe"));
        var first = watcher.Poll();
        Assert.Equal(2, first.Started.Count);
        Assert.Empty(first.Exited);

        provider.Set(new ProcessInfo(2, "b.exe", @"C:\b.exe"), new ProcessInfo(3, "c.exe", null));
        var second = watcher.Poll();
        Assert.Equal([3], second.Started.Select(p => p.ProcessId));
        Assert.Equal([1], second.Exited.Select(p => p.ProcessId));
        Assert.Equal(2, second.Current.Count);

        var third = watcher.Poll();
        Assert.Empty(third.Started);
        Assert.Empty(third.Exited);
    }

    [Fact]
    public void Reused_pid_with_other_exe_counts_as_exit_and_start()
    {
        var provider = new ListProvider();
        var watcher = new PollingProcessWatcher(provider, TimeSpan.FromSeconds(1));
        provider.Set(new ProcessInfo(7, "game.exe", @"D:\g\game.exe"));
        watcher.Poll();
        provider.Set(new ProcessInfo(7, "notepad.exe", @"C:\Windows\notepad.exe"));
        var changes = watcher.Poll();
        Assert.Equal("game.exe", Assert.Single(changes.Exited).ExeName);
        Assert.Equal("notepad.exe", Assert.Single(changes.Started).ExeName);
    }

    [Fact]
    public async Task Background_loop_calls_back_every_interval_without_overlap()
    {
        var provider = new ListProvider();
        using var watcher = new PollingProcessWatcher(provider, TimeSpan.FromMilliseconds(100));
        var calls = 0;
        var concurrent = 0;
        var maxConcurrent = 0;
        watcher.Start(async (_, token) =>
        {
            var c = Interlocked.Increment(ref concurrent);
            maxConcurrent = Math.Max(maxConcurrent, c);
            Interlocked.Increment(ref calls);
            await Task.Delay(30, token);
            Interlocked.Decrement(ref concurrent);
        });

        await Task.Delay(650, Ct);
        await watcher.StopAsync();

        Assert.InRange(calls, 3, 9);
        Assert.Equal(1, maxConcurrent);
        Assert.False(watcher.IsRunning);
    }

    [Fact]
    public async Task Exception_in_callback_does_not_stop_loop()
    {
        var provider = new ListProvider();
        using var watcher = new PollingProcessWatcher(provider, TimeSpan.FromMilliseconds(100));
        var calls = 0;
        watcher.Start((_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("Test");
        });
        await Task.Delay(450, Ct);
        await watcher.StopAsync();
        Assert.True(calls >= 3);
    }

    [Fact]
    public async Task Throwing_logger_does_not_kill_the_loop()
    {
        var provider = new ListProvider();
        using var watcher = new PollingProcessWatcher(provider, TimeSpan.FromMilliseconds(100), new ThrowingLogger());
        var calls = 0;
        watcher.Start((_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("Rückruf kaputt");
        });
        await Task.Delay(450, Ct);

        Assert.True(watcher.IsRunning);
        Assert.True(calls >= 3);
        await watcher.StopAsync();
    }

    private sealed class ThrowingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new ObjectDisposedException("log");
    }
    [Fact]
    public void Win32_provider_finds_own_process_with_path()
    {
        var provider = new Win32ProcessSnapshotProvider();
        var all = provider.GetProcesses();
        var self = Environment.ProcessId;
        var me = Assert.Single(all, p => p.ProcessId == self);
        Assert.Equal(Environment.ProcessPath, me.ExePath, ignoreCase: true);
        Assert.Equal(Path.GetFileName(Environment.ProcessPath), me.ExeName, ignoreCase: true);
        Assert.True(all.Count > 20);
        Assert.Contains(all, p => p.ExePath is null); // geschützte Systemprozesse ohne lesbaren Pfad
    }

    [Fact]
    public void Win32_provider_queries_paths_only_for_new_processes()
    {
        var provider = new Win32ProcessSnapshotProvider();
        provider.GetProcesses();
        var afterFirst = provider.PathQueries;
        provider.GetProcesses();
        provider.GetProcesses();
        Assert.True(provider.PathQueries - afterFirst < 10, $"zu viele Pfadabfragen: {provider.PathQueries - afterFirst}");
    }

    [Fact]
    public void Win32_provider_does_not_leak_handles()
    {
        var provider = new Win32ProcessSnapshotProvider();
        provider.GetProcesses();
        using var me = Process.GetCurrentProcess();
        me.Refresh();
        var before = me.HandleCount;
        for (var i = 0; i < 200; i++)
        {
            provider.GetProcesses();
        }

        me.Refresh();
        Assert.True(me.HandleCount - before < 20, $"Handles vorher {before}, nachher {me.HandleCount}");
    }

    [Fact]
    public void Processentry32w_matches_tlhelp32_header()
    {
        Assert.Equal(568, Marshal.SizeOf<ToolhelpNative.PROCESSENTRY32W>());
        var path = HeaderFiles.Find("TlHelp32.h");
        Assert.SkipWhen(path is null, "TlHelp32.h nicht gefunden (.sdk\\).");
        var header = CHeaderLayout.Parse(File.ReadAllText(path!), new Dictionary<string, long> { ["MAX_PATH"] = 260 });
        var layout = header.Layout("PROCESSENTRY32W");
        Assert.Equal(layout.Size, Marshal.SizeOf<ToolhelpNative.PROCESSENTRY32W>());
        var fields = typeof(ToolhelpNative.PROCESSENTRY32W).GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(f => (f.Name, Offset: (int)Marshal.OffsetOf<ToolhelpNative.PROCESSENTRY32W>(f.Name)))
            .OrderBy(f => f.Offset)
            .ToList();
        Assert.Equal(layout.Slots.Select(s => (s.Names[0], s.Offset)), fields);
        Assert.Equal(ToolhelpNative.TH32CS_SNAPPROCESS, (uint)header.Constant("TH32CS_SNAPPROCESS"));
    }

    private sealed class ListProvider : IProcessSnapshotProvider
    {
        private List<ProcessInfo> _list = [];

        public void Set(params ProcessInfo[] processes) => _list = [.. processes];

        public IReadOnlyList<ProcessInfo> GetProcesses() => _list;
    }
}
