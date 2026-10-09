using HdrPilot.App.Setup;

namespace HdrPilot.App.Tests.Setup.Engine;

public sealed class SetupArgumentsTests
{
    [Fact]
    public void Without_arguments_everything_is_default()
    {
        var args = SetupArguments.Parse([]);

        Assert.False(args.Quiet);
        Assert.Null(args.Autostart);
        Assert.Null(args.DesktopShortcut);
        Assert.False(args.NoLaunch);
        Assert.False(args.AllowDowngrade);
        Assert.False(args.IsUninstall);
        Assert.Null(args.WaitPid);
        Assert.Null(args.TestRoot);
        Assert.Empty(args.Unknown);
    }

    [Fact]
    public void Setup_switches_are_parsed_case_insensitively_with_german_and_english_values()
    {
        var args = SetupArguments.Parse(["--QUIET", "--autostart", "aus", "--Desktop-Shortcut", "ON", "--no-launch", "--allow-downgrade", "--test-root", @"C:\Test Ablage"]);

        Assert.True(args.Quiet);
        Assert.False(args.Autostart);
        Assert.True(args.DesktopShortcut);
        Assert.True(args.NoLaunch);
        Assert.True(args.AllowDowngrade);
        Assert.Equal(@"C:\Test Ablage", args.TestRoot);
        Assert.False(args.IsUninstall);
        Assert.Empty(args.Unknown);
    }

    [Fact]
    public void Uninstall_switches_are_parsed()
    {
        var args = SetupArguments.Parse(["--uninstall", "--quiet", "--remove-data"]);

        Assert.True(args.Uninstall);
        Assert.True(args.IsUninstall);
        Assert.True(args.Quiet);
        Assert.True(args.RemoveData);
        Assert.False(args.UninstallContinue);
    }

    [Theory]
    [InlineData("1234", 1234)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("abc", null)]
    [InlineData("+7", null)]
    public void Wait_pid_accepts_only_positive_numbers(string value, int? expected)
    {
        var args = SetupArguments.Parse(["--uninstall-continue", "--wait-pid", value]);

        Assert.True(args.UninstallContinue);
        Assert.True(args.IsUninstall);
        Assert.Equal(expected, args.WaitPid);
        if (expected is null)
        {
            Assert.Equal(["--wait-pid", value], args.Unknown);
        }
    }

    [Fact]
    public void Invalid_or_missing_values_end_up_as_unknown()
    {
        var args = SetupArguments.Parse(["--autostart", "vielleicht", "--desktop-shortcut", "--test-root"]);

        Assert.Null(args.Autostart);
        Assert.Null(args.DesktopShortcut);
        Assert.Null(args.TestRoot);
        Assert.Equal(["--autostart", "vielleicht", "--desktop-shortcut", "--test-root"], args.Unknown);
    }

    [Fact]
    public void Continue_arguments_round_trip_quiet_remove_data_and_test_root()
    {
        var first = SetupArguments.Parse(["--uninstall", "--quiet", "--remove-data", "--test-root", @"D:\T"]);

        var continued = SetupArguments.Parse(first.ToContinueArguments(4711));

        Assert.True(continued.UninstallContinue);
        Assert.False(continued.Uninstall);
        Assert.Equal(4711, continued.WaitPid);
        Assert.True(continued.Quiet);
        Assert.True(continued.RemoveData);
        Assert.Equal(@"D:\T", continued.TestRoot);
        Assert.Empty(continued.Unknown);
        Assert.Equal(["--uninstall-continue", "--wait-pid", "1"], SetupArguments.Parse(["--uninstall"]).ToContinueArguments(1));
    }
}
