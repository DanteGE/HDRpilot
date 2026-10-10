using System; using System.IO; using System.Text; using System.Collections.Generic;
public static class ChromeCache {
  static byte[] ReadShared(string p) { using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { var ms = new MemoryStream(); fs.CopyTo(ms); return ms.ToArray(); } }
  static Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
  static string dir;
  static byte[] Block(int n) { string k = "data_" + n; if (!files.ContainsKey(k)) { var p = Path.Combine(dir, k); files[k] = File.Exists(p) ? ReadShared(p) : null; } return files[k]; }
  static int BlockSize(int type) { switch (type) { case 1: return 36; case 2: return 256; case 3: return 1024; case 4: return 4096; case 5: return 8; case 6: return 104; case 7: return 48; default: return 0; } }
  public static byte[] ReadAddr(uint addr, int size) {
    if ((addr & 0x80000000) == 0 || size <= 0) return null;
    int type = (int)((addr >> 28) & 7);
    if (type == 0) { var p = Path.Combine(dir, "f_" + (addr & 0x0FFFFFFF).ToString("x6")); if (!File.Exists(p)) return null; var b = ReadShared(p); if (b.Length > size) Array.Resize(ref b, size); return b; }
    int file = (int)((addr >> 16) & 0xFF); int start = (int)(addr & 0xFFFF); int bs = BlockSize(type);
    var data = Block(file); if (data == null) return null; long off = 8192 + (long)start * bs; if (off + size > data.Length) return null;
    var r = new byte[size]; Array.Copy(data, off, r, 0, size); return r; }
  public static List<string> Dump(string cacheDir, string outDir) {
    dir = cacheDir; files.Clear(); var res = new List<string>();
    var d1 = Block(1); int blocks = (d1.Length - 8192) / 256;
    for (int i = 0; i < blocks; i++) {
      int o = 8192 + i * 256;
      int keyLen = BitConverter.ToInt32(d1, o + 32); uint longKey = BitConverter.ToUInt32(d1, o + 36);
      if (keyLen <= 0 || keyLen > 8192) continue;
      string key = null;
      if (longKey == 0) { int max = Math.Min(keyLen, d1.Length - (o + 96)); key = Encoding.ASCII.GetString(d1, o + 96, max); }
      else { var kb = ReadAddr(longKey, keyLen); if (kb != null) key = Encoding.ASCII.GetString(kb); }
      if (key == null || key.IndexOf("http", StringComparison.Ordinal) < 0) continue;
      int bodySize = BitConverter.ToInt32(d1, o + 44); uint bodyAddr = BitConverter.ToUInt32(d1, o + 60);
      string magic = "none"; string saved = "";
      var body = ReadAddr(bodyAddr, bodySize);
      if (body != null && body.Length >= 12) {
        if (body[0] == 0x89 && body[1] == 0x50) magic = "png"; else if (body[0] == 0xFF && body[1] == 0xD8) magic = "jpg";
        else if (Encoding.ASCII.GetString(body, 0, 4) == "RIFF" && Encoding.ASCII.GetString(body, 8, 4) == "WEBP") magic = "webp";
        else if (Encoding.ASCII.GetString(body, 4, 4) == "ftyp") magic = "ftyp:" + Encoding.ASCII.GetString(body, 8, 4);
        else magic = "other";
        if (outDir != null && magic != "other") { saved = Path.Combine(outDir, "e" + i + "." + magic.Replace("ftyp:","").Trim()); File.WriteAllBytes(saved, body); }
      }
      res.Add(i + "\t" + bodySize + "\t" + bodyAddr.ToString("x8") + "\t" + magic + "\t" + key.Replace("\t"," ").Replace("\n"," ").Replace("\0","") + "\t" + saved);
    }
    return res; }
}
