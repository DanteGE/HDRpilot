using System.Globalization;
using System.Text.Json;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;

namespace HdrPilot.Core.AutoHdr;

/// <summary>Stand des gemerkten globalen Auto-HDR-Schalters (E88).</summary>
public enum AutoHdrEntryStatus
{
    /// <summary>Gemerkt, Schreiben steht aus oder war beim Absturz gerade im Gang (beim Zurückstellen geklärt).</summary>
    Pending,

    /// <summary>Geschrieben.</summary>
    Applied,

    /// <summary>
    /// Erledigt, nur die Datei blieb: nie zurückstellen, nur noch löschen. Frühere Form des Kennzeichens (Vorabversionen),
    /// wird weiter gelesen, aber nicht mehr geschrieben: Eine Sperre, die das Löschen verhindert, verhindert unter NTFS auch
    /// das Ersetzen der Datei; das Kennzeichen steht deshalb in <see cref="AutoHdrGlobalDone"/>.
    /// </summary>
    Released,
}

/// <summary>Zustand vor dem Schreiben: fehlte (<see cref="GpuPreferenceValueKind.Missing"/>) oder sauberer REG_SZ.</summary>
public sealed class AutoHdrOriginalValue
{
    public GpuPreferenceValueKind Kind { get; set; }

    /// <summary>Text des REG_SZ (bei einem sauberen REG_SZ genau seine Rohdaten); null, wenn der Wert fehlte.</summary>
    public string? Text { get; set; }

    public static AutoHdrOriginalValue From(GpuPreferenceValue value) => value.IsMissing
        ? new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.Missing }
        : new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.String, Text = value.Text };

    public GpuPreferenceValue ToValue() =>
        Kind == GpuPreferenceValueKind.Missing ? GpuPreferenceValue.Missing : GpuPreferenceValue.FromString(Text ?? string.Empty);

    internal bool IsValid() =>
        (Kind == GpuPreferenceValueKind.Missing && Text is null)
        || (Kind == GpuPreferenceValueKind.String && Text is not null && GpuPreferenceText.TryParse(Text, out _));
}

/// <summary>
/// Inhalt von autohdr-global.json (Datenordner, maschinenbezogen): existiert nur, solange HDRpilot den globalen
/// Auto-HDR-Schalter für einen Spielstart hält. Wird vor dem Schreiben angelegt und nach dem Zurückstellen gelöscht; findet
/// ihn der nächste Start (Absturz von HDRpilot) oder das Entfernen über den Setup, stellen sie zurück. Ließ er sich nicht
/// löschen, hält autohdr-global.done (<see cref="AutoHdrGlobalDone"/>) fest, dass er erledigt ist: dann wird er nie
/// zurückgestellt, nur noch gelöscht.
/// </summary>
public sealed class AutoHdrGlobalMarker
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Schlüssel, für den der Merker gilt (<see cref="IGpuPreferenceStore.KeyName"/>).</summary>
    public string RegistryKey { get; set; } = string.Empty;

    public string ValueName { get; set; } = AutoHdrRegistryLocation.GlobalValueName;

    /// <summary>
    /// <see cref="AutoHdrEntryStatus.Pending"/> oder <see cref="AutoHdrEntryStatus.Applied"/>
    /// (<see cref="AutoHdrEntryStatus.Released"/> nur aus Vorabversionen).
    /// </summary>
    public AutoHdrEntryStatus Status { get; set; }

    public AutoHdrOriginalValue Original { get; set; } = new();

    /// <summary>Bisheriger Wert von <c>AutoHDREnable</c>, etwa „0“; null, wenn das Token fehlte.</summary>
    public string? OriginalToken { get; set; }

    /// <summary>Genau der Text, den HDRpilot geschrieben hat.</summary>
    public string Written { get; set; } = string.Empty;

    /// <summary>Spiele, für deren Start der Schalter gesetzt wurde (für das Protokoll).</summary>
    public List<string> Games { get; set; } = [];

    /// <summary>Windows-Build beim Setzen (das Format ist undokumentiert).</summary>
    public int WindowsBuild { get; set; }

    public DateTimeOffset SetAt { get; set; }

    internal bool IsValid() =>
        Version == CurrentVersion
        && !string.IsNullOrWhiteSpace(RegistryKey)
        && string.Equals(ValueName, AutoHdrRegistryLocation.GlobalValueName, StringComparison.OrdinalIgnoreCase)
        && Enum.IsDefined(Status)
        && Original is not null && Original.IsValid()
        && GpuPreferenceText.TryParse(Written, out _)
        && !string.Equals(Original.Text, Written, StringComparison.Ordinal)
        && Games is not null;
}

