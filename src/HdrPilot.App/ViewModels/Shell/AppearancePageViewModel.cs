using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Theming;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>
/// Seite „Darstellung“ der Einstellungen (E89, G10.8): Farbschema (Duoton/Tritonus, 8 Vorlagen, eigene Farben, Kontrast),
/// Live-Vorschau mit echten Bildern und echten Nit-Werten, Thema, Effekte, Bilder (lokale Abdeckung, Ersatzbilder,
/// SteamGridDB optional) und Symbole/Tray. Jede Änderung gilt sofort und wird gespeichert
/// (<see cref="IAppearanceService.Update"/>); „Abbrechen“ der Einstellungen nimmt sie nicht zurück. Bilder und Bildschirmwerte
/// werden nur gelesen, solange die Seite lebt (Freigaben in <see cref="Dispose"/> zurückgegeben).
/// </summary>
public sealed class AppearancePageViewModel : ObservableObject, IDisposable
{
    /// <summary>Breite, in der das Titelbild der Vorschau dekodiert wird (Karte bis etwa 760 px, doppelt für hohe DPI).</summary>
    public const int PreviewHeroWidth = 1280;

    private static readonly Color Night = Color.FromRgb(0x05, 0x06, 0x0A);

    private readonly IShellContext _context;
    private readonly IAppearanceService _appearance;
    private readonly ILogger _logger;
    private readonly Func<bool> _windowsAnimations;
    private readonly IDisposable _artworkLease;
    private readonly IDisposable _luminanceLease;
    private readonly CancellationTokenSource _cancel = new();
    private readonly List<GameConfig> _games;
    private readonly AsyncRelayCommand _checkKeyCommand;
    private IReadOnlyList<CoverageRowItem> _coverage = [];
    private IReadOnlyList<FallbackExampleItem> _examples = [];
    private bool _coverageLoaded;
    private bool _examplesLoaded;
    private PreviewGameItem? _previewGame;
    private GameArtwork? _previewArt;
    private bool _previewHdr;
    private int _previewVersion;
    private bool _taskbarLight;
    private double _dpiScale = 1;
    private string? _steamGridDbKey;
    private string? _steamGridDbMessage;
    private bool _steamGridDbMessageIsError;
    private ColorWellItem? _pickerWell;
    private Color _pickerColor;
    private string? _errorMessage;
    private IReadOnlyList<TrayStateItem> _trayStates = [];
    private IReadOnlyList<TraySizeItem> _traySizes = [];
    private ImageSource? _windowIconPreview;
    private ImageSource? _trayIconPreview;
    private bool _disposed;

    public AppearancePageViewModel(IShellContext context, ILogger? logger = null, Func<bool>? windowsAnimations = null)
    {
        _context = context;
        _appearance = context.Appearance;
        _logger = logger ?? NullLogger.Instance;
        _windowsAnimations = windowsAnimations ?? (() => SystemParameters.ClientAreaAnimation);
        _games = [.. context.Config.Current.Games];
        _taskbarLight = _appearance.Current.LightTaskbar;

        SwapCommand = new RelayCommand(Swap);
        ResetCommand = new RelayCommand(Reset);
        EditColorCommand = new RelayCommand(p => OpenPicker(p as ColorWellItem), p => p is ColorWellItem { IsEditable: true });
        ApplyPickerCommand = new RelayCommand(ApplyPicker);
        CancelPickerCommand = new RelayCommand(ClosePicker);
        _checkKeyCommand = new AsyncRelayCommand(CheckSteamGridDbKeyAsync, () => !_disposed);

        Wells =
        [
            new ColorWellItem(0, ShellStrings.RoleAccent, ShellStrings.RoleAccentHint, EditColorCommand),
            new ColorWellItem(1, ShellStrings.RoleAtmosphere, ShellStrings.RoleAtmosphereHint, EditColorCommand),
            new ColorWellItem(2, ShellStrings.RoleShine, ShellStrings.RoleShineHint, EditColorCommand),
        ];
        Presets = PalettePresets.All
            .Select(p => new PalettePresetItem(
                p,
                ShellStrings.ResourceManager.GetString(p.NameResourceKey, CultureInfo.CurrentUICulture) ?? p.Id,
                p.DefaultMode == PaletteMode.Tritone ? ShellStrings.PresetTagTritone : ShellStrings.PresetTagDuotone,
                SelectPreset))
            .ToList();
        PreviewGames = PickPreviewGames(_games).Select(g => new PreviewGameItem(g, SelectPreviewGame)).ToList();

        _artworkLease = context.Artwork.Acquire();
        _luminanceLease = context.Luminance.Acquire();
        _appearance.Changed += OnAppearanceChanged;
        context.SteamGridDb.Changed += OnSteamGridDbChanged;
        context.Luminance.Changed += OnLuminanceChanged;
        context.Artwork.Changed += OnArtworkChanged;

        UpdatePalette();
        UpdateTrayPreview();
        Loaded = LoadAsync();
    }

    // ---- Befehle ----

    public ICommand SwapCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand EditColorCommand { get; }

    public ICommand ApplyPickerCommand { get; }

    public ICommand CancelPickerCommand { get; }

    public ICommand CheckSteamGridDbCommand => _checkKeyCommand;

    /// <summary>Ende des ersten Ladens (Abdeckung, Beispiele, Vorschau); für Bildschirmfotos und Tests.</summary>
    public Task Loaded { get; }

    public AppearanceState State => _appearance.Current;

    /// <summary>Speicherfehler beim sofortigen Anwenden; null, wenn alles gespeichert ist.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorMessage is not null;

    // ---- Farbschema ----

