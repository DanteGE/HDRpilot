using System.Diagnostics.CodeAnalysis;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Ein sauberer Wert im Schlüssel UserGpuPreferences (etwa <c>DirectXUserGlobalSettings</c>), zerlegt in seine
/// <c>Schlüssel=Wert</c>-Paare (E88). Unveränderlich; <see cref="ToString"/> setzt ihn byte-gleich wieder zusammen
/// (Reihenfolge, Schreibweise und Abschluss bleiben).
/// </summary>
public sealed class GpuPreferenceTokens
{
    private readonly KeyValuePair<string, string>[] _pairs;

    private GpuPreferenceTokens(KeyValuePair<string, string>[] pairs, bool trailingSeparator)
    {
        _pairs = pairs;
        TrailingSeparator = trailingSeparator;
    }

    /// <summary>Kein Paar, mit „;“ am Ende: Ausgangslage für einen fehlenden Wert (wird zu <c>AutoHDREnable=1;</c>).</summary>
    public static GpuPreferenceTokens Empty { get; } = new([], trailingSeparator: true);

    public IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;

    /// <summary>Der Wert endet mit „;“ (bei Windows üblich, aber nicht immer).</summary>
    public bool TrailingSeparator { get; }

    public bool IsEmpty => _pairs.Length == 0;

    /// <summary>Wert zum Schlüssel (ohne Groß-/Kleinschreibung) oder null.</summary>
    public string? Get(string key)
    {
        foreach (var pair in _pairs)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>Setzt einen Schlüssel: ein vorhandener behält Platz und Schreibweise, ein neuer kommt ans Ende.</summary>
    public GpuPreferenceTokens With(string key, string value)
    {
        var pairs = _pairs.ToList();
        var index = pairs.FindIndex(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            pairs[index] = new KeyValuePair<string, string>(pairs[index].Key, value);
        }
        else
        {
            pairs.Add(new KeyValuePair<string, string>(key, value));
        }

        return new GpuPreferenceTokens([.. pairs], TrailingSeparator);
    }

    /// <summary>Entfernt einen Schlüssel (ohne Groß-/Kleinschreibung); alle anderen bleiben, wie sie sind.</summary>
    public GpuPreferenceTokens Without(string key) =>
        new([.. _pairs.Where(p => !string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))], TrailingSeparator);

    public override string ToString() =>
        string.Join(';', _pairs.Select(p => p.Key + "=" + p.Value)) + (TrailingSeparator && _pairs.Length > 0 ? ";" : string.Empty);

    /// <summary>Nur für <see cref="GpuPreferenceText.TryParse(string, out GpuPreferenceTokens?)"/>.</summary>
    internal static GpuPreferenceTokens Create(KeyValuePair<string, string>[] pairs, bool trailingSeparator) => new(pairs, trailingSeparator);
}

/// <summary>Was das Setzen des Tokens mit einem vorhandenen Wert macht.</summary>
public enum GpuPreferenceMergeKind
{
    /// <summary>Kein sauberer REG_SZ: nie angefasst.</summary>
    Unsupported,

    /// <summary>Das Token steht schon so da: nichts zu schreiben, nichts zu merken.</summary>
    Unchanged,

    /// <summary><see cref="GpuPreferenceMerge.Written"/> schreiben.</summary>
    Write,
}

/// <param name="Written">Neuer Text (nur bei <see cref="GpuPreferenceMergeKind.Write"/>).</param>
/// <param name="OriginalToken">Bisheriger Wert von <c>AutoHDREnable</c>, null ohne das Token.</param>
public sealed record GpuPreferenceMerge(GpuPreferenceMergeKind Kind, string? Written = null, string? OriginalToken = null);

/// <summary>Welche Regel beim Zurückstellen griff (E88).</summary>
public enum GpuPreferenceRestoreRule
{
    /// <summary>R1: Der Wert steht noch genau so da, wie HDRpilot ihn schrieb: Original zurück (fehlte er, gelöscht).</summary>
    Exact,

