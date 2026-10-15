using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.Games;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Eintrag im Quellen-Filter: „Alle“ oder eine Quelle mit Anzahl.</summary>
public sealed class SourceFilterOption : ObservableObject
{
    private int _count;

    public SourceFilterOption(GameSource? source, int count)
    {
        Source = source;
        Label = source is { } s ? LibraryText.SourceName(s) : LibraryStrings.Filter_All;
        _count = count;
    }

    /// <summary>Null für „Alle“.</summary>
    public GameSource? Source { get; }

    public string Label { get; }

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    /// <summary>Name für Bildschirmleser und UI-Automation: „Alle“ bzw. „Steam (631)“.</summary>
    public string AutomationName => Source is null ? Label : LibraryText.Format(LibraryStrings.Filter_SourceWithCount, Label, Count);

    public override string ToString() => AutomationName;
}

/// <summary>Bildschirm-Auswahl je Spiel: globale Einstellung oder ein bestimmter Bildschirm.</summary>
public sealed class DisplayOptionViewModel
{
    public DisplayOptionViewModel(string? devicePath, string label, string? detail, bool isHdrCapable, bool isConnected)
    {
        DevicePath = devicePath;
        Label = label;
        Detail = detail;
        IsHdrCapable = isHdrCapable;
        IsConnected = isConnected;
    }

    /// <summary>monitorDevicePath; null für die globale Einstellung.</summary>
    public string? DevicePath { get; }

    public bool IsGlobal => DevicePath is null;

    public string Label { get; }

    /// <summary>Zusatz wie „Hauptbildschirm · HDR-fähig“.</summary>
    public string? Detail { get; }

    public bool IsHdrCapable { get; }

    public bool IsConnected { get; }

    public override string ToString() => string.IsNullOrEmpty(Detail) ? Label : LibraryText.Format(LibraryStrings.Display_LabelWithDetail, Label, Detail);
}

public enum ExecutableOrigin
{
    /// <summary>Beim ersten Spielen erkannt (DetectedExecutables).</summary>
    Detected,

    /// <summary>Vom Launcher genannt (KnownExecutables).</summary>
    Known,

    /// <summary>Vom Nutzer ergänzt (ExtraExecutables).</summary>
    Extra,

    /// <summary>Nur in der Ausschlussliste.</summary>
    ExcludedOnly,
}

/// <summary>Eine EXE im Detailbereich mit Herkunft; ausgeschlossene werden gedimmt gezeigt.</summary>
public sealed class ExecutableItemViewModel
{
    public ExecutableItemViewModel(string path, ExecutableOrigin origin, bool isExcluded, bool isHelper = false)
    {
        Path = path;
        Origin = origin;
        IsExcluded = isExcluded;
        IsHelper = isHelper && !isExcluded;
        FileName = System.IO.Path.GetFileName(path);
        Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        OriginText = isExcluded
            ? LibraryStrings.Exe_OriginExcluded
            : IsHelper
                ? LibraryStrings.Exe_OriginHelper
                : origin switch
                {
                    ExecutableOrigin.Detected => LibraryStrings.Exe_OriginDetected,
                    ExecutableOrigin.Known => LibraryStrings.Exe_OriginKnown,
                    ExecutableOrigin.Extra => LibraryStrings.Exe_OriginExtra,
                    _ => LibraryStrings.Exe_OriginExcluded,
                };
    }

    /// <summary>Steht auf der globalen Ausschlussliste (Launcher-Fenster, Absturzmelder): zählt nie als Spiel.</summary>
    public bool IsHelper { get; }

    /// <summary>Gedimmt anzeigen: ausgeschlossen oder Hilfsprogramm.</summary>
    public bool IsInactive => IsExcluded || IsHelper;

    /// <summary>Pfad oder (bei Einträgen von Hand) nur der Dateiname.</summary>
    public string Path { get; }

    public string FileName { get; }

    public string Folder { get; }

    /// <summary>Ordner kurz: innerhalb des Installationsordners „…\FunkTag\Binaries\Win64“, sonst der volle Ordner.</summary>
    public string ShortFolder { get; init; } = string.Empty;

    /// <summary>Ordner für die Anzeige (kurz, wenn bekannt).</summary>
    public string DisplayFolder => string.IsNullOrEmpty(ShortFolder) ? Folder : ShortFolder;

    /// <summary>
    /// Kurzform eines EXE-Ordners relativ zum Installationsordner (Prototyp exeDir): „…\&lt;letzter Teil des Spielordners&gt;\Rest“.
    /// </summary>
    public static string Shorten(string folder, string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir) || string.IsNullOrEmpty(folder))
        {
            return folder;
        }

        var dir = installDir.TrimEnd('\\', '/');
        if (!folder.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || (folder.Length > dir.Length && folder[dir.Length] is not ('\\' or '/')))
        {
            return folder;
        }

        var last = System.IO.Path.GetFileName(dir);
        return string.IsNullOrEmpty(last) ? folder : "…\\" + last + folder[dir.Length..];
    }

    public ExecutableOrigin Origin { get; }

    public bool IsExcluded { get; }

    public string OriginText { get; }

    public override string ToString() => FileName;
}

/// <summary>Vorschlag aus einem Suchordner. Wird nur auf Knopfdruck übernommen.</summary>
public sealed class CandidateItemViewModel(GameCandidate candidate)
{
    public GameCandidate Candidate { get; } = candidate;

    public string Name => Candidate.Name;

    public string Folder => Candidate.Folder;

    public string ExecutablesText => Candidate.Executables.Count == 1
        ? System.IO.Path.GetFileName(Candidate.Executables[0])
        : LibraryText.Format(LibraryStrings.Candidate_ExeCount, Candidate.Executables.Count);

    /// <summary>„E:\Spiele\Hades · Hades.exe“.</summary>
    public string DetailText => LibraryText.Format(LibraryStrings.Candidate_Detail, Folder, ExecutablesText);

    public override string ToString() => Name;
}

/// <summary>Fehler eines Scanners beim letzten Einlesen.</summary>
public sealed class ScanErrorItem(string scanner, string message, string? path)
{
    public string Scanner { get; } = scanner;

    public string Message { get; } = message;

    public string? Path { get; } = path;

    public bool HasPath => !string.IsNullOrEmpty(Path);

    /// <summary>„Steam: appmanifest_5.acf ist defekt“.</summary>
    public string Text => LibraryText.Format(LibraryStrings.ScanErrors_Item, Scanner, Message);

    public override string ToString() => Text;
}

public enum StatusKind
{
    Info,
    Success,
    Warning,
    Error,
}
