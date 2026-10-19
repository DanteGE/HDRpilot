using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Rückfrage im Stil der App (statt MessageBox). Nur nach einem Klick im Fenster der Bibliothek bzw. der Einstellungen. Mit
/// <see cref="ConfirmRequest"/> zusätzlich eine Aufzählung (was verloren geht, welche Spiele), eine Warnung und ein Häkchen
/// (E90); die Antwort steht dann in <see cref="Answer"/>.
/// </summary>
public partial class ConfirmDialog : ChromeWindow
{
    /// <summary>Breite mit Aufzählung oder Häkchen (sonst 480 wie bisher): Namen und Erklärungen brechen seltener um.</summary>
    public const double WideWidth = 540;

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
            IconTint.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Hp.Brush.Danger");
            IconGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Hp.Brush.Danger");
        }

        if (request.Details.Count > 0)
        {
            DetailsArea.Visibility = Visibility.Visible;
            DetailsList.ItemsSource = request.Details;
            DetailsHeaderRow.Visibility = string.IsNullOrEmpty(request.DetailsHeader) ? Visibility.Collapsed : Visibility.Visible;
            DetailsHeader.Text = request.DetailsHeader ?? string.Empty;
            AutomationProperties.SetName(DetailsList, request.DetailsHeader ?? request.Title);
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

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
