using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HdrPilot.App.Services;
using HdrPilot.App.Setup;
using HdrPilot.App.Tests.Library;
using HdrPilot.Core.Hdr;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>
/// Testablage für Setup und Entfernen: alles unter einem Temp-Ordner (<see cref="SetupLocations.ForTestRoot"/>), Registry
/// als Attrappe, Eintrag unter „Installierte Apps“ im Speicher, App-Steuerung als Attrappe. Verknüpfungen entstehen echt
/// (Shell, COM), aber nur im Temp-Ordner. Nichts berührt die echte Installation.
/// </summary>
public sealed class SetupHarness : IDisposable
{
    public SetupHarness()
    {
        Root = Path.Combine(Path.GetTempPath(), "HDRpilot.Tests.Setup." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Locations = SetupLocations.ForTestRoot(Root);
        Log = new SetupLog(Path.Combine(Root, "setup.log"));
    }

    public string Root { get; }

    public SetupLocations Locations { get; }

    public SetupLog Log { get; }

    public FakeRegistry Registry { get; } = new();

    public InMemoryUninstallEntryStore Entries { get; } = new();

    public FakeAppControl App { get; } = new();

    public FakeHdrRestorer Hdr { get; } = new();

    public FakeSteamScanner Steam { get; } = new();

    public string LogText => File.Exists(Log.FilePath) ? File.ReadAllText(Log.FilePath) : string.Empty;

    public string InstalledFile(string relative) => Path.Combine(Locations.InstallDirectory, relative);

    /// <summary>
    /// EXE-Attrappe mit lesbarer Version (siehe <see cref="ReadVersion"/>). Bewusst ohne „MZ“ am Anfang: auf einen
    /// kaputten PE-Kopf lässt sich keine Verknüpfung speichern (die Shell meldet eine COMException).
    /// </summary>
    public static byte[] ExeBytes(string version) =>
        Encoding.UTF8.GetBytes("HDRPILOT-TEST-EXE VERSION=" + version + " " + new string('x', 3000));

    /// <summary>Versionsleser der Tests: liest „VERSION=x.y.z“ aus der EXE-Attrappe.</summary>
    public static Version? ReadVersion(string path)
    {
        var match = Regex.Match(File.ReadAllText(path), @"VERSION=(\d+\.\d+\.\d+)");
        return match.Success ? Version.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>Setup-Datei einer Version: EXE-Attrappe, README, Lizenzhinweise und Kommandozeile mit Versionstext.</summary>
    public SetupBundle Bundle(string version)
    {
        var build = Path.Combine(Root, "Build", version + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(build, "cli"));
        var exe = Path.Combine(build, "HDRpilot.exe");
        File.WriteAllBytes(exe, ExeBytes(version));
        File.WriteAllText(Path.Combine(build, "README.md"), "README " + version);
        File.WriteAllText(Path.Combine(build, "THIRD-PARTY-NOTICES.txt"), "NOTICES " + version);
        File.WriteAllText(Path.Combine(build, "cli", "hdrpilot.exe"), "CLI " + version);
        var output = Path.Combine(build, "HDRpilot-Setup-" + version + ".exe");
        SetupBundle.Write(
            exe,
            new Dictionary<string, string>
            {
                ["README.md"] = Path.Combine(build, "README.md"),
                ["THIRD-PARTY-NOTICES.txt"] = Path.Combine(build, "THIRD-PARTY-NOTICES.txt"),
                ["cli/hdrpilot.exe"] = Path.Combine(build, "cli", "hdrpilot.exe"),
            },
            output);
        return SetupBundle.TryOpen(output) ?? throw new InvalidOperationException("Setup-Datei der Tests ungültig.");
    }

    public SetupEngine Setup(string version, ISetupFileOperations? files = null) => Setup(Bundle(version), version, files);

    public SetupEngine Setup(SetupBundle bundle, string version, ISetupFileOperations? files = null) => new(
        Locations,
        bundle,
        Log,
        Registry,
        Entries,
        App,
        ShellSetupShortcuts.Instance,
        files,
        Version.Parse(version),
        ReadVersion);

    public Task<SetupResult> InstallAsync(string version, InstallOptions? options = null, ISetupFileOperations? files = null, IProgress<SetupStep>? progress = null) =>
        Setup(version, files).InstallAsync(options ?? new InstallOptions(Autostart: true, DesktopShortcut: false, LaunchAfterInstall: false), progress);

    public UninstallEngine Uninstall(ISetupFileOperations? files = null) => new(
        Locations,
        Log,
        Registry,
        Entries,
        App,
        ShellSetupShortcuts.Instance,
        Hdr,
        Steam,
        files,
        ReadVersion);

    public Task<SetupResult> UninstallAsync(bool removeData = false, ISetupFileOperations? files = null, IProgress<UninstallStep>? progress = null) =>
        Uninstall(files).UninstallAsync(new UninstallOptions(removeData), progress);

    /// <summary>Echte Datei als fremdes Verknüpfungsziel (die Shell „repariert“ sonst fehlende Ziele).</summary>
    public string ForeignExe()
    {
        var path = Path.Combine(Root, "Other", "HDRpilot.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ExeBytes("0.9.0"));
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp-Ordner: Aufräumen ist Kür.
        }
    }
}

/// <summary>
/// Hält die Setup-Sperre wie ein zweites Setup: auf einem eigenen Thread, der bis zum Ende lebt. (Ein Mutex auf dem
/// Test-Thread wäre nach einem <c>await</c> womöglich „verlassen“, weil der Thread endet; das gilt als frei.)
/// </summary>
public sealed class HeldSetupLock : IDisposable
{
    private readonly ManualResetEventSlim _held = new();
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _thread;

    public HeldSetupLock(string name)
    {
        _thread = new Thread(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, name);
            mutex.WaitOne();
            _held.Set();
            _release.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true,
        };
        _thread.Start();
        _held.Wait(TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        _release.Set();
        _thread.Join(TimeSpan.FromSeconds(10));
        _held.Dispose();
        _release.Dispose();
    }
}

/// <summary>Liest Verknüpfungen auf einem STA-Thread (Shell-COM).</summary>
public static class SetupLinks
{
    public static ShortcutInfo Read(string path)
    {
        ShortcutInfo? info = null;
        Sta.Run(() => info = ShortcutService.Read(path));
        return info!;
    }
}

/// <summary>Fortschritt ohne Umweg über einen Synchronisationskontext (Reihenfolge bleibt erhalten).</summary>
public sealed class SyncProgress<T> : IProgress<T>
{
    private readonly object _gate = new();
    private readonly List<T> _items = [];

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToList();
            }
        }
    }

    public void Report(T value)
    {
        lock (_gate)
        {
            _items.Add(value);
        }
    }
}

