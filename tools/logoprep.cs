using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class LogoPrep
{
    static Bitmap Crop(string path, Rectangle r, int size)
    {
        using (var src = (Bitmap)Image.FromFile(path))
        {
            var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, size, size), r, GraphicsUnit.Pixel);
            }
            return dst;
        }
    }

    /// <summary>Emblema dorado sobre negro -> dorado sobre transparente (alfa = brillo).</summary>
    public static void Emblem(string src, int x, int y, int side, int size, string outPng)
    {
        using (var b = Crop(src, new Rectangle(x, y, side, side), size))
        {
            var d = b.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var px = new int[size * size];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            for (int i = 0; i < px.Length; i++)
            {
                int c = px[i], r = (c >> 16) & 255, g = (c >> 8) & 255, bl = c & 255;
                int m = Math.Max(r, Math.Max(g, bl));
                // el negro del fondo (con algo de ruido jpg) desaparece; el oro queda opaco
                double a = Math.Max(0, Math.Min(1, (m - 18) / 150.0));
                if (a <= 0) { px[i] = 0; continue; }
                // color sin el negro mezclado: se normaliza al brillo del trazo
                double k = 255.0 / Math.Max(1, m) * Math.Min(1, m / 205.0 + .25);
                int R = Clamp(r * k), G = Clamp(g * k), B = Clamp(bl * k);
                px[i] = (int)(a * 255) << 24 | R << 16 | G << 8 | B;
            }
            Marshal.Copy(px, 0, d.Scan0, px.Length);
            b.UnlockBits(d);
            b.Save(outPng, ImageFormat.Png);
        }
    }

    static int Clamp(double v) { return (int)Math.Max(0, Math.Min(255, v)); }

    /// <summary>Icono: recorte cuadrado con esquinas redondeadas transparentes; .ico con entradas PNG.</summary>
    public static void Icon(string src, int x, int y, int side, double radiusFrac, string outIco, string previewPng)
    {
        var sizes = new[] { 256, 128, 64, 48, 40, 32, 24, 20, 16 };
        var pngs = new List<byte[]>();
        using (var master = Crop(src, new Rectangle(x, y, side, side), 1024))
        {
            // mascara redondeada con bordes suaves
            using (var masked = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(masked))
                using (var path = Rounded(new RectangleF(1, 1, 1022, 1022), (float)(1024 * radiusFrac)))
                using (var tb = new TextureBrush(master))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(tb, path);
                }
                masked.Save(previewPng, ImageFormat.Png);
                foreach (var s in sizes)
                    using (var small = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(masked, new Rectangle(0, 0, s, s));
                        }
                        pngs.Add(s >= 256 ? Png(small) : Dib(small));
                    }
            }
        }
        using (var f = new BinaryWriter(File.Create(outIco)))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                f.Write((byte)0); f.Write((byte)0); f.Write((short)1); f.Write((short)32);
                f.Write(pngs[i].Length); f.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) f.Write(p);
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    /// <summary>Entrada .ico clasica: BITMAPINFOHEADER (alto x2) + BGRA de abajo arriba + mascara AND de 1 bit.</summary>
    static byte[] Dib(Bitmap b)
    {
        int w = b.Width, h = b.Height, maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var f = new BinaryWriter(ms))
        {
            f.Write(40); f.Write(w); f.Write(h * 2); f.Write((short)1); f.Write((short)32);
            f.Write(0); f.Write(w * h * 4 + maskStride * h); f.Write(0); f.Write(0); f.Write(0); f.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    var c = b.GetPixel(x, y);
                    f.Write(c.B); f.Write(c.G); f.Write(c.R); f.Write(c.A);
                }
            for (int y = h - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < w; x++) if (b.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                f.Write(row);
            }
            return ms.ToArray();
        }
    }

    static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>
    /// Icono del .exe dibujado desde el emblema (dorado sobre transparente): placa carbon, borde y trazos en oro
    /// luminoso; los trazos se engrosan en los tamaños pequeños para que se lean.
    /// </summary>
    public static void IconFromEmblem(string emblemPng, string outIco, string previewPng)
    {
        var sizes = new[] { 256, 128, 64, 48, 40, 32, 24, 20, 16 };
        var entries = new List<byte[]>();
        float[] alpha;
        int n = 1024;
        using (var em = (Bitmap)Image.FromFile(emblemPng))
        using (var big = new Bitmap(n, n, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(em, new Rectangle(0, 0, n, n));
            }
            alpha = new float[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) alpha[y * n + x] = big.GetPixel(x, y).A / 255f;
        }
        Bitmap preview = null;
        foreach (var s in sizes)
        {
            // grosor extra (en px de la rejilla de 1024) segun el tamaño final
            int grow = s >= 128 ? 2 : s >= 64 ? 5 : s >= 48 ? 8 : s >= 32 ? 12 : s >= 24 ? 16 : 20;
            var a = Dilate(alpha, n, grow);
            var glow = Dilate(alpha, n, grow + 18);
            using (var master = Compose(a, glow, n, s))
            using (var small = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(master, new Rectangle(0, 0, s, s));
                }
                entries.Add(s >= 256 ? Png(small) : Dib(small));
                if (s == 256) preview = (Bitmap)master.Clone();
            }
        }
        preview.Save(previewPng, ImageFormat.Png);
        preview.Dispose();
        using (var f = new BinaryWriter(File.Create(outIco)))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                f.Write((byte)0); f.Write((byte)0); f.Write((short)1); f.Write((short)32);
                f.Write(entries[i].Length); f.Write(offset);
                offset += entries[i].Length;
            }
            foreach (var e in entries) f.Write(e);
        }
    }

    /// <summary>Placa + emblema en 1024 px: oro en degradado (claro arriba) con alfa realzada y resplandor tenue.</summary>
    static Bitmap Compose(float[] a, float[] glow, int n, int finalSize)
    {
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        float inset = .07f * n, border = finalSize <= 24 ? 64 : finalSize <= 48 ? 44 : 30;
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var plate = Rounded(new RectangleF(4, 4, n - 8, n - 8), n * .2f))
            {
                using (var b = new LinearGradientBrush(new PointF(0, 0), new PointF(0, n), Color.FromArgb(44, 42, 40), Color.FromArgb(10, 10, 12)))
                    g.FillPath(b, plate);
                using (var b = new LinearGradientBrush(new PointF(0, 0), new PointF(0, n), Color.FromArgb(255, 232, 160), Color.FromArgb(196, 144, 58)))
                using (var p = new Pen(b, border)) g.DrawPath(p, plate);
            }
        }
        // emblema reducido al interior de la placa
        float scale = (n - 2 * inset - 2 * border) / n, off = (n - n * scale) / 2;
        var d = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new int[n * n];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        for (int y = 0; y < n; y++)
        {
            float t = y / (float)n;
            // oro: claro arriba, viejo abajo
            float R = 255, G = 238 - 70 * t, B = 168 - 100 * t;
            for (int x = 0; x < n; x++)
            {
                int sx = (int)((x - off) / scale), sy = (int)((y - off) / scale);
                if (sx < 0 || sy < 0 || sx >= n || sy >= n) continue;
                float aa = (float)Math.Pow(a[sy * n + sx], .6);
                float gl = glow[sy * n + sx] * .22f;
                if (aa <= 0 && gl <= 0) continue;
                int c = px[y * n + x];
                if (((c >> 24) & 255) == 0) continue;   // fuera de la placa
                float br = (c >> 16) & 255, bg = (c >> 8) & 255, bb = c & 255;
                // resplandor y luego trazo
                br += (R - br) * gl; bg += (G - bg) * gl; bb += (B - bb) * gl;
                br += (R - br) * aa; bg += (G - bg) * aa; bb += (B - bb) * aa;
                px[y * n + x] = (c & unchecked((int)0xFF000000)) | Clamp(br) << 16 | Clamp(bg) << 8 | Clamp(bb);
            }
        }
        Marshal.Copy(px, 0, d.Scan0, px.Length);
        bmp.UnlockBits(d);
        return bmp;
    }

    /// <summary>Engrosado: maximo en un cuadrado de radio r (separable).</summary>
    static float[] Dilate(float[] src, int n, int r)
    {
        if (r <= 0) return src;
        var tmp = new float[n * n];
        var dst = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float m = 0;
                for (int k = Math.Max(0, x - r); k <= Math.Min(n - 1, x + r); k++) if (src[y * n + k] > m) m = src[y * n + k];
                tmp[y * n + x] = m;
            }
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            {
                float m = 0;
                for (int k = Math.Max(0, y - r); k <= Math.Min(n - 1, y + r); k++) if (tmp[k * n + x] > m) m = tmp[k * n + x];
                dst[y * n + x] = m;
            }
        return dst;
    }
}
