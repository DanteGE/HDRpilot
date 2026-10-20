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
