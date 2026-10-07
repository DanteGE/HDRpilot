using System.Runtime.InteropServices;
using HdrPilot.Core.Hdr;

namespace HdrPilot.Core.Native;

#pragma warning disable IDE1006 // Namen folgen WinUser.h

internal static unsafe partial class InputNative
{
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_LMENU = 0xA4;
    public const ushort VK_B = 0x42;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT_UNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUT_UNION u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);
}

/// <summary>Notlösung: Win+Alt+B per SendInput. Schaltet nur um und betrifft alle HDR-fähigen Bildschirme.</summary>
public sealed unsafe class Win32HdrHotkeySender : IHdrHotkeySender
{
    public bool SendToggle()
    {
        var inputs = stackalloc InputNative.INPUT[6];
        inputs[0] = Key(InputNative.VK_LWIN, down: true, extended: true);
        inputs[1] = Key(InputNative.VK_LMENU, down: true, extended: false);
        inputs[2] = Key(InputNative.VK_B, down: true, extended: false);
        inputs[3] = Key(InputNative.VK_B, down: false, extended: false);
        inputs[4] = Key(InputNative.VK_LMENU, down: false, extended: false);
        inputs[5] = Key(InputNative.VK_LWIN, down: false, extended: true);
        var sent = InputNative.SendInput(6, inputs, sizeof(InputNative.INPUT));
        return sent == 6;
    }

    private static InputNative.INPUT Key(ushort vk, bool down, bool extended) => new()
    {
        type = InputNative.INPUT_KEYBOARD,
        u = new InputNative.INPUT_UNION
        {
            ki = new InputNative.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = (down ? 0u : InputNative.KEYEVENTF_KEYUP) | (extended ? InputNative.KEYEVENTF_EXTENDEDKEY : 0u),
            },
        },
    };
}
#pragma warning restore IDE1006
