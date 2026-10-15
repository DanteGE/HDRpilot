using System.Windows;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Views.Library;

/// <summary>Dialog „Bild wählen…“ (Titelbild, Logo, Cover je Spiel). Logik in <see cref="ArtworkPickerViewModel"/>.</summary>
public partial class ArtworkPickerDialog : ChromeWindow
{
    public ArtworkPickerDialog(ArtworkPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => DataContext = null;
        Loaded += (_, _) =>
        {
            LibraryMotion.FadeIn(Body, TimeSpan.Zero, offsetY: 10, durationMs: 260);
            Candidates.Focus();
        };
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
