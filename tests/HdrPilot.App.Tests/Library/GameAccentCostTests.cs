using System.Reflection;
using System.Windows.Threading;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Kosten der Spielfarben im Fenster (E94, G15.7), nur über Zähler und Speicher, nie über Zeit: je angewendetem Wechsel
/// höchstens ein Austausch der Farbwerte, kein neuer Pinsel, höchstens 19 Überblendungen in der Ebene und 22 bei den Pinseln
/// im Code; Speicher je Wechsel des Akzentpfads unter der Grenze; gehaltene Auswahl ohne Arbeit; Schalter aus (Effekte aus und
/// voll) ohne Ebene, Anfrage, Rechnung und Zeitgeber, ein neues Schema beruhigt die Pinsel nur, solange sie überblenden; nach
/// dem Beruhigen keine Zeitgeber und keine laufenden Animationen.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class GameAccentCostTests(ITestOutputHelper output)
{
    /// <summary>Speichergrenze je angewendetem Wechsel des Akzentpfads (Planung 256 KB; gemessener Wert steht im Protokoll).</summary>
    private const long AppliedBudget = 256 * 1024;

    private const long HeldBudget = 1024;

    [Fact]
    public void Each_applied_change_swaps_the_color_part_at_most_once_and_never_replaces_a_brush()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(effects: "Full");
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            var layer = s.Window.AccentLayer!;
            layer.TimerHookForTest = (_, _) => { };
            for (var i = 0; i < 6; i++)
            {
                var (swaps, layerAnimations, brushAnimations) = (layer.Stats.ColorPartSwaps, layer.Stats.Animations, s.Window.AccentBrushes.Stats.Animations);

                s.Select(i % 2 == 0 ? "blau" : "rot");

                Assert.InRange(layer.Stats.ColorPartSwaps - swaps, 0, 1);
                Assert.InRange(layer.Stats.Animations - layerAnimations, 1, 19);
                Assert.InRange(s.Window.AccentBrushes.Stats.Animations - brushAnimations, 1, 22);
            }

            Assert.Equal(0, layer.Stats.Replacements);
            Assert.Equal(0, s.Window.AccentBrushes.Stats.Replacements);
            s.Settle();
            s.Close();
        });
    }

    [Fact]
    public void Allocation_per_applied_change_stays_within_the_budget()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(effects: "Full");
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            s.Window.AccentLayer!.TimerHookForTest = (_, _) => { };
            var update = AccentPath(s.Window);
            var red = s.Accents(GameAccentSetup.Red);
            var blue = s.Accents(GameAccentSetup.Blue);
            update(blue, false); // Aufwärmen (Grenzen der Bibliothek, Zwischenspeicher)
            update(red, false);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10; i++)
            {
                update(i % 2 == 0 ? blue : red, false);
            }

            var perChange = (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
            output.WriteLine($"Speicher je angewendetem Wechsel (Akzentpfad): {perChange} Bytes");
            Assert.True(perChange <= AppliedBudget, $"{perChange} Bytes je Wechsel");
            s.Settle();
            s.Close();
        });
    }

    [Fact]
    public void Held_selections_cost_no_layer_or_brush_work()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(effects: "Full");
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            var layer = s.Window.AccentLayer!;
            layer.TimerHookForTest = (_, _) => { };
            s.Window.AccentTimerHookForTest = (_, _) => { };
            var select = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), s.Window, Method("OnGameAccentSelection"));
            s.Select("blau");
            var (applied, animations, brushes) = (layer.Stats.Applied, layer.Stats.Animations, s.Window.AccentBrushes.Stats.Animations);

            long allocated = 0;
            for (var i = 0; i < 50; i++)
            {
                s.Select(i % 2 == 0 ? "rot" : "blau", afterMs: 33);
                s.Now += TimeSpan.FromMilliseconds(1);
                var before = GC.GetAllocatedBytesForCurrentThread();
                select(false);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Assert.Equal(applied, layer.Stats.Applied);
            Assert.Equal(animations, layer.Stats.Animations);
            Assert.Equal(brushes, s.Window.AccentBrushes.Stats.Animations);
            output.WriteLine($"Speicher je gehaltener Auswahl (Akzentpfad): {allocated / 50} Bytes");
            Assert.True(allocated / 50 <= HeldBudget, $"{allocated / 50} Bytes je gehaltener Auswahl");
            s.Close();
        });
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("Full")]
    public void Switch_off_does_no_layer_work_and_asks_for_no_colors(string effects)
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(colors: false, effects: effects);
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            var settles = 0;
            s.Window.BrushSettleHookForTest = (_, _) => settles++;
            var computed = s.Window.AccentStats.Computed;
            for (var i = 0; i < 10; i++)
            {
                s.Select(i % 2 == 0 ? "blau" : "rot", afterMs: i < 5 ? 1000 : 33); // einzeln und schnell hintereinander
            }

            Assert.Null(s.Window.AccentLayer);
            Assert.Empty(s.Art.ColorRequests);
            Assert.Equal(0, s.Window.AccentStats.Requests);
            Assert.Equal(0, s.Window.AccentStats.Applied);
            Assert.Equal(0, s.Window.AccentStats.Held);
            Assert.Equal(computed, s.Window.AccentStats.Computed); // keine Zielpalette (GamePalette/PaletteEngine)
            Assert.Equal(0, settles); // kein Zeitgeber zum Beruhigen der Pinsel
            Assert.False(((DispatcherTimer?)Field(s.Window, "_brushSettleTimer"))?.IsEnabled == true);
            Assert.False(((DispatcherTimer?)Field(s.Window, "_accentTimer"))?.IsEnabled == true);
            Assert.Equal(0, s.Window.AccentBrushes.Stats.Animations);
            Assert.Equal(0, s.Window.AccentBrushes.Animating);
            s.Close();
        });
    }

    [Fact]
    public void Switch_off_with_a_new_scheme_settles_the_brushes_only_while_they_fade()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(colors: false, effects: "Full");
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            Action? settle = null;
            s.Window.BrushSettleHookForTest = (_, a) => settle = a;
            var update = AccentPath(s.Window);

            update(null, true); // sofort und gleiches Schema: nichts
            Assert.Null(settle);

            s.Appearance.Update(a => a.Preset = "royal");
            WpfTestHost.DoEvents();
            Assert.Null(s.Window.AccentLayer);
            Assert.True(s.Window.AccentBrushes.Animating > 0, "neues Schema überblendet die Pinsel im Code");
            Assert.NotNull(settle);
            settle!();
            Assert.Equal(0, s.Window.AccentBrushes.Animating);
            s.Close();
        });
    }

    [Fact]
    public void After_settling_nothing_runs()
    {
        WpfTestHost.Run(() =>
        {
            var s = GameAccentSetup.Open(effects: "Full");
            s.Window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            var clocks = s.Window.Motion.LiveClocks;
            s.Select("blau");
            s.Select("rot", afterMs: 50); // gehalten: Zeitgeber der Entprellung läuft
            s.Now += TimeSpan.FromMilliseconds(400);
            ((DispatcherTimer?)Field(s.Window, "_accentTimer"))?.Stop();
            Method("OnAccentTimer").Invoke(s.Window, null);

            s.Settle();

            Assert.Equal(0, s.Window.AccentLayer!.Animating);
            Assert.Equal(0, s.Window.AccentBrushes.Animating);
            Assert.False(((DispatcherTimer?)Field(s.Window, "_accentTimer"))?.IsEnabled == true);
            Assert.False(((DispatcherTimer?)Field(s.Window, "_brushSettleTimer"))?.IsEnabled == true);
            Assert.Equal(clocks, s.Window.Motion.LiveClocks); // „Uhren“ unverändert: die Überblendung ist kein Dauer-Effekt
            s.Close();
        });
    }

    private static Action<PaletteColors?, bool> AccentPath(MainWindow window) =>
        (Action<PaletteColors?, bool>)Delegate.CreateDelegate(typeof(Action<PaletteColors?, bool>), window, Method("UpdateGameAccents"));

    private static MethodInfo Method(string name) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static object? Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
}