    /// <summary>
    /// R2: Andere Einträge wurden extern geändert (etwa <c>SwapEffectUpgradeEnable</c> in den Windows-Einstellungen), das
    /// eigene Token steht aber noch: nur das Token zurück, alles andere bleibt, wie es jetzt ist.
    /// </summary>
    TokenOnly,

    /// <summary>R3: Extern geändert (Token anders oder weg, Wert gelöscht oder nicht mehr sauber): bleibt stehen.</summary>
    External,
}

/// <summary>Entscheidung beim Zurückstellen: löschen, <see cref="Text"/> schreiben oder (R3) stehen lassen.</summary>
public sealed record GpuPreferenceRestore(GpuPreferenceRestoreRule Rule, bool Delete, string? Text)
{
    public bool LeaveAsIs => !Delete && Text is null;
}

/// <summary>
/// Format der Werte in UserGpuPreferences (E88, undokumentiert): REG_SZ aus <c>Schlüssel=Wert;</c>-Paaren, etwa
/// <c>AutoHDREnable=0;SwapEffectUpgradeEnable=1;</c> im globalen Wert. HDRpilot setzt darin nur das Token
/// <c>AutoHDREnable</c> und fasst nur saubere Werte an: REG_SZ, nicht leer, ohne Steuer- und Leerzeichen (auch kein
/// eingebettetes NUL), jedes Stück genau ein „=“ mit Schlüssel aus Buchstaben, Ziffern und „_“ und nicht leerem Wert, kein
/// Schlüssel doppelt (ohne Groß-/Kleinschreibung), mindestens ein Paar. So fallen etwa „0“, „;;“ und DWORD-Werte heraus.
/// </summary>
public static class GpuPreferenceText
{
    /// <summary>Das einzige Token, das HDRpilot setzt.</summary>
    public const string AutoHdrKey = "AutoHDREnable";

    /// <summary>
    /// „An“ im globalen Wert <see cref="AutoHdrRegistryLocation.GlobalValueName"/>: derselbe Schalter wie „Auto HDR“ in den
    /// Windows-Einstellungen (Livetest G9.6).
    /// </summary>
    public const string GlobalOn = "1";

    /// <summary>„Aus“ im globalen Wert (etwa über den Tray-Eintrag „Auto HDR“).</summary>
    public const string GlobalOff = "0";

