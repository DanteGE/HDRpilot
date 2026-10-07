using System.Text;

namespace HdrPilot.Core.Games.Scanners.Steam;

/// <summary>
/// Dekodiert Textdateien von Steam. Steam schreibt UTF-8 ohne BOM. Eine BOM (UTF-8, UTF-16) wird beachtet.
/// Ist der Inhalt kein gültiges UTF-8 (etwa von Hand in Latin-1 gespeichert), wird er als Latin-1 gelesen,
/// damit Umlaute nicht als Ersatzzeichen enden.
/// </summary>
internal static class SteamTextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(byte[] bytes, out bool usedLatin1Fallback)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        usedLatin1Fallback = false;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return DecodeUtf8(bytes.AsSpan(3), ref usedLatin1Fallback);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        return DecodeUtf8(bytes, ref usedLatin1Fallback);
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> bytes, ref bool usedLatin1Fallback)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            usedLatin1Fallback = true;
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
