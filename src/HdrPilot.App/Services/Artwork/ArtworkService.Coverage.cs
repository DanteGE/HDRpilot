using System.Diagnostics;
using System.IO;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Ergebnis der Messung über die ganze Bibliothek (Bericht <c>--artwork-report</c>, G10.3).</summary>
/// <param name="Smeared">Verschmierte Steam-Kapseln: Name und Kennzahlen.</param>
/// <param name="ExeIcons">Symbole aus EXE-Dateien: Name, EXE, Größe.</param>
public sealed record ArtworkReport(
    ArtworkCoverage Coverage,
    IReadOnlyList<FallbackExample> Examples,
    IReadOnlyList<string> Smeared,
    IReadOnlyList<string> ExeIcons,
    int GeneratedHeroes,
    int GeneratedCovers,
    int GeneratedHeaders,
    int Monograms,
    int NameInsteadOfLogo,
    int CoversFromSmear,
    TimeSpan Elapsed);

/// <summary>Abdeckung, Beispiele der Ersatzbilder, Bericht und die Schnittstelle für SteamGridDB.</summary>
public sealed partial class ArtworkService
{
    /// <summary>Höchstens so viele Spiele werden gleichzeitig ermittelt (Abdeckung, Bericht).</summary>
    private const int CoverageParallel = 2;

