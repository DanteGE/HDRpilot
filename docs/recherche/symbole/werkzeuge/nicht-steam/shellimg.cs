using System; using System.Runtime.InteropServices;
public static class ShellImg {
  [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
  [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
  [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)] static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
  [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int c, out BITMAP bm);
  [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
  // flags: 0x1 BIGGERSIZEOK, 0x4 ICONONLY, 0x10 ICONBACKGROUND? (SIIGBF_ICONONLY=0x4)
  public static string Get(string parsingName, int size, int flags, string savePng) {
    try {
      IShellItemImageFactory f; SHCreateItemFromParsingName(parsingName, IntPtr.Zero, new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), out f);
      IntPtr hbm; int hr = f.GetImage(new SIZE { cx = size, cy = size }, flags, out hbm);
      if (hr != 0) return "hr=0x" + hr.ToString("x8");
      BITMAP bm; GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), out bm);
      if (savePng != null) { var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src)); using (var fs = System.IO.File.Create(savePng)) enc.Save(fs); }
      DeleteObject(hbm); return bm.bmWidth + "x" + bm.bmHeight + "x" + bm.bmBitsPixel;
    } catch (Exception ex) { return "ERR " + ex.Message; } }
}
