using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class AppInfo
{
    static byte[] b;
    static List<string> strings = new List<string>();
    static bool v41;

    static string Key(ref int p)
    {
        if (v41) { int idx = BitConverter.ToInt32(b, p); p += 4; return (idx >= 0 && idx < strings.Count) ? strings[idx] : ("#" + idx); }
        return Z(ref p);
    }
    static string Z(ref int p)
    {
        int s = p; while (b[p] != 0) p++; string r = Encoding.UTF8.GetString(b, s, p - s); p++; return r;
    }

    // Dump subtree; only print paths under filter prefix
    static void Kv(ref int p, string path, StringBuilder sb, string filter)
    {
        while (true)
        {
            byte t = b[p++];
            if (t == 0x08) return;
            string k = Key(ref p);
            string np = path.Length == 0 ? k : path + "/" + k;
            bool show = np.StartsWith(filter, StringComparison.OrdinalIgnoreCase) || filter.StartsWith(np, StringComparison.OrdinalIgnoreCase);
            if (t == 0x00) { Kv(ref p, np, sb, filter); }
            else if (t == 0x01) { string v = Z(ref p); if (show && np.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) sb.AppendLine(np + " = \"" + v + "\""); }
            else if (t == 0x02) { int v = BitConverter.ToInt32(b, p); p += 4; if (show && np.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) sb.AppendLine(np + " = " + v); }
            else if (t == 0x07) { ulong v = BitConverter.ToUInt64(b, p); p += 8; if (show && np.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) sb.AppendLine(np + " = " + v); }
            else if (t == 0x03) { p += 4; }
            else if (t == 0x04 || t == 0x06) { p += 4; }
            else if (t == 0x05) { while (b[p] != 0 || b[p + 1] != 0) p += 2; p += 2; }
            else if (t == 0x0A) { p += 8; }
            else throw new Exception("unknown type " + t + " at " + p);
        }
    }

    public static string Dump(string file, uint[] appids, string filter)
    {
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        { b = new byte[fs.Length]; int r = 0; while (r < b.Length) { int n = fs.Read(b, r, b.Length - r); if (n <= 0) break; r += n; } }
        uint magic = BitConverter.ToUInt32(b, 0);
        v41 = magic == 0x07564429;
        int p = 8;
        strings.Clear();
        if (v41)
        {
            long sto = BitConverter.ToInt64(b, 8); p = 16;
            int q = (int)sto; int cnt = BitConverter.ToInt32(b, q); q += 4;
            for (int i = 0; i < cnt; i++) strings.Add(Z(ref q));
        }
        var sb = new StringBuilder();
        sb.AppendLine(string.Format("magic=0x{0:X8} v41={1} strings={2}", magic, v41, strings.Count));
        var want = new HashSet<uint>(appids);
        while (true)
        {
            uint appid = BitConverter.ToUInt32(b, p);
            if (appid == 0) break;
            uint size = BitConverter.ToUInt32(b, p + 4);
            int next = p + 8 + (int)size;
            if (want.Contains(appid))
            {
                int q = p + 8 + 4 + 4 + 8 + 20 + 4 + 20; // infostate,lastupdated,token,sha,change,binsha
                sb.AppendLine("== appid " + appid);
                try { Kv(ref q, "", sb, filter); } catch (Exception e) { sb.AppendLine("ERR " + e.Message); }
            }
            p = next;
        }
        return sb.ToString();
    }
}
