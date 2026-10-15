using HdrPilot.Core.Hdr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services.Display;

/// <summary>Einstellungen des Helligkeitsdienstes.</summary>
public sealed class DisplayLuminanceServiceOptions
{
    /// <summary>Liest die Werte aller Ausgänge (DXGI; in Tests eine Attrappe).</summary>
    public required IDisplayLuminanceReader Reader { get; init; }

    /// <summary>Aktive Bildschirme mit GDI-Namen und HDR-Zustand (QueryDisplayConfig).</summary>
    public required Func<IReadOnlyList<DisplayInfo>> Displays { get; init; }

    /// <summary>Zielbildschirme aus den Einstellungen (monitorDevicePath); leer: Hauptbildschirm.</summary>
    public Func<IReadOnlyList<string>> TargetDisplays { get; init; } = () => [];

    /// <summary>Gemerkte HDR-Spitze je Bildschirm (null: nichts merken).</summary>
    public DisplayLuminanceStore? Store { get; init; }

    public Action<Action> PostToUi { get; init; } = action => action();

    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;

    public ILogger? Logger { get; init; }
}

/// <summary>
/// Echte Helligkeitswerte der Bildschirme (E89, G10.7) über DXGI <c>IDXGIOutput6::GetDesc1</c>, zugeordnet über den GDI-Namen.
/// Gelesen wird im Hintergrund und nur, solange ein Fenster die Werte zeigt (<see cref="Acquire"/>), und dann nur beim ersten
/// Zeigen und nach <see cref="Invalidate"/>, wenn sich die Bildschirme oder ihr HDR-Zustand wirklich geändert haben; ohne
/// Fenster nie. Jede Messung mit aktivem HDR wird gemerkt (display-luminance.json): <see cref="DisplayNits.PeakNits"/> ist
/// die HDR-Spitze, auch wenn HDR gerade aus ist (beim MAG 341C 456 Nits), und damit das Maximum jeder Nit-Skala. Ereignisse
/// kommen auf dem UI-Thread.
/// </summary>
public sealed class DisplayLuminanceService : IDisplayLuminanceService
{
    private readonly DisplayLuminanceServiceOptions _options;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private IReadOnlyList<DisplayNits> _displays = [];
    private DisplayNits? _primary;
    private int _leases;
    private bool _stale = true;
    private bool _reading;
    private bool _readAgain;
    private bool _disposed;
    private int _readCount;
    private string? _signature;

    public DisplayLuminanceService(DisplayLuminanceServiceOptions options)
    {
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
    }

    public event EventHandler? Changed;

    public DisplayNits? Primary
    {
        get
        {
            lock (_gate)
            {
                return _primary;
            }
        }
    }

    public IReadOnlyList<DisplayNits> Displays
    {
        get
        {
            lock (_gate)
            {
                return _displays;
            }
        }
    }

    /// <summary>Anzahl der DXGI-Abfragen (Prüfung: nur nach einem echten Wechsel und nur mit Freigabe).</summary>
    public int ReadCount => Volatile.Read(ref _readCount);

    /// <summary>Läuft gerade eine Abfrage (Tests warten darauf).</summary>
    public Task Idle { get; private set; } = Task.CompletedTask;

    public void Invalidate()
    {
        lock (_gate)
        {
            _stale = true;
            if (_leases == 0 || _disposed)
            {
                return;
            }
        }

        StartRead();
    }

