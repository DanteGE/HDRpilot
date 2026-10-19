namespace HdrPilot.Tests.Sessions;

/// <summary>
/// „Nicht installierte Spiele ausblenden“ (E90) betrifft nur die Bibliothek: Die Zuordnung der Spielprozesse sieht
/// ausgeblendete Spiele weiter, „HDR automatisch“ schaltet für sie wie bisher (E38).
/// </summary>
public sealed partial class SessionManagerTests
{
    [Fact]
    public async Task Uninstalled_hidden_game_with_hdr_auto_still_switches_hdr()
    {
        _config.Config.Settings.HideUninstalledGames = true;
        var game = _config.AddGame("1", DirA);
        game.IsInstalled = false; // etwa ein Laufwerk, das beim Einlesen fehlte
        await Poll();
        _processes.StartExe(ExeA);
        await Poll();

        Assert.True(PrimaryOn);
        Assert.Equal("Steam:1", Assert.Single(Manager.Sessions).GameKey);
    }
}
