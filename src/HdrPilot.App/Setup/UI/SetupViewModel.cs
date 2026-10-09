using System.Collections.ObjectModel;
using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;

namespace HdrPilot.App.Setup.UI;

/// <summary>Seite des Setup-Fensters.</summary>
public enum SetupPage
{
    Options,
    Progress,
    Result,
}

/// <summary>
/// Setup-Fenster (E83): Optionen mit Vorbelegung je Art (neu, Aktualisierung, gleiche oder ältere Version), dann die
/// gemeldeten Schritte live, dann das Ergebnis. Die Arbeit macht die Engine; das ViewModel kennt nur
/// <see cref="ISetupEngine"/>. Bei „HDRpilot ließ sich nicht beenden“ und gesperrten Dateien gibt es „Erneut versuchen“.
/// </summary>
public sealed class SetupViewModel : ObservableObject
{
    private readonly ISetupEngine _engine;
    private readonly SetupInspection _inspection;
    private readonly SetupLog _log;
    private readonly StepList<SetupStep> _steps = new(SetupText.Step);
    private readonly RelayCommand _install;
    private readonly RelayCommand _retry;
    private readonly RelayCommand _cancel;
    private readonly RelayCommand _close;
    private readonly RelayCommand _primary;
    private SetupPage _page;
    private bool _autostart;
    private bool _desktopShortcut;
    private bool _launchAfter;
    private bool _allowDowngrade;
    private SetupResult? _result;
    private string _resultText = string.Empty;

