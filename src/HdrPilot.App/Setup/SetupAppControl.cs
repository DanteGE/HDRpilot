using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using HdrPilot.App.Infrastructure;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Util;

namespace HdrPilot.App.Setup;

/// <summary>Die installierte App erkennen, beenden und starten (für Setup und Entfernen).</summary>
public interface ISetupAppControl
{
    /// <summary>True, wenn die Einzelinstanz läuft oder ein Prozess aus der installierten EXE.</summary>
    bool IsRunning();

    /// <summary>
    /// EXE der laufenden Einzelinstanz oder null (keine Instanz, Pfad nicht lesbar). Kann eine portable Kopie aus einem
    /// anderen Ordner sein: Sie teilt sich die Kennung mit der installierten App.
    /// </summary>
    string? InstanceExePath();

    /// <summary>Bittet die App über den Befehlsweg zu enden und wartet. True, wenn danach nichts mehr läuft.</summary>
    bool Stop(TimeSpan timeout);

    /// <summary>Startet die EXE, ohne auf sie zu warten. True, wenn der Prozess gestartet wurde.</summary>
    bool Start(string exePath, string arguments);
}

/// <summary>Einzelinstanz der App, wie Setup und Entfernen sie sehen: Sperre, Nachrichtenfenster, Befehlsweg (Naht für Tests).</summary>
public interface ISetupInstanceChannel
{
    /// <summary>Sperre der Einzelinstanz vorhanden.</summary>
    bool IsRunning(string id);

    /// <summary>Nachrichtenfenster vorhanden, die Instanz nimmt also Befehle an.</summary>
    bool CommandWindowExists(string id);

    /// <summary>Prozess-ID über das Nachrichtenfenster (ohne Prozess-Handle) oder null.</summary>
    int? OwnerProcessId(string id);

    /// <summary>Sendet Befehle; true, wenn die Instanz sie angenommen hat.</summary>
    bool TrySend(string id, IReadOnlyList<string> args, TimeSpan timeout);
}

/// <summary>Der echte Befehlsweg (<see cref="SingleInstance"/>).</summary>
public sealed class SingleInstanceChannel : ISetupInstanceChannel
{
    public static SingleInstanceChannel Instance { get; } = new();

    public bool IsRunning(string id) => SingleInstance.IsRunning(id);

    public bool CommandWindowExists(string id) => SingleInstance.CommandWindowExists(id);

    public int? OwnerProcessId(string id) => SingleInstance.OwnerProcessId(id);

    public bool TrySend(string id, IReadOnlyList<string> args, TimeSpan timeout) => SingleInstance.TrySend(id, args, timeout);
}

