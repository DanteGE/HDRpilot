using System.Security.Cryptography;
using System.Text;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.HdrSupport;

public sealed class PcgwAccountStoreTests
{
    private const string Path = @"C:\Daten\HdrPilot\pcgw-account.dat";

    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public void Account_round_trips_and_the_file_never_contains_the_password()
    {
        var store = new PcgwAccountStore(_fs, Path, new XorProtector());

        store.Save(new PcgwAccount("Dante_GE@HdrPilot", "abc123geheim"));

        Assert.True(store.Exists);
        var text = _fs.ReadAllText(Path);
        Assert.DoesNotContain("abc123geheim", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Dante_GE", text, StringComparison.Ordinal);
        var loaded = store.Load(out var unreadable);
        Assert.False(unreadable);
        Assert.Equal("Dante_GE@HdrPilot", loaded!.UserName);
        Assert.Equal("abc123geheim", loaded.Password);

        store.Delete();
        Assert.False(store.Exists);
        Assert.Null(store.Load(out unreadable));
        Assert.False(unreadable);
    }

    [Fact]
    public void Real_dpapi_round_trip_for_the_current_user()
    {
        var protector = DpapiProtector.Instance;
        var plain = Encoding.UTF8.GetBytes("Bot-Passwort äöü");

        var sealedBytes = protector.Protect(plain);

        Assert.NotEqual(plain, sealedBytes);
        Assert.Equal(plain, protector.Unprotect(sealedBytes));
        sealedBytes[^1] ^= 0xFF;
        Assert.Throws<CryptographicException>(() => protector.Unprotect(sealedBytes));
    }

    [Fact]
    public void Store_with_real_dpapi_in_memory()
    {
        var store = new PcgwAccountStore(_fs, Path, DpapiProtector.Instance);
        store.Save(new PcgwAccount("Dante_GE@HdrPilot", "abc123geheim"));

        Assert.Equal("abc123geheim", store.Load(out _)!.Password);
    }

    [Theory]
    [InlineData("kein base64 !")]
    [InlineData("AAAA")]
    public void Unreadable_file_is_reported_not_thrown(string content)
    {
        _fs.AddFile(Path, content);
        var log = new ListLogger<PcgwAccountStoreTests>();
        var store = new PcgwAccountStore(_fs, Path, DpapiProtector.Instance, log);

        Assert.Null(store.Load(out var unreadable));
        Assert.True(unreadable);
        Assert.Single(log.Entries);
    }

    [Theory]
    [InlineData("Dante_GE@HdrPilot", true)]
    [InlineData("Name mit Leerzeichen@Bot", true)]
    [InlineData("Dante_GE", false)]
    [InlineData("@HdrPilot", false)]
    [InlineData("Dante_GE@", false)]
    [InlineData(" Dante_GE@HdrPilot", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void User_names_need_the_bot_suffix(string? name, bool valid)
    {
        Assert.Equal(valid, PcgwAccount.IsValidUserName(name));
    }

    [Fact]
    public void ToString_never_shows_the_password()
    {
        var account = new PcgwAccount("Dante_GE@HdrPilot", "abc123geheim");
        Assert.Equal("Dante_GE@HdrPilot", account.ToString());
        Assert.DoesNotContain("abc123geheim", $"{account}", StringComparison.Ordinal);
    }

    /// <summary>Umkehrbare Attrappe für DPAPI (prüft die Ablage, nicht die Verschlüsselung).</summary>
    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();

        public byte[] Unprotect(byte[] data) => Protect(data);
    }
}
