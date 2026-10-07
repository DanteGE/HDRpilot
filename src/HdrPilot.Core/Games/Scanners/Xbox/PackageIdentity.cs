using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HdrPilot.Core.Games.Scanners.Xbox;

/// <summary>
/// Berechnet den PackageFamilyName eines MSIX/AppX-Pakets aus Paketname und Publisher, ohne Windows zu fragen:
/// PublisherId = 13 Zeichen Base32 (Alphabet <c>0123456789abcdefghjkmnpqrstvwxyz</c>) über die ersten 8 Byte des
/// SHA-256 des UTF-16LE-kodierten Publisher-Strings (64 Bit plus ein Füllbit). Am Zielrechner gegen
/// <c>Get-AppxPackage</c> geprüft.
/// </summary>
public static class PackageIdentity
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    public static string ComputePublisherId(string publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        var bits = (UInt128)BinaryPrimitives.ReadUInt64BigEndian(hash) << 1;
        var id = new char[13];
        for (var i = 0; i < id.Length; i++)
        {
            id[i] = Alphabet[(int)((bits >> (60 - (5 * i))) & 31)];
        }

        return new string(id);
    }

    public static string FamilyName(string packageName, string publisher) => packageName + "_" + ComputePublisherId(publisher);

    /// <summary>Paketname laut Manifest-Schema: 3 bis 50 Zeichen aus Buchstaben, Ziffern, Punkt und Bindestrich.</summary>
    public static bool IsValidPackageName(string? name) =>
        name is { Length: >= 3 and <= 50 } && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    /// <summary>App-Id laut Manifest-Schema: ASCII-Buchstaben, Ziffern und Punkt, beginnt mit einem Buchstaben, höchstens 64 Zeichen.</summary>
    public static bool IsValidApplicationId(string? id) =>
        id is { Length: >= 1 and <= 64 } && char.IsAsciiLetter(id[0]) && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '.');
}
