using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Rückfrage im Stil der App (statt MessageBox). Nur nach einem Klick im Fenster der Bibliothek bzw. der Einstellungen. Mit
/// <see cref="ConfirmRequest"/> zusätzlich eine Aufzählung (was verloren geht, welche Spiele), eine Warnung und ein Häkchen
/// (E90); die Antwort steht dann in <see cref="Answer"/>. Nie höher als die Arbeitsfläche: Reicht der Platz nicht, scrollt
/// die Aufzählung, und die Knöpfe bleiben zu sehen.
/// </summary>
public partial class ConfirmDialog : ChromeWindow
{
    /// <summary>Breite mit Aufzählung oder Häkchen (sonst 480 wie bisher): Namen und Erklärungen brechen seltener um.</summary>
    public const double WideWidth = 540;

    /// <summary>
    /// Höchste Höhe der Aufzählung: Alles, was bei einem Spiel verloren gehen kann (höchstens elf kurze Punkte), passt ohne
    /// Scrollen; die Namensliste des Sammel-Entfernens scrollt ab etwa acht Spielen.
    /// </summary>
    public const double DetailsMaxHeight = 340;

    /// <summary>Abstand zum Rand der Arbeitsfläche (oben und unten zusammen).</summary>
    private const double WorkAreaMargin = 24;

    public ConfirmDialog(string title, string message, string confirmText, bool isDanger)
        : this(new ConfirmRequest(title, message, confirmText, isDanger))
    {
    }

    public ConfirmDialog(ConfirmRequest request)
    {
        InitializeComponent();
        Title = request.Title;
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        ConfirmButton.Content = request.ConfirmText;
        AutomationProperties.SetName(ConfirmButton, request.ConfirmText);
        if (request.IsDanger)
        {
            IconTint.SetResourceReference(Border.BackgroundProperty, "Hp.Brush.Danger");
            IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Hp.Brush.Danger");
        }

        DetailsScroll.MaxHeight = DetailsMaxHeight;
        if (request.Details.Count > 0)
        {
            DetailsArea.Visibility = Visibility.Visible;
            DetailsList.ItemsSource = request.Details;
            DetailsHeaderRow.Visibility = string.IsNullOrEmpty(request.DetailsHeader) ? Visibility.Collapsed : Visibility.Visible;
            DetailsHeader.Text = request.DetailsHeader ?? string.Empty;
            AutomationProperties.SetName(DetailsList, request.DetailsHeader ?? request.Title);
            AutomationProperties.SetName(DetailsScroll, request.DetailsHeader ?? request.Title);
        }

        if (!string.IsNullOrEmpty(request.Warning))
        {
            WarningArea.Visibility = Visibility.Visible;
            WarningText.Text = request.Warning;
            AutomationProperties.SetName(WarningText, request.Warning);
        }

        if (!string.IsNullOrEmpty(request.OptionText))
        {
            OptionBox.Visibility = Visibility.Visible;
            OptionText.Text = request.OptionText;
            AutomationProperties.SetName(OptionBox, request.OptionText);
            if (!string.IsNullOrEmpty(request.OptionHint))
            {
                OptionHint.Text = request.OptionHint;
                OptionHint.Visibility = Visibility.Visible;
                AutomationProperties.SetHelpText(OptionBox, request.OptionHint);
            }
        }

        if (request.Details.Count > 0 || !string.IsNullOrEmpty(request.OptionText))
        {
            Width = WideWidth;
        }

        // Für Bildschirmleser: Beim Öffnen hört man Titel und „Abbrechen“; Text, Warnung und was verloren geht stehen in der
        // Hilfe des Fensters und des Knopfs, auf dem der Fokus liegt.
        var help = HelpText(request);
        AutomationProperties.SetHelpText(this, help);
        AutomationProperties.SetHelpText(CancelButton, help);

        // Nie höher als die Arbeitsfläche (etwa 1280×800 bei 125 %): Dann gibt nur die Aufzählung nach und scrollt.
        var work = SystemParameters.WorkArea;
        if (work.Height > 0)
        {
            MaxHeight = Math.Max(MinHeight, work.Height - WorkAreaMargin);
        }

        // Fokus des Fensters schon vor dem Anzeigen: Auch ein nicht aktives Fenster gibt ihn beim Aktivieren an „Abbrechen“.
        FocusManager.SetFocusedElement(this, CancelButton);
        Loaded += (_, _) =>
        {
            LibraryMotion.FadeIn(Body, TimeSpan.Zero, offsetY: 10, durationMs: 220);
            CancelButton.Focus(); // Vorsichtige Vorgabe: Fokus auf „Abbrechen“, bestätigt wird nur ausdrücklich.
        };
    }

    /// <summary>Bestätigt und ob das Häkchen gesetzt ist (ohne Häkchen immer false).</summary>
    public ConfirmAnswer Answer => new(DialogResult == true, DialogResult == true && OptionBox.Visibility == Visibility.Visible && OptionBox.IsChecked == true);

    /// <summary>Hilfe für Bildschirmleser: Text, Warnung und die Punkte der Aufzählung, in dieser Reihenfolge.</summary>
    public static string HelpText(ConfirmRequest request)
    {
        var parts = new List<string> { request.Message };
        if (!string.IsNullOrEmpty(request.Warning))
        {
            parts.Add(request.Warning);
        }

        if (request.Details.Count > 0)
        {
            parts.Add((request.DetailsHeader is { Length: > 0 } header ? header + ": " : string.Empty) + string.Join("; ", request.Details.Select(d => d.Spoken)));
        }

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>
    /// Taste in der Aufzählung, wenn sie den Fokus hat (nur eine lange Liste, zum Scrollen): Enter bestätigt dort nicht, wer die
    /// Liste liest, soll nicht versehentlich „Entfernen“ auslösen. True, wenn die Taste damit erledigt ist.
    /// </summary>
    public static bool SwallowsInDetails(Key key) => key is Key.Enter;

    private void OnDetailsKeyDown(object sender, KeyEventArgs e) => e.Handled |= SwallowsInDetails(e.Key);

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
