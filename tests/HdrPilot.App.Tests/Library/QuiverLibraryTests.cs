using System.Text.RegularExpressions;
using HdrPilot.App.Resources;
using HdrPilot.App.Tests.Artwork;
using HdrPilot.App.Tests.Theming;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Quiver Launcher in der Bibliothek (E91): eigene Quelle mit Namen, Farbe und Filter, Hinweise ohne Start-Befehl und bei
/// Installation per Windows Installer, Cover aus dem Zwischenspeicher von Quiver, Symbol aus der EXE.
/// </summary>
public sealed class QuiverLibraryTests
{
    private const string BlackDir = @"D:\Spielordner\QuiverLauncher-win-Portable\Apps\Black-BlackXBERecomp";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GameConfig Black(string? launch = BlackDir + @"\BLACK.exe", string? installKind = null)
    {
        var game = Game(GameSource.Quiver, "Black-BlackXBERecomp", "Black", isNew: false, installDir: BlackDir);
        game.LaunchCommand = launch;
        game.LaunchWorkingDirectory = launch is null ? null : BlackDir;
        game.KnownExecutables = [BlackDir + @"\BLACK.exe"];
        game.InstallKind = installKind;
        return game;
    }

    private static List<string> Visible(LibraryViewModel vm) => vm.GamesView.Cast<GameItemViewModel>().Select(g => g.Name).ToList();

    [Fact]
    public void Quiver_is_a_source_of_its_own_with_name_filter_and_search() => Sta.Run(() =>
    {
        var context = new TestLibraryContext(Black(), Game(GameSource.Steam, "570", "Dota 2"), Game(GameSource.Manual, "m1", "Eigenes Spiel"));
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        Assert.Equal("Quiver", LibraryText.SourceName(GameSource.Quiver));
        Assert.Equal("Quiver", LibraryStrings.Source_Quiver);
        Assert.Equal(["Alle", "Steam (1)", "Manuell (1)", "Quiver (1)"], vm.SourceOptions.Select(o => o.AutomationName));
        Assert.Equal("Quiver", vm.Games.Single(g => g.Source == GameSource.Quiver).SourceName);

        vm.SelectedSource = vm.SourceOptions.Single(o => o.Source == GameSource.Quiver);
        Assert.Equal(["Black"], Visible(vm));
        Assert.True(vm.IsFiltered);
        Assert.Contains("Quiver", vm.ToolbarSummary, StringComparison.Ordinal);

        vm.ClearFilters();
        vm.SearchText = "quiver";
        vm.ApplySearch();
        Assert.Equal(["Black"], Visible(vm));
    });

    [Fact]
    public void Quiver_game_without_launch_command_explains_the_way_through_quiver() => Sta.Run(() =>
    {
        var context = new TestLibraryContext(Black(launch: null));
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = vm.Games.Single();
        var details = vm.Details!;

        Assert.False(details.CanLaunchGame);
        Assert.False(details.LaunchCommand.CanExecute(null));
        Assert.Equal(LibraryStrings.Launch_NoCommandQuiver, details.NoLaunchText);
        Assert.Contains("einmal in Quiver", details.NoLaunchText, StringComparison.Ordinal);
        Assert.False(details.ShowWindowsInstallerHint);

        // Gegenprobe: ein eigenes Spiel ohne Start-Befehl behält den bisherigen Hinweis.
        var manual = Game(GameSource.Manual, "m1", "Eigenes Spiel");
        manual.LaunchCommand = null;
        var other = new TestLibraryContext(manual);
        using var vm2 = new LibraryViewModel(other, new FakeDialogs());
        vm2.SelectedGame = vm2.Games.Single();
        Assert.Equal(LibraryStrings.Launch_NoCommand, vm2.Details!.NoLaunchText);
    });

    [Fact]
    public void Quiver_launch_command_appears_after_a_rescan_without_reopening() => Sta.Run(() =>
    {
        // Erst ohne Wahl in Quiver, dann (nach „einmal in Quiver starten“) mit Start-EXE: Hinweis weg, Starten frei.
        var context = new TestLibraryContext(Black(launch: null));
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = vm.Games.Single();
        var details = vm.Details!;
        var raised = new List<string?>();
        details.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        context.ConfigService.UpdateGame("Quiver:Black-BlackXBERecomp", g =>
        {
            g.LaunchCommand = BlackDir + @"\BLACK.exe";
            g.LaunchWorkingDirectory = BlackDir;
        });

        Assert.Same(details, vm.Details);
        Assert.True(details.CanLaunchGame, "CanLaunchGame: " + details.LaunchCommandText + " / " + context.Config.Games.Single().LaunchCommand);
        Assert.True(details.LaunchCommand.CanExecute(null), "LaunchCommand");
        Assert.True(details.CreateDesktopShortcutCommand.CanExecute(null), "Verknüpfung");
        Assert.Contains(nameof(GameDetailsViewModel.CanLaunchGame), raised); // der Hinweis darunter verschwindet von selbst
    });

