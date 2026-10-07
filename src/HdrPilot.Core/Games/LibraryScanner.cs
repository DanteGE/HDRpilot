using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games;

/// <summary>Ergebnis eines Scanners samt Laufzeit.</summary>
public sealed record ScannerReport(ScanResult Result, TimeSpan Duration);

public sealed record LibraryScanReport(IReadOnlyList<ScannerReport> Scanners)
{
    /// <summary>Alle Spiele aller Scanner, je Schlüssel (Quelle:ID) nur einmal, sortiert nach Name.</summary>
    public IReadOnlyList<GameEntry> Games { get; } = Scanners
        .SelectMany(s => s.Result.Games)
        .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public IReadOnlyList<ScanError> AllErrors => Scanners.SelectMany(s => s.Result.Errors).ToList();
}

/// <summary>
/// Führt alle Scanner aus. Ein werfender oder hängender Scanner stört die anderen nicht:
/// seine Ausnahme wird als Fehler dieses Scanners gesammelt.
/// </summary>
public sealed class LibraryScanner(IReadOnlyList<IGameScanner> scanners, ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public IReadOnlyList<IGameScanner> Scanners => scanners;

    public async Task<LibraryScanReport> ScanAsync(ScanOptions options, CancellationToken cancellationToken = default)
    {
        var tasks = scanners.Select(s => Task.Run(() => RunOne(s, options, cancellationToken), cancellationToken)).ToList();
        var reports = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new LibraryScanReport(reports.OrderBy(r => r.Result.Source).ToList());
    }

    private ScannerReport RunOne(IGameScanner scanner, ScanOptions options, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = scanner.Scan(options, cancellationToken);
            foreach (var error in result.Errors)
            {
                _logger.LogWarning("Scanner {Scanner}: {Message} ({Path})", scanner.Name, error.Message, error.Path);
            }

            _logger.LogInformation("Scanner {Scanner}: {Count} Spiele in {Ms} ms.", scanner.Name, result.Games.Count, sw.ElapsedMilliseconds);
            return new ScannerReport(result, sw.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scanner {Scanner} ist abgestürzt.", scanner.Name);
            return new ScannerReport(
                new ScanResult
                {
                    Source = scanner.Source,
                    ScannerName = scanner.Name,
                    LauncherDetected = false,
                    Errors = [new ScanError($"Scanner abgestürzt: {ex.GetType().Name}: {ex.Message}")],
                },
                sw.Elapsed);
        }
    }
}
