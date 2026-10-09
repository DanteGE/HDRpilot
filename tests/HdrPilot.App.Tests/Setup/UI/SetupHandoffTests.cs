using HdrPilot.App.Setup.UI;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>
/// Übergabe beim Entfernen: Kopie im Temp-Ordner, Warten ohne Handle und das Aufräumen am Ende. Gelöscht werden darf
/// nur ein Ordner HDRpilot-Uninstall-&lt;guid&gt; direkt im Temp-Ordner (hier: ein eigener Testordner als „Temp“).
/// </summary>
public sealed class SetupHandoffTests : IDisposable
{
    private const string Guid1 = "0f8fad5bd9cb469fa16570867728950e";

    private readonly SetupTestFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData(@"HDRpilot-Uninstall-" + Guid1 + @"\HDRpilot-Uninstall.exe", true)]
    [InlineData(@"hdrpilot-uninstall-" + Guid1 + @"\hdrpilot-uninstall.EXE", true)]
    [InlineData(@"HDRpilot-Uninstall-0f8fad5b-d9cb-469f-a165-70867728950e\HDRpilot-Uninstall.exe", true)]
    [InlineData(@"HDRpilot-Uninstall-" + Guid1 + @"\HDRpilot.exe", false)] // andere EXE
    [InlineData(@"HDRpilot-Uninstall-\HDRpilot-Uninstall.exe", false)] // ohne Kennung
    [InlineData(@"HDRpilot-Uninstall-foo\HDRpilot-Uninstall.exe", false)] // keine GUID
    [InlineData(@"Andere-" + Guid1 + @"\HDRpilot-Uninstall.exe", false)] // fremder Ordner
    [InlineData(@"Sub\HDRpilot-Uninstall-" + Guid1 + @"\HDRpilot-Uninstall.exe", false)] // nicht direkt im Temp-Ordner
    [InlineData(@"HDRpilot-Uninstall.exe", false)] // Temp-Ordner selbst
    [InlineData(@"HDRpilot-Uninstall-" + Guid1 + @"\..\HDRpilot-Uninstall.exe", false)] // „..“ führt hinaus
    [InlineData(@"HDRpilot-Uninstall-" + Guid1 + @"\x\..\..\..\HDRpilot-Uninstall-" + Guid1 + @"\HDRpilot-Uninstall.exe", false)]
    public void Self_removal_only_for_the_own_copy_directly_in_temp(string relative, bool allowed)
    {
        var temp = Path.Combine(_folder.Root, "Temp");
        var exe = Path.Combine(temp, relative);

        var folder = UninstallHandoff.RemovableTempFolder(exe, temp);

        Assert.Equal(allowed, folder is not null);
        if (folder is not null)
        {
            Assert.Equal(temp, Path.GetDirectoryName(folder), ignoreCase: true);
        }
    }

    [Fact]
    public void Self_removal_refuses_installed_exe_relative_paths_and_odd_characters()
    {
        var temp = Path.Combine(_folder.Root, "Temp");

        Assert.Null(UninstallHandoff.RemovableTempFolder(Path.Combine(_folder.Locations.InstallDirectory, "HDRpilot.exe"), temp));
        Assert.Null(UninstallHandoff.RemovableTempFolder(@"HDRpilot-Uninstall-" + Guid1 + @"\HDRpilot-Uninstall.exe", temp));
        Assert.Null(UninstallHandoff.RemovableTempFolder(null, temp));
        Assert.Null(UninstallHandoff.RemovableTempFolder(Path.Combine(temp, "HDRpilot-Uninstall-" + Guid1, "HDRpilot-Uninstall.exe"), null));
        Assert.Null(UninstallHandoff.RemovableTempFolder(Path.Combine(temp, "HDRpilot-Uninstall-" + Guid1, "HDRpilot-Uninstall.exe"), Path.Combine(_folder.Root, "Other")));

        // cmd.exe läse %…% auch in Anführungszeichen (und ! mit verzögerter Erweiterung): solche Temp-Pfade bleiben liegen.
        foreach (var name in new[] { "100%TEMP%", "Hallo!Welt", "Zitat\"" })
        {
            var odd = Path.Combine(_folder.Root, name);
            Assert.Null(UninstallHandoff.RemovableTempFolder(Path.Combine(odd, "HDRpilot-Uninstall-" + Guid1, "HDRpilot-Uninstall.exe"), odd));
            Assert.Null(UninstallHandoff.RemovableExtractionFolder(Path.Combine(odd, ".net", "HDRpilot-Uninstall"), odd));
        }
    }

    [Theory]
    [InlineData(@".net\HDRpilot-Uninstall", true)]
    [InlineData(@".net\HDRpilot-Uninstall\", true)]
    [InlineData(@".net\HDRpilot", false)] // Bibliotheken der App
    [InlineData(@".net\HdrPilot", false)] // dieselben, Ordner einer früheren Version
    [InlineData(@".net\HDRpilot-Setup-1.1.0", false)] // Bibliotheken eines Setups bleiben
    [InlineData(@".net\HDRpilot-Uninstall-Alt", false)] // Name nicht genau
    [InlineData(@".net\hdrpilot-uninstall", false)]
    [InlineData(@".net\HDRpilot-Uninstall\0f8fad5bd9cb", false)] // Unterordner statt des Ordners
    [InlineData(@".net", false)]
    [InlineData(@"Andere\HDRpilot-Uninstall", false)] // nicht unter .net
    [InlineData(@"Sub\.net\HDRpilot-Uninstall", false)] // .net nicht direkt im Temp-Ordner
    [InlineData(@".net\x\..\HDRpilot-Uninstall", true)] // „..“ aufgelöst: derselbe Ordner
    [InlineData(@".net\HDRpilot-Uninstall\..\HDRpilot", false)]
    public void Extraction_folder_is_only_exactly_hdrpilot_uninstall_under_dot_net_in_temp(string relative, bool allowed)
    {
        var temp = Path.Combine(_folder.Root, "Temp");

        var folder = UninstallHandoff.RemovableExtractionFolder(Path.Combine(temp, relative), temp);

        Assert.Equal(allowed, folder is not null);
        if (folder is not null)
        {
            Assert.Equal(Path.Combine(temp, ".net", "HDRpilot-Uninstall"), folder);
        }
    }

    [Fact]
    public void Extraction_folder_refuses_relative_paths_other_temp_folders_and_this_test_process()
    {
        var temp = Path.Combine(_folder.Root, "Temp");

        Assert.Null(UninstallHandoff.RemovableExtractionFolder(@".net\HDRpilot-Uninstall", temp));
        Assert.Null(UninstallHandoff.RemovableExtractionFolder(null, temp));
        Assert.Null(UninstallHandoff.RemovableExtractionFolder(Path.Combine(temp, ".net", "HDRpilot-Uninstall"), null));
        Assert.Null(UninstallHandoff.RemovableExtractionFolder(Path.Combine(temp, ".net", "HDRpilot-Uninstall"), Path.Combine(_folder.Root, "Anderer")));

        // Der Testprozess ist keine Einzeldatei-EXE: seine WPF-Bibliotheken liegen in der .NET-Installation.
        Assert.Null(UninstallHandoff.OwnExtractionFolder(Path.GetTempPath()));
    }

    [Fact]
    public void Temp_copy_is_byte_identical_and_lands_in_a_removable_folder()
    {
        var source = _folder.CreateFile("Programs\\HDRpilot\\HDRpilot.exe", [1, 2, 3, 4, 5, 250]);
        var temp = Path.Combine(_folder.Root, "Temp");

        var copy = UninstallHandoff.CreateTempCopy(source, temp);

        Assert.Equal(UninstallHandoff.CopyName, Path.GetFileName(copy));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(copy));
        Assert.Equal(Path.GetDirectoryName(copy), UninstallHandoff.RemovableTempFolder(copy, temp));
        Assert.NotEqual(copy, UninstallHandoff.CreateTempCopy(source, temp)); // jeder Lauf eigener Ordner
    }

    [Fact]
    public void Failed_copy_leaves_no_folder_behind()
    {
        var temp = Path.Combine(_folder.Root, "Temp");

        Assert.ThrowsAny<IOException>(() => UninstallHandoff.CreateTempCopy(Path.Combine(_folder.Root, "fehlt.exe"), temp));
        Assert.Empty(Directory.GetDirectories(temp));
    }

    [Fact]
    public void Removal_command_waits_retries_and_deletes_only_the_quoted_folders()
    {
        var folder = @"C:\Users\Tom & Jerry\AppData\Local\Temp\HDRpilot-Uninstall-" + Guid1;
        var extraction = @"C:\Users\Tom & Jerry\AppData\Local\Temp\.net\HDRpilot-Uninstall";
        var ping = "\"" + Path.Combine(Environment.SystemDirectory, "PING.EXE") + "\"";

        // Ohne AutoRun (/d) und verzögerte Erweiterung (/v:off); ping mit vollem Pfad; bis zu 30 Versuche im
        // Sekundenabstand, Ende, sobald beide Ordner weg sind.
        Assert.Equal(
            "/d /v:off /c (" + ping + " -n 4 127.0.0.1 >nul) & for /l %i in (1,1,30) do @(rd /s /q \"" + folder + "\" 2>nul & rd /s /q \"" + extraction + "\" 2>nul"
                + " & (if not exist \"" + folder + "\\\" if not exist \"" + extraction + "\\\" exit) & " + ping + " -n 2 127.0.0.1 >nul)",
            UninstallHandoff.RemovalArguments(folder, extraction));
        Assert.Equal(
            "/d /v:off /c (" + ping + " -n 4 127.0.0.1 >nul) & for /l %i in (1,1,30) do @(rd /s /q \"" + folder + "\" 2>nul & (if not exist \"" + folder + "\\\" exit) & " + ping + " -n 2 127.0.0.1 >nul)",
            UninstallHandoff.RemovalArguments(folder));
        Assert.Equal(30, UninstallHandoff.RemovalAttempts);
    }

    [Fact]
    public void Scheduled_removal_deletes_copy_and_extraction_folder_after_a_delay_and_nothing_else()
    {
        var temp = Path.Combine(_folder.Root, "Temp");
        var neighbour = _folder.CreateFile(Path.Combine("Temp", "Nachbar.txt"));
        var extraction = Path.GetDirectoryName(Path.GetDirectoryName(_folder.CreateFile(Path.Combine("Temp", ".net", "HDRpilot-Uninstall", "A5VsRBt8jVNi", "wpfgfx_cor3.dll"))))!;
        var appLibraries = _folder.CreateFile(Path.Combine("Temp", ".net", "HDRpilot", "A5VsRBt8jVNi", "wpfgfx_cor3.dll"));
        var setupLibraries = _folder.CreateFile(Path.Combine("Temp", ".net", "HDRpilot-Setup-1.1.0", "A5VsRBt8jVNi", "wpfgfx_cor3.dll"));
        var copy = UninstallHandoff.CreateTempCopy(_folder.CreateFile("HDRpilot.exe"), temp);
        var folder = Path.GetDirectoryName(copy)!;

        Assert.False(UninstallHandoff.ScheduleSelfRemoval(_folder.CreateFile("HDRpilot.exe"), temp, _folder.Log, extraction));
        Assert.True(UninstallHandoff.ScheduleSelfRemoval(copy, temp, _folder.Log, extraction));
        Assert.True(Directory.Exists(folder)); // erst nach der Wartezeit

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while ((Directory.Exists(folder) || Directory.Exists(extraction)) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
        }

        Assert.False(Directory.Exists(folder), "Temp-Ordner der Kopie nicht gelöscht");
        Assert.False(Directory.Exists(extraction), "Entpack-Ordner der Kopie nicht gelöscht");
        Assert.True(File.Exists(neighbour));
        Assert.True(File.Exists(appLibraries)); // Bibliotheken der App und eines Setups bleiben
        Assert.True(File.Exists(setupLibraries));
        Assert.True(Directory.Exists(temp));
    }

    [Fact]
    public void Scheduled_removal_never_takes_a_foreign_extraction_folder()
    {
        var temp = Path.Combine(_folder.Root, "Temp");
        var appLibraries = _folder.CreateFile(Path.Combine("Temp", ".net", "HDRpilot", "A5VsRBt8jVNi", "wpfgfx_cor3.dll"));
        var copy = UninstallHandoff.CreateTempCopy(_folder.CreateFile("HDRpilot.exe"), temp);
        var folder = Path.GetDirectoryName(copy)!;

        Assert.True(UninstallHandoff.ScheduleSelfRemoval(copy, temp, _folder.Log, Path.Combine(temp, ".net", "HDRpilot")));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (Directory.Exists(folder) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
        }

        Assert.False(Directory.Exists(folder), "Temp-Ordner der Kopie nicht gelöscht");
        Assert.True(File.Exists(appLibraries));
        Assert.Contains("keiner erkannt", _folder.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Waiting_polls_until_the_process_is_gone()
    {
        var polls = 0;

        var gone = await UninstallHandoff.WaitUntilGoneAsync(42, pid =>
        {
            Assert.Equal(42, pid);
            return ++polls < 3;
        }, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(gone);
        Assert.Equal(3, polls);
    }

    [Fact]
    public async Task Waiting_gives_up_after_the_timeout()
    {
        var polls = 0;

        var gone = await UninstallHandoff.WaitUntilGoneAsync(42, _ =>
        {
            polls++;
            return true;
        }, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(60), TestContext.Current.CancellationToken);

        Assert.False(gone);
        Assert.InRange(polls, 2, 100);
        Assert.Equal(TimeSpan.FromMilliseconds(200), UninstallHandoff.WaitInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), UninstallHandoff.WaitTimeout);
    }
}
