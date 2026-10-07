using System.Windows;
using System.Windows.Automation;
using HdrPilot.App.Infrastructure;

namespace HdrPilot.App.Views.Library;

/// <summary>Rückfrage im Stil der App (statt MessageBox). Nur nach einem Klick im Fenster der Bibliothek.</summary>
public partial class ConfirmDialog : ChromeWindow
{
    public ConfirmDialog(string title, string message, string confirmText, bool isDanger)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        AutomationProperties.SetName(ConfirmButton, confirmText);
        if (isDanger)
        {
            IconTint.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Hp.Brush.Danger");
            IconGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Hp.Brush.Danger");
        }

        Loaded += (_, _) =>
        {
            LibraryMotion.FadeIn(Body, TimeSpan.Zero, offsetY: 10, durationMs: 220);
            CancelButton.Focus(); // Vorsichtige Vorgabe: Fokus auf „Abbrechen“, bestätigt wird nur ausdrücklich.
        };
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
