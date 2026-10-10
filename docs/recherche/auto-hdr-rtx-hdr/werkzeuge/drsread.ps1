# READ-ONLY probe of the NVIDIA DRS database. Never calls SetSetting / SaveSettings.
$code = @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class DrsProbe {
  [DllImport("nvapi64.dll", EntryPoint="nvapi_QueryInterface", CallingConvention=CallingConvention.Cdecl)]
  static extern IntPtr QI(uint id);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int F0();
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FOutPtr(out IntPtr p);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FPtr(IntPtr p);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FNum(IntPtr s, out uint n);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FEnum(IntPtr s, uint i, out IntPtr p);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FInfo(IntPtr s, IntPtr p, IntPtr info);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FGet(IntPtr s, IntPtr p, uint id, IntPtr setting);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FBase(IntPtr s, out IntPtr p);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FFindApp(IntPtr s, [MarshalAs(UnmanagedType.LPArray)] ushort[] name, out IntPtr p, IntPtr app);
  static T D<T>(uint id) where T: class { IntPtr p = QI(id); if (p==IntPtr.Zero) throw new Exception("QI failed "+id.ToString("X")); return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T; }
  const int SET_SIZE = 12320; const int PROF_SIZE = 4116; const int APP_V4_SIZE = 4+4+4096*4+4+4096;
  static string ReadW(IntPtr p, int off) { return Marshal.PtrToStringUni(IntPtr.Add(p, off)); }
  public static string Run(uint[] ids, string appPath) {
    var sb = new StringBuilder();
    int r = D<F0>(0x0150e828)(); sb.AppendLine("Initialize=" + r);
    IntPtr s; r = D<FOutPtr>(0x0694d52e)(out s); sb.AppendLine("CreateSession=" + r);
    r = D<FPtr>(0x375dbd6b)(s); sb.AppendLine("LoadSettings=" + r);
    var getSetting = D<FGet>(0x73bf8338); var enumP = D<FEnum>(0xbc371ee0); var info = D<FInfo>(0x61cd6fd6);
    uint n; r = D<FNum>(0x1dae4fbc)(s, out n); sb.AppendLine("NumProfiles=" + n + " (r=" + r + ")");
    IntPtr setBuf = Marshal.AllocHGlobal(SET_SIZE); IntPtr profBuf = Marshal.AllocHGlobal(PROF_SIZE);
    var counts = new Dictionary<string,int>();
    var lines = new List<string>();
    Func<IntPtr, string, int> probe = (hp, pname) => {
      int found = 0;
      foreach (uint id in ids) {
        for (int i=0;i<SET_SIZE;i+=4) Marshal.WriteInt32(setBuf, i, 0);
        Marshal.WriteInt32(setBuf, 0, SET_SIZE | (1<<16));
        int rr = getSetting(s, hp, id, setBuf);
        if (rr == 0) {
          int loc = Marshal.ReadInt32(setBuf, 4108); int isCurPre = Marshal.ReadInt32(setBuf, 4112); int preValid = Marshal.ReadInt32(setBuf, 4116);
          uint pre = (uint)Marshal.ReadInt32(setBuf, 4120); uint cur = (uint)Marshal.ReadInt32(setBuf, 8220);
          string key = id.ToString("X8") + " cur=0x" + cur.ToString("X") + (isCurPre!=0?" (predef)":" (USER)") + " loc=" + loc;
          if (!counts.ContainsKey(key)) counts[key]=0; counts[key]++;
          if (loc == 0) lines.Add(pname + " | " + id.ToString("X8") + " cur=0x" + cur.ToString("X") + " isCurPredef=" + isCurPre + " preValid=" + preValid + " pre=0x" + pre.ToString("X"));
          found++;
        }
      }
      return found;
    };
    IntPtr hb; r = D<FBase>(0xda8466a0)(s, out hb); sb.AppendLine("BaseProfile r=" + r); probe(hb, "<BASE>");
    for (uint i=0;i<n;i++) {
      IntPtr hp; r = enumP(s, i, out hp); if (r != 0) { sb.AppendLine("Enum end at " + i + " r=" + r); break; }
      for (int k=0;k<PROF_SIZE;k+=4) Marshal.WriteInt32(profBuf, k, 0);
      Marshal.WriteInt32(profBuf, 0, PROF_SIZE | (1<<16));
      info(s, hp, profBuf);
      string pname = ReadW(profBuf, 4);
      probe(hp, pname);
    }
    sb.AppendLine("--- counts per (id,value,predef/user,location) over all profiles (loc 0=current profile,1=global,2=base,3=default)");
    foreach (var kv in counts) sb.AppendLine(kv.Key + " : " + kv.Value);
    sb.AppendLine("--- entries stored directly in the profile (loc=0), max 400 lines");
    int c=0; foreach (var l in lines) { if (++c>20000) break; sb.AppendLine(l); }
    if (!string.IsNullOrEmpty(appPath)) {
      IntPtr appBuf = Marshal.AllocHGlobal(APP_V4_SIZE); for (int k=0;k<APP_V4_SIZE;k+=4) Marshal.WriteInt32(appBuf,k,0);
      Marshal.WriteInt32(appBuf, 0, APP_V4_SIZE | (4<<16));
      var w = new ushort[2048]; for (int k=0;k<appPath.Length && k<2047;k++) w[k]=appPath[k];
      IntPtr hp; r = D<FFindApp>(0xeee566b2)(s, w, out hp, appBuf);
      sb.AppendLine("FindApplicationByName(" + appPath + ") r=" + r);
      if (r == 0) { for (int k=0;k<PROF_SIZE;k+=4) Marshal.WriteInt32(profBuf, k, 0); Marshal.WriteInt32(profBuf, 0, PROF_SIZE | (1<<16)); info(s, hp, profBuf); sb.AppendLine("  -> profile '" + ReadW(profBuf,4) + "' isPredefined=" + Marshal.ReadInt32(profBuf, 4104)); lines.Clear(); probe(hp, "APP"); foreach (var l in lines) sb.AppendLine("  " + l); }
    }
    r = D<FPtr>(0xdad9cff8)(s); sb.AppendLine("DestroySession=" + r + " (no Save called)");
    return sb.ToString();
  }
}
"@
Add-Type -TypeDefinition $code -Language CSharp
$ids = [uint32[]]@(0x1077A11A, 0x00DD48FB, 0x00DD48FC, 0x00DD48FD, 0x00DD48FE, 0x00DD48FF, 0x00432F84, 0x00980896, 0x1035DB89, 0x1085DA8A)
[DrsProbe]::Run($ids, 'D:\Anwendungen\Epic Games\AlanWake2\AlanWake2.exe')
