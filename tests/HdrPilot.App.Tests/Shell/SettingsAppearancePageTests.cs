using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Seite „Darstellung“ (E89, G10.8): jede Wahl wirkt und speichert sofort (unabhängig von „Speichern“/„Abbrechen“),
/// Kontrast-Kennzeichen, echte Nit-Werte statt eines festen Maximums, SteamGridDB nur auf ausdrücklichen Wunsch.
/// </summary>
public sealed class SettingsAppearancePageTests
{
    [Fact]
    public void Appearance_page_writes_every_choice_immediately()
    {
        var (page, config, _) = Create();

        page.Presets.Single(p => p.Id == "cyber").IsSelected = true;
        Assert.Equal("cyber", config.Current.Settings.Appearance.Preset);
        Assert.True(page.IsTritone); // Cyber ist eine Tritonus-Vorlage

        page.IsDuotone = true;
        Assert.Equal("Duotone", config.Current.Settings.Appearance.PaletteMode);
        Assert.Equal("cyber", config.Current.Settings.Appearance.Preset); // Vorlage bleibt, nur die Anzahl ändert sich

        page.IsThemeLight = true;
        Assert.Equal("Light", config.Current.Settings.Appearance.Theme);
        page.IsEffectReduced = true;
        Assert.Equal("Reduced", config.Current.Settings.Appearance.EffectLevel);
        page.PauseInBackground = false;
        page.MinimizeToTrayAfterLaunch = true;
        page.BackgroundFollowsGame = false;
        Assert.False(config.Current.Settings.Appearance.PauseInBackground);
        Assert.True(config.Current.Settings.Appearance.MinimizeToTrayAfterLaunch);
        Assert.False(config.Current.Settings.Appearance.BackgroundFollowsGame);

        page.ResetCommand.Execute(null);
        var reset = config.Current.Settings.Appearance;
        Assert.Null(reset.Preset);
        Assert.Null(reset.Theme);
        Assert.True(reset.PauseInBackground);
        Assert.Equal(PalettePresets.DefaultId, page.State.PresetId);
        page.Dispose();
    }

    [Fact]
    public void Appearance_page_custom_colors_and_swap_turn_the_preset_into_custom()
    {
        var (page, config, _) = Create();

        page.ApplyColor(0, ColorMath.FromHex("#12AB34"));
        var s = config.Current.Settings.Appearance;
        Assert.Equal(PalettePresets.CustomId, s.Preset);
        Assert.Equal("#12AB34", s.Accent);
        Assert.Equal("#FF3D7F", s.Atmosphere); // die andere Farbe bleibt
        Assert.Equal(ShellStrings.PresetCustom, page.PresetCaption.Split(" · ")[0]);
        Assert.DoesNotContain(page.Presets, p => p.IsSelected);

        page.SwapCommand.Execute(null);
        Assert.Equal("#FF3D7F", config.Current.Settings.Appearance.Accent);
        Assert.Equal("#12AB34", config.Current.Settings.Appearance.Atmosphere);

        // Glanz ist im Duoton automatisch: nicht wählbar, der Farbwähler öffnet nicht.
        Assert.True(page.Wells[2].IsAutomatic);
        Assert.False(page.EditColorCommand.CanExecute(page.Wells[2]));
        page.EditColorCommand.Execute(page.Wells[2]);
        Assert.False(page.IsPickerOpen);

        // Farbwähler: öffnen, Farbe setzen, übernehmen.
        page.EditColorCommand.Execute(page.Wells[1]);
        Assert.True(page.IsPickerOpen);
        page.PickerColor = ColorMath.FromHex("#3366FF");
        page.ApplyPickerCommand.Execute(null);
        Assert.False(page.IsPickerOpen);
        Assert.Equal("#3366FF", config.Current.Settings.Appearance.Atmosphere);
        page.Dispose();
    }

