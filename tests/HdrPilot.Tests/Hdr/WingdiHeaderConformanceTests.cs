using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HdrPilot.Core.Native;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Hdr;

/// <summary>
/// Gleicht die P/Invoke-Deklarationen automatisch mit der wingdi.h des Windows SDK ab: Feldreihenfolge,
/// Offsets, Größen, Konstanten und Bitpositionen. Der Header liegt nicht im Repository (Microsoft-Lizenz);
/// er wird in .sdk\wingdi.h erwartet (siehe tools\fetch-sdk-headers.ps1) oder über HDRPILOT_SDK_INCLUDE.
/// </summary>
public sealed class WingdiHeaderConformanceTests
{
    public static TheoryData<string> Structs =>
    [
        "DISPLAYCONFIG_RATIONAL",
        "DISPLAYCONFIG_2DREGION",
        "DISPLAYCONFIG_VIDEO_SIGNAL_INFO",
        "DISPLAYCONFIG_SOURCE_MODE",
        "DISPLAYCONFIG_TARGET_MODE",
        "DISPLAYCONFIG_DESKTOP_IMAGE_INFO",
        "DISPLAYCONFIG_MODE_INFO",
        "DISPLAYCONFIG_PATH_SOURCE_INFO",
        "DISPLAYCONFIG_PATH_TARGET_INFO",
        "DISPLAYCONFIG_PATH_INFO",
        "DISPLAYCONFIG_DEVICE_INFO_HEADER",
        "DISPLAYCONFIG_SOURCE_DEVICE_NAME",
        "DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS",
        "DISPLAYCONFIG_TARGET_DEVICE_NAME",
        "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO",
        "DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE",
        "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2",
        "DISPLAYCONFIG_SET_HDR_STATE",
    ];

    public static TheoryData<string> Constants =>
    [
        "QDC_ALL_PATHS",
        "QDC_ONLY_ACTIVE_PATHS",
        "DISPLAYCONFIG_PATH_ACTIVE",
        "DISPLAYCONFIG_PATH_MODE_IDX_INVALID",
        "DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE",
        "DISPLAYCONFIG_MODE_INFO_TYPE_TARGET",
        "DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE",
        "DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME",
        "DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME",
        "DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO",
        "DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE",
        "DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2",
        "DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE",
        "DISPLAYCONFIG_ADVANCED_COLOR_MODE_SDR",
        "DISPLAYCONFIG_ADVANCED_COLOR_MODE_WCG",
        "DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR",
    ];

