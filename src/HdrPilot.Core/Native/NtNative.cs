using System.Runtime.InteropServices;

namespace HdrPilot.Core.Native;

// Deklarationen aus winternl.h (SDK 10.0.26100.0). Namen wie im Header (ProcessSnapshotHeaderTests gleicht ab).
#pragma warning disable IDE1006

internal static unsafe partial class NtNative
{
    public const int SystemProcessInformation = 5;

    /// <summary>Ab Windows 11 24H2: nur PID, Eltern-PID, Sequenznummer und Name, ohne Thread-Daten (deutlich billiger).</summary>
    public const int SystemBasicProcessInformation = 252;

    public const uint STATUS_SUCCESS = 0x00000000;
    public const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    public const uint STATUS_BUFFER_TOO_SMALL = 0xC0000023;

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_BASICPROCESS_INFORMATION
    {
        public uint NextEntryOffset;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
        public ulong SequenceNumber;
        public UNICODE_STRING ImageName;
    }

    /// <summary>Nur die dokumentierten Felder bis UniqueProcessId; der Rest wird nicht gelesen.</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct SYSTEM_PROCESS_INFORMATION_HEAD
    {
        [FieldOffset(0)]
        public uint NextEntryOffset;

        [FieldOffset(4)]
        public uint NumberOfThreads;

        [FieldOffset(56)]
        public UNICODE_STRING ImageName;

        [FieldOffset(72)]
        public int BasePriority;

        [FieldOffset(80)]
        public nint UniqueProcessId;
    }

    [LibraryImport("ntdll.dll")]
    public static partial uint NtQuerySystemInformation(int systemInformationClass, void* systemInformation, uint systemInformationLength, out uint returnLength);

    // --- Nicht in winternl.h: EXE-Pfad zu einer PID ohne Handle (E65) ---
    // SystemProcessIdInformation gibt es seit Windows Vista; Microsoft dokumentiert die Klasse nicht. Beschrieben bei
    // Geoff Chappell (geoffchappell.com, SYSTEM_PROCESS_ID_INFORMATION) und in den phnt-Headern von System Informer.

    /// <summary>NT-Pfad (\Device\HarddiskVolumeN\...) des EXE einer PID, ohne OpenProcess, auch für geschützte Prozesse.</summary>
    public const int SystemProcessIdInformation = 88;

    public const uint STATUS_NOT_IMPLEMENTED = 0xC0000002;
    public const uint STATUS_INVALID_INFO_CLASS = 0xC0000003;

    /// <summary>Eingabe: ProcessId und ein leerer Puffer in ImageName (Length 0, MaximumLength in Byte).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_PROCESS_ID_INFORMATION
    {
        public nint ProcessId;
        public UNICODE_STRING ImageName;
    }

    /// <summary>Ziel eines Laufwerksbuchstabens, etwa "C:" → "\Device\HarddiskVolume3" (mehrere Ziele durch \0 getrennt).</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint QueryDosDevice(string lpDeviceName, char* lpTargetPath, uint ucchMax);
}
#pragma warning restore IDE1006
