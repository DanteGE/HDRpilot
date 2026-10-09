using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.Setup;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Setup.Engine;

public sealed class SetupLocationsTests
{
    private const string Root = @"C:\Temp\HDRpilot Setup Test";

    [Fact]
    public void Test_root_keeps_every_location_and_the_registry_branch_under_the_root()
    {
        var l = SetupLocations.ForTestRoot(Root);

        Assert.True(l.IsTest);
        Assert.Equal(Root + @"\Programs\HDRpilot", l.InstallDirectory);
        Assert.Equal(Root + @"\Programs\HDRpilot\HDRpilot.exe", l.ExePath);
        Assert.Equal(Root + @"\StartMenu\Programs\HDRpilot.lnk", l.StartMenuShortcut);
        Assert.Equal(Root + @"\StartMenu\Programs\HDRpilot", l.GameShortcutsDirectory);
        Assert.Equal(Root + @"\Desktop\HDRpilot.lnk", l.DesktopShortcut);
        Assert.Equal(Root + @"\Config", l.DataPaths.ConfigDirectory);
        Assert.Equal(Root + @"\Config", l.DataPaths.DataDirectory);
        Assert.Matches(@"^Software\\HDRpilot\.Tests\\Setup\\[0-9A-F]{16}$", l.RegistryRoot);
        Assert.Equal(l.RegistryRoot + @"\Uninstall\HDRpilot", l.UninstallKeyPath);
        Assert.Equal(l.RegistryRoot + @"\Run", l.RunKeyPath);
        Assert.Equal("--config-dir \"" + Root + "\\Config\"", l.ExtraLaunchArguments);
        Assert.Equal(SingleInstance.IdFor(Root + @"\Config"), l.InstanceId);
        Assert.NotEqual("default", l.InstanceId);
    }

    [Fact]
    public void Same_root_in_other_spelling_gives_the_same_branch_other_root_another()
    {
        var a = SetupLocations.ForTestRoot(Root);
        var b = SetupLocations.ForTestRoot(Root.ToUpperInvariant() + @"\");
        var c = SetupLocations.ForTestRoot(Root + "2");

        Assert.Equal(a.RegistryRoot, b.RegistryRoot);
        Assert.NotEqual(a.RegistryRoot, c.RegistryRoot);
        Assert.Equal(SetupSupport.MutexNameFor(a), SetupSupport.MutexNameFor(b));
        Assert.NotEqual(SetupSupport.MutexNameFor(a), SetupSupport.MutexNameFor(c));
        Assert.StartsWith(SetupSupport.MutexBaseName + ".", SetupSupport.MutexNameFor(a), StringComparison.Ordinal);
    }

    [Fact]
    public void Test_root_is_recovered_from_the_locations()
    {
        Assert.Equal(Root, SetupSupport.TestRootOf(SetupLocations.ForTestRoot(Root + @"\")));
        Assert.Null(SetupSupport.TestRootOf(DefaultLocations()));
    }

    [Fact]
    public void Default_locations_are_per_user_and_match_the_apps_autostart_key()
    {
        var l = DefaultLocations();

        Assert.False(l.IsTest);
        Assert.Equal(@"C:\Users\Test\AppData\Local\Programs\HDRpilot\HDRpilot.exe", l.ExePath);
        Assert.Equal(@"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\HDRpilot.lnk", l.StartMenuShortcut);
        Assert.Equal(@"C:\Users\Test\Desktop\HDRpilot.lnk", l.DesktopShortcut);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HDRpilot", l.UninstallKeyPath);
        Assert.Equal(AutostartService.RunKeyPath, l.RunKeyPath);
        Assert.Equal(@"C:\Users\Test\AppData\Roaming\HDRpilot", l.DataPaths.ConfigDirectory);
        Assert.Equal(@"C:\Users\Test\AppData\Local\HDRpilot", l.DataPaths.DataDirectory);
        Assert.Equal(string.Empty, l.ExtraLaunchArguments);
        Assert.Equal("default", l.InstanceId);
        Assert.Equal(@"Local\HDRpilot.Setup", SetupSupport.MutexNameFor(l)); // so in E83 festgehalten
    }

    private static SetupLocations DefaultLocations()
    {
        var environment = new FakeEnvironment();
        environment.Folders[Environment.SpecialFolder.DesktopDirectory] = @"C:\Users\Test\Desktop";
        return SetupLocations.Default(environment);
    }
}