    public IDisposable Acquire()
    {
        bool read;
        lock (_gate)
        {
            if (_disposed)
            {
                return Artwork.NoLease.Instance;
            }

            _leases++;
            read = _stale;
        }

        if (read)
        {
            StartRead();
        }

        return new Lease(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    /// <summary>Zuordnung und Zusammenführung mit der gemerkten HDR-Spitze (ohne DXGI, für Tests offen).</summary>
    public static IReadOnlyList<DisplayNits> Combine(
        IReadOnlyList<DisplayLuminance> readings,
        IReadOnlyList<DisplayInfo> displays,
        DisplayLuminanceStore? store,
        DateTimeOffset now)
    {
        var result = new List<DisplayNits>();
        foreach (var display in displays)
        {
            if (display.GdiDeviceName is not { Length: > 0 } gdi
                || readings.FirstOrDefault(r => string.Equals(r.GdiDeviceName, gdi, StringComparison.OrdinalIgnoreCase)) is not { } reading)
            {
                continue;
            }

            var hdr = reading.HdrActive || display.HdrEnabled;
            if (reading.HdrActive && reading.MaxLuminance > 0)
            {
                store?.Remember(display.MonitorDevicePath, reading.MaxLuminance, reading.MaxFullFrameLuminance, reading.MinLuminance, reading.BitsPerColor, now);
            }

            var stored = store?.Get(display.MonitorDevicePath);
            result.Add(stored is { PeakNits: > 0 }
                ? new DisplayNits(display.MonitorDevicePath, display.FriendlyName, stored.PeakNits, stored.FullFrameNits, stored.MinNits, stored.Bits, hdr, PeakMeasuredWithHdr: true)
                : new DisplayNits(display.MonitorDevicePath, display.FriendlyName, reading.MaxLuminance, reading.MaxFullFrameLuminance, reading.MinLuminance, reading.BitsPerColor, hdr, PeakMeasuredWithHdr: reading.HdrActive));
        }

        return result;
    }

    /// <summary>Erster Zielbildschirm: aus den Einstellungen, sonst der Hauptbildschirm, sonst der erste mit Werten.</summary>
    public static DisplayNits? ChoosePrimary(IReadOnlyList<DisplayNits> nits, IReadOnlyList<DisplayInfo> displays, IReadOnlyList<string> targets)
    {
        DisplayNits? Of(DisplayInfo? display) => display is null ? null : nits.FirstOrDefault(n => string.Equals(n.MonitorDevicePath, display.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
        if (targets.Count > 0)
        {
            foreach (var target in targets)
            {
                if (Of(displays.FirstOrDefault(d => string.Equals(d.MonitorDevicePath, target, StringComparison.OrdinalIgnoreCase))) is { } chosen)
                {
                    return chosen;
                }
            }
        }

        return Of(displays.FirstOrDefault(d => d.IsPrimary)) ?? nits.FirstOrDefault();
    }

    private void StartRead()
    {
        lock (_gate)
        {
            if (_reading)
            {
                _readAgain = true;
                return;
            }

            _reading = true;
            _stale = false;
            Idle = Task.Run(ReadLoop);
        }
    }

    private void ReadLoop()
    {
        while (true)
        {
            Read();
            lock (_gate)
            {
                if (!_readAgain || _leases == 0 || _disposed)
                {
                    _reading = false;
                    _readAgain = false;
                    return;
                }

                _readAgain = false;
                _stale = false;
            }
        }
    }

    /// <summary>Kennung der Bildschirme samt HDR-Zustand: ändert sie sich nicht, bleibt DXGI unberührt.</summary>
    public static string DisplaySignature(IReadOnlyList<DisplayInfo> displays) => string.Join(
        '|',
        displays.Select(d => d.MonitorDevicePath + "@" + d.GdiDeviceName + ":" + d.HdrEnabled + ":" + d.IsPrimary + ":" + d.BitsPerColorChannel));

    private void Read()
    {
        IReadOnlyList<DisplayNits> nits;
        DisplayNits? primary;
        try
        {
            // Die Liste der Bildschirme kostet kaum etwas (QueryDisplayConfig); DXGI nur, wenn sich daran etwas geändert hat.
            var displays = _options.Displays();
            var signature = DisplaySignature(displays) + "#" + string.Join(';', _options.TargetDisplays());
            lock (_gate)
            {
                if (signature == _signature)
                {
                    return;
                }
            }

            Interlocked.Increment(ref _readCount);
            var readings = _options.Reader.ReadAll();
            lock (_gate)
            {
                _signature = signature;
            }

            nits = Combine(readings, displays, _options.Store, _options.Now());
            primary = ChoosePrimary(nits, displays, _options.TargetDisplays());
            _logger.LogDebug(
                "Helligkeit: {Count} Ausgänge gelesen, Hauptziel {Name}: Spitze {Peak} nits, Vollbild {Full} nits, Minimum {Min} nits, {Bits} Bit, HDR {Hdr}.",
                readings.Count, primary?.FriendlyName, primary?.PeakNits, primary?.FullFrameNits, primary?.MinNits, primary?.BitsPerColor, primary?.HdrActive);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Helligkeit: Werte nicht gelesen.");
            return;
        }

        bool changed;
        lock (_gate)
        {
            changed = _primary != primary || !_displays.SequenceEqual(nits);
            _displays = nits;
            _primary = primary;
        }

        if (changed)
        {
            _options.PostToUi(() =>
            {
                try
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Helligkeit: Fehler in einem Empfänger von Changed.");
                }
            });
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_leases > 0)
            {
                _leases--;
            }
        }
    }

    private sealed class Lease(DisplayLuminanceService owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
