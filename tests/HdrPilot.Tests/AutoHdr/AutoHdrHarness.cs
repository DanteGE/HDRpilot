using System.Text.Json;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Auto HDR (E88) mit Attrappen: Schlüssel UserGpuPreferences im Speicher (nie die Registry), Dateisystem im Speicher,
/// echte Konfiguration (<see cref="ConfigService"/>) darüber, steuerbare Uhr. Konfigurationsänderungen gehen wie in der
/// App an <see cref="AutoHdrService.OnConfigChanged"/>.
/// </summary>
internal sealed class AutoHdrHarness
{
    public const string DirA = @"D:\Steam-Bibliothek\steamapps\common\Spiel A";
    public const string ExeA = DirA + @"\bin\x64\SpielA.exe";
    public const string DirB = @"D:\Games\Spiel B";
    public const string ExeB = DirB + @"\SpielB.exe";
    public const string Global = AutoHdrRegistryLocation.GlobalValueName;
    public const string GlobalOff = "AutoHDREnable=0;SwapEffectUpgradeEnable=1;";
    public const string GlobalOn = "AutoHDREnable=1;SwapEffectUpgradeEnable=1;";

    private AutoHdrService? _service;
    private EventHandler? _forward;

    public AutoHdrHarness()
    {
        Paths = AppPaths.InDirectory(@"C:\Test\HDRpilot-" + Guid.NewGuid().ToString("N")[..8]);
        Config = new ConfigService(new ConfigStore(Fs, Paths.ConfigFile, Clock), Clock);
        Config.Update(c =>
        {
            c.Settings.GracePeriodSeconds = 10;
            c.Settings.LaunchTimeoutSeconds = 120;
        });
    }

    public InMemoryFileSystem Fs { get; } = new();

    public FakeClock Clock { get; } = new();

    public FakeEnvironment Env { get; } = new() { WindowsBuild = 26300 };

    public FakeGpuPreferenceStore Store { get; } = new();

    public ListLogger<AutoHdrService> Log { get; } = new();

    public AppPaths Paths { get; }

    public ConfigService Config { get; }

    /// <summary>Zahl der <see cref="AutoHdrService.Changed"/>-Ereignisse.</summary>
    public int ChangedEvents { get; private set; }

    /// <summary>Optionen der Notlösung (Neustart-Puffer, kurzer Lauf); vor dem ersten Zugriff auf <see cref="Service"/> setzen.</summary>
    public SessionManagerOptions Options { get; set; } = SessionManagerOptions.Default;

    public AutoHdrService Service => _service ??= Create();

    public AppConfig Current => Config.Current;

    /// <summary>Neustart von HDRpilot (etwa nach einem Absturz): neuer Dienst über derselben Ablage und Registry.</summary>
    public AutoHdrService Restart()
    {
        if (_forward is not null)
        {
            Config.Changed -= _forward;
        }

        _service = Create();
        return _service;
    }

    public string AddGame(string id, string exe, bool autoHdr = true, bool hdrAuto = false, string? installDir = null)
    {
        var game = NewGame(id, exe, autoHdr, hdrAuto, installDir);
        Config.Update(c => c.Games.Add(game));
        return game.Key;
    }

    public static GameConfig NewGame(string id, string exe, bool autoHdr = true, bool hdrAuto = false, string? installDir = null) => new()
    {
        Source = GameSource.Steam,
        SourceId = id,
        Name = "Spiel " + id,
        InstallDir = installDir ?? Path.GetDirectoryName(exe)!,
        KnownExecutables = [exe],
        AutoHdr = autoHdr,
        HdrAuto = hdrAuto,
        LaunchCommand = "steam://rungameid/" + id,
    };

    public void SetAutoHdr(string key, bool on) => Config.UpdateGame(key, g => g.AutoHdr = on);

    public void SetFallback(bool on) => Config.Update(c => c.Settings.AutoHdrGlobalFallback = on);

    /// <summary>Änderung ohne Einfluss auf Auto HDR (wie „zuletzt gespielt“).</summary>
    public void TouchUnrelated() => Config.Update(c => c.Settings.LibrarySortOrder = c.Settings.LibrarySortOrder == "LastPlayed" ? null : "LastPlayed");

    public void RemoveGame(string key) => Config.Update(c => c.Games.RemoveAll(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase)));

    public string? Value(string name) => Store.GetString(name);

    public AutoHdrStateData? State => Fs.FileExists(Paths.AutoHdrStateFile)
        ? JsonSerializer.Deserialize<AutoHdrStateData>(Fs.ReadAllText(Paths.AutoHdrStateFile), ConfigJson.Options)
        : null;

    public AutoHdrGlobalMarker? Marker => Fs.FileExists(Paths.AutoHdrGlobalFile)
        ? JsonSerializer.Deserialize<AutoHdrGlobalMarker>(Fs.ReadAllText(Paths.AutoHdrGlobalFile), ConfigJson.Options)
        : null;

    public void WriteState(AutoHdrStateData data) => Fs.AddFile(Paths.AutoHdrStateFile, JsonSerializer.Serialize(data, ConfigJson.Options));

    public void WriteMarker(AutoHdrGlobalMarker marker) => Fs.AddFile(Paths.AutoHdrGlobalFile, JsonSerializer.Serialize(marker, ConfigJson.Options));

    public bool Logged(string fragment, LogLevel? level = null) =>
        Log.Entries.Any(e => (level is null || e.Level == level) && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public int LogCount(string fragment) => Log.Entries.Count(e => e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public AutoHdrTargetState? StateOf(string gameKey, string exe) =>
        Service.GetStatus(gameKey).Targets.FirstOrDefault(t => string.Equals(t.ExePath, exe, StringComparison.OrdinalIgnoreCase))?.State;

    /// <summary>Schreibvorgänge am globalen Wert.</summary>
    public List<string> GlobalOperations => Store.Operations.Where(o => o.Contains(Global, StringComparison.Ordinal)).ToList();

    /// <summary>Sitzungslogik über derselben Konfiguration mit diesem Dienst als Auto-HDR-Empfänger.</summary>
    public SessionManager CreateSessions(FakeHdrController hdr, FakeLauncher launcher, IAutoHdrSessionHooks? hooks = null) => new(
        hdr,
        Config,
        new InMemorySessionStateStore(),
        launcher,
        Clock,
        (_, _) => false,
        [@"C:\Windows"],
        new ListLogger<SessionManager>(),
        new SessionManagerOptions
        {
            RestartBuffer = Options.RestartBuffer,
            ShortSessionLimit = Options.ShortSessionLimit,
            FullscreenAppRunning = () => false,
            NotificationsSuppressed = () => false,
            ActivityProbe = new FakeActivityProbe(),
            AutoHdr = hooks ?? Service,
        });

    private AutoHdrService Create()
    {
        var service = new AutoHdrService(Config, Store, AutoHdrRegistryLocation.ForTest("Fake"), Fs, Paths, Env, Clock, Log, Options);
        _forward = (_, _) => service.OnConfigChanged();
        Config.Changed += _forward;
        service.Changed += (_, _) => ChangedEvents++;
        return service;
    }
}
