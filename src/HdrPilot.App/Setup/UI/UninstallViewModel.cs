using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Rückfrage vor dem Entfernen (installierte EXE mit <c>--uninstall</c>): Version, ob auch die Daten weg sollen
/// (Standard: behalten) und Warnung vor Steam-Startoptionen, die noch auf HDRpilot zeigen. „Entfernen“ startet die
/// Kopie im Temp-Ordner, die die eigentliche Arbeit macht; danach schließt sich das Fenster. Fehlt die installierte
/// EXE, geht „Entfernen“ trotzdem: Ob noch etwas da ist (etwa der Eintrag unter „Installierte Apps“), weiß erst die
/// Engine; ist nichts da, meldet die Kopie „nicht installiert“.
/// </summary>
public sealed class UninstallViewModel : ObservableObject
{
    private readonly UninstallInspection _inspection;
    private readonly Func<bool, Task> _handOff;
    private readonly SetupLog _log;
    private readonly RelayCommand _confirm;
    private readonly RelayCommand _cancel;
    private bool _removeData;
    private bool _isBusy;
    private string? _errorText;

    /// <param name="handOff">Startet die Kopie im Temp-Ordner (Argument: auch Daten löschen); wirft, wenn das scheitert.</param>
    public UninstallViewModel(UninstallInspection inspection, SetupArguments arguments, Func<bool, Task> handOff, SetupLog log)
    {
        _inspection = inspection;
        _handOff = handOff;
        _log = log;
        _removeData = arguments.RemoveData;
        _confirm = new RelayCommand(() => _ = ConfirmAsync(), () => CanConfirm);
        _cancel = new RelayCommand(Cancel, () => !IsBusy);
    }

    public event EventHandler? CloseRequested;

    /// <summary>Mit Version, wenn die installierte EXE da ist; sonst nur „HDRpilot entfernen“.</summary>
    public string Title => _inspection.InstalledVersion is { } version
        ? SetupText.Format(SetupStrings.UninstallTitle, SetupText.Version(version))
        : SetupStrings.UninstallWindowTitle;

    public bool RemoveData
    {
        get => _removeData;
        set => SetProperty(ref _removeData, value);
    }

    public IReadOnlyList<string> SteamGames => _inspection.SteamGamesWithLaunchOption;

    public bool HasSteamGames => SteamGames.Count > 0;

    /// <summary>Kopie wird angelegt und gestartet: Fenster nicht schließbar.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                _confirm.RaiseCanExecuteChanged();
                _cancel.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => ErrorText is not null;

    public bool CanConfirm => !IsBusy;

    /// <summary>Exit-Code beim Schließen: abgebrochen, Ok nach dem Start der Kopie, sonst Fehler.</summary>
    public int ExitCode { get; private set; } = SetupExitCodes.Cancelled;

    public ICommand ConfirmCommand => _confirm;

    public ICommand CancelCommand => _cancel;

    public async Task ConfirmAsync()
    {
        if (!CanConfirm)
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        try
        {
            await _handOff(RemoveData);
            ExitCode = SetupExitCodes.Ok;
            IsBusy = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.Error("Kopie zum Entfernen nicht gestartet", ex);
            ExitCode = SetupExitCodes.Failed;
            ErrorText = SetupText.Error(UninstallHandoff.ErrorKindFor(ex), ex.Message, _log.FilePath);
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        if (!IsBusy)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