    [Fact]
    public void Appearance_page_shows_contrast_with_german_decimal_and_grade()
    {
        var (page, _, _) = Create();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            // Abendrot: dunkle Schrift auf Orange/Pink etwa 5,7:1 (Prototyp „5,8:1 · AA“, hier abgerundet).
            Assert.Matches(@"^\d,\d:1 · AA$", page.OnAccentContrast);
            Assert.True(page.ThemeColors.OnAccentContrast >= 4.5);
            Assert.Equal("Hp.Brush.Success", page.OnAccentGradeBrush);
            Assert.EndsWith("Soft", page.OnAccentGradeSoftBrush, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        page.Dispose();
    }

    [Fact]
    public void Appearance_page_shows_the_real_peak_and_never_a_fixed_maximum()
    {
        var (page, _, context) = Create();
        Assert.Equal(ShellStrings.PreviewNitsUnknown, page.PreviewNitsText); // unbekannt: kein Wert
        page.Dispose();

        context.Luminance = new FixedLuminance(new DisplayNits(@"\\?\DISPLAY#MSI", "MAG 341C OLED", 456, 254, 0, 10, HdrActive: false, PeakMeasuredWithHdr: true));
        var withNits = new AppearancePageViewModel(context);
        Assert.Contains("456", withNits.PreviewNitsText, StringComparison.Ordinal);
        Assert.DoesNotContain("1000", withNits.PreviewNitsText, StringComparison.Ordinal);
        withNits.Dispose();
    }

    [Fact]
    public void Tray_menu_preview_shows_what_the_real_menu_shows_in_the_same_order()
    {
        // Kopf wie im echten Menü: HDR-Zustand und Automatik, darunter die Version (keine Nit-Zeile, kein Spielname).
        var (page, _, context) = Create();
        Assert.Equal(ShellStrings.TrayStatusHdrOff + " · " + ShellStrings.TrayStatusActive, page.TrayMenuStatus);
        Assert.Equal(HdrPilot.App.Infrastructure.AppVersion.LabelText, page.TrayMenuVersion);
        page.Dispose();
        context.TrayState = new TrayState(HdrOn: true, HdrSupported: true, Paused: false, ActiveGames: ["Cyberpunk 2077"]);
        var running = new AppearancePageViewModel(context);
        Assert.Equal(ShellStrings.TrayStatusHdrOn + " · " + string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayStatusGame, "Cyberpunk 2077"), running.TrayMenuStatus);
        running.Dispose();

        // Reihenfolge der Einträge: Vorschau in SettingsWindow.xaml wie TrayMenu.xaml (Version, HDR, Auto HDR, Pause, …).
        var menu = File.ReadAllText(HdrPilot.App.Tests.Library.SourceFiles.InRepository("src", "HdrPilot.App", "Views", "Shell", "TrayMenu.xaml"));
        var settings = File.ReadAllText(HdrPilot.App.Tests.Library.SourceFiles.InRepository("src", "HdrPilot.App", "Views", "Shell", "SettingsWindow.xaml"));
        var preview = settings[settings.IndexOf("x:Name=\"TrayMenuPreview\"", StringComparison.Ordinal)..];
        preview = preview[..preview.IndexOf("</views:GroupBorder>", StringComparison.Ordinal)];
        string[] entries = ["TrayMenuAutoHdr", "TrayMenuPause", "TrayMenuOpen", "TrayMenuSettings", "TrayMenuLogs", "TrayMenuExit"];
        AssertInOrder(menu, ["VersionText", "ToggleHdrText", .. entries]);
        AssertInOrder(preview, ["TrayMenuStatus", "TrayMenuVersion", "TrayMenuToggleText", .. entries]);
        Assert.DoesNotContain("Nits", preview, StringComparison.Ordinal);
    }