/// <summary>
/// Inhalt von autohdr-global.done (neben autohdr-global.json): Der Merker mit diesem Zeitpunkt und Text ist erledigt
/// (zurückgestellt, vom Nutzer übernommen oder nie geschrieben), ließ sich aber nicht löschen, etwa weil ein Virenscanner
/// ihn sperrt. Eine eigene Datei, weil eine solche Sperre unter NTFS auch das Ersetzen des Merkers verhindert (Löschen und
/// Ersetzen brauchen beide das Löschrecht an der Datei); diese hier sperrt sie nicht. Der nächste Start und das Entfernen
/// über den Setup stellen einen passenden Merker nie zurück und löschen beide Dateien. Ein Merker eines späteren Setzens
/// passt nicht (anderer Zeitpunkt).
/// </summary>
public sealed class AutoHdrGlobalDone
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary><see cref="AutoHdrGlobalMarker.SetAt"/> des erledigten Merkers.</summary>
    public DateTimeOffset SetAt { get; set; }

    /// <summary><see cref="AutoHdrGlobalMarker.Written"/> des erledigten Merkers.</summary>
    public string Written { get; set; } = string.Empty;

    public static AutoHdrGlobalDone Of(AutoHdrGlobalMarker marker) => new() { SetAt = marker.SetAt, Written = marker.Written };

    /// <summary>Gilt das Kennzeichen genau diesem Merker?</summary>
    public bool Matches(AutoHdrGlobalMarker marker) =>
        SetAt == marker.SetAt && string.Equals(Written, marker.Written, StringComparison.Ordinal);

    internal bool IsValid() => Version == CurrentVersion && GpuPreferenceText.TryParse(Written, out _);
}

/// <summary>Ergebnis beim Lesen einer Zustandsdatei.</summary>
internal enum AutoHdrFileState
{
    Missing,
    Loaded,

    /// <summary>Kein gültiges JSON oder unbekannter Inhalt (gesichert und entfernt, sofern das gelang).</summary>
    Corrupt,

    /// <summary>Nicht lesbar (gesperrt, keine Rechte): unverändert lassen.</summary>
    Unreadable,
}

/// <param name="Problem">Grund bei <see cref="AutoHdrFileState.Corrupt"/> und <see cref="AutoHdrFileState.Unreadable"/>.</param>
/// <param name="Backup">Sicherung einer defekten Datei; null, wenn die Sicherung scheiterte (dann bleibt die Datei).</param>
internal sealed record AutoHdrFileLoad<T>(AutoHdrFileState State, T? Data, string? Problem = null, string? Backup = null)
    where T : class;

