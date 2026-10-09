using System.Globalization;
using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using HdrPilot.Core.Platform;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>
/// Setup-Engine ohne Wirkung: meldet vorgegebene Schritte, liefert vorgegebene Ergebnisse (oder wartet auf
/// <see cref="Pending"/>) und schreibt die Aufrufe mit. Orte immer unter einem Testordner.
/// </summary>
internal sealed class FakeSetupEngine(SetupInspection inspection, SetupLocations locations) : ISetupEngine
{
    public SetupLocations Locations { get; } = locations;

    public SetupInspection Inspection { get; set; } = inspection;

    public List<InstallOptions> Calls { get; } = [];

    /// <summary>Ergebnisse der nächsten Aufrufe; leer: Erfolg.</summary>
    public Queue<SetupResult> Results { get; } = new();

    /// <summary>Schritte, die jeder Aufruf meldet (vor dem Ergebnis).</summary>
    public IReadOnlyList<SetupStep> Report { get; set; } = [SetupStep.CheckSetupFile, SetupStep.CopyFiles, SetupStep.Register];

    /// <summary>Wenn gesetzt, endet der Aufruf erst, wenn der Test diese Aufgabe abschließt.</summary>
    public TaskCompletionSource<SetupResult>? Pending { get; set; }

    public Exception? Failure { get; set; }

    public SetupInspection Inspect() => Inspection;

    public async Task<SetupResult> InstallAsync(InstallOptions options, IProgress<SetupStep>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(options);
        foreach (var step in Report)
        {
            progress?.Report(step);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        if (Pending is not null)
        {
            return await Pending.Task;
        }

        return Results.Count > 0 ? Results.Dequeue() : Success(Inspection.InstalledVersion, Inspection.SetupVersion, options.LaunchAfterInstall);
    }

    public static SetupResult Success(Version? previous, Version? next, bool started = false) => new(true, null, null, previous, next, started);
}

/// <summary>Entfernen ohne Wirkung (wie <see cref="FakeSetupEngine"/>).</summary>
internal sealed class FakeUninstallEngine(UninstallInspection inspection, SetupLocations locations) : IUninstallEngine
{
    public SetupLocations Locations { get; } = locations;

    public UninstallInspection Inspection { get; set; } = inspection;

    public List<UninstallOptions> Calls { get; } = [];

    public SetupResult? Result { get; set; }

    public IReadOnlyList<UninstallStep> Report { get; set; } = [UninstallStep.StopRunningApp, UninstallStep.RemoveFiles];

    public TaskCompletionSource<SetupResult>? Pending { get; set; }

    public Exception? Failure { get; set; }

    public UninstallInspection Inspect() => Inspection;

    public async Task<SetupResult> UninstallAsync(UninstallOptions options, IProgress<UninstallStep>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(options);
        foreach (var step in Report)
        {
            progress?.Report(step);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        if (Pending is not null)
        {
            return await Pending.Task;
        }

        return Result ?? new SetupResult(true, null, null, Inspection.InstalledVersion, null, false);
    }
}

/// <summary>
/// Umgebung der Abläufe ohne Fenster, Prozesse und echte Orte: schreibt jeden Aufruf in <see cref="Calls"/> mit (in
/// Reihenfolge), Fenster liefern <see cref="WindowExitCode"/>, die Rückfrage zum Entfernen wählt
/// <see cref="ConfirmRemoveData"/>, das Aufräumen schreibt <see cref="RemovalMarker"/> ins Protokoll.
/// </summary>
internal sealed class FakeSetupHost(FakeSetupEngine setup, FakeUninstallEngine uninstall) : ISetupHost
{
    public const string RemovalMarker = "Attrappe: Temp-Ordner wird gelöscht";

    public int ProcessId => 4242;

    public List<string> Calls { get; } = [];

    /// <summary>Befehlszeilen, mit denen die Kopie zum Entfernen gestartet worden wäre.</summary>
    public List<IReadOnlyList<string>> HandOffs { get; } = [];

    public List<string> Errors { get; } = [];

    public int WindowExitCode { get; set; } = SetupExitCodes.Ok;

    /// <summary>Wahl in der Rückfrage: „Entfernen“ mit oder ohne Daten; null: Abbrechen.</summary>
    public bool? ConfirmRemoveData { get; set; }

    public ISetupEngine CreateSetup(SetupLocations locations, SetupBundle bundle, SetupLog log)
    {
        Calls.Add("CreateSetup");
        return setup;
    }

