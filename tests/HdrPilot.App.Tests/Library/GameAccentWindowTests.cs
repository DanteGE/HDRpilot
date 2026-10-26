using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>Bibliotheksfenster mit erzeugten Spielfarben für die Tests der Spielfarben (E94).</summary>
internal sealed record GameAccentSetup(MainWindow Window, LibraryViewModel Model, TestLibraryContext Context, FakeArtworkService Art, StaticAppearanceService Appearance)
{
    public static readonly GameColors Red = new("#D62828", GameColorSource.Logo, 1, [new GameSwatch("#F28C28", GameColorSource.Cover)], []);
    public static readonly GameColors Blue = new("#1E64DC", GameColorSource.Cover, 1, [], []);
    public static readonly GameColors Gray = new(null, GameColorSource.None, 0.12, [], []);

    /// <summary>Gestellte Uhr der Entprellung.</summary>
    public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(10);

    public AppearanceState State => Appearance.Current;

    public static GameAccentSetup Open(
        bool colors = true,
        bool background = true,
        string effects = "Off",
        bool colorsAsync = false,
        string? preset = null,
        Func<string, AmbientColors?>? ambient = null,
        Size? size = null)
    {
        EnsureLibraryResources();
        GameConfig[] games =
        [
            Game(GameSource.Steam, "rot", "Testspiel Rot"),
            Game(GameSource.Steam, "blau", "Testspiel Blau"),
            Game(GameSource.Steam, "grau", "Testspiel Grau"),
            Game(GameSource.Steam, "bunt", "Testspiel Bunt"),
        ];
        var art = new FakeArtworkService(
            key => key switch
            {
                "Steam:rot" => Red,
                "Steam:blau" => Blue,
                "Steam:grau" => Gray,
                _ => null,
            },
            colorsAsync)
        {
            AmbientFor = ambient,
        };
        foreach (var game in games)
        {
            art.WithHero.Add(game.Key);
        }

        var appearance = new StaticAppearanceService(new AppearanceSettings { EffectLevel = effects, ColorsFollowGame = colors, BackgroundFollowsGame = background, Preset = preset });
        var context = new TestLibraryContext(games) { Appearance = appearance, Artwork = art };
        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = Item(model, "rot");
        context.Dispatcher.RunDelayed();
        var window = new MainWindow();
        var setup = new GameAccentSetup(window, model, context, art, appearance);
        window.AccentClock = () => setup.Now;
        window.Attach(model);
        WpfTestHost.Layout(window, size ?? new Size(1600, 900));
        context.Dispatcher.RunDelayed();
        WpfTestHost.DoEvents();
        return setup;
    }

    public static GameItemViewModel Item(LibraryViewModel model, string id) => model.Games.Single(g => g.Key == "Steam:" + id);

    /// <summary>Wählt ein Spiel (die Bilder kommen sofort, auch nach einer schnellen Auswahl).</summary>
    public void Select(string id, double afterMs = 1000)
    {
        Now += TimeSpan.FromMilliseconds(afterMs);
        Model.SelectedGame = Item(Model, id);
        Context.Dispatcher.RunDelayed();
    }

    /// <summary>Erwartete Palette eines Spiels nach den Regeln der App.</summary>
    public ThemePalette Expected(GameColors colors, bool dark = true) =>
        GamePalette.Compute(GamePalette.Map(colors, State.Mode)!.Value, State, dark);

    public PaletteColors Accents(GameColors colors) => GamePalette.Map(colors, State.Mode)!.Value;

    public void Settle()
    {
        Window.AccentLayer?.Settle();
        Window.AccentBrushes.Settle();
    }

    public void Close() => Window.Close();

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }
}

