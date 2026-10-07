using HdrPilot.Core.Games.Scanners.Steam;

namespace HdrPilot.Tests.Scanners;

/// <summary>Binärformat von appcache\appinfo.vdf (v27, v28, v29).</summary>
public sealed class SteamAppInfoReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SteamAppInfoBuilder.V29)]
    [InlineData(SteamAppInfoBuilder.V28)]
    [InlineData(SteamAppInfoBuilder.V27)]
    public void Reads_common_type_and_name_of_wanted_apps(uint magic)
    {
        var data = new SteamAppInfoBuilder(magic)
            .Add(228980, "Tool", "Steamworks Common Redistributables")
            .Add(553850, "Game", "HELLDIVERS™ 2")
            .Add(431960, "Application", "Wallpaper Engine")
            .Add(2887520, null, null)
            .Build();

        var info = SteamAppInfoReader.Read(data, new HashSet<uint> { 228980, 553850, 2887520, 999 }, Ct)!;

        Assert.Equal(3, info.Count);
        Assert.Equal(new SteamAppInfo("Tool", "Steamworks Common Redistributables"), info[228980]);
        Assert.Equal(new SteamAppInfo("Game", "HELLDIVERS™ 2"), info[553850]);
        Assert.Equal(new SteamAppInfo(null, null), info[2887520]); // Playtest ohne öffentliche Daten
        Assert.False(info.ContainsKey(431960)); // nicht angefragt
    }

    [Fact]
    public void Type_outside_common_is_ignored()
    {
        // Der Builder schreibt zusätzlich config/launch/0/type = "default"; das darf den App-Typ nicht überschreiben.
        var data = new SteamAppInfoBuilder().Add(1, "Game", "Spiel").Build();

        Assert.Equal("Game", SteamAppInfoReader.Read(data, new HashSet<uint> { 1 }, Ct)![1].Type);
    }

    [Fact]
    public void Unknown_version_returns_null()
    {
        var data = new SteamAppInfoBuilder().Add(1, "Game", "Spiel").Build();
        data[0] = 0x26;

        Assert.Null(SteamAppInfoReader.Read(data, new HashSet<uint> { 1 }, Ct));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(40)]
    [InlineData(100)]
    public void Broken_truncated_file_throws_invalid_data(int length)
    {
        var data = new SteamAppInfoBuilder(SteamAppInfoBuilder.V28).Add(1, "Game", "Spiel").Add(2, "Tool", "Werkzeug").Build();

        Assert.Throws<InvalidDataException>(() => SteamAppInfoReader.Read(data[..length], new HashSet<uint> { 2 }, Ct));
    }

    [Fact]
    public void Broken_key_table_offset_throws_invalid_data()
    {
        var data = new SteamAppInfoBuilder().Add(1, "Game", "Spiel").Build();
        BitConverter.GetBytes((long)data.Length + 100).CopyTo(data, 8);

        Assert.Throws<InvalidDataException>(() => SteamAppInfoReader.Read(data, new HashSet<uint> { 1 }, Ct));
    }

    [Fact]
    public void Broken_key_index_throws_invalid_data()
    {
        var data = new SteamAppInfoBuilder().Add(1, "Game", "Spiel").Build();
        var firstKey = 16 + 8 + 60 + 1; // Kopf, AppID+Größe, Eintragskopf, Typbyte
        BitConverter.GetBytes(int.MaxValue).CopyTo(data, firstKey);

        Assert.Throws<InvalidDataException>(() => SteamAppInfoReader.Read(data, new HashSet<uint> { 1 }, Ct));
    }

    [Fact]
    public void Broken_unknown_value_type_throws_invalid_data()
    {
        var data = new SteamAppInfoBuilder().Add(1, "Game", "Spiel").Build();
        data[16 + 8 + 60] = 0x42;

        Assert.Throws<InvalidDataException>(() => SteamAppInfoReader.Read(data, new HashSet<uint> { 1 }, Ct));
    }

    [Fact]
    public void Honors_cancellation()
    {
        var data = new SteamAppInfoBuilder().Add(1, "Game", "Spiel").Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => SteamAppInfoReader.Read(data, new HashSet<uint> { 1 }, cts.Token));
    }
}
