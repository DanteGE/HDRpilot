using System; using System.Runtime.InteropServices; using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging; using System.IO; using System.Collections.Generic;
public static class Sheet {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint PrivateExtractIcons(string file, int idx, int cx, int cy, IntPtr[] phicon, uint[] ids, uint n, uint flags);
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
  public static Bitmap ExeIcon(string path, int size) {
    var h = new IntPtr[1]; var ids = new uint[1];
    uint n = PrivateExtractIcons(path, 0, size, size, h, ids, 1, 0);
    if (n == 0 || n == 0xFFFFFFFF || h[0] == IntPtr.Zero) return null;
    using (var ic = Icon.FromHandle(h[0])) { var b = ic.ToBitmap(); DestroyIcon(h[0]); return b; } }
  public static Bitmap Img(string path, int size) {
    try {
      if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return ExeIcon(path, size);
      if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) { using (var ic = new Icon(path, size, size)) return Fit(ic.ToBitmap(), size); }
      using (var fs = File.OpenRead(path)) { var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(fs, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dec.Frames[0])); var ms = new MemoryStream(); enc.Save(ms); ms.Position = 0; return Fit(new Bitmap(ms), size); }
    } catch { return null; } }
  // center-crop to square then scale
  static Bitmap Fit(Bitmap src, int size) { int s = Math.Min(src.Width, src.Height); var r = new Rectangle((src.Width - s) / 2, (src.Height - s) / 2, s, s); var b = new Bitmap(size, size); using (var g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(src, new Rectangle(0, 0, size, size), r, GraphicsUnit.Pixel); } return b; }
  public static void Make(string[] rows, string outPng) { // each row: label|path1|path2|...
    int cell = 56, labelW = 170, cols = 6; var bmp = new Bitmap(labelW + cols * cell, rows.Length * cell); 
    using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.FromArgb(40,40,48)); var f = new Font("Segoe UI", 8);
      for (int i = 0; i < rows.Length; i++) { var p = rows[i].Split('|'); g.DrawString(p[0], f, Brushes.White, new RectangleF(2, i * cell + 4, labelW - 4, cell - 4));
        for (int j = 1; j < p.Length && j <= cols; j++) { if (p[j] == "") continue; var im = Img(p[j], 48); if (im != null) g.DrawImage(im, labelW + (j - 1) * cell + 4, i * cell + 4, 48, 48); else g.DrawString("x", f, Brushes.Red, labelW + (j - 1) * cell + 20, i * cell + 20); } } }
    bmp.Save(outPng, ImageFormat.Png); }
}
