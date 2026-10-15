using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HdrPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.HdrSupport;

/// <summary>PCGamingWiki-Konto für den Abgleich: Benutzername im Format „Name@BotName“ und Bot-Passwort.</summary>
public sealed class PcgwAccount(string userName, string password)
{
    public string UserName { get; } = userName;

    public string Password { get; } = password;

    /// <summary>Bot-Passwörter melden sich als „Benutzer@BotName“ an.</summary>
    public static bool IsValidUserName(string? userName)
    {
        var at = userName?.IndexOf('@', StringComparison.Ordinal) ?? -1;
        return at > 0 && at < userName!.Length - 1 && userName.Trim() == userName;
    }

    /// <summary>Nie das Passwort ausgeben (Protokolle, Fehlermeldungen).</summary>
    public override string ToString() => UserName;
}

/// <summary>Verschlüsselt Daten für den angemeldeten Windows-Benutzer (in Tests eine Attrappe).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);

    /// <summary>Wirft <see cref="CryptographicException"/>, wenn die Daten nicht entschlüsselt werden können.</summary>
    byte[] Unprotect(byte[] data);
}

/// <summary>
/// Windows-Datenschutz (DPAPI, CryptProtectData) für den aktuellen Benutzer, direkt über crypt32.dll statt über ein
/// zusätzliches Paket. Nur derselbe Windows-Benutzer auf diesem Rechner kann die Daten wieder lesen.
/// </summary>
public sealed unsafe partial class DpapiProtector : ISecretProtector
{
    private const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    /// <summary>Zusätzlicher Schlüsselanteil: andere Programme desselben Benutzers entschlüsseln nicht ohne Weiteres.</summary>
    private static readonly byte[] PcgwEntropy = "HdrPilot.PCGamingWiki.v1"u8.ToArray();

    private readonly byte[] _entropy;

    /// <param name="entropy">Eigener Schlüsselanteil (etwa für den SteamGridDB-Schlüssel, E89); null: der von PCGamingWiki.</param>
    public DpapiProtector(byte[]? entropy = null)
    {
        _entropy = entropy is { Length: > 0 } ? entropy.ToArray() : PcgwEntropy;
    }

    public static DpapiProtector Instance { get; } = new();

    public byte[] Protect(byte[] data) => Transform(data, protect: true);

    public byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private byte[] Transform(byte[] data, bool protect)
    {
        fixed (byte* input = data)
        fixed (byte* entropy = _entropy)
        {
            var inBlob = new DATA_BLOB { cbData = (uint)data.Length, pbData = input };
            var entropyBlob = new DATA_BLOB { cbData = (uint)_entropy.Length, pbData = entropy };
            var outBlob = default(DATA_BLOB);
            var ok = protect
                ? CryptProtectData(&inBlob, null, &entropyBlob, null, null, CRYPTPROTECT_UI_FORBIDDEN, &outBlob)
                : CryptUnprotectData(&inBlob, null, &entropyBlob, null, null, CRYPTPROTECT_UI_FORBIDDEN, &outBlob);
            if (!ok)
            {
                throw new CryptographicException("DPAPI: " + new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            }

            try
            {
                return new ReadOnlySpan<byte>(outBlob.pbData, (int)outBlob.cbData).ToArray();
            }
            finally
            {
                if (!protect)
                {
                    new Span<byte>(outBlob.pbData, (int)outBlob.cbData).Clear();
                }

                LocalFree((IntPtr)outBlob.pbData);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public uint cbData;
        public byte* pbData;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(DATA_BLOB* pDataIn, char* szDataDescr, DATA_BLOB* pOptionalEntropy, void* pvReserved, void* pPromptStruct, uint dwFlags, DATA_BLOB* pDataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(DATA_BLOB* pDataIn, char** ppszDataDescr, DATA_BLOB* pOptionalEntropy, void* pvReserved, void* pPromptStruct, uint dwFlags, DATA_BLOB* pDataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr hMem);
}

/// <summary>
/// Ablage des PCGamingWiki-Kontos in %LOCALAPPDATA%\HdrPilot\pcgw-account.dat (nicht in config.json, roamt nicht):
/// Benutzername und Bot-Passwort als JSON, mit DPAPI verschlüsselt, Base64, atomar geschrieben. Das Passwort steht
/// nie im Protokoll.
/// </summary>
public sealed class PcgwAccountStore(IFileSystem fileSystem, string path, ISecretProtector protector, ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public bool Exists => fileSystem.FileExists(path);

    /// <summary>Gespeichertes Konto; null, wenn keines gespeichert oder die Datei nicht lesbar ist (dann <paramref name="unreadable"/>).</summary>
    public PcgwAccount? Load(out bool unreadable)
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
                using var doc = JsonDocument.Parse(plain);
                var root = doc.RootElement;
                var user = root.TryGetProperty("user", out var u) ? u.GetString() : null;
                var password = root.TryGetProperty("password", out var p) ? p.GetString() : null;
                if (PcgwAccount.IsValidUserName(user) && !string.IsNullOrEmpty(password))
                {
                    return new PcgwAccount(user!, password);
                }
            }
            finally
            {
                Array.Clear(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("PCGamingWiki-Konto: gespeicherte Anmeldedaten nicht lesbar ({Error}).", ex.GetType().Name);
        }

        unreadable = true;
        return null;
    }

    public void Save(PcgwAccount account)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["user"] = account.UserName, ["password"] = account.Password });
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