    public IUninstallEngine CreateUninstall(SetupLocations locations, SetupLog log)
    {
        Calls.Add("CreateUninstall");
        return uninstall;
    }

    public Task<int> ShowSetupAsync(ISetupEngine engine, SetupInspection inspection, SetupArguments arguments, SetupLog log)
    {
        Calls.Add("ShowSetup");
        return Task.FromResult(WindowExitCode);
    }

    public async Task<int> ShowUninstallAsync(UninstallInspection inspection, SetupArguments arguments, Func<bool, Task> handOff, SetupLog log)
    {
        Calls.Add("ShowUninstall");
        if (ConfirmRemoveData is not { } removeData)
        {
            return SetupExitCodes.Cancelled;
        }

        await handOff(removeData);
        return SetupExitCodes.Ok;
    }

    public async Task<int> ShowUninstallProgressAsync(IUninstallEngine engine, bool removeData, string configDirectory, SetupLog log)
    {
        Calls.Add("ShowUninstallProgress " + configDirectory);
        var result = await engine.UninstallAsync(new UninstallOptions(removeData));
        return SetupMode.ExitCodeFor(result);
    }

    public void ShowError(string title, string text)
    {
        Calls.Add("ShowError");
        Errors.Add(text);
    }

    public Task HandOffAsync(IReadOnlyList<string> arguments, SetupLog log)
    {
        Calls.Add("HandOff");
        HandOffs.Add(arguments);
        return Task.CompletedTask;
    }

    public Task WaitUntilGoneAsync(int processId, SetupLog log)
    {
        Calls.Add("Wait " + processId.ToString(CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    public void ScheduleSelfRemoval(SetupLog log)
    {
        Calls.Add("ScheduleSelfRemoval");
        log.Info(RemovalMarker);
    }

    /// <summary>Ergebnis der Container-Prüfung (E84): Paketordner der App, null = nicht umgeleitet.</summary>
    public string? ContainerPackage { get; set; }

    public string? ContainerProblem { get; set; }

    public bool ContainerCheckForced { get; set; }

    public ContainerRedirection DetectContainer(string folder)
    {
        Calls.Add("DetectContainer " + folder);
        return new ContainerRedirection(folder, ContainerPackage, ContainerProblem);
    }
}

/// <summary>Eigener Ordner im Temp-Ordner je Test (Protokoll, Testablage, Setup-Dateien), wird danach gelöscht.</summary>
internal sealed class SetupTestFolder : IDisposable
{
    public SetupTestFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "HDRpilot-SetupUiTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Locations = SetupLocations.ForTestRoot(Root);
        Log = new SetupLog(Path.Combine(Root, "setup.log"));
    }

    public string Root { get; }

    public SetupLocations Locations { get; }

    public SetupLog Log { get; }

    public string LogText => File.Exists(Log.FilePath) ? File.ReadAllText(Log.FilePath) : string.Empty;

    public string[] LogLines => File.Exists(Log.FilePath) ? File.ReadAllLines(Log.FilePath) : [];

    /// <summary>Setup-Datei (EXE mit angehängtem Paket) im Testordner.</summary>
    public string CreateSetupFile()
    {
        var setup = Path.Combine(Root, "HDRpilot-Setup-1.1.0.exe");
        SetupBundle.Write(CreateFile("HDRpilot.exe"), new Dictionary<string, string>(), setup);
        return setup;
    }

    /// <summary>Datei mit festem Inhalt (etwa eine „EXE“ für Paket und Kopie).</summary>
    public string CreateFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? [0x4D, 0x5A, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist Kür.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class SetupFixtures
{
    public const string InstallDir = @"C:\Users\Test\AppData\Local\Programs\HDRpilot";

    public static SetupInspection Inspection(string? installed, string setup = "1.1.0", bool autostart = false, bool desktop = false, bool running = false, bool elevated = false) =>
        new(Version.Parse(setup), installed is null ? null : Version.Parse(installed), installed is not null, InstallDir, autostart, desktop, running, elevated);

    public static UninstallInspection Uninstall(string? installed = "1.1.0", params string[] steamGames) =>
        new(InstallDir, installed is null ? null : Version.Parse(installed), steamGames, @"C:\Users\Test\AppData\Roaming\HDRpilot", @"C:\Users\Test\AppData\Local\HDRpilot", false);

    public static SetupArguments Args(params string[] args) => SetupArguments.Parse(args);
}
