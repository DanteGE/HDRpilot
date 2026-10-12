using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services;

/// <summary>Woher das Symbol eines Spiels stammt.</summary>
public enum IconOrigin
{
    /// <summary>Eigenes Symbol (Rechtsklick „Symbol ändern…“).</summary>
    Custom,

    /// <summary>Symbol-Hinweis des Launchers (<see cref="GameConfig.IconCandidates"/>), etwa das offizielle Steam-Symbol.</summary>
    Launcher,

    /// <summary>Symbol der Spiel-EXE (<see cref="IconExecutableResolver"/>).</summary>
    Executable,
}

/// <summary>Gewähltes Symbol eines Spiels: Datei, Art der Quelle und das gelesene Symbol.</summary>
public sealed record IconSelection(string Path, IconOrigin Origin, IconImage Icon);

/// <summary>
/// Symbole der Spiele (E86). Je Spiel gilt die erste Quelle, die taugt:
/// <list type="number">
/// <item>ein eigenes Symbol (Rechtsklick „Symbol ändern…“, PNG im Datenordner), solange die Datei lesbar ist;</item>
/// <item>die Symbol-Hinweise des Launchers in ihrer Reihenfolge (<see cref="GameConfig.IconCandidates"/>: Steam das
/// offizielle Steam-Symbol, GOG goggame-ID.ico und Galaxy-Symbol, Xbox-Logos, Ubisoft DisplayIcon);</item>
/// <item>die EXE des Spiels (<see cref="IconExecutableResolver"/>), nur gesucht, wenn kein Hinweis taugt.</item>
/// </list>
/// „Taugt“ heißt vorhanden, kein bekanntes Standardsymbol (<see cref="GenericIconDetector"/>) und in der Datei mindestens
/// 48 px groß (<see cref="IconImage.IsGood"/>). Taugt keine Quelle, gewinnt die erste vorhandene ohne Standardsymbol, notfalls
/// ein Standardsymbol. Für Steam heißt das: das offizielle Symbol zuerst; die EXE nur, wenn es fehlt, ein Standardsymbol ist
/// oder kleiner als 48 px, während die EXE mindestens 48 px hat. Die tiefere EXE-Suche läuft nur, wenn es sonst gar kein
/// echtes Symbol gibt (ein richtiges 32-px-Symbol wird nie gegen eine fremde EXE aus der Tiefe getauscht).
/// <para>
/// Geladen wird im Hintergrund mit begrenzter Parallelität, zuletzt angefragte zuerst (die gerade sichtbaren Zeilen);
/// Bilder sind in Zielgröße dekodiert und eingefroren, Fehler (fehlender WebP-Decoder, unlesbare Datei) gelten als
/// „kein Symbol“. Der Zwischenspeicher gehört zum Fenster und wird beim Schließen geleert (<see cref="ClearCache"/>);
/// die gewählte Quelle merkt sich <see cref="IconChoiceCache"/> über Neustarts hinweg.
/// </para>
/// </summary>
public sealed class GameIconService : IGameIconService
{
    /// <summary>Kantenlänge der Symbole in Pixeln (Liste 32, Detailbereich 48).</summary>
    public const int IconSize = 48;

    /// <summary>
    /// Eine gemerkte Wahl ohne gutes Symbol (keins, Standardsymbol oder unter 48 px) wird nach dieser Zeit neu geprüft: Ein
    /// Update des Spiels kann eine bessere EXE bringen, ohne dass sich die Eingaben des Spiels ändern.
    /// </summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(7);

    private readonly IconExecutableResolver _resolver;
    private readonly Func<string, IconImage?> _read;
    private readonly Func<IReadOnlyList<string>?> _helperPatterns;
    private readonly Func<string, string?> _fileStamp;
    private readonly Func<DateTimeOffset> _now;
    private readonly CustomIconStore? _customIcons;
    private readonly IconChoiceCache _choices;
    private readonly ILogger _logger;
    private readonly int _maxParallel;
    private readonly ConcurrentDictionary<string, CachedIcon> _byGame = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IconImage> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _epochs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<WorkItem> _queue = new();
    private readonly object _gate = new();
    private int _workers;
    private int _generation;

