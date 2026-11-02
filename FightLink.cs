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
    /// Pelea en el hueco entre la ventana principal y el escaner (ver Arena y Fighter). Apagada por defecto: la interfaz queda limpia.
    /// Toggle() (el rombo de la cabecera del escaner) la enciende: las ventanas se separan temblando, se despliega la viga y empieza;
    /// otro Toggle() la quita (y el sonido) y devuelve las ventanas a su sitio. Ventana por capas encima de las dos, sin clics ni foco;
    /// el menu de caras es una ventanita aparte que si recibe el clic.
    /// </summary>
    sealed class FightLink : Form
    {
        public static readonly Sfx Sound = new Sfx();
        readonly Form root, tip;
        readonly Timer timer = new Timer { Interval = 33 }, moveTimer = new Timer { Interval = 15 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Arena arena = new Arena { Snd = Sound };
        readonly FaceBar faces;
        double last;
        bool linked, moving;
        IntPtr memDc, dib, oldObj;
        Bitmap canvas;
        Size canvasSize;
        Rectangle box;
        // separacion animada de las ventanas
        Point savedRoot, savedTip, fromRoot, fromTip, toRoot, toTip;
        double moveAt; float moveDur; bool moveShake;
        readonly Random rng = new Random();

        public bool On { get; private set; }
        static bool warmed;

        public FightLink(Form root, Form tip)
        {
            this.root = root; this.tip = tip;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Owner = tip;   // el escaner es de la principal y esto del escaner: queda encima de las dos
            faces = new FaceBar(arena) { Owner = this };
            timer.Tick += (s, e) => Frame();
            moveTimer.Tick += (s, e) => MoveStep();
            foreach (var f in new[] { root, tip })
            {
                f.Move += (s, e) => Sync();
                f.Resize += (s, e) => Sync();
                f.VisibleChanged += (s, e) => { if (f == tip && !tip.Visible && On) Toggle(); else Sync(); };
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

        // ---------- interruptor ----------
        public void Toggle()
        {
            if (moving) return;
            On = !On;
            if (On)
            {
                savedRoot = root.Location; savedTip = tip.Location;
                PlanSeparation(out toRoot, out toTip);
                arena.BeginOpening();
                Sound.Start();
                if (!warmed) { warmed = true; System.Threading.Tasks.Task.Run(() => Arena.Prewarm()); }
                StartMove(toRoot, toTip, 1.3f, true);
                Sound.Play("theme", .7f);
            }
            else
            {
                Sound.Stop();
                Sync();
                // vuelven a donde estaban (si siguen en la misma pantalla)
                StartMove(savedRoot, savedTip, .35f, false);
            }
        }

        /// <summary>Hueco amplio para pelear: el conjunto centrado en la pantalla; si no cabe, el hueco que haya.</summary>
        void PlanSeparation(out Point r, out Point t)
        {
            r = root.Location; t = tip.Location;
            Rectangle a = root.Bounds, b = tip.Bounds;
            bool side = Math.Max(b.Left - a.Right, a.Left - b.Right) >= Math.Max(b.Top - a.Bottom, a.Top - b.Bottom);
            if (!side || root.WindowState != FormWindowState.Normal) return;
            var area = Screen.FromControl(root).WorkingArea;
            bool tipRight = b.Left >= a.Left;
            int gapNow = tipRight ? b.Left - a.Right : a.Left - b.Right;
            // hueco amplio; si la pantalla no da, las ventanas salen en parte por los lados (siempre con media ventana a la vista)
            int gap = Math.Min(Theme.S(760), area.Width - (a.Width + b.Width) / 2);
            if (gap <= gapNow) return;
            int x0 = area.Left + (area.Width - (a.Width + gap + b.Width)) / 2;
            if (tipRight) { r = new Point(x0, a.Top); t = new Point(x0 + a.Width + gap, b.Top); }
            else { t = new Point(x0, b.Top); r = new Point(x0 + b.Width + gap, a.Top); }
        }

        void StartMove(Point r, Point t, float dur, bool shake)
        {
            fromRoot = root.Location; fromTip = tip.Location; toRoot = r; toTip = t;
            moveAt = clock.Elapsed.TotalSeconds; moveDur = dur; moveShake = shake; moving = true;
            moveTimer.Start();
        }

        /// <summary>Paso de la separacion: interpolacion suave con temblor que se apaga al final.</summary>
        void MoveStep()
        {
            float u = (float)Math.Min(1, (clock.Elapsed.TotalSeconds - moveAt) / moveDur), e = u * u * (3 - 2 * u);
            float amp = moveShake ? Theme.S(5) * (1 - u) * (u < .15f ? u / .15f : 1) : 0;
            Point P(Point a, Point b, int sx, int sy) => new Point((int)(a.X + (b.X - a.X) * e) + sx, (int)(a.Y + (b.Y - a.Y) * e) + sy);
            int J() => amp > 0 ? (int)Math.Round((rng.NextDouble() * 2 - 1) * amp) : 0;
            if (root.WindowState == FormWindowState.Normal) root.Location = P(fromRoot, toRoot, J(), J());
            tip.Location = P(fromTip, toTip, J(), J());
            if (u < 1) return;
            root.Location = root.WindowState == FormWindowState.Normal ? toRoot : root.Location; tip.Location = toTip;
            moveTimer.Stop(); moving = false;
            Sync();
            arena.EndOpening();
        }

        // ---------- ciclo ----------
        void Sync()
        {
            if (IsDisposed) return;
            bool show = On && !(moving && !arena.Opening) && root.Visible && tip.Visible && root.WindowState != FormWindowState.Minimized && tip.WindowState != FormWindowState.Minimized
                && arena.Fit(root.Bounds, tip.Bounds);
            if (!show)
            {
                linked = false;
                timer.Stop();
                if (Visible) { Hide(); arena.ReleaseCaches(); }
                FreeCanvas();
                if (faces.Visible) faces.Hide();
                return;
            }
            box = arena.Bounds;
            if (!linked) { linked = true; last = clock.Elapsed.TotalSeconds; }
            if (!Visible) Show();
            Frame();
            timer.Start();
        }

        void Frame()
        {
            if (!linked || IsDisposed || box.Width <= 0 || box.Height <= 0) return;
            timer.Interval = Form.ActiveForm == null ? 66 : 33;   // app en segundo plano: mitad de fotogramas
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)(now - last); last = now;
            try
            {
                arena.Step(dt);
                if (!Canvas(box.Size)) return;   // sin memoria para el lienzo: se salta este fotograma
                using (var g = Graphics.FromImage(canvas))
                {
                    g.SetClip(new Rectangle(0, 0, box.Width, box.Height));
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TranslateTransform(-box.X, -box.Y);
                    arena.Draw(g, dt);
                }
                var dst = new POINT { X = box.X, Y = box.Y };
                var size = new SIZE { cx = box.Width, cy = box.Height };
                var src = new POINT();
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
                var fs = FaceBar.Want(arena.Roster.Count);
                faces.Place(new Rectangle(root.Right - fs.Width - Theme.S(18), root.Bottom - fs.Height - Theme.S(12), fs.Width, fs.Height));
            }
            catch (Exception ex)
            {
                // un fotograma fallido no debe tumbar la ventana: se anota (una vez por tipo) y se sigue
                string key = ex.GetType().Name + ex.TargetSite?.Name;
                if (logged.Add(key)) Crash.Log("pelea: fotograma", ex);
            }
        }

        /// <summary>Para pruebas y capturas: avanza la pelea 'seconds' y la dibuja sobre g (coordenadas de pantalla).</summary>
        internal bool RenderAt(Graphics g, float seconds, float step = 1 / 30f)
        {
            if (!arena.Fit(root.Bounds, tip.Bounds)) return false;
            for (float t = 0; t < seconds; t += step) arena.Step(step);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            arena.Draw(g, step);
            return true;
        }

        internal Rectangle Zone => arena.Bounds;

        readonly System.Collections.Generic.HashSet<string> logged = new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// Lienzo (DIB premultiplicado) con holgura: se reserva redondeado a 128 px y se reutiliza mientras quepa, asi al arrastrar las
        /// ventanas no se crea y destruye uno por fotograma (UpdateLayeredWindow acepta un origen mayor que la ventana). Si la reserva
        /// falla, devuelve false en vez de crear un Bitmap sobre un puntero nulo (eso era el "memoria insuficiente").
        /// </summary>
        bool Canvas(Size size)
        {
            if (canvas != null && size.Width <= canvasSize.Width && size.Height <= canvasSize.Height) return true;
            FreeCanvas();
            var cap = new Size((size.Width + 127) / 128 * 128, (size.Height + 127) / 128 * 128);
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = cap.Width, biHeight = -cap.Height, biPlanes = 1, biBitCount = 32 };
            memDc = CreateCompatibleDC(IntPtr.Zero);
            dib = CreateDIBSection(IntPtr.Zero, ref bi, 0, out var bits, IntPtr.Zero, 0);
            if (memDc == IntPtr.Zero || dib == IntPtr.Zero || bits == IntPtr.Zero) { FreeCanvas(); return false; }
            oldObj = SelectObject(memDc, dib);
            canvas = new Bitmap(cap.Width, cap.Height, cap.Width * 4, PixelFormat.Format32bppPArgb, bits);
            canvasSize = cap;
            return true;
        }

        void FreeCanvas()
        {
            canvas?.Dispose(); canvas = null;
            if (memDc != IntPtr.Zero) { SelectObject(memDc, oldObj); DeleteDC(memDc); memDc = IntPtr.Zero; }
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
            canvasSize = Size.Empty;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); moveTimer.Dispose(); faces.Dispose(); }
            FreeCanvas();
            base.Dispose(disposing);
        }

        /// <summary>
        /// Menu de caras en la esquina de la ventana principal (como la pantalla de seleccion): un clic mete a ese luchador en la pelea.
        /// Marco dorado vivo en los que estan peleando, tenue en el resto. Bajo el raton la cara se acerca, se balancea y se enciende
        /// con la luz de su color; al elegirla da un golpe de zoom con destello, onda y chispas suyas.
        /// </summary>
        sealed class FaceBar : Form
        {
            readonly Arena arena; int hover = -1; Rectangle at; string sig;
            // nombre del luchador bajo el raton: aparece encima de las caras con un fundido corto y se va igual
            int named = -1; float nameA;
            // animacion: solo corre mientras hay algo moviendose (raton encima, golpe de seleccion o chispas)
            readonly Timer anim = new Timer { Interval = 15 }; readonly Stopwatch clock = Stopwatch.StartNew(); double lastT;
            readonly float[] hot = new float[8], pop = new float[8];   // encendido por el raton y golpe de seleccion, 0..1
            struct Spark { public float X, Y, VX, VY, Life; public Color C; }
            readonly System.Collections.Generic.List<Spark> sparks = new System.Collections.Generic.List<Spark>();
            readonly Random rnd = new Random();
            public FaceBar(Arena a)
            {
                arena = a;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                MouseMove += (s, e) => { int i = Tile(e.Location); if (i != hover) Hover(i); };
                MouseLeave += (s, e) => Hover(-1);
                anim.Tick += (s, e) => Step();
                // clic izquierdo: jugador 1 (izquierda); clic derecho: jugador 2 (derecha)
                MouseClick += (s, e) => { int i = Tile(e.Location); if (i >= 0) Choose(i, e.Button == MouseButtons.Right ? 2 : 1); };
            }

            void Choose(int i, int slot)
            {
                var f = arena.Roster[i];
                arena.Pick(f.Who, slot); sig = null;
                Sound.Play("teleport", .6f);
                Sound.Play(f.Who == Who.Mileena ? "swing" : f.Who == Who.Baraka ? "slash" : f.Key + "_attack", .8f);   // su grito (o el filo de sus armas)
                pop[i] = 1;
                var rc = TileRect(i); float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height * .5f;
                for (int k = 0; k < 16; k++)
                {
                    double a = rnd.NextDouble() * Math.PI * 2; float v = Theme.S(60) + (float)rnd.NextDouble() * Theme.S(90);
                    sparks.Add(new Spark { X = cx, Y = cy, VX = (float)Math.Cos(a) * v, VY = (float)Math.Sin(a) * v - Theme.S(30), Life = .35f + (float)rnd.NextDouble() * .3f, C = k % 3 == 0 ? Color.White : f.Look.Rim });
                }
                Wake();
            }

            void Hover(int i)
            {
                hover = i;
                if (i >= 0) { if (named != i) nameA = Math.Min(nameA, .35f); named = i; }   // de una cara a otra: el nuevo nombre entra sin parpadeo
                Wake();
            }

            void Wake() { if (!anim.Enabled) { lastT = clock.Elapsed.TotalSeconds; anim.Start(); } Paint0(); }

            void Step()
            {
                double now = clock.Elapsed.TotalSeconds; float h = (float)Math.Min(.05, now - lastT); lastT = now;
                float goal = hover >= 0 ? 1 : 0;
                nameA = goal > nameA ? Math.Min(1, nameA + h * 6.5f) : Math.Max(0, nameA - h * 9);
                if (nameA == 0 && goal == 0) named = -1;
                bool busy = hover >= 0 || nameA > 0;
                for (int i = 0; i < hot.Length; i++)
                {
                    float tg = i == hover ? 1 : 0;
                    hot[i] = tg > hot[i] ? Math.Min(1, hot[i] + h * 7) : Math.Max(0, hot[i] - h * 5);
                    pop[i] = Math.Max(0, pop[i] - h * 2.6f);
                    busy |= hot[i] > 0 || pop[i] > 0;
                }
                for (int k = sparks.Count - 1; k >= 0; k--)
                {
                    var sp = sparks[k]; sp.Life -= h;
                    if (sp.Life <= 0) { sparks.RemoveAt(k); continue; }
                    sp.X += sp.VX * h; sp.Y += sp.VY * h; sp.VY += Theme.S(260) * h; sp.VX *= 1 - 2 * h;
                    sparks[k] = sp;
                }
                busy |= sparks.Count > 0;
                if (!busy) anim.Stop();
                Paint0();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) anim.Dispose();
                base.Dispose(disposing);
            }

            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; cp.ClassStyle |= 0x8; return cp; }   // LAYERED | TOOLWINDOW | NOACTIVATE; CS_DBLCLKS
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
                base.WndProc(ref m);
            }

            static int TW => Theme.S(36);
            static int TH => Theme.S(42);
            static int Gap => Theme.S(5);
            static int LabelH => Theme.S(20);   // franja transparente encima de las caras para el nombre (no tapa clics si esta vacia)
            public static Size Want(int n) => new Size(n * TW + (n - 1) * Gap + Theme.S(4), LabelH + TH + Theme.S(4));
            static Rectangle TileRect(int i) => new Rectangle(Theme.S(2) + i * (TW + Gap), LabelH + Theme.S(2), TW, TH);

            int Tile(Point p)
            {
                for (int i = 0; i < arena.Roster.Count; i++) if (TileRect(i).Contains(p)) return i;
                return -1;
            }

            public void Place(Rectangle r)
            {
                string now = arena.P1.Who + "-" + arena.P2.Who;
                if (r == at && now == sig && Visible) return;
                at = r; sig = now;
                if (!Visible) { hover = named = -1; nameA = 0; Array.Clear(hot, 0, hot.Length); Array.Clear(pop, 0, pop.Length); sparks.Clear(); anim.Stop(); Show(); }
                Paint0();
            }

            void Paint0()
            {
                if (at.Width <= 0) return;
                try { PaintCore(); } catch (Exception ex) { Crash.Log("pelea: caras", ex); }
            }

            void PaintCore()
            {
                using (var bmp = new Bitmap(at.Width, at.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp)) Draw(g);
                    Push(this, bmp, at.Location);
                }
            }

            void Draw(Graphics g)
            {
                g.Clear(Color.Transparent);
                using (var catcher = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(catcher, 0, LabelH, at.Width, at.Height - LabelH);   // la fila de caras recibe el raton entera
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (named >= 0 && nameA > 0) Label(g, arena.Roster[named].Name, TileRect(named), nameA);
                float time = (float)clock.Elapsed.TotalSeconds;
                for (int i = 0; i < arena.Roster.Count; i++)
                {
                    var f = arena.Roster[i]; bool fighting = f == arena.P1 || f == arena.P2;
                    float ht = i < hot.Length ? hot[i] : 0, hs = ht * ht * (3 - 2 * ht), pp = i < pop.Length ? pop[i] : 0;
                    float lit = Math.Max(hs, fighting ? 1 : 0);
                    var rc = TileRect(i);
                    using (var br = Fighter.Grad(rc, Color.FromArgb(235, 38, 38, 42), Color.FromArgb(235, 10, 10, 12), 90f)) g.FillRectangle(br, rc);
                    var old = g.Clip; g.SetClip(rc);
                    // luz de su color subiendo desde abajo
                    if (hs > 0 || pp > 0)
                        using (var br = Fighter.Grad(new PointF(0, rc.Bottom), new PointF(0, rc.Y + rc.Height * .25f), Color.FromArgb((int)(150 * Math.Max(hs, pp)), f.Look.Rim), Color.FromArgb(0, f.Look.Rim)))
                            g.FillRectangle(br, rc);
                    // cara en grande, recortada al marco: se acerca, respira y se balancea bajo el raton; golpe de zoom al elegirla
                    float zoom = 1 + .12f * hs + .28f * pp * pp, r = TW * .36f * zoom;
                    float sway = (float)Math.Sin(time * 2.3 + i) * 5 * hs + (float)Math.Sin(pp * 18) * 9 * pp;
                    float bob = (float)Math.Sin(time * 3.1 + i) * Theme.S(1) * hs - Theme.S(2) * hs;
                    Fighter.Head(g, new PointF(rc.X + rc.Width * .5f, rc.Y + rc.Height * .56f + bob), sway, 1, f.Look, .55f + .45f * lit, 0, r / .09f,
                        (c, dk) => { var col = dk > 0 ? Theme.Mix(c, Color.Black, dk) : c; return lit >= 1 ? col : Theme.Mix(col, Color.Black, .45f * (1 - lit)); });
                    if (pp > 0) using (var br = new SolidBrush(Color.FromArgb((int)(160 * pp * pp * pp), 255, 255, 255))) g.FillRectangle(br, rc);   // destello
                    g.Clip = old; old.Dispose();
                    var frame = fighting ? Theme.Gold : Color.FromArgb((int)(110 + 145 * hs), Theme.Mix(Theme.GoldDeep, Theme.Mix(Theme.Gold, Color.White, .3f), hs));
                    using (var pen = new Pen(frame, fighting || hs > .5f ? Math.Max(2f, Theme.S(2)) : Math.Max(1f, Theme.S(1))))
                        g.DrawRectangle(pen, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);
                    // onda del golpe: un marco que se abre y se apaga
                    if (pp > 0)
                    {
                        float grow = (1 - pp) * Theme.S(7);
                        using (var pen = new Pen(Color.FromArgb((int)(230 * pp), Theme.Mix(f.Look.Rim, Color.White, .4f)), Math.Max(1f, Theme.S(2) * pp)))
                            g.DrawRectangle(pen, rc.X - grow, rc.Y - grow, rc.Width - 1 + 2 * grow, rc.Height - 1 + 2 * grow);
                    }
                }
                foreach (var sp in sparks)
                {
                    float k = Math.Min(1, sp.Life / .3f), len = .025f;
                    using (var pen = new Pen(Color.FromArgb((int)(255 * k), sp.C), Math.Max(1f, Theme.S(2) * k)))
                        g.DrawLine(pen, sp.X, sp.Y, sp.X - sp.VX * len, sp.Y - sp.VY * len);
                }
            }

            /// <summary>
            /// Nombre en cursiva dorada como el del marcador, centrado sobre la cara (sin salirse por los lados), que sube un poco al
            /// aparecer; debajo, un filo dorado que se abre desde el centro.
            /// </summary>
            void Label(Graphics g, string name, Rectangle tile, float a)
            {
                float e = a * a * (3 - 2 * a), size = Theme.S(13);
                var (path, b) = Arena.TextPath(name, size, FontStyle.Italic);
                float cx = Math.Max(b.Width / 2 + Theme.S(3), Math.Min(at.Width - b.Width / 2 - Theme.S(3), tile.X + tile.Width / 2f));
                float baseY = LabelH - Theme.S(5) + (1 - e) * Theme.S(3);
                var st = g.Save();
                g.TranslateTransform(cx - b.X - b.Width / 2, baseY - b.Bottom);
                int al = (int)(255 * e);
                using (var pen = new Pen(Color.FromArgb(al * 3 / 4, 0, 0, 0), Math.Max(2f, size * .2f)) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                using (var br = Fighter.Grad(new PointF(0, b.Top), new PointF(0, b.Bottom), Color.FromArgb(al, Theme.Mix(Theme.Gold, Color.White, .45f)), Color.FromArgb(al, Theme.GoldDeep))) g.FillPath(br, path);
                g.Restore(st);
                float half = (b.Width / 2 + Theme.S(4)) * e, ly = LabelH - Theme.S(2);
                using (var br = Fighter.Grad(new PointF(cx - half, ly), new PointF(cx + half, ly), Color.FromArgb(0, Theme.Gold), Color.FromArgb(0, Theme.Gold)))
                {
                    br.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, Theme.Gold), Color.FromArgb(al * 4 / 5, Theme.Gold), Color.FromArgb(0, Theme.Gold) }, Positions = new[] { 0f, .5f, 1f } };
                    if (half > 1) g.FillRectangle(br, cx - half, ly, 2 * half, Math.Max(1f, Theme.S(1)));
                }
            }
        }

        /// <summary>Lleva un bitmap con alfa a una ventana por capas.</summary>
        static void Push(Form f, Bitmap bmp, Point at)
        {
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = bmp.Width, biHeight = -bmp.Height, biPlanes = 1, biBitCount = 32 };
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hb = CreateDIBSection(IntPtr.Zero, ref bi, 0, out var bits, IntPtr.Zero, 0), old = SelectObject(mem, hb);
            try
            {
                using (var pre = new Bitmap(bmp.Width, bmp.Height, bmp.Width * 4, PixelFormat.Format32bppPArgb, bits))
                using (var g = Graphics.FromImage(pre)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(bmp, 0, 0); }
                var dst = new POINT { X = at.X, Y = at.Y }; var size = new SIZE { cx = bmp.Width, cy = bmp.Height }; var src = new POINT();
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(f.Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally { SelectObject(mem, old); DeleteObject(hb); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
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
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    }
}
