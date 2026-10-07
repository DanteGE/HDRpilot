using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Services;
using HdrPilot.Core.Config;

namespace HdrPilot.App;

// GERÜST: wird durch die Spieleliste ersetzt (Hauptfenster, Symbole, Verknüpfungen).
public sealed partial class AppServices
{
    private partial IGameIconService CreateIconService() => new NoIcons();

    private partial IShortcutService CreateShortcutService() => new NoShortcuts();

    private partial Window CreateMainWindow() => new MainWindow();

    private sealed class NoIcons : IGameIconService
    {
        public ImageSource? GetCachedIcon(GameConfig game) => null;

        public Task<ImageSource?> GetIconAsync(GameConfig game, CancellationToken cancellationToken = default) => Task.FromResult<ImageSource?>(null);
    }

    private sealed class NoShortcuts : IShortcutService
    {
        public string CreateDesktopShortcut(GameConfig game) => throw new NotSupportedException();

        public string CreateStartMenuShortcut(GameConfig game) => throw new NotSupportedException();
    }
}
