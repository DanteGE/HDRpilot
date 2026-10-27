using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.Config;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Filter, die sich als Symbolknopf, per Alt-Kürzel und im Menü der Werkzeugleiste schalten lassen.</summary>
public enum ToolbarFilter
{
    /// <summary>Nur Spiele mit „HDR automatisch“ (Alt+A).</summary>
    HdrAuto,

    /// <summary>Nur neue Spiele (Alt+N).</summary>
    New,

    /// <summary>Nur Spiele mit HDR laut PCGamingWiki (Alt+H).</summary>
    HdrCapable,
}

/// <summary>
/// Werkzeugleiste „Fließleiste mit Lichtnaht“ (Wunsch des Nutzers 2026-10-17): eingeklappt oder nicht (gemerkt in
/// AppSettings.LibraryToolbarCollapsed, ein Zustand für alle Ansichten), Inhalt von Kapsel und Ansage (welche Filter gelten),
/// Zähler der Filterknöpfe für Bildschirmleser (ItemStatus) und die Texte der Tooltips. Alle Texte folgen dem Filterzustand:
/// <see cref="RaiseToolbarTexts"/> läuft bei jeder Änderung von Filtern, Sortierung und Zählern.
/// </summary>
public sealed partial class LibraryViewModel
{
    private static readonly string[] ToolbarTextProperties =
    [
        nameof(AppliedSearchText), nameof(ToolbarSummary), nameof(ToolbarTipText),
        nameof(OnlySelectedStatus), nameof(OnlyNewStatus), nameof(OnlyHdrCapableStatus),
        nameof(OnlySelectedTipLead), nameof(OnlySelectedTip), nameof(OnlyNewTipLead), nameof(OnlyNewTip),
        nameof(OnlyHdrCapableTipLead), nameof(OnlyHdrCapableTip), nameof(SearchTipFooter), nameof(SourceTipFooter),
        nameof(SortTipFooter), nameof(SortTipCompactText), nameof(SeenTipFooter), nameof(ResetTipText),
    ];

    private bool _toolbarCollapsed;

    /// <summary>
    /// Werkzeugleiste eingeklappt (Kapsel auf der Bildkante). Setzen schaltet um und speichert sofort; das Fenster bewegt
    /// nach der Effektstufe.
    /// </summary>
    public bool IsToolbarCollapsed
    {
        get => _toolbarCollapsed;
        set => SetToolbarCollapsed(value, persist: true);
    }

    /// <summary>Erster Eintrag im Menü der Leiste: „Leiste ausblenden“ bzw. „Leiste einblenden“.</summary>
    public string ToolbarToggleText => _toolbarCollapsed ? LibraryStrings.Toolbar_Show : LibraryStrings.Toolbar_Hide;

    /// <summary>Strg+F, Strg+F1, Lasche, Kapsel und Menü: Werkzeugleiste aus- bzw. einblenden.</summary>
    public RelayCommand ToggleToolbarCommand { get; private set; } = null!;

    /// <summary>Filter umschalten (Parameter <see cref="ToolbarFilter"/> oder sein Name): Alt+A/N/H und Menü.</summary>
    public RelayCommand ToggleFilterCommand { get; private set; } = null!;

    /// <summary>Angewandter Suchbegriff (nach der Entprellung), wie die Kapsel ihn zeigt; leer ohne Suche.</summary>
    public string AppliedSearchText => string.Join(' ', _searchTokens);

    /// <summary>
    /// Zusammenfassung für Bildschirmleser (ItemStatus von Lasche und Kapsel) und den Tooltip der Kapsel: „Gefiltert: Suche
    /// „gears“, Steam, Neu – 3 von 652“ bzw. „Keine Filter“.
    /// </summary>
    public string ToolbarSummary => IsFiltered
        ? LibraryText.Format(LibraryStrings.Toolbar_Filtered, string.Join(", ", ActiveFilterNames()), VisibleCount, TotalCount)
        : LibraryStrings.Toolbar_NoFilters;

    /// <summary>Erklärung im Tooltip der eingeklappten Kapsel.</summary>
    public string ToolbarTipText => IsFiltered ? ToolbarSummary : LibraryStrings.Toolbar_NoFiltersTip;

    /// <summary>ItemStatus der Filterknöpfe: wie viele Spiele das Merkmal haben („36 Spiele“).</summary>
    public string OnlySelectedStatus => LibraryText.Games(HdrAutoCount);

    public string OnlyNewStatus => LibraryText.Games(NewCount);

    public string OnlyHdrCapableStatus => LibraryText.Games(HdrCapableCount);

    /// <summary>Fußzeile der Tooltips der Filter: hervorgehoben „Filter an“, dann der Rest („ · 36 von 652 · Klick hebt ihn auf“).</summary>
    public string OnlySelectedTipLead => FilterTipLead(_onlySelected);

    public string OnlySelectedTip => FilterTip(_onlySelected, HdrAutoCount);

    public string OnlyNewTipLead => FilterTipLead(_onlyNew);

    public string OnlyNewTip => FilterTip(_onlyNew, NewCount);

    public string OnlyHdrCapableTipLead => FilterTipLead(_onlyHdrCapable);

    public string OnlyHdrCapableTip => FilterTip(_onlyHdrCapable, HdrCapableCount);

