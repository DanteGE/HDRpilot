using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Threading;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

/// <summary>Führt einen Test auf einem eigenen STA-Thread mit WPF-Dispatcher aus (CollectionView, await-Fortsetzungen).</summary>
public static class Sta
{
    public static void Run(Func<Task> body, int timeoutSeconds = 30)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await body();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    frame.Continue = false;
                }
            }));
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            throw new TimeoutException("STA-Test hängt.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    public static void Run(Action body) => Run(() =>
    {
        body();
        return Task.CompletedTask;
    });
}

/// <summary>UI-Dispatcher für Tests: Post sofort, Verzögerungen erst auf <see cref="RunDelayed"/>.</summary>
public sealed class ManualUiDispatcher : IUiDispatcher
{
    private readonly List<Handle> _delayed = [];

    public int PendingDelays => _delayed.Count(h => !h.Cancelled);

    public void Post(Action action) => action();

    public IDisposable Delay(TimeSpan delay, Action action)
    {
        var handle = new Handle(action);
        _delayed.Add(handle);
        return handle;
    }

    public void RunDelayed()
    {
        var due = _delayed.ToList();
        _delayed.Clear();
        foreach (var handle in due.Where(h => !h.Cancelled))
        {
            handle.Action();
        }
    }

    private sealed class Handle(Action action) : IDisposable
    {
        public Action Action { get; } = action;

        public bool Cancelled { get; private set; }

        public void Dispose() => Cancelled = true;
    }
}

/// <summary>Pfade zu Quelldateien der App (für Prüfungen am XAML).</summary>
public static class SourceFiles
{
    public static string MainWindowXaml => Path.Combine(RepositoryRoot(), "src", "HdrPilot.App", "MainWindow.xaml");

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        FindUp(Path.GetDirectoryName(thisFile)) ?? FindUp(AppContext.BaseDirectory)
        ?? throw new DirectoryNotFoundException("Projektordner (HdrPilot.slnx) nicht gefunden.");

    private static string? FindUp(string? dir)
    {
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "HdrPilot.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return string.IsNullOrEmpty(dir) ? null : dir;
    }
}

/// <summary>Dateisystem, das beim Auflisten bestimmter Ordner den Zugriff verweigert (wie geschützte Systemordner).</summary>
public sealed class DenyingFileSystem(IFileSystem inner, params string[] deniedDirectories) : IFileSystem
{
    public bool FileExists(string path) => inner.FileExists(path);

    public bool DirectoryExists(string path) => inner.DirectoryExists(path);

    public string ReadAllText(string path) => inner.ReadAllText(path);

    public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) =>
        IsDenied(directory) ? throw new UnauthorizedAccessException("Zugriff verweigert: " + directory) : inner.EnumerateFiles(directory, searchPattern, recursive);

    public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) =>
        IsDenied(directory) ? throw new UnauthorizedAccessException("Zugriff verweigert: " + directory) : inner.EnumerateDirectories(directory, searchPattern, recursive);

    public void CreateDirectory(string path) => inner.CreateDirectory(path);

    public void WriteAllTextAtomic(string path, string contents) => inner.WriteAllTextAtomic(path, contents);

    public void DeleteFile(string path) => inner.DeleteFile(path);

    public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();

    private bool IsDenied(string directory) => deniedDirectories.Any(d => HdrPilot.Core.Util.PathUtil.PathEquals(d, directory));
}

public sealed class FakeDialogs : ILibraryDialogs
{
    public Queue<string?> Executables { get; } = new();

    public Queue<string?> Folders { get; } = new();

    public Queue<bool> Answers { get; } = new();

    public List<(string Title, string Message)> Confirms { get; } = [];

    public Func<AddGameViewModel, bool>? OnAddGame { get; set; }

    public string? PickExecutable(string? initialDirectory) => Executables.Count > 0 ? Executables.Dequeue() : null;

    public string? PickFolder(string? initialDirectory) => Folders.Count > 0 ? Folders.Dequeue() : null;

    public bool Confirm(string title, string message, string confirmText, bool isDanger)
    {
        Confirms.Add((title, message));
        return Answers.Count > 0 && Answers.Dequeue();
    }

