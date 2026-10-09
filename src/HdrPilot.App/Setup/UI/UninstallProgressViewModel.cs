using System.Collections.ObjectModel;
using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.Util;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Entfernen in der Kopie im Temp-Ordner: Schritte live, danach das Ergebnis mit dem Hinweis, wo die Einstellungen
/// liegen (oder dass sie gelöscht sind). „Fertig“ erst am Ende; vorher ist das Fenster nicht schließbar. Bei einem Fehler
/// wird genau der Schritt rot, den die Engine nennt; ein nicht wiederhergestellter HDR-Zustand färbt seinen Schritt rot
/// und steht im Ergebnis, ohne dass das Entfernen als gescheitert gilt.
/// </summary>
public sealed class UninstallProgressViewModel : ObservableObject
{
    private readonly IUninstallEngine _engine;
    private readonly bool _removeData;
    private readonly string _configDirectory;
    private readonly SetupLog _log;
    private readonly StepList<UninstallStep> _steps = new(SetupText.Step);
    private readonly RelayCommand _close;
    private bool _isBusy;
    private SetupResult? _result;
    private string _resultText = string.Empty;

    /// <param name="configDirectory">Ordner der Einstellungen (für „Deine Einstellungen liegen weiter in …“).</param>
    public UninstallProgressViewModel(IUninstallEngine engine, bool removeData, string configDirectory, SetupLog log)
    {
        _engine = engine;
        _removeData = removeData;
        _configDirectory = configDirectory;
        _log = log;
        _close = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty), () => IsDone);
        _steps.Reset(StepsFor(removeData));
    }

    public event EventHandler? CloseRequested;

    public ReadOnlyObservableCollection<ProgressStepItem> Steps => _steps.Items;

    public StepList<UninstallStep> StepList => _steps;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsDone => _result is not null;

    public bool Succeeded => _result?.Success == true;

    public bool HasError => _result?.Success == false;

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    /// <summary>Exit-Code der Kopie (auch für das Protokoll); Fehler, solange kein Ergebnis da ist.</summary>
    public int ExitCode { get; private set; } = SetupExitCodes.Failed;

    public ICommand CloseCommand => _close;

    public async Task RunAsync()
    {
        if (IsBusy || IsDone)
        {
            return;
        }

        IsBusy = true;
        SetupResult result;
        try
        {
            result = await _engine.UninstallAsync(new UninstallOptions(_removeData), new UiProgress<UninstallStep>(OnStep));
        }
        catch (Exception ex)
        {
            _log.Error("Entfernen abgebrochen", ex);
            result = SetupResult.Failed(SetupErrorKind.Unexpected, ex.Message);
        }

        SetupRunner.LogResult(_log, result);
        var hdrNotRestored = result.Has(SetupWarning.HdrNotRestored);
        IEnumerable<UninstallStep> alsoFailed = hdrNotRestored ? [UninstallStep.RestoreHdr] : [];
        if (result.Success)
        {
            _steps.Complete(alsoFailed);
        }
        else
        {
            _steps.Fail(result.FailedUninstallStep, alsoFailed);
        }

        ExitCode = SetupMode.ExitCodeFor(result);
        var text = result.Success
            ? SetupStrings.UninstallDone + " " + (_removeData ? SetupStrings.UninstallDoneDataRemoved : SetupText.Format(SetupStrings.UninstallDoneDataKept, _configDirectory))
            : SetupText.UninstallError(result.Error, result.Message, _log.FilePath, LockedFileWithoutEntry(result));
        ResultText = hdrNotRestored ? text + " " + SetupStrings.UninstallHdrNotRestored : text;
        _result = result;
        IsBusy = false;
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(Succeeded));
        OnPropertyChanged(nameof(HasError));
        _close.RaiseCanExecuteChanged();
    }

    private static IEnumerable<UninstallStep> StepsFor(bool removeData) =>
        Enum.GetValues<UninstallStep>().Where(s => removeData || s != UninstallStep.RemoveData);

    /// <summary>
    /// Gesperrte Datei, nach der der Eintrag unter „Installierte Apps“ schon weg ist: die Engine löscht die EXE zuerst
    /// und lässt den Eintrag nur stehen, solange die EXE noch da ist. Ist die EXE selbst gesperrt, null (neuer Versuch geht).
    /// </summary>
    private string? LockedFileWithoutEntry(SetupResult result) =>
        result is { Error: SetupErrorKind.FilesInUse, FailedFile: { } file } && !PathUtil.PathEquals(file, _engine.Locations.ExePath) ? file : null;

    private void OnStep(UninstallStep step)
    {
        if (IsBusy)
        {
            _steps.Begin(step);
        }
    }
}