    /// <param name="fileSystem">Dateizugriff für die EXE-Wahl und den gemerkten Stand.</param>
    /// <param name="helperPatterns">Ausschlussliste aus der Konfiguration (null: Standardliste).</param>
    /// <param name="fileLength">Dateigröße (Standard: echte Datei).</param>
    /// <param name="extract">Nur Bild aus einer Datei (Tests); gilt als scharf und nicht als Standardsymbol.</param>
    /// <param name="maxParallel">Höchstens so viele Symbole gleichzeitig.</param>
    /// <param name="read">Symbol samt Größe und Standardsymbol-Erkennung (Standard: <see cref="IconReader"/>).</param>
    /// <param name="iconDirectory">Ordner der eigenen Symbole (null: keine eigenen Symbole).</param>
    /// <param name="choiceCacheFile">Datei der gemerkten Wahl (null: nichts merken).</param>
    /// <param name="fileStamp">Größe und Änderungszeit einer Datei als Text, null wenn sie fehlt (Standard: echte Datei).</param>
    /// <param name="now">Uhr (Standard: jetzt).</param>
    /// <param name="deepSearchBudget">Zeitgrenze der tieferen EXE-Suche (Standard 1 s).</param>
    /// <param name="logger">Protokoll (nur Debug-Meldungen).</param>
    public GameIconService(
        IFileSystem fileSystem,
        Func<IReadOnlyList<string>?>? helperPatterns = null,
        Func<string, long>? fileLength = null,
        Func<string, ImageSource?>? extract = null,
        int maxParallel = 2,
        Func<string, IconImage?>? read = null,
        string? iconDirectory = null,
        string? choiceCacheFile = null,
        Func<string, string?>? fileStamp = null,
        Func<DateTimeOffset>? now = null,
        TimeSpan? deepSearchBudget = null,
        ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _resolver = new IconExecutableResolver(fileSystem, fileLength, deepSearchBudget);
        _helperPatterns = helperPatterns ?? (() => null);
        var reader = new IconReader(logger: _logger);
        _read = read
            ?? (extract is null ? path => reader.Read(path) : path => extract(path) is { } image ? new IconImage(image, IconImage.SharpSize, false) : null);
        _fileStamp = fileStamp ?? RealFileStamp;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _customIcons = iconDirectory is null ? null : new CustomIconStore(iconDirectory);
        _choices = new IconChoiceCache(fileSystem, choiceCacheFile, _logger);
        _maxParallel = Math.Max(1, maxParallel);
    }

    public ImageSource? GetCachedIcon(GameConfig game) =>
        _byGame.TryGetValue(game.Key, out var cached) && cached.Signature == MemorySignature(game) ? cached.Icon : null;

    public Task<ImageSource?> GetIconAsync(GameConfig game, CancellationToken cancellationToken = default)
    {
        var signature = MemorySignature(game);
        _latest[game.Key] = signature;
        if (_byGame.TryGetValue(game.Key, out var cached) && cached.Signature == signature)
        {
            return Task.FromResult(cached.Icon);
        }

        var key = game.Key + "|" + signature;
        return _inflight.GetOrAdd(key, _ => Enqueue(game, key, signature, cancellationToken));
    }

    /// <summary>Gibt alle zwischengespeicherten Symbole frei (Fenster geschlossen); die gemerkte Wahl wird geschrieben.</summary>
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

