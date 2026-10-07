using System.Diagnostics;
using System.Runtime.InteropServices;
using HdrPilot.Core.Native;
using HdrPilot.Core.Processes;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using DeviceMapping = HdrPilot.Core.Processes.ProcessImagePathResolver.DeviceMapping;

namespace HdrPilot.Tests.Processes;

/// <summary>EXE-Pfade ohne Handle auf fremde Prozesse (E65).</summary>
[Collection(ProcessHandleCollection.Name)]
public sealed class ProcessImagePathResolverTests
{
    private static readonly DeviceMapping[] Map =
    [
        new(@"\Device\HarddiskVolume1", "C:"),
        new(@"\Device\HarddiskVolume10", "D:"),
        new(@"\Device\Mup\nas\spiele", "Z:"),
    ];

    [Theory]
    [InlineData(@"\Device\HarddiskVolume1\Windows\explorer.exe", @"C:\Windows\explorer.exe")]
    [InlineData(@"\Device\HarddiskVolume10\Games\Spiel\Spiel.exe", @"D:\Games\Spiel\Spiel.exe")]
    [InlineData(@"\device\harddiskvolume1\Windows\notepad.exe", @"C:\Windows\notepad.exe")]
    [InlineData(@"\Device\HarddiskVolume1", @"C:\")]
    [InlineData(@"\Device\Mup\nas\spiele\Retro\Spiel.exe", @"Z:\Retro\Spiel.exe")]
    [InlineData(@"\Device\Mup\server\freigabe\Spiel.exe", @"\\server\freigabe\Spiel.exe")]
    public void Nt_paths_become_win32_paths(string ntPath, string expected)
    {
        Assert.Equal(expected, ProcessImagePathResolver.ToDosPath(ntPath, Map));
    }

    [Theory]
    [InlineData(@"\Device\HarddiskVolume2\Windows\explorer.exe")] // kein Laufwerksbuchstabe
    [InlineData(@"\Device\HarddiskVolume100\x.exe")] // nicht HarddiskVolume10 oder 1
    [InlineData("Registry")]
    [InlineData("MemCompression")]
    [InlineData("")]
    public void Unknown_or_pseudo_paths_give_null(string ntPath)
    {
        Assert.Null(ProcessImagePathResolver.ToDosPath(ntPath, Map));
    }

    [Theory]
    [InlineData("C:", @"\Device\HarddiskVolume3", @"\Device\HarddiskVolume3", "C:")]
    [InlineData("Z:", @"\Device\LanmanRedirector\;Z:0000000000012345\nas\spiele", @"\Device\Mup\nas\spiele", "Z:")]
    [InlineData("X:", @"\??\C:\Daten\Spiele", null, null)] // SUBST: keine eigene Zuordnung
    public void Dos_device_targets_are_parsed(string drive, string target, string? device, string? expectedDrive)
    {
        var mapping = ProcessImagePathResolver.ParseDosDeviceTarget(drive, target);
        if (device is null)
        {
            Assert.Null(mapping);
            return;
        }

        Assert.Equal(new DeviceMapping(device, expectedDrive!), mapping);
    }

    [Fact]
    public void Device_map_is_read_once_and_refreshed_only_after_interval_for_unknown_devices()
    {
        var clock = new FakeClock();
        var reads = 0;
        var paths = new Dictionary<int, string>
        {
            [10] = @"\Device\HarddiskVolume1\Spiel.exe",
            [11] = @"\Device\HarddiskVolume7\Neu.exe",
        };
        var resolver = new ProcessImagePathResolver(
            null,
            () =>
            {
                reads++;
                return reads == 1 ? [Map[0]] : [Map[0], new DeviceMapping(@"\Device\HarddiskVolume7", "F:")];
            },
            (int pid, out string? nt) =>
            {
                nt = paths[pid];
                return NtNative.STATUS_SUCCESS;
            },
            clock);

        Assert.Equal(@"C:\Spiel.exe", resolver.Resolve(10));
        Assert.Null(resolver.Resolve(11)); // Laufwerk unbekannt, Zuordnung eben erst gelesen
        Assert.Equal(1, reads);

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(@"F:\Neu.exe", resolver.Resolve(11)); // neu gelesen
        Assert.Equal(2, reads);
        Assert.Equal(@"C:\Spiel.exe", resolver.Resolve(10));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Failed_query_gives_null_without_handle_fallback()
    {
        var resolver = new ProcessImagePathResolver(
            null,
            () => Map,
            (int _, out string? nt) =>
            {
                nt = null;
                return 0xC000000B; // STATUS_INVALID_CID: Prozess schon beendet
            },
            new FakeClock());

        Assert.Null(resolver.Resolve(1234));
        Assert.False(resolver.UsesHandleFallback);
    }

    [Fact]
    public void Missing_info_class_switches_to_handle_fallback_once()
    {
        var log = new ListLogger<ProcessImagePathResolver>();
        var calls = 0;
        var resolver = new ProcessImagePathResolver(
            log,
            () => Map,
            (int _, out string? nt) =>
            {
                calls++;
                nt = null;
                return NtNative.STATUS_INVALID_INFO_CLASS;
            },
            new FakeClock());

        // Der Rückfall liest den eigenen Prozess über ein kurzes Handle.
        Assert.Equal(Environment.ProcessPath, resolver.Resolve(Environment.ProcessId), ignoreCase: true);
        Assert.True(resolver.UsesHandleFallback);
        Assert.Equal(Environment.ProcessPath, resolver.Resolve(Environment.ProcessId), ignoreCase: true);
        Assert.Equal(1, calls);
        Assert.True(log.Contains(LogLevel.Warning, "SystemProcessIdInformation"));
    }

    [Fact]
    public void Structure_layout_matches_documented_x64_layout()
    {
        // SYSTEM_PROCESS_ID_INFORMATION { HANDLE ProcessId; UNICODE_STRING ImageName; } (Chappell, phnt): 24 Byte.
        Assert.Equal(24, Marshal.SizeOf<NtNative.SYSTEM_PROCESS_ID_INFORMATION>());
        Assert.Equal(8, (int)Marshal.OffsetOf<NtNative.SYSTEM_PROCESS_ID_INFORMATION>(nameof(NtNative.SYSTEM_PROCESS_ID_INFORMATION.ImageName)));
        Assert.Equal(88, NtNative.SystemProcessIdInformation);
    }

    [Fact]
    public void Real_query_finds_own_path_without_fallback()
    {
        var resolver = new ProcessImagePathResolver();

        Assert.Equal(Environment.ProcessPath, resolver.Resolve(Environment.ProcessId), ignoreCase: true);
        Assert.False(resolver.UsesHandleFallback);
        Assert.Null(resolver.Resolve(0));
    }

    [Fact]
    public void Real_query_reads_paths_of_processes_that_cannot_be_opened()
    {
        // services.exe läuft als SYSTEM in Sitzung 0: ohne Adminrechte verweigert OpenProcess den Zugriff,
        // die Abfrage ohne Handle liefert den Pfad trotzdem.
        // Mit Adminrechten gelingt OpenProcess; der Pfad muss dann trotzdem ohne Handle kommen (kein Überspringen,
        // das Test-Gate verlangt null übersprungene Tests).
        var services = Process.GetProcessesByName("services").FirstOrDefault();
        Assert.NotNull(services);
        var pid = (uint)services.Id;
        var resolver = new ProcessImagePathResolver();

        var path = resolver.Resolve((int)pid);

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "services.exe"), path, ignoreCase: true);
        Assert.False(resolver.UsesHandleFallback);
        if (!IsElevated())
        {
            var handle = ToolhelpNative.OpenProcess(ToolhelpNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle != IntPtr.Zero)
            {
                ToolhelpNative.CloseHandle(handle);
            }

            Assert.Equal(IntPtr.Zero, handle);
        }
    }

    [Fact]
    public void Folder_mounted_volume_maps_to_its_mount_folder()
    {
        // Spiele-SSD ohne Laufwerksbuchstaben, eingebunden unter C:\Games.
        DeviceMapping[] map = [new(@"\Device\HarddiskVolume1", "C:"), new(@"\Device\HarddiskVolume5", @"C:\Games")];

        Assert.Equal(@"C:\Games\SteamLibrary\steamapps\common\X\X.exe", ProcessImagePathResolver.ToDosPath(@"\Device\HarddiskVolume5\SteamLibrary\steamapps\common\X\X.exe", map));
        Assert.Equal(@"C:\Games\", ProcessImagePathResolver.ToDosPath(@"\Device\HarddiskVolume5", map));
    }

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    [Fact]
    public void Snapshot_providers_find_paths_of_protected_processes_now()
    {
        using var nt = new NtProcessSnapshotProvider();
        var all = nt.GetProcesses();
        var services = Assert.Single(all, p => p.ExeName.Equals("services.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "services.exe"), services.ExePath, ignoreCase: true);

        var toolhelp = new Win32ProcessSnapshotProvider().GetProcesses();
        var services2 = Assert.Single(toolhelp, p => p.ExeName.Equals("services.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(services.ExePath, services2.ExePath, ignoreCase: true);
    }
}