    public bool IsDuotone
    {
        get => State.Mode == PaletteMode.Duotone;
        set
        {
            if (value && !IsDuotone)
            {
                SetMode(PaletteMode.Duotone);
            }
        }
    }

    public bool IsTritone
    {
        get => State.Mode == PaletteMode.Tritone;
        set
        {
            if (value && !IsTritone)
            {
                SetMode(PaletteMode.Tritone);
            }
        }
    }

    public IReadOnlyList<ColorWellItem> Wells { get; }

    public IReadOnlyList<PalettePresetItem> Presets { get; }

    /// <summary>„Abendrot · Duoton, Glanz automatisch“ oder „Eigene Farben · Tritonus, 3 Farben“.</summary>
    public string PresetCaption
    {
        get
        {
            var name = State.PresetId == PalettePresets.CustomId
                ? ShellStrings.PresetCustom
                : Presets.FirstOrDefault(p => p.Id == State.PresetId)?.Name ?? ShellStrings.PresetCustom;
            return string.Format(CultureInfo.CurrentCulture, IsTritone ? ShellStrings.PresetCaptionTritone : ShellStrings.PresetCaptionDuotone, name);
        }
    }

    /// <summary>Verlaufsbalken Akzent → Atmosphäre → Glanz.</summary>
    public Brush PaletteBar
    {
        get
        {
            var p = State.Palette;
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            brush.GradientStops.Add(new GradientStop(p.Accent, 0));
            brush.GradientStops.Add(new GradientStop(p.Atmosphere, 0.5));
            brush.GradientStops.Add(new GradientStop(p.Shine, 1));
            brush.Freeze();
            return brush;
        }
    }

    public bool AutoTextOnAccent
    {
        get => State.AutoTextOnAccent;
        set
        {
            if (value != State.AutoTextOnAccent)
            {
                Write(s => s.AutoTextOnAccent = value);
            }
        }
    }

    /// <summary>Farben des aktuellen Themas (für die Kontrast-Kennzeichen).</summary>
    public ThemePalette ThemeColors => PaletteEngine.Compute(State);

    /// <summary>„5,8:1 · AA“: Text auf der schwächeren Seite des Akzentverlaufs.</summary>
    public string OnAccentContrast => ContrastText(ThemeColors.OnAccentContrast);

    public ContrastGrade OnAccentGrade => PaletteEngine.Grade(ThemeColors.OnAccentContrast);

    /// <summary>„7,8:1 · AAA“: Akzent als Textfarbe auf der Kartenfläche.</summary>
    public string AccentTextContrast => ContrastText(ThemeColors.AccentTextContrast);

    public ContrastGrade AccentTextGrade => PaletteEngine.Grade(ThemeColors.AccentTextContrast);

    /// <summary>Farb-Ressource des Kennzeichens „Auf Akzent“ (grün ab AA, gelb bei „AA groß“, rot darunter).</summary>
    public string OnAccentGradeBrush => GradeBrush(OnAccentGrade, soft: false);

    public string OnAccentGradeSoftBrush => GradeBrush(OnAccentGrade, soft: true);

    public string AccentTextGradeBrush => GradeBrush(AccentTextGrade, soft: false);

    public string AccentTextGradeSoftBrush => GradeBrush(AccentTextGrade, soft: true);

    public bool BackgroundFollowsGame
    {
        get => State.BackgroundFollowsGame;
        set
        {
            if (value != State.BackgroundFollowsGame)
            {
                Write(s => s.BackgroundFollowsGame = value);
            }
        }
    }

    // ---- Farbwähler ----

    /// <summary>Feld, dessen Farbe gerade gewählt wird; null: Farbwähler zu.</summary>
    public ColorWellItem? PickerWell
    {
        get => _pickerWell;
        private set
        {
            if (SetProperty(ref _pickerWell, value))
            {
                OnPropertyChanged(nameof(IsPickerOpen));
            }
        }
    }

    public bool IsPickerOpen
    {
        get => _pickerWell is not null;
        set
        {
            if (!value)
            {
                ClosePicker();
            }
        }
    }

    /// <summary>Farbe im offenen Farbwähler (noch nicht übernommen).</summary>
    public Color PickerColor
    {
        get => _pickerColor;
        set => SetProperty(ref _pickerColor, value);
    }

    // ---- Thema ----

    public bool IsThemeSystem
    {
        get => State.ThemePreference == ThemePreference.System;
        set => SetTheme(value, ThemePreference.System);
    }

    public bool IsThemeLight
    {
        get => State.ThemePreference == ThemePreference.Light;
        set => SetTheme(value, ThemePreference.Light);
    }

    public bool IsThemeDark
    {
        get => State.ThemePreference == ThemePreference.Dark;
        set => SetTheme(value, ThemePreference.Dark);
    }

    /// <summary>„Folgt Windows · gerade dunkel“, „Immer hell“ oder „Immer dunkel“.</summary>
    public string ThemeCaption => State.ThemePreference switch
    {
        ThemePreference.Light => ShellStrings.ThemeCaptionLight,
        ThemePreference.Dark => ShellStrings.ThemeCaptionDark,
        _ => string.Format(CultureInfo.CurrentCulture, ShellStrings.ThemeCaptionSystem, State.IsDark ? ShellStrings.ThemeNowDark : ShellStrings.ThemeNowLight),
    };

    // ---- Effekte ----

    public bool IsEffectFull
    {
        get => State.EffectLevelSetting == EffectLevel.Full;
        set => SetEffect(value, EffectLevel.Full);
    }

    public bool IsEffectReduced
    {
        get => State.EffectLevelSetting == EffectLevel.Reduced;
        set => SetEffect(value, EffectLevel.Reduced);
    }

