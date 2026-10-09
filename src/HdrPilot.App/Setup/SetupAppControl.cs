using System.Diagnostics;
using System.IO;
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

    private readonly string _instanceId;
    private readonly string _installedExePath;
    private readonly Func<IProcessSnapshotProvider> _snapshots;

    public SetupAppControl(SetupLocations locations)
        : this(locations.InstanceId, locations.ExePath)
    {
    }

    /// <param name="snapshots">Liefert die Prozessliste (Standard: <see cref="NtProcessSnapshotProvider"/>; Tests: feste Liste).</param>
    public SetupAppControl(string instanceId, string installedExePath, Func<IProcessSnapshotProvider>? snapshots = null)
    {
        _instanceId = instanceId;
        _installedExePath = installedExePath;
        _snapshots = snapshots ?? (() => new NtProcessSnapshotProvider());
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
        if (SingleInstance.OwnerProcessId(_instanceId) is not { } processId)
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
            if (SingleInstance.IsRunning(_instanceId))
            {
                SingleInstance.TrySend(_instanceId, ["--exit"], SendTimeout);
            }

            while (IsRunning(snapshot))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                Thread.Sleep(PollInterval);
            }

            return true;
        }
        finally
        {
            (snapshot as IDisposable)?.Dispose();
        }
    }

    public bool Start(string exePath, string arguments)
    {
        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
        };

        // Nur starten, nie warten: das Handle wird sofort geschlossen.
        using var process = Process.Start(info);
        return process is not null;
    }

    private bool IsRunning(IProcessSnapshotProvider snapshot) => SingleInstance.IsRunning(_instanceId) || RunsFromExe(snapshot);

    private bool RunsFromExe(IProcessSnapshotProvider snapshot)
    {
        var own = Environment.ProcessId;
        return snapshot.GetProcesses().Any(p => p.ProcessId != own && p.ExePath is { } path && PathUtil.PathEquals(path, _installedExePath));
    }
}