    [Fact]
    public void Quiver_windows_installer_game_says_which_folder_counts() => Sta.Run(() =>
    {
        var context = new TestLibraryContext(Black(installKind: InstallKinds.WindowsInstaller), Game(GameSource.Steam, "570", "Dota 2"));
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.SelectedGame = vm.Games.Single(g => g.Source == GameSource.Quiver);
        Assert.True(vm.Details!.ShowWindowsInstallerHint);
        Assert.Contains("Windows Installer", LibraryStrings.Details_QuiverWindowsInstaller, StringComparison.Ordinal);

        vm.SelectedGame = vm.Games.Single(g => g.Source == GameSource.Steam);
        Assert.False(vm.Details!.ShowWindowsInstallerHint);
    });

    [Fact]
    public void Quiver_launch_starts_the_game_key_like_any_other_source() => Sta.Run(async () =>
    {
        var context = new TestLibraryContext(Black());
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = vm.Games.Single();

        Assert.True(vm.Details!.LaunchCommand.CanExecute(null));
        vm.Details.LaunchCommand.Execute(null);
        await Sta.Finished(vm.Details.LaunchCommand);

        Assert.Equal(["Quiver:Black-BlackXBERecomp"], context.Launched);
        Assert.Equal(BlackDir + @"\BLACK.exe", vm.Details.LaunchCommandText);
    });