    public bool IsEffectOff
    {
        get => State.EffectLevelSetting == EffectLevel.Off;
        set => SetEffect(value, EffectLevel.Off);
    }

    /// <summary>„Voll“ gewählt, Windows-Animationen aus: „Reduziert“ ist wirksam (Kennzeichen an der Karte).</summary>
    public bool ReducedIsEffective => State.ReducedByWindows && State.EffectLevel == EffectLevel.Reduced;

    /// <summary>Wirksame Stufe (für die Vorführkarten).</summary>
    public EffectLevel EffectiveLevel => State.EffectLevel;

    public bool PauseWhilePlaying
    {
        get => State.PauseWhilePlaying;
        set
        {
            if (value != State.PauseWhilePlaying)
            {
                Write(s => s.PauseWhilePlaying = value);
            }
        }
    }

    public bool PauseInBackground
    {
        get => State.PauseInBackground;
        set
        {
            if (value != State.PauseInBackground)
            {
                Write(s => s.PauseInBackground = value);
            }
        }
    }

    public bool RespectWindowsAnimations
    {
        get => State.RespectWindowsAnimations;
        set
        {
            if (value != State.RespectWindowsAnimations)
            {
                Write(s => s.RespectWindowsAnimations = value);
            }
        }
    }

    /// <summary>Hinweis mit dem Zustand der Windows-Option „Animationseffekte“.</summary>
    public string RespectWindowsAnimationsHint =>
        State.ReducedByWindows ? ShellStrings.RespectWindowsAnimationsHintReduced
        : !_windowsAnimations() ? ShellStrings.RespectWindowsAnimationsHintIgnored
        : ShellStrings.RespectWindowsAnimationsHintOn;

    public bool MinimizeToTrayAfterLaunch
    {
        get => State.MinimizeToTrayAfterLaunch;
        set
        {
            if (value != State.MinimizeToTrayAfterLaunch)
            {
                Write(s => s.MinimizeToTrayAfterLaunch = value);
            }
        }
    }

    // ---- Bilder ----

    public IReadOnlyList<CoverageRowItem> Coverage
    {
        get => _coverage;
        private set
        {
            if (SetProperty(ref _coverage, value))
            {
                OnPropertyChanged(nameof(CoverageStatus));
                OnPropertyChanged(nameof(HasCoverage));
            }
        }
    }

    public bool HasCoverage => _coverage.Count > 0;

    /// <summary>„Bilder werden gezählt …“, „Noch keine Spiele eingelesen.“ oder leer, wenn die Tabelle steht.</summary>
    public string CoverageStatus => _coverage.Count > 0 ? string.Empty : _coverageLoaded ? ShellStrings.CoverageEmpty : ShellStrings.CoverageLoading;

    public IReadOnlyList<FallbackExampleItem> FallbackExamples
    {
        get => _examples;
        private set
        {
            if (SetProperty(ref _examples, value))
            {
                OnPropertyChanged(nameof(HasFallbackExamples));
                OnPropertyChanged(nameof(FallbackStatus));
            }
        }
    }

    public bool HasFallbackExamples => _examples.Count > 0;

    public string FallbackStatus => _examples.Count > 0 ? string.Empty : _examplesLoaded ? ShellStrings.FallbackExamplesEmpty : ShellStrings.CoverageLoading;

    // ---- SteamGridDB ----

    public SteamGridDbStatus SteamGridDbStatus => _context.SteamGridDb.Status;

    /// <summary>Schalter „SteamGridDB verwenden“ (Standard aus); schreibt settings.artwork.steamGridDb.</summary>
    public bool SteamGridDbEnabled
    {
        get => SteamGridDbStatus.Enabled;
        set
        {
            if (value == SteamGridDbStatus.Enabled)
            {
                return;
            }

            try
            {
                _context.SteamGridDb.SetEnabled(value);
            }
            catch (IOException ex)
            {
                ErrorMessage = string.Format(CultureInfo.CurrentCulture, ShellStrings.SaveFailed, ex.Message);
                _logger.LogWarning(ex, "SteamGridDB-Schalter ließ sich nicht speichern.");
            }

            RaiseSteamGridDb();
        }
    }

    /// <summary>„Füllt nur fehlende Bilder: 14 Titelbilder, 22 Logos, 17 Cover. …“</summary>
    public string SteamGridDbHint
    {
        get
        {
            var s = SteamGridDbStatus;
            return string.Format(CultureInfo.CurrentCulture, ShellStrings.SteamGridDbHint, s.MissingHero, s.MissingLogo, s.MissingCover);
        }
    }

    /// <summary>Stand des Netzwerkzugriffs (klare Anzeige, G10.4).</summary>
    public string SteamGridDbStatusText
    {
        get
        {
            if (_steamGridDbMessage is { } message)
            {
                return message;
            }

            var s = SteamGridDbStatus;
            if (!s.Enabled)
            {
                return ShellStrings.SteamGridDbStatusOff;
            }

            if (s.Key == SteamGridDbKeyState.Unreadable)
            {
                return ShellStrings.SteamGridDbStatusUnreadable;
            }

            if (s.Key == SteamGridDbKeyState.Missing)
            {
                return ShellStrings.SteamGridDbStatusNoKey;
            }

            if (s.IsRunning)
            {
                return ShellStrings.SteamGridDbStatusRunning;
            }

            if (s.LastError is { } error)
            {
                return string.Format(CultureInfo.CurrentCulture, ShellStrings.SteamGridDbStatusError, error);
            }

            return s.LastRun is { } last
                ? string.Format(CultureInfo.CurrentCulture, ShellStrings.SteamGridDbStatusLastRun, last.ToLocalTime(), s.LastRequests, s.LastFilled)
                : ShellStrings.SteamGridDbStatusReady;
        }
    }

