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
    public void Prepare_and_ready_event_are_parsed_and_forwarded()
    {
        const string Event = @"Local\HdrPilot.Prepared.0123456789abcdef0123456789abcdef";
        var args = AppArguments.Parse(["--config-dir", @"C:\t", "--prepare", "Steam:1091500", "--ready-event", Event]);

        Assert.Equal("Steam:1091500", args.PrepareGameKey);
        Assert.Equal(Event, args.ReadyEvent);
        Assert.True(args.HasCommand);
        Assert.Equal(["--prepare", "Steam:1091500", "--ready-event", Event], args.ToForwardedArgs());
        Assert.Equal(args.ToForwardedArgs(), args.ToRunningInstance());

        var received = AppArguments.Parse(args.ToForwardedArgs()); // so kommt es bei der laufenden Instanz an
        Assert.Equal("Steam:1091500", received.PrepareGameKey);
        Assert.Equal(Event, received.ReadyEvent);
    }

    [Fact]
    public void Wrap_is_never_forwarded_and_the_game_command_is_never_read_as_commands()
    {
        var args = AppArguments.Parse(["--wrap", "--", @"D:\Spiel\game.exe", "--exit", "--run", "Steam:1", "--show", "--prepare", "Steam:2"]);

        Assert.True(args.Wrap);
        Assert.False(args.Exit);
        Assert.False(args.Show);
        Assert.Null(args.RunGameKey);
        Assert.Null(args.PrepareGameKey);
        Assert.False(args.HasCommand);
        Assert.Empty(args.Unknown);
        Assert.Empty(args.ToForwardedArgs());
        Assert.Equal(["--run", "Steam:1"], AppArguments.Parse(["--wrap", "--run", "Steam:1"]).ToForwardedArgs());
    }

    [Fact]
    public void Ready_event_alone_is_a_command_so_the_wrapper_gets_an_answer()
    {
        var args = AppArguments.Parse(["--ready-event", "X"]);

        Assert.True(args.HasCommand);
        Assert.Equal(["--ready-event", "X"], args.ToRunningInstance());
    }

    [Fact]
    public void Instance_check_of_the_wrapper_never_becomes_the_instance()
    {
        var id = "test-" + Guid.NewGuid().ToString("N")[..12]; // nie die echte Kennung "default"
        Assert.False(HdrPilot.App.Infrastructure.SingleInstance.IsRunning(id));
        Assert.False(HdrPilot.App.Infrastructure.SingleInstance.IsRunning(id)); // die Abfrage legt nichts an

        using (var instance = HdrPilot.App.Infrastructure.SingleInstance.TryAcquire(id))
        {
            Assert.NotNull(instance);
            Assert.True(HdrPilot.App.Infrastructure.SingleInstance.IsRunning(id));
        }

        Assert.False(HdrPilot.App.Infrastructure.SingleInstance.IsRunning(id));
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

    [Fact]
    public void AutoHdr_test_root_is_parsed_but_never_forwarded()
    {
        // Wie --config-dir eine Angabe für den Start dieser Instanz (E88), kein Befehl an eine laufende.
        var args = AppArguments.Parse(["--config-dir", @"C:\t", "--autohdr-test-root", "Phase9Ui", "--show"]);
        Assert.Equal("Phase9Ui", args.AutoHdrTestRoot);
        Assert.Equal(@"C:\t", args.ConfigDir);
        Assert.Empty(args.Unknown);
        Assert.Equal(["--show"], args.ToForwardedArgs());

        // Gegenprobe: ohne Wert ist es ein unbekannter Schalter, nach "--" gehört es zum Spielbefehl.
        Assert.Null(AppArguments.Parse(["--autohdr-test-root"]).AutoHdrTestRoot);
        Assert.Null(AppArguments.Parse(["--", "--autohdr-test-root", "x"]).AutoHdrTestRoot);
    }

    [Fact]
    public void Snapshot_options_are_parsed_but_never_forwarded()
    {
        // E89: Größen, Zustände und Themen der Bildschirmfotos gehören zum Bildschirmfoto-Lauf, nie zu einer laufenden Instanz.
        var args = AppArguments.Parse(["--snapshot", @"C:\shots", "--config-dir", @"C:\t", "--snapshot-sizes", "1280x800,3440x1440", "--snapshot-scenes", "hauptfenster", "--snapshot-themes", "hell", "--snapshot-display", "MAG 341C OLED;456;254;0;10"]);
        Assert.Equal(@"C:\shots", args.SnapshotDir);
        Assert.Equal("1280x800,3440x1440", args.SnapshotSizes);
        Assert.Equal("hauptfenster", args.SnapshotScenes);
        Assert.Equal("hell", args.SnapshotThemes);
        Assert.Equal("MAG 341C OLED;456;254;0;10", args.SnapshotDisplay);
        Assert.Empty(args.Unknown);
        Assert.False(args.HasCommand);
        Assert.Empty(args.ToForwardedArgs());

        // Gegenprobe: ohne Wert unbekannt, nach "--" Spielbefehl.
        Assert.Null(AppArguments.Parse(["--snapshot-sizes"]).SnapshotSizes);
        Assert.Null(AppArguments.Parse(["--", "--snapshot-sizes", "1280x800"]).SnapshotSizes);
    }

    [Fact]
    public void Artwork_report_is_parsed_but_never_forwarded()
    {
        var args = AppArguments.Parse(["--config-dir", @"C:\t", "--artwork-report", @"C:\out\bilder.json"]);
        Assert.Equal(@"C:\out\bilder.json", args.ArtworkReport);
        Assert.False(args.HasCommand);
        Assert.Empty(args.ToForwardedArgs());
        Assert.Null(AppArguments.Parse(["--artwork-report"]).ArtworkReport);
    }

    [Fact]
    public void Test_hdr_moment_is_a_command_and_forwarded_to_the_test_instance()
    {
        // G10.9: das Messskript lässt die laufende Testinstanz den HDR-Moment spielen, ohne HDR zu schalten.
        var args = AppArguments.Parse(["--config-dir", @"C:\t", "--test-hdr-moment"]);
        Assert.True(args.TestHdrMoment);
        Assert.True(args.HasCommand);
        Assert.Equal(["--test-hdr-moment"], args.ToForwardedArgs());
        Assert.True(AppArguments.Parse(args.ToForwardedArgs()).TestHdrMoment);

        // Gegenprobe: nach "--" gehört es zum Spielbefehl.
        Assert.False(AppArguments.Parse(["--", "--test-hdr-moment"]).TestHdrMoment);
    }

    [Fact]
    public void Test_toolbar_search_is_a_command_and_forwarded_to_the_test_instance()
    {
        // G10.5: phase10-ui.ps1 lässt die laufende Testinstanz Strg+F ausführen, ohne eine Taste zu senden.
        var args = AppArguments.Parse(["--config-dir", @"C:\t", "--test-toolbar-search"]);
        Assert.True(args.TestToolbarSearch);
        Assert.True(args.HasCommand);
        Assert.Equal(["--test-toolbar-search"], args.ToForwardedArgs());
        Assert.True(AppArguments.Parse(args.ToForwardedArgs()).TestToolbarSearch);
        Assert.False(AppArguments.Parse(["--", "--test-toolbar-search"]).TestToolbarSearch);
        Assert.False(AppArguments.Parse(["--test-hdr-moment"]).TestToolbarSearch);
    }

    [Theory]
    [InlineData(@"C:\Temp\hdrpilot-probe", true)]
    [InlineData(@"C:\Users\Test\AppData\Roaming\HDRpilot\Kopie", true)] // Unterordner: eigene config.json
    [InlineData(@"C:\Users\Test\AppData\Roaming\HDRpilot", false)] // die echte Konfiguration
    [InlineData(@"c:\users\test\appdata\roaming\hdrpilot\", false)] // dieselbe, anders geschrieben
    [InlineData(@"C:/Users/Test/AppData/Roaming/HDRpilot", false)]
    [InlineData(@"C:\Users\Test\AppData\Roaming\HDRpilot\Kopie\..", false)]
    [InlineData(@"C:\Users\Test\AppData\Local\HDRpilot", false)] // der echte Datenordner (Protokolle, Symbole, Bilder)
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Developer_modes_run_only_with_an_own_config_dir(string? configDir, bool own)
    {
        // E90, Prüfung: --settings-probe (wie --snapshot und --artwork-report) speichert die Konfiguration seiner Ablage; gegen
        // die echte Ablage oder den echten Datenordner beendet sich die App mit Exit-Code 2, statt sie zu verändern.
        var defaults = new HdrPilot.Core.Config.AppPaths(@"C:\Users\Test\AppData\Roaming\HDRpilot", @"C:\Users\Test\AppData\Local\HDRpilot");
        var args = AppArguments.Parse(configDir is null ? ["--settings-probe", @"C:\Temp\bericht.json"] : ["--settings-probe", @"C:\Temp\bericht.json", "--config-dir", configDir]);

        Assert.Equal(@"C:\Temp\bericht.json", args.SettingsProbe);
        Assert.Equal(own, args.HasOwnConfigDir(defaults));
    }
}