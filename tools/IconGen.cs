// Cliport icon generator: a small SDF ray-marcher (real 3D render: soft shadows, AO, specular).
// Usage: IconGen.exe <outDir>  -> icon-1024.png (transparent), icon-ios-1024.png (full bleed), Cliport.ico
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

struct V
{
    public double x, y, z;
    public V(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    public static V operator +(V a, V b) { return new V(a.x + b.x, a.y + b.y, a.z + b.z); }
    public static V operator -(V a, V b) { return new V(a.x - b.x, a.y - b.y, a.z - b.z); }
    public static V operator *(V a, double s) { return new V(a.x * s, a.y * s, a.z * s); }
    public static V operator *(V a, V b) { return new V(a.x * b.x, a.y * b.y, a.z * b.z); }
    public double Dot(V b) { return x * b.x + y * b.y + z * b.z; }
    public double Len() { return Math.Sqrt(x * x + y * y + z * z); }
    public V Norm() { double l = Len(); return new V(x / l, y / l, z / l); }
    public static V Lerp(V a, V b, double t) { return a + (b - a) * t; }
    public static V Hex(int rgb) { return new V(Math.Pow(((rgb >> 16) & 255) / 255.0, 2.2), Math.Pow(((rgb >> 8) & 255) / 255.0, 2.2), Math.Pow((rgb & 255) / 255.0, 2.2)); }
}

sealed class Frame   // rigid transform: local = M^T (p - c)
{
    readonly double[] m = new double[9]; readonly V c;
    public Frame(V c, double ry, double rx, double rz)
    {
        this.c = c;
        double cy = Math.Cos(ry), sy = Math.Sin(ry), cx = Math.Cos(rx), sx = Math.Sin(rx), cz = Math.Cos(rz), sz = Math.Sin(rz);
        double[] Y = { cy, 0, sy, 0, 1, 0, -sy, 0, cy }, X = { 1, 0, 0, 0, cx, -sx, 0, sx, cx }, Z = { cz, -sz, 0, sz, cz, 0, 0, 0, 1 };
        var t = Mul(Mul(Y, X), Z); Array.Copy(t, m, 9);
    }
    static double[] Mul(double[] a, double[] b)
    {
        var r = new double[9];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return r;
    }
    public V ToLocal(V p)
    {
        V d = p - c;
        return new V(m[0] * d.x + m[3] * d.y + m[6] * d.z, m[1] * d.x + m[4] * d.y + m[7] * d.z, m[2] * d.x + m[5] * d.y + m[8] * d.z);
    }
}

static class IconGen
{
    static bool fullBleed;
    const double TileH = 0.12, BH = 0.036;          // tile / board half thickness
    static readonly Frame back = new Frame(new V(-0.17, 0.11, 0.44), -0.33, 0.20, 0.15);
    static readonly Frame front = new Frame(new V(0.16, -0.08, 0.70), -0.33, 0.20, -0.10);