        _byGame.Clear();
        _byPath.Clear();
        _inflight.Clear();
        _latest.Clear();
        _ = Task.Run(_choices.Release);
    }

    /// <summary>Vergisst das Symbol eines Spiels (nach „Symbol ändern…“ oder „Symbol zurücksetzen“); die nächste Anfrage lädt neu.</summary>
    public void Forget(string gameKey)
    {
        _epochs.AddOrUpdate(gameKey, 1, (_, epoch) => epoch + 1);
        _byGame.TryRemove(gameKey, out _);
    }

    /// <summary>Erzeugt das eigene Symbol aus einer Bild- oder EXE-Datei; liefert den Namen für <see cref="GameConfig.CustomIcon"/>.</summary>
    public string SaveCustomIcon(GameConfig game, string sourcePath) =>
        (_customIcons ?? throw new InvalidOperationException("Kein Ordner für eigene Symbole festgelegt.")).Save(game.Key, sourcePath);

    /// <summary>Löscht die PNG eines eigenen Symbols (fehlt sie, passiert nichts).</summary>
    public void DeleteCustomIcon(string? fileName) => _customIcons?.Delete(fileName);

    /// <summary>
    /// Symboldatei für eine Verknüpfung „Mit HDR starten“, in derselben Reihenfolge wie die Liste, soweit eine Verknüpfung
    /// die Datei zeigen kann (.ico oder .exe):
    /// <list type="number">
    /// <item>ein eigenes Symbol als .ico neben seiner PNG (<see cref="CustomIconStore.ShortcutIcon"/>; die PNG selbst kann eine
    /// Verknüpfung nicht zeigen). „Symbol zurücksetzen“ löscht die .ico nicht, damit vorhandene Verknüpfungen ihr Symbol
    /// behalten;</item>
    /// <item>die gemerkte automatische Wahl der Bibliothek, solange sie gilt (gleiche Eingaben, Datei unverändert, Quelle
    /// erlaubt; dafür wird kein Symbol gelesen);</item>
    /// <item>sonst (etwa ohne icon-cache.json oder nach geänderten Eingaben, während ein eigenes Symbol galt) dieselbe Wahl wie
    /// in der Liste ohne tiefere Suche, also etwa das offizielle Steam-Symbol;</item>
    /// <item>ist die Wahl ein Bild (Xbox-Logo, WebP von GOG Galaxy, librarycache), die EXE wie in der Liste ohne tiefere
    /// Suche (<see cref="IconExecutableResolver.ResolveWithIcon"/>, höchstens <see cref="IconExecutableResolver.MaxProbes"/>
    /// Symbole).</item>
    /// </list>
    /// Null ohne Quelle (dann das Symbol von HDRpilot). Nie auf dem UI-Thread aufrufen: Hat die Liste für das Spiel nichts
    /// gemerkt, lesen die Schritte 3 und 4 bis zu 1 + <see cref="IconExecutableResolver.MaxProbes"/> Symbole.
    /// </summary>
    public string? ShortcutIconFile(GameConfig game)
    {
        if (Custom(game) is { } custom)
        {
            try
            {
                if (_customIcons!.ShortcutIcon(custom.Path) is { } ico)
                {
                    return ico;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Symbol für die Verknüpfung von {Game} nicht geschrieben, es gilt das automatische: {Message}", game.Key, ex.Message);
            }
        }

        var patterns = _helperPatterns();
        var path = _choices.Get(game.Key) is { Path: { } remembered } choice
            && choice.Signature == IconSignature.Of(game, patterns)
            && IsAllowedSource(game, remembered, patterns)
            && string.Equals(_fileStamp(remembered), choice.Stamp, StringComparison.Ordinal)
                ? remembered
                : Choose(game, patterns, allowDeepSearch: false)?.Path;
        if (path is not null && (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            return path;
        }

        return _resolver.ResolveWithIcon(game, patterns, ReadPath, deepSearch: false)?.Path;
    }

    /// <summary>
    /// Wählt das Symbol eines Spiels sofort auf dem aufrufenden Thread, ohne Zwischenspeicher im Fenster (Messung und
    /// Prüfung). Ohne <paramref name="remember"/> wird immer neu gewählt; mit wie in der Liste: die gemerkte Wahl gilt, eine
    /// neue wird gemerkt und gleich geschrieben. Null, wenn keine Quelle ein Symbol hergibt. Nie auf dem UI-Thread aufrufen.
    /// </summary>
    public IconSelection? Select(GameConfig game, bool remember = false)
    {
        if (!remember)
        {
            return Custom(game) ?? Choose(game, _helperPatterns());
        }

        var selection = Custom(game) ?? Automatic(game);
        _choices.Flush();
        return selection;
    }

    /// <summary>
    /// Symbol einer EXE oder .ico in Zielgröße als eingefrorenes Bild; null, wenn die Datei keines enthält (dann zeigt die
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

    private static string? RealFileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Kennung für den Zwischenspeicher im Fenster: Eingaben des Spiels, eigenes Symbol und „vergessen“-Zähler.</summary>
    private string MemorySignature(GameConfig game) =>
        IconSignature.Of(game, _helperPatterns()) + "|" + game.CustomIcon + "|" + _epochs.GetValueOrDefault(game.Key);

    private Task<ImageSource?> Enqueue(GameConfig game, string inflightKey, string signature, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _queue.Push(new WorkItem(game, inflightKey, signature, completion, cancellationToken, _generation));
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
                    break;
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
                _inflight.TryRemove(item.InflightKey, out _);
            }
        }

        // Nichts steht mehr an: gemerkte Wahl gesammelt schreiben.
        _choices.Flush();
    }

    private ImageSource? Load(WorkItem item)
    {
        if (IsOvertaken(item))
        {
            return null; // überholt: nicht mehr suchen (die Zeile verwirft das Ergebnis ohnehin)
        }

        var game = item.Game;
        var icon = (Custom(game) ?? Automatic(game, () => !IsOvertaken(item)))?.Icon.Image;
        if (!IsOvertaken(item) && MemorySignature(game) == item.Signature)
        {
            _byGame[game.Key] = new CachedIcon(item.Signature, icon);
        }

        return icon;
    }

    /// <summary>
    /// Überholt ist eine Anfrage, wenn das Fenster inzwischen zu ist oder für das Spiel inzwischen ein anderer Stand angefragt
    /// ist (etwa mit den Hinweisen aus dem ersten Einlesen nach einem Update): Die neuere Anfrage liefert das Symbol, das
    /// ältere Ergebnis würde sonst den Speicher und die gemerkte Wahl des neueren überschreiben.
    /// </summary>
    private bool IsOvertaken(WorkItem item) =>
        Volatile.Read(ref _generation) != item.Generation
        || (_latest.TryGetValue(item.Game.Key, out var latest) && latest != item.Signature);

    /// <summary>Eigenes Symbol; null, wenn keins gesetzt ist oder die Datei fehlt bzw. unlesbar ist (dann automatisch).</summary>
    private IconSelection? Custom(GameConfig game)
    {
        if (_customIcons?.PathFor(game.CustomIcon) is not { } path)
        {
            return null;
        }

        // Nie aus dem Zwischenspeicher nach Pfad: „Symbol ändern…“ ersetzt dieselbe Datei.
        if (_read(path) is { } icon)
        {
            return new IconSelection(path, IconOrigin.Custom, icon);
        }

        _logger.LogDebug("Eigenes Symbol {Path} von {Game} fehlt oder ist unlesbar, es gilt das automatische.", path, game.Key);
        return null;
    }

    /// <summary>
    /// Automatisches Symbol: die gemerkte Wahl, solange Spiel und Datei unverändert sind und die Quelle noch erlaubt ist (eine
    /// Wahl ohne gutes Symbol nur <see cref="RecheckAfter"/> lang), sonst neu wählen und merken. Nicht gemerkt wird eine Wahl
    /// ohne gutes Symbol, solange der Spielordner fehlt (nicht installiert, Laufwerk getrennt): Nach der Installation am
    /// selben Ort sind die Eingaben dieselben, und ohne Ordner kostet das erneute Wählen kaum etwas. Ebenso nicht, wenn
    /// <paramref name="stillWanted"/> meldet, dass inzwischen ein neuerer Stand angefragt ist.
    /// </summary>
    private IconSelection? Automatic(GameConfig game, Func<bool>? stillWanted = null)
    {
        var patterns = _helperPatterns();
        var signature = IconSignature.Of(game, patterns);
        var now = _now();
        if (_choices.Get(game.Key) is { } choice && choice.Signature == signature)
        {
            var fresh = now - choice.Checked < RecheckAfter && now >= choice.Checked;
            if (choice.Path is null)
            {
                if (fresh)
                {
                    return null;
                }
            }
            else if ((choice.Good || fresh)
                && IsAllowedSource(game, choice.Path, patterns)
                && string.Equals(_fileStamp(choice.Path), choice.Stamp, StringComparison.Ordinal)
                && ReadPath(choice.Path) is { } remembered
                && remembered.IsGood == choice.Good)
            {
                _byPath[choice.Path] = remembered;
                return new IconSelection(choice.Path, choice.Origin, remembered);
            }
        }

        var selection = Choose(game, patterns);
        if (stillWanted?.Invoke() == false)
        {
            return selection;
        }

        if (selection is not { Icon.IsGood: true } && !_resolver.InstallDirExists(game))
        {
            _choices.Remove(game.Key);
            return selection;
        }

        _choices.Set(game.Key, selection is null
            ? new IconChoice(signature, null, IconOrigin.Executable, null, 0, false, now)
            : new IconChoice(signature, selection.Path, selection.Origin, _fileStamp(selection.Path), selection.Icon.NativeSize, selection.Icon.IsGood, now));
        return selection;
    }

    /// <summary>
    /// Die eigentliche Wahl (siehe Klassenbeschreibung); ohne <paramref name="allowDeepSearch"/> nie die tiefere EXE-Suche
    /// (Verknüpfungen, UI-Thread). Das gewählte Bild bleibt nach Pfad im Speicher.
    /// </summary>
    private IconSelection? Choose(GameConfig game, IReadOnlyList<string>? patterns, bool allowDeepSearch = true)
    {
        IconSelection? fallback = null;
        void Consider(IconSelection selection)
        {
            if (fallback is null || (fallback.Icon.IsGeneric && !selection.Icon.IsGeneric))
            {
                fallback = selection;
            }
        }

        IconSelection? Chosen(IconSelection? selection)
        {
            if (selection is not null)
            {
                _byPath[selection.Path] = selection.Icon;
            }

            return selection;
        }

        foreach (var candidate in game.IconCandidates)
        {
            if (!IconFiles.IsReadableCandidate(candidate) || ReadPath(candidate) is not { } icon)
            {
                continue;
            }

            var hint = new IconSelection(candidate, IconOrigin.Launcher, icon);
            if (icon.IsGood)
            {
                return Chosen(hint);
            }

            Consider(hint);
        }

        // Die tiefere Suche nur, wenn noch kein echtes Symbol da ist.
        var deepSearch = allowDeepSearch && (fallback is null || fallback.Icon.IsGeneric);
        if (_resolver.ResolveWithIcon(game, patterns, ReadPath, deepSearch) is { } exe)
        {
            var selection = new IconSelection(exe.Path, IconOrigin.Executable, exe.Icon);
            if (exe.Icon.IsGood)
            {
                return Chosen(selection);
            }

            Consider(selection);
        }

        return Chosen(fallback);
    }

    /// <summary>
    /// Darf eine gemerkte Quelle (aus icon-cache.json) noch gelesen werden? Nur eine lokale, lesbare Datei
    /// (<see cref="IconFiles.IsReadableCandidate"/>, nie UNC, auch wenn config.json und icon-cache.json von Hand dorthin
    /// zeigen), die ein aktueller Hinweis des Launchers ist oder eine EXE, die die EXE-Wahl heute noch zulässt (etwa nach
    /// einer erweiterten Ausschlussliste nicht mehr).
    /// </summary>
    private bool IsAllowedSource(GameConfig game, string path, IReadOnlyList<string>? patterns) =>
        IconFiles.IsReadableCandidate(path)
        && (game.IconCandidates.Contains(path, StringComparer.OrdinalIgnoreCase) || _resolver.IsAllowed(game, path, patterns));

    private IconImage? ReadPath(string path) => _byPath.TryGetValue(path, out var cached) ? cached : _read(path);

    private sealed record CachedIcon(string Signature, ImageSource? Icon);

    private sealed record WorkItem(
        GameConfig Game, string InflightKey, string Signature, TaskCompletionSource<ImageSource?> Completion, CancellationToken CancellationToken, int Generation);
}

