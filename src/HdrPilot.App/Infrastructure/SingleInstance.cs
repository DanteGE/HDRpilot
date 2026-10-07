using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Interop;

namespace HdrPilot.App.Infrastructure;

/// <summary>
/// Nur eine Instanz je Benutzer-Sitzung (und je Konfigurationsordner). Weitere Aufrufe reichen ihre Befehle per
/// WM_COPYDATA an ein unsichtbares Nachrichtenfenster der ersten Instanz weiter. Das ist rein lokal und öffnet
/// keine Netzwerk-Schnittstelle (anders als benannte Pipes).
/// </summary>
public sealed partial class SingleInstance : IDisposable
{
    private const int WM_COPYDATA = 0x004A;
    private const int CopyDataMagic = 0x48445250; // "HDRP"
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly Mutex _mutex;
    private HwndSource? _window;
    private Action<IReadOnlyList<string>>? _onCommand;

    private SingleInstance(Mutex mutex, string id)
    {
        _mutex = mutex;
        Id = id;
    }

    public string Id { get; }

    /// <summary>Kennung: "default" für die normale Ablage, sonst ein Hash des Konfigurationsordners.</summary>
    public static string IdFor(string? configDir) => configDir is null
        ? "default"
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(configDir)).ToUpperInvariant())))[..16];

    /// <summary>Liefert die Instanz, wenn dies der erste Aufruf ist, sonst null.</summary>
    public static SingleInstance? TryAcquire(string id)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\HdrPilot.Instance." + id, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, id);
    }

    /// <summary>Öffnet das Nachrichtenfenster. Befehle kommen auf dem UI-Thread an.</summary>
    public void Listen(Action<IReadOnlyList<string>> onCommand)
    {
        _onCommand = onCommand;
        var parameters = new HwndSourceParameters(WindowTitle(Id))
        {
            ParentWindow = HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _window = new HwndSource(parameters);
        _window.AddHook(WndProc);
    }

    /// <summary>Sendet Befehle an die laufende Instanz. True, wenn sie angenommen wurden.</summary>
    public static unsafe bool TrySend(string id, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        IntPtr target;
        do
        {
            target = FindWindowExW(HWND_MESSAGE, IntPtr.Zero, null, WindowTitle(id));
            if (target != IntPtr.Zero)
            {
                break;
            }

            Thread.Sleep(100); // erste Instanz startet gerade
        }
        while (DateTime.UtcNow < deadline);

        if (target == IntPtr.Zero)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args));
        fixed (byte* data = payload)
        {
            var cds = new COPYDATASTRUCT { dwData = CopyDataMagic, cbData = payload.Length, lpData = (IntPtr)data };
            var result = SendMessageTimeoutW(target, WM_COPYDATA, IntPtr.Zero, (IntPtr)(&cds), 0x0002 /* SMTO_ABORTIFHUNG */, (uint)timeout.TotalMilliseconds, out var answer);
            return result != IntPtr.Zero && answer == 1;
        }
    }

    public void Dispose()
    {
        _window?.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Nicht vom aktuellen Thread besessen: beim Beenden unkritisch.
        }

        _mutex.Dispose();
    }

    private static string WindowTitle(string id) => "HdrPilot.Commands." + id;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_COPYDATA)
        {
            return IntPtr.Zero;
        }

        handled = true;
        try
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (cds.dwData != CopyDataMagic || cds.cbData <= 0 || cds.cbData > 64 * 1024)
            {
                return IntPtr.Zero;
            }

            var bytes = new byte[cds.cbData];
            Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
            var args = JsonSerializer.Deserialize<string[]>(bytes) ?? [];
            _onCommand?.Invoke(args);
            return new IntPtr(1);
        }
        catch (JsonException)
        {
            return IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public nint dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string lpszWindow);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageTimeoutW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);
}
