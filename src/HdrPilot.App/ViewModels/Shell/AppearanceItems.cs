using System.Windows.Input;
using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Theming;
using HdrPilot.Core.Config;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>Eine Farbvorlage in der Auswahl (4 × 2 bzw. 8 nebeneinander).</summary>
public sealed class PalettePresetItem : ObservableObject
{
    private readonly Action<PalettePresetItem> _select;
    private bool _isSelected;

    public PalettePresetItem(PalettePreset preset, string name, string tag, Action<PalettePresetItem> select)
    {
        Preset = preset;
        Name = name;
        Tag = tag;
        _select = select;
        var colors = preset.DefaultMode == PaletteMode.Tritone
            ? new[] { preset.Colors.Accent, preset.Colors.Atmosphere, preset.Colors.Shine }
            : new[] { preset.Colors.Accent, preset.Colors.Atmosphere };
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0.4), EndPoint = new System.Windows.Point(1, 0.6) };
        for (var i = 0; i < colors.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : i / (double)(colors.Length - 1)));
        }

        brush.Freeze();
        Gradient = brush;
    }

    public PalettePreset Preset { get; }

    public string Id => Preset.Id;

    /// <summary>Angezeigter Name aus ShellStrings.resx („Abendrot“).</summary>
    public string Name { get; }

    /// <summary>„DUO“ oder „TRI“: Standardanzahl der Vorlage.</summary>
    public string Tag { get; }

    /// <summary>Verlauf der Vorlage in ihrer Standardanzahl.</summary>
    public Brush Gradient { get; }

    /// <summary>Gewählt (Auswahl setzt Vorlage und ihre Standardanzahl).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
            {
                _select(this);
            }

            SetProperty(ref _isSelected, value);
        }
    }

    internal void SetSelected(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
}

/// <summary>Ein Farbfeld (Akzent, Atmosphäre, Glanz) mit Rolle, Hex-Wert und Farbwähler.</summary>
public sealed class ColorWellItem : ObservableObject
{
    private Color _color;
    private bool _isAutomatic;

    public ColorWellItem(int index, string role, string roleHint, ICommand edit)
    {
        Index = index;
        Role = role;
        RoleHint = roleHint;
        EditCommand = edit;
    }

    /// <summary>0 Akzent, 1 Atmosphäre, 2 Glanz.</summary>
    public int Index { get; }