    public bool SteamGridDbStatusIsError => _steamGridDbMessageIsError || SteamGridDbStatus.Key == SteamGridDbKeyState.Unreadable;

    /// <summary>Eingabe im Schlüsselfeld (PasswordBox, nur in diese Richtung); nie gespeichert, bevor „Prüfen“ gedrückt ist.</summary>
    public void SetSteamGridDbKey(string? key) => _steamGridDbKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();

    /// <summary>Wird gefeuert, wenn das Schlüsselfeld geleert werden soll (nach dem Speichern).</summary>
    public event EventHandler? SteamGridDbKeyClearRequested;

    // ---- Live-Vorschau ----

    public IReadOnlyList<PreviewGameItem> PreviewGames { get; }

    public bool HasPreviewGames => PreviewGames.Count > 0;

    public PreviewGameItem? PreviewGame => _previewGame;

    public string PreviewName => _previewGame?.Name ?? ShellStrings.PreviewEmpty;

    public string PreviewSource => _previewGame is null ? string.Empty : SourceName(_previewGame.Game.Source);

    public string PreviewSourceBrushKey => _previewGame is null ? "Hp.Brush.Source.Manual" : "Hp.Brush.Source." + _previewGame.Game.Source;

    /// <summary>„vor 2 Tagen“ wie in der Bibliothek (Datum ohne Uhrzeit, wenn älter als fünf Wochen).</summary>
    public string PreviewWhen => _previewGame?.Game.LastPlayed is { } played ? RelativeDate(played, DateTimeOffset.Now) : ShellStrings.PreviewNeverPlayed;

    public ImageSource? PreviewHero => _previewArt?.Hero?.Image;

    /// <summary>Vorab aufgehellte Fassung für HDR an (null, solange nicht berechnet: dann nur der Schleier weg).</summary>
    public ImageSource? PreviewBoost => _previewArt?.HeroBoost?.Image;

    public ImageSource? PreviewLogo => _previewArt?.Logo?.Image;

    public bool PreviewShowsName => PreviewLogo is null;

    public ImageSource? PreviewField => _previewArt?.Field?.Image;

    /// <summary>Farbfeld des Spiels, solange kein Titelbild geladen ist.</summary>
    public Brush PreviewFieldBrush => FieldBrush(PreviewAmbientColors);

    /// <summary>Umgebungsfarben der Vorschau: aus dem Bild, wenn „Hintergrund folgt dem Spiel“, sonst aus dem Schema.</summary>
    public AmbientColors PreviewAmbientColors => _previewArt?.Ambient ?? AmbientColors.FromName(_previewGame?.Name ?? string.Empty);

