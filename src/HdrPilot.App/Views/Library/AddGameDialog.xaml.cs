using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Views.Library;

/// <summary>Dialog „Spiel hinzufügen“ (EXE oder Ordner, Name frei wählbar). Logik in <see cref="AddGameViewModel"/>.</summary>
public partial class AddGameDialog : ChromeWindow
{
    private readonly AddGameViewModel _viewModel;

    public AddGameDialog(AddGameViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) =>
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            DataContext = null;
        };
        Loaded += (_, _) =>
        {
            LibraryMotion.FadeIn(Body, TimeSpan.Zero, offsetY: 10, durationMs: 220);
            PathBox.Focus();
        };
    }

    private void OnCloseRequested(object? sender, bool confirmed)
    {
        if (IsVisible)
        {
            DialogResult = confirmed;
        }
    }
}
