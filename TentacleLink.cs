using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Tentaculo cyberpunk (metal negro, vertebras y nucleo de energia dorados) que une la ventana principal con el escaner cuando estan separados. Ventana por capas
    /// (UpdateLayeredWindow, alfa por pixel) que no recibe clics ni foco. Solo anima mientras se ve.
    /// </summary>
    sealed class TentacleLink : Form
    {
        const int Samples = 44;
        readonly Form root, tip;
        readonly Timer timer = new Timer { Interval = 33 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly PointF[] pts = new PointF[Samples], nrm = new PointF[Samples];
        readonly float[] rad = new float[Samples];
        double shownAt;
        bool linked;
        IntPtr memDc, dib, oldObj;
        Bitmap canvas;
        Size canvasSize;
        PointF a, b;           // anclajes en el borde de cada ventana
        Rectangle box;         // zona de dibujo en pantalla

        public TentacleLink(Form root, Form tip)
        {
            this.root = root; this.tip = tip;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Owner = root;
            timer.Tick += (s, e) => Frame();
            foreach (var f in new[] { root, tip })
            {
                f.Move += (s, e) => Sync();
                f.Resize += (s, e) => Sync();
                f.VisibleChanged += (s, e) => Sync();
            }
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;   // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        // ---------- geometria ----------
        void Sync()
        {
            if (IsDisposed) return;
            bool show = root.Visible && tip.Visible && root.WindowState != FormWindowState.Minimized && Anchors();
            if (!show)
            {
                linked = false;
                timer.Stop();
                if (Visible) Hide();
                return;
            }
            if (!linked) { linked = true; shownAt = clock.Elapsed.TotalSeconds; }
            if (!Visible) Show();
            Frame();
            timer.Start();
        }

        /// <summary>Puntos de union en los bordes enfrentados; false si se solapan, estan pegadas o demasiado lejos.</summary>
        bool Anchors()
        {
            Rectangle r = root.Bounds, t = tip.Bounds;
            if (Rectangle.Inflate(r, Theme.S(20), Theme.S(20)).IntersectsWith(t)) return false;
            var cr = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            var ct = new PointF(t.X + t.Width / 2f, t.Y + t.Height / 2f);
            a = Edge(r, cr, ct.X - cr.X, ct.Y - cr.Y, Theme.S(8));
            b = Edge(t, ct, cr.X - ct.X, cr.Y - ct.Y, -Theme.S(13));   // fuera: el conector de la punta llega justo al borde
            float len = Dist(a, b);
            if (len < Theme.S(28) || len > Theme.S(1100)) return false;
            int m = Theme.S(70);
            box = Rectangle.FromLTRB((int)Math.Min(a.X, b.X) - m, (int)Math.Min(a.Y, b.Y) - m,
                (int)Math.Max(a.X, b.X) + m, (int)Math.Max(a.Y, b.Y) + m + (int)(len * .14f) + Theme.S(20));
            return true;
        }

        /// <summary>Punto del borde de r en la direccion (dx, dy) desde su centro, metido 'inset' hacia dentro.</summary>
        static PointF Edge(Rectangle r, PointF c, float dx, float dy, float inset)
        {
            float l = (float)Math.Sqrt(dx * dx + dy * dy);
            if (l < 1) return c;
            dx /= l; dy /= l;
            float k = Math.Min(Math.Abs(dx) > 1e-4 ? r.Width / 2f / Math.Abs(dx) : float.MaxValue,
                               Math.Abs(dy) > 1e-4 ? r.Height / 2f / Math.Abs(dy) : float.MaxValue) - inset;
            return new PointF(c.X + dx * k, c.Y + dy * k);
        }

        static float Dist(PointF p, PointF q) => (float)Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));

        // ---------- dibujo ----------
        void Frame()
        {
            if (!linked || IsDisposed || box.Width <= 0) return;
            timer.Interval = Form.ActiveForm == null ? 66 : 33;   // app en segundo plano: mitad de fotogramas
            Canvas(box.Size);
            double now = clock.Elapsed.TotalSeconds;
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-box.X, -box.Y);
                Render(g, (float)now, (float)Math.Min(1, (now - shownAt) / .45));
            }
            var dst = new POINT { X = box.X, Y = box.Y };
            var size = new SIZE { cx = box.Width, cy = box.Height };
            var src = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }

        /// <summary>Para capturas: dibuja el tentaculo de un instante dado sobre g (coordenadas de pantalla).</summary>
        internal bool RenderAt(Graphics g, float t)
        {
            if (!Anchors()) return false;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Render(g, t, 1);
            return true;
        }

        void Render(Graphics g, float t, float grow)
        {
            float e = 1 - (1 - grow) * (1 - grow) * (1 - grow);   // sale de la ventana principal y se estira
            var end = new PointF(a.X + (b.X - a.X) * e, a.Y + (b.Y - a.Y) * e);
            float len = Dist(a, end);
            if (len < 2) return;
            float ux = (end.X - a.X) / len, uy = (end.Y - a.Y) / len, nx = -uy, ny = ux;
            // movimiento lento: caida por su peso que respira + onda suave que recorre el cuerpo
            float sag = len * (.1f + .015f * (float)Math.Sin(t * .9)), amp = Math.Min(Theme.S(9), len * .045f);
            float r0 = Theme.S(11), r1 = Theme.S(4);

            var p1 = new PointF(a.X + ux * len * .3f, a.Y + uy * len * .3f + sag);
            var p2 = new PointF(a.X + ux * len * .7f, a.Y + uy * len * .7f + sag * .8f);
            for (int i = 0; i < Samples; i++)
            {
                float s = i / (float)(Samples - 1), q = 1 - s;
                float x = q * q * q * a.X + 3 * q * q * s * p1.X + 3 * q * s * s * p2.X + s * s * s * end.X;
                float y = q * q * q * a.Y + 3 * q * q * s * p1.Y + 3 * q * s * s * p2.Y + s * s * s * end.Y;
                float wave = amp * (float)(Math.Sin(2 * Math.PI * (.9 * s - .22 * t)) * Math.Sin(Math.PI * s));
                pts[i] = new PointF(x + nx * wave, y + ny * wave);
                rad[i] = (r1 + (r0 - r1) * (float)Math.Pow(1 - s, .8)) * (.6f + .4f * e);
            }
            for (int i = 0; i < Samples; i++)
            {
                var p = pts[Math.Max(0, i - 1)]; var n = pts[Math.Min(Samples - 1, i + 1)];
                float dx = n.X - p.X, dy = n.Y - p.Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-3f) l = 1;
                nrm[i] = new PointF(-dy / l, dx / l);
            }
            // normales hacia la luz (arriba a la izquierda), con el mismo sentido en todo el cuerpo
            float lit = 0;
            foreach (var n in nrm) lit += n.X + n.Y;
            if (lit > 0) for (int i = 0; i < Samples; i++) nrm[i] = new PointF(-nrm[i].X, -nrm[i].Y);

            // sombra difusa
            for (int k = 0; k < 4; k++)
                using (var br = new SolidBrush(Color.FromArgb(13, 0, 0, 0))) g.FillPolygon(br, Body(1f + k * .18f, 0, Theme.S(5) + k * Theme.S(2)));
            // anclaje en la ventana principal: aro dorado con luz
            Socket(g, a, r0 * 1.45f, t);

            // funda: metal negro (rampa coseno = cilindro) con brillo especular duro
            const int layers = 14;
            for (int k = 0; k < layers; k++)
            {
                float p = k / (float)(layers - 1), f = 1 - p * .86f;
                float tone = (float)(1 - Math.Cos(p * Math.PI / 2));
                var c = Theme.Mix(Color.FromArgb(8, 8, 10), Color.FromArgb(74, 74, 80), tone);
                using (var br = new SolidBrush(c)) g.FillPolygon(br, Body(f, (1 - f) * .34f, 0));
            }
            using (var br = new SolidBrush(Color.FromArgb(140, 255, 255, 255))) g.FillPolygon(br, Body(.06f, .52f, 0));

            // nucleo de energia: ranura dorada a lo largo del eje, con pulsos de datos hacia el escaner
            using (var br = new SolidBrush(Color.FromArgb(55, Theme.Gold))) g.FillPolygon(br, Body(.34f, -.06f, 0));
            using (var br = new SolidBrush(Color.FromArgb(120, Theme.Gold))) g.FillPolygon(br, Body(.14f, -.06f, 0));
            using (var br = new SolidBrush(Color.FromArgb(235, 255, 232, 180))) g.FillPolygon(br, Body(.045f, -.06f, 0));
            float[] pulses = new float[3];
            for (int j = 0; j < 3; j++) pulses[j] = (float)((t * .32 + j / 3.0) % 1.0);
            foreach (var s in pulses)
            {
                int i = (int)(s * (Samples - 1));
                float fade = (float)Math.Sin(Math.PI * s), rr = rad[i] * .9f;
                var c = new PointF(pts[i].X - nrm[i].X * rad[i] * .08f, pts[i].Y - nrm[i].Y * rad[i] * .08f);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(c.X - rr, c.Y - rr, rr * 2, rr * 2);
                    using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(200 * fade), 255, 236, 190), SurroundColors = new[] { Color.FromArgb(0, Theme.Gold) } })
                        g.FillPath(br, path);
                }
            }

            // vertebras: junta oscura + aro dorado biselado cada pocos puntos; se encienden al pasar un pulso
            for (int i = 4; i < Samples - 4; i += 5)
            {
                float s = i / (float)(Samples - 1), heat = 0;
                foreach (var ps in pulses) heat = Math.Max(heat, 1 - Math.Min(1, Math.Abs(ps - s) * 14));
                var n = nrm[i]; float r = rad[i] * 1.04f;
                var tg = new PointF(-n.Y, n.X);   // tangente
                float wdt = Math.Max(1f, rad[i] * .12f);
                var top = new PointF(pts[i].X + n.X * r, pts[i].Y + n.Y * r);
                var bot = new PointF(pts[i].X - n.X * r, pts[i].Y - n.Y * r);
                using (var p = new Pen(Color.FromArgb(150, 4, 4, 6), wdt * 1.2f)) g.DrawLine(p, Off(top, tg, wdt * 1.1f), Off(bot, tg, wdt * 1.1f));
                var ring = Theme.Mix(Theme.Mix(Theme.GoldDeep, Color.Black, .25f), Theme.Gold, .25f + .75f * heat);
                using (var br = new LinearGradientBrush(top, bot, Theme.Mix(ring, Color.White, .45f), Theme.Mix(ring, Color.Black, .55f)) { WrapMode = WrapMode.TileFlipXY })
                using (var p = new Pen(br, wdt)) g.DrawLine(p, top, bot);
            }

            // punta: conector dorado que se enchufa al escaner
            if (e > .98f) Plug(g, pts[Samples - 1], nrm[Samples - 1], rad[Samples - 1], t);
        }

        static PointF Off(PointF p, PointF d, float k) => new PointF(p.X + d.X * k, p.Y + d.Y * k);

        /// <summary>Aro de anclaje: disco negro con anillo dorado y luz interior que late despacio.</summary>
        static void Socket(Graphics g, PointF c, float r, float t)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
                using (var br = new PathGradientBrush(path) { CenterPoint = new PointF(c.X - r * .3f, c.Y - r * .3f), CenterColor = Color.FromArgb(70, 70, 76), SurroundColors = new[] { Color.FromArgb(8, 8, 10) } })
                    g.FillPath(br, path);
            }
            using (var p = new Pen(Theme.Gold, Math.Max(1.5f, r * .12f))) g.DrawEllipse(p, c.X - r * .78f, c.Y - r * .78f, r * 1.56f, r * 1.56f);
            int glow = (int)(90 + 60 * Math.Sin(t * 2.2));
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - r * .5f, c.Y - r * .5f, r, r);
                using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb(glow, 255, 230, 170), SurroundColors = new[] { Color.FromArgb(0, Theme.Gold) } })
                    g.FillPath(br, path);
            }
        }

        /// <summary>Conector de la punta: cuerpo dorado con estrias y un led que parpadea al recibir cada pulso.</summary>
        void Plug(Graphics g, PointF c, PointF n, float r, float t)
        {
            var tg = new PointF(-n.Y, n.X);
            // la tangente apunta hacia el escaner
            var prev = pts[Samples - 4];
            if ((c.X - prev.X) * tg.X + (c.Y - prev.Y) * tg.Y < 0) tg = new PointF(-tg.X, -tg.Y);
            float hw = r * 1.25f, l = r * 2.6f;
            PointF P(float along, float side) => new PointF(c.X + tg.X * along + n.X * side, c.Y + tg.Y * along + n.Y * side);
            var body = new[] { P(-l * .2f, hw), P(l * .7f, hw), P(l, hw * .55f), P(l, -hw * .55f), P(l * .7f, -hw), P(-l * .2f, -hw) };
            using (var br = new LinearGradientBrush(P(0, hw), P(0, -hw), Theme.Mix(Theme.Gold, Color.White, .35f), Theme.GoldDeep) { WrapMode = WrapMode.TileFlipXY })
                g.FillPolygon(br, body);
            using (var p = new Pen(Color.FromArgb(10, 10, 12), Math.Max(1f, r * .14f)) { LineJoin = LineJoin.Miter }) g.DrawPolygon(p, body);
            using (var p = new Pen(Color.FromArgb(150, 20, 16, 8), Math.Max(1f, r * .1f)))
                for (int k = 0; k < 3; k++) { float al = l * (.05f + k * .18f); g.DrawLine(p, P(al, hw * .8f), P(al, -hw * .8f)); }
            float led = (float)Math.Pow(Math.Max(0, Math.Sin(t * .32 * Math.PI * 6)), 4);
            var lc = P(l * .82f, 0);
            using (var br = new SolidBrush(Color.FromArgb((int)(120 + 135 * led), 255, 244, 214))) g.FillEllipse(br, lc.X - r * .22f, lc.Y - r * .22f, r * .44f, r * .44f);
        }

        /// <summary>Contorno del cuerpo con radio escalado f, desplazado 'lift' radios hacia la luz y 'down' px hacia abajo.</summary>
        PointF[] Body(float f, float lift, float down)
        {
            var poly = new PointF[Samples * 2];
            for (int i = 0; i < Samples; i++)
            {
                float r = rad[i] * f, o = rad[i] * lift;
                var c = new PointF(pts[i].X + nrm[i].X * o, pts[i].Y + nrm[i].Y * o + down);
                poly[i] = new PointF(c.X + nrm[i].X * r, c.Y + nrm[i].Y * r);
                poly[Samples * 2 - 1 - i] = new PointF(c.X - nrm[i].X * r, c.Y - nrm[i].Y * r);
            }
            return poly;
        }

        // ---------- superficie: DIB premultiplicado reutilizado entre fotogramas ----------
        void Canvas(Size size)
        {
            if (canvas != null && canvasSize == size) return;
            FreeCanvas();
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = size.Width, biHeight = -size.Height, biPlanes = 1, biBitCount = 32 };
            memDc = CreateCompatibleDC(IntPtr.Zero);
            dib = CreateDIBSection(IntPtr.Zero, ref bi, 0, out var bits, IntPtr.Zero, 0);
            oldObj = SelectObject(memDc, dib);
            canvas = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, bits);
            canvasSize = size;
        }

        void FreeCanvas()
        {
            canvas?.Dispose(); canvas = null;
            if (memDc != IntPtr.Zero) { SelectObject(memDc, oldObj); DeleteDC(memDc); memDc = IntPtr.Zero; }
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            FreeCanvas();
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    }
}