    public string Number => (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string Role { get; }

    public string RoleHint { get; }

    public ICommand EditCommand { get; }

    public Color Color
    {
        get => _color;
        private set
        {
            if (SetProperty(ref _color, value))
            {
                OnPropertyChanged(nameof(Hex));
                OnPropertyChanged(nameof(Brush));
                OnPropertyChanged(nameof(Caption));
            }
        }
    }

    public string Hex => ColorMath.ToHex(_color);

    public Brush Brush => Freeze(new SolidColorBrush(_color));

    /// <summary>Glanz im Duoton: aus der Atmosphäre abgeleitet, nicht wählbar.</summary>
    public bool IsAutomatic
    {
        get => _isAutomatic;
        private set
        {
            if (SetProperty(ref _isAutomatic, value))
            {
                OnPropertyChanged(nameof(IsEditable));
                OnPropertyChanged(nameof(Caption));
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public bool IsEditable => !_isAutomatic;

    /// <summary>Hex-Wert oder „automatisch“.</summary>
    public string Caption => _isAutomatic ? Resources.ShellStrings.ColorAutomatic : Hex;

    public string AutomationName => _isAutomatic
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.ShellStrings.ColorWellAutomaticName, Role)
        : string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.ShellStrings.ColorWellName, Role);

    internal void Update(Color color, bool automatic)
    {
        Color = color;
        IsAutomatic = automatic;
    }

    private static T Freeze<T>(T freezable)
        where T : System.Windows.Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>Eine Zelle der Abdeckungstabelle: „631/636“ und „+3“ (gefunden, aber nicht genutzt).</summary>
public sealed record CoverageCellItem(int Used, int Found, int Games, string Text, string Extra, double Fraction, string AutomationName)
{
    public bool HasExtra => Extra.Length > 0;

    public bool IsZero => Used == 0;
}

/// <summary>Eine Zeile der Abdeckungstabelle je Launcher.</summary>
public sealed record CoverageRowItem(string SourceKey, string Source, int Games, CoverageCellItem Hero, CoverageCellItem Logo, CoverageCellItem Cover, CoverageCellItem Icon)
{
    /// <summary>Schlüssel der Quellenfarbe (ThemeKeys), etwa „Hp.Brush.Source.Steam“.</summary>
    public string SourceBrushKey => "Hp.Brush.Source." + SourceKey;
}

/// <summary>Ein echtes Beispiel für ein erzeugtes Bild („Titelbild aus Symbol“ …).</summary>
public sealed class FallbackExampleItem : ObservableObject
{
    private ImageSource? _image;
    private ImageSource? _original;

    public FallbackExampleItem(FallbackExample example, string title, Brush field)
    {
        Example = example;
        Title = title;
        Field = field;
    }

    public FallbackExample Example { get; }

    public FallbackKind Kind => Example.Kind;

    public string Title { get; }

    public string GameName => Example.GameName;

    public string Detail => Example.Detail;

    /// <summary>„Clive Barker's Hellraiser: Revival · Xbox · aus dem Kachelbild“.</summary>
    public string Caption => Example.GameName + " · " + Example.Detail;

    public string AutomationName => string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.ShellStrings.FallbackExampleName, Title, Example.GameName, Example.Detail);

    /// <summary>Farbfeld des Spiels (Hintergrund hinter Cover und Monogramm).</summary>
    public Brush Field { get; }

    /// <summary>Das erzeugte Bild (Titelbild oder Cover).</summary>
    public ImageSource? Image
    {
        get => _image;
        internal set => SetProperty(ref _image, value);
    }

    /// <summary>Nur bei der verschmierten Kapsel: das ursprüngliche Steam-Bild links vom Pfeil.</summary>
    public ImageSource? Original
    {
        get => _original;
        internal set
        {
            if (SetProperty(ref _original, value))
            {
                OnPropertyChanged(nameof(HasOriginal));
            }
        }
    }

    public bool HasOriginal => _original is not null;

    public bool IsCover => Kind is FallbackKind.CoverGenerated or FallbackKind.CoverFromSmearedCapsule or FallbackKind.Monogram;

    public bool ShowsName => Kind == FallbackKind.NameInsteadOfLogo;
}

/// <summary>Ein Spiel zur Auswahl in der Live-Vorschau.</summary>
public sealed class PreviewGameItem : ObservableObject
{
    private readonly Action<PreviewGameItem> _select;
    private ImageSource? _thumbnail;
    private bool _isSelected;

    public PreviewGameItem(GameConfig game, Action<PreviewGameItem> select)
    {
        Game = game;
        _select = select;
        var ambient = HdrPilot.Core.Artwork.AmbientColors.FromName(game.Name);
        Field = AppearancePageViewModel.FieldBrush(ambient);
    }

    public GameConfig Game { get; }

    public string Name => Game.Name;

    public string AutomationName => string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.ShellStrings.PreviewPickName, Game.Name);

    /// <summary>Farbfeld, solange kein Bild geladen ist.</summary>
    public Brush Field { get; }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        internal set => SetProperty(ref _thumbnail, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
            {
                _select(this);
            }

            SetProperty(ref _isSelected, value);
        }
    }

    internal void SetSelected(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
}

/// <summary>Eine Zustandskachel der Symbol-Vorschau (HDR aus, HDR ein, Pausiert, Spiel läuft).</summary>
public sealed record TrayStateItem(string Label, string Hint, ImageSource Icon, bool IsCurrent, string AutomationId);

/// <summary>Echte Größe 16/20/24/32 px: Symbol „aus“ und „ein“ in genau dieser Pixelgröße.</summary>
public sealed record TraySizeItem(int Pixels, double Size, ImageSource Off, ImageSource On, string Label);