/// <summary>
/// Echte App-Steuerung. Erkennen ohne Handle auf fremde Prozesse: Einzelinstanz-Sperre und EXE-Pfade aus der
/// Prozessliste (<see cref="NtProcessSnapshotProvider"/>, E65). Beenden nur über den vorhandenen Befehlsweg
/// (<c>--exit</c> an die laufende Instanz, E67); endet sie nicht, meldet <see cref="Stop"/> false und der Aufrufer sagt
/// es dem Nutzer. Der eigene Prozess zählt nie mit (das Entfernen läuft zuerst aus der installierten EXE).
/// </summary>
public sealed class SetupAppControl : ISetupAppControl
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Abstand, in dem nach dem Nachrichtenfenster einer gerade startenden Instanz gesehen wird.</summary>
    private static readonly TimeSpan SendRetryInterval = TimeSpan.FromSeconds(1);

    private readonly string _instanceId;
    private readonly string _installedExePath;
    private readonly Func<IProcessSnapshotProvider> _snapshots;
    private readonly ISetupInstanceChannel _instance;

    public SetupAppControl(SetupLocations locations)
        : this(locations.InstanceId, locations.ExePath)
    {
    }

    /// <param name="snapshots">Liefert die Prozessliste (Standard: <see cref="NtProcessSnapshotProvider"/>; Tests: feste Liste).</param>
    /// <param name="instance">Befehlsweg zur Einzelinstanz (Standard: <see cref="SingleInstanceChannel"/>; Tests: Attrappe).</param>
    public SetupAppControl(string instanceId, string installedExePath, Func<IProcessSnapshotProvider>? snapshots = null, ISetupInstanceChannel? instance = null)
    {
        _instanceId = instanceId;
        _installedExePath = installedExePath;
        _snapshots = snapshots ?? (() => new NtProcessSnapshotProvider());
        _instance = instance ?? SingleInstanceChannel.Instance;
    }

    public bool IsRunning()
    {
        var snapshot = _snapshots();
        try
        {
            return IsRunning(snapshot);
        }
        finally
        {
            (snapshot as IDisposable)?.Dispose();
        }
    }

    /// <summary>Prozess-ID über das Nachrichtenfenster der Instanz, Pfad aus der Prozessliste (kein Handle).</summary>
    public string? InstanceExePath()
    {
        if (_instance.OwnerProcessId(_instanceId) is not { } processId)
        {
            return null;
        }

        var snapshot = _snapshots();
        try
        {
            return snapshot.GetProcesses().FirstOrDefault(p => p.ProcessId == processId)?.ExePath;
        }
        finally
        {
            (snapshot as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Stellt <c>--exit</c> genau einmal zu: Hat die Instanz ihre Sperre, aber noch kein Nachrichtenfenster (sie startet
    /// gerade), wird etwa jede Sekunde erneut nachgesehen, solange die Frist läuft. Ist das Fenster da, wird gesendet und
    /// danach nie wieder (auch wenn die Antwort ausbleibt: eine zweite Aufforderung käme womöglich doppelt an).
    /// </summary>
    public bool Stop(TimeSpan timeout)
    {
        var snapshot = _snapshots();
        try
        {
            if (!IsRunning(snapshot))
            {
                return true;
            }

            var deadline = DateTime.UtcNow + timeout;
            var exitSent = false;
            var nextAttempt = DateTime.MinValue;
            while (true)
            {
                if (!exitSent && DateTime.UtcNow >= nextAttempt && _instance.IsRunning(_instanceId))
                {
                    if (_instance.CommandWindowExists(_instanceId))
                    {
                        exitSent = true;
                        _instance.TrySend(_instanceId, ["--exit"], SendTimeout);
                    }
                    else
                    {
                        nextAttempt = DateTime.UtcNow + SendRetryInterval;
                    }
                }

                if (!IsRunning(snapshot))
                {
                    return true;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                Thread.Sleep(PollInterval);
            }
        }
        finally
        {
            (snapshot as IDisposable)?.Dispose();
        }
    }

    /// <summary>Startet über <see cref="LaunchFor"/>; wirft <see cref="Win32Exception"/>, wenn die Shell ablehnt.</summary>
    public bool Start(string exePath, string arguments) => ShellLaunch.Run(LaunchFor(exePath, arguments));

    /// <summary>
    /// Start über die Shell (ShellExecuteEx): Die App erbt keine Handles des Setups (sonst hielte sie etwa die umgeleitete
    /// Ausgabe eines Skripts offen, das den Setup aufruft und auf das Ende seiner Ausgabe wartet). Ohne Zonenprüfung: Eine
    /// EXE mit Download-Markierung (Zone.Identifier; etwa eine von Hand aus einem ZIP kopierte ältere Version, deren
    /// Markierung beim Ersetzen auf die neue EXE übergeht, oder eine portable Kopie) löste sonst eine Sicherheitsabfrage
    /// oder SmartScreen aus, die den Start aufhält oder scheitern lässt. Ohne Fehlermeldung der Shell (der Setup
    /// protokolliert selbst), erst zurück, wenn gestartet (der Setup endet womöglich gleich danach), ohne Prozess-Handle.
    /// Arbeitsordner ist der der EXE.
    /// </summary>
    public static ShellLaunch LaunchFor(string exePath, string arguments) =>
        new(exePath, arguments, Path.GetDirectoryName(exePath) ?? string.Empty, ShellLaunch.NoAsync | ShellLaunch.NoUi | ShellLaunch.NoZoneChecks);

    private bool IsRunning(IProcessSnapshotProvider snapshot) => _instance.IsRunning(_instanceId) || RunsFromExe(snapshot);

    private bool RunsFromExe(IProcessSnapshotProvider snapshot)
    {
        var own = Environment.ProcessId;
        return snapshot.GetProcesses().Any(p => p.ProcessId != own && p.ExePath is { } path && PathUtil.PathEquals(path, _installedExePath));
    }
}

/// <summary>
/// Ein Start über ShellExecuteEx: Datei, Parameter, Arbeitsordner und Schalter (SEE_MASK_*). Nie mit Prozess-Handle
/// (<see cref="NoCloseProcess"/> wird beim Start entfernt), nie ein fremder Prozess geöffnet.
/// </summary>
public sealed record ShellLaunch(string File, string Parameters, string Directory, uint Mask)
{
    /// <summary>SEE_MASK_NOASYNC: erst zurück, wenn der Start erledigt ist.</summary>
    public const uint NoAsync = ShellExecuteNative.SEE_MASK_NOASYNC;

    /// <summary>SEE_MASK_FLAG_NO_UI: keine Fehlermeldung der Shell.</summary>
    public const uint NoUi = ShellExecuteNative.SEE_MASK_FLAG_NO_UI;

    /// <summary>SEE_MASK_NOZONECHECKS: keine Prüfung der Download-Markierung (Sicherheitsabfrage, SmartScreen).</summary>
    public const uint NoZoneChecks = ShellExecuteNative.SEE_MASK_NOZONECHECKS;

    /// <summary>SEE_MASK_NOCLOSEPROCESS: lieferte ein Prozess-Handle, das offen bliebe.</summary>
    public const uint NoCloseProcess = ShellExecuteNative.SEE_MASK_NOCLOSEPROCESS;

    /// <summary>Startet auf einem STA-Thread (die Shell braucht eins). True, wenn gestartet; sonst <see cref="Win32Exception"/>.</summary>
    public static bool Run(ShellLaunch launch) => SetupSupport.OnSta("HDRpilot Setup Start", () => Execute(launch));

    private static unsafe bool Execute(ShellLaunch launch)
    {
        fixed (char* file = launch.File, parameters = launch.Parameters, directory = launch.Directory)
        {
            var info = new ShellExecuteNative.SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(ShellExecuteNative.SHELLEXECUTEINFOW),
                fMask = launch.Mask & ~NoCloseProcess,
                lpFile = file,
                lpParameters = launch.Parameters.Length == 0 ? null : parameters,
                lpDirectory = launch.Directory.Length == 0 ? null : directory,
                nShow = ShellExecuteNative.SW_SHOWNORMAL,
            };
            if (!ShellExecuteNative.ShellExecuteExW(&info))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        return true;
    }
}

// shellapi.h (SDK 10.0.26100.0): ShellExecuteExW. Startet eine Datei über die Shell; öffnet keinen fremden Prozess.
#pragma warning disable IDE1006 // Namen folgen shellapi.h

internal static unsafe partial class ShellExecuteNative
{
    public const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    public const uint SEE_MASK_NOASYNC = 0x00000100;
    public const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    public const uint SEE_MASK_NOZONECHECKS = 0x00800000;
    public const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct SHELLEXECUTEINFOW
    {
        public uint cbSize;
        public uint fMask;
        public nint hwnd;
        public char* lpVerb;
        public char* lpFile;
        public char* lpParameters;
        public char* lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        public char* lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIconOrMonitor;
        public nint hProcess;
    }

    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShellExecuteExW(SHELLEXECUTEINFOW* pExecInfo);
}
#pragma warning restore IDE1006
