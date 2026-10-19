using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Theming;
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

    /// <summary>Wartet, bis ein asynchroner Befehl fertig ist (seine Fortsetzungen laufen über den Dispatcher des Tests).</summary>
    public static async Task Finished(AsyncRelayCommand command)
    {
        for (var i = 0; i < 500 && command.IsRunning; i++)
        {
            await Task.Delay(10);
        }

        Assert.False(command.IsRunning, "Befehl läuft noch");
    }
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

    /// <summary>Datei im Projektordner, Teile relativ zur Wurzel (für Prüfungen am Quelltext).</summary>
    public static string InRepository(params string[] parts) => Path.Combine([RepositoryRoot(), .. parts]);

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

    /// <summary>Antworten für „Symbol ändern…“ (null: abgebrochen).</summary>
    public Queue<string?> IconFiles { get; } = new();

    public string? PickIconFile(string? initialDirectory) => IconFiles.Count > 0 ? IconFiles.Dequeue() : null;

    /// <summary>Läuft, während die Rückfrage „offen“ ist (wie ein modaler Dialog, der Nachrichten pumpt).</summary>
    public Action? WhileConfirmOpen { get; set; }

    public bool Confirm(string title, string message, string confirmText, bool isDanger)
    {
        Confirms.Add((title, message));
        WhileConfirmOpen?.Invoke();
        return Answers.Count > 0 && Answers.Dequeue();
    }

    /// <summary>Rückfragen mit Aufzählung und Häkchen (E90), in der Reihenfolge des Öffnens.</summary>
    public List<ConfirmRequest> Requests { get; } = [];

    /// <summary>Ob das Häkchen gesetzt wird (je Rückfrage mit Häkchen; leer: nicht gesetzt).</summary>
    public Queue<bool> OptionAnswers { get; } = new();

    public ConfirmAnswer Confirm(ConfirmRequest request)
    {
        Requests.Add(request);
        Confirms.Add((request.Title, request.Message));
        WhileConfirmOpen?.Invoke();
        var confirmed = Answers.Count > 0 && Answers.Dequeue();
        var option = request.OptionText is not null && OptionAnswers.Count > 0 && OptionAnswers.Dequeue();
        return new ConfirmAnswer(confirmed, confirmed && option);
    }

    public bool ShowAddGame(AddGameViewModel viewModel) => OnAddGame?.Invoke(viewModel) ?? false;

    /// <summary>Antworten für „Bild wählen…“ → „Eigene Datei…“ (null: abgebrochen).</summary>
    public Queue<string?> ArtworkFiles { get; } = new();

    public string? PickArtworkFile(string? initialDirectory) => ArtworkFiles.Count > 0 ? ArtworkFiles.Dequeue() : null;

    /// <summary>Geöffnete Dialoge „Bild wählen…“.</summary>
    public List<ArtworkPickerViewModel> Pickers { get; } = [];

    /// <summary>Was der Dialog tut (Standard: abbrechen).</summary>
    public Func<ArtworkPickerViewModel, bool>? OnArtworkPicker { get; set; }

    public bool ShowArtworkPicker(ArtworkPickerViewModel viewModel)
    {
        Pickers.Add(viewModel);
        return OnArtworkPicker?.Invoke(viewModel) ?? false;
    }
}

public sealed class FakeIcons : IGameIconService
{
    public List<string> Requested { get; } = [];

    public ImageSource? GetCachedIcon(GameConfig game) => null;

    /// <summary>Wenn gesetzt, liefert das die Symbole (etwa erst später fertige Anfragen); sonst sofort keins.</summary>
    public Func<GameConfig, Task<ImageSource?>>? Load { get; set; }

    public Task<ImageSource?> GetIconAsync(GameConfig game, CancellationToken cancellationToken = default)
    {
        Requested.Add(game.Key);
        return Load?.Invoke(game) ?? Task.FromResult<ImageSource?>(null);
    }

    public int Cleared { get; private set; }

    public void ClearCache() => Cleared++;

    public List<string> Forgotten { get; } = [];

    /// <summary>Gespeicherte eigene Symbole: Spielschlüssel und gewählte Datei.</summary>
    public List<(string Key, string Source)> Saved { get; } = [];

    public List<string?> Deleted { get; } = [];

    /// <summary>Wenn gesetzt, scheitert „Symbol ändern…“ damit (etwa kein Bild in der Datei).</summary>
    public Exception? SaveFailure { get; set; }

    public void Forget(string gameKey) => Forgotten.Add(gameKey);

    /// <summary>Spiele, deren Symbol samt Eintrag in icon-cache.json vergessen wurde (E90).</summary>
    public List<string> ForgottenGames { get; } = [];

    public void ForgetGame(string gameKey)
    {
        lock (ForgottenGames)
        {
            ForgottenGames.Add(gameKey);
        }
    }