/// <summary>
/// „Farben folgen dem Spiel“ im Bibliotheksfenster (E94, G15.3), auf dem WPF-Testhost erzeugt, nie angezeigt: Farbebene nur
/// bei eingeschaltetem Schalter, bleibende Pinsel, Stilsetzer und Popups lösen die Ebene auf, Überblendung und Entprellung mit
/// gestellter Uhr, hoher Kontrast, Verweise statt Momentaufnahmen, gleicher Baum, graue Spiele genau das Schema, Partikel,
/// Änderung der Darstellung, Aus und wieder An vor dem Beruhigen, Farben und Bilder in beliebiger Reihenfolge, ruhiger
/// Grund, Werkzeugleiste und helles Thema.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class GameAccentWindowTests
{
    [Fact]
    public void W1_switch_off_has_no_layer_and_reads_no_colors()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(colors: false);
            for (var i = 0; i < 10; i++)
            {
                s.Select(i % 2 == 0 ? "blau" : "rot", afterMs: 40);
            }

            Assert.Null(s.Window.AccentLayer);
            Assert.DoesNotContain(s.Window.Resources.MergedDictionaries, d => d.Contains(GameAccentLayer.MarkerKey));
            Assert.Same(Application.Current.FindResource(ThemeKeys.BrushAccent), s.Window.FindResource(ThemeKeys.BrushAccent));
            Assert.Empty(s.Art.ColorRequests);
            Assert.Equal(0, s.Window.AccentStats.Requests);
            Assert.Equal(s.State.Palette, s.Window.ShownAccents);
            s.Close();
        });
    }

    [Fact]
    public void W2_switch_on_resolves_the_game_accent_through_the_same_instance()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open();
            var accent = Assert.IsType<LinearGradientBrush>(s.Window.FindResource(ThemeKeys.BrushAccent));
            var red = s.Expected(GameAccentSetup.Red);

            Assert.Equal([red.AccentFill1, red.AccentFill2], accent.GradientStops.Select(x => x.Color));
            Assert.NotSame(Application.Current.FindResource(ThemeKeys.BrushAccent), accent);
            Assert.False(accent.IsFrozen);

            s.Select("blau");
            s.Settle();
            var blue = s.Expected(GameAccentSetup.Blue);
            Assert.Same(accent, s.Window.FindResource(ThemeKeys.BrushAccent));
            Assert.Equal([blue.AccentFill1, blue.AccentFill2], accent.GradientStops.Select(x => x.Color));

            // Neue Bedienelemente aus den Rückmeldungen (Quellenmenü, Bildlaufleiste im Spalt, Fokusrahmen) lösen dieselbe Ebene auf.
            var title = (FrameworkElement)s.Window.FindName("SourceTitleButton");
            Assert.Same(s.Window.FindResource(ThemeKeys.BrushFocusRing), title.FindResource(ThemeKeys.BrushFocusRing));
            s.Close();
        });
    }

    [Fact]
    public void W3_style_setter_brushes_resolve_the_layer()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open();
            var root = (Panel)s.Window.FindName("Root");
            var red = s.Expected(GameAccentSetup.Red);
            Border Probe(string style, object data)
            {
                var border = new Border { Style = (Style)s.Window.FindResource(style), DataContext = data };
                root.Children.Add(border);
                border.ApplyTemplate();
                WpfTestHost.DoEvents();
                return border;
            }

            var tag = Probe("Lib.Tag", new { HdrSupport = HdrSupportStatus.Native });
            var badge = Probe("Lib.HdrBadge", new { HdrSupport = HdrSupportStatus.Native });
            var artTag = Probe("Lib.ArtTag", new { IsHdrNative = true });
            var line = Probe("Lib.SectionLine", new { });

            Assert.Equal(red.C3, Assert.IsType<SolidColorBrush>(tag.Background).Color);
            Assert.Equal(red.C3, Assert.IsType<SolidColorBrush>(badge.BorderBrush).Color);
            Assert.Equal(red.Ambient3, Assert.IsType<SolidColorBrush>(artTag.BorderBrush).Color);
            Assert.Equal(red.AccentText, Assert.IsType<LinearGradientBrush>(line.Background).GradientStops[0].Color);
            foreach (var probe in new[] { tag, badge, artTag, line })
            {
                root.Children.Remove(probe);
            }

            s.Close();
        });
    }

    [Fact]
    public void W4_full_effects_fade_with_kept_instances_and_end_without_animation()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(effects: "Full");
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            var layer = s.Window.AccentLayer!;
            Action? settle = null;
            layer.TimerHookForTest = (_, a) => settle = a;
            var instances = GameAccentLayer.BrushKeys.Select(k => layer.Dictionary[k]).ToList();
            var before = layer.Stats.Animations;

            s.Select("blau");
            Assert.True(layer.Stats.Animations > before, "Wechsel überblendet");
            Assert.True(layer.Animating > 0);
            s.Select("rot"); // mitten in der Überblendung, nach der Entprellzeit

            Assert.Equal(instances, GameAccentLayer.BrushKeys.Select(k => layer.Dictionary[k]));
            settle!();
            s.Window.AccentBrushes.Settle();
            Assert.Equal(0, layer.Animating);
            Assert.Equal(0, s.Window.AccentBrushes.Animating);
            Assert.DoesNotContain(GameAccentLayer.BrushKeys, k => layer.Dictionary[k] is System.Windows.Media.Animation.Animatable { HasAnimatedProperties: true });
            s.Close();
        });
    }

    [Fact]
    public void W5_fast_selection_and_held_keys_are_debounced()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open();
            Action? timer = null;
            s.Window.AccentTimerHookForTest = (_, a) => timer = a;
            var applied = s.Window.AccentStats.Applied;
            var held = s.Window.AccentStats.Held;

            for (var i = 0; i < 20; i++)
            {
                s.Select(i % 2 == 0 ? "blau" : "rot", afterMs: i == 0 ? 1000 : 33);
            }

            Assert.Equal(applied + 1, s.Window.AccentStats.Applied);
            Assert.Equal(held + 19, s.Window.AccentStats.Held);
            s.Now += TimeSpan.FromMilliseconds(300);
            timer!();
            Assert.Equal(applied + 2, s.Window.AccentStats.Applied);
            Assert.Equal(s.Accents(GameAccentSetup.Red), s.Window.ShownAccents);

            // Gehaltene Taste: schon die erste Wiederholung hält, auch nach langer Pause.
            s.Now += TimeSpan.FromSeconds(5);
            s.Window.NoteAccentKey(repeat: true);
            s.Select("blau", afterMs: 0);
            Assert.Equal(held + 20, s.Window.AccentStats.Held);
            Assert.Equal(s.Accents(GameAccentSetup.Red), s.Window.ShownAccents);
            s.Close();
        });
    }

    [Fact]
    public void W6_context_menu_and_tooltip_resolve_the_layer()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open();
            var layerAccent = s.Window.FindResource(ThemeKeys.BrushAccent);
            var target = (FrameworkElement)s.Window.FindName("SourceTitleButton");

            var menu = Assert.IsType<ContextMenu>(s.Window.TryFindResource("GameContextMenu"));
            menu.PlacementTarget = target;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            Assert.Same(layerAccent, menu.FindResource(ThemeKeys.BrushAccent));
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent, menu));
            Assert.DoesNotContain(menu.Resources.MergedDictionaries, d => d.Contains(GameAccentLayer.MarkerKey));

            var tip = new ToolTip { PlacementTarget = target, Content = "Tipp" };
            tip.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent, tip));
            Assert.Same(layerAccent, tip.FindResource(ThemeKeys.BrushAccent));
            tip.RaiseEvent(new RoutedEventArgs(ToolTip.ClosedEvent, tip));
            s.Close();
        });
    }

    [Fact]
    public void W7_high_contrast_removes_the_layer_at_once_and_restores_it()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(effects: "Full");
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);

            s.Window.SetToolbarHighContrast(true);

            Assert.Null(s.Window.AccentLayer);
            Assert.Equal(s.State.Palette, s.Window.ShownAccents);
            Assert.Same(Application.Current.FindResource(ThemeKeys.BrushAccent), s.Window.FindResource(ThemeKeys.BrushAccent));

            s.Window.SetToolbarHighContrast(false);

            Assert.NotNull(s.Window.AccentLayer);
            Assert.Equal(s.Accents(GameAccentSetup.Red), s.Window.ShownAccents);
            Assert.Equal(0, s.Window.AccentLayer!.Animating); // sofort, ohne Überblendung
            s.Close();
        });
    }

    [Fact]
    public void W8_themed_brushes_in_code_follow_the_layer_and_the_scheme()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var on in new[] { true, false })
            {
                var s = GameAccentSetup.Open(colors: on);
                s.Window.FreezeHdrMoment(0.5); // HDR an zeigen, ohne zu schalten
                WpfTestHost.DoEvents();
                var scope = on ? (FrameworkElement)s.Window : null;
                object Expected(string key) => scope?.FindResource(key) ?? Application.Current.FindResource(key);

                Assert.Same(Expected(ThemeKeys.BrushShine), Named<Ellipse>(s, "CapsuleDot").Fill);
                Assert.Same(Expected(ThemeKeys.BrushTextOnAccent), Named<System.Windows.Shapes.Path>(s, "BrandGlyph").Stroke);
                Assert.Same(Expected(ThemeKeys.BrushAccentShine), Named<NitsScaleBar>(s, "NitsScale").Fill);
                Assert.Same(Expected(ThemeKeys.BrushAccentText), Named<TextBlock>(s, "EffectsGlyph").Foreground);
                Assert.True(IsResourceReference(Named<Ellipse>(s, "StatusDot"), Shape.FillProperty));

                typeof(LibraryViewModel).GetMethod("SetPreviewHdr", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s.Model, [false]);
                WpfTestHost.DoEvents();
                Assert.Same(Expected(ThemeKeys.BrushAccentText), Named<System.Windows.Shapes.Path>(s, "BrandGlyph").Stroke);
                Assert.True(IsResourceReference(Named<Border>(s, "EdgeLine"), Border.BorderBrushProperty));
                s.Close();
            }
        });
    }

    [Fact]
    public void W9_visual_trees_are_identical_apart_from_brush_colors()
    {
        WpfTestHost.Run(() =>
        {
            string Shape(GameAccentSetup s)
            {
                var names = new List<string>();
                foreach (var name in new[] { "GameList", "HeroLarge", "ToolbarArea", "DetailsArea" })
                {
                    if (s.Window.FindName(name) is DependencyObject root)
                    {
                        names.AddRange(WpfTestHost.Visuals(root).Select(v => v.GetType().Name));
                    }
                }

                return string.Join(",", names);
            }

            var off = GameAccentSetup.Open(colors: false);
            var on = GameAccentSetup.Open(colors: true);

            Assert.Equal(Shape(off), Shape(on));
            off.Close();
            on.Close();
        });
    }

    [Fact]
    public void W10_gray_game_gives_exactly_the_scheme_while_loading_and_empty_selection_keep_the_colors()
    {
        WpfTestHost.Run(() =>
        {
            var royal = ThemeResources.Build(PaletteEngine.Compute(GamePaletteState("royal").Palette, true, true));
            Application.Current.Resources.MergedDictionaries.Add(royal);
            try
            {
                var s = GameAccentSetup.Open(preset: "royal", colorsAsync: true);
                s.Art.CompleteColors("Steam:rot");
                WpfTestHost.DoEvents();
                s.Select("blau");
                s.Select("rot");
                Assert.Equal(s.Accents(GameAccentSetup.Red), s.Window.ShownAccents);

                // Bilder bzw. Farben laden noch: Rot bleibt, nie zwischendurch das Schema.
                s.Select("blau");
                Assert.Equal(s.Accents(GameAccentSetup.Red), s.Window.ShownAccents);

                // Graues Spiel: genau das Farbschema der App.
                s.Art.CompleteColors("Steam:grau");
                WpfTestHost.DoEvents();
                s.Select("grau");
                s.Settle();
                Assert.Equal(GamePalette.Scheme(s.State, dark: true), s.Window.AccentLayer!.Target);
                var accent = (LinearGradientBrush)s.Window.FindResource(ThemeKeys.BrushAccent);
                Assert.Equal(((LinearGradientBrush)royal[ThemeKeys.BrushAccent]).GradientStops.Select(x => x.Color), accent.GradientStops.Select(x => x.Color));

                // Leere Auswahl (Suche ohne Treffer): gezeigte Farben bleiben.
                s.Art.CompleteColors("Steam:blau");
                WpfTestHost.DoEvents();
                s.Select("blau");
                var shown = s.Window.ShownAccents;
                s.Model.SearchText = "kein Treffer weit und breit";
                s.Model.ApplySearch();
                Assert.Null(s.Model.SelectedGame);
                Assert.Equal(shown, s.Window.ShownAccents);
                s.Close();
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(royal);
            }
        });
    }

    [Fact]
    public void W11_particles_and_burst_use_the_shown_accents()
    {
        WpfTestHost.Run(() =>
        {
            var off = GameAccentSetup.Open(background: false);
            var red = off.Accents(GameAccentSetup.Red);
            Assert.Contains(red.Accent, off.Window.MomentParticleColors);
            Assert.Equal([red.Accent, red.Shine], off.Window.ButtonBurstColors);
            off.Close();

            var on = GameAccentSetup.Open(background: true);
            Assert.Contains(on.Accents(GameAccentSetup.Red).Shine, on.Window.MomentParticleColors);
            Assert.DoesNotContain(on.State.Palette.Accent, on.Window.MomentParticleColors);
            on.Close();
        });
    }

    [Fact]
    public void W12_appearance_and_theme_changes_recompute_the_layer()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open();
            s.Appearance.Update(a => a.Preset = "cyber");
            s.Settle();
            Assert.Equal(s.Expected(GameAccentSetup.Red), s.Window.AccentLayer!.Target);

            // Thema: AppearanceService meldet sich zuerst (noch mit dem alten _dark), das Fenster folgt nach ReadTheme sofort.
            WpfTestHost.UseTheme(false);
            try
            {
                s.Appearance.RaiseChanged();
                typeof(MainWindow).GetMethod("OnThemeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s.Window, [null, EventArgs.Empty]);
                Assert.Equal(s.Expected(GameAccentSetup.Red, dark: false), s.Window.AccentLayer!.Target);
                Assert.Equal(0, s.Window.AccentLayer.Animating);
            }
            finally
            {
                WpfTestHost.UseTheme(true);
            }

            s.Close();
        });
    }

    [Fact]
    public void W13_off_and_on_again_before_settling_keeps_the_layer()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(effects: "Full");
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            var layer = s.Window.AccentLayer!;
            Action? settle = null;
            layer.TimerHookForTest = (_, a) => settle = a;

            s.Appearance.Update(a => a.ColorsFollowGame = false);
            s.Appearance.Update(a => a.ColorsFollowGame = true);
            settle!();

            Assert.Same(layer, s.Window.AccentLayer);
            Assert.Contains(layer.Dictionary, s.Window.Resources.MergedDictionaries);

            // Gegenprobe: aus und beruhigt – die Ebene ist weg.
            s.Appearance.Update(a => a.ColorsFollowGame = false);
            settle!();
            Assert.Null(s.Window.AccentLayer);
            s.Close();
        });
    }

    [Fact]
    public void W14_colors_and_art_apply_together_in_any_order_and_stale_colors_are_ignored()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(colorsAsync: true);
            var start = s.Window.ShownAccents;

            // Bild zuerst, Farben danach.
            s.Select("blau");
            Assert.Equal(start, s.Window.ShownAccents);
            s.Art.CompleteColors("Steam:blau");
            WpfTestHost.DoEvents();
            Assert.Equal(s.Accents(GameAccentSetup.Blue), s.Window.ShownAccents);

            // Farben einer früheren Auswahl kommen zu spät: werden nicht gezeigt.
            s.Select("rot");
            s.Select("bunt");
            s.Art.CompleteColors("Steam:rot");
            WpfTestHost.DoEvents();
            Assert.Equal(s.Accents(GameAccentSetup.Blue), s.Window.ShownAccents);
            s.Close();
        });

        WpfTestHost.Run(() =>
        {
            // Farben zuerst (bekannt), Bild danach: angewendet erst mit dem Bild.
            var s = GameAccentSetup.Open();
            s.Context.Dispatcher.RunDelayed();
            s.Now += TimeSpan.FromSeconds(1);
            s.Model.SelectedGame = GameAccentSetup.Item(s.Model, "blau");
            if (s.Context.Dispatcher.PendingDelays > 0)
            {
                Assert.Equal(s.Accents(GameAccentSetup.Red), s.Window.ShownAccents); // Bild fehlt noch
                s.Context.Dispatcher.RunDelayed();
            }

            Assert.Equal(s.Accents(GameAccentSetup.Blue), s.Window.ShownAccents);
            s.Close();
        });
    }

    [Fact]
    public void W15_calm_background_without_background_switch_and_gray_mix_with_both_on()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(background: false);
            var calm = GamePalette.Calm(s.State.Palette);
            var expected = AmbientPalette.Resolve(null, calm, false, true);
            var stage = s.Window.HeroStage;
            var first = Ambient(s);
            Assert.Equal(expected.A2, first.A2);
            Assert.True(ColorMath.Chroma(first.A2) <= 0.035 && ColorMath.Chroma(first.A3) <= 0.035);
            s.Select("blau");
            Assert.Equal(first, Ambient(s)); // gleich für jedes Spiel
            Assert.NotNull(stage);
            s.Close();

            // Beide an, graues Titelbild: die Mischung nimmt die gezeigten Akzente statt des Schemas.
            var gray = new AmbientColors("#262626", "#7A7A7A", "#C8C8C8", 0.6, 0.4, 0.5, AmbientDullness.Gray);
            var both = GameAccentSetup.Open(background: true, ambient: _ => gray);
            var mixed = AmbientPalette.Resolve(gray, both.Accents(GameAccentSetup.Red), true, true);
            Assert.Equal(mixed.A2, Ambient(both).A2);
            Assert.NotEqual(AmbientPalette.Resolve(gray, both.State.Palette, true, true).A2, Ambient(both).A2);
            both.Close();
        });

        static AmbientPalette Ambient(GameAccentSetup s) =>
            (AmbientPalette)typeof(MainWindow).GetField("_palette", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s.Window)!;
    }

    [Fact]
    public void W16_toolbar_follows_the_accents_and_ambient_never_cancels_an_accent_fade()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(effects: "Full");
            var red = s.Accents(GameAccentSetup.Red);
            var seam = Assert.IsType<LinearGradientBrush>(Named<Rectangle>(s, "SeamCore").Fill);
            Assert.Equal(AmbientPalette.Alpha(red.Atmosphere, 0.95), seam.GradientStops[^1].Color);
            var toggle = Named<ToolbarToggle>(s, "ToolbarToggle");
            Assert.Equal(Color.FromArgb(0x8C, red.Atmosphere.R, red.Atmosphere.G, red.Atmosphere.B), toggle.HaloColors[0]);
            Assert.Equal(Color.FromArgb(0x38, red.Accent.R, red.Accent.G, red.Accent.B), toggle.HaloColors[1]);

            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            s.Window.AccentLayer!.TimerHookForTest = (_, _) => { };
            s.Select("blau");
            Assert.Contains(seam.GradientStops, x => x.HasAnimatedProperties);
            Assert.Same(seam, Named<Rectangle>(s, "SeamCore").Fill);

            // Wie bei jeder Auswahl: ApplyAmbient läuft gleich danach und setzt dieselben Ziele – die Überblendung läuft weiter.
            typeof(MainWindow).GetMethod("ApplyAmbient", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s.Window, [true]);
            Assert.Contains(seam.GradientStops, x => x.HasAnimatedProperties);
            s.Settle();
            Assert.Equal(AmbientPalette.Alpha(s.Accents(GameAccentSetup.Blue).Atmosphere, 0.95), seam.GradientStops[^1].Color);
            s.Close();
        });
    }

    [Fact]
    public void W17_light_theme_gives_light_layer_values()
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.UseTheme(false);
            try
            {
                var s = GameAccentSetup.Open();
                Assert.True(s.Appearance.IsDark); // der Dienst bleibt dunkel, das Fenster liest das Thema aus den Ressourcen
                Assert.Equal(s.Expected(GameAccentSetup.Red, dark: false), s.Window.AccentLayer!.Target);
                Assert.Equal(s.Expected(GameAccentSetup.Red, dark: false).AccentText, ((SolidColorBrush)s.Window.FindResource(ThemeKeys.BrushAccentText)).Color);
                s.Close();
            }
            finally
            {
                WpfTestHost.UseTheme(true);
            }
        });
    }

    internal static AppearanceState GamePaletteState(string preset) =>
        AppearanceService.Compute(new AppearanceSettings { Preset = preset }, true, true, true, false);

    private static T Named<T>(GameAccentSetup s, string name) => (T)s.Window.FindName(name);

    private static bool IsResourceReference(DependencyObject element, DependencyProperty property) =>
        element.ReadLocalValue(property)?.GetType().Name == "ResourceReferenceExpression";
}
