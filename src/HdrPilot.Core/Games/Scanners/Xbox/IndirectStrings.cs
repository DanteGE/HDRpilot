using System.Runtime.InteropServices;

namespace HdrPilot.Core.Games.Scanners.Xbox;

/// <summary>
/// Löst indirekte Zeichenketten wie <c>@{C:\XboxGames\Spiel\Content\resources.pri?ms-resource://Paket/Resources/AppDisplayName}</c> auf.
/// Eigene Schnittstelle, damit Scanner-Tests ohne Windows-Ressourcensystem auskommen.
/// </summary>
public interface IIndirectStringResolver
{
    /// <summary>Aufgelöster Text oder null, wenn die Quelle fehlt oder den Schlüssel nicht kennt.</summary>
    string? Resolve(string indirectString);
}

/// <summary>Auflösung über <c>SHLoadIndirectString</c> (shlwapi.dll). Liest nur die angegebene resources.pri.</summary>
public sealed unsafe partial class ShellIndirectStringResolver : IIndirectStringResolver
{
    private const int BufferLength = 1024;

    public static ShellIndirectStringResolver Instance { get; } = new();

    public string? Resolve(string indirectString)
    {
        if (string.IsNullOrWhiteSpace(indirectString))
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[BufferLength];
            buffer[0] = '\0';
            var hr = SHLoadIndirectString(indirectString, buffer, BufferLength, 0);
            if (hr != 0)
            {
                return null;
            }

            buffer[BufferLength - 1] = '\0';
            var text = new string(buffer).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHLoadIndirectString(string pszSource, char* pszOutBuf, uint cchOutBuf, nint ppvReserved);
}

/// <summary>Hilfen für <c>ms-resource:</c>-Verweise in Paketmanifesten.</summary>
public static class MsResource
{
    public const string Prefix = "ms-resource:";

    public static bool IsReference(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Vollständige Ressourcen-URI: <c>ms-resource:AppDisplayName</c> wird zu
    /// <c>ms-resource://&lt;Paket&gt;/Resources/AppDisplayName</c>, <c>ms-resource:Datei/Schlüssel</c> zu
    /// <c>ms-resource://&lt;Paket&gt;/Datei/Schlüssel</c>, <c>ms-resource:///Pfad</c> zu <c>ms-resource://&lt;Paket&gt;/Pfad</c>;
    /// eine URI mit Paketangabe bleibt unverändert.
    /// </summary>
    public static string ToUri(string value, string packageName)
    {
        var rest = value[Prefix.Length..].Trim();
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            var afterSlashes = rest[2..];
            return afterSlashes.StartsWith('/')
                ? "ms-resource://" + packageName + afterSlashes
                : "ms-resource://" + afterSlashes;
        }

        if (rest.StartsWith('/'))
        {
            return "ms-resource://" + packageName + rest;
        }

        return rest.Contains('/', StringComparison.Ordinal)
            ? "ms-resource://" + packageName + "/" + rest
            : "ms-resource://" + packageName + "/Resources/" + rest;
    }

    /// <summary>Eingabe für <see cref="IIndirectStringResolver"/>: <c>@{&lt;resources.pri&gt;?&lt;URI&gt;}</c>.</summary>
    public static string ToIndirectString(string priFilePath, string value, string packageName) =>
        "@{" + priFilePath + "?" + ToUri(value, packageName) + "}";
}
