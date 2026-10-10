using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class PeScan
{
    class Sec { public uint VA, VSize, Raw, RawSize; public string Name; }

    static uint Rva2Off(List<Sec> secs, uint rva)
    {
        foreach (var s in secs)
        {
            uint size = Math.Max(s.VSize, s.RawSize);
            if (rva >= s.VA && rva < s.VA + size) return rva - s.VA + s.Raw;
        }
        return 0xFFFFFFFF;
    }

    static string ReadAsciiZ(byte[] b, uint off)
    {
        if (off >= b.Length) return "";
        int end = (int)off;
        while (end < b.Length && b[end] != 0 && end - off < 260) end++;
        return Encoding.ASCII.GetString(b, (int)off, end - (int)off);
    }

    public static string Scan(string path)
    {
        var sb = new StringBuilder();
        byte[] b;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            b = new byte[fs.Length];
            int read = 0;
            while (read < b.Length) { int n = fs.Read(b, read, b.Length - read); if (n <= 0) break; read += n; }
        }
        sb.AppendFormat("size={0:N0}", b.Length);
        if (b.Length < 0x100 || b[0] != 'M' || b[1] != 'Z') { sb.Append(" notPE"); return sb.ToString(); }
        int peOff = BitConverter.ToInt32(b, 0x3C);
        ushort machine = BitConverter.ToUInt16(b, peOff + 4);
        ushort nsec = BitConverter.ToUInt16(b, peOff + 6);
        ushort optSize = BitConverter.ToUInt16(b, peOff + 20);
        int opt = peOff + 24;
        ushort magic = BitConverter.ToUInt16(b, opt);
        bool pe32plus = magic == 0x20B;
        int ddOff = opt + (pe32plus ? 112 : 96);
        sb.AppendFormat(" arch={0}", pe32plus ? "x64" : "x86");
        var secs = new List<Sec>();
        int secOff = opt + optSize;
        var secNames = new List<string>();
        for (int i = 0; i < nsec; i++)
        {
            int o = secOff + i * 40;
            var s = new Sec();
            s.Name = Encoding.ASCII.GetString(b, o, 8).TrimEnd('\0');
            s.VSize = BitConverter.ToUInt32(b, o + 8);
            s.VA = BitConverter.ToUInt32(b, o + 12);
            s.RawSize = BitConverter.ToUInt32(b, o + 16);
            s.Raw = BitConverter.ToUInt32(b, o + 20);
            secs.Add(s); secNames.Add(s.Name);
        }
        sb.AppendFormat(" sections=[{0}]", string.Join(",", secNames.ToArray()));

        // Export directory (index 0)
        uint expRva = BitConverter.ToUInt32(b, ddOff + 0 * 8);
        var exports = new List<string>();
        if (expRva != 0)
        {
            uint eo = Rva2Off(secs, expRva);
            if (eo != 0xFFFFFFFF && eo + 40 <= b.Length)
            {
                uint nNames = BitConverter.ToUInt32(b, (int)eo + 24);
                uint namesRva = BitConverter.ToUInt32(b, (int)eo + 32);
                uint no = Rva2Off(secs, namesRva);
                for (uint i = 0; i < nNames && i < 5000 && no != 0xFFFFFFFF; i++)
                {
                    uint nr = BitConverter.ToUInt32(b, (int)(no + i * 4));
                    string n = ReadAsciiZ(b, Rva2Off(secs, nr));
                    if (n.StartsWith("D3D12SDK") || n.StartsWith("NvOptimus") || n.StartsWith("AmdPower") || n.IndexOf("Agility") >= 0)
                        exports.Add(n);
                }
                sb.AppendFormat(" exportsTotal={0}", nNames);
            }
        }
        sb.AppendFormat(" interestingExports=[{0}]", string.Join(",", exports.ToArray()));

        // Import directory (index 1)
        var imports = new List<string>();
        uint impRva = BitConverter.ToUInt32(b, ddOff + 1 * 8);
        if (impRva != 0)
        {
            uint io = Rva2Off(secs, impRva);
            for (int i = 0; io != 0xFFFFFFFF && i < 512; i++)
            {
                int d = (int)io + i * 20;
                if (d + 20 > b.Length) break;
                uint nameRva = BitConverter.ToUInt32(b, d + 12);
                uint ft = BitConverter.ToUInt32(b, d + 16);
                if (nameRva == 0 && ft == 0) break;
                imports.Add(ReadAsciiZ(b, Rva2Off(secs, nameRva)).ToLowerInvariant());
            }
        }
        // Delay import directory (index 13)
        var delay = new List<string>();
        uint dlRva = BitConverter.ToUInt32(b, ddOff + 13 * 8);
        if (dlRva != 0)
        {
            uint dlo = Rva2Off(secs, dlRva);
            for (int i = 0; dlo != 0xFFFFFFFF && i < 512; i++)
            {
                int d = (int)dlo + i * 32;
                if (d + 32 > b.Length) break;
                uint attrs = BitConverter.ToUInt32(b, d);
                uint nameRva = BitConverter.ToUInt32(b, d + 4);
                if (nameRva == 0) break;
                delay.Add(ReadAsciiZ(b, Rva2Off(secs, nameRva)).ToLowerInvariant());
            }
        }
        string[] gfx = { "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d10_1.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll", "vulkan-1.dll", "opengl32.dll", "ddraw.dll", "unityplayer.dll", "gameassembly.dll" };
        var gi = new List<string>(); var gd = new List<string>();
        foreach (var x in imports) if (Array.IndexOf(gfx, x) >= 0) gi.Add(x);
        foreach (var x in delay) if (Array.IndexOf(gfx, x) >= 0) gd.Add(x);
        sb.AppendFormat(" importsTotal={0} gfxImports=[{1}] delayTotal={2} gfxDelay=[{3}]", imports.Count, string.Join(",", gi.ToArray()), delay.Count, string.Join(",", gd.ToArray()));

        // String scan (ASCII case-insensitive and UTF-16LE) for DLL names in binary (LoadLibrary literals)
        var found = new List<string>();
        string lower = Encoding.ASCII.GetString(b).ToLowerInvariant();
        foreach (var x in gfx)
        {
            int cA = 0, idx = 0;
            while ((idx = lower.IndexOf(x, idx, StringComparison.Ordinal)) >= 0) { cA++; idx += x.Length; }
            byte[] w = Encoding.Unicode.GetBytes(x);
            int cW = CountUtf16(b, x);
            if (cA + cW > 0) found.Add(x + ":" + cA + "/" + cW);
        }
        sb.AppendFormat(" strings=[{0}]", string.Join(",", found.ToArray()));
        return sb.ToString();
    }

    static int CountUtf16(byte[] b, string s)
    {
        // case-insensitive ASCII-in-UTF16 search
        string ls = s.ToLowerInvariant();
        int count = 0; int n = ls.Length;
        for (int i = 0; i + 2 * n <= b.Length; i++)
        {
            bool ok = true;
            for (int k = 0; k < n; k++)
            {
                byte c = b[i + 2 * k]; byte hi = b[i + 2 * k + 1];
                if (hi != 0) { ok = false; break; }
                char ch = (char)c; if (ch >= 'A' && ch <= 'Z') ch = (char)(ch + 32);
                if (ch != ls[k]) { ok = false; break; }
            }
            if (ok) { count++; i += 2 * n - 1; }
        }
        return count;
    }
}