    public static TheoryData<string, string, int> Bits => new()
    {
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO", "advancedColorSupported", AdvancedColorBits.LegacyAdvancedColorSupported },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO", "advancedColorEnabled", AdvancedColorBits.LegacyAdvancedColorEnabled },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO", "wideColorEnforced", AdvancedColorBits.LegacyWideColorEnforced },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO", "advancedColorForceDisabled", AdvancedColorBits.LegacyAdvancedColorForceDisabled },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2", "advancedColorSupported", AdvancedColorBits.Info2AdvancedColorSupported },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2", "advancedColorActive", AdvancedColorBits.Info2AdvancedColorActive },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2", "advancedColorLimitedByPolicy", AdvancedColorBits.Info2AdvancedColorLimitedByPolicy },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2", "highDynamicRangeSupported", AdvancedColorBits.Info2HighDynamicRangeSupported },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2", "highDynamicRangeUserEnabled", AdvancedColorBits.Info2HighDynamicRangeUserEnabled },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2", "wideColorSupported", AdvancedColorBits.Info2WideColorSupported },
        { "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2", "wideColorUserEnabled", AdvancedColorBits.Info2WideColorUserEnabled },
        { "DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE", "enableAdvancedColor", AdvancedColorBits.SetEnableAdvancedColor },
        { "DISPLAYCONFIG_SET_HDR_STATE", "enableHdr", AdvancedColorBits.SetEnableHdr },
    };

    private static readonly Lazy<CHeaderLayout?> Header = new(() =>
    {
        var path = HeaderFiles.Find("wingdi.h");
        return path is null ? null : CHeaderLayout.Parse(File.ReadAllText(path));
    });

    [Theory]
    [MemberData(nameof(Structs))]
    public void Struct_matches_wingdi(string structName)
    {
        var header = RequireHeader();
        Assert.False(header.IsDefinedMoreThanOnce(structName), structName + " ist im Header mehrfach definiert");
        var type = typeof(DisplayConfigNative).GetNestedType(structName, BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("Keine C#-Struktur " + structName);

        Assert.Empty(Differences(header, structName, type));
    }

    [Fact]
    public void Comparison_detects_swapped_and_missing_fields()
    {
        // Negativkontrollen: der Abgleich muss vertauschte Felder, fehlende Felder und falsche Größen melden.
        var swapped = CHeaderLayout.Parse("""
            typedef struct DISPLAYCONFIG_DEVICE_INFO_HEADER { UINT32 type; UINT32 size; LUID adapterId; UINT32 id; } DISPLAYCONFIG_DEVICE_INFO_HEADER;
            typedef struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 {
                DISPLAYCONFIG_DEVICE_INFO_HEADER header;
                union { struct { UINT32 a : 1; UINT32 r : 31; } DUMMYSTRUCTNAME; UINT32 value; } DUMMYUNIONNAME;
                UINT32 bitsPerColorChannel;
                UINT32 colorEncoding;
                UINT32 activeColorMode;
            } DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2;
            """);
        Assert.NotEmpty(Differences(swapped, "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2", typeof(DisplayConfigNative.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2)));

        var missing = CHeaderLayout.Parse("""
            typedef struct DISPLAYCONFIG_DEVICE_INFO_HEADER { UINT32 type; UINT32 size; LUID adapterId; UINT32 id; } DISPLAYCONFIG_DEVICE_INFO_HEADER;
            typedef struct DISPLAYCONFIG_SET_HDR_STATE { DISPLAYCONFIG_DEVICE_INFO_HEADER header; UINT32 value; UINT32 extra; } DISPLAYCONFIG_SET_HDR_STATE;
            """);
        Assert.NotEmpty(Differences(missing, "DISPLAYCONFIG_SET_HDR_STATE", typeof(DisplayConfigNative.DISPLAYCONFIG_SET_HDR_STATE)));

        var wrongWidth = CHeaderLayout.Parse("""
            typedef struct DISPLAYCONFIG_DEVICE_INFO_HEADER { UINT32 type; UINT64 size; LUID adapterId; UINT32 id; } DISPLAYCONFIG_DEVICE_INFO_HEADER;
            """);
        Assert.NotEmpty(Differences(wrongWidth, "DISPLAYCONFIG_DEVICE_INFO_HEADER", typeof(DisplayConfigNative.DISPLAYCONFIG_DEVICE_INFO_HEADER)));
    }

    /// <summary>Alle Abweichungen zwischen Header-Struktur und C#-Struktur (leer = identisch).</summary>
    private static List<string> Differences(CHeaderLayout header, string structName, Type type)
    {
        var diffs = new List<string>();
        var c = header.Layout(structName);
        if (c.Size != Marshal.SizeOf(type))
        {
            diffs.Add($"Größe: Header {c.Size}, C# {Marshal.SizeOf(type)}");
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(f => (f.Name, Offset: (int)Marshal.OffsetOf(type, f.Name), Size: FieldSize(f)))
            .OrderBy(f => f.Offset)
            .ToList();

        foreach (var field in fields)
        {
            var slot = c.Slots.SingleOrDefault(s => s.Offset == field.Offset);
            if (slot is null)
            {
                diffs.Add($"{field.Name}: Offset {field.Offset} gibt es im Header nicht");
                continue;
            }

            if (!slot.Names.Contains(field.Name))
            {
                diffs.Add($"{field.Name}: an Offset {field.Offset} steht im Header {string.Join("/", slot.Names)}");
            }

            if (!slot.IsUnion && slot.Size != field.Size)
            {
                diffs.Add($"{field.Name}: Größe Header {slot.Size}, C# {field.Size}");
            }
        }

        foreach (var slot in c.Slots.Where(s => !fields.Any(f => f.Offset == s.Offset)))
        {
            diffs.Add($"Header-Feld {string.Join("/", slot.Names)} an Offset {slot.Offset} fehlt in C#");
        }

        var csharpOrder = fields.GroupBy(f => f.Offset).Select(g => g.Key).ToList();
        if (!c.Slots.Select(s => s.Offset).SequenceEqual(csharpOrder))
        {
            diffs.Add("Feldreihenfolge weicht ab");
        }

        return diffs;
    }

    [Theory]
    [MemberData(nameof(Constants))]
    public void Constant_matches_wingdi(string name)
    {
        var header = RequireHeader();
        var field = typeof(DisplayConfigNative).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Xunit.Sdk.XunitException("Keine C#-Konstante " + name);
        var csharp = Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(header.Constant(name), csharp);
    }

    [Theory]
    [MemberData(nameof(Bits))]
    public void Bit_position_matches_wingdi(string structName, string bitField, int csharpBit)
    {
        var header = RequireHeader();
        Assert.Equal(header.BitPosition(structName, bitField), csharpBit);
    }

    [Fact]
    public void Parser_detects_wrong_field_order()
    {
        // Negativkontrolle: eine absichtlich vertauschte Struktur muss als abweichend erkannt werden.
        var header = CHeaderLayout.Parse("""
            typedef struct S { UINT32 a; UINT16 b; UINT16 c; UINT64 d; } S;
            typedef struct T { UINT32 value; union { struct { UINT32 x : 1; UINT32 y : 3; UINT32 z : 1; } DUMMYSTRUCTNAME; UINT32 raw; } DUMMYUNIONNAME; } T;
            """);
        var s = header.Layout("S");
        Assert.Equal(16, s.Size);
        Assert.Equal([0, 4, 6, 8], s.Slots.Select(x => x.Offset));
        Assert.Equal(["a"], s.Slots[0].Names);
        Assert.NotEqual(["c"], s.Slots[1].Names);
        Assert.Equal(4, header.BitPosition("T", "z"));
        Assert.Equal(["raw"], header.Layout("T").Slots[1].Names);
    }

    private static CHeaderLayout RequireHeader()
    {
        var header = Header.Value;
        Assert.SkipWhen(header is null, "wingdi.h nicht gefunden: tools\\fetch-sdk-headers.ps1 ausführen oder HDRPILOT_SDK_INCLUDE setzen.");
        return header!;
    }

    private static int FieldSize(FieldInfo field)
    {
        var fixedBuffer = field.GetCustomAttribute<FixedBufferAttribute>();
        if (fixedBuffer is not null)
        {
            var elementSize = fixedBuffer.ElementType == typeof(char) ? 2 : Marshal.SizeOf(fixedBuffer.ElementType);
            return fixedBuffer.Length * elementSize;
        }

        return Marshal.SizeOf(field.FieldType);
    }
}

public static class HeaderFiles
{
    /// <summary>Sucht SDK-Header in HDRPILOT_SDK_INCLUDE oder in .sdk\ oberhalb des Testordners.</summary>
    public static string? Find(string fileName)
    {
        var configured = Environment.GetEnvironmentVariable("HDRPILOT_SDK_INCLUDE");
        if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, fileName)))
        {
            return Path.Combine(configured, fileName);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".sdk", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
