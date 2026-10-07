using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
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

    public IReadOnlyList<DisplayInfo> GetDisplays() => Hdr.GetDisplays();

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
    public Dispatcher Dispatcher { get; } = Dispatcher.CurrentDispatcher;

    public TrayState State { get; set; } = TrayState.Initial;

    public List<string> Calls { get; } = [];

    public TrayState QueryTrayState() => State;

    public Task ToggleHdrNowAsync()
    {
        Calls.Add("toggle");
        return Task.CompletedTask;
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