    public Task<ArtworkCoverage> GetCoverageAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default)
    {
        var snapshots = games.Select(Snapshot).ToList();
        return Task.Run(() => Coverage(ResolveAll(snapshots, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<FallbackExample>> GetFallbackExamplesAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default)
    {
        var snapshots = games.Select(Snapshot).ToList();
        return Task.Run(() => Examples(ResolveAll(snapshots, cancellationToken)), cancellationToken);
    }

    /// <summary>Messung über die ganze Bibliothek mit Zeit (Bericht, G10.3). Liest nur; schreibt index.json.</summary>
    public Task<ArtworkReport> BuildReportAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default)
    {
        var snapshots = games.Select(Snapshot).ToList();
        return Task.Run(
            () =>
            {
                var clock = Stopwatch.StartNew();
                var states = ResolveAll(snapshots, cancellationToken);
                var elapsed = clock.Elapsed;
                var smeared = states.Where(s => s.CoverSmeared)
                    .Select(s => s.Game.Name + " (" + Entry(s.Key, e => e.SmearMetrics) + ")")
                    .ToList();
                var exe = states.Where(s => s.Icon.Kind == "exe")
                    .Select(s => s.Game.Name + ": " + Path.GetFileName(s.Icon.Path) + " · " + s.Icon.Px + " px")
                    .ToList();
                return new ArtworkReport(
                    Coverage(states),
                    Examples(states),
                    smeared,
                    exe,
                    states.Count(s => s.Hero.Generate == Generator.Hero),
                    states.Count(s => s.Cover.Generate is Generator.CoverFromHero or Generator.CoverFromField or Generator.CoverFromSquare),
                    states.Count(s => s.Header.Generate is Generator.Header or Generator.HeaderFromSquare),
                    states.Count(s => s.Icon.Kind == "monogram"),
                    states.Count(s => s.Logo.File is null),
                    states.Count(s => s.Summary.CoverFromSmear),
                    elapsed);
            },
            cancellationToken);
    }

    // ---- SteamGridDB -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Bildarten (Titelbild, Logo, Cover), die einem Spiel lokal fehlen und keine eigene Wahl haben: nur diese füllt SteamGridDB.
    /// Eine verschmierte Steam-Kapsel zählt als fehlendes Cover. Schon geladene Bilder von SteamGridDB zählen nicht.
    /// </summary>
    public IReadOnlyList<ArtworkKind> MissingKinds(GameConfig game)
    {
        var state = Resolve(Snapshot(game), full: false);
        var missing = new List<ArtworkKind>();
        if (state.Located.Hero is null && game.Artwork?.Hero is null && state.Hero.Origin != ArtworkOrigin.SteamGridDb)
        {
            missing.Add(ArtworkKind.Hero);
        }

        if (state.Located.Logo is null && game.Artwork?.Logo is null && state.Logo.Origin != ArtworkOrigin.SteamGridDb)
        {
            missing.Add(ArtworkKind.Logo);
        }

        if ((state.Located.Cover is null || state.CoverSmeared) && game.Artwork?.Cover is null && state.Cover.Origin != ArtworkOrigin.SteamGridDb)
        {
            missing.Add(ArtworkKind.Cover);
        }

        return missing;
    }

    /// <summary>Zeitpunkt der letzten Abfrage einer Bildart bei SteamGridDB (null: nie).</summary>
    public DateTimeOffset? SteamGridDbChecked(GameConfig game, ArtworkKind kind) =>
        Entry(game.Key, e => e.SteamGridDb?.Checked.TryGetValue(NameOf(kind), out var when) == true ? when : (DateTimeOffset?)null);

    public void MarkSteamGridDbChecked(GameConfig game, ArtworkKind kind, DateTimeOffset when) =>
        WithEntry(game.Key, e => (e.SteamGridDb ??= new IndexedSteamGridDb()).Checked[NameOf(kind)] = when);

    /// <summary>Gemerkte SteamGridDB-ID eines Spiels ohne Steam-AppID.</summary>
    public long? SteamGridDbGameId(GameConfig game) => Entry(game.Key, e => e.SteamGridDb?.GameId);

    public void SetSteamGridDbGameId(GameConfig game, long id) => WithEntry(game.Key, e => (e.SteamGridDb ??= new IndexedSteamGridDb()).GameId = id);

    /// <summary>Setzt die ID nur, solange noch <paramref name="expected"/> gilt (Prüfen und Setzen unter der Sperre des Verzeichnisses).</summary>
    public bool TrySetSteamGridDbGameId(GameConfig game, long? expected, long id)
    {
        var set = WithEntry(game.Key, e =>
        {
            if (e.SteamGridDb?.GameId != expected)
            {
                return false;
            }

            (e.SteamGridDb ??= new IndexedSteamGridDb()).GameId = id;
            return true;
        });
        if (set)
        {
            MarkDirty();
        }

        return set;
    }

    /// <summary>
    /// Neue, vom Nutzer gewählte Zuordnung (E93): Unter der Sperre des Verzeichnisses werden die Bilder und Fehlanzeigen der
    /// alten Zuordnung vergessen und die ID gesetzt; danach (außerhalb der Sperre) die Dateien in art\sgdb gelöscht, das Spiel
    /// im Speicher vergessen und gemeldet. Gleiche ID: nichts.
    /// </summary>
    public void ReplaceSteamGridDbGame(GameConfig game, long id)
    {
        List<string>? files = null;
        WithEntry(game.Key, e =>
        {
            var sgdb = e.SteamGridDb ??= new IndexedSteamGridDb();
            if (sgdb.GameId == id)
            {
                return;
            }

            files = [.. sgdb.Files.Values];
            sgdb.Files.Clear();
            sgdb.Checked.Clear();
            sgdb.GameId = id;
        });
        if (files is null)
        {
            return;
        }

        var folder = Path.Combine(_options.ArtDirectory, ArtworkFiles.SteamGridDbFolder);
        foreach (var file in files.Where(f => f.Length > 0 && Path.GetFileName(f) == f))
        {
            try
            {
                _fileSystem.DeleteFile(Path.Combine(folder, file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Bilder: Bild der alten SteamGridDB-Zuordnung {File} nicht gelöscht: {Message}", file, ex.Message);
            }
        }

        ForgetInMemory(game.Key);
        Raise(game.Key);
    }

    /// <summary>Letzter Lauf von SteamGridDB (höchstens einmal am Tag nach dem Start).</summary>
    public DateTimeOffset? SteamGridDbLastRun
    {
        get
        {
            lock (_indexGate)
            {
                return (_index ??= _indexFile.Load()).SteamGridDbLastRun;
            }
        }

        set
        {
            lock (_indexGate)
            {
                (_index ??= _indexFile.Load()).SteamGridDbLastRun = value;
            }

            MarkDirty();
        }
    }

    /// <summary>
    /// Speichert ein Bild von SteamGridDB in art\sgdb (nur, wenn es sich als Bild dekodieren lässt) und meldet die Änderung.
    /// Wirft InvalidDataException für kein Bild und IO-Ausnahmen beim Schreiben.
    /// </summary>
    public void StoreSteamGridDbImage(GameConfig game, ArtworkKind kind, byte[] data, string extension, DateTimeOffset when) =>
        TryStoreSteamGridDbImage(game, kind, data, extension, when, expectedGameId: null);

    /// <summary>
    /// Wie <see cref="StoreSteamGridDbImage"/>, aber nur, solange die gemerkte ID noch <paramref name="expectedGameId"/> ist
    /// (null: keine Prüfung). Geprüft wird vor dem Schreiben und noch einmal unter der Sperre des Verzeichnisses beim
    /// Eintragen; hat sich die Zuordnung dazwischen geändert, wird die eben geschriebene Datei wieder gelöscht. False: verworfen.
    /// </summary>
    public bool TryStoreSteamGridDbImage(GameConfig game, ArtworkKind kind, byte[] data, string extension, DateTimeOffset when, long? expectedGameId)
    {
        if (expectedGameId is not null && SteamGridDbGameId(game) != expectedGameId)
        {
            return false;
        }

        try
        {
            // Größe aus dem Kopf vor dem Dekodieren (höchstens 36 MP), dann nur ein kleines Vorschaubild.
            ArtworkImaging.Validate(data, ArtworkImaging.MaxPixels);
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex) && ex is not InvalidDataException)
        {
            throw new InvalidDataException(ArtworkStrings.Error_NotAnImage, ex);
        }

        var file = ArtworkFiles.SteamGridDbFileName(game.Key, kind, extension);
        var folder = Path.Combine(_options.ArtDirectory, ArtworkFiles.SteamGridDbFolder);
        foreach (var old in new[] { ".png", ".jpg", ".webp" }.Select(e => ArtworkFiles.SteamGridDbFileName(game.Key, kind, e)).Where(f => f != file))
        {
            _fileSystem.DeleteFile(Path.Combine(folder, old));
        }

        ArtworkImaging.WriteAtomic(Path.Combine(folder, file), data);
        var stored = WithEntry(game.Key, e =>
        {
            if (expectedGameId is not null && e.SteamGridDb?.GameId != expectedGameId)
            {
                return false;
            }

            var sgdb = e.SteamGridDb ??= new IndexedSteamGridDb();
            sgdb.Files[NameOf(kind)] = file;
            sgdb.Checked[NameOf(kind)] = when;
            return true;
        });
        if (!stored)
        {
            // Die neue Zuordnung kennt diese Datei nicht; ihr Lauf beginnt erst nach dem Ende dieses Laufs.
            _fileSystem.DeleteFile(Path.Combine(folder, file));
            return false;
        }

        MarkDirty();
        ForgetInMemory(game.Key);
        Raise(game.Key);
        return true;
    }

    /// <summary>
    /// SteamGridDB ein- oder ausgeschaltet: Spiele mit gespeicherten Bildern von dort vergessen und melden, damit sie gleich
    /// mit den nun geltenden Bildern erscheinen (aus: wieder lokal bzw. erzeugt).
    /// </summary>
    public void SteamGridDbSwitched(bool enabled)
    {
        List<string> keys;
        lock (_indexGate)
        {
            keys = (_index ??= _indexFile.Load()).Games
                .Where(g => g.Value.SteamGridDb?.Files is { Count: > 0 })
                .Select(g => g.Key)
                .ToList();
        }

        foreach (var key in keys)
        {
            Forget(key);
        }
    }

    // ---- intern ------------------------------------------------------------------------------------------------------

    /// <summary>Ermittelt alle Spiele (ohne Farben), höchstens zwei zugleich; Fehler eines Spiels überspringen nur dieses.</summary>
    private List<GameState> ResolveAll(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken)
    {
        var states = new GameState?[games.Count];
        Parallel.For(0, games.Count, new ParallelOptions { MaxDegreeOfParallelism = CoverageParallel, CancellationToken = cancellationToken }, i =>
        {
            try
            {
                states[i] = Resolve(games[i], full: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Bilder: {Game} nicht ermittelt.", games[i].Key);
            }
        });

        if (Volatile.Read(ref _leases) == 0)
        {
            FlushIndex();
        }

        return states.OfType<GameState>().ToList();
    }

    /// <summary>„genutzt / gefunden“ je Launcher und Bildart (verschmierte Kapseln zählen als gefunden, nicht als genutzt).</summary>
    private static ArtworkCoverage Coverage(IReadOnlyList<GameState> states)
    {
        static bool FromLauncher(ArtSpec spec) => spec.Origin is ArtworkOrigin.Launcher or ArtworkOrigin.Alternative;
        var rows = states
            .GroupBy(s => s.Game.Source)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => new ArtworkCoverageRow(
                g.Key,
                g.Count(),
                new ArtworkCount(g.Count(s => FromLauncher(s.Hero)), g.Count(s => s.Located.Hero is not null || s.Located.Alternatives.Any(a => a.Kind == ArtworkKind.Hero))),
                new ArtworkCount(g.Count(s => FromLauncher(s.Logo)), g.Count(s => s.Located.Logo is not null)),
                new ArtworkCount(g.Count(s => FromLauncher(s.Cover)), g.Count(s => s.Located.Cover is not null)),
                new ArtworkCount(g.Count(s => s.Icon.Kind is not ("monogram" or "custom")), g.Count(s => s.Icon.Kind != "monogram"))))
            .ToList();
        return new ArtworkCoverage(rows);
    }

    /// <summary>
    /// Je Ersatzbild-Art ein echtes Beispiel, möglichst je Art ein anderes Spiel: zuletzt gespielte zuerst; beim erzeugten Cover
    /// Kachelbilder zuerst, beim Titelbild aus dem Symbol echte Symbole (kein Kachelbild) mit der größten Kantenlänge.
    /// </summary>
    private static IReadOnlyList<FallbackExample> Examples(IReadOnlyList<GameState> states)
    {
        var ordered = states.OrderByDescending(s => s.Game.LastPlayed ?? DateTimeOffset.MinValue).ThenBy(s => s.Game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var examples = new List<FallbackExample>();
        void Add(FallbackKind kind, IEnumerable<GameState> candidates, Func<GameState, string> detail)
        {
            var list = candidates.ToList();
            if ((list.FirstOrDefault(s => !used.Contains(s.Key)) ?? list.FirstOrDefault()) is { } state)
            {
                used.Add(state.Key);
                examples.Add(new FallbackExample(kind, state.Key, state.Game.Name, detail(state)));
            }
        }

        string Source(GameState s) => LibraryText.SourceName(s.Game.Source);
        Add(
            FallbackKind.CoverFromSmearedCapsule,
            ordered.Where(s => s.Summary.CoverFromSmear),
            s => Format(ArtworkStrings.Fallback_Smeared, Source(s)));
        Add(
            FallbackKind.CoverGenerated,
            ordered.Where(s => s.Cover.Generate is Generator.CoverFromField or Generator.CoverFromHero && !s.Summary.CoverFromSmear && s.Game.Artwork?.Cover is null)
                .OrderByDescending(s => s.Icon.Kind == "tile"),
            s => Format(
                s.Icon.Kind == "tile" ? ArtworkStrings.Fallback_CoverFromTile : s.Cover.Generate == Generator.CoverFromHero ? ArtworkStrings.Fallback_CoverFromHero : ArtworkStrings.Fallback_CoverFromIcon,
                Source(s)));
        Add(
            FallbackKind.HeroFromIcon,
            ordered.Where(s => s.Hero.Generate == Generator.Hero && s.Game.Artwork?.Hero is null && s.Icon.Path is not null)
                .OrderByDescending(s => s.Icon.Kind == "exe").ThenBy(s => s.Icon.Kind == "tile").ThenByDescending(s => s.Icon.Px),
            s => s.Icon.Kind switch
            {
                "tile" => Format(ArtworkStrings.Fallback_FromTile, Source(s), s.Icon.Px),
                "ico" => Format(ArtworkStrings.Fallback_FromIconFile, Source(s), s.Icon.Px),
                _ => Format(ArtworkStrings.Fallback_FromFile, Source(s), Path.GetFileName(s.Icon.Path), s.Icon.Px),
            });
        Add(
            FallbackKind.NameInsteadOfLogo,
            ordered.Where(s => s.Logo.File is null && s.Hero.File is not null && s.Game.Artwork?.Logo is null),
            s => Format(ArtworkStrings.Fallback_NoLogo, Source(s)));
        Add(
            FallbackKind.Monogram,
            ordered.Where(s => s.Icon.Kind == "monogram"),
            s => Format(ArtworkStrings.Fallback_Monogram, Source(s)));
        return examples.OrderBy(e => e.Kind).ToList();
    }
}
