using System.Globalization;
using System.IO;
using System.Windows;
using HdrPilot.App.Infrastructure;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Platform;
using HdrPilot.Core.Processes;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Was die Abläufe von <see cref="SetupRunner"/> nach außen tun: Engines erzeugen, Fenster und Meldungen zeigen, die
/// Kopie zum Entfernen starten, auf die installierte EXE warten und den Temp-Ordner aufräumen lassen. Im Test ersetzt
/// (ohne Fenster, Prozesse und echte Orte).
/// </summary>
public interface ISetupHost
{
    /// <summary>Prozess-ID dieser EXE (die Kopie zum Entfernen wartet auf ihr Ende).</summary>
    int ProcessId { get; }

    ISetupEngine CreateSetup(SetupLocations locations, SetupBundle bundle, SetupLog log);

    IUninstallEngine CreateUninstall(SetupLocations locations, SetupLog log);

    /// <summary>Setup-Fenster bis zum Schließen; liefert seinen Exit-Code.</summary>
    Task<int> ShowSetupAsync(ISetupEngine engine, SetupInspection inspection, SetupArguments arguments, SetupLog log);

    /// <summary>Rückfrage vor dem Entfernen bis zum Schließen; <paramref name="handOff"/> startet die Kopie (Argument: auch Daten löschen).</summary>
    Task<int> ShowUninstallAsync(UninstallInspection inspection, SetupArguments arguments, Func<bool, Task> handOff, SetupLog log);

    /// <summary>Fortschritt des Entfernens in der Kopie bis zum Schließen; liefert den Exit-Code.</summary>
    Task<int> ShowUninstallProgressAsync(IUninstallEngine engine, bool removeData, string configDirectory, SetupLog log);

    void ShowError(string title, string text);

    /// <summary>Kopiert die eigene EXE in den Temp-Ordner und startet die Kopie mit <paramref name="arguments"/>; wirft, wenn das scheitert.</summary>
    Task HandOffAsync(IReadOnlyList<string> arguments, SetupLog log);

    /// <summary>Wartet ohne Prozess-Handle, bis der Prozess beendet ist (höchstens <see cref="UninstallHandoff.WaitTimeout"/>).</summary>
    Task WaitUntilGoneAsync(int processId, SetupLog log);

    /// <summary>Lässt Temp- und Entpack-Ordner der Kopie nach dem Ende löschen (nur, wenn diese EXE die Kopie ist).</summary>
    void ScheduleSelfRemoval(SetupLog log);

    /// <summary>Leitet Windows Schreibzugriffe auf <paramref name="folder"/> in den Container einer anderen App um (E84)? Wirft nie.</summary>
    ContainerRedirection DetectContainer(string folder);

    /// <summary>
    /// Prüfläufe (<c>--test-root</c>) überspringen die Container-Prüfung, außer diese Angabe verlangt sie (Umgebungsvariable
    /// <see cref="SetupRunner.ForceContainerCheckVariable"/> = 1, nur für Prüfskripte).
    /// </summary>
    bool ContainerCheckForced { get; }
}

/// <summary>Echte Umgebung: Fenster im Stil der App, die eigene EXE, Prozessliste und Temp-Ordner.</summary>
public sealed class WpfSetupHost(Application app) : ISetupHost
{
    public int ProcessId => Environment.ProcessId;

    public ISetupEngine CreateSetup(SetupLocations locations, SetupBundle bundle, SetupLog log) => SetupEngines.CreateSetup(locations, bundle, log);

    public IUninstallEngine CreateUninstall(SetupLocations locations, SetupLog log) => SetupEngines.CreateUninstall(locations, log);

    public Task<int> ShowSetupAsync(ISetupEngine engine, SetupInspection inspection, SetupArguments arguments, SetupLog log)
    {
        ThemeManager.Initialize(app);
        var model = new SetupViewModel(engine, inspection, arguments, log);
        var window = new SetupWindow();
        window.Attach(model);
        return ShowAsync(window, () => model.ExitCode);
    }

    public Task<int> ShowUninstallAsync(UninstallInspection inspection, SetupArguments arguments, Func<bool, Task> handOff, SetupLog log)
    {
        ThemeManager.Initialize(app);
        var model = new UninstallViewModel(inspection, arguments, handOff, log);
        var window = new UninstallWindow();
        window.Attach(model);
        return ShowAsync(window, () => model.ExitCode);
    }

    public async Task<int> ShowUninstallProgressAsync(IUninstallEngine engine, bool removeData, string configDirectory, SetupLog log)
    {
        ThemeManager.Initialize(app);
        var model = new UninstallProgressViewModel(engine, removeData, configDirectory, log);
        var window = new UninstallProgressWindow();
        window.Attach(model);
        var closed = ShowAsync(window, () => model.ExitCode);
        await model.RunAsync();
        return await closed;
    }

    public void ShowError(string title, string text) => MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public Task HandOffAsync(IReadOnlyList<string> arguments, SetupLog log)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Pfad der eigenen EXE unbekannt.");
        return Task.Run(() => UninstallHandoff.Start(self, Path.GetTempPath(), arguments, log));
    }

    /// <summary>Über die Prozessliste (alle 200 ms, höchstens 30 s), nie über ein Handle.</summary>
    public async Task WaitUntilGoneAsync(int processId, SetupLog log)
    {
        using var snapshot = new NtProcessSnapshotProvider();
        bool IsRunning(int id)
        {
            try
            {
                return snapshot.GetProcesses().Any(p => p.ProcessId == id);
            }
            catch (Exception ex)
            {
                // Ohne Prozessliste nicht ewig warten; scheitert das Entfernen an der laufenden EXE, meldet es das.
                log.Warn("Prozessliste nicht lesbar: " + ex.Message);
                return false;
            }
        }

        if (!await UninstallHandoff.WaitUntilGoneAsync(processId, IsRunning, UninstallHandoff.WaitInterval, UninstallHandoff.WaitTimeout))
        {
            log.Warn(string.Create(CultureInfo.InvariantCulture, $"Prozess {processId} lief nach {UninstallHandoff.WaitTimeout.TotalSeconds:0} s noch; Entfernen versucht es trotzdem."));
        }
    }

    public void ScheduleSelfRemoval(SetupLog log)
    {
        var temp = Path.GetTempPath();
        UninstallHandoff.ScheduleSelfRemoval(Environment.ProcessPath, temp, log, UninstallHandoff.OwnExtractionFolder(temp));
    }

    public ContainerRedirection DetectContainer(string folder) => new ContainerDetector(SystemEnvironment.Instance).Detect(folder);

    public bool ContainerCheckForced => Environment.GetEnvironmentVariable(SetupRunner.ForceContainerCheckVariable) == "1";

    private static Task<int> ShowAsync(Window window, Func<int> exitCode)
    {
        var closed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(exitCode());
        window.Show();
        window.Activate();
        return closed.Task;
    }
}
