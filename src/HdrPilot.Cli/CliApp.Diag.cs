using System.Diagnostics;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Sessions;

namespace HdrPilot.Cli;

/// <summary>Diagnose: Kosten eines Abfragezyklus messen (Prozessliste, Differenz, Sitzungslogik).</summary>
public sealed partial class CliApp
{
    private async Task<int> RunDiagAsync(CliArguments args, CancellationToken cancellationToken)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("poll", StringComparison.OrdinalIgnoreCase))
        {
            return Usage("diag braucht einen Unterbefehl: poll.");
        }

        var count = int.TryParse(args.Get("--count"), out var c) ? Math.Clamp(c, 10, 10_000) : 200;
        using var provider = new NtProcessSnapshotProvider();
        var watcher = new PollingProcessWatcher(provider, TimeSpan.FromSeconds(1));
        var config = OpenConfig(ResolvePaths(args));
        var sessions = new SessionManager(
            new NoSwitchHdrController(), config, new MemoryStateStore(), new ShellGameLauncher(), services.Clock,
            (name, patterns) => HelperExecutables.IsHelper(name, patterns));

        await sessions.InitializeAsync(watcher.Poll(), cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < 20; i++)
        {
            await sessions.UpdateAsync(watcher.Poll(), cancellationToken).ConfigureAwait(false); // Aufwärmen (JIT)
        }

        var snapshotTicks = 0L;
        var diffTicks = 0L;
        var sessionTicks = 0L;
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var processCount = 0;
        for (var i = 0; i < count; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            var list = provider.GetProcesses();
            var t1 = Stopwatch.GetTimestamp();
            var changes = watcher.Poll();
            var t2 = Stopwatch.GetTimestamp();
            await sessions.UpdateAsync(changes, cancellationToken).ConfigureAwait(false);
            var t3 = Stopwatch.GetTimestamp();
            snapshotTicks += t1 - t0;
            diffTicks += t2 - t1 - (t1 - t0); // Poll ruft die Liste selbst noch einmal ab
            sessionTicks += t3 - t2;
            processCount = list.Count;
        }

        var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;
        double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency / count;
        stdout.WriteLine($"Prozesse: {processCount}, Spiele in der Konfiguration: {config.Current.Games.Count}, Zyklen: {count}");
        stdout.WriteLine($"Prozessliste (+ Pfade neuer Prozesse)        : {Ms(snapshotTicks):0.000} ms je Zyklus");
        stdout.WriteLine($"Differenz alt/neu:                                {Math.Max(0, Ms(diffTicks)):0.000} ms je Zyklus");
        stdout.WriteLine($"Sitzungslogik (Zuordnung, Nachlauf):              {Ms(sessionTicks):0.000} ms je Zyklus");
        stdout.WriteLine($"CPU-Zeit gesamt je Zyklus (2 Listenabfragen):     {cpu.TotalMilliseconds / count:0.000} ms");
        stdout.WriteLine($"Pfadabfragen gesamt: {provider.PathQueries}, Verfahren: {provider.Method}");
        return ExitCodes.Ok;
    }

    private sealed class NoSwitchHdrController : Core.Hdr.IHdrController
    {
        public IReadOnlyList<Core.Hdr.DisplayInfo> GetDisplays() => [];

        public Task<Core.Hdr.HdrSetResult> SetHdrAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Core.Hdr.HdrSetResult { Status = Core.Hdr.HdrSetStatus.NotConnected, MonitorDevicePath = monitorDevicePath, Message = "Diagnose" });
    }

    private sealed class MemoryStateStore : ISessionStateStore
    {
        public SessionStateData? Load() => null;

        public void Save(SessionStateData data)
        {
        }

        public void Delete()
        {
        }
    }
}
