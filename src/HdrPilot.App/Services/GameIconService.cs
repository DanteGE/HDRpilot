using System.IO;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Util;

namespace HdrPilot.App.Services;

/// <summary>
/// Symbole aus den EXE-Dateien der Spiele. Geladen wird im Hintergrund mit begrenzter Parallelität, zuletzt
/// angefragte zuerst (die gerade sichtbaren Zeilen). Bilder sind in Zielgröße dekodiert und eingefroren.
/// Der Zwischenspeicher gehört zum Fenster und wird beim Schließen geleert (<see cref="ClearCache"/>).
/// </summary>
public sealed class GameIconService : IGameIconService
{
    /// <summary>Kantenlänge der Symbole in Pixeln (Liste 32, Detailbereich 48).</summary>
    public const int IconSize = 48;

    private readonly IconExecutableResolver _resolver;
    private readonly Func<string, ImageSource?> _extract;
    private readonly Func<IReadOnlyList<string>?> _helperPatterns;
    private readonly int _maxParallel;
    private readonly ConcurrentDictionary<string, string?> _exeByGame = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageSource?> _iconByExe = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<WorkItem> _queue = new();
    private readonly object _gate = new();
    private int _workers;
    private int _generation;

    /// <param name="fileSystem">Dateizugriff für die EXE-Wahl.</param>
    /// <param name="helperPatterns">Ausschlussliste aus der Konfiguration (null: Standardliste).</param>
    /// <param name="fileLength">Dateigröße (Standard: echte Datei).</param>
    /// <param name="extract">Symbol aus einer EXE (Standard: <see cref="ExtractIcon"/>).</param>
    public GameIconService(
        IFileSystem fileSystem,
        Func<IReadOnlyList<string>?>? helperPatterns = null,
        Func<string, long>? fileLength = null,
        Func<string, ImageSource?>? extract = null,
        int maxParallel = 2)
    {
        _resolver = new IconExecutableResolver(fileSystem, fileLength);
        _helperPatterns = helperPatterns ?? (() => null);
        _extract = extract ?? (path => ExtractIcon(path, IconSize));
        _maxParallel = Math.Max(1, maxParallel);
    }

    public ImageSource? GetCachedIcon(GameConfig game) =>
        _exeByGame.TryGetValue(game.Key, out var exe) && exe is not null && _iconByExe.TryGetValue(exe, out var icon) ? icon : null;

    public Task<ImageSource?> GetIconAsync(GameConfig game, CancellationToken cancellationToken = default)
    {
        if (_exeByGame.TryGetValue(game.Key, out var exe) && (exe is null || _iconByExe.ContainsKey(exe)))
        {
            return Task.FromResult(exe is null ? null : _iconByExe.GetValueOrDefault(exe));
        }

        return _inflight.GetOrAdd(game.Key, _ => Enqueue(game, cancellationToken));
    }

    /// <summary>Gibt alle zwischengespeicherten Symbole frei (Fenster geschlossen).</summary>
    public void ClearCache()
    {
        lock (_gate)
        {
            _generation++;
            while (_queue.Count > 0)
            {
                _queue.Pop().Completion.TrySetResult(null);
            }
        }

        _exeByGame.Clear();
        _iconByExe.Clear();
        _inflight.Clear();
    }

    /// <summary>
    /// Symbol einer EXE in Zielgröße als eingefrorenes Bild; null, wenn die Datei keines enthält (dann zeigt die
    /// Liste das Ersatzsymbol der Quelle statt des allgemeinen Windows-Programmsymbols).
    /// <see cref="System.Drawing.Icon.ExtractIcon(string, int, int)"/> liefert die gewünschte Größe direkt (statt
    /// fester 32 px von ExtractAssociatedIcon). Das Icon besitzt sein Handle: Dispose ruft DestroyIcon.
    /// </summary>
    public static ImageSource? ExtractIcon(string exePath, int size)
    {
        System.Drawing.Icon? icon;
        try
        {
            icon = System.Drawing.Icon.ExtractIcon(exePath, 0, size);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or ExternalException)
        {
            return null;
        }

        if (icon is null)
        {
            return null;
        }

        using (icon)
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
            source.Freeze();
            return source;
        }
    }

    private Task<ImageSource?> Enqueue(GameConfig game, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _queue.Push(new WorkItem(game, completion, cancellationToken, _generation));
            if (_workers < _maxParallel)
            {
                _workers++;
                _ = Task.Factory.StartNew(Work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            }
        }

        return completion.Task;
    }

    private void Work()
    {
        while (true)
        {
            WorkItem item;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    _workers--;
                    return;
                }

                item = _queue.Pop();
            }

            try
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Completion.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                item.Completion.TrySetResult(Load(item));
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
            }
            finally
            {
                _inflight.TryRemove(item.Game.Key, out _);
            }
        }
    }

    private ImageSource? Load(WorkItem item)
    {
        var exe = _resolver.Resolve(item.Game, _helperPatterns());
        ImageSource? icon = null;
        if (exe is not null && !_iconByExe.TryGetValue(exe, out icon))
        {
            icon = _extract(exe);
        }

        if (Volatile.Read(ref _generation) == item.Generation)
        {
            _exeByGame[item.Game.Key] = exe;
            if (exe is not null)
            {
                _iconByExe[exe] = icon;
            }
        }

        return icon;
    }

    private sealed record WorkItem(GameConfig Game, TaskCompletionSource<ImageSource?> Completion, CancellationToken CancellationToken, int Generation);
}

