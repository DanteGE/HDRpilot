using System.IO;
using System.Windows.Media.Imaging;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.Theming;
using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Fenstersymbol in den Palettenfarben (E89, G10.2): alle Größen 16–256 px in einer Symboldatei, gezeichnet nur bei Bedarf
/// und einmal je Zustand; Fenster übernehmen Änderungen und melden sich beim Schließen ab; das Tray reicht seinen Zustand
/// weiter und zeichnet bei einer Farbänderung nur, wenn sich die Spezifikation ändert.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class WindowIconTests
{
    [Fact]
    public void WindowIcon_contains_every_size_from_16_to_256()
    {
        var ico = WindowIconRenderer.RenderIco(new TrayIconSpec(true, false));
        using var stream = new MemoryStream(ico);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        Assert.Equal(WindowIconRenderer.Sizes, decoder.Frames.Select(f => f.PixelWidth).Order());
        Assert.All(decoder.Frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));

        var (frozen, width, frames) = WpfTestHost.Run(() =>
        {
            var frame = WindowIconRenderer.Render(new TrayIconSpec(false, false));
            return (frame.IsFrozen, frame.PixelWidth, frame.Decoder.Frames.Count);
        });
        Assert.True(frozen);
        Assert.Equal(256, width);
        Assert.Equal(WindowIconRenderer.Sizes.Count, frames);
    }

    [Fact]
    public void WindowIcon_is_drawn_only_when_asked_and_once_per_state()
    {
        var source = new WindowIconSource();
        var changes = 0;
        source.Changed += (_, _) => changes++;

        // Ohne Darstellung (Setup): kein eigenes Symbol, nichts gezeichnet.
        Assert.Null(source.Icon);
        Assert.Equal(0, source.RenderCount);

        source.SetAppearance(IconPalette.Default, lightTaskbar: false);
        Assert.Equal(1, changes);
        Assert.Equal(0, source.RenderCount); // noch kein Fenster hat gefragt

        var first = WpfTestHost.Run(() => source.Icon);
        Assert.NotNull(first);
        Assert.Same(first, WpfTestHost.Run(() => source.Icon));
        Assert.Equal(1, source.RenderCount);

        // Gleicher Zustand: kein Ereignis, kein neues Bild.
        source.SetState(hdrOn: false, paused: false, gameRunning: false);
        Assert.Equal(1, changes);

        // Gegenprobe: HDR an meldet sich und zeichnet beim nächsten Abruf genau einmal neu.
        source.SetState(hdrOn: true, paused: false, gameRunning: false);
        Assert.Equal(2, changes);
        var second = WpfTestHost.Run(() => source.Icon);
        Assert.NotSame(first, second);
        Assert.Equal(2, source.RenderCount);
        Assert.True(source.Spec.HdrOn);
    }

    [Fact]
    public void WindowIcon_follows_the_palette_colors()
    {
        var cyber = IconPalette.From(PaletteEngine.Compute(TrayIconRendererTests.Preset("cyber"), true, true));
        var (warm, cool) = WpfTestHost.Run(() => (Pixel(WindowIconRenderer.Render(new TrayIconSpec(true, false))), Pixel(WindowIconRenderer.Render(new TrayIconSpec(true, false, Palette: cyber)))));

        Assert.True(warm.R > warm.B + 60, $"Abendrot: {warm}");
        Assert.True(cool.B > cool.R + 60, $"Cyber: {cool}");
    }

    [Fact]
    public void WindowIcon_reaches_open_windows_and_they_unsubscribe_when_closed()
    {
        var source = new WindowIconSource();
        WpfTestHost.Run(() =>
        {
            var before = new TestWindow(source);
            Assert.NotNull(before.Icon); // festes Programmsymbol, solange keine Darstellung da ist
            Assert.Equal(0, source.RenderCount);
            before.Close();

            source.SetAppearance(IconPalette.Default, lightTaskbar: false);
            var window = new TestWindow(source);
            var off = window.Icon;
            Assert.Same(source.Icon, off);

            source.SetState(hdrOn: true, paused: false, gameRunning: false);
            Assert.NotSame(off, window.Icon);
            Assert.Same(source.Icon, window.Icon);
            Assert.Equal(1, source.Subscribers);

            window.Close();
            Assert.Equal(0, source.Subscribers);
        });
    }

    [Fact]
    public void WindowIcon_and_tray_take_the_hdr_state_from_the_tray()
    {
        var source = new WindowIconSource();
        var host = new FakeTrayHost { State = new TrayState(HdrOn: true, HdrSupported: true, Paused: false, ActiveGames: ["Hades II"]) };
        var surface = new TraySurface();
        var service = new TrayService(host, new NotificationGate(() => false, new ShellUserNotificationState()), surfaceFactory: () => surface, iconSize: () => 16, windowIcon: source);

        WpfTestHost.Run(service.Initialize);

        Assert.True(source.Spec.HdrOn);
        Assert.True(source.Spec.GameRunning);
        Assert.False(source.Spec.Paused);
        WpfTestHost.Run(service.Dispose);
    }

    [Fact]
    public void TrayIcon_is_redrawn_on_a_palette_change_but_not_on_other_appearance_changes()
    {
        var appearance = new StaticAppearanceService();
        var surface = new TraySurface();
        var service = new TrayService(new FakeTrayHost(), new NotificationGate(() => false, new ShellUserNotificationState()), surfaceFactory: () => surface, iconSize: () => 16, appearance: appearance);
        WpfTestHost.Run(service.Initialize);
        Assert.Equal(1, surface.IconSets);
        var standard = WpfTestHost.Run(() => service.CurrentIconSpec);

        // Nur die Effektstufe: gleiche Farben, gleiche Spezifikation, nichts gezeichnet.
        WpfTestHost.Run(() => appearance.Update(s => s.EffectLevel = "Reduced"));
        Assert.Equal(1, surface.IconSets);

        // Andere Vorlage: neues Symbol in den neuen Farben.
        WpfTestHost.Run(() => appearance.Update(s => s.Preset = "cyber"));
        Assert.Equal(2, surface.IconSets);
        var cyber = WpfTestHost.Run(() => service.CurrentIconSpec);
        Assert.NotEqual(standard, cyber);
        Assert.Equal(PalettePresets.Find("cyber")!.Colors.Accent, cyber!.Value.Colors.Accent);

        // Nur das App-Thema: Das Symbol steht auf der Taskleiste und behält seine Farben.
        WpfTestHost.Run(() => appearance.Update(s => s.Theme = "Light"));
        Assert.Equal(2, surface.IconSets);

        WpfTestHost.Run(service.Dispose);
        WpfTestHost.Run(() => appearance.Update(s => s.Preset = "glut"));
        Assert.Equal(2, surface.IconSets); // nach dem Beenden abgemeldet
    }

    private static System.Windows.Media.Color Pixel(BitmapFrame frame)
    {
        var small = frame.Decoder.Frames.Single(f => f.PixelWidth == 32);
        var pixels = new byte[4];
        small.CopyPixels(new System.Windows.Int32Rect(6, 9, 1, 1), pixels, 4, 0);
        return System.Windows.Media.Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    private sealed class TestWindow(WindowIconSource source) : ChromeWindow(source);

    /// <summary>Symbol im Infobereich ohne echtes NotifyIcon.</summary>
    private sealed class TraySurface : ITrayIconSurface
    {
        private System.Drawing.Icon? _icon;

        public event EventHandler? RightClick
        {
            add { }
            remove { }
        }

        public event EventHandler? DoubleClick
        {
            add { }
            remove { }
        }

        public event EventHandler? BalloonClicked
        {
            add { }
            remove { }
        }

        public int IconSets { get; private set; }

        public System.Drawing.Icon? Icon
        {
            get => _icon;
            set
            {
                _ = value?.Handle;
                _icon = value;
                IconSets++;
            }
        }

        public string Text { get; set; } = string.Empty;

        public bool Visible { get; set; }

        public void ShowBalloon(int timeoutMs, string title, string text, bool isWarning)
        {
        }

        public void Dispose()
        {
        }
    }
}
