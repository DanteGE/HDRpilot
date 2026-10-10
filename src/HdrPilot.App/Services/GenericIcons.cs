using System.Numerics;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HdrPilot.App.Services;

/// <summary>
/// Fingerabdruck eines Symbols in 48 px (E86): <see cref="Hash"/> sind die ersten 16 Hex-Zeichen von SHA-256 über die
/// Pixel als BGRA, voll durchsichtige Pixel auf 0 gesetzt (wie die Messung an der Bibliothek). Für leicht anders
/// gezeichnete Varianten dazu ein Wahrnehmungsabdruck: Helligkeitsverlauf (9×8), Helligkeit über dem Mittel (8×8),
/// Deckkraft-Maske (8×8) und mittlere Farbe der deckenden Pixel.
/// </summary>
public sealed record IconFingerprint(string Hash, ulong Gradient, ulong Brightness, ulong Mask, int Red, int Green, int Blue)
{
    public const int Size = 48;

    /// <summary>Abdruck eines Bildes in 48 × 48 Pixeln; null bei anderer Größe.</summary>
    public static IconFingerprint? Of(BitmapSource image)
    {
        var pixels = NormalizedPixels(image);
        return pixels is null ? null : Of(pixels);
    }

    /// <summary>Abdruck aus 48 × 48 BGRA-Pixeln (durchsichtige Pixel schon auf 0).</summary>
    public static IconFingerprint Of(byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != Size * Size * 4)
        {
            throw new ArgumentException("Erwartet 48 × 48 BGRA-Pixel.", nameof(pixels));
        }

        var hash = Convert.ToHexString(SHA256.HashData(pixels))[..16];
        var luminance = new double[Size * Size];
        var alpha = new double[Size * Size];
        double red = 0, green = 0, blue = 0, coverage = 0;
        for (var i = 0; i < Size * Size; i++)
        {
            var a = pixels[(i * 4) + 3] / 255.0;
            double b = pixels[i * 4], g = pixels[(i * 4) + 1], r = pixels[(i * 4) + 2];

            // Vor grauem Grund (128), damit durchsichtige Bereiche nicht als Schwarz zählen.
            luminance[i] = (0.299 * ((r * a) + (128 * (1 - a)))) + (0.587 * ((g * a) + (128 * (1 - a)))) + (0.114 * ((b * a) + (128 * (1 - a))));
            alpha[i] = pixels[(i * 4) + 3];
            red += Math.Floor(r * a);
            green += Math.Floor(g * a);
            blue += Math.Floor(b * a);
            coverage += pixels[(i * 4) + 3];
        }

        var area = Math.Max(1.0, coverage / 255.0);
        var gradientGrid = Downscale(luminance, 9, 8);
        ulong gradient = 0;
        for (int y = 0, bit = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++, bit++)
            {
                if (gradientGrid[(y * 9) + x] < gradientGrid[(y * 9) + x + 1])
                {
                    gradient |= 1UL << bit;
                }
            }
        }

        var brightnessGrid = Downscale(luminance, 8, 8);
        var mean = brightnessGrid.Average();
        var maskGrid = Downscale(alpha, 8, 8);
        ulong brightness = 0, mask = 0;
        for (var i = 0; i < 64; i++)
        {
            if (brightnessGrid[i] > mean)
            {
                brightness |= 1UL << i;
            }

            if (maskGrid[i] > 127)
            {
                mask |= 1UL << i;
            }
        }

        return new IconFingerprint(hash, gradient, brightness, mask, (int)(red / area), (int)(green / area), (int)(blue / area));
    }

    /// <summary>
    /// Abstand zweier Abdrücke: abweichende Bits der drei Muster plus Farbabstand / 15 (0 = gleich). Gemessen an den 598
    /// verschiedenen EXE-Symbolen der Bibliothek (2026-10-10, neu gelesen): Das ähnlichste echte Spielsymbol hat zu einem
    /// Standardsymbol den Abstand 29 (Necrosmith und Space Marine 2 zu Godot 4), die drei Boltgun-Einträge mit ihrem
    /// gemeinsamen echten Symbol 68.
    /// </summary>
    public int DistanceTo(IconFingerprint other) =>
        BitOperations.PopCount(Gradient ^ other.Gradient) + BitOperations.PopCount(Brightness ^ other.Brightness) + BitOperations.PopCount(Mask ^ other.Mask)
        + ((Math.Abs(Red - other.Red) + Math.Abs(Green - other.Green) + Math.Abs(Blue - other.Blue)) / 15);

    /// <summary>48 × 48 Pixel als BGRA, voll durchsichtige Pixel auf 0; null bei anderer Größe.</summary>
    public static byte[]? NormalizedPixels(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.PixelWidth != Size || image.PixelHeight != Size)
        {
            return null;
        }

        var converted = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[Size * Size * 4];
        converted.CopyPixels(pixels, Size * 4, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0)
            {
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
            }
        }

        return pixels;
    }

    private static double[] Downscale(double[] source, int width, int height)
    {
        var result = new double[width * height];
        for (var oy = 0; oy < height; oy++)
        {
            for (var ox = 0; ox < width; ox++)
            {
                int x0 = ox * Size / width, x1 = Math.Max(x0 + 1, (ox + 1) * Size / width);
                int y0 = oy * Size / height, y1 = Math.Max(y0 + 1, (oy + 1) * Size / height);
                double sum = 0;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        sum += source[(y * Size) + x];
                        count++;
                    }
                }

                result[(oy * width) + ox] = sum / count;
            }
        }

        return result;
    }
}

