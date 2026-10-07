using System.Runtime.InteropServices;

namespace HdrPilot.Core.Launch;

/// <summary>
/// Die Befehlszeile genau so, wie der Aufrufer (Steam) sie übergab. Environment.CommandLine taugt dafür nicht: .NET setzt
/// sie aus args[] neu zusammen (gemessen mit .NET 10: <c>-name="foo bar"</c> wird zu <c>"-name=foo bar"</c>, mehrere
/// Leerzeichen werden eines, und vorne steht die .dll statt der .exe).
/// </summary>
public static partial class RawCommandLine
{
    public static string Get()
    {
        var pointer = GetCommandLineW();
        return pointer == IntPtr.Zero ? Environment.CommandLine : Marshal.PtrToStringUni(pointer) ?? Environment.CommandLine;
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCommandLineW();
}