public sealed class FakeAppControl : ISetupAppControl
{
    public bool Running { get; set; }

    public bool StopSucceeds { get; set; } = true;

    public int StopCalls { get; private set; }

    public List<(string Exe, string Arguments)> Started { get; } = [];

    public bool IsRunning() => Running;

    public bool Stop(TimeSpan timeout)
    {
        StopCalls++;
        if (StopSucceeds)
        {
            Running = false;
        }

        return !Running;
    }

    public bool Start(string exePath, string arguments)
    {
        Started.Add((exePath, arguments));
        return true;
    }
}

public sealed class InMemoryUninstallEntryStore : IUninstallEntryStore
{
    public UninstallEntry? Entry { get; set; }

    /// <summary>Wird bei jedem Schreiben geworfen (Fehler beim Eintragen).</summary>
    public Exception? FailOnWrite { get; set; }

    public UninstallEntry? Read() => Entry;

    public void Write(UninstallEntry entry)
    {
        if (FailOnWrite is not null)
        {
            throw FailOnWrite;
        }

        Entry = entry;
    }

    public void Delete() => Entry = null;
}

public sealed class FakeHdrRestorer : IHdrStateRestorer
{
    public List<(string MonitorDevicePath, bool Enable)> Calls { get; } = [];

    public HdrSetStatus Status { get; set; } = HdrSetStatus.Success;

