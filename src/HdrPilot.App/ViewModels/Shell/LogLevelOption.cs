using HdrPilot.App.Resources;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>Eintrag der Auswahl „Protokollstufe“ (Trace bis Error).</summary>
public sealed record LogLevelOption(LogLevel Value, string Label)
{
    public static IReadOnlyList<LogLevelOption> All { get; } =
    [
        new(LogLevel.Trace, ShellStrings.LogLevelTrace),
        new(LogLevel.Debug, ShellStrings.LogLevelDebug),
        new(LogLevel.Information, ShellStrings.LogLevelInformation),
        new(LogLevel.Warning, ShellStrings.LogLevelWarning),
        new(LogLevel.Error, ShellStrings.LogLevelError),
    ];

    /// <summary>Begrenzt auf Trace..Error (Critical und None werden zu Error).</summary>
    public static LogLevel Clamp(LogLevel level) => level switch
    {
        < LogLevel.Trace => LogLevel.Trace,
        > LogLevel.Error => LogLevel.Error,
        _ => level,
    };

    public override string ToString() => Label;
}
