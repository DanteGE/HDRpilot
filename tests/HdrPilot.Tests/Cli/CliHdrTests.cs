using System.Text.Json;
using HdrPilot.Cli;
using HdrPilot.Core.Hdr;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Cli;

public sealed class CliHdrTests
{
    private readonly FakeDisplayConfigApi _api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    private Task<int> Run(params string[] args)
    {
        var services = new TestServices(_api);
        return new CliApp(services, _out, _err).RunAsync(args, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Status_lists_name_capability_and_state()
    {
        var code = await Run("hdr", "status");
        Assert.Equal(ExitCodes.Ok, code);
        var text = _out.ToString();
        Assert.Contains("MAG 341C OLED", text, StringComparison.Ordinal);
        Assert.Contains("HDR-fähig: ja", text, StringComparison.Ordinal);
        Assert.Contains("HDR: aus", text, StringComparison.Ordinal);
        Assert.Contains("Hauptbildschirm", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_json_is_parseable()
    {
        _api.Monitors.Add(new FakeMonitor
        {
            Name = "SDR Monitor",
            DevicePath = @"\\?\DISPLAY#SDR#1",
            Target = new DisplayTarget(1, 0, 7),
            HdrSupported = false,
            WcgSupported = false,
        });

        var code = await Run("hdr", "status", "--json");

        Assert.Equal(ExitCodes.Ok, code);
        using var doc = JsonDocument.Parse(_out.ToString());
        var rows = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].GetProperty("hdrSupported").GetBoolean());
        Assert.False(rows[0].GetProperty("hdrEnabled").GetBoolean());
        Assert.False(rows[1].GetProperty("hdrSupported").GetBoolean());
        Assert.Equal("SDR Monitor", rows[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task On_then_off_switch_primary_display()
    {
        Assert.Equal(ExitCodes.Ok, await Run("hdr", "on"));
        Assert.True(_api.Monitors[0].HdrUserEnabled);
        Assert.Equal(ExitCodes.Ok, await Run("hdr", "off"));
        Assert.False(_api.Monitors[0].HdrUserEnabled);
        Assert.Equal(string.Empty, _err.ToString());
    }

    [Fact]
    public async Task Toggle_inverts_state()
    {
        Assert.Equal(ExitCodes.Ok, await Run("hdr", "toggle"));
        Assert.True(_api.Monitors[0].HdrUserEnabled);
        Assert.Equal(ExitCodes.Ok, await Run("hdr", "toggle"));
        Assert.False(_api.Monitors[0].HdrUserEnabled);
    }

    [Fact]
    public async Task Display_without_hdr_ends_with_own_exit_code_and_clear_message()
    {
        _api.Monitors[0].HdrSupported = false;

        var code = await Run("hdr", "on");

        Assert.Equal(ExitCodes.NotHdrCapable, code);
        Assert.Contains("unterstützt kein HDR", _err.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Unerwarteter Fehler", _err.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, _api.CallCount("Set"));
    }

    [Fact]
    public async Task Selecting_sdr_display_by_number_returns_not_capable()
    {
        _api.Monitors.Add(new FakeMonitor
        {
            Name = "Büro SDR",
            DevicePath = @"\\?\DISPLAY#SDR#1",
            Target = new DisplayTarget(1, 0, 7),
            HdrSupported = false,
            WcgSupported = false,
        });

        Assert.Equal(ExitCodes.NotHdrCapable, await Run("hdr", "on", "--display", "2"));
        Assert.Equal(ExitCodes.NotHdrCapable, await Run("hdr", "off", "--display", "Büro"));
    }

    [Fact]
    public async Task Unknown_display_returns_not_found()
    {
        Assert.Equal(ExitCodes.DisplayNotFound, await Run("hdr", "on", "--display", "9"));
        Assert.Equal(ExitCodes.DisplayNotFound, await Run("hdr", "on", "--display", "gibt es nicht"));
    }

    [Fact]
    public async Task Ambiguous_name_is_usage_error()
    {
        _api.Monitors.Add(new FakeMonitor { Name = "MAG 341C OLED", DevicePath = @"\\?\DISPLAY#MSI#2", Target = new DisplayTarget(1, 0, 8) });
        Assert.Equal(ExitCodes.Usage, await Run("hdr", "on", "--display", "MAG"));
    }

    [Fact]
    public async Task Failed_switch_returns_switch_failed()
    {
        _api.IgnoreSets = true;
        Assert.Equal(ExitCodes.SwitchFailed, await Run("hdr", "on"));
        Assert.Contains("ließ sich nicht einschalten", _err.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("hdr")]
    [InlineData("hdr", "blink")]
    [InlineData("hdr", "status", "--api", "foo")]
    [InlineData("hdr", "on", "--display")]
    [InlineData("hdr", "on", "--unknown", "x")]
    [InlineData("frobnicate")]
    public async Task Bad_usage_returns_2(params string[] args) => Assert.Equal(ExitCodes.Usage, await Run(args));

    [Fact]
    public async Task Help_returns_0() => Assert.Equal(ExitCodes.Ok, await Run("help"));

    [Fact]
    public async Task Exception_in_controller_is_caught()
    {
        var code = await new CliApp(new ThrowingServices(), _out, _err).RunAsync(["hdr", "status"], TestContext.Current.CancellationToken);
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("Unerwarteter Fehler", _err.ToString(), StringComparison.Ordinal);
    }

    private sealed class TestServices(FakeDisplayConfigApi api) : ICliServices
    {
        public IHdrController CreateHdrController(HdrControllerOptions options) =>
            new DisplayConfigHdrController(api, new FakeEnvironment(), new FakeClock(), options);
    }

    private sealed class ThrowingServices : ICliServices
    {
        public IHdrController CreateHdrController(HdrControllerOptions options) => throw new InvalidOperationException("kaputt");
    }
}
