using System;
using System.Runtime.InteropServices;
using System.Text;

// READ-ONLY probe of the NVIDIA DRS database: CreateSession, LoadSettings, FindApplicationByName,
// GetProfileInfo, EnumApplications, DestroySession. Never calls SaveSettings or any Set/Create/Delete function.
public static class DrsProbe
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr QI(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_Init();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_CreateSession(out IntPtr s);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_Session(IntPtr s);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_Find(IntPtr s, IntPtr name, out IntPtr prof, IntPtr app);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_ProfInfo(IntPtr s, IntPtr prof, IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_EnumApps(IntPtr s, IntPtr prof, uint start, ref uint count, IntPtr apps);

    const int US = 4096; // NvAPI_UnicodeString bytes (2048 * NvU16)
    const int APP_SIZE = 4 + 4 + US * 4 + 4 + US; // 20492
    const int PROF_SIZE = 4 + US + 4 + 4 + 4 + 4; // 4116
    static uint AppVer = (uint)APP_SIZE | (4u << 16);
    static uint ProfVer = (uint)PROF_SIZE | (1u << 16);

    static T F<T>(uint id) where T : class
    {
        IntPtr p = QI(id);
        if (p == IntPtr.Zero) throw new Exception("QI failed " + id.ToString("X8"));
        return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
    }

    static string Str(IntPtr basePtr, int off) { return Marshal.PtrToStringUni(IntPtr.Add(basePtr, off)); }

    static string AppDesc(IntPtr a)
    {
        uint flags = (uint)Marshal.ReadInt32(a, 8 + US * 4);
        return string.Format("appName='{0}' predefined={1} launcher='{2}' fileInFolder='{3}' isMetro={4} isCmdLine={5} cmd='{6}'",
            Str(a, 8), Marshal.ReadInt32(a, 4), Str(a, 8 + US * 2), Str(a, 8 + US * 3), flags & 1, (flags >> 1) & 1, Str(a, 8 + US * 4 + 4));
    }

    public static string Run(string[] queries, int maxAppsListed)
    {
        var sb = new StringBuilder();
        var init = F<D_Init>(0x0150E828);
        sb.AppendLine("Initialize=" + init());
        var create = F<D_CreateSession>(0x0694D52E);
        var destroy = F<D_Session>(0xDAD9CFF8);
        var load = F<D_Session>(0x375DBD6B);
        var find = F<D_Find>(0xEEE566B2);
        var pinfo = F<D_ProfInfo>(0x61CD6FD6);
        var enumApps = F<D_EnumApps>(0x7FA2173A);
        IntPtr s;
        sb.AppendLine("CreateSession=" + create(out s));
        try
        {
            sb.AppendLine("LoadSettings=" + load(s));
            IntPtr name = Marshal.AllocHGlobal(US);
            IntPtr app = Marshal.AllocHGlobal(APP_SIZE);
            IntPtr prof = Marshal.AllocHGlobal(PROF_SIZE);
            try
            {
                foreach (var q in queries)
                {
                    for (int i = 0; i < US; i++) Marshal.WriteByte(name, i, 0);
                    byte[] qb = Encoding.Unicode.GetBytes(q);
                    Marshal.Copy(qb, 0, name, Math.Min(qb.Length, US - 2));
                    for (int i = 0; i < APP_SIZE; i++) Marshal.WriteByte(app, i, 0);
                    Marshal.WriteInt32(app, 0, (int)AppVer);
                    IntPtr hp;
                    int r = find(s, name, out hp, app);
                    if (r != 0) { sb.AppendLine(string.Format("[{0}] -> status {1}", q, r)); continue; }
                    for (int i = 0; i < PROF_SIZE; i++) Marshal.WriteByte(prof, i, 0);
                    Marshal.WriteInt32(prof, 0, (int)ProfVer);
                    int pr = pinfo(s, hp, prof);
                    string pname = pr == 0 ? Str(prof, 4) : ("<GetProfileInfo " + pr + ">");
                    int predefP = Marshal.ReadInt32(prof, 4 + US + 4);
                    int nApps = Marshal.ReadInt32(prof, 4 + US + 8);
                    int nSet = Marshal.ReadInt32(prof, 4 + US + 12);
                    sb.AppendLine(string.Format("[{0}] -> profile '{1}' (predefined={2}, apps={3}, settings={4}); matched {5}", q, pname, predefP, nApps, nSet, AppDesc(app)));
                    if (maxAppsListed > 0 && nApps > 1)
                    {
                        uint cnt = (uint)Math.Min(nApps, maxAppsListed);
                        IntPtr arr = Marshal.AllocHGlobal(APP_SIZE * (int)cnt);
                        try
                        {
                            for (int k = 0; k < cnt; k++) { for (int i = 0; i < APP_SIZE; i += 4) Marshal.WriteInt32(arr, k * APP_SIZE + i, 0); Marshal.WriteInt32(arr, k * APP_SIZE, (int)AppVer); }
                            int er = enumApps(s, hp, 0, ref cnt, arr);
                            if (er == 0) for (int k = 0; k < cnt; k++) sb.AppendLine("      app: " + AppDesc(IntPtr.Add(arr, k * APP_SIZE)));
                            else sb.AppendLine("      EnumApplications status " + er);
                        }
                        finally { Marshal.FreeHGlobal(arr); }
                    }
                }
            }
            finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(app); Marshal.FreeHGlobal(prof); }
        }
        finally { sb.AppendLine("DestroySession=" + destroy(s)); }
        return sb.ToString();
    }
}
