using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.Artwork;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Bilder der Spielfarben (E94, G15.6) mit erzeugten Spielbildern (nie echte Bilder, nie Launcher-Caches): Fenster
/// 1600×900, Effekte aus, Schublade zu. Geprüft werden der Starten-Knopf gegen den erwarteten Verlauf an seiner Stelle,
/// graue Spiele gegen das Bild ohne Schalter, der ruhige Grund, der Glanz im Tritonus, der Hinweis aus dem Titelbild, die
/// dunkle Markenfarbe und Bernstein im hellen Thema. Mit HDRPILOT_LIBRARY_RENDER_DIR entstehen 18 PNG zur Sichtprüfung
/// (gamecolors-&lt;Fall&gt;-&lt;Ansicht&gt;-&lt;Thema&gt;.png); phase15-gamecolors.ps1 prüft sie.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class GameColorRenderTests
{
    private static readonly Size WindowSize = new(1600, 900);

    [Fact]
    public void Rot() => Run("rot", Scene.Red, [LibraryViewMode.List, LibraryViewMode.Shelf], [true, false]);

    [Fact]
    public void Blau() => Run("blau", Scene.Blue, [LibraryViewMode.List], [true, false]);

    [Fact]
    public void Grau() => Run("grau", Scene.Gray, [LibraryViewMode.List], [true, false], preset: "royal", check: (r, dark) =>
    {
        // Gleich dem Bild ohne Schalter: genau das Farbschema.
        var off = Render("grau", Scene.Gray, LibraryViewMode.List, dark, colors: false, background: true, PaletteMode.Tritone, "royal");
        Assert.True(MeanDeltaE(r.Bitmap, off.Bitmap) <= 0.01, $"mittlere Abweichung {MeanDeltaE(r.Bitmap, off.Bitmap):0.0000}");
        Assert.Equal(off.Window.ShownAccents, r.Window.ShownAccents);
        off.Window.Close();
    });

    [Fact]
    public void BuntDuo() => Run("bunt-duo", Scene.Colorful, [LibraryViewMode.Grid], [true, false], mode: PaletteMode.Duotone, check: (r, _) =>
    {
        Assert.InRange(Hue(r.Window.ShownAccents.Accent), 245, 275); // Blau aus dem Logo
        var atmosphere = Hue(r.Window.ShownAccents.Atmosphere);
        Assert.True(atmosphere is >= 120 and <= 175 || ColorScience.HueDistance(atmosphere, Hue(r.Window.ShownAccents.Accent)) is >= 30 and <= 45, $"Atmosphäre {atmosphere:0}°");
    });

    [Fact]
    public void BuntTri() => Run("bunt-tri", Scene.Colorful, [LibraryViewMode.Grid], [true, false], mode: PaletteMode.Tritone, check: (r, _) =>
    {
        // Gelb aus dem Logo liegt 161° vom blauen Akzent (über 150°, E94): der Glanz kommt vom Bogen hinter der Atmosphäre.
        var shown = r.Window.ShownAccents;
        Assert.NotEqual(PalettePresets.DerivedShine(shown.Atmosphere), shown.Shine);
        Assert.True(ColorScience.HueDistance(Hue(shown.Accent), Hue(shown.Shine)) <= 150);
        Assert.True(ColorScience.Mud(Rgba(shown.Accent), Rgba(shown.Shine)) >= 0.7, "Akzent → Glanz ohne graue Mitte");
        Assert.True(ColorScience.Mud(Rgba(shown.Atmosphere), Rgba(shown.Shine)) >= 0.7, "Atmosphäre → Glanz ohne graue Mitte");
    });

    [Fact]
    public void BgAus() => Run("bg-aus", Scene.Red, [LibraryViewMode.List], [true, false], background: false, check: (r, _) =>
    {
        var backdrop = (FrameworkElement)r.Window.FindName("Ambient");
        var glow = Pixel(RenderElement(backdrop), backdrop.ActualWidth * 0.8, backdrop.ActualHeight * 0.15);
        Assert.True(ColorMath.Chroma(glow) <= 0.05, $"Leuchten {ColorMath.ToHex(glow)} C {ColorMath.Chroma(glow):0.000}");
    });

    [Fact]
    public void Hint() => Run("hint", Scene.RedOnSky, [LibraryViewMode.Shelf], [true], check: (r, _) =>
    {
        var artwork = r.Window.ViewModel!.Hero!.Artwork!;
        var hint = LibraryArt.Parse(artwork.Ambient.Glow);
        var expected = GamePalette.Map(r.Colors, PaletteMode.Duotone, hint);
        Assert.Equal(expected, r.Window.ShownAccents);
    });

    [Fact]
    public void Navy() => Run("navy", Scene.Navy, [LibraryViewMode.List], [true, false], check: (r, _) =>
        Assert.True(ColorMath.Chroma(r.Window.ShownAccents.Accent) >= 0.14, ColorMath.ToHex(r.Window.ShownAccents.Accent)));

    [Fact]
    public void Gold() => Run("gold", Scene.Gold, [LibraryViewMode.List], [false], check: (r, _) =>
    {
        var text = ((SolidColorBrush)r.Window.FindResource(ThemeKeys.BrushAccentText)).Color;
        Assert.InRange(Hue(text), 70, 80);
        Assert.InRange(Hue(r.Window.FindResource(ThemeKeys.ColorAccentText) is Color c ? c : default), 70, 80);
        Assert.InRange(Hue(((SolidColorBrush)r.Window.FindResource(ThemeKeys.BrushFocusRing)).Color), 70, 80);
    });

    private enum Scene
    {
        Red,
        Blue,
        Gray,
        Colorful,
        RedOnSky,
        Navy,
        Gold,
    }

    private static void Run(
        string name,
        Scene scene,
        LibraryViewMode[] views,
        bool[] themes,
        PaletteMode mode = PaletteMode.Duotone,
        bool background = true,
        string? preset = null,
        Action<RenderResult, bool>? check = null)
    {
        WpfTestHost.Run(() =>
        {
            ResourceDictionary? scheme = null;
            if (preset is not null)
            {
                var state = AppearanceService.Compute(new AppearanceSettings { Preset = preset, PaletteMode = mode.ToString() }, true, true, true, false);
                scheme = ThemeResources.Build(PaletteEngine.Compute(state.Palette, true, true));
                Application.Current.Resources.MergedDictionaries.Add(scheme);
            }

            try
            {
                foreach (var dark in themes)
                {
                    foreach (var view in views)
                    {
                        var result = Render(name, scene, view, dark, colors: true, background, mode, preset);
                        Assert.Equal((1600, 900), (result.Bitmap.PixelWidth, result.Bitmap.PixelHeight));
                        AssertLaunchButton(result);
                        check?.Invoke(result, dark);
                        Save(result.Bitmap, $"gamecolors-{name}-{ViewName(view)}-{(dark ? "dunkel" : "hell")}.png");
                        result.Window.Close();
                    }
                }
            }
            finally
            {
                if (scheme is not null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(scheme);
                }

                WpfTestHost.UseTheme(dark: true);
            }
        });
    }

    private static RenderResult Render(string name, Scene scene, LibraryViewMode view, bool dark, bool colors, bool background, PaletteMode mode, string? preset)
    {
        WpfTestHost.UseTheme(dark);
        EnsureLibraryResources();
        var (game, art) = Art(scene);
        var context = new TestLibraryContext(game)
        {
            Appearance = new StaticAppearanceService(new AppearanceSettings
            {
                EffectLevel = "Off",
                ColorsFollowGame = colors,
                BackgroundFollowsGame = background,
                PaletteMode = mode.ToString(),
                Preset = preset,
            }),
            Artwork = art,
        };
        context.ConfigService.Update(c => c.Settings.LibraryView = LibraryViewModes.ToConfig(view));
        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games[0];
        context.Dispatcher.RunDelayed();
        var window = new MainWindow();
        window.Attach(model);
        WpfTestHost.Layout(window, WindowSize);
        window.FitDetailsColumn();
        context.Dispatcher.RunDelayed();
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, WindowSize);
        var root = (Visual)VisualTreeHelper.GetChild(window, 0);
        var bitmap = new RenderTargetBitmap((int)WindowSize.Width, (int)WindowSize.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        _ = name;
        return new RenderResult(window, bitmap, art.Colors);
    }

    /// <summary>Starten-Knopf 8 px innen links auf halber Höhe gegen den Akzentverlauf an dieser Stelle (ΔE_OK ≤ 0,03).</summary>
    private static void AssertLaunchButton(RenderResult r)
    {
        var button = new[] { "LaunchButton", "HeroLaunchButton", "CompactLaunchButton" }
            .Select(n => r.Window.FindName(n) as Button)
            .FirstOrDefault(b => b is { ActualWidth: > 40, ActualHeight: > 20 } && IsShown(b) && Inside(r.Window, b));
        Assert.NotNull(button);
        var at = button!.TranslatePoint(new Point(8, button.ActualHeight / 2), (UIElement)VisualTreeHelper.GetChild(r.Window, 0));
        var pixel = Pixel(r.Bitmap, at.X, at.Y);

        var accent = (LinearGradientBrush)button.FindResource(ThemeKeys.BrushAccent);
        double u = 8 / button.ActualWidth, v = 0.5;
        var (sx, sy, ex, ey) = (accent.StartPoint.X, accent.StartPoint.Y, accent.EndPoint.X, accent.EndPoint.Y);
        var t = Math.Clamp((((u - sx) * (ex - sx)) + ((v - sy) * (ey - sy))) / (((ex - sx) * (ex - sx)) + ((ey - sy) * (ey - sy))), 0, 1);
        var expected = ColorMath.Mix(accent.GradientStops[0].Color, accent.GradientStops[1].Color, t);
        var delta = ColorScience.DeltaE(Rgba(pixel), Rgba(expected));
        Assert.True(delta <= 0.03, $"Starten: {ColorMath.ToHex(pixel)} statt {ColorMath.ToHex(expected)} (ΔE {delta:0.000})");
    }

    private static (GameConfig Game, GeneratedArtwork Art) Art(Scene scene)
    {
        var game = Game(GameSource.Steam, "test", scene switch
        {
            Scene.Red => "Testspiel Rot",
            Scene.Blue => "Testspiel Blau",
            Scene.Gray => "Testspiel Grau",
            Scene.Colorful => "Testspiel Bunt",
            Scene.RedOnSky => "Testspiel Rot",
            Scene.Navy => "Testspiel Navy",
            _ => "Testspiel Gold",
        });
        var red = GeneratedArt.Logo(new LogoSpec { Fill = GeneratedArt.C("#D62828"), Outline = 2 });
        var (logo, cover, hero) = scene switch
        {
            Scene.Red => (red, GeneratedArt.Cover(CoverScene.NightPortrait), (ArtImage?)null),
            Scene.Blue => (GeneratedArt.Logo(new LogoSpec { Fill = GeneratedArt.C("#FFFFFF") }), GeneratedArt.Cover(CoverScene.Blue), null),
            Scene.Gray => (GeneratedArt.Logo(new LogoSpec { Fill = GeneratedArt.C("#101010") }), GeneratedArt.Cover(CoverScene.Gray), GeneratedArt.Hero(HeroScene.Gray)),
            Scene.Colorful => (GeneratedArt.Logo(new LogoSpec { Bands = [(0.5, GeneratedArt.C("#1E64DC")), (0.33, GeneratedArt.C("#FFD400")), (0.17, GeneratedArt.C("#E02020"))] }), GeneratedArt.Cover(CoverScene.Jungle), null),
            Scene.RedOnSky => (red, GeneratedArt.Cover(CoverScene.NightPortrait), GeneratedArt.Hero(HeroScene.BlueSky)),
            Scene.Navy => (GeneratedArt.Logo(new LogoSpec { Fill = GeneratedArt.C("#0B1F4D") }), null, null),
            _ => (GeneratedArt.Logo(new LogoSpec { Fill = GeneratedArt.C("#E8B500") }), null, null),
        };
        return (game, new GeneratedArtwork(game.Key, logo, cover, hero));
    }

    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is UIElement { Visibility: not Visibility.Visible } || e is UIElement { Opacity: < 0.99 })
            {
                return false;
            }
        }

        return true;
    }

    private static bool Inside(Window window, FrameworkElement element)
    {
        var root = (UIElement)VisualTreeHelper.GetChild(window, 0);
        var box = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        return box.Left >= 0 && box.Top >= 0 && box.Right <= WindowSize.Width && box.Bottom <= WindowSize.Height;
    }

    private static Color Pixel(BitmapSource bitmap, double x, double y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(Math.Clamp((int)x, 0, bitmap.PixelWidth - 1), Math.Clamp((int)y, 0, bitmap.PixelHeight - 1), 1, 1), pixels, 4, 0);
        var a = pixels[3] / 255.0;
        return a <= 0 ? Colors.Transparent : Color.FromRgb((byte)Math.Round(pixels[2] / a), (byte)Math.Round(pixels[1] / a), (byte)Math.Round(pixels[0] / a));
    }

    private static RenderTargetBitmap RenderElement(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)element.ActualWidth), Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private static double MeanDeltaE(BitmapSource a, BitmapSource b)
    {
        double sum = 0;
        var n = 0;
        for (var y = 3; y < a.PixelHeight; y += 23)
        {
            for (var x = 5; x < a.PixelWidth; x += 29)
            {
                sum += ColorScience.DeltaE(Rgba(Pixel(a, x, y)), Rgba(Pixel(b, x, y)));
                n++;
            }
        }

        return sum / n;
    }

    private static string ViewName(LibraryViewMode view) => view switch
    {
        LibraryViewMode.List => "liste",
        LibraryViewMode.Grid => "raster",
        _ => "regal",
    };

    private static void Save(BitmapSource bitmap, string name)
    {
        var dir = Environment.GetEnvironmentVariable("HDRPILOT_LIBRARY_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, name));
        encoder.Save(stream);
    }

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }

    private static Rgba8 Rgba(Color c) => new(c.R, c.G, c.B);

    private static double Hue(Color c) => ColorScience.ToOklab(Rgba(c)).H;

    private sealed record RenderResult(MainWindow Window, RenderTargetBitmap Bitmap, GameColors Colors);

    /// <summary>
    /// Bilderdienst mit erzeugten Spielbildern: Titelbild, Logo und Cover als Bitmaps, Umgebungsfarben aus dem Titelbild,
    /// Spielfarben aus <see cref="GameColorAnalysis"/> über dieselben Bilder (wie im Betrieb verkleinert).
    /// </summary>
    private sealed class GeneratedArtwork : IArtworkService, IGameColorsSource
    {
        private readonly GameArtwork _artwork;

        public GeneratedArtwork(string key, ArtImage? logo, ArtImage? cover, ArtImage? hero)
        {
            Colors = GameColorAnalysis.Derive(
                logo is null ? null : GeneratedArt.AnalyzeLogo(logo),
                cover is null ? null : GeneratedArt.AnalyzeCover(cover),
                hero is null ? null : GeneratedArt.AnalyzeHero(hero));
            var heroImage = hero ?? GeneratedArt.Solid(960, 320, new Rgba8(0x24, 0x26, 0x30));
            var small = GeneratedArt.HeroForAnalysis(heroImage);
            var ambient = PixelAnalysis.Ambient(small.Pixels, small.Width, small.Height, isScene: true)!.Colors;
            var heroArt = new ArtworkImage(Bitmap(heroImage), ArtworkKind.Hero, hero is null ? ArtworkOrigin.Generated : ArtworkOrigin.Launcher, heroImage.Width, heroImage.Height, "Test");
            var logoArt = logo is null ? null : new ArtworkImage(Bitmap(logo), ArtworkKind.Logo, ArtworkOrigin.Launcher, logo.Width, logo.Height, "Test");
            var coverArt = cover is null ? null : new ArtworkImage(Bitmap(cover), ArtworkKind.Cover, ArtworkOrigin.Launcher, cover.Width, cover.Height, "Test");
            var summary = new ArtworkSummary(heroArt.Origin, logoArt?.Origin ?? ArtworkOrigin.None, coverArt?.Origin ?? ArtworkOrigin.None, ArtworkOrigin.None, logoArt is null, false, "Testbilder");
            _artwork = new GameArtwork(key, heroArt, null, null, logoArt, coverArt, null, ambient, summary);
        }

        public event EventHandler<ArtworkChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public GameColors Colors { get; }

        public bool TryPeek(GameConfig game, out GameColors colors)
        {
            colors = Colors;
            return true;
        }

        public Task<GameColors> GetGameColorsAsync(GameConfig game, CancellationToken cancellationToken = default) => Task.FromResult(Colors);

        public ImageSource? GetCachedImage(GameConfig game, ArtworkKind kind, int decodeWidth) => null;

        public Task<ImageSource?> GetImageAsync(GameConfig game, ArtworkKind kind, int decodeWidth, CancellationToken cancellationToken = default) =>
            Task.FromResult(kind switch
            {
                ArtworkKind.Cover => _artwork.Cover?.Image ?? _artwork.Hero?.Image,
                ArtworkKind.Logo => _artwork.Logo?.Image,
                _ => _artwork.Hero?.Image,
            });

        public GameArtwork? GetCachedArtwork(GameConfig game) => null;

        public Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default) => Task.FromResult(_artwork);

        public Task<IReadOnlyList<ArtworkCandidate>> GetCandidatesAsync(GameConfig game, ArtworkKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArtworkCandidate>>([]);

        public string SaveCustomImage(GameConfig game, ArtworkKind kind, string sourcePath) => throw new NotSupportedException();

        public void DeleteCustomImage(string? token)
        {
        }

        public Task<ArtworkCoverage> GetCoverageAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) => Task.FromResult(ArtworkCoverage.Empty);

        public Task<IReadOnlyList<FallbackExample>> GetFallbackExamplesAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FallbackExample>>([]);

        public IDisposable Acquire() => NoLease.Instance;

        public void ClearCache()
        {
        }

        public void Dispose()
        {
        }

        private static BitmapSource Bitmap(ArtImage image)
        {
            var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