    /// <summary>Die Texte kommen in dieser Reihenfolge vor (jeweils die erste Fundstelle).</summary>
    private static void AssertInOrder(string text, string[] needles)
    {
        var positions = needles.Select(n => text.IndexOf(n, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public async Task Preview_loads_the_bright_variant_only_when_its_hdr_switch_is_on()
    {
        var (page, config, context) = Create();
        page.Dispose();
        var game = new GameConfig { Source = GameSource.Steam, SourceId = "5", Name = "Mit Titelbild", LastPlayed = DateTimeOffset.Now.AddDays(-1) };
        config.Update(c => c.Games.Add(game));
        var art = new HdrPilot.App.Tests.Library.FakeArtworkService();
        art.WithHero.Add(game.Key);
        context.Artwork = art;

        var vm = new AppearancePageViewModel(context, windowsAnimations: () => true);
        await vm.Loaded;
        Assert.NotNull(vm.PreviewHero);
        Assert.Null(vm.PreviewBoost);
        Assert.Empty(art.BoostRequests); // HDR der Vorschau aus: nicht berechnet

        vm.PreviewHdr = true;

        Assert.NotNull(vm.PreviewBoost);
        Assert.Equal([game.Key], art.BoostRequests);
        vm.Dispose();
    }

    [Fact]
    public void Appearance_page_offers_run_now_and_remove_key_only_when_they_make_sense()
    {
        var (page, _, context) = Create();
        var grid = new RecordingSteamGridDb();
        context.SteamGridDb = grid;
        page.Dispose();
        var vm = new AppearancePageViewModel(context);

        Assert.False(vm.RunSteamGridDbNowCommand.CanExecute(null)); // aus, ohne Schlüssel
        Assert.False(vm.RemoveSteamGridDbKeyCommand.CanExecute(null));

        grid.Key = SteamGridDbKeyState.Stored;
        vm.SteamGridDbEnabled = true;
        Assert.True(vm.RunSteamGridDbNowCommand.CanExecute(null));
        Assert.True(vm.RemoveSteamGridDbKeyCommand.CanExecute(null));

        vm.RunSteamGridDbNowCommand.Execute(null);
        vm.RemoveSteamGridDbKeyCommand.Execute(null);

        Assert.Equal(["enabled:True", "run", "remove"], grid.Calls);
        Assert.False(vm.RemoveSteamGridDbKeyCommand.CanExecute(null));
        Assert.False(vm.RunSteamGridDbNowCommand.CanExecute(null));
        vm.Dispose();
    }

    [Fact]
    public void Appearance_page_turns_steamgriddb_on_only_when_asked_and_checks_only_on_click()
    {
        var (page, _, context) = Create();
        var grid = new RecordingSteamGridDb();
        context.SteamGridDb = grid;
        var vm = new AppearancePageViewModel(context);
        page.Dispose();

        Assert.False(vm.SteamGridDbEnabled);
        Assert.Equal(ShellStrings.SteamGridDbStatusOff, vm.SteamGridDbStatusText);
        Assert.Empty(grid.Calls); // Öffnen der Seite: kein Netz, kein Schalten

        vm.SetSteamGridDbKey("  geheim  ");
        Assert.Empty(grid.Calls); // Eintippen speichert und prüft nichts

        vm.SteamGridDbEnabled = true;
        Assert.Equal(["enabled:True"], grid.Calls);

        vm.CheckSteamGridDbCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => grid.Calls.Count >= 3, TimeSpan.FromSeconds(5)));
        Assert.Equal(["enabled:True", "check:geheim", "save:geheim"], grid.Calls);
        Assert.Equal(ShellStrings.SteamGridDbCheckValid, vm.SteamGridDbStatusText);
        vm.Dispose();
    }

    [Fact]
    public void Appearance_page_releases_its_leases_and_handlers_when_disposed()
    {
        var (page, _, context) = Create();
        var art = new LeaseCountingArtwork();
        context.Artwork = art;
        var vm = new AppearancePageViewModel(context);
        page.Dispose();
        Assert.Equal(1, art.OpenLeases);

        vm.Dispose();
        Assert.Equal(0, art.OpenLeases);
        Assert.Equal(0, art.Subscribers);
    }

    [Fact]
    public async Task Appearance_page_shows_the_icon_next_to_the_name_for_a_hero_made_from_the_icon()
    {
        // Das erzeugte Titelbild enthält kein Symbol (CORE); wie in der Bibliothek legt die Seite Symbol und Name darüber.
        var (page, config, context) = Create();
        page.Dispose();
        var crab = new GameConfig { Source = GameSource.Steam, SourceId = "3570950", Name = "Everything is Crab Playtest" };
        var bg3 = new GameConfig { Source = GameSource.Gog, SourceId = "1456460669", Name = "Baldur's Gate 3" };
        config.Update(c => c.Games.AddRange([crab, bg3]));
        var art = new ExampleArtwork(
            new FallbackExample(FallbackKind.HeroFromIcon, crab.Key, crab.Name, "Steam · aus Everything is Crab.exe · 256 px"),
            new FallbackExample(FallbackKind.NameInsteadOfLogo, bg3.Key, bg3.Name, "GOG · kein Logo im Cache"));
        context.Artwork = art;

        var vm = new AppearancePageViewModel(context, windowsAnimations: () => true);
        await vm.Loaded;

        var fromIcon = vm.FallbackExamples.Single(e => e.Kind == FallbackKind.HeroFromIcon);
        var nameOnly = vm.FallbackExamples.Single(e => e.Kind == FallbackKind.NameInsteadOfLogo);
        Assert.True(fromIcon.ShowsName && fromIcon.ShowsIcon && fromIcon.HasIcon);
        Assert.Same(art.IconImage, fromIcon.Icon);
        Assert.True(nameOnly.ShowsName);
        Assert.False(nameOnly.ShowsIcon || nameOnly.HasIcon); // Gegenprobe: echtes Titelbild, nur der Name
        Assert.Equal([crab.Key], art.IconRequests);
        vm.Dispose();
    }

    [Fact]
    public void Appearance_page_previews_the_most_recently_played_games()
    {
        var games = Enumerable.Range(1, 6).Select(i => new GameConfig
        {
            Source = GameSource.Steam,
            SourceId = i.ToString(CultureInfo.InvariantCulture),
            Name = "Spiel " + i,
            LastPlayed = i == 6 ? null : DateTimeOffset.Now.AddDays(-i),
        }).ToList();

        Assert.Equal(["Spiel 1", "Spiel 2", "Spiel 3", "Spiel 4"], AppearancePageViewModel.PickPreviewGames(games).Select(g => g.Name));
        Assert.Equal(["Spiel 6"], AppearancePageViewModel.PickPreviewGames([games[5]]).Select(g => g.Name));
    }

    [Fact]
    public void Colors_follow_game_writes_immediately_independent_of_the_background_switch()
    {
        var (page, config, _) = Create();
        Assert.False(page.ColorsFollowGame); // Standard aus

        page.ColorsFollowGame = true;

        Assert.True(config.Current.Settings.Appearance.ColorsFollowGame);
        Assert.True(config.Current.Settings.Appearance.BackgroundFollowsGame);
        Assert.True(page.State.ColorsFollowGame);
        page.BackgroundFollowsGame = false;
        Assert.True(config.Current.Settings.Appearance.ColorsFollowGame);
        page.Dispose();
    }

    [Fact]
    public void Standard_turns_colors_follow_game_off()
    {
        var (page, config, _) = Create();
        page.ColorsFollowGame = true;

        page.ResetCommand.Execute(null);

        Assert.False(config.Current.Settings.Appearance.ColorsFollowGame);
        Assert.False(page.ColorsFollowGame);
        page.Dispose();
    }

    [Fact]
    public async Task Colors_sample_shows_the_game_gradient_for_a_red_game_and_the_scheme_for_a_gray_one()
    {
        var (page, config, context) = Create();
        page.Dispose();
        var red = new GameConfig { Source = GameSource.Steam, SourceId = "31", Name = "Testspiel Rot", LastPlayed = DateTimeOffset.Now.AddDays(-1) };
        var gray = new GameConfig { Source = GameSource.Steam, SourceId = "32", Name = "Testspiel Grau", LastPlayed = DateTimeOffset.Now.AddDays(-2) };
        config.Update(c => c.Games.AddRange([red, gray]));
        var redColors = new GameColors("#D62828", GameColorSource.Logo, 1, [], []);
        context.Artwork = new HdrPilot.App.Tests.Library.FakeArtworkService(key => key == red.Key ? redColors : GameColors.None);

        var vm = new AppearancePageViewModel(context, windowsAnimations: () => true);
        await vm.Loaded;
        var game = GamePalette.Compute(GamePalette.Map(redColors, vm.State.Mode)!.Value, vm.State, dark: true);
        var redSample = Assert.IsType<LinearGradientBrush>(vm.ColorsFollowSample);
        Assert.Equal([game.AccentFill1, game.AccentFill2], redSample.GradientStops.Select(s => s.Color));
        Assert.Null(vm.PreviewAccents); // Schalter aus: die Vorschau zeigt das Schema, die Probe zeigt, was er ergäbe
        vm.ColorsFollowGame = true;
        Assert.Equal(GamePalette.Map(redColors, vm.State.Mode), vm.PreviewAccents);

        vm.PreviewGames.Single(g => g.Game.Key == gray.Key).IsSelected = true;
        await Task.Yield();
        var scheme = GamePalette.Scheme(vm.State, dark: true);
        var graySample = Assert.IsType<LinearGradientBrush>(vm.ColorsFollowSample);
        Assert.Equal([scheme.AccentFill1, scheme.AccentFill2], graySample.GradientStops.Select(s => s.Color));
        Assert.Null(vm.PreviewAccents);
        vm.Dispose();
    }

    [Fact]
    public async Task Preview_colors_are_requested_only_through_the_colors_source_and_canceled_on_close()
    {
        var (page, config, context) = Create();
        page.Dispose();
        var game = new GameConfig { Source = GameSource.Steam, SourceId = "33", Name = "Testspiel Blau", LastPlayed = DateTimeOffset.Now.AddDays(-1) };
        config.Update(c => c.Games.Add(game));
        var art = new HdrPilot.App.Tests.Library.FakeArtworkService(_ => new GameColors("#1E64DC", GameColorSource.Cover, 1, [], []), colorsAsync: true);
        context.Artwork = art;

        var vm = new AppearancePageViewModel(context, windowsAnimations: () => true);
        await Task.Yield();
        Assert.Equal([game.Key], art.ColorRequests); // einmal je Vorschauspiel, auch bei ausgeschaltetem Schalter
        vm.Dispose();
        Assert.Equal([game.Key], art.CanceledColorRequests);

        // Ein Bilderdienst ohne Spielfarben: kein Fehler, die Probe zeigt das Schema.
        context.Artwork = new LeaseCountingArtwork();
        var plain = new AppearancePageViewModel(context, windowsAnimations: () => true);
        await plain.Loaded;
        plain.ColorsFollowGame = true;
        Assert.Null(plain.PreviewAccents);
        Assert.Equal(GamePalette.Scheme(plain.State, true).AccentFill1, Assert.IsType<LinearGradientBrush>(plain.ColorsFollowSample).GradientStops[0].Color);
        plain.Dispose();
    }

    [Fact]
    public void Preview_layer_is_attached_only_while_the_switch_is_on()
    {
        WpfTestHost.Run(() =>
        {
            var (window, appearance) = PreviewWindow(colorsFollowGame: true);
            var model = (SettingsViewModel)window.DataContext;
            WpfTestHost.DoEvents();

            Assert.False(SystemParameters.HighContrast, "Testumgebung ohne hohen Kontrast");
            Assert.NotNull(model.Appearance.PreviewAccents);
            Assert.True(window.PreviewAccentLayerAttached);

            appearance.Update(s => s.ColorsFollowGame = false);
            Assert.Null(model.Appearance.PreviewAccents);
            Assert.False(window.PreviewAccentLayerAttached);

            appearance.Update(s => s.ColorsFollowGame = true);
            Assert.True(window.PreviewAccentLayerAttached);
            window.Close();
            Assert.False(window.PreviewAccentLayerAttached);
        });
    }

    [Fact]
    public void Stage_dot_resolves_its_shine_from_inside_the_stage()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _) = PreviewWindow(colorsFollowGame: true);
            var model = (SettingsViewModel)window.DataContext;
            WpfTestHost.DoEvents();
            var stage = (FrameworkElement)window.FindName("Stage");
            var dot = (System.Windows.Shapes.Ellipse)window.FindName("StageDot");