/// <summary>
/// Wählt die EXE, deren Symbol ein Spiel zeigt: gemerkte EXEs, dann vom Launcher genannte (jeweils die größte
/// vorhandene), sonst die größte EXE im Installationsordner bis eine Ebene tief. Hilfsprogramme und vom Nutzer
/// ausgeschlossene EXEs zählen nie. Die Ordnersuche ist begrenzt (Steam nennt keine EXEs).
/// </summary>
public sealed class IconExecutableResolver(IFileSystem fileSystem, Func<string, long>? fileLength = null)
{
    /// <summary>Höchstens so viele Unterordner werden durchsucht.</summary>
    public const int MaxSubdirectories = 32;

    /// <summary>Höchstens so viele EXEs werden je Spiel verglichen.</summary>
    public const int MaxCandidates = 64;

    private readonly Func<string, long> _fileLength = fileLength ?? RealFileLength;

    public string? Resolve(GameConfig game, IReadOnlyList<string>? helperPatterns = null)
    {
        bool Usable(string path) =>
            !string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && Path.IsPathFullyQualified(path)
            && !HelperExecutables.IsHelper(path, helperPatterns)
            && !game.ExcludedExecutables.Any(x => IsExcludedBy(x, path))
            && SafeFileExists(path);

        var chosen = Largest(game.DetectedExecutables.Select(PathUtil.Normalize).Where(Usable))
            ?? Largest(game.KnownExecutables.Select(PathUtil.Normalize).Where(Usable));
        if (chosen is not null)
        {
            return chosen;
        }

        var dir = PathUtil.Normalize(game.InstallDir);
        if (dir.Length <= 3 || !Path.IsPathFullyQualified(dir) || !SafeDirectoryExists(dir))
        {
            return null; // Nie eine Laufwerkswurzel durchsuchen.
        }

        var candidates = new List<string>(SafeExecutables(dir));
        foreach (var sub in SafeDirectories(dir).Take(MaxSubdirectories))
        {
            if (candidates.Count >= MaxCandidates)
            {
                break;
            }

            candidates.AddRange(SafeExecutables(sub));
        }

        return Largest(candidates.Take(MaxCandidates).Select(PathUtil.Normalize).Where(Usable));
    }

    private static bool IsExcludedBy(string entry, string path) =>
        !string.IsNullOrWhiteSpace(entry) && (entry.Contains('\\') || entry.Contains('/')
            ? PathUtil.PathEquals(entry, path)
            : string.Equals(entry.Trim(), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));

    private static long RealFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    private string? Largest(IEnumerable<string> paths)
    {
        string? best = null;
        long bestSize = -1;
        foreach (var path in paths)
        {
            var size = SafeLength(path);
            if (size > bestSize)
            {
                best = path;
                bestSize = size;
            }
        }

        return best;
    }

    private long SafeLength(string path)
    {
        try
        {
            return _fileLength(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    private bool SafeFileExists(string path)
    {
        try
        {
            return fileSystem.FileExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private bool SafeDirectoryExists(string path)
    {
        try
        {
            return fileSystem.DirectoryExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private List<string> SafeExecutables(string dir)
    {
        try
        {
            return fileSystem.EnumerateFiles(dir, "*.exe").Take(MaxCandidates).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return [];
        }
    }

    private List<string> SafeDirectories(string dir)
    {
        try
        {
            return fileSystem.EnumerateDirectories(dir).Take(MaxSubdirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return [];
        }
    }
}
