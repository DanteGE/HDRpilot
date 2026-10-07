using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Sessions;

namespace HdrPilot.Tests.TestSupport;

/// <summary>HDR-Steuerung im Speicher: Bildschirme mit Zustand, Aufrufprotokoll, steuerbare Fehler.</summary>
public sealed class FakeHdrController : IHdrController
{
    public const string Primary = @"\\?\DISPLAY#MSI3DD0#primary";
    public const string Second = @"\\?\DISPLAY#SAM7232#second";

    public List<FakeDisplay> Displays { get; } = [];

    public List<(string Path, bool Enable, HdrSetStatus Status)> Calls { get; } = [];

    /// <summary>Wird bei jedem Schaltaufruf vor der Ausführung aufgerufen (etwa um den Speicherstand zu prüfen).</summary>
    public Action<string, bool>? BeforeSet { get; set; }

    /// <summary>Erzwingt das Ergebnis des nächsten Schaltaufrufs.</summary>
    public Queue<HdrSetStatus> NextResults { get; } = new();

    public bool ThrowOnSet { get; set; }

    public static FakeHdrController WithPrimary(bool hdrOn = false)
    {
        var c = new FakeHdrController();
        c.Displays.Add(new FakeDisplay(Primary, "MAG 341C OLED") { IsPrimary = true, HdrOn = hdrOn });
        return c;
    }

    public FakeDisplay this[string path] => Displays.Single(d => d.Path == path);

    public int SetCalls(bool enable) => Calls.Count(c => c.Enable == enable);

    public IReadOnlyList<DisplayInfo> GetDisplays() => Displays.Where(d => d.Connected).Select(d => new DisplayInfo
    {
        MonitorDevicePath = d.Path,
        FriendlyName = d.Name,
        IsPrimary = d.IsPrimary,
        HdrSupported = d.Capable,
        HdrEnabled = d.HdrOn,
        ActiveColorMode = d.HdrOn ? AdvancedColorMode.Hdr : AdvancedColorMode.Wcg,
        Api = HdrApi.Modern,
    }).ToList();

    public Task<HdrSetResult> SetHdrAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken = default)
    {
        BeforeSet?.Invoke(monitorDevicePath, enable);
        if (ThrowOnSet)
        {
            throw new InvalidOperationException("Treiberfehler");
        }

        var d = Displays.FirstOrDefault(x => x.Path == monitorDevicePath && x.Connected);
        HdrSetStatus status;
        bool? before = d?.HdrOn;
        if (NextResults.Count > 0)
        {
            status = NextResults.Dequeue();
        }
        else if (d is null)
        {
            status = HdrSetStatus.NotConnected;
        }
        else if (!d.Capable)
        {
            status = HdrSetStatus.NotHdrCapable;
        }
        else if (d.HdrOn == enable)
        {
            status = HdrSetStatus.AlreadyInState;
        }
        else
        {
            d.HdrOn = enable;
            status = HdrSetStatus.Success;
        }

        Calls.Add((monitorDevicePath, enable, status));
        return Task.FromResult(new HdrSetResult
        {
            Status = status,
            MonitorDevicePath = monitorDevicePath,
            FriendlyName = d?.Name,
            Before = before,
            After = d?.HdrOn,
            Api = HdrApi.Modern,
            Message = status.ToString(),
        });
    }
}

public sealed class FakeDisplay(string path, string name)
{
    public string Path { get; } = path;

    public string Name { get; } = name;

    public bool IsPrimary { get; init; }

    public bool HdrOn { get; set; }

    public bool Capable { get; set; } = true;

    public bool Connected { get; set; } = true;
}

public sealed class FakeSessionConfig : ISessionConfigSource
{
    private long _revision;

    public AppConfig Config { get; } = new();

    public AppConfig Current => Config;

    public long Revision => _revision;

    public List<(string Key, List<string> Exes)> Recorded { get; } = [];

    public void Touch() => _revision++;

    public GameConfig AddGame(string id, string installDir, bool hdrAuto = true, GameSource source = GameSource.Steam, bool restore = true)
    {
        var g = new GameConfig { Source = source, SourceId = id, Name = "Spiel " + id, InstallDir = installDir, HdrAuto = hdrAuto, RestoreAfter = restore, LaunchCommand = "steam://rungameid/" + id };
        Config.Games.Add(g);
        Touch();
        return g;
    }

    public void RecordDetectedExecutables(string gameKey, IReadOnlyCollection<string> executables)
    {
        Recorded.Add((gameKey, executables.ToList()));
        var g = Config.FindGame(gameKey)!;
        g.DetectedExecutables = executables.ToList();
        Touch();
    }

    public void RecordLastPlayed(string gameKey, DateTimeOffset when) => Config.FindGame(gameKey)!.LastPlayed = when;
}

public sealed class InMemorySessionStateStore : ISessionStateStore
{
    public SessionStateData? Data { get; private set; }

    public int Saves { get; private set; }

    public int Deletes { get; private set; }

    public SessionStateData? Load() => Data;

    public void Save(SessionStateData data)
    {
        Saves++;
        Data = System.Text.Json.JsonSerializer.Deserialize<SessionStateData>(System.Text.Json.JsonSerializer.Serialize(data, ConfigJson.Options), ConfigJson.Options);
    }

    public void Delete()
    {
        if (Data is not null)
        {
            Deletes++;
        }

        Data = null;
    }

    public void Preset(params DisplayClaimState[] displays) => Data = new SessionStateData { Displays = [.. displays] };
}

public sealed class FakeLauncher : IGameLauncher
{
    public List<(string Key, DateTimeOffset At)> Launched { get; } = [];

    public bool Succeeds { get; set; } = true;

    public Func<DateTimeOffset>? Clock { get; set; }

    public Action<GameConfig>? OnLaunch { get; set; }

    public GameLaunchResult Launch(GameConfig game)
    {
        Launched.Add((game.Key, Clock?.Invoke() ?? DateTimeOffset.MinValue));
        OnLaunch?.Invoke(game);
        return Succeeds ? new GameLaunchResult(true, "gestartet") : new GameLaunchResult(false, "Start fehlgeschlagen (Test)");
    }
}

/// <summary>Simulierte Prozessliste: liefert ProcessChanges wie der echte Watcher.</summary>
public sealed class ProcessScript
{
    private readonly Dictionary<int, ProcessInfo> _running = [];
    private Dictionary<int, ProcessInfo> _previous = [];
    private int _nextPid = 1000;

    public ProcessScript()
    {
        // Ein paar Systemprozesse, die nie passen dürfen.
        Start("explorer.exe", @"C:\Windows\explorer.exe");
        Start("svchost.exe", @"C:\Windows\System32\svchost.exe");
        Start("csrss.exe", null);
    }

    public int Start(string exeName, string? exePath)
    {
        var pid = _nextPid++;
        _running[pid] = new ProcessInfo(pid, exeName, exePath);
        return pid;
    }

    public int StartExe(string exePath) => Start(Path.GetFileName(exePath), exePath);

    public void Stop(int pid) => _running.Remove(pid);

    public ProcessChanges Next()
    {
        var current = _running.Values.ToList();
        var started = current.Where(p => !_previous.ContainsKey(p.ProcessId)).ToList();
        var exited = _previous.Values.Where(p => !_running.ContainsKey(p.ProcessId)).ToList();
        _previous = new Dictionary<int, ProcessInfo>(_running);
        return new ProcessChanges(started, exited, current);
    }
}
