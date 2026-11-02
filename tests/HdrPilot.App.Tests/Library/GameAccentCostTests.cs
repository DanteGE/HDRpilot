using System.Reflection;
using System.Windows.Threading;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Kosten der Spielfarben im Fenster (E94, G15.7), nur über Zähler und Speicher, nie über Zeit: je angewendetem Wechsel
/// höchstens ein Austausch der Farbwerte, kein neuer Pinsel, höchstens 19 Überblendungen in der Ebene und 22 bei den Pinseln
/// im Code; Speicher je Wechsel des Akzentpfads höchstens eine Ressourcen-Änderung im selben Fenster plus 150 KiB eigener
/// Anteil; gehaltene Auswahl ohne Arbeit; Schalter aus (Effekte aus und voll) ohne Ebene, Anfrage, Rechnung und Zeitgeber, ein
/// neues Schema beruhigt die Pinsel nur, solange sie überblenden; nach dem Beruhigen keine Zeitgeber und keine laufenden
/// Animationen.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class GameAccentCostTests(ITestOutputHelper output)
{
    /// <summary>
    /// Eigener Anteil der Spielfarben am Speicher je angewendetem Wechsel: 150 KiB = 153.600 Byte (Entscheidung des Nutzers
    /// 2026-10-28, E94). Die Grenze ist eine Ressourcen-Änderung im selben Fenster (<see cref="ResourceChangeCost"/>) plus
    /// dieser Anteil. Den einen Austausch der Farbwerte je Wechsel verlangt die Spezifikation; was WPF dafür im ganzen Baum tut,
    /// wächst mit dem Fenster, und die Grenze wandert mit. Geprüft wird genau der Teil, den die Spielfarben selbst verursachen.
    /// </summary>
    private const long OwnShareBudget = 150 * 1024;

    /// <summary>
    /// Untergrenze der Vergleichsmessung (64 KiB, rund ein Drittel des gemessenen Werts): Eine Änderung in einem Wörterbuch ohne
    /// Baum kostet rund 24 Byte, in einem Fenster mit 1000 Elementen ohne Ressourcen-Verweise rund 550 Byte, in diesem Fenster
    /// rund 213 KB (rund 1250 Elemente, viele mit DynamicResource). Darunter hat die Messung den Baum des Fensters nicht oder nur
    /// zum Teil erfasst, misst also nicht, was der Austausch der Farbwerte kostet.
    /// </summary>
    private const long MinResourceChange = 64 * 1024;

    /// <summary>Gemessene Wechsel nach dem Aufwärmen (Spezifikation: 10).</summary>
    private const int MeasuredChanges = 10;

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

                // Die gestellte Uhr steht, auf dem Bildschirm wäre also noch die Startfarbe: ohne Beruhigen hätte der nächste
                // Wechsel (zurück) nichts zu überblenden. Beruhigt beginnt jeder Wechsel wie im Betrieb bei der zuletzt gezeigten Farbe.
                s.Settle();
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
            var layer = s.Window.AccentLayer!;
            layer.TimerHookForTest = (_, _) => { };
            var update = AccentPath(s.Window);
            var red = s.Accents(GameAccentSetup.Red);
            var blue = s.Accents(GameAccentSetup.Blue);
            update(blue, false); // Aufwärmen (Grenzen der Bibliothek, Zwischenspeicher)
            update(red, false);
            var (applied, swaps) = (layer.Stats.Applied, layer.Stats.ColorPartSwaps);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < MeasuredChanges; i++)
            {
                update(i % 2 == 0 ? blue : red, false);
            }

            var perChange = (GC.GetAllocatedBytesForCurrentThread() - before) / MeasuredChanges;
            var walk = ResourceChangeCost(s.Window);
            var limit = walk + OwnShareBudget;
            output.WriteLine(
                $"Speicher je angewendetem Wechsel (Akzentpfad): {perChange} Bytes; eine Ressourcen-Änderung im selben Fenster (WPF geht den Baum durch): {walk} Bytes; eigener Anteil der Spielfarben: {perChange - walk} Bytes; Grenze: {walk} + {OwnShareBudget} = {limit} Bytes");

            // Gegen eine leere Messung: Jeder gemessene Wechsel wurde angewendet und tauschte die Farbwerte genau einmal, die
            // Vergleichsmessung erfasste den Baum, und ein Wechsel kostet mindestens diesen einen Austausch.
            Assert.Equal(MeasuredChanges, layer.Stats.Applied - applied);
            Assert.Equal(MeasuredChanges, layer.Stats.ColorPartSwaps - swaps);
            Assert.True(walk >= MinResourceChange, $"Ressourcen-Änderung im selben Fenster nur {walk} Bytes (mindestens {MinResourceChange}): Die Vergleichsmessung hat den Baum nicht erfasst");
            Assert.True(perChange >= walk, $"{perChange} Bytes je Wechsel, weniger als eine Ressourcen-Änderung ({walk} Bytes), obwohl jeder Wechsel die Farbwerte austauscht: Die Messung ist kaputt");
            Assert.True(perChange <= limit, $"{perChange} Bytes je Wechsel über der Grenze {limit} Bytes (Ressourcen-Änderung {walk} + eigener Anteil {OwnShareBudget}); eigener Anteil {perChange - walk} Bytes");
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

    /// <summary>
    /// Speicher einer einzelnen Ressourcen-Änderung im Fenster (ein Schlüssel, der nirgends benutzt wird), die Vergleichsgröße der
    /// Speichergrenze: WPF meldet jede Änderung über den ganzen Baum. Der Austausch der Farbwerte der Ebene kostet in WPF ebenso
    /// viel (gemessen bis auf 0,3 % gleich), unabhängig von Schlüsseln und Werten.
    /// </summary>
    private static long ResourceChangeCost(MainWindow window)
    {
        const string probe = "Probe.Unbenutzt";
        window.Resources[probe] = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1; i <= 10; i++)
        {
            window.Resources[probe] = i;
        }

        var cost = (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
        window.Resources.Remove(probe);
        return cost;
    }

    private static Action<PaletteColors?, bool> AccentPath(MainWindow window) =>
        (Action<PaletteColors?, bool>)Delegate.CreateDelegate(typeof(Action<PaletteColors?, bool>), window, Method("UpdateGameAccents"));

    private static MethodInfo Method(string name) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static object? Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
}
