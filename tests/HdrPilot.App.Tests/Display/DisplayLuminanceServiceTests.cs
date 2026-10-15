using HdrPilot.App.Services.Display;
using HdrPilot.Core.Hdr;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Display;

/// <summary>
/// Helligkeitswerte der Bildschirme (E89, G10.7) mit einer Attrappe statt DXGI: gelesen nur mit offenem Fenster und nur nach
/// einem HDR- oder Bildschirmwechsel, Zuordnung über den GDI-Namen, die Spitze mit HDR wird gemerkt und ist das Maximum der
/// Nit-Skala, auch wenn HDR gerade aus ist.
/// </summary>
public sealed class DisplayLuminanceServiceTests
{
    private const string Mag = @"\\?\DISPLAY#MSI3CB1#5&1a&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string Other = @"\\?\DISPLAY#DEL41A8#5&1a&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string File = @"C:\Data\display-luminance.json";

    private readonly FakeReader _reader = new();
    private readonly InMemoryFileSystem _fs = new();
    private readonly List<DisplayInfo> _displays = [Display(Mag, @"\\.\DISPLAY1", "MAG 341C OLED", primary: true, hdr: false), Display(Other, @"\\.\DISPLAY2", "DELL U2720Q", primary: false, hdr: false)];
    private List<string> _targets = [];

    [Fact]
    public async Task Reads_only_while_a_window_shows_the_values_and_after_invalidate()
    {
        _reader.Values = [new DisplayLuminance(@"\\.\DISPLAY1", 456, 254, 0, 10, true)];
        var service = Service();

        service.Invalidate();
        await service.Idle;
        Assert.Equal(0, service.ReadCount); // ohne Fenster nie

        var lease = service.Acquire();
        await service.Idle;
        Assert.Equal(1, service.ReadCount);
        var second = service.Acquire();
        await service.Idle;
        Assert.Equal(1, service.ReadCount); // zweites Fenster: kein neues Lesen ohne Wechsel

        service.Invalidate();
        await service.Idle;
        Assert.Equal(2, service.ReadCount);

        second.Dispose();
        lease.Dispose();
        service.Invalidate();
        await service.Idle;
        Assert.Equal(2, service.ReadCount);

        // Wechsel während das Fenster zu war: beim nächsten Öffnen einmal lesen.
        using var again = service.Acquire();
        await service.Idle;
        Assert.Equal(3, service.ReadCount);
    }

