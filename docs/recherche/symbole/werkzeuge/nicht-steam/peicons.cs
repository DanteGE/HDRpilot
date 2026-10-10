using System; using System.Runtime.InteropServices; using System.Collections.Generic; using System.Text;
public static class PeIcons {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string f, IntPtr h, uint flags);
  [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr h);
  delegate bool EnumResNameProc(IntPtr h, IntPtr type, IntPtr name, IntPtr p);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr h, IntPtr type, EnumResNameProc cb, IntPtr p);
  [DllImport("kernel32.dll")] static extern IntPtr FindResource(IntPtr h, IntPtr name, IntPtr type);
  [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr h, IntPtr r);
  [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr r);
  [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr h, IntPtr r);
  // returns "groups=N; first=sizes" ; first group = what ExtractIcon index 0 returns
  public static string Info(string path) {
    IntPtr h = LoadLibraryEx(path, IntPtr.Zero, 0x00000002 | 0x00000020); // DATAFILE | IMAGE_RESOURCE
    if (h == IntPtr.Zero) return "load-failed:" + Marshal.GetLastWin32Error();
    try {
      var names = new List<IntPtr>();
      EnumResourceNames(h, (IntPtr)14, (m, t, n, p) => { names.Add(n); return true; }, IntPtr.Zero);
      if (names.Count == 0) return "groups=0";
      IntPtr r = FindResource(h, names[0], (IntPtr)14); IntPtr d = LockResource(LoadResource(h, r)); int sz = (int)SizeofResource(h, r);
      var b = new byte[sz]; Marshal.Copy(d, b, 0, sz);
      int count = BitConverter.ToUInt16(b, 4); var sizes = new List<string>();
      for (int i = 0; i < count; i++) { int o = 6 + i * 14; int w = b[o] == 0 ? 256 : b[o]; int bpp = BitConverter.ToUInt16(b, o + 6); sizes.Add(w + "x" + bpp); }
      return "groups=" + names.Count + "; first=" + string.Join(",", sizes);
    } finally { FreeLibrary(h); } }
}

