using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Dialog „Bild wählen…“ (Titelbild, Logo, Cover je Spiel; E89, E93). Logik in <see cref="ArtworkPickerViewModel"/>. Das
/// Fenster ist nie höher als die Arbeitsfläche des Bildschirms der Bibliothek und wächst nur, um seine Mitte
/// (<see cref="DialogPlacement"/>); die Mitte scrollt. Hoher Kontrast über <see cref="DialogContrast"/>. „Übernehmen“ lädt
/// Bilder von SteamGridDB noch im offenen Fenster und schließt erst danach; ein Schließen während des Ladens bricht ab.
/// </summary>
public partial class ArtworkPickerDialog : ChromeWindow
{
    private readonly ArtworkPickerViewModel _viewModel;
    private readonly List<object> _reveal = [];
    private Rect? _workArea;
    private bool _closing;
    private bool _applying;
    private bool _focusInSection;

    public ArtworkPickerDialog(ArtworkPickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SetHighContrast(SystemParameters.HighContrast ? HighContrastPalette.FromSystem() : null);
        FitToWorkArea(SystemParameters.WorkArea);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.Online.FocusRequest += OnFocusRequest;
        viewModel.Online.PropertyChanged += OnOnlineChanged;
        viewModel.PropertyChanged += OnPickerChanged;
        viewModel.Online.Results.CollectionChanged += OnResultsChanged;
        SgdbSection.IsKeyboardFocusWithinChanged += OnSectionFocusChanged;
        SizeChanged += OnSizeChanged;
        SourceInitialized += (_, _) => FitToWorkArea(DialogPlacement.WorkAreaFor(Owner));
        Closing += (_, _) => BeginClosing();
        Closed += OnClosed;
        Loaded += (_, _) =>
        {
            viewModel.Online.PreviewScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            LibraryMotion.FadeIn(Body, TimeSpan.Zero, offsetY: 10, durationMs: 260);
            Candidates.Focus();
        };
    }

    /// <summary>Arbeitsfläche, nach der sich das Fenster richtet.</summary>
    internal Rect? WorkArea => _workArea;

    /// <summary>Höchste Höhe nach der Arbeitsfläche (für Tests und Bildschirmfotos auch vorgegeben).</summary>
    internal void FitToWorkArea(Rect work)
    {
        _workArea = work;
        MaxHeight = DialogPlacement.MaxHeightFor(work, MinHeight);
    }

    /// <summary>Hoher Kontrast an (Palette) oder aus (null); für Tests und Bildschirmfotos auch mit vorgegebener Palette.</summary>
    internal void SetHighContrast(HighContrastPalette? palette) => DialogContrast.Apply(Resources, palette);

    /// <summary>
    /// Nur wachsen (D19): Mehr Inhalt macht das Fenster höher, um seine Mitte und in der Arbeitsfläche; weniger Inhalt (ein
    /// anderer Reiter, ein anderer Zustand) lässt es gleich hoch, der Platz geht an die Mitte. Auch für Tests, die das
    /// Fenster nur anordnen.
    /// </summary>
    internal void Grow(double previousHeight, double newHeight)
    {
        if (previousHeight <= 0 || newHeight <= previousHeight)
        {
            return;
        }

        MinHeight = Math.Min(newHeight, MaxHeight);
        if (IsLoaded && _workArea is { } work && !double.IsNaN(Top))
        {
            Top = DialogPlacement.GrowTop(work, Top, previousHeight, newHeight);
        }
    }

    /// <summary>Folgt die Ansicht einer Bitte um Fokus? Eine Kachel nach gescheitertem Übernehmen immer, sonst nur aus dem Abschnitt heraus.</summary>
    internal static bool FollowsFocus(SteamGridDbFocusTarget target, bool focusInSection) =>
        target == SteamGridDbFocusTarget.Tile || focusInSection;

    /// <summary>Das Fenster schließt (Abbrechen, Esc, X): ab jetzt setzt nichts mehr ein Dialogergebnis.</summary>
    internal void BeginClosing() => _closing = true;

    /// <summary>
    /// „Übernehmen“: lädt gewählte Bilder von SteamGridDB, solange das Fenster offen ist (wirft nie), und schließt dann mit
    /// „true“, aber nie, wenn das Fenster inzwischen schließt. True, wenn es das Ergebnis gesetzt hat.
    /// </summary>
    internal async Task<bool> ApplyCoreAsync()
    {
        if (_applying || _closing)
        {
            return false;
        }

        _applying = true;
        try
        {
            if (await _viewModel.PrepareApplyAsync() && !_closing)
            {
                DialogResult = true;
                return true;
            }

            return false;
        }
        finally
        {
            _applying = false;
        }
    }

    private static FrameworkElement? Container(ItemsControl list, object item) =>
        list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;

    private static void FocusElement(FrameworkElement? element)
    {
        if (element is null)
        {
            return;
        }

        element.BringIntoView();
        element.Focus();
        Keyboard.Focus(element);
    }