    public bool ShowAddGame(AddGameViewModel viewModel) => OnAddGame?.Invoke(viewModel) ?? false;
}

public sealed class FakeIcons : IGameIconService
{
    public List<string> Requested { get; } = [];

    public ImageSource? GetCachedIcon(GameConfig game) => null;

    public Task<ImageSource?> GetIconAsync(GameConfig game, CancellationToken cancellationToken = default)
    {
        Requested.Add(game.Key);
        return Task.FromResult<ImageSource?>(null);
    }

    public int Cleared { get; private set; }

    public void ClearCache() => Cleared++;
}

public sealed class FakeShortcuts : IShortcutService
{
    public List<(string Kind, string Key)> Created { get; } = [];

    public Exception? Failure { get; set; }

    public string CreateDesktopShortcut(GameConfig game) => Create("Desktop", game);

    public string CreateStartMenuShortcut(GameConfig game) => Create("Startmenü", game);

    private string Create(string kind, GameConfig game)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Created.Add((kind, game.Key));
        return $@"C:\Links\{kind}\{game.Name} (HDR).lnk";
    }
}

/// <summary>Zwischenablage zum Mitlesen; berührt nie die echte Zwischenablage.</summary>
public sealed class FakeClipboard : IClipboard
{
    public List<string> Texts { get; } = [];

    /// <summary>Wenn gesetzt, scheitert das Kopieren damit (etwa belegte Zwischenablage).</summary>
    public Exception? Failure { get; set; }

    public void SetText(string text)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Texts.Add(text);
    }
}

/// <summary>Kontext der Bibliothek über einem echten ConfigService (Ablage im Speicher) und Attrappen.</summary>
public sealed class TestLibraryContext : ILibraryContext
{
    public const string ConfigPath = @"C:\Cfg\config.json";

