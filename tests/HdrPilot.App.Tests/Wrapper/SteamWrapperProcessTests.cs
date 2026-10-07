using System.Diagnostics;

namespace HdrPilot.App.Tests.Wrapper;

/// <summary>
/// Ende zu Ende: HdrPilot.exe als Steam-Startoption, immer mit eigener Ablage (--config-dir in einem Testordner, also nie
/// die echte Konfiguration und nie die laufende Instanz) und einem harmlosen Ersatz-Spiel (cmd.exe legt einen Ordner an).
/// </summary>
public sealed class SteamWrapperProcessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HdrPilotTests", Guid.NewGuid().ToString("N"), "Ablage mit Leerzeichen");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        for (var i = 0; i < 20 && Directory.Exists(root); i++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(100); // cmd.exe kann den Ordner noch kurz offen halten
            }
        }
    }

    [Fact]
    public async Task Wrapper_starts_the_stand_in_game_logs_one_line_and_exits_without_starting_hdrpilot()
    {
        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(AppContext.BaseDirectory, "HDRpilot.exe");
        Assert.True(File.Exists(exe), "HDRpilot.exe fehlt neben den Tests: " + exe);
        var marker = Path.Combine(_dir, "gestartet");
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var gameCommand = $"\"{cmd}\" /c mkdir \"{marker}\"";
        var info = new ProcessStartInfo(exe)
        {
            Arguments = $"--config-dir \"{_dir}\" --wrap -- {gameCommand}",
            UseShellExecute = false,
            WorkingDirectory = _dir,
        };
        info.Environment["SteamAppId"] = "480";
        info.Environment.Remove("SteamGameId");

        using (var process = Process.Start(info)!)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            Assert.Equal(0, process.ExitCode);
        }

        // Das Ersatz-Spiel läuft unabhängig vom Wrapper (der nicht darauf wartet).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!Directory.Exists(marker) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(Directory.Exists(marker), "Das Ersatz-Spiel wurde nicht gestartet.");

        var line = Assert.Single(File.ReadAllLines(Path.Combine(_dir, "logs", "wrap.log")));
        Assert.Contains("SteamAppId: 480", line, StringComparison.Ordinal);
        Assert.Contains("HDR vorbereitet: nein (HDRpilot läuft nicht)", line, StringComparison.Ordinal);
        Assert.Contains("Spiel gestartet: ja", line, StringComparison.Ordinal);
        Assert.Contains("Arbeitsordner: " + _dir + " |", line, StringComparison.Ordinal);
        Assert.EndsWith("--wrap -- " + gameCommand, line, StringComparison.Ordinal);

        // Nie die Tray-App: sie hätte config.json und ihr normales Protokoll angelegt.
        Assert.False(File.Exists(Path.Combine(_dir, "config.json")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "logs"), "hdrpilot-*.log"));
    }
}