    /// <summary>Leuchten der Vorschau (Spitzlicht) in der Glanzfarbe des Spiels bzw. des Schemas.</summary>
    public Brush PreviewGlow
    {
        get
        {
            var shine = State.BackgroundFollowsGame && ColorMath.TryParseHex(PreviewAmbientColors.Shine, out var s) ? s : State.Palette.Shine;
            var light = PreviewAmbientColors;
            var center = new Point(Math.Clamp(light.LightX, 0.3, 0.62), Math.Clamp(light.LightY, 0.3, 0.75));
            var brush = new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = 0.4, RadiusY = 0.7 };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(Math.Clamp((light.Highlight * 0.85) - 0.05, 0.14, 0.7) * 255), 0xFF, 0xF6, 0xE2), 0));
            brush.GradientStops.Add(new GradientStop(ColorMath.WithAlpha(shine, 0.32), 0.25));
            brush.GradientStops.Add(new GradientStop(ColorMath.WithAlpha(shine, 0), 1));
            brush.Freeze();
            return brush;
        }
    }

    public bool PreviewHdr
    {
        get => _previewHdr;
        set
        {
            if (SetProperty(ref _previewHdr, value))
            {
                OnPropertyChanged(nameof(PreviewHdrText));
            }
        }
    }

    public string PreviewHdrText => _previewHdr ? ShellStrings.PreviewHdrOn : ShellStrings.PreviewHdrOff;

    /// <summary>Spitze des Bildschirms mit HDR („456 nits“, nie ein fester Wert); „—“, solange unbekannt.</summary>
    public string PreviewNitsText => _context.Luminance.Primary is { } d && NitScale.Maximum(d) > 0
        ? string.Format(CultureInfo.CurrentCulture, ShellStrings.PreviewNits, NitScale.Format(NitScale.Maximum(d)))
        : ShellStrings.PreviewNitsUnknown;

    /// <summary>Farbfeld des gewählten Spiels für „Hintergrund folgt dem Spiel“ (Bild oder Verlauf).</summary>
    public Brush FollowSample => PreviewField is { } image
        ? Freeze(new ImageBrush(image) { Stretch = Stretch.Fill })
        : PreviewFieldBrush;

    // ---- Symbole und Tray ----

    /// <summary>Taskleiste der Vorschau (Standard: wie Windows); ändert nur die Vorschau.</summary>
    public bool TaskbarLight
    {
        get => _taskbarLight;
        set
        {
            if (SetProperty(ref _taskbarLight, value))
            {
                OnPropertyChanged(nameof(TaskbarDark));
                UpdateTrayPreview();
            }
        }
    }

    public bool TaskbarDark
    {
        get => !_taskbarLight;
        set => TaskbarLight = !value;
    }

    /// <summary>DPI-Faktor des Fensters, damit „Echte Größe“ genau in Bildschirmpixeln zeichnet.</summary>
    public double DpiScale
    {
        get => _dpiScale;
        set
        {
            if (value > 0 && Math.Abs(value - _dpiScale) > 0.01)
            {
                _dpiScale = value;
                UpdateTrayPreview();
            }
        }
    }

    public IReadOnlyList<TrayStateItem> TrayStates
    {
        get => _trayStates;
        private set => SetProperty(ref _trayStates, value);
    }

    public IReadOnlyList<TraySizeItem> TraySizes
    {
        get => _traySizes;
        private set => SetProperty(ref _traySizes, value);
    }

    public ImageSource? WindowIconPreview
    {
        get => _windowIconPreview;
        private set => SetProperty(ref _windowIconPreview, value);
    }

    public ImageSource? TrayIconPreview
    {
        get => _trayIconPreview;
        private set => SetProperty(ref _trayIconPreview, value);
    }

    /// <summary>Gerade HDR an (Bildschirmwerte oder Hauptbildschirm).</summary>
    public bool HdrOnNow
    {
        get
        {
            if (_context.Luminance.Primary is { } nits)
            {
                return nits.HdrActive;
            }

            try
            {
                return _context.GetDisplays().Any(d => d.IsPrimary && d.HdrEnabled);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }
    }

    /// <summary>Zeile im Kopf der Tray-Menü-Vorschau: „HDR aus · MAG 341C OLED“.</summary>
    public string TrayMenuStatus
    {
        get
        {
            var display = _context.Luminance.Primary?.FriendlyName;
            if (string.IsNullOrWhiteSpace(display))
            {
                try
                {
                    display = _context.GetDisplays().FirstOrDefault(d => d.IsPrimary)?.FriendlyName;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    display = null;
                }
            }

            var state = HdrOnNow ? ShellStrings.TrayStatusHdrOn : ShellStrings.TrayStatusHdrOff;
            return string.IsNullOrWhiteSpace(display) ? state : string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayPreviewStatus, state, display);
        }
    }

    /// <summary>„Spitze 456 · Vollbild 254 nits · 10 Bit“; null, solange die Werte unbekannt sind.</summary>
    public string? TrayMenuNits => _context.Luminance.Primary is { } d && d.PeakNits > 0
        ? string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayMenuNits, NitScale.Format(NitScale.Maximum(d)), NitScale.Format(d.FullFrameNits), d.BitsPerColor)
        : null;

    public bool HasTrayMenuNits => TrayMenuNits is not null;

    public string TrayMenuToggleText => HdrOnNow ? ShellStrings.TrayMenuToggleHdrOff : ShellStrings.TrayMenuToggleHdrOn;

    /// <summary>„Auto HDR · Gears of War: E…“ mit dem zuletzt gespielten Spiel.</summary>
    public string TrayMenuAutoHdrText => PreviewGames.FirstOrDefault() is { } game
        ? string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayPreviewItemWithGame, ShellStrings.TrayMenuAutoHdr, Shorten(game.Name, 16))
        : ShellStrings.TrayMenuAutoHdr;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancel.Cancel();
        _appearance.Changed -= OnAppearanceChanged;
        _context.SteamGridDb.Changed -= OnSteamGridDbChanged;
        _context.Luminance.Changed -= OnLuminanceChanged;
        _context.Artwork.Changed -= OnArtworkChanged;
        _artworkLease.Dispose();
        _luminanceLease.Dispose();
        _steamGridDbKey = null;
        SteamGridDbKeyClearRequested = null;
        _cancel.Dispose();
    }

    /// <summary>Farbfeld aus Umgebungsfarben (Ersatz ohne Bild, Prototyp nameField()).</summary>
    public static Brush FieldBrush(AmbientColors ambient)
    {
        var a1 = ColorMath.TryParseHex(ambient.Deep, out var deep) ? deep : Night;
        var a2 = ColorMath.TryParseHex(ambient.Glow, out var glow) ? glow : a1;
        var a3 = ColorMath.TryParseHex(ambient.Shine, out var shine) ? shine : a2;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.2), EndPoint = new Point(1, 0.8) };
        brush.GradientStops.Add(new GradientStop(a1, 0));
        brush.GradientStops.Add(new GradientStop(ColorMath.Mix(a2, Night, 0.35), 0.55));
        brush.GradientStops.Add(new GradientStop(ColorMath.Mix(a3, Night, 0.45), 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>Die vier zuletzt gespielten Spiele (sonst die ersten nach Namen) für die Vorschau.</summary>
    public static IReadOnlyList<GameConfig> PickPreviewGames(IReadOnlyList<GameConfig> games)
    {
        var played = games.Where(g => g.LastPlayed is not null).OrderByDescending(g => g.LastPlayed).Take(4).ToList();
        if (played.Count < 4)
        {
            played.AddRange(games.Where(g => g.LastPlayed is null).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Take(4 - played.Count));
        }

        return played;
    }

    /// <summary>Datum wie in der Bibliothek: „heute, 14:05“, „gestern, …“, „vor 3 Tagen“, „vor 2 Wochen“, sonst „6. Okt. 2026“.</summary>
    public static string RelativeDate(DateTimeOffset value, DateTimeOffset now)
    {
        var local = value.ToLocalTime().DateTime;
        var days = (now.ToLocalTime().Date - local.Date).Days;
        var weeks = (int)Math.Round(days / 7.0);
        var culture = CultureInfo.CurrentCulture;
        return days switch
        {
            <= 0 => string.Format(culture, ShellStrings.RelativeToday, local),
            1 => string.Format(culture, ShellStrings.RelativeYesterday, local),
            < 7 => string.Format(culture, ShellStrings.RelativeDays, days),
            < 35 => weeks <= 1 ? ShellStrings.RelativeOneWeek : string.Format(culture, ShellStrings.RelativeWeeks, weeks),
            _ => string.Format(culture, ShellStrings.RelativeDate, local),
        };
    }

    private static string SourceName(GameSource source) =>
        LibraryStrings.ResourceManager.GetString("Source_" + source, CultureInfo.CurrentUICulture) ?? source.ToString();

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)].TrimEnd() + ShellStrings.Ellipsis;

    private static string GradeBrush(ContrastGrade grade, bool soft) =>
        (grade switch
        {
            ContrastGrade.Aaa or ContrastGrade.Aa => ThemeKeys.BrushSuccess,
            ContrastGrade.AaLarge => ThemeKeys.BrushWarning,
            _ => ThemeKeys.BrushDanger,
        }) + (soft ? "Soft" : string.Empty);

    private static string ContrastText(double ratio)
    {
        var grade = PaletteEngine.Grade(ratio) switch
        {
            ContrastGrade.Aaa => ShellStrings.ContrastAaa,
            ContrastGrade.Aa => ShellStrings.ContrastAa,
            ContrastGrade.AaLarge => ShellStrings.ContrastAaLarge,
            _ => ShellStrings.ContrastTooLow,
        };

        // Abgerundet auf eine Stelle: 4,49 darf nie als „4,5:1 · AA“ erscheinen.
        return string.Format(CultureInfo.CurrentCulture, ShellStrings.ContrastFormat, Math.Floor(ratio * 10) / 10, grade);
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private void Write(Action<AppearanceSettings> change)
    {
        try
        {
            _appearance.Update(change);
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, ShellStrings.SaveFailed, ex.Message);
            _logger.LogWarning(ex, "Darstellung ließ sich nicht speichern.");
        }
    }

    private void SetMode(PaletteMode mode) => Write(s => s.PaletteMode = mode.ToString());

    private void SelectPreset(PalettePresetItem item) => Write(s =>
    {
        s.Preset = item.Id;
        s.PaletteMode = null;
        s.Accent = null;
        s.Atmosphere = null;
        s.Shine = null;
    });

    /// <summary>Eigene Farben: Vorlage wird „custom“, alle drei Farben gespeichert (Glanz bleibt für den Wechsel zu Tritonus).</summary>
    private void WriteCustom(Color accent, Color atmosphere, Color shine) => Write(s =>
    {
        s.Preset = PalettePresets.CustomId;
        s.PaletteMode = State.Mode.ToString();
        s.Accent = ColorMath.ToHex(accent);
        s.Atmosphere = ColorMath.ToHex(atmosphere);
        s.Shine = ColorMath.ToHex(shine);
    });

    private void Swap()
    {
        var p = State.Palette;
        var shine = State.Mode == PaletteMode.Duotone ? PalettePresets.DerivedShine(p.Accent) : p.Shine;
        WriteCustom(p.Atmosphere, p.Accent, shine);
    }

    private void Reset()
    {
        try
        {
            _appearance.ResetToDefaults();
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, ShellStrings.SaveFailed, ex.Message);
            _logger.LogWarning(ex, "Darstellung ließ sich nicht zurücksetzen.");
        }
    }

    /// <summary>Übernimmt eine eigene Farbe für ein Feld (0 Akzent, 1 Atmosphäre, 2 Glanz nur im Tritonus).</summary>
    public void ApplyColor(int index, Color color)
    {
        var p = State.Palette;
        color = Color.FromRgb(color.R, color.G, color.B);
        switch (index)
        {
            case 0:
                WriteCustom(color, p.Atmosphere, p.Shine);
                break;
            case 1:
                WriteCustom(p.Accent, color, State.Mode == PaletteMode.Duotone ? PalettePresets.DerivedShine(color) : p.Shine);
                break;
            case 2 when State.Mode == PaletteMode.Tritone:
                WriteCustom(p.Accent, p.Atmosphere, color);
                break;
        }
    }

    private void OpenPicker(ColorWellItem? well)
    {
        if (well is not { IsEditable: true })
        {
            return;
        }

        PickerColor = well.Color;
        PickerWell = well;
    }

    private void ApplyPicker()
    {
        if (_pickerWell is { } well)
        {
            ApplyColor(well.Index, _pickerColor);
        }

        ClosePicker();
    }

    private void ClosePicker() => PickerWell = null;

    private void SetTheme(bool on, ThemePreference theme)
    {
        if (on && State.ThemePreference != theme)
        {
            Write(s => s.Theme = theme.ToString());
        }
    }

    private void SetEffect(bool on, EffectLevel level)
    {
        if (on && State.EffectLevelSetting != level)
        {
            Write(s => s.EffectLevel = level.ToString());
        }
    }

    private void SelectPreviewGame(PreviewGameItem item)
    {
        if (ReferenceEquals(_previewGame, item))
        {
            return;
        }

        foreach (var other in PreviewGames)
        {
            other.SetSelected(ReferenceEquals(other, item));
        }

        _previewGame = item;
        _previewArt = null;
        RaisePreview();
        _ = LoadPreviewAsync(item, ++_previewVersion);
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        UpdatePalette();
        UpdateTrayPreview();

        // Alle abgeleiteten Werte neu lesen (Bindungen an diese Seite).
        OnPropertyChanged(string.Empty);
    }

    private void OnSteamGridDbChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            RaiseSteamGridDb();
        }
    }

    private void OnLuminanceChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        OnPropertyChanged(nameof(PreviewNitsText));
        OnPropertyChanged(nameof(TrayMenuNits));
        OnPropertyChanged(nameof(HasTrayMenuNits));
        OnPropertyChanged(nameof(TrayMenuStatus));
        OnPropertyChanged(nameof(TrayMenuToggleText));
        UpdateTrayPreview();
    }

    private void OnArtworkChanged(object? sender, ArtworkChangedEventArgs e)
    {
        if (_disposed || _previewGame is null)
        {
            return;
        }

        if (e.GameKey is null || string.Equals(e.GameKey, _previewGame.Game.Key, StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadPreviewAsync(_previewGame, ++_previewVersion);
        }
    }

    private void RaiseSteamGridDb()
    {
        OnPropertyChanged(nameof(SteamGridDbStatus));
        OnPropertyChanged(nameof(SteamGridDbEnabled));
        OnPropertyChanged(nameof(SteamGridDbHint));
        OnPropertyChanged(nameof(SteamGridDbStatusText));
        OnPropertyChanged(nameof(SteamGridDbStatusIsError));
    }

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(PreviewGame));
        OnPropertyChanged(nameof(PreviewName));
        OnPropertyChanged(nameof(PreviewSource));
        OnPropertyChanged(nameof(PreviewSourceBrushKey));
        OnPropertyChanged(nameof(PreviewWhen));
        OnPropertyChanged(nameof(PreviewHero));
        OnPropertyChanged(nameof(PreviewBoost));
        OnPropertyChanged(nameof(PreviewLogo));
        OnPropertyChanged(nameof(PreviewShowsName));
        OnPropertyChanged(nameof(PreviewField));
        OnPropertyChanged(nameof(PreviewFieldBrush));
        OnPropertyChanged(nameof(PreviewAmbientColors));
        OnPropertyChanged(nameof(PreviewGlow));
        OnPropertyChanged(nameof(FollowSample));
    }

    private void UpdatePalette()
    {
        var p = State.Palette;
        Wells[0].Update(p.Accent, automatic: false);
        Wells[1].Update(p.Atmosphere, automatic: false);
        Wells[2].Update(p.Shine, automatic: State.Mode == PaletteMode.Duotone);
        foreach (var preset in Presets)
        {
            preset.SetSelected(preset.Id == State.PresetId);
        }

        if (_pickerWell is { IsEditable: false })
        {
            ClosePicker();
        }
    }

    /// <summary>Zustandskacheln, echte Größen und Taskleiste in den Farben des Schemas (nur bei Änderung neu gezeichnet).</summary>
    private void UpdateTrayPreview()
    {
        try
        {
            var palette = IconPalette.From(PaletteEngine.Compute(State.Palette, dark: true, State.AutoTextOnAccent));
            var off = new TrayIconSpec(false, false, Palette: palette, LightTaskbar: _taskbarLight);
            var on = off with { HdrOn = true };
            var paused = off with { Paused = true };
            var running = on with { GameRunning = true };
            var current = HdrOnNow ? on : off;
            var tile = (int)Math.Round(46 * Math.Max(1, _dpiScale) * 2);
            TrayStates =
            [
                new TrayStateItem(ShellStrings.TrayStateOff, ShellStrings.TrayStateOffHint, IconImages.Render(off, tile), current == off, "TrayStateOff"),
                new TrayStateItem(ShellStrings.TrayStateOn, ShellStrings.TrayStateOnHint, IconImages.Render(on, tile), current == on, "TrayStateOn"),
                new TrayStateItem(ShellStrings.TrayStatePaused, ShellStrings.TrayStatePausedHint, IconImages.Render(paused, tile), false, "TrayStatePaused"),
                new TrayStateItem(ShellStrings.TrayStateRunning, ShellStrings.TrayStateRunningHint, IconImages.Render(running, tile), false, "TrayStateRunning"),
            ];
            TraySizes = new[] { 16, 20, 24, 32 }
                .Select(size =>
                {
                    var px = (int)Math.Round(size * _dpiScale);
                    return new TraySizeItem(px, size, IconImages.Render(off, px), IconImages.Render(on, px), string.Format(CultureInfo.CurrentCulture, ShellStrings.TraySizePixels, size));
                })
                .ToList();
            WindowIconPreview = IconImages.Render(current, (int)Math.Round(24 * _dpiScale * 2));
            TrayIconPreview = IconImages.Render(current, (int)Math.Round(16 * _dpiScale));
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Vorschau der Symbole ließ sich nicht zeichnen.");
        }
    }

    private async Task LoadAsync()
    {
        var first = PreviewGames.FirstOrDefault();
        if (first is not null)
        {
            first.SetSelected(true);
            _previewGame = first;
            RaisePreview();
        }

        await Task.WhenAll(
            LoadCoverageAsync(),
            LoadExamplesAsync(),
            first is null ? Task.CompletedTask : LoadPreviewAsync(first, ++_previewVersion),
            LoadThumbnailsAsync());
    }

    private async Task LoadCoverageAsync()
    {
        try
        {
            var coverage = await _context.Artwork.GetCoverageAsync(_games, _cancel.Token);
            if (_disposed)
            {
                return;
            }

            Coverage = coverage.Rows.Where(r => r.Games > 0).Select(ToRow).ToList();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Abdeckung mit lokalen Bildern nicht lesbar.");
        }

        _coverageLoaded = true;
        if (!_disposed)
        {
            OnPropertyChanged(nameof(CoverageStatus));
        }
    }

    private CoverageRowItem ToRow(ArtworkCoverageRow row)
    {
        var source = SourceName(row.Source);
        CoverageCellItem Cell(ArtworkCount c, string kind)
        {
            var extra = c.Found > c.Used ? "+" + (c.Found - c.Used).ToString(CultureInfo.CurrentCulture) : string.Empty;
            var name = string.Format(CultureInfo.CurrentCulture, ShellStrings.CoverageCellName, source, kind, c.Used, c.Found, row.Games);
            return new CoverageCellItem(
                c.Used,
                c.Found,
                row.Games,
                c.Used.ToString(CultureInfo.CurrentCulture) + "/" + row.Games.ToString(CultureInfo.CurrentCulture),
                extra,
                row.Games == 0 ? 0 : Math.Clamp(c.Used / (double)row.Games, 0, 1),
                name);
        }

        return new CoverageRowItem(
            row.Source.ToString(),
            source,
            row.Games,
            Cell(row.Hero, ShellStrings.CoverageHero),
            Cell(row.Logo, ShellStrings.CoverageLogo),
            Cell(row.Cover, ShellStrings.CoverageCover),
            Cell(row.Icon, ShellStrings.CoverageIcon));
    }

    private async Task LoadExamplesAsync()
    {
        try
        {
            var examples = await _context.Artwork.GetFallbackExamplesAsync(_games, _cancel.Token);
            if (_disposed)
            {
                return;
            }

            var items = examples
                .GroupBy(e => e.Kind)
                .Select(g => g.First())
                .Where(e => e.Kind != FallbackKind.Monogram || examples.Count < 4)
                .Take(4)
                .Select(e => new FallbackExampleItem(
                    e,
                    ShellStrings.ResourceManager.GetString("FallbackKind_" + e.Kind, CultureInfo.CurrentUICulture) ?? e.Kind.ToString(),
                    FieldBrush(AmbientColors.FromName(e.GameName))))
                .ToList();
            FallbackExamples = items;
            await Task.WhenAll(items.Select(LoadExampleImagesAsync));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Beispiele der erzeugten Bilder nicht lesbar.");
        }

        _examplesLoaded = true;
        if (!_disposed)
        {
            OnPropertyChanged(nameof(FallbackStatus));
        }
    }

    private async Task LoadExampleImagesAsync(FallbackExampleItem item)
    {
        var game = _games.FirstOrDefault(g => string.Equals(g.Key, item.Example.GameKey, StringComparison.OrdinalIgnoreCase));
        if (game is null)
        {
            return;
        }

        try
        {
            item.Image = item.IsCover
                ? await _context.Artwork.GetImageAsync(game, ArtworkKind.Cover, 300, _cancel.Token)
                : await _context.Artwork.GetImageAsync(game, ArtworkKind.Hero, 720, _cancel.Token);
            if (item.Kind == FallbackKind.CoverFromSmearedCapsule)
            {
                var candidates = await _context.Artwork.GetCandidatesAsync(game, ArtworkKind.Cover, _cancel.Token);
                item.Original = candidates.FirstOrDefault(c => c.Origin == ArtworkOrigin.Launcher)?.Preview;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Beispielbild für {Game} nicht lesbar.", item.GameName);
        }
    }

    private async Task LoadPreviewAsync(PreviewGameItem item, int version)
    {
        try
        {
            var art = await _context.Artwork.GetArtworkAsync(item.Game, PreviewHeroWidth, _cancel.Token);
            if (_disposed || version != _previewVersion)
            {
                return;
            }

            _previewArt = art;
            RaisePreview();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bilder der Vorschau für {Game} nicht lesbar.", item.Name);
        }
    }

    private async Task LoadThumbnailsAsync()
    {
        foreach (var item in PreviewGames)
        {
            try
            {
                item.Thumbnail = await _context.Artwork.GetImageAsync(item.Game, ArtworkKind.Header, 192, _cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vorschaubild für {Game} nicht lesbar.", item.Name);
            }
        }
    }

    /// <summary>„Prüfen“: eine Anfrage an SteamGridDB mit dem eingegebenen Schlüssel; gültig (oder nicht prüfbar) wird gespeichert.</summary>
    private async Task CheckSteamGridDbKeyAsync()
    {
        if (_steamGridDbKey is not { } key)
        {
            SetSteamGridDbMessage(ShellStrings.SteamGridDbKeyMissing, isError: true);
            return;
        }

        SetSteamGridDbMessage(ShellStrings.SteamGridDbChecking, isError: false);
        SteamGridDbCheckResult result;
        try
        {
            result = await _context.SteamGridDb.CheckKeyAsync(key, _cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SteamGridDB-Schlüssel ließ sich nicht prüfen.");
            result = SteamGridDbCheckResult.NetworkError;
        }

        if (_disposed)
        {
            return;
        }

        if (result != SteamGridDbCheckResult.Invalid)
        {
            try
            {
                _context.SteamGridDb.SaveKey(key);
                _steamGridDbKey = null;
                SteamGridDbKeyClearRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                _logger.LogWarning(ex, "SteamGridDB-Schlüssel ließ sich nicht speichern.");
                SetSteamGridDbMessage(string.Format(CultureInfo.CurrentCulture, ShellStrings.SteamGridDbSaveFailed, ex.Message), isError: true);
                return;
            }
        }

        SetSteamGridDbMessage(
            result switch
            {
                SteamGridDbCheckResult.Valid => ShellStrings.SteamGridDbCheckValid,
                SteamGridDbCheckResult.Invalid => ShellStrings.SteamGridDbCheckInvalid,
                SteamGridDbCheckResult.RateLimited => ShellStrings.SteamGridDbCheckRateLimited,
                _ => ShellStrings.SteamGridDbCheckNetworkError,
            },
            isError: result != SteamGridDbCheckResult.Valid);
    }

    private void SetSteamGridDbMessage(string? message, bool isError)
    {
        _steamGridDbMessage = message;
        _steamGridDbMessageIsError = isError;
        RaiseSteamGridDb();
    }
}