    private static T? Descendant<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found)
            {
                return found;
            }

            if (Descendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Dispatcher.BeginInvoke(() => SetHighContrast(SystemParameters.HighContrast ? HighContrastPalette.FromSystem() : null));
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
        {
            Grow(e.PreviousSize.Height, e.NewSize.Height);
        }
    }

    private async void OnApply(object sender, RoutedEventArgs e) => await ApplyCoreAsync();

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (!_closing)
        {
            DialogResult = false;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (!_closing)
        {
            DialogResult = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.Online.FocusRequest -= OnFocusRequest;
        _viewModel.Online.PropertyChanged -= OnOnlineChanged;
        _viewModel.PropertyChanged -= OnPickerChanged;
        _viewModel.Online.Results.CollectionChanged -= OnResultsChanged;
        _viewModel.CloseSession();
        DataContext = null;
    }

    /// <summary>
    /// Merkt, ob der Tastaturfokus im Abschnitt SteamGridDB liegt. Verschwindet das fokussierte Element (der Suchknopf weicht
    /// den Ergebnissen), gilt der Fokus weiter als dort, bis ein Element außerhalb ihn bekommt.
    /// </summary>
    private void OnSectionFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _focusInSection = true;
        }
        else if (Keyboard.FocusedElement is DependencyObject focused && !ReferenceEquals(focused, this) && !SgdbSection.IsAncestorOf(focused))
        {
            _focusInSection = false;
        }
    }

    /// <summary>Fokus nach einer Suche oder „Mehr laden“: nur, solange der Tastaturfokus im Abschnitt liegt (nie aus den lokalen Bildern weg).</summary>
    private void OnFocusRequest(object? sender, SteamGridDbFocusRequestEventArgs e)
    {
        if (!FollowsFocus(e.Target, _focusInSection || SgdbSection.IsKeyboardFocusWithin))
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            switch (e.Target)
            {
                case SteamGridDbFocusTarget.FirstResult or SteamGridDbFocusTarget.FirstNewResult or SteamGridDbFocusTarget.Tile when e.Item is { } item:
                    FocusElement(Container(Results, item) ?? Container(Candidates, item));
                    break;
                case SteamGridDbFocusTarget.Retry when RetryButton.IsVisible:
                    FocusElement(RetryButton);
                    break;
                case SteamGridDbFocusTarget.SettingsLink when SettingsLink.IsVisible:
                    FocusElement(SettingsLink);
                    break;
                case SteamGridDbFocusTarget.MatchSearch when _viewModel.Online.IsMatchOpen:
                    UpdateLayout();
                    FocusElement(MatchSearch);
                    if (LibraryMotion.Instance.IsFull)
                    {
                        LibraryMotion.FadeIn(MatchPanel, TimeSpan.Zero, offsetY: 0, durationMs: 160);
                    }

                    break;
                case SteamGridDbFocusTarget.MatchList:
                    UpdateLayout();
                    FocusElement(FirstEntry());
                    break;
            }
        });
    }

    private Button? FirstEntry() => MatchEntries.Items.Count > 0
        ? Descendant<Button>(MatchEntries.ItemContainerGenerator.ContainerFromIndex(0))
        : null;

    /// <summary>Enter sucht (und übernimmt nicht), Pfeil runter geht in die Liste.</summary>
    private void OnMatchSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_viewModel.Online.SearchGamesCommand.CanExecute(null))
            {
                _viewModel.Online.SearchGamesCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down && FirstEntry() is { } first)
        {
            FocusElement(first);
            e.Handled = true;
        }
    }

    /// <summary>Enter oder Leertaste wählt einen Eintrag (und übernimmt nicht).</summary>
    private void OnMatchEntryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && sender is Button { DataContext: SteamGridDbMatchEntry entry })
        {
            if (_viewModel.Online.ChooseMatchCommand.CanExecute(entry))
            {
                _viewModel.Online.ChooseMatchCommand.Execute(entry);
            }

            MatchButton.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Esc in der Spielwahl klappt sie zu und schließt den Dialog nicht.</summary>
    private void OnMatchPanelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.Online.IsMatchOpen = false;
            MatchButton.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Die Live-Ansage meldet nur Zustandswechsel und fertige Seiten (nie jede Vorschau).</summary>
    private void OnOnlineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SteamGridDbSearchViewModel.Announcement))
        {
            RaiseLiveRegion(Announcement);
        }
    }

    private void OnPickerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArtworkPickerViewModel.BusyText))
        {
            RaiseLiveRegion(BusyLine);
        }
    }

    private void RaiseLiveRegion(UIElement element)
    {
        if ((UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element)) is { } peer)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged));
        }
    }

    /// <summary>
    /// Neue Kacheln blenden ein (Voll: Ein- und Aufsteigen, 18 ms gestaffelt; Reduziert: nur kurz einblenden; Aus: nichts).
    /// Einmalig, danach läuft nichts mehr.
    /// </summary>
    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || !LibraryMotion.Enabled)
        {
            return;
        }

        // Eine Seite kommt als einzelne Einfügungen: gesammelt und gemeinsam gestaffelt.
        var first = _reveal.Count == 0;
        _reveal.AddRange(e.NewItems.Cast<object>());
        if (!first)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var full = LibraryMotion.Instance.IsFull;
            var index = 0;
            var items = _reveal.ToList();
            _reveal.Clear();
            foreach (var item in items)
            {
                if (Container(Results, item) is { } container)
                {
                    var delay = full ? TimeSpan.FromMilliseconds(Math.Min(index * 18, 430)) : TimeSpan.Zero;
                    LibraryMotion.FadeIn(container, delay, offsetY: full ? 10 : 0, durationMs: full ? 220 : 150);
                }

                index++;
            }
        });
    }
}
