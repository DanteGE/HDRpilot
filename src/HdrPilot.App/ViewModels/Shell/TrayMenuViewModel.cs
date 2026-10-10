using System.Globalization;
using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>Daten des Tray-Menüs. Lebt nur, solange das Menü offen ist.</summary>
public sealed class TrayMenuViewModel : ObservableObject
{
    private readonly ITrayHost _host;
    private readonly Action<Exception> _onError;
    private TrayState _state;

    public TrayMenuViewModel(ITrayHost host, TrayState state, Action<Exception> onError)
    {
        _host = host;
        _state = state;
        _onError = onError;
        ToggleHdrCommand = new RelayCommand(() => RunAsync(_host.ToggleHdrNowAsync));
        OpenCommand = new RelayCommand(() => Run(_host.ShowMainWindow));
        SettingsCommand = new RelayCommand(() => Run(_host.ShowSettingsWindow));
        LogsCommand = new RelayCommand(() => Run(_host.OpenLogFolder));
        ExitCommand = new RelayCommand(() => RunAsync(_host.ExitAsync));
    }

    public ICommand ToggleHdrCommand { get; }

    public ICommand OpenCommand { get; }

    public ICommand SettingsCommand { get; }

    public ICommand LogsCommand { get; }

    public ICommand ExitCommand { get; }

    public bool IsHdrOn => _state.HdrOn;

    public bool HdrSupported => _state.HdrSupported || _state.HdrOn;

    /// <summary>Abhakbarer Menüpunkt „Automatik pausieren“.</summary>
    public bool IsPaused
    {
        get => _state.Paused;
        set
        {
            if (value == _state.Paused)
            {
                return;
            }

            _state = _state with { Paused = value };
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDetail));
            Run(() => _host.SetPaused(value));
        }
    }

    public string StatusTitle => _state.HdrOn ? ShellStrings.TrayStatusHdrOn : ShellStrings.TrayStatusHdrOff;

    public string StatusDetail
    {
        get
        {
            if (!HdrSupported)
            {
                return ShellStrings.TrayStatusNoHdr;
            }

            if (_state.ActiveGames.Count == 1)
            {
                return string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayStatusGame, _state.ActiveGames[0]);
            }

            if (_state.ActiveGames.Count > 1)
            {
                return string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayStatusGames, _state.ActiveGames.Count);
            }

            return _state.Paused ? ShellStrings.TrayStatusPaused : ShellStrings.TrayStatusActive;
        }
    }

    public string ToggleHdrText => _state.HdrOn ? ShellStrings.TrayMenuToggleHdrOff : ShellStrings.TrayMenuToggleHdrOn;

    /// <summary>Oberste, graue Zeile des Menüs: „HDRpilot 1.2.0“ (<see cref="AppVersion"/>).</summary>
    public string VersionText => AppVersion.DisplayText;

    public void Update(TrayState state)
    {
        if (state == _state)
        {
            return;
        }

        _state = state;
        OnPropertyChanged(nameof(IsHdrOn));
        OnPropertyChanged(nameof(HdrSupported));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(ToggleHdrText));
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _onError(ex);
        }
    }

    private async void RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _onError(ex);
        }
    }
}