    static double Slab(double px, double py, double pz, double hx, double hy, double r, double hz, double er)
    {
        double qx = Math.Abs(px) - hx + r, qy = Math.Abs(py) - hy + r;
        double d2 = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - r;
        double wx = d2 + er, wy = Math.Abs(pz) - hz + er;
        return Math.Min(Math.Max(wx, wy), 0) + Math.Sqrt(Math.Max(wx, 0) * Math.Max(wx, 0) + Math.Max(wy, 0) * Math.Max(wy, 0)) - er;
    }
    static double Capsule(V p, V a, V b, double r)
    {
        V pa = p - a, ba = b - a; double h = Math.Max(0, Math.Min(1, pa.Dot(ba) / ba.Dot(ba)));
        return (pa - ba * h).Len() - r;
    }
    static double RoundBox(V p, V b, double r)
    {
        double qx = Math.Abs(p.x) - b.x + r, qy = Math.Abs(p.y) - b.y + r, qz = Math.Abs(p.z) - b.z + r;
        double o = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0) + Math.Max(qz, 0) * Math.Max(qz, 0));
        return o + Math.Min(Math.Max(qx, Math.Max(qy, qz)), 0) - r;
    }

    // materials: 0 tile, 1 back board, 2 front board, 3 text line, 4 metal, 5 pin head, 6 accent line, 7 back line
    static double Map(V p, out int mat)
    {
        double d = fullBleed ? Math.Abs(p.z) - TileH : Slab(p.x, p.y, p.z, 1.0, 1.0, 0.44, TileH, 0.05); mat = 0;
        double t;
        V b = back.ToLocal(p);
        if (b.Len() < 1.2)
        {
            t = Slab(b.x, b.y, b.z, 0.42, 0.54, 0.09, BH, 0.016); if (t < d) { d = t; mat = 1; }
            t = Math.Min(Capsule(b, new V(-0.27, 0.02, BH), new V(0.10, 0.02, BH), 0.028), Capsule(b, new V(-0.27, -0.14, BH), new V(-0.02, -0.14, BH), 0.028));
            t = Math.Min(t, Capsule(b, new V(-0.27, -0.30, BH), new V(0.06, -0.30, BH), 0.028));
            if (t < d) { d = t; mat = 7; }
            t = Capsule(b, new V(-0.24, 0.36, BH), new V(-0.24, 0.36, BH + 0.13), 0.013); if (t < d) { d = t; mat = 4; }
            t = (b - new V(-0.24, 0.36, BH + 0.17)).Len() - 0.085; if (t < d) { d = t; mat = 5; }
        }
        else d = Math.Min(d, b.Len() - 0.85);
        V f = front.ToLocal(p);
        if (f.Len() < 1.2)
        {
            t = Slab(f.x, f.y, f.z, 0.42, 0.54, 0.09, BH, 0.016); if (t < d) { d = t; mat = 2; }
            t = Capsule(f, new V(-0.27, 0.20, BH), new V(0.27, 0.20, BH), 0.032); if (t < d) { d = t; mat = 6; }
            t = Math.Min(Capsule(f, new V(-0.27, 0.04, BH), new V(0.27, 0.04, BH), 0.028), Capsule(f, new V(-0.27, -0.12, BH), new V(0.27, -0.12, BH), 0.028));
            t = Math.Min(t, Capsule(f, new V(-0.27, -0.28, BH), new V(0.04, -0.28, BH), 0.028));
            if (t < d) { d = t; mat = 3; }
            // clip: metal body + loop
            t = RoundBox(f - new V(0, 0.50, BH + 0.035), new V(0.17, 0.075, 0.04), 0.035);
            V q = f - new V(0, 0.60, BH + 0.03); double ring = Math.Sqrt(q.x * q.x + q.y * q.y) - 0.062;
            t = Math.Min(t, Math.Sqrt(ring * ring + q.z * q.z) - 0.022);
            if (t < d) { d = t; mat = 4; }
        }
        else d = Math.Min(d, f.Len() - 0.85);
        return d;
    }
    static double Map(V p) { int m; return Map(p, out m); }
    static V Normal(V p)
    {
        const double e = 0.0008;
        return new V(Map(new V(p.x + e, p.y, p.z)) - Map(new V(p.x - e, p.y, p.z)), Map(new V(p.x, p.y + e, p.z)) - Map(new V(p.x, p.y - e, p.z)), Map(new V(p.x, p.y, p.z + e)) - Map(new V(p.x, p.y, p.z - e))).Norm();
    }
    static double Shadow(V p, V l)
    {
        double res = 1, t = 0.02;
        for (int i = 0; i < 60 && t < 3; i++) { double h = Map(p + l * t); if (h < 0.0005) return 0; res = Math.Min(res, 9 * h / t); t += Math.Max(h, 0.012); }
        return Math.Max(0, Math.Min(1, res));
    }
    static double AO(V p, V n)
    {
        double occ = 0, sca = 1;
        for (int i = 1; i <= 5; i++) { double h = 0.035 * i; occ += (h - Map(p + n * h)) * sca; sca *= 0.75; }
        return Math.Max(0, Math.Min(1, 1 - 2.2 * occ));
    }
    static readonly V cA = V.Hex(0x2F8BFF), cB = V.Hex(0x6A4BFF), cC = V.Hex(0xA03CF2), cGlow = V.Hex(0x7FE8FF);
    static V TileColor(V p)
    {
        double t = Math.Max(0, Math.Min(1, (p.x - p.y + 2) / 4));
        V c = t < 0.5 ? V.Lerp(cA, cB, t * 2) : V.Lerp(cB, cC, (t - 0.5) * 2);
        double g = Math.Exp(-((p.x + 0.75) * (p.x + 0.75) + (p.y - 0.8) * (p.y - 0.8)) * 1.1);
        return V.Lerp(c, cGlow, 0.35 * g);
    }
    static bool Trace(double u, double v, double ext, out V col)
    {
        V eye = new V(0, 0, 7.5), dir = (new V(u * ext, v * ext, TileH) - eye).Norm();
        double t = 5.5; int mat = 0; bool hit = false; V p = eye;
        for (int i = 0; i < 220 && t < 9; i++) { p = eye + dir * t; double d = Map(p, out mat); if (d < 0.0004) { hit = true; break; } t += d * 0.9; }
        col = new V(0, 0, 0); if (!hit) return false;
        V n = Normal(p);
        V key = new V(-0.50, 0.62, 0.60).Norm(), fill = new V(0.6, -0.2, 0.75).Norm();
        V alb; double spec = 0.35, pow = 50, refl = 0;
        switch (mat)
        {
            case 0: alb = TileColor(p); spec = 0.18; pow = 30; break;
            case 1: alb = V.Hex(0x1FD6C4); spec = 0.45; pow = 70; break;
            case 2: alb = V.Hex(0xFBFCFF); spec = 0.30; pow = 80; break;
            case 3: alb = V.Hex(0xC3CCEA); break;
            case 4: alb = V.Hex(0xFFC53D); spec = 0.9; pow = 90; refl = 0.35; break;
            case 5: alb = V.Hex(0xFF4D6A); spec = 0.8; pow = 110; break;
            case 6: alb = V.Hex(0x6A4BFF); break;
            default: alb = V.Hex(0xD9FFFA); break;
        }
        double ao = AO(p, n), sh = Shadow(p + n * 0.002, key);
        double dif = Math.Max(0, n.Dot(key)) * sh, dif2 = Math.Max(0, n.Dot(fill));
        V h = (key - dir).Norm(); double sp = Math.Pow(Math.Max(0, n.Dot(h)), pow) * spec * sh;
        double fres = Math.Pow(1 - Math.Max(0, -n.Dot(dir)), 3);
        V c = alb * (0.30 * ao + 0.78 * dif + 0.16 * dif2 * ao);
        if (refl > 0) { V r = dir - n * (2 * dir.Dot(n)); double sky = 0.5 + 0.5 * r.y; c = V.Lerp(c, new V(1, 0.93, 0.75) * (0.35 + 0.9 * sky), refl); }
        c = c + new V(1, 1, 1) * sp + new V(0.75, 0.85, 1) * (0.10 * fres * ao);
        col = c; return true;
    }

    static byte[] Render(int size, int ss, double ext, bool bleed)
    {
        fullBleed = bleed;
        var px = new byte[size * size * 4];
        Parallel.For(0, size, delegate (int y)
        {
            for (int x = 0; x < size; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < ss; sy++) for (int sx = 0; sx < ss; sx++)
                    {
                        double u = ((x + (sx + 0.5) / ss) / size) * 2 - 1, v = 1 - ((y + (sy + 0.5) / ss) / size) * 2;
                        V c; if (Trace(u, v, ext, out c)) { r += c.x; g += c.y; b += c.z; a += 1; }
                    }
                int o = (y * size + x) * 4;
                if (a > 0)
                {
                    px[o + 2] = Tone(r / a); px[o + 1] = Tone(g / a); px[o] = Tone(b / a);
                    px[o + 3] = (byte)Math.Round(255 * a / (ss * ss));
                }
            }
        });
        return px;
    }
    static byte Tone(double lin) { lin = lin / (1 + 0.12 * lin); return (byte)Math.Max(0, Math.Min(255, Math.Round(255 * Math.Pow(Math.Max(0, lin), 1 / 2.2) * 1.04))); }

    static Bitmap ToBitmap(byte[] bgra, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(bgra, 0, bd.Scan0, bgra.Length); bmp.UnlockBits(bd); return bmp;
    }
    static Bitmap Scale(Bitmap src, int size)
    {
        // repeated halving keeps small sizes crisp
        Bitmap cur = src;
        while (cur.Width / 2 >= size && cur.Width / 2 >= 16)
        {
            var half = Resize(cur, Math.Max(size, cur.Width / 2)); if (cur != src) cur.Dispose(); cur = half; if (cur.Width == size) return cur;
        }
        var r = Resize(cur, size); if (cur != src) cur.Dispose(); return r;
    }
    static Bitmap Resize(Bitmap src, int size)
    {
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.CompositingQuality = CompositingQuality.HighQuality;
            using (var ia = new ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia); }
        }
        return dst;
    }
    static byte[] DibEntry(Bitmap b)
    {
        int n = b.Width; var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        var bd = b.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var row = new byte[n * 4];
        for (int y = n - 1; y >= 0; y--) { Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, row.Length); w.Write(row); }
        b.UnlockBits(bd);
        int maskRow = ((n + 31) / 32) * 4; w.Write(new byte[maskRow * n]);
        return ms.ToArray();
    }

    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "."; Directory.CreateDirectory(dir);
        int size = args.Length > 1 ? int.Parse(args[1]) : 1024, ss = args.Length > 2 ? int.Parse(args[2]) : 3;
        using (Bitmap win = ToBitmap(Render(size, ss, 1.05, false), size))
        {
            win.Save(Path.Combine(dir, "icon-1024.png"), ImageFormat.Png);
            var entries = new List<byte[]>(); int[] sizes = { 256, 128, 64, 48, 40, 32, 24, 20, 16 };
            foreach (int s in sizes)
                using (Bitmap b = Scale(win, s))
                {
                    if (s == 256) { var ms = new MemoryStream(); b.Save(ms, ImageFormat.Png); entries.Add(ms.ToArray()); }
                    else entries.Add(DibEntry(b));
                }
            using (var fs = new FileStream(Path.Combine(dir, "Cliport.ico"), FileMode.Create)) using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(entries[i].Length); w.Write(off); off += entries[i].Length;
                }
                foreach (var e in entries) w.Write(e);
            }
        }
        byte[] ios = Render(size, ss, 1.16, true);
        for (int i = 3; i < ios.Length; i += 4) ios[i] = 255;
        using (Bitmap b = ToBitmap(ios, size)) using (var rgb = new Bitmap(size, size, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(rgb)) g.DrawImage(b, 0, 0, size, size);
            rgb.Save(Path.Combine(dir, "icon-ios-1024.png"), ImageFormat.Png);
        }
        Console.WriteLine("icons written to " + Path.GetFullPath(dir));
        return 0;
    }
}
