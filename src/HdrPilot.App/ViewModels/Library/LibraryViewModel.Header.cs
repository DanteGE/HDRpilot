using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Kopf der Bibliothek folgt der Quelle (Wunsch des Nutzers 2026-10-22, Entscheidung: nur die Quelle im Titel). Groß steht
/// die gewählte Quelle („Alle Spiele“, „Steam-Spiele“ …, <see cref="HeaderTitle"/>), darüber klein „SPIELEBIBLIOTHEK“. Die
/// Kennzahlen zählen nur die Auswahl: ohne Filter und Suche alle Spiele der Quelle („641 Spiele · 12 mit HDR automatisch ·
/// 3 neu“), mit Filtern oder Suche die sichtbaren, bezogen auf alle der Quelle („36 von 641 Spielen · 4 mit HDR
/// automatisch · 1 neu“). Ausgeblendete nicht installierte Spiele zählen nie (sie stehen nicht in <see cref="Games"/>, E90).
/// Der Titel öffnet das Quellenmenü; <see cref="SelectSourceCommand"/> setzt <see cref="SelectedSource"/> wie das Feld
/// „Quelle“ der Werkzeugleiste, so folgen Leiste, Kapsel, Menü und Titel immer derselben Wahl. Die Zähler der Filterknöpfe
/// bleiben, wie sie sind (alle Spiele, siehe <see cref="HdrAutoCount"/>).
/// </summary>
public sealed partial class LibraryViewModel
{
    private RelayCommand? _selectSourceCommand;
    private string _headerTitle = LibraryText.SourceTitle(null);
    private string _headerCountText = LibraryText.Games(0);
    private int _headerSourceCount;
    private int _headerShownCount;
    private int _headerHdrAutoCount;
    private int _headerNewCount;

    /// <summary>Großer Titel im Kopf: die gewählte Quelle.</summary>
    public string HeaderTitle
    {
        get => _headerTitle;
        private set
        {
            if (SetProperty(ref _headerTitle, value))
            {
                OnPropertyChanged(nameof(HeaderSourceName));
            }
        }
    }

    /// <summary>Name des Titels für Bildschirmleser: „Quelle: Steam-Spiele“.</summary>
    public string HeaderSourceName => LibraryText.Format(LibraryStrings.Header_SourceName, _headerTitle);

    /// <summary>Filter oder Suche aktiv (die Quelle zählt hier nicht: Sie ist der Titel).</summary>
    public bool IsHeaderFiltered => _searchTokens.Length > 0 || _onlySelected || _onlyNew || _onlyHdrCapable;

    /// <summary>Alle Spiele der gewählten Quelle.</summary>
    public int HeaderSourceCount
    {
        get => _headerSourceCount;
        private set => SetProperty(ref _headerSourceCount, value);
    }

    /// <summary>Gezählte Spiele: mit Filtern oder Suche die sichtbaren, sonst alle der Quelle.</summary>
    public int HeaderShownCount
    {
        get => _headerShownCount;
        private set => SetProperty(ref _headerShownCount, value);
    }

    /// <summary>„641 Spiele“ bzw. mit Filtern oder Suche „36 von 641 Spielen“.</summary>
    public string HeaderCountText
    {
        get => _headerCountText;
        private set => SetProperty(ref _headerCountText, value);
    }

    /// <summary>Gezählte Spiele mit „HDR automatisch“.</summary>
    public int HeaderHdrAutoCount
    {
        get => _headerHdrAutoCount;
        private set
        {
            if (SetProperty(ref _headerHdrAutoCount, value))
            {
                OnPropertyChanged(nameof(HeaderHdrAutoText));
            }
        }
    }

    public string HeaderHdrAutoText => LibraryText.Format(LibraryStrings.Stats_HdrAuto, _headerHdrAutoCount);

    /// <summary>Gezählte neue Spiele (die Angabe steht nur, wenn es welche gibt).</summary>
    public int HeaderNewCount
    {
        get => _headerNewCount;
        private set
        {
            if (SetProperty(ref _headerNewCount, value))
            {
                OnPropertyChanged(nameof(HeaderNewText));
            }
        }
    }

    public string HeaderNewText => LibraryText.Format(LibraryStrings.Stats_New, _headerNewCount);

    /// <summary>Quellenmenü am Titel: wählt die Quelle (Parameter: ein Eintrag aus <see cref="SourceOptions"/>).</summary>
    public RelayCommand SelectSourceCommand => _selectSourceCommand ??= new RelayCommand(p =>
    {
        if (p is SourceFilterOption option && SourceOptions.Contains(option))
        {
            SelectedSource = option;
        }
    });

    /// <summary>Kopftext für die Zahlen: „641 Spiele“ bzw. „36 von 641 Spielen“ („… von 1 Spiel“).</summary>
    public static string HeaderCount(bool filtered, int shown, int total) => !filtered
        ? LibraryText.Games(total)
        : total == 1
            ? LibraryText.Format(LibraryStrings.Stats_FilteredOfOne, shown)
            : LibraryText.Format(LibraryStrings.Stats_Filtered, shown, total);

    /// <summary>Haken im Quellenmenü auf die gewählte Quelle setzen (beim Wählen und Zurücksetzen).</summary>
    private void MarkSelectedSource()
    {
        foreach (var option in SourceOptions)
        {
            option.IsSelected = ReferenceEquals(option, _selectedSource);
        }
    }

    /// <summary>
    /// Titel und Kennzahlen des Kopfs neu zählen (nach jedem Filtern und jeder Änderung der Zähler). Zählt die Grundmenge
    /// der Quelle und, mit Filtern oder Suche, die Sicht, wie die Liste sie zeigt.
    /// </summary>
    private void UpdateHeader()
    {
        var source = _selectedSource.Source;
        var filtered = IsHeaderFiltered;
        int total = 0, shown = 0, auto = 0, fresh = 0;
        foreach (var game in Games)
        {
            if (source is { } s && game.Source != s)
            {
                continue;
            }

            total++;
            if (!filtered)
            {
                auto += game.HdrAuto ? 1 : 0;
                fresh += game.IsNew ? 1 : 0;
            }
        }

        if (filtered)
        {
            foreach (var game in GamesView.OfType<GameItemViewModel>())
            {
                shown++;
                auto += game.HdrAuto ? 1 : 0;
                fresh += game.IsNew ? 1 : 0;
            }
        }
        else
        {
            shown = total;
        }

        HeaderTitle = LibraryText.SourceTitle(source);
        HeaderSourceCount = total;
        HeaderShownCount = shown;
        HeaderCountText = HeaderCount(filtered, shown, total);
        HeaderHdrAutoCount = auto;
        HeaderNewCount = fresh;
        OnPropertyChanged(nameof(IsHeaderFiltered));
    }
}
