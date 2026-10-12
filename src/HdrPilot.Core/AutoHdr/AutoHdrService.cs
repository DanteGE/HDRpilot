using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Windows Auto HDR je Spiel (E88), getrennt von „HDR automatisch“: Für Spiele mit <see cref="GameConfig.AutoHdr"/> trägt
/// HDRpilot je Spiel-EXE <c>AutoHDREnable=2097</c> in den Schlüssel UserGpuPreferences ein und nimmt es beim Ausschalten
/// wieder zurück; die Notlösung <see cref="AppSettings.AutoHdrGlobalFallback"/> setzt zusätzlich den globalen Schalter,
/// solange ein solches Spiel läuft. Abgleich beim Start und bei jeder Konfigurationsänderung, Rücknahme auf Wunsch.
/// <para>Gerüst: noch ohne Wirkung (schreibt nichts, meldet keinen Stand).</para>
/// </summary>
public sealed class AutoHdrService : IAutoHdrService, IAutoHdrSessionHooks
{
    private readonly IGpuPreferenceStore _store;
    private readonly ILogger _logger;

    public AutoHdrService(
        ConfigService config,
        IGpuPreferenceStore store,
        AutoHdrRegistryLocation location,
        IFileSystem fileSystem,
        AppPaths paths,
        IAppEnvironment environment,
        IClock clock,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        Location = location;
        _logger = logger ?? NullLogger.Instance;
    }

    public AutoHdrRegistryLocation Location { get; }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <summary>Beim Start der Automatik, vor dem ersten Zyklus: einmal abgleichen. Wirft nie.</summary>
    public void Start() => _logger.LogDebug("Auto HDR: Ziel {Key}.", _store.KeyName);

    /// <summary>Konfiguration geändert (Option, gelernte EXEs, Notlösung): abgleichen. Wirft nie.</summary>
    public void OnConfigChanged()
    {
    }

    /// <summary>Beim Beenden der Automatik (nach dem letzten Zyklus). Wirft nie.</summary>
    public void Shutdown()
    {
    }

    public AutoHdrGameStatus GetStatus(string gameKey) => AutoHdrGameStatus.None;

    public Task<AutoHdrRollbackResult> RollbackAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AutoHdrRollbackResult(0, 0, 0, 0));

    public void OnStartAnnounced(GameConfig game, SessionTrigger trigger, DateTimeOffset now)
    {
    }

    public void OnRecovery(IReadOnlyCollection<string> runningGameKeys, AppConfig config, DateTimeOffset now)
    {
    }

    public void OnCycle(IReadOnlyCollection<string> runningGameKeys, AppConfig config, bool paused, DateTimeOffset now)
    {
    }
}
