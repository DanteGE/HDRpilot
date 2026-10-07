using System.Globalization;
using System.Text;
using System.Text.Json;

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
            throw new InvalidDataException("Ungültige Zeichenkodierung (weder UTF-8 noch UTF-16 mit BOM).");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, Options);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line
                ? string.Create(CultureInfo.InvariantCulture, $" (Zeile {line + 1}, Spalte {(ex.BytePositionInLine ?? 0) + 1})")
                : string.Empty;
            throw new InvalidDataException("Kein gültiges JSON" + where + ".", ex);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidDataException("JSON-Wurzel ist kein Objekt.");
        }

        return document;
    }

    /// <summary>Sucht eine Eigenschaft erst exakt, dann ohne Beachtung der Groß-/Kleinschreibung.</summary>
    public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Zeichenkette oder null, wenn die Eigenschaft fehlt oder keine Zeichenkette ist.</summary>
    public static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Wie <see cref="GetString"/>, nimmt aber auch Zahlen an (IDs werden je nach Version als Zahl geschrieben).</summary>
    public static string? GetId(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
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
            .Select(e => e.GetString()!)
            .ToList();
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