/// <summary>Lesen, Schreiben (atomar) und Sichern der Zustandsdatei von Auto HDR.</summary>
internal static class AutoHdrFiles
{
    /// <param name="clock">Für den Namen der Sicherung einer defekten Datei; null: nicht sichern, die Datei bleibt (Setup).</param>
    public static AutoHdrFileLoad<T> Load<T>(IFileSystem fileSystem, string path, Func<T, bool> isValid, IClock? clock)
        where T : class
    {
        string text;
        try
        {
            text = fileSystem.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return new AutoHdrFileLoad<T>(AutoHdrFileState.Missing, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AutoHdrFileLoad<T>(AutoHdrFileState.Unreadable, null, ex.Message);
        }

        string problem;
        try
        {
            var data = JsonSerializer.Deserialize<T>(text, ConfigJson.Options);
            if (data is not null && isValid(data))
            {
                return new AutoHdrFileLoad<T>(AutoHdrFileState.Loaded, data);
            }

            problem = "unbekannter oder unvollständiger Inhalt";
        }
        catch (JsonException ex)
        {
            problem = ex.Message;
        }

        return new AutoHdrFileLoad<T>(AutoHdrFileState.Corrupt, null, problem, clock is null ? null : Backup(fileSystem, path, text, clock));
    }

    public static void Save<T>(IFileSystem fileSystem, string path, T data) =>
        fileSystem.WriteAllTextAtomic(path, JsonSerializer.Serialize(data, ConfigJson.Options));

    /// <summary>Sichert eine defekte Datei als „&lt;Datei&gt;.defekt-&lt;Zeit&gt;.bak“ und entfernt sie dann. Null: nicht gesichert, Datei bleibt.</summary>
    private static string? Backup(IFileSystem fileSystem, string path, string text, IClock clock)
    {
        var backup = path + ".defekt-" + clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak";
        try
        {
            fileSystem.WriteAllTextAtomic(backup, text);
            fileSystem.DeleteFile(path);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Was beim Zurückstellen geschah.</summary>
internal enum AutoHdrRestoreOutcome
{
    /// <summary>Nach R1 oder R2 zurückgestellt.</summary>
    Restored,

    /// <summary>Der Wert steht schon auf dem Original (nie geschrieben oder schon zurückgestellt): nichts getan.</summary>
    AlreadyOriginal,

    /// <summary>R3: extern geändert (etwa vom Nutzer in den Windows-Einstellungen), bleibt stehen.</summary>
    LeftAsIs,
}

/// <param name="Before">Wert vor dem Zurückstellen.</param>
/// <param name="After">Text danach; null, wenn der Wert gelöscht wurde oder unverändert blieb.</param>
internal sealed record AutoHdrRestoreResult(AutoHdrRestoreOutcome Outcome, GpuPreferenceRestoreRule? Rule, GpuPreferenceValue Before, string? After);

/// <summary>Gemeinsames Zurückstellen für die App (nach der Sitzung, nach einem Absturz) und das Entfernen über den Setup.</summary>
internal static class AutoHdrRestore
{
    /// <summary>
    /// Liest den aktuellen Wert und stellt zurück: steht er schon auf dem Original, nichts; sonst nach R1/R2/R3
    /// (<see cref="GpuPreferenceText.DecideRestore"/>). Wirft bei Registry-Fehlern (der Aufrufer behält den Merker).
    /// </summary>
    public static AutoHdrRestoreResult Apply(IGpuPreferenceStore store, AutoHdrGlobalMarker marker)
    {
        var current = store.Read(marker.ValueName);
        var originalValue = marker.Original.ToValue();
        if (GpuPreferenceText.SameValue(current, originalValue))
        {
            return new AutoHdrRestoreResult(AutoHdrRestoreOutcome.AlreadyOriginal, null, current, null);
        }

        var decision = GpuPreferenceText.DecideRestore(current, originalValue, marker.OriginalToken, marker.Written, GpuPreferenceText.GlobalOn);
        if (decision.Delete)
        {
            store.Delete(marker.ValueName);
            return new AutoHdrRestoreResult(AutoHdrRestoreOutcome.Restored, decision.Rule, current, null);
        }

        if (decision.Text is { } text)
        {
            store.WriteString(marker.ValueName, text);
            return new AutoHdrRestoreResult(AutoHdrRestoreOutcome.Restored, decision.Rule, current, text);
        }

        return new AutoHdrRestoreResult(AutoHdrRestoreOutcome.LeftAsIs, decision.Rule, current, null);
    }

    /// <summary>Protokollzeile zum Ergebnis; <paramref name="subject"/> etwa „Auto HDR global: DirectXUserGlobalSettings“.</summary>
    public static string Describe(AutoHdrRestoreResult result, string subject, string reason) => result switch
    {
        { Outcome: AutoHdrRestoreOutcome.AlreadyOriginal } =>
            $"{subject} steht schon auf dem Originalzustand „{GpuPreferenceText.Describe(result.Before)}“, nichts zurückzustellen ({reason}).",
        { Outcome: AutoHdrRestoreOutcome.LeftAsIs } =>
            $"{subject} extern geändert (jetzt „{GpuPreferenceText.Describe(result.Before)}“), HDRpilot lässt den Wert stehen ({reason}).",
        { Rule: GpuPreferenceRestoreRule.TokenOnly } =>
            $"{subject}: andere Einträge extern geändert, nur AutoHDREnable zurückgestellt (vorher „{GpuPreferenceText.Describe(result.Before)}“, jetzt {(result.After is null ? "gelöscht" : "„" + result.After + "“")}; {reason}).",
        _ => $"{subject} zurückgestellt auf {(result.After is null ? "„(fehlt)“, gelöscht" : "„" + result.After + "“")} ({reason}).",
    };
}
