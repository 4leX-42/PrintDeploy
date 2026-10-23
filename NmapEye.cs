using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Ojo del boton Escaner, calcado del logo de Nmap en tono sobre tono (trazo del color del texto del boton): parpados gruesos rayados, comisuras
    /// enroscadas, esclerotica clara, iris esferico con reticula (meridianos y paralelos), pupila con mira y brillo.
    /// Se dibuja en vectorial a 6x sobre una rejilla de 1000 x 560 y se reduce; cache por tamaño, mirada y giro.
    /// </summary>
    static class NmapEye
    {
        const int SS = 6, LookSteps = 12, SpinSteps = 6;
        const float RefW = 1000, RefH = 560;
        static readonly Dictionary<(int, int, int, int, int, int), Bitmap> cache = new Dictionary<(int, int, int, int, int, int), Bitmap>();

        /// <summary>
        /// Ojo en r con los colores del boton: trazo 'fg' (el del texto) y rellenos que son 'bg' apenas teñido de 'fg',
        /// para que se integre con la cara del boton. look: -1 (izquierda) .. 1 (derecha); spin: giro del globo.
        /// </summary>
        public static void Draw(Graphics g, Rectangle r, float look, double spin, Color fg, Color bg)
        {
            int li = (int)Math.Round((Math.Max(-1, Math.Min(1, look)) + 1) / 2 * LookSteps);
            // los meridianos van cada 15 grados: el giro se repite cada 15 grados
            double period = Math.PI / 12;
            int si = (int)Math.Round(((spin % period) + period) % period / period * SpinSteps) % SpinSteps;
            var key = (r.Width, r.Height, li, si, fg.ToArgb(), bg.ToArgb());
            if (!cache.TryGetValue(key, out var bmp))
                cache[key] = bmp = Render(r.Width, r.Height, li / (float)LookSteps * 2 - 1, si / (double)SpinSteps * period, fg, bg);
            g.DrawImageUnscaled(bmp, r.X, r.Y);
        }

        static Bitmap Render(int w, int h, float look, double spin, Color fg, Color bg)
        {
            using (var big = new Bitmap(w * SS, h * SS, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(big))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float k = Math.Min(w * SS / RefW, h * SS / RefH);
                    g.TranslateTransform((w * SS - RefW * k) / 2, (h * SS - RefH * k) / 2);
                    g.ScaleTransform(k, k);
                    Paint(g, look, spin, fg, bg);
                }
                var small = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(big, new Rectangle(0, 0, w, h));
                }
                return small;
            }
        }

        // curvas del ojo (rejilla 1000 x 560): exterior e interior de cada parpado
        static readonly PointF[] OuterTop = { new PointF(15, 300), new PointF(180, -40), new PointF(820, -40), new PointF(985, 300) };
        static readonly PointF[] InnerTop = { new PointF(85, 330), new PointF(250, 70), new PointF(750, 70), new PointF(915, 330) };
        static readonly PointF[] InnerBottom = { new PointF(85, 330), new PointF(260, 545), new PointF(740, 545), new PointF(915, 330) };
        static readonly PointF[] OuterBottom = { new PointF(15, 300), new PointF(240, 605), new PointF(760, 605), new PointF(985, 300) };

        static PointF Bez(PointF[] c, float t)
        {
            float u = 1 - t;
            return new PointF(u * u * u * c[0].X + 3 * u * u * t * c[1].X + 3 * u * t * t * c[2].X + t * t * t * c[3].X,
                              u * u * u * c[0].Y + 3 * u * u * t * c[1].Y + 3 * u * t * t * c[2].Y + t * t * t * c[3].Y);
        }

        static PointF[] Lerp(PointF[] a, PointF[] b, float t)
        {
            var r = new PointF[4];
            for (int i = 0; i < 4; i++) r[i] = new PointF(a[i].X + (b[i].X - a[i].X) * t, a[i].Y + (b[i].Y - a[i].Y) * t);
            return r;
        }

        static GraphicsPath Band(PointF[] outer, PointF[] inner)
        {
            var p = new GraphicsPath();
            p.AddBezier(outer[0], outer[1], outer[2], outer[3]);
            p.AddLine(outer[3], inner[3]);
            p.AddBezier(inner[3], inner[2], inner[1], inner[0]);
            p.CloseFigure();
            return p;
        }

        static void Paint(Graphics g, float look, double spin, Color fg, Color bg)
        {
            // tono sobre tono: trazo del color del texto; rellenos = fondo con un toque de ese color
            var ink = fg;
            var lidLight = Theme.Mix(bg, fg, .24f);
            var lidDark = Theme.Mix(bg, fg, .07f);
            var scleraC = Theme.Mix(bg, fg, .17f);
            var scleraE = Theme.Mix(bg, fg, .04f);
            var irisLight = Theme.Mix(bg, fg, .4f);
            var irisDark = Theme.Mix(bg, fg, .12f);
            var mesh = fg;
            var pupil = Theme.Mix(bg, Color.Black, .5f);
            float line = 8;

            // ojo (apertura): esclerotica clara con sombra hacia los bordes
            using (var open = new GraphicsPath())
            {
                open.AddBezier(InnerTop[0], InnerTop[1], InnerTop[2], InnerTop[3]);
                open.AddBezier(InnerBottom[3], InnerBottom[2], InnerBottom[1], InnerBottom[0]);
                open.CloseFigure();
                using (var b = new PathGradientBrush(open) { CenterPoint = new PointF(500, 300), CenterColor = scleraC, SurroundColors = new[] { scleraE } })
                    g.FillPath(b, open);

                var clip = g.Clip;
                g.SetClip(open, CombineMode.Intersect);
                // iris: esfera con reticula; sigue la mirada
                float cx = 500 + look * 230, cy = 300, ir = 205;
                using (var ip = new GraphicsPath())
                {
                    ip.AddEllipse(cx - ir, cy - ir, ir * 2, ir * 2);
                    using (var b = new PathGradientBrush(ip) { CenterPoint = new PointF(cx - 40, cy - 50), CenterColor = irisLight, SurroundColors = new[] { irisDark } })
                        g.FillPath(b, ip);
                    var ic = g.Clip;
                    g.SetClip(ip, CombineMode.Intersect);
                    using (var p = new Pen(Color.FromArgb(85, mesh), 3f))
                    {
                        // paralelos
                        for (int lat = -80; lat <= 80; lat += 10)
                        {
                            float y = cy + ir * (float)Math.Sin(lat * Math.PI / 180), half = ir * (float)Math.Cos(lat * Math.PI / 180);
                            g.DrawLine(p, cx - half, y, cx + half, y);
                        }
                        // meridianos: giran con el faro
                        for (int m = 0; m < 12; m++)
                        {
                            double ph = spin + m * Math.PI / 12;
                            float mw = ir * (float)Math.Abs(Math.Cos(ph));
                            if (mw > 1) g.DrawEllipse(p, cx - mw, cy - ir, mw * 2, ir * 2);
                        }
                    }
                    g.Clip = ic;
                    ic.Dispose();
                    using (var p = new Pen(ink, line)) g.DrawEllipse(p, cx - ir, cy - ir, ir * 2, ir * 2);
                }
                // pupila: disco oscuro con mira (anillo claro + cruz)
                float pr = 92;
                using (var pp = new GraphicsPath())
                {
                    pp.AddEllipse(cx - pr, cy - pr, pr * 2, pr * 2);
                    using (var b = new PathGradientBrush(pp) { CenterPoint = new PointF(cx - 20, cy - 25), CenterColor = Theme.Mix(pupil, fg, .12f), SurroundColors = new[] { pupil } })
                        g.FillPath(b, pp);
                }
                using (var p = new Pen(fg, 11))
                {
                    g.DrawEllipse(p, cx - 50, cy - 50, 100, 100);
                    g.DrawLine(p, cx - 78, cy, cx - 22, cy); g.DrawLine(p, cx + 22, cy, cx + 78, cy);
                    g.DrawLine(p, cx, cy - 78, cx, cy - 22); g.DrawLine(p, cx, cy + 22, cx, cy + 78);
                }
                using (var b = new SolidBrush(fg)) g.FillEllipse(b, cx - 10, cy - 10, 20, 20);
                // brillo arriba a la derecha
                using (var b = new SolidBrush(Color.FromArgb(140, Theme.Mix(fg, Color.White, .5f)))) g.FillEllipse(b, cx + 74, cy - 146, 56, 36);
                g.Clip = clip;
                clip.Dispose();
                // sombra del parpado superior sobre el ojo
                using (var p = new Pen(Color.FromArgb(90, 60, 40, 10), 26)) g.DrawBezier(p, InnerTop[0], InnerTop[1], InnerTop[2], InnerTop[3]);
            }

            // parpados: banda con degradado, rayado transversal y dos lineas longitudinales
            foreach (var (outer, inner, n) in new[] { (OuterTop, InnerTop, 46), (OuterBottom, InnerBottom, 38) })
            {
                using (var band = Band(outer, inner))
                {
                    using (var b = new LinearGradientBrush(new PointF(0, outer == OuterTop ? 0 : 560), new PointF(0, 300), lidLight, lidDark) { WrapMode = WrapMode.TileFlipXY })
                        g.FillPath(b, band);
                    using (var p = new Pen(Color.FromArgb(130, ink), 3.2f))
                    {
                        for (int i = 1; i < n; i++)
                        {
                            float t = i / (float)n;
                            g.DrawLine(p, Bez(outer, t), Bez(inner, t));
                        }
                        foreach (var f in new[] { .34f, .67f })
                        {
                            var mid = Lerp(outer, inner, f);
                            g.DrawBezier(p, mid[0], mid[1], mid[2], mid[3]);
                        }
                    }
                    using (var p = new Pen(ink, line) { LineJoin = LineJoin.Round }) g.DrawPath(p, band);
                }
            }
            // comisuras enroscadas
            foreach (var (x, dir) in new[] { (60f, 1f), (940f, -1f) })
            {
                using (var b = new SolidBrush(lidLight)) g.FillEllipse(b, x - 32, 300, 64, 52);
                using (var p = new Pen(ink, 7)) { g.DrawEllipse(p, x - 32, 300, 64, 52); g.DrawArc(p, x - 16 + dir * 4, 312, 32, 26, dir > 0 ? 180 : 0, 270); }
            }
        }
    }
}
