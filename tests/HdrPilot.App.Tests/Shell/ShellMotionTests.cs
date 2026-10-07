using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Windows-Einstellung „Animationseffekte“: Ist sie aus, stehen alle Elemente sofort im Endzustand, es läuft keine
/// Animation und es entsteht kein Unschärfe-Effekt. Jeder Test setzt <see cref="ShellMotion.Override"/> nur innerhalb
/// eines einzigen Aufrufs auf dem WPF-Testhost (andere WPF-Tests können sich dort nicht dazwischenschieben).
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class ShellMotionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entrance_runs_only_when_animations_are_allowed(bool allowed)
    {
        var (opacity, animated) = WithMotion(allowed, () =>
        {
            var content = new Grid();
            var card = new Border();
            content.Children.Add(card);
            ShellMotion.PlayEntrance(content, [card]);
            return (card.Opacity, card.HasAnimatedProperties || content.HasAnimatedProperties);
        });

        Assert.Equal(allowed, animated);
        Assert.Equal(allowed ? 0.0 : 1.0, opacity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Check_pop_runs_only_when_animations_are_allowed(bool allowed)
    {
        var (opacity, animated) = WithMotion(allowed, () =>
        {
            var box = new CheckBox
            {
                Style = (Style)Application.Current.FindResource("Hp.Shell.CheckCard"),
                DataContext = new DisplayOptionViewModel("p", "Bildschirm", true, true, false, true, false),
            };
            WpfTestHost.Layout(box, new Size(400, 100));
            var check = (FrameworkElement)box.Template.FindName("Check", box);

            box.IsChecked = true;
            WpfTestHost.DoEvents();
            return (check.Opacity, check.RenderTransform.HasAnimatedProperties);
        });

        Assert.Equal(1.0, opacity);
        Assert.Equal(allowed, animated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hover_feedback_without_animations_is_immediate_and_without_blur(bool allowed)
    {
        var result = WithMotion(allowed, () =>
        {
            var section = new HeaderedContentControl
            {
                Style = (Style)Application.Current.FindResource("Hp.Shell.Section"),
                Header = "Bildschirme",
                Content = new TextBlock { Text = "Inhalt" },
            };
            WpfTestHost.Layout(section, new Size(500, 300));
            var ring = (UIElement)section.Template.FindName("PART_Ring", section);
            var glow = (UIElement)section.Template.FindName("PART_Glow", section);

            section.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            var hover = (Ring: ring.Opacity, RingAnimated: ring.HasAnimatedProperties, Blur: glow.Effect is BlurEffect);

            section.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            return (hover, LeaveRing: ring.Opacity, LeaveBlur: glow.Effect is BlurEffect);
        });

        if (allowed)
        {
            // Mit Animationen: Rahmen blendet ein, Leuchten mit Unschärfe nur auf dieser einen Karte.
            Assert.True(result.hover.RingAnimated);
            Assert.True(result.hover.Blur);
        }
        else
        {
            Assert.Equal(1.0, result.hover.Ring);
            Assert.False(result.hover.RingAnimated);
            Assert.False(result.hover.Blur);
            Assert.Equal(0.0, result.LeaveRing);
            Assert.False(result.LeaveBlur);
        }
    }

    [Fact]
    public void Override_notifies_bindings_and_falls_back_to_the_windows_setting()
    {
        var events = WpfTestHost.Run(() =>
        {
            var names = new List<string?>();
            void Handler(object? s, System.ComponentModel.PropertyChangedEventArgs e) => names.Add(e.PropertyName);
            ShellMotion.StaticPropertyChanged += Handler;
            try
            {
                ShellMotion.Override = false;
                Assert.False(ShellMotion.IsEnabled);
                ShellMotion.Override = true;
                Assert.True(ShellMotion.IsEnabled);
            }
            finally
            {
                ShellMotion.Override = null;
                ShellMotion.StaticPropertyChanged -= Handler;
            }

            Assert.Equal(SystemParameters.ClientAreaAnimation && (RenderCapability.Tier >> 16) > 0, ShellMotion.IsEnabled);
            return names;
        });

        Assert.Equal([nameof(ShellMotion.IsEnabled), nameof(ShellMotion.IsEnabled), nameof(ShellMotion.IsEnabled)], events);
    }

    private static T WithMotion<T>(bool allowed, Func<T> body) => WpfTestHost.Run(() =>
    {
        ShellMotion.Override = allowed;
        try
        {
            return body();
        }
        finally
        {
            ShellMotion.Override = null;
        }
    });
}
