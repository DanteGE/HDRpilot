using System.Globalization;
using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.Core.AutoHdr;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>Daten des Tray-Menüs. Lebt nur, solange das Menü offen ist.</summary>
public sealed class TrayMenuViewModel : ObservableObject
{
    private readonly ITrayHost _host;
    private readonly Action<Exception> _onError;
    private TrayState _state;
    private AutoHdrSwitchState _autoHdr;

    /// <summary>An, aber nur, weil HDRpilot den Schalter gerade für einen Spielstart hält (danach wieder aus).</summary>
    private bool _autoHdrHeld;

    public TrayMenuViewModel(ITrayHost host, TrayState state, Action<Exception> onError)
    {
        _host = host;
        _state = state;
        _onError = onError;

        // Das Menü entsteht beim Öffnen: den Windows-Schalter jetzt lesen (ein Registry-Wert, kein Zeitgeber im Hintergrund).
        _autoHdr = AutoHdrSwitchState.Unknown;
        Run(() =>
        {
            _autoHdr = _host.ReadAutoHdrSwitch();
            _autoHdrHeld = _autoHdr == AutoHdrSwitchState.On && _host.AutoHdrSwitchHeld;
        });

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

    /// <summary>
    /// Abhakbarer Eintrag „Auto HDR“ (G9.9): der globale Auto-HDR-Schalter von Windows, wie beim Öffnen des Menüs gelesen.
    /// Umschalten setzt nur dessen Token und gilt ab dem nächsten Spielstart, auch ohne HDRpilot. Es ist die Wahl des
    /// Nutzers: Eine Auto-HDR-Sitzung stellt sie danach nicht zurück (E88). Nicht lesbar: Eintrag gesperrt, nichts geschrieben.
    /// </summary>
    public bool IsAutoHdrOn
    {
        get => _autoHdr == AutoHdrSwitchState.On;
        set
        {
            if (value == IsAutoHdrOn || _autoHdr == AutoHdrSwitchState.Unknown)
            {
                return;
            }

            var after = AutoHdrSwitchState.Unknown;
            Run(() => after = _host.SetAutoHdrSwitch(value));
            _autoHdr = after;
            _autoHdrHeld = false; // umgeschaltet ist es die Wahl des Nutzers, HDRpilot hält den Schalter nicht mehr
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanToggleAutoHdr));
            OnPropertyChanged(nameof(AutoHdrHint));
        }
    }

    public bool CanToggleAutoHdr => _autoHdr != AutoHdrSwitchState.Unknown;

    /// <summary>
    /// Tooltip des Eintrags: „gilt ab dem nächsten Spielstart“; hält HDRpilot den Schalter gerade für ein Spiel, dass er
    /// danach wieder aus ist (sonst sähe das Häkchen wie eine dauerhafte Einstellung aus); bei einem nicht lesbaren Wert
    /// der Grund für die Sperre.
    /// </summary>
    public string AutoHdrHint => !CanToggleAutoHdr
        ? ShellStrings.TrayMenuAutoHdrUnknown
        : _autoHdrHeld ? ShellStrings.TrayMenuAutoHdrHeld : ShellStrings.TrayMenuAutoHdrHint;

    public string StatusTitle => TitleOf(_state);

    public string StatusDetail => DetailOf(_state);

    /// <summary>„HDR an“ / „HDR aus“ im Kopf (auch für die Vorschau in den Einstellungen).</summary>
    public static string TitleOf(TrayState state) => state.HdrOn ? ShellStrings.TrayStatusHdrOn : ShellStrings.TrayStatusHdrOff;

    /// <summary>„Automatik aktiv“, „Automatik pausiert“, „Cyberpunk 2077 läuft“ … im Kopf (auch für die Vorschau).</summary>
    public static string DetailOf(TrayState state)
    {
        if (!state.HdrSupported && !state.HdrOn)
        {
            return ShellStrings.TrayStatusNoHdr;
        }

        if (state.ActiveGames.Count == 1)
        {
            return string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayStatusGame, state.ActiveGames[0]);
        }

        if (state.ActiveGames.Count > 1)
        {
            return string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayStatusGames, state.ActiveGames.Count);
        }

        return state.Paused ? ShellStrings.TrayStatusPaused : ShellStrings.TrayStatusActive;
    }

    public string ToggleHdrText => _state.HdrOn ? ShellStrings.TrayMenuToggleHdrOff : ShellStrings.TrayMenuToggleHdrOn;

    /// <summary>Graue Zeile unter dem Menükopf: „Version 1.2.0“ (<see cref="AppVersion"/>; der Kopf zeigt schon den Namen).</summary>
    public string VersionText => AppVersion.LabelText;

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
