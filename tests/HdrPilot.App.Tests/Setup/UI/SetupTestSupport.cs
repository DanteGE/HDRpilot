using HdrPilot.App.Setup;

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

    public static SetupInspection Inspection(string? installed, string setup = "1.1.0", bool autostart = false, bool desktop = false, bool running = false) =>
        new(Version.Parse(setup), installed is null ? null : Version.Parse(installed), installed is not null, InstallDir, autostart, desktop, running);

    public static UninstallInspection Uninstall(string? installed = "1.1.0", params string[] steamGames) =>
        new(InstallDir, installed is null ? null : Version.Parse(installed), steamGames, @"C:\Users\Test\AppData\Roaming\HDRpilot", @"C:\Users\Test\AppData\Local\HDRpilot", false);

    public static SetupArguments Args(params string[] args) => SetupArguments.Parse(args);
}