    /// <param name="arguments">Schalter der Befehlszeile belegen die Optionen vor, wenn sie angegeben sind.</param>
    public SetupViewModel(ISetupEngine engine, SetupInspection inspection, SetupArguments arguments, SetupLog log)
    {
        _engine = engine;
        _inspection = inspection;
        _log = log;
        Kind = inspection.Kind;
        _autostart = arguments.Autostart ?? (Kind == SetupKind.Install || inspection.AutostartEnabled);
        _desktopShortcut = arguments.DesktopShortcut ?? inspection.DesktopShortcutExists;
        _launchAfter = !arguments.NoLaunch && (Kind == SetupKind.Install || inspection.AppRunning);
        _allowDowngrade = arguments.AllowDowngrade;
        _install = new RelayCommand(StartInstall, () => CanInstall);
        _retry = new RelayCommand(StartInstall, () => CanRetry);
        _cancel = new RelayCommand(Cancel, () => !IsBusy);
        _close = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty), () => IsResultPage);
        _primary = new RelayCommand(Primary, () => CanInstall || IsResultPage);
    }

    /// <summary>Das Fenster soll sich schließen (Fertig, Abbrechen).</summary>
    public event EventHandler? CloseRequested;

    public SetupKind Kind { get; }

    public string Title => Kind switch
    {
        SetupKind.Install => SetupText.Format(SetupStrings.SetupTitleInstall, SetupText.Version(_inspection.SetupVersion)),
        SetupKind.Update => SetupStrings.SetupTitleUpdate,
        SetupKind.Reinstall => SetupText.Format(SetupStrings.SetupTitleReinstall, SetupText.Version(_inspection.SetupVersion)),
        _ => SetupStrings.SetupTitleDowngrade,
    };

    /// <summary>Alt → neu bei Aktualisierung und älterer Version; sonst leer (die Version steht im Titel).</summary>
    public string VersionInfo => Kind switch
    {
        SetupKind.Update => SetupText.Format(SetupStrings.SetupVersionUpdate, SetupText.Version(_inspection.InstalledVersion), SetupText.Version(_inspection.SetupVersion)),
        SetupKind.Downgrade => SetupText.Format(SetupStrings.SetupVersionDowngrade, SetupText.Version(_inspection.InstalledVersion), SetupText.Version(_inspection.SetupVersion)),
        _ => string.Empty,
    };

    public bool HasVersionInfo => VersionInfo.Length > 0;

    public string InstallDirectory => _inspection.InstallDirectory;

    /// <summary>Wo Einstellungen und Daten liegen (der Setup fasst sie nie an).</summary>
    public string DataNote => SetupText.Format(SetupStrings.SetupDataNote, _engine.Locations.DataPaths.ConfigDirectory, _engine.Locations.DataPaths.DataDirectory);

    public bool ShowRunningNote => _inspection.AppRunning;

    public bool Autostart
    {
        get => _autostart;
        set => SetProperty(ref _autostart, value);
    }

    public bool DesktopShortcut
    {
        get => _desktopShortcut;
        set => SetProperty(ref _desktopShortcut, value);
    }

    public bool LaunchAfter
    {
        get => _launchAfter;
        set => SetProperty(ref _launchAfter, value);
    }

    public bool IsDowngrade => Kind == SetupKind.Downgrade;

    /// <summary>Ausdrückliche Bestätigung einer älteren Version; vorher bleibt „Installieren“ gesperrt.</summary>
    public bool AllowDowngrade
    {
        get => _allowDowngrade;
        set
        {
            if (SetProperty(ref _allowDowngrade, value))
            {
                RaiseCommands();
            }
        }
    }

    public string InstallButtonText => Kind == SetupKind.Update ? SetupStrings.SetupUpdateButton : SetupStrings.SetupInstallButton;

    public SetupPage Page
    {
        get => _page;
        private set
        {
            if (SetProperty(ref _page, value))
            {
                OnPropertyChanged(nameof(IsOptionsPage));
                OnPropertyChanged(nameof(IsProgressPage));
                OnPropertyChanged(nameof(IsResultPage));
                OnPropertyChanged(nameof(ShowsSteps));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(Succeeded));
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(CanRetry));
                OnPropertyChanged(nameof(ShowCancel));
                RaiseCommands();
            }
        }
    }

    public bool IsOptionsPage => Page == SetupPage.Options;

    public bool IsProgressPage => Page == SetupPage.Progress;

    public bool IsResultPage => Page == SetupPage.Result;

    public bool ShowsSteps => Page != SetupPage.Options;

    /// <summary>Installation läuft: Fenster nicht schließbar (der Rückfall auf die alte Version braucht das Ende).</summary>
    public bool IsBusy => Page == SetupPage.Progress;

    public bool Succeeded => IsResultPage && _result?.Success == true;

    public bool HasError => IsResultPage && _result?.Success == false;

    public bool CanRetry => HasError && _result?.Error is SetupErrorKind.AppStillRunning or SetupErrorKind.FilesInUse;

    public bool ShowCancel => IsOptionsPage || HasError;

    public bool CanInstall => IsOptionsPage && (!IsDowngrade || AllowDowngrade);

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public ReadOnlyObservableCollection<ProgressStepItem> Steps => _steps.Items;

    public StepList<SetupStep> StepList => _steps;

    /// <summary>Exit-Code beim Schließen: abgebrochen, bis ein Ergebnis da ist.</summary>
    public int ExitCode { get; private set; } = SetupExitCodes.Cancelled;

    public InstallOptions Options => new(Autostart, DesktopShortcut, LaunchAfter, IsDowngrade && AllowDowngrade);

    public ICommand InstallCommand => _install;

    public ICommand RetryCommand => _retry;

    public ICommand CancelCommand => _cancel;

    public ICommand CloseCommand => _close;

    /// <summary>Eingabetaste: Installieren, auf der Ergebnisseite erneut versuchen oder schließen.</summary>
    public ICommand PrimaryCommand => _primary;

    /// <summary>Installiert (oder versucht es erneut). Fehler der Engine landen als Ergebnis, nie als Ausnahme.</summary>
    public async Task InstallAsync()
    {
        if (!CanInstall && !CanRetry)
        {
            return;
        }

        var options = Options;
        _steps.Reset(StepsFor(options));
        _result = null;
        Page = SetupPage.Progress;
        SetupResult result;
        try
        {
            result = await _engine.InstallAsync(options, new UiProgress<SetupStep>(OnStep));
        }
        catch (Exception ex)
        {
            _log.Error("Installation abgebrochen", ex);
            result = SetupResult.Failed(SetupErrorKind.Unexpected, ex.Message, _inspection.InstalledVersion);
        }

        SetupRunner.LogResult(_log, result);
        if (result.Success)
        {
            _steps.Complete();
        }
        else
        {
            _steps.Fail();
        }

        _result = result;
        ExitCode = SetupMode.ExitCodeFor(result);
        ResultText = result.Success
            ? SetupText.Format(Kind == SetupKind.Update ? SetupStrings.SetupDoneUpdate : SetupStrings.SetupDoneInstall, SetupText.Version(result.NewVersion ?? _inspection.SetupVersion))
            : SetupText.Error(result.Error, result.Message, _log.FilePath);
        Page = SetupPage.Result;
    }

    private IEnumerable<SetupStep> StepsFor(InstallOptions options)
    {
        yield return SetupStep.CheckSetupFile;
        if (_inspection.AppRunning)
        {
            yield return SetupStep.StopRunningApp;
        }

        yield return SetupStep.CopyFiles;
        yield return SetupStep.Shortcuts;
        yield return SetupStep.Autostart;
        yield return SetupStep.Register;
        if (options.LaunchAfterInstall)
        {
            yield return SetupStep.LaunchApp;
        }
    }

    private void OnStep(SetupStep step)
    {
        if (IsBusy)
        {
            _steps.Begin(step);
        }
    }

    private void StartInstall() => _ = InstallAsync();

    private void Cancel()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsOptionsPage)
        {
            ExitCode = SetupExitCodes.Cancelled;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Primary()
    {
        if (CanInstall || CanRetry)
        {
            StartInstall();
        }
        else if (IsResultPage)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseCommands()
    {
        _install.RaiseCanExecuteChanged();
        _retry.RaiseCanExecuteChanged();
        _cancel.RaiseCanExecuteChanged();
        _close.RaiseCanExecuteChanged();
        _primary.RaiseCanExecuteChanged();
    }
}
