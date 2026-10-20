using System.Text;
using HdrPilot.App.Setup;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>Setup-Dateiformat (E83): [EXE][Zip][Abschluss]. Nur Temp-Dateien.</summary>
public sealed class SetupBundleTests : IDisposable
{
    private readonly SetupHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Valid_setup_file_is_recognised_verified_and_yields_exe_and_payload()
    {
        var bundle = _h.Bundle("1.1.0");

        Assert.True(bundle.VerifyExe());
        Assert.Equal(SetupHarness.ExeBytes("1.1.0").Length, bundle.ExeLength);
        Assert.Equal(["README.md", "LICENSE", "THIRD-PARTY-NOTICES.txt", "cli/hdrpilot.exe"], bundle.PayloadEntries());

        using var exe = new MemoryStream();
        bundle.CopyExeTo(exe);
        Assert.Equal(SetupHarness.ExeBytes("1.1.0"), exe.ToArray());

        using var cli = new MemoryStream();
        Assert.True(bundle.CopyPayloadEntryTo("cli/hdrpilot.exe", cli));
        Assert.Equal("CLI 1.1.0", Encoding.UTF8.GetString(cli.ToArray()));

        using var license = new MemoryStream();
        Assert.True(bundle.CopyPayloadEntryTo("LICENSE", license));
        Assert.Equal("LICENSE 1.1.0", Encoding.UTF8.GetString(license.ToArray()));
    }

    [Fact]
    public void Allowed_entries_are_exactly_readme_license_notices_and_cli()
    {
        // E92: der Lizenztext liegt ab 1.5.0 bei; alles andere bleibt ausgeschlossen (E83).
        Assert.Equal(["README.md", "LICENSE", "THIRD-PARTY-NOTICES.txt", "cli/hdrpilot.exe"], SetupBundle.PayloadEntryNames);
        Assert.Equal(["HDRpilot.exe", "README.md", "LICENSE", "THIRD-PARTY-NOTICES.txt", @"cli\hdrpilot.exe"], SetupLocations.InstalledFiles);
    }

    [Fact]
    public void Setup_file_from_before_1_5_without_license_stays_valid()
    {
        var bundle = _h.Bundle("1.4.0", withLicense: false);

        Assert.True(bundle.VerifyExe());
        Assert.Equal(["README.md", "THIRD-PARTY-NOTICES.txt", "cli/hdrpilot.exe"], bundle.PayloadEntries());
        Assert.False(bundle.CopyPayloadEntryTo("LICENSE", Stream.Null));
    }

    [Fact]
    public void Plain_exe_foreign_tiny_and_missing_files_are_not_setup_files()
    {
        var plain = Path.Combine(_h.Root, "plain.exe");
        File.WriteAllBytes(plain, SetupHarness.ExeBytes("1.1.0"));
        var tiny = Path.Combine(_h.Root, "tiny.bin");
        File.WriteAllBytes(tiny, [1, 2, 3]);
        var foreign = Path.Combine(_h.Root, "foreign.bin");
        File.WriteAllBytes(foreign, Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray());

        Assert.Null(SetupBundle.TryOpen(plain));
        Assert.Null(SetupBundle.TryOpen(tiny));
        Assert.Null(SetupBundle.TryOpen(foreign));
        Assert.Null(SetupBundle.TryOpen(Path.Combine(_h.Root, "fehlt.exe")));
        Assert.Null(SetupBundle.TryOpen(null));
        Assert.Null(SetupBundle.TryOpen("  "));
    }

    [Fact]
    public void Truncated_setup_files_are_rejected()
    {
        var bytes = File.ReadAllBytes(_h.Bundle("1.1.0").FilePath);

        // Ende abgeschnitten (Abschluss fehlt) und Mitte fehlt (Abschluss da, Längen passen nicht mehr).
        var tail = Path.Combine(_h.Root, "tail.exe");
        File.WriteAllBytes(tail, bytes[..^10]);
        var middle = Path.Combine(_h.Root, "middle.exe");
        File.WriteAllBytes(middle, [.. bytes[..100], .. bytes[200..]]);

        Assert.Null(SetupBundle.TryOpen(tail));
        Assert.Null(SetupBundle.TryOpen(middle));
    }

