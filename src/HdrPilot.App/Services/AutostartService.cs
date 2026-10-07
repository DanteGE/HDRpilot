using System.IO;
using HdrPilot.App.Resources;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services;

public enum AutostartReconcileResult
{
    Unchanged,

    /// <summary>Eintrag fehlte, obwohl die Konfiguration Autostart verlangt; neu angelegt.</summary>
    Created,

    /// <summary>Eintrag zeigte auf einen anderen Pfad (EXE verschoben); korrigiert.</summary>
    PathUpdated,

    Failed,
}

/// <summary>
/// Autostart über HKCU\Software\Microsoft\Windows\CurrentVersion\Run, Wert „HdrPilot“ =
/// <c>"&lt;Pfad zur EXE&gt;" --minimized</c>. Deaktivieren im Task-Manager (StartupApproved) bleibt unberührt.
/// </summary>
public sealed class AutostartService(IRegistry registry, string? executablePath, ILogger? logger = null) : IAutostartService
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "HDRpilot";
    public const string MinimizedArgument = "--minimized";

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Befehlszeile, die im Run-Schlüssel stehen soll; null, wenn der EXE-Pfad unbekannt ist.</summary>
    public string? ExpectedCommand => string.IsNullOrWhiteSpace(executablePath) ? null : BuildCommand(executablePath);

    /// <summary>Aktueller Wert im Run-Schlüssel oder null.</summary>
    public string? CurrentCommand
    {
        get
        {
            using var key = registry.OpenKey(RegistryHiveKind.CurrentUser, RunKeyPath);
            return key?.GetValue(ValueName) as string;
        }
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(CurrentCommand);

    public static string BuildCommand(string executablePath) => "\"" + executablePath.Trim().Trim('"') + "\" " + MinimizedArgument;

    /// <summary>
    /// Pfad, der in den Run-Schlüssel darf: <see cref="Environment.ProcessPath"/>, außer HdrPilot läuft über den
    /// .NET-Host (<c>dotnet HdrPilot.dll</c>) – dann wäre der Eintrag „dotnet.exe --minimized“ und startete nichts.
    /// </summary>
    public static string? ResolveExecutablePath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(processPath.Trim().Trim('"'));
        return string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase) ? null : processPath;
    }

    /// <summary>Setzt oder entfernt den Eintrag. Wirft bei Registry-Fehlern (der Aufrufer meldet sie).</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var command = ExpectedCommand ?? throw new InvalidOperationException(ShellStrings.AutostartPathUnknown);
            using var key = registry.CreateKey(RegistryHiveKind.CurrentUser, RunKeyPath);
            key.SetValue(ValueName, command);
            _logger.LogInformation("Autostart eingetragen: {Command}", command);
            return;
        }

        if (CurrentCommand is null)
        {
            return;
        }

        using (var key = registry.CreateKey(RegistryHiveKind.CurrentUser, RunKeyPath))
        {
            key.DeleteValue(ValueName);
        }

        _logger.LogInformation("Autostart entfernt.");
    }

    /// <summary>
    /// Beim Start: Verlangt die Konfiguration Autostart und zeigt der Eintrag auf einen anderen Pfad (EXE verschoben)
    /// oder fehlt er, wird er korrigiert. Ist Autostart in der Konfiguration aus, bleibt die Registry unberührt.
    /// Wirft nicht.
    /// </summary>
    public AutostartReconcileResult Reconcile(bool startWithWindows)
    {
        if (!startWithWindows || ExpectedCommand is not { } expected)
        {
            return AutostartReconcileResult.Unchanged;
        }

        try
        {
            var current = CurrentCommand;
            if (string.Equals(current?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            {
                return AutostartReconcileResult.Unchanged;
            }

            using (var key = registry.CreateKey(RegistryHiveKind.CurrentUser, RunKeyPath))
            {
                key.SetValue(ValueName, expected);
            }

            _logger.LogInformation("Autostart-Eintrag korrigiert: vorher {Before}, jetzt {After}", current ?? "(fehlte)", expected);
            return current is null ? AutostartReconcileResult.Created : AutostartReconcileResult.PathUpdated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Autostart-Eintrag ließ sich nicht korrigieren.");
            return AutostartReconcileResult.Failed;
        }
    }
}

/// <summary>
/// Autostart für Läufe mit eigener Ablage (<c>--config-dir</c>, Tests und Prüfskripte): Der Schalter lebt nur in der
/// Konfiguration, die echte Registry wird nie verändert.
/// </summary>
public sealed class ConfigOnlyAutostartService(ConfigService config, ILogger? logger = null) : IAutostartService
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public bool IsEnabled => config.Current.Settings.StartWithWindows;

    public void SetEnabled(bool enabled) =>
        _logger.LogInformation("Eigene Ablage: Autostart ({State}) wird nur in der Konfiguration gespeichert, nicht in der Registry.", enabled ? "an" : "aus");
}
