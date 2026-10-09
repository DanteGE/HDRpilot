using System.IO;

namespace HdrPilot.App.Setup;

/// <summary>
/// Ersetzt Dateien in zwei Phasen: erst alle als „.new“ ablegen (<see cref="Stage"/>), dann jede ersetzen, die bisherige
/// bleibt als „.old“ liegen (<see cref="CommitAll"/>). Bis <see cref="Complete"/> lässt sich alles zurücknehmen
/// (<see cref="Rollback"/>): die vorige Installation bleibt lauffähig. Erst danach werden die „.old“-Dateien gelöscht.
/// </summary>
internal sealed class SetupFileTransaction(ISetupFileOperations files, SetupLog log)
{
    public const string NewSuffix = ".new";
    public const string OldSuffix = ".old";

    private readonly List<string> _staged = [];
    private readonly List<(string Target, bool HadPrevious)> _replaced = [];
    private (string Target, bool HadPrevious)? _inProgress;

    /// <summary>True, wenn der letzte Fehler beim Ersetzen einer vorhandenen Datei kam (nicht beim Ablegen).</summary>
    public bool FailedOnExistingFile { get; private set; }

    /// <summary>Datei, an der gerade gearbeitet wird (für Fehlermeldungen).</summary>
    public string? CurrentFile { get; private set; }

    /// <summary>Schreibt <c>&lt;target&gt;.new</c> und merkt ihn für das Ersetzen vor.</summary>
    public void Stage(string target, Action<Stream> write)
    {
        _staged.Add(target);
        CurrentFile = target + NewSuffix;
        using var stream = files.Create(target + NewSuffix);
        write(stream);
        if (stream is FileStream file)
        {
            file.Flush(flushToDisk: true);
        }
    }

    /// <summary>Ersetzt alle vorgemerkten Dateien in der Reihenfolge des Ablegens.</summary>
    public void CommitAll()
    {
        foreach (var target in _staged)
        {
            var existed = File.Exists(target);
            FailedOnExistingFile = existed;
            CurrentFile = target;
            _inProgress = (target, existed);
            if (existed)
            {
                files.Delete(target + OldSuffix); // Rest eines abgebrochenen Laufs
                files.Replace(target + NewSuffix, target, target + OldSuffix);
            }
            else
            {
                files.Move(target + NewSuffix, target);
            }

            _replaced.Add((target, existed));
            _inProgress = null;
            log.Info((existed ? "Ersetzt: " : "Neu: ") + target);
        }

        FailedOnExistingFile = false;
    }

    /// <summary>Nimmt alles zurück: bisherige Dateien wieder an ihren Platz, neue und „.new“ weg. Wirft nicht.</summary>
    public void Rollback()
    {
        // Scheitert ReplaceFile mitten drin, kann die bisherige Datei schon „.old“ heißen (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2).
        if (_inProgress is { HadPrevious: true } current && !File.Exists(current.Target) && File.Exists(current.Target + OldSuffix))
        {
            Undo(current.Target, hadPrevious: true);
        }

        for (var i = _replaced.Count - 1; i >= 0; i--)
        {
            Undo(_replaced[i].Target, _replaced[i].HadPrevious);
        }

        foreach (var target in _staged)
        {
            TryDelete(target + NewSuffix);
        }

        _replaced.Clear();
        _staged.Clear();
        _inProgress = null;
    }

    /// <summary>Abschluss: „.old“-Dateien löschen (Fehler nur protokollieren, etwa eine noch laufende alte Kommandozeile).</summary>
    public void Complete()
    {
        foreach (var (target, hadPrevious) in _replaced)
        {
            if (hadPrevious)
            {
                TryDelete(target + OldSuffix);
            }
        }

        _replaced.Clear();
        _staged.Clear();
    }

    private void Undo(string target, bool hadPrevious)
    {
        try
        {
            if (hadPrevious)
            {
                files.Move(target + OldSuffix, target);
            }
            else
            {
                files.Delete(target);
            }

            log.Info("Zurückgenommen: " + target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error("Zurücknehmen fehlgeschlagen: " + target, ex);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            files.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn("Nicht gelöscht: " + path + " (" + ex.Message + ")");
        }
    }
}