    [Fact]
    public void Quiver_source_colour_has_tokens_for_both_themes_and_differs_from_the_others()
    {
        foreach (var file in new[] { "Tokens.Dark.xaml", "Tokens.Light.xaml" })
        {
            var text = File.ReadAllText(Path.Combine(ThemeTokenFileTests.RepositoryRoot(), "src", "HdrPilot.App", "Themes", file));
            var colours = Regex.Matches(text, @"x:Key=""Hp\.Brush\.Source\.(\w+)"" Color=""(#\w+)""").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            Assert.True(colours.ContainsKey("Quiver"), file + ": Quiver fehlt");
            Assert.DoesNotContain(colours.Where(c => c.Key != "Quiver"), c => string.Equals(c.Value, colours["Quiver"], StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains(ThemeKeys.BrushSourceQuiver, ThemeKeys.All);
    }

    [Fact]
    public void Quiver_colour_triggers_exist_wherever_sources_are_coloured()
    {
        var library = File.ReadAllText(SourceFiles.InRepository("src", "HdrPilot.App", "Themes", "Library.xaml"));
        var main = File.ReadAllText(SourceFiles.MainWindowXaml);

        foreach (var property in new[] { "Foreground", "Fill", "Background" })
        {
            Assert.Matches(new Regex(@"Value=""Quiver"">\s*<Setter Property=""" + property + @""" Value=""\{DynamicResource Hp\.Brush\.Source\.Quiver\}"""), library);
        }

        Assert.Matches(new Regex(@"Value=""Quiver"">\s*<Setter TargetName=""Dot"" Property=""Fill"" Value=""\{DynamicResource Hp\.Brush\.Source\.Quiver\}"""), main);
        Assert.Contains("Text=\"{Binding NoLaunchText}\"", main, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"QuiverWindowsInstallerHint\"", main, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quiver_icon_comes_from_the_exe_even_with_a_title_image()
    {
        // Mit Titelbild (hier ein eigenes) fragt HDRpilot die EXE nur bei Quellen ohne eigene Symbole (Epic, EA, Battle.net,
        // Quiver, eigene Spiele); Gegenprobe: GOG nimmt dann das Monogramm.
        using var art = new ArtworkFixture();
        var gameDir = Path.Combine(art.Root, "Quiver", "Apps", "Black-BlackXBERecomp");
        Directory.CreateDirectory(gameDir);
        var exe = Path.Combine(gameDir, "BLACK.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), exe);
        var service = art.Service();

        async Task<ArtworkOrigin> IconOrigin(GameSource source)
        {
            var game = new GameConfig { Source = source, SourceId = "Black-BlackXBERecomp", Name = "Black", InstallDir = gameDir, KnownExecutables = [exe] };
            var hero = ArtworkFiles.CustomFileName(game.Key, ArtworkKind.Hero);
            art.Save(Path.Combine(art.ArtDirectory, ArtworkFiles.CustomFolder, hero), ArtworkFixture.Hero());
            game.Artwork = new GameArtworkChoice { Hero = ArtworkOverride.CustomPrefix + hero };
            var artwork = await service.GetArtworkAsync(game, 640, Ct);
            Assert.Equal(ArtworkOrigin.Custom, artwork.Summary.Hero);
            return artwork.Summary.Icon;
        }

        Assert.Equal(ArtworkOrigin.Launcher, await IconOrigin(GameSource.Quiver));
        Assert.Equal(ArtworkOrigin.Generated, await IconOrigin(GameSource.Gog));
    }

    [Fact]
    public async Task Quiver_cover_comes_from_the_quiver_cache()
    {
        using var art = new ArtworkFixture();
        var cache = Path.Combine(art.Root, "Quiver", "Cache", "Images");
        var cover = art.Save(Path.Combine(cache, "8C8FC6677FB84678288E4615E4E470A5.png"), ArtworkFixture.Cover(300, 450));
        var withoutExtension = Path.ChangeExtension(cover, null);
        File.Move(cover, withoutExtension); // Quiver legt seine Bilder ohne Endung ab
        var game = new GameConfig { Source = GameSource.Quiver, SourceId = "Black-BlackXBERecomp", Name = "Black", CoverCandidates = [withoutExtension] };
        var service = art.Service();

        var artwork = await service.GetArtworkAsync(game, 640, Ct);

        Assert.Equal(ArtworkOrigin.Launcher, artwork.Summary.Cover);
        Assert.Equal("Quiver-Cache", artwork.Cover!.SourceLabel);
        Assert.Equal(ArtworkOrigin.Generated, artwork.Summary.Hero);
        Assert.Equal((300, 450), (artwork.Cover.PixelWidth, artwork.Cover.PixelHeight));
        Assert.Contains("Quiver", artwork.Summary.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quiver_artwork_summary_names_the_cover_from_the_cache_and_not_a_missing_cache()
    {
        // Cover aus dem Cache von Quiver und eine EXE mit Symbol (der Normalfall): früher „Quiver: kein Bildcache · Symbol aus
        // …“, obwohl das Cover daneben aus dem Quiver-Cache kommt.
        using var art = new ArtworkFixture();
        var gameDir = Path.Combine(art.Root, "Quiver", "Apps", "Langes-Spiel");
        Directory.CreateDirectory(gameDir);
        var exe = Path.Combine(gameDir, "LangesSpiel.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), exe);
        var cover = Path.ChangeExtension(art.Save(Path.Combine(art.Root, "Quiver", "Cache", "Images", "AAAA.png"), ArtworkFixture.Cover(300, 450)), null);
        File.Move(cover + ".png", cover);
        var service = art.Service();
        GameConfig Game(GameSource source, string? coverFile) => new()
        {
            Source = source, SourceId = "Langes-Spiel", Name = "Langes Spiel", InstallDir = gameDir, KnownExecutables = [exe],
            CoverCandidates = coverFile is null ? null : [coverFile],
        };

        var withCover = await service.GetArtworkAsync(Game(GameSource.Quiver, cover), 640, Ct);
        Assert.Equal(ArtworkOrigin.Launcher, withCover.Summary.Icon); // Symbol aus der EXE
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, ArtworkStrings.Summary_CoverFromCache, "Quiver-Cache"), withCover.Summary.Description);
        Assert.DoesNotContain("kein Bildcache", withCover.Summary.Description, StringComparison.Ordinal);

        // Ohne Cover: kein lokales Titelbild, erzeugt aus dem Symbol (Quiver hat einen Bildcache, nur nichts darin).
        var without = await art.Service().GetArtworkAsync(Game(GameSource.Quiver, null), 640, Ct);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, ArtworkStrings.Summary_NoHeroFromIcon, "Quiver"), without.Summary.Description);

        // Gegenprobe: Epic hat keinen Bildcache, dort bleibt „kein Bildcache · Symbol aus …“.
        var epic = await art.Service().GetArtworkAsync(Game(GameSource.Epic, null), 640, Ct);
        Assert.StartsWith("Epic Games: kein Bildcache · Symbol aus LangesSpiel.exe", epic.Summary.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quiver_square_cover_is_shown_whole_in_a_portrait_cover_and_in_the_title_image()
    {
        // Quiver legt die Kartenbilder meist quadratisch ab (bei „Black“ 480 × 480). Als Cover im Hochformat zugeschnitten fehlte
        // links und rechts ein Drittel; jetzt steht das ganze Bild oben in einem erzeugten Cover und rechts im Titelbild.
        using var art = new ArtworkFixture();
        var green = ArtworkFixture.Image(480, 480, (x, _) => ((byte)0, (byte)(x / Stripe % 2 == 0 ? 255 : 110), (byte)0, (byte)255));
        var cover = Path.ChangeExtension(art.Save(Path.Combine(art.Root, "Quiver", "Cache", "Images", "8C8FC6677FB84678288E4615E4E470A5.png"), green), null);
        File.Move(cover + ".png", cover);
        var game = new GameConfig { Source = GameSource.Quiver, SourceId = "Black-BlackXBERecomp", Name = "Black", CoverCandidates = [cover] };
        var service = art.Service();

        var artwork = await service.GetArtworkAsync(game, 1920, Ct);
        service.FlushIndex();

        Assert.Equal(ArtworkOrigin.Launcher, artwork.Summary.Cover);
        Assert.Equal("Quiver-Cache", artwork.Cover!.SourceLabel);
        Assert.Equal((400, 600), (artwork.Cover.PixelWidth, artwork.Cover.PixelHeight)); // Hochformat, nicht 400 × 400
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, ArtworkStrings.Summary_SquareArt, "Quiver-Cache"), artwork.Summary.Description);
        var stem = ArtworkFiles.Stem(game.Key);
        Assert.Contains("\"cover-square\"", File.ReadAllText(Path.Combine(art.ArtDirectory, "index.json")), StringComparison.Ordinal);

        // Das Bild steht ganz oben im Cover und rechts der Mitte im Titelbild: dort wechseln helle und dunkle Streifen (das
        // Farbfeld allein wäre glatt); die Farben (Farbfeld) kommen aus ihm.
        var coverScale = 600.0 / 480 * (400.0 / 600); // Vorlage → Cover 600 × 900 → geladen 400 × 600
        Assert.True(StripesAt(artwork.Cover.Image, x0: 0, scale: coverScale, y: 100), "Cover oben mit den Streifen der Vorlage");
        var hero = artwork.Hero!;
        Assert.True(StripesAt(hero.Image, x0: 0.37 * hero.PixelWidth, scale: hero.PixelHeight / 480.0, y: hero.PixelHeight / 2), "Titelbild rechts der Mitte mit den Streifen");
        Assert.True(IsGreen(artwork.Field!.Image, artwork.Field.PixelWidth / 2, artwork.Field.PixelHeight / 2), "Farbfeld aus dem Bild");
        Assert.True(File.Exists(Path.Combine(art.ArtDirectory, "gen", stem + "-cover.jpg")));

        // „Bild wählen…“ nennt beim automatischen Cover die Größe des Hochformats, nicht die der Vorlage.
        var automatic = (await service.GetCandidatesAsync(game, ArtworkKind.Cover, Ct)).First();
        Assert.Equal((ArtworkOrigin.Launcher, 600, 900), (automatic.Origin, automatic.PixelWidth, automatic.PixelHeight));

        // Gegenprobe Steam: Ein quadratisches Cover aus einem anderen Cache bleibt, wie es ist (nur Quiver legt Karten so ab).
        art.SteamFile("4711", "library_600x900.jpg", ArtworkFixture.Cover(480, 480));
        var steam = await art.Service().GetArtworkAsync(ArtworkFixture.Steam("4711"), 1920, Ct);
        Assert.Equal((ArtworkOrigin.Launcher, 400, 400), (steam.Summary.Cover, steam.Cover!.PixelWidth, steam.Cover.PixelHeight));

        // Gegenprobe: ein Cover im Hochformat bleibt, wie es ist (siehe Quiver_cover_comes_from_the_quiver_cache), und das
        // Titelbild zeigt kein Bild rechts der Mitte.
        var portrait = Path.ChangeExtension(art.Save(Path.Combine(art.Root, "Quiver", "Cache", "Images", "BBBB.png"), ArtworkFixture.Image(300, 450, (_, _) => ((byte)0, (byte)255, (byte)0, (byte)255))), null);
        File.Move(portrait + ".png", portrait);
        var other = await art.Service().GetArtworkAsync(new GameConfig { Source = GameSource.Quiver, SourceId = "Hoch", Name = "Hoch", CoverCandidates = [portrait] }, 1920, Ct);
        Assert.Equal((300, 450), (other.Cover!.PixelWidth, other.Cover.PixelHeight));
        Assert.False(StripesAt(other.Hero!.Image, x0: 0.37 * other.Hero.PixelWidth, scale: other.Hero.PixelHeight / 480.0, y: other.Hero.PixelHeight / 2), "ohne quadratisches Bild keine Streifen im Titelbild");
    }

    [Fact]
    public async Task Quiver_square_cover_is_also_the_picture_in_the_list()
    {
        // Liste mit Bildern (Querformat-Vorschau, Kopfbild 460 × 215): Ohne Kopf- und Titelbild zeigte sie bei „Black“ nur Farbfeld
        // und Monogramm („BL“), obwohl Titelbild und Cover das quadratische Bild zeigen. Jetzt füllt es die Vorschau wie ein
        // Kachelbild (Xbox): die Mitte über die ganze Breite.
        using var art = new ArtworkFixture();
        var green = ArtworkFixture.Image(480, 480, (x, _) => ((byte)0, (byte)(x / Stripe % 2 == 0 ? 255 : 110), (byte)0, (byte)255));
        var cover = Path.ChangeExtension(art.Save(Path.Combine(art.Root, "Quiver", "Cache", "Images", "8C8FC6677FB84678288E4615E4E470A5.png"), green), null);
        File.Move(cover + ".png", cover);
        var game = new GameConfig { Source = GameSource.Quiver, SourceId = "Black-BlackXBERecomp", Name = "Black", CoverCandidates = [cover] };
        var service = art.Service();
        var index = Path.Combine(art.ArtDirectory, "index.json");
        var file = Path.Combine(art.ArtDirectory, ArtworkFiles.GeneratedFolder, ArtworkFiles.GeneratedFileName(game.Key, ArtworkKind.Header));
        const int Width = HdrPilot.App.Services.Artwork.ArtworkRenderer.HeaderWidth;

        var header = await service.GetImageAsync(game, ArtworkKind.Header, Width, Ct);
        service.FlushIndex();

        var picture = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(header);
        Assert.Equal((Width, HdrPilot.App.Services.Artwork.ArtworkRenderer.HeaderHeight), (picture.PixelWidth, picture.PixelHeight));
        Assert.True(StripesAt(picture, x0: 0, scale: Width / 480.0, y: 10), "Vorschau oben mit den Streifen der Vorlage");
        Assert.True(StripesAt(picture, x0: 0, scale: Width / 480.0, y: picture.PixelHeight / 2), "Vorschau in der Mitte mit den Streifen der Vorlage (statt Monogramm)");
        Assert.True(File.Exists(file));
        Assert.Contains("\"header-square\"", File.ReadAllText(index), StringComparison.Ordinal);

        // Bestand: Eine früher nur mit Monogramm gezeichnete Vorschau (unter „header“ vermerkt) gilt nicht mehr, sie wird neu
        // gezeichnet.
        File.WriteAllText(index, File.ReadAllText(index).Replace("\"header-square\"", "\"header\"", StringComparison.Ordinal));
        art.Save(file, ArtworkFixture.Image(Width, 215, (_, _) => ((byte)0, (byte)0, (byte)0, (byte)255)));
        var redrawn = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(await art.Service().GetImageAsync(game, ArtworkKind.Header, Width, Ct));
        Assert.True(StripesAt(redrawn, x0: 0, scale: Width / 480.0, y: 10), "alte Vorschau ersetzt");

        // Gegenprobe: Mit einem Cover im Hochformat bleibt es bei Farbfeld und Monogramm (keine Streifen).
        var portrait = Path.ChangeExtension(art.Save(Path.Combine(art.Root, "Quiver", "Cache", "Images", "BBBB.png"), ArtworkFixture.Image(300, 450, (x, _) => ((byte)0, (byte)(x / Stripe % 2 == 0 ? 255 : 110), (byte)0, (byte)255))), null);
        File.Move(portrait + ".png", portrait);
        var high = new GameConfig { Source = GameSource.Quiver, SourceId = "Hoch", Name = "Hoch", CoverCandidates = [portrait] };
        var plain = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(await art.Service().GetImageAsync(high, ArtworkKind.Header, Width, Ct));
        Assert.False(StripesAt(plain, x0: 0, scale: Width / 480.0, y: 10), "ohne quadratisches Bild keine Streifen in der Vorschau");
    }

    [Fact]
    public async Task Quiver_square_cover_counts_as_generated_header_and_cover_in_the_report()
    {
        // Gegenprüfung 2026-10-20: Der Bericht (--artwork-report) zählte das aus dem quadratischen Cover erzeugte Kopfbild und
        // Cover nicht mit, obwohl HDRpilot beide zeichnet. Gegenprobe: Mit einem Cover im Hochformat ist nur das Kopfbild erzeugt.
        using var art = new ArtworkFixture();
        string QuiverCover(string name, int width, int height)
        {
            var image = ArtworkFixture.Image(width, height, (x, _) => ((byte)0, (byte)(x / Stripe % 2 == 0 ? 255 : 110), (byte)0, (byte)255));
            var path = Path.ChangeExtension(art.Save(Path.Combine(art.Root, "Quiver", "Cache", "Images", name + ".png"), image), null);
            File.Move(path + ".png", path);
            return path;
        }

        var black = new GameConfig { Source = GameSource.Quiver, SourceId = "Black-BlackXBERecomp", Name = "Black", CoverCandidates = [QuiverCover("8C8FC6677FB84678288E4615E4E470A5", 480, 480)] };
        var high = new GameConfig { Source = GameSource.Quiver, SourceId = "Hoch", Name = "Hoch", CoverCandidates = [QuiverCover("BBBB", 300, 450)] };

        var square = await art.Service().BuildReportAsync([black], Ct);
        Assert.Equal((1, 1, 1), (square.GeneratedHeroes, square.GeneratedHeaders, square.GeneratedCovers));

        var portrait = await art.Service().BuildReportAsync([high], Ct);
        Assert.Equal((1, 1, 0), (portrait.GeneratedHeroes, portrait.GeneratedHeaders, portrait.GeneratedCovers));
    }

    /// <summary>Breite der Streifen der quadratischen Vorlage (480 px): gerade Streifen hellgrün, ungerade dunkelgrün.</summary>
    private const int Stripe = 40;

    /// <summary>
    /// True, wenn in der Zeile <paramref name="y"/> die Mitte des 7. Streifens der Vorlage (ab <paramref name="x0"/>, in
    /// <paramref name="scale"/> vergrößert) hell und die des 8. dunkel ist: das Bild steht dort ganz (nicht beschnitten).
    /// </summary>
    private static bool StripesAt(System.Windows.Media.ImageSource image, double x0, double scale, int y)
    {
        var bgra = HdrPilot.App.Services.Artwork.ArtworkImaging.ToBgra((System.Windows.Media.Imaging.BitmapSource)image);
        int Green(double x) => bgra.Pixels[((y * bgra.Width) + (int)Math.Round(x)) * 4 + 1];
        var bright = Green(x0 + (6.5 * Stripe * scale));
        var dark = Green(x0 + (7.5 * Stripe * scale));
        return bright > 220 && dark < 160;
    }

    private static bool IsGreen(System.Windows.Media.ImageSource image, int x, int y)
    {
        var bgra = HdrPilot.App.Services.Artwork.ArtworkImaging.ToBgra((System.Windows.Media.Imaging.BitmapSource)image);
        var o = ((y * bgra.Width) + x) * 4;
        int b = bgra.Pixels[o], g = bgra.Pixels[o + 1], r = bgra.Pixels[o + 2];
        return g > 120 && g > (2 * r) + 20 && g > (2 * b) + 20;
    }

    [Fact]
    public void Quiver_profile_line_names_no_exe_when_there_is_no_unique_one() => Sta.Run(() =>
    {
        // Mehrere EXE und kein Start-Befehl: früher „Quiver · Editor.exe“ (nur alphabetisch vorne) über dem Hinweis „keine
        // eindeutige Start‑EXE“.
        var several = Black(launch: null);
        several.KnownExecutables = [BlackDir + @"\Editor.exe", BlackDir + @"\Spiel.exe"];
        var context = new TestLibraryContext(several);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = vm.Games.Single();

        Assert.Equal("Quiver · mehrere EXE", vm.Details!.ProfileSubtitle);

        // Beim Spielen gemerkt: Diese EXE nennt die Zeile.
        context.ConfigService.UpdateGame("Quiver:Black-BlackXBERecomp", g => g.DetectedExecutables = [BlackDir + @"\Spiel.exe"]);
        Assert.Equal("Quiver · Spiel.exe", vm.Details!.ProfileSubtitle);

        // Mit Start-Befehl gilt wie bisher die Haupt-EXE.
        var launchable = Black();
        launchable.KnownExecutables = [BlackDir + @"\Editor.exe", BlackDir + @"\BLACK.exe"];
        using var vm2 = new LibraryViewModel(new TestLibraryContext(launchable), new FakeDialogs());
        vm2.SelectedGame = vm2.Games.Single();
        Assert.StartsWith("Quiver · ", vm2.Details!.ProfileSubtitle, StringComparison.Ordinal);
        Assert.NotEqual("Quiver · mehrere EXE", vm2.Details.ProfileSubtitle);
    });

    [Fact]
    public void Quiver_hint_without_launch_command_covers_scripts_and_names_the_rescan_button()
    {
        Assert.Contains("BAT oder CMD", LibraryStrings.Launch_NoCommandQuiver, StringComparison.Ordinal);
        Assert.Contains("„" + LibraryStrings.Rescan_Button + "“", LibraryStrings.Launch_NoCommandQuiver, StringComparison.Ordinal);
        Assert.Contains("Quiver", LibraryStrings.Picker_SourceNote, StringComparison.Ordinal);
        Assert.Contains("bei Quiver nach dem nächsten Einlesen", ShellStrings.ArtworkLocalHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Quiver_game_also_entered_by_hand_shows_a_hint_on_both_entries() => Sta.Run(() =>
    {
        // Früher von Hand eingetragen (über die EXE in einem Unterordner), jetzt auch aus Quiver: beide Einträge nennen den
        // anderen, nichts wird entfernt.
        var manual = Game(GameSource.Manual, "m1", "Black (eigener Eintrag)", isNew: false, installDir: BlackDir + @"\bin");
        var context = new TestLibraryContext(Black(), manual, Game(GameSource.Steam, "570", "Dota 2"));
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.SelectedGame = vm.Games.Single(g => g.Source == GameSource.Quiver);
        Assert.True(vm.Details!.HasDuplicateHint);
        Assert.Contains("„Black (eigener Eintrag)“", vm.Details.DuplicateHint, StringComparison.Ordinal);

        vm.SelectedGame = vm.Games.Single(g => g.Source == GameSource.Manual);
        Assert.True(vm.Details!.HasDuplicateHint);
        Assert.Contains("aus Quiver („Black“)", vm.Details.DuplicateHint, StringComparison.Ordinal);

        vm.SelectedGame = vm.Games.Single(g => g.Source == GameSource.Steam);
        Assert.False(vm.Details!.HasDuplicateHint);

        // Ist der eigene Eintrag entfernt, verschwindet der Hinweis beim Quiver-Spiel (die Oberfläche erfährt es).
        vm.SelectedGame = vm.Games.Single(g => g.Source == GameSource.Quiver);
        var details = vm.Details!;
        var raised = new List<string?>();
        details.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        context.ConfigService.Update(c => c.Games.RemoveAll(g => g.Source == GameSource.Manual));
        vm.SyncFromConfig();
        Assert.Same(details, vm.Details);
        Assert.False(details.HasDuplicateHint);
        Assert.Contains(nameof(GameDetailsViewModel.HasDuplicateHint), raised);
        Assert.Contains(nameof(GameDetailsViewModel.DuplicateHint), raised);
        Assert.Equal(2, context.Config.Games.Count);

        // Gegenprobe: ein eigener Eintrag in einem anderen Ordner ist kein Doppel.
        var elsewhere = Game(GameSource.Manual, "m2", "Anderes", isNew: false, installDir: @"D:\Spielordner\Anderes");
        using var vm2 = new LibraryViewModel(new TestLibraryContext(Black(), elsewhere), new FakeDialogs());
        vm2.SelectedGame = vm2.Games.Single(g => g.Source == GameSource.Quiver);
        Assert.False(vm2.Details!.HasDuplicateHint);

        // Gegenprobe: zwei eigene Einträge ineinander sind kein Doppel mit Quiver.
        var outer = Game(GameSource.Manual, "m3", "Außen", isNew: false, installDir: @"D:\Spielordner\Außen");
        var inner = Game(GameSource.Manual, "m4", "Innen", isNew: false, installDir: @"D:\Spielordner\Außen\bin");
        using var vm3 = new LibraryViewModel(new TestLibraryContext(outer, inner), new FakeDialogs());
        vm3.SelectedGame = vm3.Games.Single(g => g.Name == "Innen");
        Assert.False(vm3.Details!.HasDuplicateHint);
    });

    [Fact]
    public void Quiver_duplicate_hint_is_bound_in_the_details()
    {
        var main = File.ReadAllText(SourceFiles.MainWindowXaml);
        Assert.Matches(new Regex(@"Text=""\{Binding DuplicateHint\}""[^>]*AutomationProperties\.AutomationId=""DuplicateEntryHint""[^>]*Visibility=""\{Binding HasDuplicateHint"), main);
    }

    [Fact]
    public void Quiver_cover_file_is_not_held_open_while_decoding()
    {
        // Quiver ersetzt Bilder (und windows-installer.json) durch Verschieben über die alte Datei. Hielte HDRpilot sie während
        // des Dekodierens offen (auch mit Freigabe zum Löschen), scheiterte das mit „Zugriff verweigert“.
        var dir = Path.Combine(Path.GetTempPath(), "hdrpilot-quiver-offen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "8C8FC6677FB84678288E4615E4E470A5");
        try
        {
            File.WriteAllBytes(path, Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="));
            var replacement = Path.Combine(dir, "neu.tmp");
            File.WriteAllBytes(replacement, File.ReadAllBytes(path));

            using (var stream = HdrPilot.App.Services.Artwork.ArtworkImaging.OpenRead(path))
            {
                File.Move(replacement, path, overwrite: true); // wie Quiver, während HDRpilot das Bild noch in der Hand hat
                Assert.Equal(34, stream.Length);
            }

            Assert.False(File.Exists(replacement));
            Assert.Equal(1, HdrPilot.App.Services.Artwork.ArtworkImaging.Decode(path, 64).PixelWidth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UnknownSource_images_survive_the_cleanup_after_closing()
    {
        // Eine Datei einer neueren Version mit einer Quelle, die diese Version nicht kennt: Beim Aufräumen nach dem Schließen
        // bleiben die eigenen Bilder dieses Spiels (die App reicht AllGameKeys an den Bilderdienst), die eines entfernten gehen.
        using var art = new ArtworkFixture();
        var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(
            """{ "games": [ { "source": "Steam", "sourceId": "1", "name": "Eins" }, { "source": "Itch", "sourceId": "celeste", "name": "Celeste" } ] }""",
            ConfigJson.Options)!;
        using var service = new HdrPilot.App.Services.Artwork.ArtworkService(new HdrPilot.App.Services.Artwork.ArtworkServiceOptions
        {
            ArtDirectory = art.ArtDirectory,
            Roots = () => art.Roots,
            WatchCaches = false,
            LibraryKeys = () => config.AllGameKeys(),
        });
        string Custom(string key)
        {
            var path = Path.Combine(art.ArtDirectory, ArtworkFiles.CustomFolder, ArtworkFiles.CustomFileName(key, ArtworkKind.Cover));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
            return path;
        }

        var unknown = Custom("Itch:celeste");
        var removed = Custom("Steam:2");

        Assert.Equal(1, service.PruneRemovedGames());
        Assert.True(File.Exists(unknown), "Bild des Spiels mit unbekannter Quelle");
        Assert.False(File.Exists(removed));

        var wiring = File.ReadAllText(SourceFiles.InRepository("src", "HdrPilot.App", "AppServices.Artwork.cs"));
        Assert.Contains("LibraryKeys = () => Config.Current.AllGameKeys(),", wiring, StringComparison.Ordinal);
    }

    [Fact]
    public void Quiver_webp_cover_without_extension_is_decoded_by_windows()
    {
        // 1 × 1 Pixel, verlustfreies WebP: wie das echte Cover von „Black“ (480 × 480, WebP, ohne Endung im Zwischenspeicher).
        var dir = Path.Combine(Path.GetTempPath(), "hdrpilot-quiver-webp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "8C8FC6677FB84678288E4615E4E470A5");
        try
        {
            File.WriteAllBytes(path, Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="));

            Assert.Equal((1, 1), HdrPilot.App.Services.Artwork.ArtworkImaging.Measure(path));
            var image = HdrPilot.App.Services.Artwork.ArtworkImaging.Decode(path, 64);
            Assert.Equal(1, image.PixelWidth);
            Assert.True(image.IsFrozen);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
