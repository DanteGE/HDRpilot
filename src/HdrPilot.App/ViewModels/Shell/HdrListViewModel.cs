using System.Globalization;
using System.IO;
using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>
/// Abschnitt „HDR-Liste (PCGamingWiki)“ der Einstellungen: Stand des Abgleichs, „Jetzt abgleichen“ und das optionale
/// Konto. Anders als die übrigen Einstellungen wirken diese Knöpfe sofort (das Konto steht nicht in config.json); der
/// Schalter „Bei jedem Start abgleichen“ gehört zu <see cref="SettingsViewModel"/> und wird mit „Speichern“ übernommen.
/// Das Bot-Passwort kommt nur über <see cref="SetPassword"/> herein und wird nie angezeigt.
/// </summary>
public sealed class HdrListViewModel : ObservableObject, IDisposable
{
    public const string CreateAccountUrl = "https://www.pcgamingwiki.com/wiki/Special:CreateAccount";
    public const string BotPasswordsUrl = "https://www.pcgamingwiki.com/wiki/Special:BotPasswords";
    public const string SiteUrl = "https://www.pcgamingwiki.com/";
    public const string LicenseUrl = "https://creativecommons.org/licenses/by-nc-sa/3.0/";

    private readonly IHdrListService _service;
    private readonly Action<string> _openWebPage;
    private readonly SynchronizationContext? _ui;
    private readonly AsyncRelayCommand _syncNowCommand;
    private readonly AsyncRelayCommand _saveAccountCommand;
    private readonly RelayCommand _removeAccountCommand;
    private string _password = string.Empty;
    private string _userName = string.Empty;
    private string? _accountUser;
    private bool _accountUnreadable;
    private string _statusText = string.Empty;
    private bool _hasStatusError;
    private string _modeText = string.Empty;
    private bool _isSyncing;
    private bool _isTestingAccount;
    private string? _accountMessage;
    private bool _accountMessageIsError;
    private bool _disposed;

    public HdrListViewModel(IHdrListService service, Action<string> openWebPage)
    {
        _service = service;
        _openWebPage = openWebPage;
        _ui = SynchronizationContext.Current;
        _syncNowCommand = new AsyncRelayCommand(SyncNowAsync, () => !_isSyncing);
        _saveAccountCommand = new AsyncRelayCommand(SaveAccountAsync, () => !_isTestingAccount);
        _removeAccountCommand = new RelayCommand(RemoveAccount, () => CanRemoveAccount && !_isTestingAccount);
        OpenCreateAccountCommand = new RelayCommand(() => Open(CreateAccountUrl));
        OpenBotPasswordsCommand = new RelayCommand(() => Open(BotPasswordsUrl));
        OpenSiteCommand = new RelayCommand(() => Open(SiteUrl));
        OpenLicenseCommand = new RelayCommand(() => Open(LicenseUrl));

        _service.Changed += OnServiceChanged;
        Refresh();
        _userName = _accountUser ?? string.Empty;
    }

    /// <summary>Nach erfolgreichem Speichern: das Passwortfeld soll sich leeren.</summary>
    public event EventHandler? PasswordClearRequested;

    public ICommand SyncNowCommand => _syncNowCommand;

    public ICommand SaveAccountCommand => _saveAccountCommand;

    public ICommand RemoveAccountCommand => _removeAccountCommand;

    public ICommand OpenCreateAccountCommand { get; }

    public ICommand OpenBotPasswordsCommand { get; }

    public ICommand OpenSiteCommand { get; }

    public ICommand OpenLicenseCommand { get; }

    /// <summary>„Stand: 07.10.2026 14:03, 412 von 650 Spielen geprüft“, „Abgleich läuft …“ oder mit letztem Fehler.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasStatusError
    {
        get => _hasStatusError;
        private set => SetProperty(ref _hasStatusError, value);
    }

