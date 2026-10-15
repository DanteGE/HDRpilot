using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>IShellContext ohne AppServices: echte Konfiguration im Speicher, Bildschirme aus FakeHdrController.</summary>
internal sealed class FakeShellContext(ConfigService config, FakeHdrController hdr, IAutostartService autostart) : IShellContext
{
    public event EventHandler? HdrStateChanged;

    public ConfigService Config { get; } = config;

    public IAutostartService Autostart { get; } = autostart;

    public FakeHdrController Hdr { get; } = hdr;

    public List<bool> SavedCalls { get; } = [];

    public int OpenLogFolderCalls { get; private set; }

    public Exception? OpenLogFolderFailure { get; set; }

    public int Subscribers => HdrStateChanged?.GetInvocationList().Length ?? 0;

    /// <summary>Wenn gesetzt, scheitert die Bildschirmabfrage mit dieser Ausnahme.</summary>
    public Exception? DisplayFailure { get; set; }

    public IReadOnlyList<DisplayInfo> GetDisplays() => DisplayFailure is { } failure ? throw failure : Hdr.GetDisplays();

    public void OpenLogFolder()
    {
        OpenLogFolderCalls++;
        if (OpenLogFolderFailure is { } failure)
        {
            throw failure;
        }
    }

    public void OnSettingsSaved(bool searchFoldersChanged) => SavedCalls.Add(searchFoldersChanged);

    public void RaiseHdrStateChanged() => HdrStateChanged?.Invoke(this, EventArgs.Empty);

    public FakeHdrListService FakeHdrList { get; } = new();

    public IHdrListService HdrList => FakeHdrList;

    /// <summary>Darstellung (E89); Standard: Auslieferungszustand, dunkel.</summary>
    public IAppearanceService Appearance { get; set; } = new StaticAppearanceService();

    /// <summary>Spielbilder (E89); Standard: keine Bilder.</summary>
    public IArtworkService Artwork { get; set; } = NullArtworkService.Instance;

    /// <summary>SteamGridDB (E89); Standard: aus, nie ein Netzwerkzugriff.</summary>
    public ISteamGridDbService SteamGridDb { get; set; } = NullSteamGridDbService.Instance;

    /// <summary>Helligkeit der Bildschirme (E89); Standard: unbekannt.</summary>
    public IDisplayLuminanceService Luminance { get; set; } = NullDisplayLuminanceService.Instance;

    public List<string> OpenedPages { get; } = [];

    /// <summary>Wenn gesetzt, scheitert das Öffnen einer Webseite damit.</summary>
    public Exception? OpenWebPageFailure { get; set; }

    public void OpenWebPage(string url)
    {
        if (OpenWebPageFailure is { } failure)
        {
            throw failure;
        }

        OpenedPages.Add(url);
    }
}

/// <summary>HDR-Liste ohne Netz: Stand und Ergebnisse vorgegeben, Aufrufe werden mitgeschrieben.</summary>
internal sealed class FakeHdrListService : IHdrListService
{
    public event EventHandler? Changed;

    public HdrListStatus Status { get; set; } = new(null, null, 0, 0, false, false, null);