    /// <summary>Zerlegt einen Wert; false, wenn er fehlt oder nicht sauber ist (dann nie anfassen).</summary>
    public static bool TryParse(GpuPreferenceValue value, [NotNullWhen(true)] out GpuPreferenceTokens? tokens)
    {
        tokens = null;
        return value.Kind == GpuPreferenceValueKind.String && value.Text is not null && TryParse(value.Text, out tokens);
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out GpuPreferenceTokens? tokens)
    {
        tokens = null;
        if (text.Length == 0 || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            return false;
        }

        var trailing = text.EndsWith(';');
        var body = trailing ? text[..^1] : text;
        if (body.Length == 0)
        {
            return false;
        }

        var pieces = body.Split(';');
        var pairs = new KeyValuePair<string, string>[pieces.Length];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];
            var eq = piece.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || eq == piece.Length - 1 || piece.IndexOf('=', eq + 1) >= 0)
            {
                return false;
            }

            var key = piece[..eq];
            if (!key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') || !seen.Add(key))
            {
                return false;
            }

            pairs[i] = new KeyValuePair<string, string>(key, piece[(eq + 1)..]);
        }

        tokens = GpuPreferenceTokens.Create(pairs, trailing);
        return true;
    }

    /// <summary>Sauber im Sinne von <see cref="TryParse(GpuPreferenceValue, out GpuPreferenceTokens?)"/>.</summary>
    public static bool IsClean(GpuPreferenceValue value) => TryParse(value, out _);

    /// <summary>
    /// Setzt <see cref="AutoHdrKey"/> auf <paramref name="value"/>, vorhanden an seinem Platz (Schreibweise bleibt), sonst
    /// am Ende; alle anderen Paare und der Abschluss bleiben. Ein fehlender Wert wird zu <c>AutoHDREnable=&lt;value&gt;;</c>.
    /// </summary>
    public static GpuPreferenceMerge SetToken(GpuPreferenceValue current, string value)
    {
        GpuPreferenceTokens? tokens;
        if (current.IsMissing)
        {
            tokens = GpuPreferenceTokens.Empty;
        }
        else if (!TryParse(current, out tokens))
        {
            return new GpuPreferenceMerge(GpuPreferenceMergeKind.Unsupported);
        }

        var originalToken = tokens.Get(AutoHdrKey);
        var merged = tokens.With(AutoHdrKey, value).ToString();
        return !current.IsMissing && string.Equals(merged, current.Text, StringComparison.Ordinal)
            ? new GpuPreferenceMerge(GpuPreferenceMergeKind.Unchanged, null, originalToken)
            : new GpuPreferenceMerge(GpuPreferenceMergeKind.Write, merged, originalToken);
    }

    /// <summary>
    /// Zurückstellen eines Werts, den HDRpilot geschrieben hat (<paramref name="written"/>, Token <paramref name="onValue"/>),
    /// nach R1/R2/R3 (siehe <see cref="GpuPreferenceRestoreRule"/>). <paramref name="original"/> ist fehlend oder ein
    /// sauberer REG_SZ; <paramref name="originalToken"/> der damalige Wert des Tokens (null: fehlte).
    /// </summary>
    public static GpuPreferenceRestore DecideRestore(
        GpuPreferenceValue current, GpuPreferenceValue original, string? originalToken, string written, string onValue)
    {
        if (TryParse(current, out var tokens))
        {
            if (string.Equals(current.Text, written, StringComparison.Ordinal))
            {
                return BackToOriginal(GpuPreferenceRestoreRule.Exact, original);
            }

            if (string.Equals(tokens.Get(AutoHdrKey), onValue, StringComparison.Ordinal))
            {
                var rest = originalToken is null ? tokens.Without(AutoHdrKey) : tokens.With(AutoHdrKey, originalToken);
                return rest.IsEmpty
                    ? BackToOriginal(GpuPreferenceRestoreRule.TokenOnly, original)
                    : new GpuPreferenceRestore(GpuPreferenceRestoreRule.TokenOnly, false, rest.ToString());
            }
        }

        return new GpuPreferenceRestore(GpuPreferenceRestoreRule.External, false, null);
    }

    /// <summary>Gleich im Sinne des Zurückstellens: beide fehlen oder beide sind REG_SZ mit gleichem Text.</summary>
    public static bool SameValue(GpuPreferenceValue a, GpuPreferenceValue b) =>
        a.Kind == b.Kind && (a.IsMissing || (a.Kind == GpuPreferenceValueKind.String && string.Equals(a.Text, b.Text, StringComparison.Ordinal)));

    /// <summary>Lesbare Beschreibung für das Protokoll, etwa „AutoHDREnable=0;“, „(fehlt)“ oder „REG_DWORD 1“.</summary>
    public static string Describe(GpuPreferenceValue value) => value.Kind switch
    {
        GpuPreferenceValueKind.Missing => "(fehlt)",
        GpuPreferenceValueKind.String => (value.Text ?? string.Empty).Replace("\0", "\\0", StringComparison.Ordinal),
        GpuPreferenceValueKind.ExpandString => "REG_EXPAND_SZ " + value.Text,
        GpuPreferenceValueKind.MultiString => "REG_MULTI_SZ",
        GpuPreferenceValueKind.DWord => "REG_DWORD " + value.Text,
        GpuPreferenceValueKind.QWord => "REG_QWORD " + value.Text,
        GpuPreferenceValueKind.Binary => "REG_BINARY",
        _ => "unbekannte Art",
    };

    private static GpuPreferenceRestore BackToOriginal(GpuPreferenceRestoreRule rule, GpuPreferenceValue original) =>
        original.IsMissing
            ? new GpuPreferenceRestore(rule, true, null)
            : new GpuPreferenceRestore(rule, false, original.Text ?? string.Empty);
}
