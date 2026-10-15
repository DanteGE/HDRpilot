using System.Security.Cryptography;
using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.HdrSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Artwork;

/// <summary>
/// Ablage des SteamGridDB-Schlüssels in %LOCALAPPDATA%\HDRpilot\steamgriddb-key.dat (E89, nicht in config.json, roamt nicht):
/// wie das PCGamingWiki-Konto mit DPAPI für den Windows-Benutzer verschlüsselt (eigener Schlüsselanteil
/// <see cref="Entropy"/>), Base64, atomar geschrieben. Der Schlüssel steht nie im Protokoll.
/// </summary>
public sealed class SteamGridDbKeyStore(IFileSystem fileSystem, string path, ISecretProtector protector, ILogger? logger = null)
{
    /// <summary>Eigener Schlüsselanteil für DPAPI (anders als beim PCGamingWiki-Konto).</summary>
    public static ReadOnlySpan<byte> Entropy => "HdrPilot.SteamGridDB.v1"u8;

    /// <summary>Längster angenommener Schlüssel (SteamGridDB vergibt 32 Hex-Zeichen).</summary>
    public const int MaxKeyLength = 256;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>DPAPI mit dem eigenen Schlüsselanteil.</summary>
    public static ISecretProtector CreateProtector() => new DpapiProtector(Entropy.ToArray());

    public bool Exists => fileSystem.FileExists(path);

    /// <summary>True für einen plausiblen Schlüssel (druckbar, ohne Leerraum, nicht zu lang).</summary>
    public static bool IsPlausible(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Trim().Length is >= 8 and <= MaxKeyLength && key.Trim().All(c => c > ' ' && c < 127);

    /// <summary>Gespeicherter Schlüssel; null, wenn keiner gespeichert oder die Datei nicht lesbar ist (dann <paramref name="unreadable"/>).</summary>
    public string? Load(out bool unreadable)
    {
        unreadable = false;
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var plain = protector.Unprotect(Convert.FromBase64String(fileSystem.ReadAllText(path).Trim()));
            try
            {
                var key = Encoding.UTF8.GetString(plain);
                if (IsPlausible(key))
                {
                    return key.Trim();
                }
            }
            finally
            {
                Array.Clear(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning("SteamGridDB: gespeicherter Schlüssel nicht lesbar ({Error}).", ex.GetType().Name);
        }

        unreadable = true;
        return null;
    }

    /// <summary>Speichert den Schlüssel verschlüsselt. Wirft bei unplausiblem Schlüssel ArgumentException, sonst IO- und DPAPI-Ausnahmen.</summary>
    public void Save(string key)
    {
        if (!IsPlausible(key))
        {
            throw new ArgumentException("Kein gültiger API-Schlüssel.", nameof(key));
        }

        var plain = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            fileSystem.WriteAllTextAtomic(path, Convert.ToBase64String(protector.Protect(plain)));
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    public void Delete() => fileSystem.DeleteFile(path);
}
