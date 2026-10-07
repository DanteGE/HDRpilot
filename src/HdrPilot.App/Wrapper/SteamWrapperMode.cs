using HdrPilot.App.Infrastructure;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Launch;

namespace HdrPilot.App.Wrapper;

/// <summary>
/// Einstieg der Steam-Startoption in der App (siehe <see cref="SteamWrapper"/>): verbindet den Wrapper mit der
/// Einzelinstanz-Sperre (läuft HdrPilot?) und WM_COPYDATA (Befehl weiterreichen) und schreibt nach logs\wrap.log.
/// Kein Fenster, kein Tray, kein Design, keine Automatik; die Tray-App wird hier nie gestartet.
/// </summary>
internal static class SteamWrapperMode
{
    /// <summary>So lange wird auf das Nachrichtenfenster einer gerade startenden Instanz gewartet.</summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);

    public static void Run(WrapRequest request)
    {
        string id;
        WrapLog? log = null;
        try
        {
            id = SingleInstance.IdFor(request.ConfigDir);
            var paths = request.ConfigDir is { } dir
                ? AppPaths.InDirectory(System.IO.Path.GetFullPath(dir))
                : AppPaths.Default(SystemEnvironment.Instance);
            log = new WrapLog(paths.LogDirectory);
        }
        catch (Exception)
        {
            // Ungültige Ablage: nie die normale Instanz ansprechen, das Spiel startet trotzdem (ohne Protokoll).
            id = "ungueltig";
        }

        new SteamWrapper(new InstanceChannel(id), new DirectGameStarter(), SystemWrapContext.Instance, log).Run(request);
    }

    private sealed class InstanceChannel(string id) : IWrapInstanceChannel
    {
        public bool IsInstanceRunning() => SingleInstance.IsRunning(id);

        public bool TrySend(IReadOnlyList<string> args) => SingleInstance.TrySend(id, args, SendTimeout);
    }
}
