using HdrPilot.Core.Hdr;

namespace HdrPilot.Tests.TestSupport;

/// <summary>Simulierter Bildschirm für <see cref="FakeDisplayConfigApi"/>.</summary>
public sealed class FakeMonitor
{
    public required string Name { get; init; }

    public required string DevicePath { get; init; }

    public DisplayTarget Target { get; set; }

    public bool IsPrimary { get; init; }

    public bool HdrSupported { get; set; } = true;

    public bool HdrUserEnabled { get; set; }

    public bool WcgSupported { get; set; } = true;

    public bool WcgUserEnabled { get; set; }

    public bool LimitedByPolicy { get; set; }

    /// <summary>Wenn true, wird HDR zwar eingeschaltet, aber nie aktiv (Richtlinie, Akku).</summary>
    public bool HdrNeverActivates { get; set; }

    public AdvancedColorMode ActiveMode =>
        HdrUserEnabled && !HdrNeverActivates ? AdvancedColorMode.Hdr
        : WcgUserEnabled ? AdvancedColorMode.Wcg
        : AdvancedColorMode.Sdr;

    /// <summary>Bitfeld wie DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2.value.</summary>
    public uint Info2Value =>
        (HdrSupported || WcgSupported ? 1u << 0 : 0u)
        | (ActiveMode != AdvancedColorMode.Sdr ? 1u << 1 : 0u)
        | (LimitedByPolicy ? 1u << 3 : 0u)
        | (HdrSupported ? 1u << 4 : 0u)
        | (HdrUserEnabled ? 1u << 5 : 0u)
        | (WcgSupported ? 1u << 6 : 0u)
        | (WcgUserEnabled ? 1u << 7 : 0u);

    /// <summary>Bitfeld wie DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO.value (Verhalten wie am echten System mit Build 26200 gemessen).</summary>
    public uint LegacyValue =>
        (HdrSupported || WcgSupported ? 1u << 0 : 0u)
        | (ActiveMode != AdvancedColorMode.Sdr ? 1u << 1 : 0u)
        | (ActiveMode == AdvancedColorMode.Wcg || (WcgUserEnabled && ActiveMode == AdvancedColorMode.Sdr) ? 1u << 2 : 0u)
        | (LimitedByPolicy ? 1u << 3 : 0u);
}

/// <summary>Attrappe der Display-Configuration-API mit Zustandsautomat und Aufrufprotokoll.</summary>
public sealed class FakeDisplayConfigApi : IDisplayConfigApi
{
    private readonly object _gate = new();
    private readonly List<(FakeMonitor Monitor, Action Apply, int ReadsLeft)> _pending = [];

    public List<FakeMonitor> Monitors { get; } = [];

    public List<string> Calls { get; } = [];

    public int QueryResult { get; set; }

    public int Info2Result { get; set; }

    public int LegacyInfoResult { get; set; }

    public int SetHdrResult { get; set; }

    public int SetAdvancedColorResult { get; set; }

    /// <summary>Nach wie vielen Lesevorgängen ein Setzen sichtbar wird (Windows braucht etwas Zeit).</summary>
    public int ApplyAfterReads { get; set; } = 1;

    /// <summary>Wenn true, ignoriert Windows das Setzen (Bestätigung schlägt fehl).</summary>
    public bool IgnoreSets { get; set; }

    /// <summary>Wenn true, melden beide Set-Aufrufe Fehler 31, schalten aber trotzdem.</summary>
    public bool FailButApply { get; set; }

    /// <summary>Wird nach jedem erfolgreichen Setzen aufgerufen, etwa um die Ziel-ID zu ändern.</summary>
    public Action<FakeMonitor>? AfterSet { get; set; }

    public static FakeDisplayConfigApi WithSingleHdrMonitor(bool hdrOn = false, bool wcgOn = true)
    {
        var api = new FakeDisplayConfigApi();
        api.Monitors.Add(new FakeMonitor
        {
            Name = "MAG 341C OLED",
            DevicePath = @"\\?\DISPLAY#MSI3DD0#5&1a2b3c4&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
            Target = new DisplayTarget(0x1A2B3, 0, 4353),
            IsPrimary = true,
            HdrUserEnabled = hdrOn,
            WcgUserEnabled = wcgOn,
        });
        return api;
    }