    public Dictionary<string, HdrSupportStatus> Statuses { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning { get; set; }

    public int SyncCalls { get; private set; }

    /// <summary>Wenn gesetzt, endet „Jetzt abgleichen“ erst, wenn der Test diese Aufgabe abschließt.</summary>
    public TaskCompletionSource<HdrListRunResult>? PendingSync { get; set; }

    public List<(string User, string? Password)> SaveCalls { get; } = [];

    public PcgwErrorKind? NextSaveResult { get; set; }

    public int RemoveCalls { get; private set; }

    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    public HdrListStatus GetStatus() => Status with { IsRunning = IsRunning };

    public IReadOnlyDictionary<string, HdrSupportStatus> LoadStatuses() => Statuses;

    public Task<HdrListRunResult> SyncNowAsync(bool force = true)
    {
        SyncCalls++;
        return PendingSync?.Task ?? Task.FromResult(new HdrListRunResult(HdrListRunOutcome.Completed, null, 1, 1, 1, false));
    }

    public Task<PcgwErrorKind?> SaveAccountAsync(string userName, string? password)
    {
        SaveCalls.Add((userName, password));
        if (NextSaveResult is null)
        {
            Status = Status with { AccountUser = userName, AccountUnreadable = false };
        }

        return Task.FromResult(NextSaveResult);
    }

    public void RemoveAccount()
    {
        RemoveCalls++;
        Status = Status with { AccountUser = null, AccountUnreadable = false };
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeFolderPicker : IFolderPicker
{
    public Queue<string?> Results { get; } = new();

    public List<string?> Requests { get; } = [];

    public string? PickFolder(string? initialDirectory)
    {
        Requests.Add(initialDirectory);
        return Results.Count > 0 ? Results.Dequeue() : null;
    }
}

/// <summary>Autostart, der beim Setzen scheitert (Registry gesperrt).</summary>
internal sealed class FailingAutostart(Exception failure) : IAutostartService
{
    public bool IsEnabled => false;

    public void SetEnabled(bool enabled) => throw failure;
}

/// <summary>Registry, deren Schreibzugriff scheitert (für Reconcile-Fehlerfall).</summary>
internal sealed class ReadOnlyRegistry(FakeRegistry inner) : IRegistry
{
    public IRegistryKey? OpenKey(RegistryHiveKind hive, string subKeyPath, RegistryViewKind view = RegistryViewKind.Default) => inner.OpenKey(hive, subKeyPath, view);

    public IRegistryKey CreateKey(RegistryHiveKind hive, string subKeyPath) => throw new UnauthorizedAccessException("Zugriff verweigert (Test)");
}

internal sealed class FakeTrayHost : ITrayHost
{
    private int _queries;

    public Dispatcher Dispatcher => WpfTestHost.Dispatcher;

    public TrayState State { get; set; } = TrayState.Initial;

    public List<string> Calls { get; } = [];

    /// <summary>Wie oft der Zustand gelesen wurde (läuft im Hintergrund).</summary>
    public int Queries => Volatile.Read(ref _queries);

    public TrayState QueryTrayState()
    {
        Interlocked.Increment(ref _queries);
        return State;
    }

    public Task ToggleHdrNowAsync()
    {
        Calls.Add("toggle");
        return Task.CompletedTask;
    }

    /// <summary>Globaler Auto-HDR-Schalter (Attrappe, nie die Registry).</summary>
    public AutoHdrSwitchState AutoHdr { get; set; } = AutoHdrSwitchState.Off;

    public int AutoHdrReads { get; private set; }

    /// <summary>HDRpilot hält den Schalter gerade für einen Spielstart (Attrappe).</summary>
    public bool AutoHdrSwitchHeld { get; set; }

    /// <summary>Wenn gesetzt, scheitert das Umschalten damit (nichts geändert).</summary>
    public Exception? AutoHdrSetFailure { get; set; }

    public AutoHdrSwitchState ReadAutoHdrSwitch()
    {
        AutoHdrReads++;
        return AutoHdr;
    }

    public AutoHdrSwitchState SetAutoHdrSwitch(bool on)
    {
        Calls.Add("autohdr:" + on);
        if (AutoHdrSetFailure is not null)
        {
            throw AutoHdrSetFailure;
        }

        AutoHdr = on ? AutoHdrSwitchState.On : AutoHdrSwitchState.Off;
        return AutoHdr;
    }

    public void SetPaused(bool paused) => Calls.Add("pause:" + paused);

    public void ShowMainWindow() => Calls.Add("open");

    public void ShowSettingsWindow() => Calls.Add("settings");

    public void OpenLogFolder() => Calls.Add("logs");

    public Task ExitAsync()
    {
        Calls.Add("exit");
        return Task.CompletedTask;
    }
}

/// <summary>Host, bei dem jeder Befehl scheitert (Menü darf dann nicht abstürzen).</summary>
internal sealed class ThrowingTrayHost : ITrayHost
{
    public Dispatcher Dispatcher => WpfTestHost.Dispatcher;

    public TrayState QueryTrayState() => throw new InvalidOperationException("Zustand");

    public Task ToggleHdrNowAsync() => Task.FromException(new InvalidOperationException("Umschalten"));

    public AutoHdrSwitchState ReadAutoHdrSwitch() => throw new InvalidOperationException("Auto HDR lesen");

    public bool AutoHdrSwitchHeld => throw new InvalidOperationException("Auto HDR gehalten");

    public AutoHdrSwitchState SetAutoHdrSwitch(bool on) => throw new InvalidOperationException("Auto HDR schalten");

    public void SetPaused(bool paused) => throw new InvalidOperationException("Pause");

    public void ShowMainWindow() => throw new InvalidOperationException("Fenster");

    public void ShowSettingsWindow() => throw new InvalidOperationException("Einstellungen");

    public void OpenLogFolder() => throw new System.ComponentModel.Win32Exception(2);

    public Task ExitAsync() => Task.FromException(new InvalidOperationException("Beenden"));
}

/// <summary>
/// Tests, die allein laufen müssen: Sie setzen <see cref="HdrPilot.App.Views.Shell.ShellMotion.Override"/> (DoEvents
/// pumpt verschachtelt und ließe sonst einen parallelen Test den Schalter mitten im Test zurücksetzen) oder zählen
/// GDI-/USER-Objekte des ganzen Testprozesses (parallele Tests würden mitzählen).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ShellSerialCollection
{
    public const string Name = "ShellSerial";
}

internal static class ShellTestFactory
{
    public const string ConfigPath = @"C:\Users\Test\AppData\Roaming\HdrPilot\config.json";

    public static ConfigService NewConfig(InMemoryFileSystem fs, Action<AppConfig>? setup = null)
    {
        var clock = new FakeClock();
        var service = new ConfigService(new ConfigStore(fs, ConfigPath, clock), clock);
        if (setup is not null)
        {
            service.Update(setup);
        }

        return service;
    }

    public static FakeHdrController TwoDisplays()
    {
        var hdr = FakeHdrController.WithPrimary(hdrOn: false);
        hdr.Displays.Add(new FakeDisplay(FakeHdrController.Second, "Odyssey G7") { Capable = false });
        return hdr;
    }
}

/// <summary>
/// Ein STA-Thread mit laufendem Dispatcher und einer WPF-Application samt HdrPilot-Ressourcen (Tokens, Controls,
/// Shell). Fenster werden dort nur erzeugt, nie angezeigt. Gibt es schon eine Application im Prozess, wird deren
/// Dispatcher benutzt.
/// </summary>
internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> Host = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Run(Action action) => Host.Value.Invoke(action);

    public static T Run<T>(Func<T> func) => Host.Value.Invoke(func);

    public static Dispatcher Dispatcher => Host.Value;

    /// <summary>Arbeitet ausstehende Dispatcher-Aufgaben ab (z. B. Bindungen mit DataBind-Priorität). Auf dem Host-Thread aufrufen.</summary>
    public static void DoEvents() => Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    /// <summary>Misst und ordnet ein Element (auch ein nie angezeigtes Fenster) an: Vorlagen und Listenzeilen entstehen.</summary>
    public static void Layout(FrameworkElement element, Size available)
    {
        if (element is Window window)
        {
            // Ein Fenster ohne Handle misst sich nicht selbst: Vorlage anwenden und deren Wurzel messen.
            window.ApplyTemplate();
            element = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(window, 0);
        }

        element.Measure(available);
        var width = double.IsInfinity(available.Width) ? element.DesiredSize.Width : available.Width;
        var height = double.IsInfinity(available.Height) ? element.DesiredSize.Height : Math.Min(available.Height, Math.Max(element.DesiredSize.Height, 1));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    /// <summary>Alle Elemente im visuellen Baum (Tiefensuche).</summary>
    public static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            if (current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
            {
                for (var i = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--)
                {
                    stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
                }
            }
        }
    }

    /// <summary>Tauscht die Farb-Token (hell/dunkel). Auf dem Host-Thread aufrufen.</summary>
    public static void UseTheme(bool dark)
    {
        var app = Application.Current;
        var merged = app.Resources.MergedDictionaries;
        var tokens = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Themes/Tokens.", StringComparison.Ordinal) == true);
        if (tokens is not null)
        {
            merged.Remove(tokens);
        }

        merged.Insert(0, Load(dark ? "Tokens.Dark.xaml" : "Tokens.Light.xaml"));
    }

    private static Dispatcher Start()
    {
        if (Application.Current is { } existing)
        {
            return existing.Dispatcher;
        }

        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                if (!app.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("Shell.xaml", StringComparison.Ordinal) == true))
                {
                    app.Resources.MergedDictionaries.Add(Load("Tokens.Dark.xaml"));
                    app.Resources.MergedDictionaries.Add(Load("Controls.xaml"));
                    app.Resources.MergedDictionaries.Add(Load("Shell.xaml"));
                }

                ready.SetResult(app.Dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "WPF-Testhost (Shell)",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    private static ResourceDictionary Load(string file) =>
        new() { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/" + file, UriKind.Absolute) };
}
