using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Util;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Dialog „Spiel hinzufügen“: EXE oder Ordner, Name frei wählbar. Prüft den Pfad mit eigenen, übersetzbaren
/// Meldungen, warnt vor sehr weiten Ordnern und legt den Eintrag über <see cref="ManualGames.Create"/> an.
/// </summary>
public sealed class AddGameViewModel : ObservableObject
{
    private readonly IFileSystem _fileSystem;
    private readonly IAppEnvironment _environment;
    private readonly ILibraryDialogs _dialogs;
    private string _path = string.Empty;
    private string _name = string.Empty;
    private string? _error;

    public AddGameViewModel(IFileSystem fileSystem, IAppEnvironment environment, ILibraryDialogs dialogs)
    {
        _fileSystem = fileSystem;
        _environment = environment;
        _dialogs = dialogs;
        BrowseExeCommand = new RelayCommand(BrowseExe);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        ConfirmCommand = new RelayCommand(() =>
        {
            if (TryConfirm())
            {
                CloseRequested?.Invoke(this, true);
            }
        });
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
    }

    /// <summary>Der Dialog soll schließen (true: übernommen).</summary>
    public event EventHandler<bool>? CloseRequested;

    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value ?? string.Empty))
            {
                Error = null;
                OnPropertyChanged(nameof(SuggestedName));
                OnPropertyChanged(nameof(NamePlaceholder));
            }
        }
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    /// <summary>Name, der ohne Eingabe verwendet wird (Datei- bzw. Ordnername).</summary>
    public string SuggestedName
    {
        get
        {
            var p = PathUtil.Normalize(_path);
            if (p.Length <= 3)
            {
                return string.Empty;
            }

            return p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? System.IO.Path.GetFileNameWithoutExtension(p)
                : System.IO.Path.GetFileName(p);
        }
    }

    /// <summary>Platzhalter im Namensfeld: der vorgeschlagene Name oder ein allgemeiner Hinweis.</summary>
    public string NamePlaceholder => SuggestedName is { Length: > 0 } s ? s : LibraryStrings.AddGame_NamePlaceholder;

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>Der angelegte Eintrag nach erfolgreicher Bestätigung.</summary>
    public GameEntry? Result { get; private set; }

    public ICommand BrowseExeCommand { get; }

    public ICommand BrowseFolderCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Prüft die Eingaben (mit Rückfrage bei weiten Ordnern) und legt den Eintrag an.</summary>
    public bool TryConfirm()
    {
        Error = null;
        Result = null;
        var path = PathUtil.Normalize(_path);
        if (string.IsNullOrWhiteSpace(path))
        {
            Error = LibraryStrings.AddGame_ErrorNoPath;
            return false;
        }

        if (!System.IO.Path.IsPathFullyQualified(path))
        {
            Error = LibraryStrings.AddGame_ErrorRelative;
            return false;
        }

        var isFile = _fileSystem.FileExists(path);
        var isFolder = !isFile && _fileSystem.DirectoryExists(path);
        if (!isFile && !isFolder)
        {
            Error = LibraryStrings.AddGame_ErrorNotFound;
            return false;
        }

        if (isFile && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Error = LibraryStrings.AddGame_ErrorNotExe;
            return false;
        }

        var folder = isFile ? PathUtil.Normalize(System.IO.Path.GetDirectoryName(path) ?? string.Empty) : path;
        if (!ConfirmFolder(folder, _environment, _dialogs, out var refused))
        {
            Error = refused;
            return false;
        }

        try
        {
            Result = ManualGames.Create(_fileSystem, path, string.IsNullOrWhiteSpace(_name) ? null : _name);
            return true;
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Prüft einen künftigen Installationsordner: Laufwerkswurzel wird abgelehnt (Meldung in <paramref name="refusal"/>),
    /// sehr weite Ordner nur nach Rückfrage. True, wenn der Ordner verwendet werden darf.
    /// </summary>
    public static bool ConfirmFolder(string folder, IAppEnvironment environment, ILibraryDialogs dialogs, out string? refusal)
    {
        refusal = null;
        switch (FolderSafety.Classify(folder, environment))
        {
            case FolderRisk.DriveRoot:
                refusal = LibraryStrings.AddGame_ErrorDriveRoot;
                return false;
            case FolderRisk.Wide:
                return dialogs.Confirm(
                    LibraryStrings.WideFolder_Title,
                    LibraryText.Format(LibraryStrings.WideFolder_Text, folder),
                    LibraryStrings.WideFolder_Confirm,
                    isDanger: true);
            default:
                return true;
        }
    }

    private void BrowseExe()
    {
        var picked = _dialogs.PickExecutable(InitialDirectory());
        if (!string.IsNullOrEmpty(picked))
        {
            Path = picked;
        }
    }

    private void BrowseFolder()
    {
        var picked = _dialogs.PickFolder(InitialDirectory());
        if (!string.IsNullOrEmpty(picked))
        {
            Path = picked;
        }
    }

    private string? InitialDirectory()
    {
        var p = PathUtil.Normalize(_path);
        if (p.Length == 0)
        {
            return null;
        }

        if (_fileSystem.DirectoryExists(p))
        {
            return p;
        }

        var parent = System.IO.Path.GetDirectoryName(p);
        return parent is not null && _fileSystem.DirectoryExists(parent) ? parent : null;
    }
}
