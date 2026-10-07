using HdrPilot.App;

namespace HdrPilot.App.Tests;

public sealed class AppArgumentsTests
{
    [Fact]
    public void Run_key_and_flags_are_parsed_and_forwarded()
    {
        var args = AppArguments.Parse(["--run", "Steam:1091500", "--config-dir", @"C:\t", "--pause", "--unknown"]);
        Assert.Equal("Steam:1091500", args.RunGameKey);
        Assert.Equal(@"C:\t", args.ConfigDir);
        Assert.True(args.Pause);
        Assert.True(args.HasCommand);
        Assert.Equal(["--unknown"], args.Unknown);
        Assert.Equal(["--run", "Steam:1091500", "--pause"], args.ToForwardedArgs());
    }

    [Fact]
    public void No_command_means_start_only()
    {
        var args = AppArguments.Parse(["--minimized"]);
        Assert.False(args.HasCommand);
        Assert.True(args.Minimized);
        Assert.Empty(args.ToForwardedArgs());
    }

    [Fact]
    public void Second_start_opens_window_but_autostart_forwards_nothing()
    {
        Assert.Equal(["--show"], AppArguments.Parse([]).ToRunningInstance());
        Assert.Empty(AppArguments.Parse(["--minimized"]).ToRunningInstance());
        Assert.Equal(["--run", "Steam:1"], AppArguments.Parse(["--minimized", "--run", "Steam:1"]).ToRunningInstance());
    }

    [Fact]
    public void Instance_id_depends_on_config_dir()
    {
        Assert.Equal("default", HdrPilot.App.Infrastructure.SingleInstance.IdFor(null));
        Assert.Equal(
            HdrPilot.App.Infrastructure.SingleInstance.IdFor(@"C:\Test\A"),
            HdrPilot.App.Infrastructure.SingleInstance.IdFor(@"c:\test\a\"));
        Assert.NotEqual(
            HdrPilot.App.Infrastructure.SingleInstance.IdFor(@"C:\Test\A"),
            HdrPilot.App.Infrastructure.SingleInstance.IdFor(@"C:\Test\B"));
    }
}