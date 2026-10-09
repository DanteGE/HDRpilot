using HdrPilot.App.Setup;
using HdrPilot.Core.Processes;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>
/// Echte App-Steuerung ohne Prozess-Handle: erkennt laufende Prozesse über ihren EXE-Pfad aus der Prozessliste. Startet
/// und beendet nichts (Einzelinstanz-Kennung und EXE-Pfade sind erfunden oder gehören laufenden Windows-Prozessen).
/// </summary>
public sealed class SetupAppControlTests
{
    private static string UnusedInstance() => "setup-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Running_process_is_found_by_its_exe_path_without_a_handle()
    {
        // svchost.exe läuft unter Windows immer (mehrfach); der eigene Prozess zählt nie.
        var svchost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "svchost.exe");

        Assert.True(new SetupAppControl(UnusedInstance(), svchost.ToUpperInvariant()).IsRunning());
    }

    [Fact]
    public void Nothing_running_from_the_exe_counts_as_stopped_without_sending_anything()
    {
        var control = new SetupAppControl(UnusedInstance(), Path.Combine(Path.GetTempPath(), "HDRpilot.Tests." + Guid.NewGuid().ToString("N"), "HDRpilot.exe"));

        Assert.False(control.IsRunning());
        Assert.True(control.Stop(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Own_process_never_counts_as_the_installed_app_but_any_other_process_from_the_exe_does()
    {
        const string Exe = @"C:\Users\U\AppData\Local\Programs\HDRpilot\HDRpilot.exe";
        var own = new ProcessInfo(Environment.ProcessId, "HDRpilot.exe", Exe);
        var other = new ProcessInfo(Environment.ProcessId + 1, "HDRpilot.exe", Exe.ToLowerInvariant());
        var portable = new ProcessInfo(Environment.ProcessId + 2, "HDRpilot.exe", @"D:\Portable\HDRpilot.exe");
        var unreadable = new ProcessInfo(Environment.ProcessId + 3, "HDRpilot.exe", null);

        Assert.False(new SetupAppControl(UnusedInstance(), Exe, () => new FixedSnapshot(own, portable, unreadable)).IsRunning());
        Assert.True(new SetupAppControl(UnusedInstance(), Exe, () => new FixedSnapshot(own, other)).IsRunning());
    }

    [Fact]
    public void Process_that_keeps_running_without_an_instance_is_reported_after_the_timeout()
    {
        const string Exe = @"C:\Users\U\AppData\Local\Programs\HDRpilot\HDRpilot.exe";
        var hung = new ProcessInfo(Environment.ProcessId + 1, "HDRpilot.exe", Exe);
        var control = new SetupAppControl(UnusedInstance(), Exe, () => new FixedSnapshot(hung));

        Assert.False(control.Stop(TimeSpan.FromMilliseconds(300)));
    }

    private sealed class FixedSnapshot(params ProcessInfo[] processes) : IProcessSnapshotProvider
    {
        public IReadOnlyList<ProcessInfo> GetProcesses() => processes;
    }

    [Fact]
    public void Default_setup_version_is_the_file_version_of_the_app()
    {
        var app = typeof(SetupEngine).Assembly;

        Assert.Equal(SetupSupport.Normalize(app.GetName().Version!), SetupSupport.ReadFileVersion(app.Location));
    }

    [Theory]
    [InlineData("\"C:\\Apps\\HDRpilot\\HDRpilot.exe\" --minimized", true)]
    [InlineData("\"c:\\apps\\hdrpilot\\HDRPILOT.EXE\"", true)]
    [InlineData("C:\\Apps\\HDRpilot\\HDRpilot.exe --minimized", true)]
    [InlineData("\"D:\\Other\\HDRpilot.exe\" --minimized", false)]
    [InlineData("\"C:\\Apps\\HDRpilot\\HDRpilot.exe.old\"", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Autostart_command_points_to_the_exe_only_for_the_same_path(string? command, bool expected) =>
        Assert.Equal(expected, SetupSupport.PointsTo(command, @"C:\Apps\HDRpilot\HDRpilot.exe"));
}