    [Fact]
    public async Task Peak_with_hdr_is_remembered_and_stays_the_scale_maximum_when_hdr_is_off()
    {
        var service = Service();
        _reader.Values = [new DisplayLuminance(@"\\.\DISPLAY1", 456, 254, 0, 10, HdrActive: true)];
        using (service.Acquire())
        {
            await service.Idle;
        }

        // HDR aus: DXGI meldet jetzt SDR-Werte, die Skala bleibt bei der HDR-Spitze.
        _reader.Values = [new DisplayLuminance(@"\\.\DISPLAY1", 270, 270, 0.5, 8, HdrActive: false)];
        var fresh = Service();
        using var lease = fresh.Acquire();
        await fresh.Idle;

        var primary = fresh.Primary!;
        Assert.Equal(456, primary.PeakNits);
        Assert.Equal(254, primary.FullFrameNits);
        Assert.Equal(10, primary.BitsPerColor);
        Assert.False(primary.HdrActive);
        Assert.True(primary.PeakMeasuredWithHdr);
        Assert.Equal(456, NitScale.Maximum(primary));
        Assert.Equal("MAG 341C OLED", primary.FriendlyName);
        Assert.Contains("\"peakNits\": 456", _fs.ReadAllText(File), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_hdr_measurement_the_current_peak_is_flagged()
    {
        _reader.Values = [new DisplayLuminance(@"\\.\DISPLAY1", 270, 270, 0.5, 8, HdrActive: false)];
        var service = Service();
        using var lease = service.Acquire();
        await service.Idle;

        Assert.Equal(270, service.Primary!.PeakNits);
        Assert.False(service.Primary.PeakMeasuredWithHdr);
        Assert.False(_fs.FileExists(File)); // nichts gemerkt ohne HDR
    }

    [Fact]
    public void Readings_map_by_gdi_name_and_unknown_outputs_are_left_out()
    {
        IReadOnlyList<DisplayLuminance> readings =
        [
            new(@"\\.\DISPLAY2", 400, 350, 0.3, 10, false),
            new(@"\\.\display1", 456, 254, 0, 10, true),
            new(@"\\.\DISPLAY9", 1000, 600, 0, 10, true),
        ];

        var nits = DisplayLuminanceService.Combine(readings, _displays, store: null, DateTimeOffset.UtcNow);

        Assert.Equal([Mag, Other], nits.Select(n => n.MonitorDevicePath));
        Assert.Equal([456.0, 400.0], nits.Select(n => n.PeakNits));
        Assert.Empty(DisplayLuminanceService.Combine(readings, [Display(Mag, null, "x", true, false)], null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Primary_is_the_configured_target_display_else_the_main_display()
    {
        _reader.Values = [new DisplayLuminance(@"\\.\DISPLAY1", 456, 254, 0, 10, true), new DisplayLuminance(@"\\.\DISPLAY2", 400, 350, 0.3, 10, false)];
        var service = Service();
        using (service.Acquire())
        {
            await service.Idle;
            Assert.Equal(Mag, service.Primary?.MonitorDevicePath);
            Assert.Equal(2, service.Displays.Count);
        }

        _targets = [Other];
        var targeted = Service();
        using var lease = targeted.Acquire();
        await targeted.Idle;
        Assert.Equal(Other, targeted.Primary?.MonitorDevicePath);
    }

    [Fact]
    public async Task Changed_is_raised_only_when_values_change()
    {
        _reader.Values = [new DisplayLuminance(@"\\.\DISPLAY1", 456, 254, 0, 10, true)];
        var service = Service();
        var changes = 0;
        service.Changed += (_, _) => Interlocked.Increment(ref changes);
        using var lease = service.Acquire();
        await service.Idle;

        service.Invalidate();
        await service.Idle;
        Assert.Equal(1, changes);

        _displays[0] = _displays[0] with { HdrEnabled = true };
        _reader.Values = [new DisplayLuminance(@"\\.\DISPLAY1", 456, 254, 0, 10, false)];
        service.Invalidate();
        await service.Idle;
        Assert.Equal(1, changes); // gleiche Werte: HDR galt laut Windows schon vorher

        _displays[0] = _displays[0] with { HdrEnabled = false };
        service.Invalidate();
        await service.Idle;
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Corrupt_store_starts_empty_and_remembers_changes_only()
    {
        _fs.AddFile(File, "{ kaputt");
        var store = new DisplayLuminanceStore(_fs, File);

        Assert.Null(store.Get(Mag));
        Assert.True(store.Remember(Mag, 456, 254, 0, 10, DateTimeOffset.UtcNow));
        Assert.False(store.Remember(Mag, 456, 254, 0, 10, DateTimeOffset.UtcNow));
        Assert.Equal(456, new DisplayLuminanceStore(_fs, File).Get(Mag)?.PeakNits);
    }

    private static DisplayInfo Display(string path, string? gdi, string name, bool primary, bool hdr) => new()
    {
        MonitorDevicePath = path,
        FriendlyName = name,
        GdiDeviceName = gdi,
        IsPrimary = primary,
        HdrSupported = true,
        HdrEnabled = hdr,
        Api = HdrApi.Modern,
    };

    private DisplayLuminanceService Service() => new(new DisplayLuminanceServiceOptions
    {
        Reader = _reader,
        Displays = () => _displays.ToList(),
        TargetDisplays = () => _targets,
        Store = new DisplayLuminanceStore(_fs, File),
    });

    private sealed class FakeReader : IDisplayLuminanceReader
    {
        public IReadOnlyList<DisplayLuminance> Values { get; set; } = [];

        public IReadOnlyList<DisplayLuminance> ReadAll() => Values;
    }
}
