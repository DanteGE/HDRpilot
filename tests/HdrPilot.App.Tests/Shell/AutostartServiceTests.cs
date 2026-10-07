using HdrPilot.App.Services;
using HdrPilot.Core.Abstractions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Tests.Shell;

public sealed class AutostartServiceTests
{
    private const string Exe = @"C:\Apps\HdrPilot\HdrPilot.exe";
    private const string Moved = @"D:\Tools\HdrPilot\HdrPilot.exe";
    private const string Expected = "\"" + Exe + "\" --minimized";

    [Fact]
    public void Enable_writes_quoted_path_with_minimized_to_hkcu_run()
    {
        var registry = new FakeRegistry();
        var service = new AutostartService(registry, Exe);

        service.SetEnabled(true);

        Assert.Equal(Expected, registry.Get(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot"));
        Assert.True(service.IsEnabled);
        Assert.Equal(Expected, service.CurrentCommand);
    }

    [Fact]
    public void Disable_removes_only_the_hdrpilot_value()
    {
        var registry = new FakeRegistry()
            .Set(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot", Expected)
            .Set(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "Steam", "\"C:\\Steam\\steam.exe\" -silent");
        var service = new AutostartService(registry, Exe);

        service.SetEnabled(false);

        Assert.False(service.IsEnabled);
        Assert.Null(registry.Get(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot"));
        Assert.NotNull(registry.Get(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "Steam"));
    }

    [Fact]
    public void Disable_without_entry_does_not_create_the_run_key()
    {
        var registry = new FakeRegistry();
        var service = new AutostartService(registry, Exe);

        service.SetEnabled(false);

        Assert.Null(registry.OpenKey(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath));
    }

    [Fact]
    public void Reconcile_fixes_path_after_the_exe_was_moved()
    {
        var registry = new FakeRegistry().Set(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot", Expected);
        var logger = new ListLogger<AutostartService>();
        var service = new AutostartService(registry, Moved, logger);

        var result = service.Reconcile(startWithWindows: true);

        Assert.Equal(AutostartReconcileResult.PathUpdated, result);
        Assert.Equal("\"" + Moved + "\" --minimized", registry.Get(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot"));
        Assert.True(logger.Contains(LogLevel.Information, "korrigiert"));
    }

    [Fact]
    public void Reconcile_leaves_a_correct_entry_alone()
    {
        var registry = new FakeRegistry().Set(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot", Expected);
        var service = new AutostartService(registry, Exe);

        Assert.Equal(AutostartReconcileResult.Unchanged, service.Reconcile(startWithWindows: true));
        Assert.Equal(Expected, service.CurrentCommand);
    }

    [Fact]
    public void Reconcile_recreates_a_missing_entry_when_the_config_wants_autostart()
    {
        var registry = new FakeRegistry();
        var service = new AutostartService(registry, Exe);

        Assert.Equal(AutostartReconcileResult.Created, service.Reconcile(startWithWindows: true));
        Assert.Equal(Expected, service.CurrentCommand);
    }

    [Fact]
    public void Reconcile_never_touches_the_registry_when_autostart_is_off()
    {
        var registry = new FakeRegistry().Set(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot", "\"" + Moved + "\" --minimized");
        var service = new AutostartService(registry, Exe);

        Assert.Equal(AutostartReconcileResult.Unchanged, service.Reconcile(startWithWindows: false));
        Assert.Equal("\"" + Moved + "\" --minimized", service.CurrentCommand);
    }

    [Fact]
    public void Reconcile_with_autostart_off_does_not_create_an_entry()
    {
        var registry = new FakeRegistry();
        var service = new AutostartService(registry, Exe);

        Assert.Equal(AutostartReconcileResult.Unchanged, service.Reconcile(startWithWindows: false));
        Assert.Null(registry.OpenKey(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath));
    }

    [Fact]
    public void Reconcile_reports_registry_failures_without_throwing()
    {
        var inner = new FakeRegistry().Set(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot", Expected);
        var service = new AutostartService(new ReadOnlyRegistry(inner), Moved);

        Assert.Equal(AutostartReconcileResult.Failed, service.Reconcile(startWithWindows: true));
    }

    [Fact]
    public void Unknown_exe_path_cannot_enable_and_reconcile_does_nothing()
    {
        var service = new AutostartService(new FakeRegistry(), executablePath: null);

        Assert.Throws<InvalidOperationException>(() => service.SetEnabled(true));
        Assert.Equal(AutostartReconcileResult.Unchanged, service.Reconcile(startWithWindows: true));
    }

    [Theory]
    [InlineData(@"C:\A\HdrPilot.exe", "\"C:\\A\\HdrPilot.exe\" --minimized")]
    [InlineData("\"C:\\A B\\HdrPilot.exe\"", "\"C:\\A B\\HdrPilot.exe\" --minimized")]
    public void Command_line_is_quoted_once(string path, string expected) =>
        Assert.Equal(expected, AutostartService.BuildCommand(path));

    [Theory]
    [InlineData(@"C:\Apps\HdrPilot\HdrPilot.exe", @"C:\Apps\HdrPilot\HdrPilot.exe")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", null)]
    [InlineData(@"C:\Program Files\dotnet\DOTNET.EXE", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_the_hdrpilot_exe_may_go_into_the_run_key(string? processPath, string? expected) =>
        Assert.Equal(expected, AutostartService.ResolveExecutablePath(processPath));

    [Fact]
    public void Config_only_autostart_mirrors_the_config_and_never_writes_the_registry()
    {
        var config = ShellTestFactory.NewConfig(new InMemoryFileSystem(), c => c.Settings.StartWithWindows = true);
        var service = new ConfigOnlyAutostartService(config);

        Assert.True(service.IsEnabled);
        service.SetEnabled(false);
        Assert.True(service.IsEnabled);
    }
}
