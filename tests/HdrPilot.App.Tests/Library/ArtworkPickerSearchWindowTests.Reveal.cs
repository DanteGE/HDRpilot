using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Bildschirmfotos der SteamGridDB-Szenen (E93): Sie nehmen erst auf, wenn jede neue Kachel ganz eingeblendet ist. Früher
/// warteten sie fest 300 ms, das Einblenden dauert gestaffelt bis 650 ms (430 + 220). Echte Uhren auf dem Testhost.
/// </summary>
public sealed partial class ArtworkPickerSearchWindowTests
{
    [Fact]
    public void Snapshot_scenes_wait_until_every_new_tile_is_fully_shown()
    {
        WpfTestHost.Run(() =>
        {
            var previous = LibraryMotion.Instance.Level;
            try
            {
                LibraryMotion.Instance.SetLevel(EffectLevel.Full);
                var span = ArtworkPickerDialog.RevealLatestStart + ArtworkPickerDialog.RevealDuration;
                Assert.Equal(TimeSpan.FromMilliseconds(650), span);

                // Die 24. Kachel beginnt 414 ms später und braucht 220 ms: Das Einblenden läuft noch über 300 ms hinaus. Das zeigt
                // die Prüfung nur, wenn der Thread des Testhosts zwischen Suche und Prüfung nicht steht (im vollen Testlauf stand
                // er einmal so lange, dass das Einblenden fast vorbei war). Die Zeit seit vor der Suche ist eine obere Grenze für
                // die Zeit seit Beginn des Einblendens: Ist sie zu lang, entscheidet die Prüfung nichts, und der Ablauf beginnt
                // noch einmal mit einem neuen Dialog, höchstens dreimal; der dritte Versuch prüft immer.
                var lastTileEnd = ArtworkPickerDialog.RevealStep * 23 + ArtworkPickerDialog.RevealDuration;
                ArtworkPickerDialog dialog;
                List<ListBoxItem> tiles;
                for (var attempt = 1; ; attempt++)
                {
                    (dialog, var picker, _) = Open(ArtworkKind.Hero);
                    Arrange(dialog, double.PositiveInfinity);
                    Assert.Equal(TimeSpan.Zero, dialog.RevealRemaining);

                    var sinceSearch = Stopwatch.StartNew();
                    Search(picker, ArtworkKind.Hero);
                    Arrange(dialog, double.PositiveInfinity);
                    WpfTestHost.DoEvents();
                    var remaining = dialog.RevealRemaining;
                    var elapsed = sinceSearch.Elapsed;
                    tiles = Descendants(Find<ListBox>(dialog, "Results")).OfType<ListBoxItem>().ToList();
                    Assert.True(tiles.Count >= 24, "Kacheln: " + tiles.Count);
                    if (!LibraryMotion.Enabled)
                    {
                        Assert.Equal(TimeSpan.Zero, remaining); // ohne Hardware-Darstellung kein Einblenden
                        dialog.Close();
                        return;
                    }

                    Assert.InRange(remaining, TimeSpan.Zero, span);
                    if (elapsed > lastTileEnd - TimeSpan.FromMilliseconds(400) && attempt < 3)
                    {
                        dialog.Close();
                        continue;
                    }

                    Assert.True(
                        remaining >= TimeSpan.FromMilliseconds(400),
                        FormattableString.Invariant($"Einblenden läuft nur noch {remaining.TotalMilliseconds:0} ms, {elapsed.TotalMilliseconds:0} ms nach Beginn der Suche (Versuch {attempt})"));
                    break;
                }

                var wait = HdrPilot.App.AppServices.RevealEndedAsync(dialog);
                var clock = Stopwatch.StartNew();
                Frames(() => wait.IsCompleted, 3000);
                Assert.True(wait.IsCompleted, "Warten endet nicht");

                // Ein frischer Bildtakt wie vor der Aufnahme, dann steht jede Kachel ganz da.
                var waited = clock.ElapsedMilliseconds;
                Frames(() => false, 40);
                foreach (var tile in tiles)
                {
                    Assert.True(tile.Opacity >= 0.98, FormattableString.Invariant($"Kachel {tiles.IndexOf(tile)} nach {waited} ms bei Deckkraft {tile.Opacity:0.###}"));
                    Assert.True(tile.RenderTransform is not TranslateTransform { Y: > 0.2 }, FormattableString.Invariant($"Kachel {tiles.IndexOf(tile)} nach {waited} ms noch verschoben"));
                }

                Assert.Equal(TimeSpan.Zero, dialog.RevealRemaining);

                dialog.Close();
            }
            finally
            {
                LibraryMotion.Instance.SetLevel(previous);
            }
        });
    }

    /// <summary>Pumpt Nachrichten und Bildtakt, bis <paramref name="done"/> gilt oder <paramref name="milliseconds"/> vergangen sind.</summary>
    private static void Frames(Func<bool> done, int milliseconds)
    {
        EventHandler frame = (_, _) => { };
        CompositionTarget.Rendering += frame;
        try
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.ElapsedMilliseconds < milliseconds)
            {
                WpfTestHost.DoEvents();
                Thread.Sleep(5);
            }
        }
        finally
        {
            CompositionTarget.Rendering -= frame;
        }
    }
}
