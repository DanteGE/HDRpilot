using System.Collections;
using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Reihenfolge der Spieleliste.</summary>
public enum GameSortOrder
{
    /// <summary>Nach Name (Standard).</summary>
    Name,

    /// <summary>Zuletzt gespielt zuerst; nie gespielte danach, nach Name.</summary>
    LastPlayed,

    /// <summary>HDR laut PCGamingWiki zuerst, dann eingeschränktes HDR, dann alle übrigen; jeweils nach Name.</summary>
    HdrSupport,
}

/// <summary>Eintrag der Sortier-Auswahl (Text und Symbol aus Segoe Fluent Icons).</summary>
public sealed class SortOption(GameSortOrder order, string label, string glyph)
{
    public GameSortOrder Order { get; } = order;

    public string Label { get; } = label;

    public string Glyph { get; } = glyph;

    public override string ToString() => Label;
}

/// <summary>Sortierung der Spieleliste und ihre Ablage in der Konfiguration (AppSettings.LibrarySortOrder).</summary>
public static class GameSorting
{
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

    /// <summary>Die Auswahl im Fenster, in dieser Reihenfolge.</summary>
    public static IReadOnlyList<SortOption> CreateOptions() =>
    [
        new SortOption(GameSortOrder.Name, LibraryStrings.Sort_ByName, ""),
        new SortOption(GameSortOrder.LastPlayed, LibraryStrings.Sort_ByLastPlayed, ""),
        new SortOption(GameSortOrder.HdrSupport, LibraryStrings.Sort_ByHdrSupport, ""),
    ];

    /// <summary>Wert aus der Konfiguration; fehlend oder unbekannt (etwa aus einer neueren Version): nach Name.</summary>
    public static GameSortOrder Parse(string? value)
    {
        foreach (var order in Enum.GetValues<GameSortOrder>())
        {
            if (string.Equals(order.ToString(), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }
        }

        return GameSortOrder.Name;
    }

    /// <summary>Wert für die Konfiguration: der Standard wird nicht gespeichert (null), damit config.json gleich bleibt.</summary>
    public static string? ToConfig(GameSortOrder order) => order == GameSortOrder.Name ? null : order.ToString();

    /// <summary>Vergleich für die gefilterte Sicht (ListCollectionView.CustomSort).</summary>
    public static IComparer Comparer(GameSortOrder order) => order switch
    {
        GameSortOrder.LastPlayed => LastPlayedComparer.Instance,
        GameSortOrder.HdrSupport => HdrSupportComparer.Instance,
        _ => NameComparer.Instance,
    };

    /// <summary>Name, bei gleichem Namen der Schlüssel (stabile Reihenfolge).</summary>
    public static int CompareByName(GameItemViewModel a, GameItemViewModel b)
    {
        var byName = NameOrder.Compare(a.Name, b.Name);
        return byName != 0 ? byName : StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key);
    }

    /// <summary>Zuletzt gespielt zuerst; Spiele ohne Datum danach; sonst nach Name.</summary>
    public static int CompareByLastPlayed(GameItemViewModel a, GameItemViewModel b)
    {
        var (pa, pb) = (a.Config.LastPlayed, b.Config.LastPlayed);
        if (pa is { } x && pb is { } y && x != y)
        {
            return y.CompareTo(x);
        }

        if (pa.HasValue != pb.HasValue)
        {
            return pa.HasValue ? -1 : 1;
        }

        return CompareByName(a, b);
    }

    /// <summary>HDR laut PCGamingWiki, dann eingeschränkt, dann alle übrigen (auch „nur mit Mods“); sonst nach Name.</summary>
    public static int CompareByHdrSupport(GameItemViewModel a, GameItemViewModel b)
    {
        var byRank = HdrRank(a.HdrSupport).CompareTo(HdrRank(b.HdrSupport));
        return byRank != 0 ? byRank : CompareByName(a, b);
    }

    private static int HdrRank(HdrSupportStatus status) => status switch
    {
        HdrSupportStatus.Native => 0,
        HdrSupportStatus.Limited => 1,
        _ => 2,
    };

    private sealed class NameComparer : IComparer, IComparer<GameItemViewModel>
    {
        public static readonly NameComparer Instance = new();

        public int Compare(GameItemViewModel? x, GameItemViewModel? y) => x is null || y is null ? Nulls(x, y) : CompareByName(x, y);

        public int Compare(object? x, object? y) => Compare(x as GameItemViewModel, y as GameItemViewModel);
    }

    private sealed class LastPlayedComparer : IComparer, IComparer<GameItemViewModel>
    {
        public static readonly LastPlayedComparer Instance = new();

        public int Compare(GameItemViewModel? x, GameItemViewModel? y) => x is null || y is null ? Nulls(x, y) : CompareByLastPlayed(x, y);

        public int Compare(object? x, object? y) => Compare(x as GameItemViewModel, y as GameItemViewModel);
    }

    private sealed class HdrSupportComparer : IComparer, IComparer<GameItemViewModel>
    {
        public static readonly HdrSupportComparer Instance = new();

        public int Compare(GameItemViewModel? x, GameItemViewModel? y) => x is null || y is null ? Nulls(x, y) : CompareByHdrSupport(x, y);

        public int Compare(object? x, object? y) => Compare(x as GameItemViewModel, y as GameItemViewModel);
    }

    private static int Nulls(object? x, object? y) => (x is null ? 1 : 0) - (y is null ? 1 : 0);
}