/// <summary>
/// Erkennt Standardsymbole, die nichts über das Spiel sagen (EasyAntiCheat-Starter, Unreal-„U“, Epic/EOS-Starter, Godot,
/// Unity, Unreal 3, Windows-Programmsymbol; E86): gleicher Fingerabdruck oder Wahrnehmungsabstand höchstens
/// <see cref="MaxDistance"/> zu einem Eintrag der festen Liste. Wie oft ein Symbol vorkommt, zählt bewusst nicht: Die
/// Regel „mindestens drei Spiele teilen es“ hätte die drei Boltgun-Einträge mit ihrem echten Symbol erwischt.
/// </summary>
public sealed class GenericIconDetector(IEnumerable<IconFingerprint> references)
{
    /// <summary>
    /// Bis zu diesem Abstand gilt ein Symbol als Variante eines Standardsymbols (etwa leicht anders gezeichnet). Gemessen:
    /// echte Spielsymbole ab 29, Godot 3 und seine Variante aus GodotWorkshopUtility.exe 1.
    /// </summary>
    public const int MaxDistance = 8;

    private readonly IReadOnlyList<IconFingerprint> _references = references.ToList();
    private readonly HashSet<string> _hashes = new(references.Select(r => r.Hash), StringComparer.OrdinalIgnoreCase);

    /// <summary>Erkennung mit der festen Liste aus <see cref="KnownGenericIcons"/>.</summary>
    public static GenericIconDetector Default { get; } = new(KnownGenericIcons.All.Select(k => k.Fingerprint));

    public bool IsGeneric(BitmapSource image) => IconFingerprint.Of(image) is { } fingerprint && IsGeneric(fingerprint);

    public bool IsGeneric(IconFingerprint fingerprint) =>
        _hashes.Contains(fingerprint.Hash) || _references.Any(r => r.DistanceTo(fingerprint) <= MaxDistance);
}

/// <summary>
/// Feste Liste der Standardsymbole (E86), nur Zahlen, keine Bilder. Die ersten acht stammen aus der Messung an der
/// Bibliothek des Nutzers (647 Spiele, 2026-10-10, von Auge bestätigt, docs/recherche/symbole); neu aus den Spieldateien
/// gelesen ergaben alle acht denselben Hash. Dazu das Unreal-3-Symbol (UDK.exe, gleich in Homesick und beiden Viscera
/// Cleanup Detail) und das Windows-Programmsymbol (imageres.dll -15, gleich shell32.dll -3).
/// </summary>
public static class KnownGenericIcons
{
    public static IReadOnlyList<(string Label, IconFingerprint Fingerprint)> All { get; } =
    [
        ("EasyAntiCheat-Starter", new("BFF8A9D4CC3574F3", 0xE4CEC68D8D83C6E4, 0xCF9F9F1B01060CCF, 0x007E7EFFFFFF7E00, 39, 105, 146)),
        ("Unreal Engine (schwarzes U)", new("21704A055F2F2AF1", 0x33CD96969696E533, 0xE781BD1C1CA581E7, 0x3C7EFFFFFFFF7E3C, 97, 97, 97)),
        ("Unreal Engine (umrandetes U)", new("D970DE3C3B3B97F1", 0xB24D9694959665B2, 0xE789BD3D3EA5C3E7, 0x187E7EFFFF7E7E18, 125, 125, 125)),
        ("Epic Games / EOS-Starter", new("CAFA9E41B8D0CFCE", 0xE8DC96B21696B6C0, 0xE7813C007E3E7E81, 0x187E7E7E7E7E7E7E, 58, 58, 58)),
        ("Godot 3 (GodotWorkshopUtility)", new("94DA36B9F4063577", 0xF1B6694A32C0E868, 0x003C7E5A66000000, 0x007E7E7E7E7E3C00, 92, 150, 193)),
        ("Godot 3", new("D30DAE223B9DC3FB", 0xF1B6694A32E0E868, 0x003C7E5A66000000, 0x007E7E7E7E7E3C00, 92, 150, 193)),
        ("Godot 4", new("F9F095E9485D704F", 0xB20F332B737317A8, 0x003C7E7E7E7E3C00, 0xFFFFFFFFFFFFFFFF, 78, 118, 148)),
        ("Unity", new("89AEAB8B670448CD", 0x70CC8EAAB696CE50, 0xC3093C0C3C3CFFFF, 0x187E7E7E7E7E7E18, 122, 122, 122)),
        ("Unreal Engine 3 (UDK)", new("2322153F433979FB", 0xF0CCCA9A9696CEE0, 0xE78181BDBFBF8181, 0x007E7E7E7E7E7E7E, 77, 75, 74)),
        ("Windows-Programmsymbol", new("8E2BA1E166E6995D", 0x2461616261A24124, 0x00FFC3C7E7E7FE00, 0x00FFFFFFFFFFFF00, 168, 196, 215)),
    ];
}