    /// <summary>Fußzeile des Tooltips der Suche: Treffer für den Suchbegriff bzw. die Zahl aller Spiele.</summary>
    public string SearchTipFooter => _searchTokens.Length > 0
        ? LibraryText.Format(LibraryStrings.Tip_SearchHits, VisibleCount, AppliedSearchText)
        : LibraryText.Games(TotalCount);

    public string SourceTipFooter => _selectedSource.Source is null
        ? LibraryText.Format(LibraryStrings.Tip_SourceAll, LibraryText.Games(TotalCount))
        : LibraryText.Format(LibraryStrings.Tip_SourceOne, _selectedSource.Label, LibraryText.Games(_selectedSource.Count));

    public string SortTipFooter => _selectedSort.Label;

    /// <summary>Erklärung der Sortierung, wenn sie im schmalen Fenster nur ihr Symbol zeigt.</summary>
    public string SortTipCompactText => LibraryText.Format(LibraryStrings.Tip_SortTextCompact, _selectedSort.Label);

    public string SeenTipFooter => LibraryText.Format(LibraryStrings.Tip_SeenFooter, NewCount);

    /// <summary>
    /// Tooltip von „Zurücksetzen“: „Zeigt wieder alle 651 Spiele.“; mit ausgeblendeten nicht installierten Spielen (E90)
    /// auch, wie viele ausgeblendet bleiben (sonst nennt sie nur die Einstellungskarte).
    /// </summary>
    public string ResetTipText => HiddenCount switch
    {
        0 => LibraryText.Format(LibraryStrings.Toolbar_ResetTip, LibraryText.Games(TotalCount)),
        1 => LibraryText.Format(LibraryStrings.Toolbar_ResetTipHiddenOne, LibraryText.Games(TotalCount), LibraryText.HiddenGames(1)),
        var hidden => LibraryText.Format(LibraryStrings.Toolbar_ResetTipHidden, LibraryText.Games(TotalCount), LibraryText.HiddenGames(hidden)),
    };

    /// <summary>Umschalten; <paramref name="persist"/> false nur für Bildschirmfotos und Prüfungen ohne Schreiben.</summary>
    public void SetToolbarCollapsed(bool collapsed, bool persist)
    {
        if (collapsed == _toolbarCollapsed)
        {
            return;
        }

        _toolbarCollapsed = collapsed;
        OnPropertyChanged(nameof(IsToolbarCollapsed));
        OnPropertyChanged(nameof(ToolbarToggleText));
        if (persist && !_disposed)
        {
            _ = WriteAsync(c => c.Settings.LibraryToolbarCollapsed = collapsed);
        }
    }

    /// <summary>Einen Filter umschalten (Knopf, Alt-Kürzel, Menü).</summary>
    public void ToggleFilter(ToolbarFilter filter)
    {
        switch (filter)
        {
            case ToolbarFilter.HdrAuto:
                OnlySelected = !OnlySelected;
                break;
            case ToolbarFilter.New:
                OnlyNew = !OnlyNew;
                break;
            case ToolbarFilter.HdrCapable:
                OnlyHdrCapable = !OnlyHdrCapable;
                break;
        }
    }

    /// <summary>Aktive Filter in Worten, in der Reihenfolge der Leiste (Suche, Quelle, Automatik, Neu, HDR-fähig).</summary>
    public IReadOnlyList<string> ActiveFilterNames()
    {
        var names = new List<string>();
        if (_searchTokens.Length > 0)
        {
            names.Add(LibraryText.Format(LibraryStrings.Toolbar_FilterSearch, AppliedSearchText));
        }

        if (_selectedSource.Source is not null)
        {
            names.Add(_selectedSource.Label);
        }

        if (_onlySelected)
        {
            names.Add(LibraryStrings.Filter_OnlySelected);
        }

        if (_onlyNew)
        {
            names.Add(LibraryStrings.Filter_OnlyNew);
        }

        if (_onlyHdrCapable)
        {
            names.Add(LibraryStrings.Filter_OnlyHdrCapable);
        }

        return names;
    }

    private void InitializeToolbar(AppSettings settings)
    {
        _toolbarCollapsed = settings.LibraryToolbarCollapsed;
        ToggleToolbarCommand = new RelayCommand(() => IsToolbarCollapsed = !IsToolbarCollapsed);
        ToggleFilterCommand = new RelayCommand(p =>
        {
            if (p is ToolbarFilter filter)
            {
                ToggleFilter(filter);
            }
            else if (p is string text && Enum.TryParse<ToolbarFilter>(text, ignoreCase: true, out var parsed))
            {
                ToggleFilter(parsed);
            }
        });
    }

    private static string FilterTipLead(bool on) => on ? LibraryStrings.Tip_FilterOnLead : string.Empty;

    private string FilterTip(bool on, int count) => on
        ? LibraryText.Format(LibraryStrings.Tip_FilterOn, count, TotalCount)
        : LibraryText.Format(LibraryStrings.Tip_FilterOff, LibraryText.Games(count));

    /// <summary>Texte von Kapsel, Ansage, Zählern und Tooltips neu melden (Filter, Sortierung oder Zähler geändert).</summary>
    private void RaiseToolbarTexts()
    {
        foreach (var name in ToolbarTextProperties)
        {
            OnPropertyChanged(name);
        }
    }
}