    [Fact]
    public void Wrong_magic_or_impossible_lengths_are_rejected()
    {
        var exe = SetupHarness.ExeBytes("1.1.0");
        var magic = Path.Combine(_h.Root, "magic.exe");
        RawSetupFile.Write(magic, exe, new Dictionary<string, byte[]> { ["README.md"] = [1] }, magic: "HDRPILOT-SETUP02");
        Assert.Null(SetupBundle.TryOpen(magic));

        var good = Path.Combine(_h.Root, "good.exe");
        RawSetupFile.Write(good, exe, new Dictionary<string, byte[]> { ["README.md"] = [1] });
        Assert.NotNull(SetupBundle.TryOpen(good));

        var bytes = File.ReadAllBytes(good);
        var zeroExe = (byte[])bytes.Clone();
        BitConverter.GetBytes(0L).CopyTo(zeroExe, bytes.Length - 64 + 16);
        var negativePayload = (byte[])bytes.Clone();
        BitConverter.GetBytes(-1L).CopyTo(negativePayload, bytes.Length - 64 + 24);
        File.WriteAllBytes(magic, zeroExe);
        Assert.Null(SetupBundle.TryOpen(magic));
        File.WriteAllBytes(magic, negativePayload);
        Assert.Null(SetupBundle.TryOpen(magic));
    }

    [Fact]
    public void Changed_exe_bytes_keep_the_footer_readable_but_fail_the_checksum()
    {
        var path = _h.Bundle("1.1.0").FilePath;
        var bytes = File.ReadAllBytes(path);
        bytes[10] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var damaged = SetupBundle.TryOpen(path);

        Assert.NotNull(damaged);
        Assert.False(damaged.VerifyExe());
    }

    [Fact]
    public void Only_allowed_payload_entries_are_listed_and_extracted()
    {
        var path = Path.Combine(_h.Root, "evil.exe");
        RawSetupFile.Write(path, SetupHarness.ExeBytes("1.1.0"), new Dictionary<string, byte[]>
        {
            ["../evil.txt"] = Encoding.UTF8.GetBytes("böse"),
            ["x.dll"] = [0x4D, 0x5A],
            ["README.md"] = Encoding.UTF8.GetBytes("Lies mich"),
            ["cli/../../HDRpilot.exe"] = [0x4D, 0x5A],
        });
        var bundle = SetupBundle.TryOpen(path)!;

        Assert.True(bundle.VerifyExe());
        Assert.Equal(["README.md"], bundle.PayloadEntries());
        using var sink = new MemoryStream();
        Assert.False(bundle.CopyPayloadEntryTo("../evil.txt", sink));
        Assert.False(bundle.CopyPayloadEntryTo("x.dll", sink));
        Assert.False(bundle.CopyPayloadEntryTo("cli/hdrpilot.exe", sink)); // erlaubt, aber nicht im Paket
        Assert.Equal(0, sink.Length);
        Assert.True(bundle.CopyPayloadEntryTo("README.md", sink));
        Assert.Equal("Lies mich", Encoding.UTF8.GetString(sink.ToArray()));
    }

    [Fact]
    public void Setup_file_without_payload_has_no_entries()
    {
        var path = Path.Combine(_h.Root, "nopayload.exe");
        RawSetupFile.Write(path, SetupHarness.ExeBytes("1.1.0"), entries: null);
        var bundle = SetupBundle.TryOpen(path)!;

        Assert.Equal(0, bundle.PayloadLength);
        Assert.Empty(bundle.PayloadEntries());
        Assert.False(bundle.CopyPayloadEntryTo("README.md", Stream.Null));
        Assert.True(bundle.VerifyExe());
    }

    [Fact]
    public void Writing_refuses_entries_outside_the_allowed_list()
    {
        var exe = Path.Combine(_h.Root, "app.exe");
        File.WriteAllBytes(exe, SetupHarness.ExeBytes("1.1.0"));

        Assert.Throws<ArgumentException>(() => SetupBundle.Write(exe, new Dictionary<string, string> { ["x.dll"] = exe }, Path.Combine(_h.Root, "out.exe")));
    }
}