    public string SaveCustomIcon(GameConfig game, string sourcePath)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        Saved.Add((game.Key, sourcePath));
        return CustomIcons.FileNameFor(game.Key);
    }

    public void DeleteCustomIcon(string? fileName) => Deleted.Add(fileName);

    public string? ShortcutIconFile(GameConfig game) => null;
}

public sealed class FakeShortcuts : IShortcutService
{
    public List<(string Kind, string Key)> Created { get; } = [];

    /// <summary>Threads, auf denen die Verknüpfungen entstanden (nie der UI-Thread).</summary>
    public List<int> Threads { get; } = [];

    public Exception? Failure { get; set; }

    public string CreateDesktopShortcut(GameConfig game) => Create("Desktop", game);

    public string CreateStartMenuShortcut(GameConfig game) => Create("Startmenü", game);

    /// <summary>Vorhandene HDRpilot-Verknüpfungen je Spielschlüssel (E90; die Attrappe liest nie die echte Platte).</summary>
    public Dictionary<string, List<GameShortcut>> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gelöschte Verknüpfungen.</summary>
    public List<GameShortcut> Deleted { get; } = [];

    public IReadOnlyList<GameShortcut> FindFor(string gameKey)
    {
        lock (Existing)
        {
            return Existing.TryGetValue(gameKey, out var list) ? list.ToList() : [];
        }
    }

    public IReadOnlyList<GameShortcut> Delete(string gameKey, IReadOnlyCollection<GameShortcut> shortcuts)
    {
        lock (Existing)
        {
            var list = Existing.TryGetValue(gameKey, out var found) ? found : [];
            var gone = shortcuts.Where(s => list.Remove(s)).ToList();
            Deleted.AddRange(gone);
            return gone;
        }
    }

    private string Create(string kind, GameConfig game)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Threads.Add(Environment.CurrentManagedThreadId);
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

    /// <summary>AppIDs, bei denen die HDRpilot-Startoption schon in Steam steht (Attrappe; liest nie Steam).</summary>
    public HashSet<string> SteamLaunchOptionIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Abgefragte AppIDs der Steam-Startoption, in der Reihenfolge der Abfragen.</summary>
    public List<string> SteamLaunchOptionChecks { get; } = [];

    /// <summary>Wenn gesetzt, antwortet die nächste Abfrage erst, wenn der Test sie abschließt (Lesen dauert).</summary>
    public TaskCompletionSource<bool>? PendingSteamLaunchOptionCheck { get; set; }

    public Task<bool> HasSteamLaunchOptionAsync(string appId)
    {
        SteamLaunchOptionChecks.Add(appId);
        if (PendingSteamLaunchOptionCheck is { } pending)
        {
            PendingSteamLaunchOptionCheck = null;
            return pending.Task;
        }

        return Task.FromResult(SteamLaunchOptionIds.Contains(appId));
    }

    public List<SessionInfo> SessionList { get; } = [];

    public List<string> Launched { get; } = [];

    public LaunchOutcome NextLaunch { get; set; } = new(true, "gestartet", new DateTimeOffset(2026, 10, 7, 12, 0, 1, TimeSpan.Zero), null);

    /// <summary>Gesetzt: der nächste Start wartet auf diese Quelle (Tests: Start läuft noch).</summary>
    public TaskCompletionSource<LaunchOutcome>? PendingLaunch { get; set; }

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

    /// <summary>Spielbilder (E89); Standard: keine Bilder.</summary>
    public IArtworkService Artwork { get; set; } = NullArtworkService.Instance;

    /// <summary>Helligkeit der Bildschirme (E89); Standard: unbekannt.</summary>
    public IDisplayLuminanceService Luminance { get; set; } = NullDisplayLuminanceService.Instance;

    /// <summary>Darstellung (E89); Standard: Auslieferungszustand, dunkel, Animationen erlaubt.</summary>
    public IAppearanceService Appearance { get; set; } = new StaticAppearanceService();

    public IShortcutService Shortcuts => FakeShortcuts;

    /// <summary>Ordner der eigenen Symbole in der Ablage im Speicher (E90).</summary>
    public const string IconDirectory = @"C:\Data\icons";

    /// <summary>Ordner der eigenen Bilder in der Ablage im Speicher (E90).</summary>
    public const string CustomArtDirectory = @"C:\Data\art\custom";

    /// <summary>Aufräumen über die Ablage im Speicher und die Attrappen (E90).</summary>
    public UninstalledGameCleanup Cleanup => new(Files, IconDirectory, CustomArtDirectory, FakeIcons, Artwork, FakeShortcuts);

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
        return PendingLaunch?.Task ?? Task.FromResult(NextLaunch);
    }

    public void ShowSettings() => SettingsShown++;

    /// <summary>Geöffnete Installationsordner („Installationsordner öffnen“; die Attrappe startet nichts).</summary>
    public List<string> OpenedFolders { get; } = [];

    public void OpenFolder(string path) => OpenedFolders.Add(path);

    public void RaiseHdrStateChanged() => HdrStateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
