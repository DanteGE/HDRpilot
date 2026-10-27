using System.Globalization;
using System.Text;
using System.Text.Json;
using HdrPilot.Core.Resources;

namespace HdrPilot.Core.Games.Scanners.Common;

/// <summary>
/// Liest JSON-Dateien von Launchern (Epic-Manifeste, GOG-Spielinfos) robust ein: UTF-8 mit oder ohne BOM,
/// UTF-16 nur mit BOM. Ungültige Bytes werden nicht still ersetzt, sondern als Fehler gemeldet.
/// </summary>
internal static class ScannerJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64,
    };

    /// <summary>
    /// Dekodiert und parst die Datei. Die Wurzel muss ein JSON-Objekt sein.
    /// Wirft <see cref="InvalidDataException"/> mit deutscher Meldung bei falscher Kodierung oder ungültigem JSON.
    /// </summary>
    public static JsonDocument ParseObject(byte[] bytes)
    {
        string text;
        try
        {
            text = Decode(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException(ScanStrings.Json_InvalidEncoding);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, Options);
        }
        catch (JsonException ex)
        {
            var message = ex.LineNumber is { } line
                ? ScanText.Format(ScanStrings.Json_InvalidAt, (line + 1).ToString(CultureInfo.InvariantCulture), ((ex.BytePositionInLine ?? 0) + 1).ToString(CultureInfo.InvariantCulture))
                : ScanStrings.Json_Invalid;
            throw new InvalidDataException(message, ex);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidDataException(ScanStrings.Json_RootNotObject);
        }

        return document;
    }

    /// <summary>
    /// Sucht eine Eigenschaft erst exakt, dann ohne Beachtung der Groß-/Kleinschreibung. Namen mit ungültiger
    /// Escape-Folge (etwa ein einzelnes Surrogat <c>\uD800</c>) lassen sich nicht dekodieren; sie können keiner der
    /// gesuchten Namen sein und werden übergangen, statt eine Ausnahme aus dem Scanner zu werfen.
    /// </summary>
    public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        try
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // Ein anderer Eigenschaftsname ist nicht dekodierbar: unten tolerant weitersuchen.
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(TryGetName(property), name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Zeichenkette oder null, wenn die Eigenschaft fehlt oder keine Zeichenkette ist. Wirft
    /// <see cref="InvalidDataException"/>, wenn der Wert eine ungültige Escape-Folge enthält.
    /// </summary>
    public static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? ReadString(value, name) : null;

    /// <summary>Wie <see cref="GetString"/>, nimmt aber auch Zahlen an (IDs werden je nach Version als Zahl geschrieben).</summary>
    public static string? GetId(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => ReadString(value, name),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    /// <summary>Wahrheitswert oder null, wenn die Eigenschaft fehlt oder kein true/false ist.</summary>
    public static bool? GetBool(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>Alle Zeichenketten eines Arrays; fehlt das Array, ist die Liste leer. Andere Elemente werden übergangen.</summary>
    public static IReadOnlyList<string> GetStringArray(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => ReadString(e, name))
            .ToList();
    }

    /// <summary>
    /// Dekodiert einen JSON-String. System.Text.Json prüft Escape-Folgen wie <c>\uD800</c> (einzelnes Surrogat) erst
    /// hier und wirft dann <see cref="InvalidOperationException"/>; das wird zum gemeldeten Formatfehler.
    /// </summary>
    private static string ReadString(JsonElement value, string name)
    {
        try
        {
            return value.GetString() ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            throw new InvalidDataException(ScanText.Format(ScanStrings.Json_LoneSurrogate, name));
        }
    }

    private static string? TryGetName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return StrictUtf8.GetString(span[3..]);
        }

        if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return StrictUtf16Le.GetString(span[2..]);
        }

        if (span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return StrictUtf16Be.GetString(span[2..]);
        }

        return StrictUtf8.GetString(span);
    }
}