    public TestLibraryContext(params GameConfig[] games)
    {
        Files = new InMemoryFileSystem();
        FakeClock = new FakeClock();
        ConfigService = new ConfigService(new ConfigStore(Files, ConfigPath, FakeClock), FakeClock);
        if (games.Length > 0)
        {
            ConfigService.Update(c => c.Games.AddRange(games));
        }

        ConfigService.Changed += (_, _) =>
        {
            if (!Disposed)
            {
                ConfigChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        Env.Folders[System.Environment.SpecialFolder.Windows] = @"C:\Windows";
    }

    public event EventHandler? ConfigChanged;

    public event EventHandler? HdrStateChanged;

    public event EventHandler? ScanStateChanged;

    public event EventHandler? HdrSupportChanged;

    /// <summary>HDR-Unterstützung laut PCGamingWiki, wie sie der Zwischenspeicher liefern würde.</summary>
    public Dictionary<string, HdrSupportStatus> HdrSupport { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int HdrSupportLoads { get; private set; }

    public int HdrSupportChangedSubscribers => HdrSupportChanged?.GetInvocationList().Length ?? 0;

    public InMemoryFileSystem Files { get; }

    public FakeClock FakeClock { get; }

    public ConfigService ConfigService { get; }

    public FakeHdrController Hdr { get; } = FakeHdrController.WithPrimary();

    public FakeEnvironment Env { get; } = new();

    public ManualUiDispatcher Dispatcher { get; } = new();

    public FakeIcons FakeIcons { get; } = new();

    public FakeShortcuts FakeShortcuts { get; } = new();

    public FakeClipboard FakeClipboard { get; } = new();

    /// <summary>Programmpfad von HdrPilot für die Steam-Startoption (Attrappe, mit Leerzeichen wie unter „Program Files“).</summary>
    public string? ExecutablePath { get; set; } = @"C:\Program Files\HdrPilot\HdrPilot.exe";

    public List<SessionInfo> SessionList { get; } = [];

    public List<string> Launched { get; } = [];

    public LaunchOutcome NextLaunch { get; set; } = new(true, "gestartet", new DateTimeOffset(2026, 10, 7, 12, 0, 1, TimeSpan.Zero), null);

    public Exception? FailNextUpdate { get; set; }

    public int Updates { get; private set; }

    public int ToggleHdrCalls { get; private set; }

    public int RescanCalls { get; private set; }

    public int SettingsShown { get; private set; }

    public bool Disposed { get; private set; }

    public LibraryScanReport? ReportAfterRescan { get; set; }

    public int ConfigChangedSubscribers => ConfigChanged?.GetInvocationList().Length ?? 0;

    public int HdrStateChangedSubscribers => HdrStateChanged?.GetInvocationList().Length ?? 0;

    public int ScanStateChangedSubscribers => ScanStateChanged?.GetInvocationList().Length ?? 0;

    public AppConfig Config => ConfigService.Current;

    public IFileSystem FileSystem => Files;

    public IAppEnvironment Environment => Env;

    public IClock Clock => FakeClock;

    public IGameIconService Icons => FakeIcons;

    public IShortcutService Shortcuts => FakeShortcuts;

    public IUiDispatcher Ui => Dispatcher;

    public IClipboard Clipboard => FakeClipboard;

    public IReadOnlyList<SessionInfo> Sessions => SessionList;

    public bool IsPaused { get; private set; }

    public bool IsScanning { get; private set; }

    public LibraryScanReport? LastScan { get; set; }

    public static GameConfig Game(
        GameSource source, string id, string name, bool hdrAuto = false, bool isNew = true, bool installed = true, string? installDir = null) => new()
    {
        Source = source,
        SourceId = id,
        Name = name,
        InstallDir = installDir ?? $@"D:\Games\{id}",
        HdrAuto = hdrAuto,
        IsNew = isNew,
        IsInstalled = installed,
        LaunchCommand = "steam://rungameid/" + id,
    };

    public Task UpdateConfigAsync(Action<AppConfig> mutate)
    {
        if (FailNextUpdate is { } failure)
        {
            FailNextUpdate = null;
            return Task.FromException(failure);
        }

        Updates++;
        ConfigService.Update(mutate);
        return Task.CompletedTask;
    }

    /// <summary>Wenn gesetzt, scheitert das Laden der HDR-Unterstützung damit (Zwischenspeicher gesperrt).</summary>
    public Exception? HdrSupportFailure { get; set; }

    public Task<IReadOnlyDictionary<string, HdrSupportStatus>> LoadHdrSupportAsync()
    {
        HdrSupportLoads++;
        if (HdrSupportFailure is { } failure)
        {
            return Task.FromException<IReadOnlyDictionary<string, HdrSupportStatus>>(failure);
        }

        return Task.FromResult<IReadOnlyDictionary<string, HdrSupportStatus>>(new Dictionary<string, HdrSupportStatus>(HdrSupport, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Wie das Ende eines Abgleichs: neue Ergebnisse setzen und melden.</summary>
    public void PublishHdrSupport(params (string Key, HdrSupportStatus Status)[] statuses)
    {
        HdrSupport.Clear();
        foreach (var (key, status) in statuses)
        {
            HdrSupport[key] = status;
        }

        HdrSupportChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<DisplayInfo> GetDisplays() => Hdr.GetDisplays();

    public IReadOnlyList<DisplayInfo> TargetDisplays()
    {
        var displays = Hdr.GetDisplays();
        var targets = Config.Settings.TargetDisplays;
        return targets.Count > 0
            ? displays.Where(d => targets.Contains(d.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)).ToList()
            : displays.Where(d => d.IsPrimary).Take(1).ToList();
    }

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        RaiseHdrStateChanged();
    }

    public Task ToggleHdrNowAsync()
    {
        ToggleHdrCalls++;
        return Task.CompletedTask;
    }

    public Task RescanAsync()
    {
        RescanCalls++;
        IsScanning = true;
        ScanStateChanged?.Invoke(this, EventArgs.Empty);
        if (ReportAfterRescan is { } report)
        {
            LastScan = report;
            ConfigService.ApplyScan(report, Files);
        }

        IsScanning = false;
        ScanStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<LaunchOutcome> LaunchAsync(string gameKey)
    {
        Launched.Add(gameKey);
        return Task.FromResult(NextLaunch);
    }

    public void ShowSettings() => SettingsShown++;

    public void RaiseHdrStateChanged() => HdrStateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