    public int CallCount(string prefix)
    {
        lock (_gate)
        {
            return Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    public int QueryActivePaths(out IReadOnlyList<RawDisplayPath> paths)
    {
        lock (_gate)
        {
            Calls.Add("Query");
            TickPending();
            paths = QueryResult == 0
                ? Monitors.Select(m => new RawDisplayPath(m.Target, m.Target.AdapterLow, m.Target.AdapterHigh, 0, m.IsPrimary)).ToList()
                : [];
            return QueryResult;
        }
    }

    public int GetTargetName(DisplayTarget target, out RawTargetName name)
    {
        lock (_gate)
        {
            var m = Monitors.FirstOrDefault(x => x.Target == target);
            name = m is null ? default : new RawTargetName(m.Name, m.DevicePath, 10);
            return m is null ? 87 : 0;
        }
    }

    public int GetSourceGdiName(uint adapterLow, int adapterHigh, uint sourceId, out string gdiDeviceName)
    {
        gdiDeviceName = @"\\.\DISPLAY" + (sourceId + 1);
        return 0;
    }

    public int GetAdvancedColorInfo(DisplayTarget target, out RawColorInfo info)
    {
        lock (_gate)
        {
            Calls.Add("Get9");
            var m = Monitors.FirstOrDefault(x => x.Target == target);
            info = m is null ? default : new RawColorInfo(m.LegacyValue, 0, m.ActiveMode == AdvancedColorMode.Sdr ? 8u : 10u);
            return m is null ? 87 : LegacyInfoResult;
        }
    }

    public int GetAdvancedColorInfo2(DisplayTarget target, out RawColorInfo2 info)
    {
        lock (_gate)
        {
            Calls.Add("Get15");
            var m = Monitors.FirstOrDefault(x => x.Target == target);
            info = m is null ? default : new RawColorInfo2(m.Info2Value, 0, m.ActiveMode == AdvancedColorMode.Sdr ? 8u : 10u, (uint)m.ActiveMode);
            return m is null ? 87 : Info2Result;
        }
    }

    public int SetAdvancedColorState(DisplayTarget target, bool enable)
    {
        lock (_gate)
        {
            Calls.Add("Set10:" + enable);
            var m = Monitors.FirstOrDefault(x => x.Target == target);
            if (m is null)
            {
                return 87;
            }

            if (SetAdvancedColorResult != 0)
            {
                return SetAdvancedColorResult;
            }

            Schedule(m, () =>
            {
                m.HdrUserEnabled = enable;
                if (!enable)
                {
                    m.WcgUserEnabled = false; // Typ 10 schaltet Advanced Color ganz ab.
                }
            });
            return FailButApply ? 31 : 0;
        }
    }

    public int SetHdrState(DisplayTarget target, bool enable)
    {
        lock (_gate)
        {
            Calls.Add("Set16:" + enable);
            var m = Monitors.FirstOrDefault(x => x.Target == target);
            if (m is null)
            {
                return 87;
            }

            if (SetHdrResult != 0)
            {
                return SetHdrResult;
            }

            Schedule(m, () => m.HdrUserEnabled = enable);
            return FailButApply ? 31 : 0;
        }
    }

    /// <summary>Simuliert Win+Alt+B: schaltet HDR an allen HDR-fähigen Bildschirmen um.</summary>
    public void SimulateHotkeyToggle()
    {
        lock (_gate)
        {
            Calls.Add("Hotkey");
            foreach (var m in Monitors.Where(x => x.HdrSupported))
            {
                m.HdrUserEnabled = !m.HdrUserEnabled;
            }
        }
    }

    private void Schedule(FakeMonitor monitor, Action apply)
    {
        if (IgnoreSets)
        {
            return;
        }

        _pending.Add((monitor, () =>
        {
            apply();
            AfterSet?.Invoke(monitor);
        }, ApplyAfterReads));
    }

    private void TickPending()
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var (monitor, apply, left) = _pending[i];
            if (left <= 1)
            {
                apply();
                _pending.RemoveAt(i);
            }
            else
            {
                _pending[i] = (monitor, apply, left - 1);
            }
        }
    }
}

public sealed class FakeHotkeySender(FakeDisplayConfigApi api, bool succeeds = true) : IHdrHotkeySender
{
    public int Sent { get; private set; }

    public bool SendToggle()
    {
        Sent++;
        if (succeeds)
        {
            api.SimulateHotkeyToggle();
        }

        return succeeds;
    }
}
