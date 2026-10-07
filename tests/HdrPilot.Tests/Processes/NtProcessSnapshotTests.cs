using System.Diagnostics;
using System.Runtime.InteropServices;
using HdrPilot.Core.Native;
using HdrPilot.Core.Processes;
using HdrPilot.Tests.Hdr;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Processes;

[Collection(ProcessHandleCollection.Name)]
public sealed class NtProcessSnapshotTests
{
    [Theory]
    [InlineData(NtNative.SystemBasicProcessInformation, "SystemBasicProcessInformation")]
    [InlineData(NtNative.SystemProcessInformation, "SystemProcessInformation")]
    public void Finds_own_process_with_path_for_both_info_classes(int infoClass, string expectedMethod)
    {
        using var provider = new NtProcessSnapshotProvider(null, infoClass);
        var all = provider.GetProcesses();

        Assert.Equal(expectedMethod, provider.Method);
        var me = Assert.Single(all, p => p.ProcessId == Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, me.ExePath, ignoreCase: true);
        Assert.Equal(Path.GetFileName(Environment.ProcessPath), me.ExeName, ignoreCase: true);
        Assert.True(all.Count > 20);
        Assert.Contains(all, p => p.ExePath is null); // geschützte Prozesse ohne lesbaren Pfad
        Assert.DoesNotContain(all, p => p.ProcessId == 0);
    }

    [Fact]
    public void Same_process_set_as_toolhelp()
    {
        using var nt = new NtProcessSnapshotProvider();
        var toolhelp = new Win32ProcessSnapshotProvider().GetProcesses().Select(p => p.ProcessId).ToHashSet();
        var basic = nt.GetProcesses().Select(p => p.ProcessId).ToHashSet();

        // Zwischen beiden Abfragen können einzelne Prozesse kommen und gehen.
        var difference = toolhelp.Except(basic).Count() + basic.Except(toolhelp).Count();
        Assert.True(difference <= 5, $"Unterschied {difference} Prozesse");
    }

    [Fact]
    public void Detects_new_and_exited_process_and_queries_path_only_once()
    {
        using var provider = new NtProcessSnapshotProvider();
        provider.GetProcesses();
        var before = provider.PathQueries;

        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        var withChild = provider.GetProcesses();
        var entry = Assert.Single(withChild, p => p.ProcessId == child.Id);
        Assert.Equal("cmd.exe", entry.ExeName, ignoreCase: true);
        Assert.EndsWith(@"\cmd.exe", entry.ExePath, StringComparison.OrdinalIgnoreCase);

        provider.GetProcesses();
        provider.GetProcesses();
        Assert.True(provider.PathQueries - before < 15, $"Pfadabfragen: {provider.PathQueries - before}");

        child.Kill(entireProcessTree: true);
        child.WaitForExit();
        Thread.Sleep(100);
        Assert.DoesNotContain(provider.GetProcesses(), p => p.ProcessId == child.Id);
    }

    [Fact]
    public void Does_not_leak_handles_or_memory()
    {
        using var provider = new NtProcessSnapshotProvider();
        provider.GetProcesses();
        using var me = Process.GetCurrentProcess();
        me.Refresh();
        var handles = me.HandleCount;
        for (var i = 0; i < 500; i++)
        {
            provider.GetProcesses();
        }

        me.Refresh();
        Assert.True(me.HandleCount - handles < 20, $"Handles {handles} -> {me.HandleCount}");
    }

    [Fact]
    public void Basic_process_information_is_much_cheaper_than_toolhelp()
    {
        using var nt = new NtProcessSnapshotProvider();
        var toolhelp = new Win32ProcessSnapshotProvider();
        nt.GetProcesses();
        toolhelp.GetProcesses();

        var swNt = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            nt.GetProcesses();
        }

        swNt.Stop();
        var swTh = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            toolhelp.GetProcesses();
        }

        swTh.Stop();
        Assert.True(swNt.Elapsed * 5 < swTh.Elapsed, $"Nt {swNt.Elapsed.TotalMilliseconds:0.0} ms, Toolhelp {swTh.Elapsed.TotalMilliseconds:0.0} ms");
    }

    [Fact]
    public void Structures_match_winternl_header()
    {
        Assert.Equal(48, Marshal.SizeOf<NtNative.SYSTEM_BASICPROCESS_INFORMATION>());
        var path = HeaderFiles.Find("winternl.h");
        Assert.SkipWhen(path is null, "winternl.h nicht gefunden (.sdk\\).");
        var header = CHeaderLayout.Parse(File.ReadAllText(path!));

        var basic = header.Layout("SYSTEM_BASICPROCESS_INFORMATION");
        Assert.Equal(basic.Size, Marshal.SizeOf<NtNative.SYSTEM_BASICPROCESS_INFORMATION>());
        foreach (var slot in basic.Slots)
        {
            Assert.Equal(slot.Offset, (int)Marshal.OffsetOf<NtNative.SYSTEM_BASICPROCESS_INFORMATION>(slot.Names[0]));
        }

        var full = header.Layout("SYSTEM_PROCESS_INFORMATION");
        foreach (var name in new[] { "NextEntryOffset", "NumberOfThreads", "ImageName", "BasePriority", "UniqueProcessId" })
        {
            var slot = full.Slots.Single(s => s.Names.Contains(name));
            Assert.Equal(slot.Offset, (int)Marshal.OffsetOf<NtNative.SYSTEM_PROCESS_INFORMATION_HEAD>(name));
        }

        Assert.Equal(NtNative.SystemProcessInformation, (int)header.Constant("SystemProcessInformation"));
        Assert.Equal(NtNative.SystemBasicProcessInformation, (int)header.Constant("SystemBasicProcessInformation"));
    }
}