    public Task<HdrSetResult> SetHdrAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken = default)
    {
        Calls.Add((monitorDevicePath, enable));
        return Task.FromResult(new HdrSetResult { Status = Status, MonitorDevicePath = monitorDevicePath, Message = "Attrappe" });
    }
}

public sealed class FakeSteamScanner : ISteamLaunchOptionScanner
{
    public IReadOnlyList<string> Games { get; set; } = [];

    public IReadOnlyList<string> FindGamesWithLaunchOption() => Games;
}

/// <summary>
/// Echte Dateioperationen, die bei bestimmten Aufrufen scheitern (<c>fail(operation, pfad)</c> liefert die Ausnahme)
/// oder beim Anlegen das erste Byte verfälschen.
/// </summary>
public sealed class FailingFileOperations(Func<string, string, Exception?> fail, Func<string, bool>? corrupt = null) : ISetupFileOperations
{
    private readonly SetupFileOperations _inner = SetupFileOperations.Instance;

    public List<string> Calls { get; } = [];

    public static IOException SharingViolation() => new("Die Datei wird von einem anderen Prozess verwendet.", unchecked((int)0x80070020));

    public static IOException DiskFull() => new("Nicht genügend Speicherplatz.", unchecked((int)0x80070070));

    public void CreateDirectory(string path)
    {
        Check("CreateDirectory", path);
        _inner.CreateDirectory(path);
    }

    public Stream Create(string path)
    {
        Check("Create", path);
        var stream = _inner.Create(path);
        return corrupt?.Invoke(path) == true ? new CorruptingStream(stream) : stream;
    }

    public void Replace(string source, string destination, string backup)
    {
        Check("Replace", destination);
        _inner.Replace(source, destination, backup);
    }

    public void Move(string source, string destination)
    {
        Check("Move", destination);
        _inner.Move(source, destination);
    }

    public void Delete(string path)
    {
        Check("Delete", path);
        _inner.Delete(path);
    }

    private void Check(string operation, string path)
    {
        Calls.Add(operation + " " + path);
        if (fail(operation, path) is { } ex)
        {
            throw ex;
        }
    }

    /// <summary>Kippt ein Bit im ersten geschriebenen Byte (Datenfehler beim Ablegen).</summary>
    private sealed class CorruptingStream(Stream inner) : Stream
    {
        private bool _done;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (!_done && buffer.Length > 0)
            {
                var copy = buffer.ToArray();
                copy[0] ^= 0x01;
                _done = true;
                inner.Write(copy);
                return;
            }

            inner.Write(buffer);
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>Setup-Dateien beliebigen Inhalts (auch kaputte), am Format von <see cref="SetupBundle"/> vorbei geschrieben.</summary>
public static class RawSetupFile
{
    public static void Write(string output, byte[] exe, IReadOnlyDictionary<string, byte[]>? entries, string magic = SetupBundle.Magic, byte[]? exeHash = null)
    {
        using var file = File.Create(output);
        file.Write(exe);
        long payloadLength = 0;
        if (entries is not null)
        {
            using var zipBuffer = new MemoryStream();
            using (var zip = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, data) in entries)
                {
                    using var entry = zip.CreateEntry(name).Open();
                    entry.Write(data);
                }
            }

            payloadLength = zipBuffer.Length;
            file.Write(zipBuffer.ToArray());
        }

        var footer = new byte[SetupBundle.FooterLength];
        Encoding.ASCII.GetBytes(magic).CopyTo(footer, 0);
        BitConverter.GetBytes((long)exe.Length).CopyTo(footer, 16);
        BitConverter.GetBytes(payloadLength).CopyTo(footer, 24);
        (exeHash ?? SHA256.HashData(exe)).CopyTo(footer, 32);
        file.Write(footer);
    }
}