/// <summary>Gewählte EXE samt ihrem Symbol.</summary>
public sealed record ResolvedIcon(string Path, IconImage Icon);

/// <summary>
/// Wählt die EXE, deren Symbol ein Spiel zeigt, in Gruppen: erkannte EXEs, dann vom Launcher genannte, dann der
/// Installationsordner; je Gruppe die größte zuerst. Im Ordner kommen Unreal-Spiele zuerst mit
/// <c>[&lt;Projekt&gt;\]Binaries\Win64\*.exe</c> dran, <c>*-Shipping.exe</c> vorn (die Starter im Spielordner tragen oft nur das
/// Unreal- oder EasyAntiCheat-Symbol), danach die EXEs bis eine Ebene tief. Hilfsprogramme, vom Nutzer ausgeschlossene EXEs
/// und die Ausschlussliste nur für Symbole (<see cref="IconExcludedExecutables"/>) zählen nie. Mit Symbolprobe
/// (<see cref="ResolveWithIcon"/>; mit tieferer Suche nur auf dem Arbeits-Thread der Symbole) gilt die erste Gruppe mit einem
/// echten Symbol, darin die erste EXE mit gutem Symbol (ab 48 px, kein Standardsymbol; etwa eine 256-px-<c>*.console.exe</c>
/// von Godot hinter einer größeren EXE mit 32 px), sonst die erste ohne Standardsymbol; EXEs ohne Symbol oder mit
/// Standardsymbol werden übersprungen, solange es eine andere gibt. Hat keine EXE ein echtes Symbol, wird als Ersatz bis drei
/// Ebenen tief gesucht, begrenzt auf
/// <see cref="DeepSearchMaxDirectories"/> Ordner und 1 s (gemessen ohne Grenze: im Mittel 66 ms, schlimmstenfalls 12 s).
/// </summary>
public sealed class IconExecutableResolver(IFileSystem fileSystem, Func<string, long>? fileLength = null, TimeSpan? deepSearchBudget = null)
{
    /// <summary>Höchstens so viele Unterordner werden durchsucht.</summary>
    public const int MaxSubdirectories = 32;