            model.Appearance.PreviewHdr = true;

            Assert.Same(stage.FindResource(ThemeKeys.BrushShine), dot.Fill);
            Assert.NotSame(Application.Current.FindResource(ThemeKeys.BrushShine), dot.Fill); // Spielfarbe, nicht das Schema
            model.Appearance.PreviewHdr = false;
            Assert.IsType<SolidColorBrush>(dot.Fill);
            window.Close();
        });
    }

    private static (HdrPilot.App.Views.Shell.SettingsWindow Window, StaticAppearanceService Appearance) PreviewWindow(bool colorsFollowGame)
    {
        var red = new GameConfig { Source = GameSource.Steam, SourceId = "41", Name = "Testspiel Rot", LastPlayed = DateTimeOffset.Now.AddDays(-1) };
        var config = ShellTestFactory.NewConfig(new InMemoryFileSystem(), c => c.Games.Add(red));
        var appearance = new StaticAppearanceService(new AppearanceSettings { ColorsFollowGame = colorsFollowGame });
        var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config))
        {
            Appearance = appearance,
            Artwork = new HdrPilot.App.Tests.Library.FakeArtworkService(key => key == red.Key ? new GameColors("#D62828", GameColorSource.Logo, 1, [], []) : null),
        };
        var window = new HdrPilot.App.Views.Shell.SettingsWindow();
        window.Attach(new SettingsViewModel(context, new FakeFolderPicker()));
        return (window, appearance);
    }

    private static (AppearancePageViewModel Page, ConfigService Config, FakeShellContext Context) Create()
    {
        var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
        var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config))
        {
            Appearance = new ConfigAppearance(config),
        };
        return (new AppearancePageViewModel(context, windowsAnimations: () => true), config, context);
    }

    /// <summary>Darstellung auf der echten Konfiguration im Speicher (schreibt wie AppearanceService, ohne Ressourcen).</summary>
    private sealed class ConfigAppearance(ConfigService config) : IAppearanceService
    {
        public event EventHandler? Changed;

        public AppearanceState Current => AppearanceService.Compute(config.Current.Settings.Appearance, true, true, true, false);

        public void Update(Action<AppearanceSettings> change)
        {
            config.Update(c => change(c.Settings.Appearance ??= new AppearanceSettings()));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void ResetToDefaults()
        {
            config.Update(c => c.Settings.Appearance = new AppearanceSettings());
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixedLuminance(DisplayNits nits) : IDisplayLuminanceService
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public DisplayNits? Primary => nits;

        public IReadOnlyList<DisplayNits> Displays => [nits];

        public void Invalidate()
        {
        }

        public IDisposable Acquire() => NoLease.Instance;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSteamGridDb : ISteamGridDbService
    {
        private bool _enabled;

        public event EventHandler? Changed;

        public List<string> Calls { get; } = [];

        public SteamGridDbKeyState Key { get; set; } = SteamGridDbKeyState.Missing;

        public SteamGridDbStatus Status => SteamGridDbStatus.Off with { Enabled = _enabled, Key = Key };

        public void SetEnabled(bool enabled)
        {
            Calls.Add("enabled:" + enabled);
            _enabled = enabled;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public Task<SteamGridDbCheckResult> CheckKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            Calls.Add("check:" + key);
            return Task.FromResult(SteamGridDbCheckResult.Valid);
        }

        public void SaveKey(string key) => Calls.Add("save:" + key);

        public void RemoveKey()
        {
            Calls.Add("remove");
            Key = SteamGridDbKeyState.Missing;
        }

        public Task RunNowAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("run");
            return Task.CompletedTask;
        }

        public void ScheduleStartup(bool allowed) => Calls.Add("schedule");

        public void Dispose()
        {
        }
    }

    /// <summary>Bilderdienst mit festen Beispielen; merkt sich, für welche Spiele ein Symbol angefragt wurde.</summary>
    private sealed class ExampleArtwork(params FallbackExample[] examples) : IArtworkService
    {
        public event EventHandler<ArtworkChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public ImageSource IconImage { get; } = Pixel();

        public List<string> IconRequests { get; } = [];

        public ImageSource? GetCachedImage(GameConfig game, ArtworkKind kind, int decodeWidth) => null;

        public Task<ImageSource?> GetImageAsync(GameConfig game, ArtworkKind kind, int decodeWidth, CancellationToken cancellationToken = default)
        {
            if (kind == ArtworkKind.Icon)
            {
                IconRequests.Add(game.Key);
                return Task.FromResult<ImageSource?>(IconImage);
            }

            return Task.FromResult<ImageSource?>(Pixel());
        }

        public GameArtwork? GetCachedArtwork(GameConfig game) => null;

        public Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default) =>
            NullArtworkService.Instance.GetArtworkAsync(game, heroDecodeWidth, cancellationToken);

        public Task<IReadOnlyList<ArtworkCandidate>> GetCandidatesAsync(GameConfig game, ArtworkKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArtworkCandidate>>([]);

        public string SaveCustomImage(GameConfig game, ArtworkKind kind, string sourcePath) => throw new NotSupportedException();

        public void DeleteCustomImage(string? token)
        {
        }

        public Task<ArtworkCoverage> GetCoverageAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) => Task.FromResult(ArtworkCoverage.Empty);

        public Task<IReadOnlyList<FallbackExample>> GetFallbackExamplesAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FallbackExample>>(examples);

        public IDisposable Acquire() => NoLease.Instance;

        public void ClearCache()
        {
        }

        public void Dispose()
        {
        }

        private static ImageSource Pixel()
        {
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
            bitmap.Freeze();
            return bitmap;
        }
    }

    private sealed class LeaseCountingArtwork : IArtworkService
    {
        private EventHandler<ArtworkChangedEventArgs>? _changed;

        public event EventHandler<ArtworkChangedEventArgs>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public int OpenLeases { get; private set; }

        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

        public ImageSource? GetCachedImage(GameConfig game, ArtworkKind kind, int decodeWidth) => null;

        public Task<ImageSource?> GetImageAsync(GameConfig game, ArtworkKind kind, int decodeWidth, CancellationToken cancellationToken = default) => Task.FromResult<ImageSource?>(null);

        public GameArtwork? GetCachedArtwork(GameConfig game) => null;

        public Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default) =>
            NullArtworkService.Instance.GetArtworkAsync(game, heroDecodeWidth, cancellationToken);

        public Task<IReadOnlyList<ArtworkCandidate>> GetCandidatesAsync(GameConfig game, ArtworkKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArtworkCandidate>>([]);

        public string SaveCustomImage(GameConfig game, ArtworkKind kind, string sourcePath) => throw new NotSupportedException();

        public void DeleteCustomImage(string? token)
        {
        }

        public Task<ArtworkCoverage> GetCoverageAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) => Task.FromResult(ArtworkCoverage.Empty);

        public Task<IReadOnlyList<FallbackExample>> GetFallbackExamplesAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FallbackExample>>([]);

        public IDisposable Acquire()
        {
            OpenLeases++;
            return new Lease(this);
        }

        public void ClearCache()
        {
        }

        public void Dispose()
        {
        }

        private sealed class Lease(LeaseCountingArtwork owner) : IDisposable
        {
            private bool _done;

            public void Dispose()
            {
                if (!_done)
                {
                    _done = true;
                    owner.OpenLeases--;
                }
            }
        }
    }
}
