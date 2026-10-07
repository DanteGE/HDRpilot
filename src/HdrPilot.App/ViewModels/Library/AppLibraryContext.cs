using System.Windows.Threading;
using HdrPilot.App.Services;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Verbindet die Spielebibliothek mit <see cref="AppServices"/>. Meldet sich bei Konfiguration und App an und beim
/// Freigeben (Fenster geschlossen) wieder ab, damit nach dem Schließen nichts vom Fenster im Speicher bleibt.
/// </summary>
public sealed class AppLibraryContext : ILibraryContext
{
    private readonly AppServices _services;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ILogger _logger;
    private bool _disposed;

    public AppLibraryContext(AppServices services)
    {
        _services = services;
        _logger = services.LoggerFactory.CreateLogger("Library");
        Ui = new WpfUiDispatcher(services.Dispatcher);
        services.Config.Changed += OnConfigChanged;
        services.HdrStateChanged += OnHdrStateChanged;
        services.ScanStateChanged += OnScanStateChanged;
    }

    public event EventHandler? ConfigChanged;

    public event EventHandler? HdrStateChanged;

    public event EventHandler? ScanStateChanged;

    public AppConfig Config => _services.Config.Current;

    public IFileSystem FileSystem => _services.FileSystem;

    public IAppEnvironment Environment => _services.Environment;

    public IClock Clock => _services.Host.Clock;

    public IGameIconService Icons => _services.Icons;

    public IShortcutService Shortcuts => _services.Shortcuts;

    public IUiDispatcher Ui { get; }

    public IReadOnlyList<SessionInfo> Sessions => _services.Sessions.Sessions;

    public bool IsPaused => _services.Sessions.Paused;

    public bool IsScanning => _services.IsScanning;

    public LibraryScanReport? LastScan => _services.LastScan;

    public async Task UpdateConfigAsync(Action<AppConfig> mutate)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => _services.Config.Update(mutate)).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        try
        {
            return _services.Hdr.GetDisplays();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bildschirme ließen sich nicht abfragen.");
            return [];
        }
    }

    public IReadOnlyList<DisplayInfo> TargetDisplays()
    {
        try
        {
            return _services.TargetDisplays();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Zielbildschirme ließen sich nicht abfragen.");
            return [];
        }
    }

    public void SetPaused(bool paused) => _services.SetPaused(paused);

    public Task ToggleHdrNowAsync() => _services.ToggleHdrNowAsync();

    public Task RescanAsync() => _services.RescanAsync();

    public Task<LaunchOutcome> LaunchAsync(string gameKey) => _services.LaunchAsync(gameKey);

    public void ShowSettings() => _services.ShowSettingsWindow();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.Config.Changed -= OnConfigChanged;
        _services.HdrStateChanged -= OnHdrStateChanged;
        _services.ScanStateChanged -= OnScanStateChanged;
        ConfigChanged = null;
        HdrStateChanged = null;
        ScanStateChanged = null;

        // Symbole gehören zum Fenster: im Hintergrund kein Bildspeicher.
        (_services.Icons as GameIconService)?.ClearCache();
    }

    // ConfigService.Changed kann von einem Hintergrund-Thread kommen (Speichern, Automatik, Einlesen).
    private void OnConfigChanged(object? sender, EventArgs e) => _services.RaiseOnUi(() =>
    {
        if (!_disposed)
        {
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }
    });

    private void OnHdrStateChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            HdrStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnScanStateChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            ScanStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>UI-Thread über den WPF-Dispatcher. Verzögerungen sind einmalige Zeitgeber, die sich selbst anhalten.</summary>
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action) => dispatcher.BeginInvoke(DispatcherPriority.Background, action);

    public IDisposable Delay(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
        return new TimerHandle(timer);
    }

    private sealed class TimerHandle(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