    /// <summary>Höchstens so viele EXEs werden je Spiel verglichen.</summary>
    public const int MaxCandidates = 64;

    /// <summary>Tiefe der Ersatzsuche unter dem Installationsordner.</summary>
    public const int DeepSearchDepth = 3;

    /// <summary>Höchstens so viele Ordner besucht die Ersatzsuche.</summary>
    public const int DeepSearchMaxDirectories = 300;

    /// <summary>Höchstens so viele EXEs liest die Symbolprobe je Spiel.</summary>
    public const int MaxProbes = 24;

    /// <summary>Ordner, die die Ersatzsuche nie betritt: Engine-Dateien von Unreal und die Ordner aus <see cref="IconExcludedExecutables.Folders"/>.</summary>
    private static readonly HashSet<string> SkippedFolders = new(["Engine", .. IconExcludedExecutables.Folders], StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, long> _fileLength = fileLength ?? RealFileLength;
    private readonly TimeSpan _deepSearchBudget = deepSearchBudget ?? DefaultDeepSearchBudget;

    /// <summary>Standard-Zeitgrenze der Ersatzsuche.</summary>
    public static TimeSpan DefaultDeepSearchBudget { get; } = TimeSpan.FromSeconds(1);

    /// <summary>EXE für das Symbol ohne Probe (etwa für Verknüpfungen): die erste der ersten nicht leeren Gruppe.</summary>
    public string? Resolve(GameConfig game, IReadOnlyList<string>? helperPatterns = null)
    {
        foreach (var group in Groups(game, helperPatterns))
        {
            if (group.Count > 0)
            {
                return group[0];
            }
        }

        return null;
    }

    /// <summary>
    /// EXE mit Symbol in Gruppenreihenfolge: in der ersten Gruppe, in der eine EXE ein Symbol ohne Standardsymbol hat, die
    /// erste mit gutem Symbol (<see cref="IconImage.IsGood"/>), sonst die erste ohne Standardsymbol; gibt es keine, die erste
    /// mit Standardsymbol, sonst null. Eine spätere Gruppe kommt nur zum Zug, wenn die früheren kein echtes Symbol haben:
    /// Erkannte und vom Launcher genannte EXEs sind das Spiel, eine schärfere aus dem Ordner kann ein Werkzeug sein (gemessen:
    /// SWAT 3 bekäme sonst statt seines 32-px-Symbols das des Mod-Starters Last_Resort.exe). Ebenso läuft die Ersatzsuche
    /// (<paramref name="deepSearch"/>) nur ohne echtes Symbol.
    /// </summary>
    public ResolvedIcon? ResolveWithIcon(GameConfig game, IReadOnlyList<string>? helperPatterns, Func<string, IconImage?> probe, bool deepSearch = true)
    {
        ResolvedIcon? fallback = null;
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var probes = 0;

        // True, sobald die Suche endet: gutes Symbol gefunden, eine Gruppe mit echtem Symbol fertig oder alle Proben verbraucht.
        bool Probe(IEnumerable<List<string>> groups, out ResolvedIcon? good)
        {
            good = null;
            foreach (var group in groups)
            {
                foreach (var exe in group)
                {
                    if (!tried.Add(exe))
                    {
                        continue;
                    }

                    if (probes++ >= MaxProbes)
                    {
                        return true;
                    }

                    if (probe(exe) is not { } icon)
                    {
                        continue;
                    }

                    if (icon.IsGood)
                    {
                        good = new ResolvedIcon(exe, icon);
                        return true;
                    }

                    if (fallback is null || (fallback.Icon.IsGeneric && !icon.IsGeneric))
                    {
                        fallback = new ResolvedIcon(exe, icon);
                    }
                }

                if (fallback is { Icon.IsGeneric: false })
                {
                    return true;
                }
            }

            return false;
        }

        if (!Probe(Groups(game, helperPatterns), out var found) && deepSearch)
        {
            _ = Probe(DeepSearch(game, helperPatterns), out found);
        }

        return found ?? fallback;
    }

    /// <summary>True, wenn der Installationsordner vorhanden und durchsuchbar ist (nie eine Laufwerkswurzel).</summary>
    public bool InstallDirExists(GameConfig game) => SearchableInstallDir(game) is not null;

    /// <summary>
    /// True, wenn die EXE heute als Symbolquelle des Spiels zulässig wäre: vorhanden, nicht ausgeschlossen und erkannt, vom
    /// Launcher genannt oder unter dem Installationsordner (für gemerkte Wahlen aus icon-cache.json).
    /// </summary>
    public bool IsAllowed(GameConfig game, string path, IReadOnlyList<string>? helperPatterns)
    {
        var normalized = PathUtil.Normalize(path);
        return IsUsable(game, normalized, helperPatterns)
            && (PathUtil.IsUnder(normalized, game.InstallDir)
                || game.DetectedExecutables.Any(x => PathUtil.PathEquals(x, normalized))
                || game.KnownExecutables.Any(x => PathUtil.PathEquals(x, normalized)));
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

    private static bool IsShipping(string path) => path.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Installationsordner, falls durchsuchbar (nie eine Laufwerkswurzel), sonst null.</summary>
    private string? SearchableInstallDir(GameConfig game)
    {
        var dir = PathUtil.Normalize(game.InstallDir);
        return dir.Length <= 3 || !Path.IsPathFullyQualified(dir) || !SafeDirectoryExists(dir) ? null : dir;
    }

    /// <summary>Die Gruppen in Reihenfolge, je Gruppe beste zuerst. Wird erst bei Bedarf ausgewertet.</summary>
    private IEnumerable<List<string>> Groups(GameConfig game, IReadOnlyList<string>? helperPatterns)
    {
        bool Usable(string path) => IsUsable(game, path, helperPatterns);

        yield return BySize(game.DetectedExecutables.Select(PathUtil.Normalize).Where(Usable));
        yield return BySize(game.KnownExecutables.Select(PathUtil.Normalize).Where(Usable));

        if (SearchableInstallDir(game) is not { } dir)
        {
            yield break;
        }

        // Unreal: [<Projekt>\]Binaries\Win64, Shipping zuerst; der Ordner Engine gehört nie zum Spiel.
        var unreal = new List<string>(UnrealBinaries(dir));
        var candidates = new List<string>(SafeExecutables(dir));
        foreach (var sub in SafeDirectories(dir, MaxSubdirectories))
        {
            if (!SkippedFolders.Contains(Path.GetFileName(sub)))
            {
                unreal.AddRange(UnrealBinaries(sub));
            }

            if (candidates.Count < MaxCandidates)
            {
                candidates.AddRange(SafeExecutables(sub));
            }
        }

        var usableUnreal = unreal.Select(PathUtil.Normalize).Where(Usable).ToList();
        yield return BySize(usableUnreal.Where(IsShipping))
            .Concat(BySize(usableUnreal.Where(p => !IsShipping(p))))
            .Concat(BySize(candidates.Take(MaxCandidates).Select(PathUtil.Normalize).Where(Usable)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> UnrealBinaries(string projectDir)
    {
        var win64 = Path.Combine(projectDir, "Binaries", "Win64");
        return SafeDirectoryExists(win64) ? SafeExecutables(win64) : [];
    }

    /// <summary>
    /// Ersatzsuche: alle EXEs bis <see cref="DeepSearchDepth"/> Ebenen unter dem Installationsordner (Breitensuche, ohne
    /// <see cref="SkippedFolders"/>), begrenzt auf <see cref="DeepSearchMaxDirectories"/> Ordner, ebenso viele EXEs und die
    /// Zeitgrenze; Shipping-EXEs zuerst, dann die größten.
    /// </summary>
    private IEnumerable<List<string>> DeepSearch(GameConfig game, IReadOnlyList<string>? helperPatterns)
    {
        if (SearchableInstallDir(game) is not { } dir)
        {
            yield break;
        }

        var clock = Stopwatch.StartNew();
        var found = new List<string>();
        var pending = new Queue<(string Dir, int Depth)>();
        pending.Enqueue((dir, 0));
        var visited = 0;
        while (pending.Count > 0 && visited < DeepSearchMaxDirectories && clock.Elapsed < _deepSearchBudget && found.Count < DeepSearchMaxDirectories)
        {
            var (current, depth) = pending.Dequeue();
            visited++;
            found.AddRange(SafeExecutables(current).Select(PathUtil.Normalize).Where(p => IsUsable(game, p, helperPatterns)));
            if (depth < DeepSearchDepth)
            {
                foreach (var sub in SafeDirectories(current, DeepSearchMaxDirectories - visited - pending.Count))
                {
                    if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                    {
                        pending.Enqueue((sub, depth + 1));
                    }
                }
            }
        }

        yield return BySize(found.Where(IsShipping)).Concat(BySize(found.Where(p => !IsShipping(p)))).ToList();
    }

    private bool IsUsable(GameConfig game, string path, IReadOnlyList<string>? helperPatterns) =>
        !string.IsNullOrWhiteSpace(path)
        && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && Path.IsPathFullyQualified(path)
        && !HelperExecutables.IsHelper(path, helperPatterns)
        && !IconExcludedExecutables.IsExcluded(path, game.InstallDir)
        && !game.ExcludedExecutables.Any(x => IsExcludedBy(x, path))
        && SafeFileExists(path);

    /// <summary>Größte zuerst (bei gleicher Größe in Fundreihenfolge).</summary>
    private List<string> BySize(IEnumerable<string> paths) =>
        paths.Distinct(StringComparer.OrdinalIgnoreCase).Select((p, i) => (Path: p, Index: i, Size: SafeLength(p)))
            .OrderByDescending(x => x.Size).ThenBy(x => x.Index).Select(x => x.Path).ToList();

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

    private List<string> SafeExecutables(string dir, string pattern = "*.exe")
    {
        try
        {
            return fileSystem.EnumerateFiles(dir, pattern).Take(MaxCandidates).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return [];
        }
    }

    private List<string> SafeDirectories(string dir, int limit)
    {
        try
        {
            return limit <= 0 ? [] : fileSystem.EnumerateDirectories(dir).Take(limit).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return [];
        }
    }
}