    /// <summary>Ohne Konto (Namen werden abgefragt), mit Konto (vollständige Liste) oder Konto nicht lesbar (nichts wird abgefragt).</summary>
    public string ModeText
    {
        get => _modeText;
        private set => SetProperty(ref _modeText, value);
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        private set
        {
            if (SetProperty(ref _isSyncing, value))
            {
                _syncNowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasAccount => _accountUser is not null;

    /// <summary>Auch ein gespeichertes, aber nicht lesbares Konto lässt sich entfernen (sonst fragt der Abgleich nie wieder).</summary>
    private bool CanRemoveAccount => HasAccount || _accountUnreadable;

    /// <summary>„Gespeichert: Name@HdrPilot …“; null ohne Konto.</summary>
    public string? AccountUserText => _accountUser is null ? null : string.Format(CultureInfo.CurrentCulture, ShellStrings.PcgwAccountStored, _accountUser);

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value ?? string.Empty);
    }

    public bool IsTestingAccount
    {
        get => _isTestingAccount;
        private set
        {
            if (SetProperty(ref _isTestingAccount, value))
            {
                _saveAccountCommand.RaiseCanExecuteChanged();
                _removeAccountCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? AccountMessage
    {
        get => _accountMessage;
        private set
        {
            if (SetProperty(ref _accountMessage, value))
            {
                OnPropertyChanged(nameof(HasAccountMessage));
            }
        }
    }

    public bool AccountMessageIsError
    {
        get => _accountMessageIsError;
        private set => SetProperty(ref _accountMessageIsError, value);
    }

    public bool HasAccountMessage => !string.IsNullOrEmpty(_accountMessage);

    /// <summary>Vom Passwortfeld (PasswordChanged); der Wert wird nie zurückgegeben.</summary>
    public void SetPassword(string? password) => _password = password ?? string.Empty;

    /// <summary>„Jetzt abgleichen“: auch innerhalb einer Stunde nach dem letzten Abgleich.</summary>
    public async Task SyncNowAsync()
    {
        IsSyncing = true;
        StatusText = ShellStrings.HdrListSyncing;
        HasStatusError = false;
        try
        {
            await _service.SyncNowAsync(force: true);
        }
        catch (OperationCanceledException)
        {
            // Programmende.
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>„Speichern und testen“: prüft Anmeldung und Cargo-Recht, speichert nur bei Erfolg.</summary>
    public async Task SaveAccountAsync()
    {
        var user = _userName.Trim();
        if (!PcgwAccount.IsValidUserName(user))
        {
            ShowAccountMessage(ShellStrings.PcgwUserInvalid, isError: true);
            return;
        }

        var reuseStored = _password.Length == 0 && string.Equals(user, _accountUser, StringComparison.Ordinal);
        if (_password.Length == 0 && !reuseStored)
        {
            ShowAccountMessage(ShellStrings.PcgwPasswordMissing, isError: true);
            return;
        }

        IsTestingAccount = true;
        ShowAccountMessage(ShellStrings.PcgwTesting, isError: false);
        try
        {
            var error = await _service.SaveAccountAsync(user, reuseStored ? null : _password);
            if (error is null)
            {
                _password = string.Empty;
                PasswordClearRequested?.Invoke(this, EventArgs.Empty);
                ShowAccountMessage(ShellStrings.PcgwSaved, isError: false);
            }
            else
            {
                ShowAccountMessage(AccountErrorText(error.Value), isError: true);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            ShowAccountMessage(ShellStrings.PcgwUnexpected, isError: true);
        }
        catch (OperationCanceledException)
        {
            // Programmende.
        }
        finally
        {
            IsTestingAccount = false;
            Refresh();
        }
    }

    public void RemoveAccount()
    {
        try
        {
            _service.RemoveAccount();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowAccountMessage(string.Format(CultureInfo.CurrentCulture, ShellStrings.PcgwRemoveFailed, ex.Message), isError: true);
            return;
        }

        _password = string.Empty;
        PasswordClearRequested?.Invoke(this, EventArgs.Empty);
        UserName = string.Empty;
        ShowAccountMessage(ShellStrings.PcgwRemoved, isError: false);
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _service.Changed -= OnServiceChanged;
        PasswordClearRequested = null;
        _password = string.Empty;
    }

    /// <summary>Kurzer Grund für die Statuszeile.</summary>
    public static string ErrorText(PcgwErrorKind error) => error switch
    {
        PcgwErrorKind.Network => ShellStrings.HdrListErrorNetwork,
        PcgwErrorKind.RateLimited => ShellStrings.HdrListErrorRateLimited,
        PcgwErrorKind.HttpError => ShellStrings.HdrListErrorHttp,
        PcgwErrorKind.LoginFailed => ShellStrings.HdrListErrorLoginFailed,
        PcgwErrorKind.LoginThrottled => ShellStrings.HdrListErrorLoginThrottled,
        PcgwErrorKind.PermissionDenied => ShellStrings.HdrListErrorPermission,
        PcgwErrorKind.AccountUnreadable => ShellStrings.HdrListErrorAccountUnreadable,
        PcgwErrorKind.AccountNotSaved => ShellStrings.HdrListErrorAccountNotSaved,
        _ => ShellStrings.HdrListErrorUnexpected,
    };

    /// <summary>Ausführliche Meldung nach „Speichern und testen“.</summary>
    public static string AccountErrorText(PcgwErrorKind error) => error switch
    {
        PcgwErrorKind.LoginFailed => ShellStrings.PcgwLoginFailed,
        PcgwErrorKind.LoginThrottled => ShellStrings.PcgwLoginThrottled,
        PcgwErrorKind.PermissionDenied => ShellStrings.PcgwPermissionDenied,
        PcgwErrorKind.RateLimited => ShellStrings.PcgwRateLimited,
        PcgwErrorKind.Network or PcgwErrorKind.HttpError => ShellStrings.PcgwUnreachable,
        PcgwErrorKind.AccountNotSaved => ShellStrings.PcgwNotSaved,
        _ => ShellStrings.PcgwUnexpected,
    };

    /// <summary>Stand neu lesen (Zwischenspeicher, Konto, läuft gerade ein Abgleich).</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        HdrListStatus status;
        try
        {
            status = _service.GetStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = ShellStrings.HdrListNever;
            HasStatusError = false;
            return;
        }

        var couldRemove = CanRemoveAccount;
        _accountUser = status.AccountUser;
        _accountUnreadable = status.AccountUnreadable;
        if (couldRemove != CanRemoveAccount)
        {
            _removeAccountCommand.RaiseCanExecuteChanged();
        }

        OnPropertyChanged(nameof(HasAccount));
        OnPropertyChanged(nameof(AccountUserText));
        ModeText = _accountUser is not null
            ? string.Format(CultureInfo.CurrentCulture, ShellStrings.HdrListModeAccount, _accountUser)
            : _accountUnreadable ? ShellStrings.HdrListModeAccountUnreadable : ShellStrings.HdrListModeAnonymous;
        IsSyncing = status.IsRunning;
        if (status.IsRunning)
        {
            StatusText = ShellStrings.HdrListSyncing;
            HasStatusError = false;
            return;
        }

        var stand = status.LastSuccessAt is { } at
            ? string.Format(CultureInfo.CurrentCulture, ShellStrings.HdrListStand, at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), status.CheckedGames, status.TotalGames)
            : ShellStrings.HdrListNever;
        HasStatusError = status.LastError is not null;
        StatusText = status.LastError is { } error
            ? string.Format(CultureInfo.CurrentCulture, ShellStrings.HdrListStandWithError, stand.TrimEnd('.'), ErrorText(error))
            : stand;
    }

    private void ShowAccountMessage(string message, bool isError)
    {
        AccountMessageIsError = isError;
        AccountMessage = message;
    }

    private void Open(string url)
    {
        try
        {
            _openWebPage(url);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or FileNotFoundException)
        {
            ShowAccountMessage(string.Format(CultureInfo.CurrentCulture, ShellStrings.PcgwOpenFailed, ex.Message), isError: true);
        }
    }

    // Kommt vom Thread des Abgleichs.
    private void OnServiceChanged(object? sender, EventArgs e)
    {
        if (_ui is null)
        {
            Refresh();
        }
        else
        {
            _ui.Post(_ => Refresh(), null);
        }
    }
}
