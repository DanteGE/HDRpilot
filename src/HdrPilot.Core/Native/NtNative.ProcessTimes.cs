using System.Runtime.InteropServices;

namespace HdrPilot.Core.Native;

// CPU-Zeit je Prozess ohne Handle (Hinweis „Spiel hängt“): dieselbe Abfrage wie die Prozessliste
// (NtQuerySystemInformation, SystemProcessInformation = 5), nur mit den Zeitfeldern.
//
// winternl.h (SDK 10.0.26100.0) fasst die Bytes 8 bis 55 von SYSTEM_PROCESS_INFORMATION als BYTE Reserved1[48]
// zusammen. Belegung laut den phnt-Headern von System Informer (ntexapi.h, _SYSTEM_PROCESS_INFORMATION) und Geoff
// Chappell (geoffchappell.com, SYSTEM_PROCESS_INFORMATION), x64:
//   0x08 WorkingSetPrivateSize (LARGE_INTEGER, ab Vista)   0x10 HardFaultCount (ULONG, ab 7)
//   0x14 NumberOfThreadsHighWatermark (ULONG, ab 7)        0x18 CycleTime (ULONGLONG, ab 7)
//   0x20 CreateTime (LARGE_INTEGER)   0x28 UserTime (LARGE_INTEGER)   0x30 KernelTime (LARGE_INTEGER)
// danach ImageName bei 0x38 wie im Header. CreateTime, UserTime und KernelTime liegen in allen dort beschriebenen
// Versionen direkt vor ImageName. Zeiten in 100-ns-Einheiten; CreateTime als FILETIME (UTC).
// NtProcessSnapshotTests prüft die Lage gegen winternl.h (die drei Felder füllen das Ende von Reserved1) und am
// eigenen Prozess gegen GetProcessTimes.
#pragma warning disable IDE1006

internal static unsafe partial class NtNative
{
    /// <summary>Kopf eines SYSTEM_PROCESS_INFORMATION-Eintrags mit den Zeitfeldern aus Reserved1 (siehe oben).</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct SYSTEM_PROCESS_INFORMATION_TIMES
    {
        [FieldOffset(0)]
        public uint NextEntryOffset;

        /// <summary>Startzeit des Prozesses (FILETIME, UTC); erkennt wiederverwendete PIDs.</summary>
        [FieldOffset(32)]
        public long CreateTime;

        /// <summary>Summe der CPU-Zeit im Benutzermodus (100 ns), auch beendeter Threads.</summary>
        [FieldOffset(40)]
        public long UserTime;

        /// <summary>Summe der CPU-Zeit im Kernelmodus (100 ns), auch beendeter Threads.</summary>
        [FieldOffset(48)]
        public long KernelTime;

        [FieldOffset(80)]
        public nint UniqueProcessId;
    }
}
#pragma warning restore IDE1006
