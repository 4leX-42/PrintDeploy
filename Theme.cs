using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Paleta: blanco, carbon y dorado como acento. El rojo queda solo para errores y estado offline.
    /// Segoe UI para todo; mono solo para datos (IP, drivers, registro).
    /// </summary>
    static class Theme
    {
        public static readonly Color Black = Color.FromArgb(22, 22, 24);   // carbon, no negro puro (legibilidad)
        public static readonly Color Ink = Color.FromArgb(24, 24, 26);
        public static readonly Color Gold = Color.FromArgb(201, 169, 110);       // acento sobre carbon
        public static readonly Color GoldDeep = Color.FromArgb(112, 84, 30);    // acento sobre blanco (texto y filetes)
        public static readonly Color Red = Color.FromArgb(200, 16, 46);
        public static readonly Color RedOnDark = Color.FromArgb(240, 72, 92);   // texto rojo sobre fondo carbon
        public static readonly Color Paper = Color.White;
        public static readonly Color Muted = Color.FromArgb(56, 56, 60);
        public static readonly Color Line = Color.FromArgb(222, 222, 222);
        public static readonly Color Hover = Color.FromArgb(247, 247, 247);
        public static readonly Color Green = Color.FromArgb(40, 200, 110);   // nucleo del indicador online
        public static readonly Color Amber = Color.FromArgb(214, 150, 40);
        public static readonly Color HeadSub = Color.FromArgb(184, 184, 188);    // texto secundario sobre carbon

        /// <summary>
        /// Escala unica de toda la interfaz (medidas y fuentes): DPI del sistema / 96, reducida si la ventana
        /// principal no cabe en la pantalla. Fuentes en pixeles con la misma escala: el texto y los controles
        /// guardan siempre la misma proporcion, sea cual sea el escalado de Windows del equipo.
        /// PRINTDEPLOY_SCALE (p. ej. 1.5) la fuerza.
        /// </summary>
        public static readonly float Scale = InitScale();
        public static int S(int px) => (int)Math.Round(px * Scale);
        static float InitScale()
        {
            if (float.TryParse(Environment.GetEnvironmentVariable("PRINTDEPLOY_SCALE"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var forced) && forced >= 0.5f && forced <= 4f)
                return forced;
            float dpi = 96f;
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX; } catch { }
            float scale = dpi / 96f;
            var area = Screen.PrimaryScreen.WorkingArea;
            // ventana principal 780 x 660 + margen
            float fit = Math.Min(area.Width / 820f, area.Height / 700f);
            return Math.Max(0.75f, Math.Min(scale, fit));
        }
        /// <summary>Tamaño en puntos a 96 ppp, escalado y en pixeles; nunca por debajo de 8,75 pt (legible a 100 % en 1080p).</summary>
        /// Compartidas: una sola instancia por familia, tamaño y estilo para toda la app (no se liberan).
        static Font F(string family, float pt, FontStyle style = FontStyle.Regular)
        {
            var key = (family, pt, style);
            lock (fonts)
            {
                if (!fonts.TryGetValue(key, out var f))
                    fonts[key] = f = new Font(family, Math.Max(pt, 8.75f) * 96f / 72f * Scale, style, GraphicsUnit.Pixel);
                return f;
            }
        }
        static readonly Dictionary<(string, float, FontStyle), Font> fonts = new Dictionary<(string, float, FontStyle), Font>();

        /// <summary>
        /// Texto con ClearType aunque el equipo lo tenga desactivado o en escala de grises (RDP, perfiles nuevos):
        /// TextRenderer toma la calidad de la fuente de la pista de la Graphics. Devuelve la misma Graphics.
        /// </summary>
        public static Graphics Crisp(Graphics g) { g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; return g; }

        /// <summary>Primera familia instalada de la lista (prueba directa: no enumera todas las fuentes del sistema).</summary>
        static string Pick(params string[] names)
        {
            foreach (var n in names)
                try { using (new FontFamily(n)) return n; }
                catch (ArgumentException) { }
            return names.Last();
        }

        // Segoe UI estatica en todos los equipos (Windows 10 y 11): la variable cambia de aspecto entre equipos y con
        // GDI se ve mas fina y lavada en tamaños pequeños.
        static readonly string DisplayFamily = Pick("Segoe UI Semibold", "Segoe UI");
        static readonly string TextFamily = "Segoe UI";
        static readonly string StrongFamily = Pick("Segoe UI Semibold", "Segoe UI");
        static readonly string MonoFamily = "Consolas";
        static readonly string IconFamily = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");

        /// <summary>Iconos del sistema (Segoe Fluent Icons en Windows 11, MDL2 en Windows 10).</summary>
        public static Font Icon(float size) => F(IconFamily, size);
        public const string GlyphPrinter = "";

        /// <summary>Panel clasico "Dispositivos e impresoras".</summary>
        public static void OpenPrintersPanel()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}"); } catch { }
        }

        static readonly string LightFamily = Pick("Segoe UI Light", "Segoe UI");
        /// <summary>Trazo fino: solo la marca de la pantalla de inicio.</summary>
        public static Font Light(float size) => F(LightFamily, size);

        /// <summary>Titulos (cabecera, tarjetas, paso actual).</summary>
        public static Font Title(float size) => F(DisplayFamily, size);
        /// <summary>Texto normal.</summary>
        public static Font Sans(float size, FontStyle style = FontStyle.Regular) => F(TextFamily, size, style);
        /// <summary>Etiquetas, botones, encabezados de tabla.</summary>
        public static Font Strong(float size) => F(StrongFamily, size);
        /// <summary>Solo datos: IP, nombres de driver, registro.</summary>
        public static Font Mono(float size, FontStyle style = FontStyle.Regular) => F(MonoFamily, size, style);

        /// <summary>
        /// Marca de cabecera: rombo dorado pequeño + "PRINTDEPLOY · SECCION" en dorado, sin degradados.
        /// Devuelve el ancho dibujado.
        /// </summary>
        public static int DrawMark(Graphics g, string section, Font font, Point at, Func<int, int> dpiScale)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = dpiScale(6), h = TextRenderer.MeasureText("A", font).Height, cy = at.Y + h / 2;
            using (var b = new SolidBrush(Gold))
                g.FillPolygon(b, new[] { new Point(at.X + d / 2, cy - d / 2), new Point(at.X + d, cy), new Point(at.X + d / 2, cy + d / 2), new Point(at.X, cy) });
            g.SmoothingMode = old;
            var text = string.IsNullOrEmpty(section) ? "PRINTDEPLOY" : "PRINTDEPLOY   ·   " + Caps(section);
            int x = at.X + d + dpiScale(10);
            TextRenderer.DrawText(g, text, font, new Point(x, at.Y), Gold, TextFormatFlags.NoPadding);
            return x + TextRenderer.MeasureText(text, font).Width - at.X;
        }

        public static Color Mix(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        /// <summary>Etiqueta en mayusculas (sin espaciado artificial entre letras).</summary>
        public static string Caps(string s) => s.ToUpperInvariant();
    }

    /// <summary>Base de controles pintados a mano con escalado DPI.</summary>
    abstract class Painted : Control
    {
        protected Painted()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Paper;
        }
        protected bool hover;
        protected int S(int px) => Theme.S(px);
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected static void Draw(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding)
            => TextRenderer.DrawText(g, s, f, r, c, flags);
    }

    /// <summary>Panel con doble bufer (sin parpadeo al repintar).</summary>
    class BufferedPanel : Panel
    {
        public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    /// <summary>Boton de icono para cabeceras oscuras: gris claro, fondo al pasar, con tooltip.</summary>
    sealed class IconBtn : Label
    {
        static readonly ToolTip tips = new ToolTip { InitialDelay = 300 };
        public Color HoverBack = Color.FromArgb(56, 56, 58);
        public IconBtn(string glyph, string tip, float size = 10f, bool icon = true)
        {
            Text = glyph;
            Font = icon ? Theme.Icon(size) : Theme.Sans(size);
            ForeColor = Color.FromArgb(222, 222, 224);
            BackColor = Theme.Black;
            TextAlign = ContentAlignment.MiddleCenter;
            Cursor = Cursors.Hand;
            if (tip != null) tips.SetToolTip(this, tip);
            MouseEnter += (s, e) => { BackColor = HoverBack; ForeColor = Theme.Paper; };
            MouseLeave += (s, e) => { BackColor = Theme.Black; ForeColor = Color.FromArgb(222, 222, 224); };
        }
    }

    /// <summary>Boton plano. Primario: carbon, texto dorado al pasar. Secundario: contorno.</summary>
    sealed class FlatBtn : Painted
    {
        public bool Primary { get; set; }
        /// <summary>Variante para la cabecera oscura: contorno gris, dorado al pasar.</summary>
        public bool OnDark { get; set; }
        readonly Font font = Theme.Title(9.5f);
        bool pressed;
        public FlatBtn(string text, bool primary) { Text = text; Primary = primary; Cursor = Cursors.Hand; Height = S(40); Width = S(150); TabStop = true; }

        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        /// <summary>
        /// Estilo Borderlands 2: esquinas opuestas cortadas, contorno grueso, sombreado plano en dos tonos
        /// (cel shading) y un filete interior. Al pasar el raton se rellena de dorado.
        /// Con ojo (Scanner, Scan, Comprobar): tecla en relieve con canto, degradado y brillo; se hunde al pulsar.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Crisp(e.Graphics);
            bool relief = Eye && Enabled;
            int depth = relief ? (pressed ? S(1) : S(4)) : 0;
            int w = Width - 1, h = Height - 1 - (relief ? S(4) : 0), c = Math.Min(S(9), h / 3);
            int top = relief ? S(4) - depth : 0;   // al pulsar la cara baja hasta el canto
            Color bg, fg, border, inner;
            if (!Enabled) { bg = Theme.Hover; fg = Theme.Line; border = Theme.Line; inner = Color.Empty; }
            else if (OnDark) { bg = hover ? Theme.Gold : Theme.Black; fg = hover ? Theme.Ink : Theme.Gold; border = Theme.Gold; inner = hover ? Color.Empty : Color.FromArgb(70, Theme.Gold); }
            else if (Primary) { bg = hover ? Theme.Gold : Theme.Black; fg = hover ? Theme.Ink : Theme.Paper; border = Theme.Ink; inner = hover ? Color.FromArgb(120, Theme.Ink) : Color.FromArgb(150, Theme.Gold); }
            else { bg = hover ? Theme.Ink : Theme.Paper; fg = hover ? Theme.Gold : Theme.Black; border = Theme.Ink; inner = hover ? Color.FromArgb(110, Theme.Gold) : Color.Empty; }

            g.Clear(Parent?.BackColor ?? Theme.Paper);
            faceBg = bg;
            Point[] Shape(int i, int y0) => new[] { new Point(c + i, y0 + i), new Point(w - i, y0 + i), new Point(w - i, y0 + h - c - i), new Point(w - c - i, y0 + h - i), new Point(i, y0 + h - i), new Point(i, y0 + c + i) };
            var old = g.SmoothingMode;
            if (relief)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // sombra de contacto y canto (la parte de la tecla que asoma por debajo)
                using (var b = new SolidBrush(Color.FromArgb(OnDark ? 120 : 45, 0, 0, 0))) g.FillPolygon(b, Shape(S(2), top + depth + S(1)));
                using (var b = new SolidBrush(Theme.Mix(border, Color.Black, .55f))) g.FillPolygon(b, Shape(0, top + depth));
                // cara: degradado vertical (luz arriba) + brillo en la mitad superior
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(Shape(0, top));
                    using (var b = new LinearGradientBrush(new Rectangle(0, top - 1, Width, h + 2), Theme.Mix(bg, Color.White, .16f), Theme.Mix(bg, Color.Black, .22f), LinearGradientMode.Vertical))
                        g.FillPath(b, path);
                    g.SetClip(path);
                    using (var b = new LinearGradientBrush(new Rectangle(0, top - 1, Width, h / 2 + 2), Color.FromArgb(44, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical) { WrapMode = WrapMode.TileFlipXY })
                        g.FillRectangle(b, 0, top, Width, h / 2);
                    if (sweep >= 0 && sweep <= 1) DrawBeam(g, w, h, top, bg);
                    g.ResetClip();
                }
                // bisel: filo claro arriba, contorno
                using (var p = new Pen(Color.FromArgb(hover ? 120 : 80, 255, 255, 255), 1)) g.DrawLine(p, c + S(2), top + S(2), w - S(2), top + S(2));
                using (var p = new Pen(border, Math.Max(2, S(2))) { LineJoin = LineJoin.Miter }) g.DrawPolygon(p, Shape(1, top));
            }
            else
            {
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(Shape(0, 0));
                    using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                    // cel shading: franja inferior un tono mas oscura, corte duro
                    g.SetClip(path);
                    using (var b = new SolidBrush(Theme.Mix(bg, Color.Black, Enabled ? .14f : .03f))) g.FillRectangle(b, 0, (int)(h * .62), Width, Height);
                    g.ResetClip();
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (inner != Color.Empty) using (var p = new Pen(inner, 1)) g.DrawPolygon(p, Shape(S(4), 0));
                using (var p = new Pen(border, Enabled ? Math.Max(2, S(2)) : 1) { LineJoin = LineJoin.Miter }) g.DrawPolygon(p, Shape(Enabled ? 1 : 0, 0));
            }
            g.SmoothingMode = old;
            var caption = Theme.Caps(base.Text);
            var face = new Rectangle(0, top, Width, h);
            if (relief)
            {
                // ojo + texto, centrados juntos
                int ew = S(34), eh = S(19), gap = S(8);
                int tw = TextRenderer.MeasureText(g, caption, font, Size.Empty, TextFormatFlags.NoPadding).Width;
                int x0 = (Width - (ew + gap + tw)) / 2;
                lamp = new PointF(x0 + ew / 2f, top + h / 2f);
                DrawEye(g, new Rectangle(x0, top + (h - eh) / 2, ew, eh), fg);
                var tr = new Rectangle(x0 + ew + gap, top, tw + S(4), h);
                var tf = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
                Draw(g, caption, font, tr, fg, tf);
                if (sweep >= 0 && sweep <= 1) LightText(g, caption, tr, fg, tf);
            }
            else Draw(g, caption, font, face, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            // foco: marca triangular en la esquina libre
            if (Focused && Enabled)
                using (var b = new SolidBrush(hover ? Theme.Ink : Theme.Gold))
                    g.FillPolygon(b, new[] { new Point(w - S(10), top + S(3)), new Point(w - S(3), top + S(3)), new Point(w - S(3), top + S(10)) });
        }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        // ---------- faro: el haz gira alrededor del ojo como la luz de un faro vista desde la playa ----------
        System.Windows.Forms.Timer anim;
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        float sweep = -1;          // 0..1 dentro de una vuelta; -1 = apagado
        bool scanning;
        PointF lamp;               // centro del ojo = foco del faro
        Color faceBg;              // color de la cara (el ojo se tiñe con el)
        const float TurnMs = 2400f;

        /// <summary>Faro girando sin parar mientras dura un escaneo.</summary>
        public bool Scanning
        {
            get => scanning;
            set { if (scanning == value) return; scanning = value; if (value) StartSweep(); }
        }

        /// <summary>Una vuelta del faro al entrar el raton.</summary>
        public bool SweepOnHover { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            if (SweepOnHover && !scanning && sweep < 0) StartSweep();
            base.OnMouseEnter(e);
        }

        /// <summary>Lanza una vuelta (o el giro continuo si Scanning).</summary>
        public void StartSweep()
        {
            if (anim == null)
            {
                anim = new System.Windows.Forms.Timer { Interval = 16 };
                anim.Tick += (s, e) =>
                {
                    float t = clock.ElapsedMilliseconds / TurnMs;
                    if (scanning) sweep = t % 1f;
                    else if (t <= 1) sweep = t;
                    else { sweep = -1; anim.Stop(); }
                    Invalidate();
                };
            }
            if (sweep >= 0 && anim.Enabled) return;   // ya girando: sin saltos
            clock.Restart();
            sweep = 0;
            anim.Start();
        }

        protected override void Dispose(bool disposing) { if (disposing) anim?.Dispose(); base.Dispose(disposing); }

        /// <summary>Angulo del haz: empieza de espaldas (detras), barre a la izquierda, destella de frente y sigue hacia la derecha.</summary>
        double Angle => sweep < 0 ? Math.PI / 2 : -Math.PI / 2 - sweep * Math.PI * 2;
        /// <summary>Encendido suave al empezar y al acabar una vuelta suelta.</summary>
        float Envelope => scanning ? 1f : (float)Math.Pow(Math.Sin(Math.PI * Math.Max(0, Math.Min(1, sweep))), .5);

        /// <summary>Icono de ojo delante del texto (estilo Nmap); el iris sigue al haz.</summary>
        public bool Eye { get; set; }

        /// <summary>Ojo del logo de Nmap (NmapEye) con resplandor cuando el faro mira de frente.</summary>
        void DrawEye(Graphics g, Rectangle r, Color c)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            bool on = sweep >= 0 && sweep <= 1;
            float front = on ? (float)Math.Max(0, Math.Sin(Angle)) * Envelope : 0;   // 1 = el haz apunta hacia quien mira
            if (front > .01f)
                using (var glow = new GraphicsPath())
                {
                    float gr = r.Width * (.6f + .9f * front);
                    glow.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
                    var lc = Theme.Mix(c, Color.White, .5f);
                    using (var b = new PathGradientBrush(glow) { CenterColor = Color.FromArgb((int)(190 * front * front), lc), SurroundColors = new[] { Color.FromArgb(0, lc) } })
                        g.FillPath(b, glow);
                }
            // ojo del logo de Nmap: la pupila mira hacia el haz y el globo del iris gira con el faro
            float look = on ? (float)Math.Cos(Angle) * Envelope : 0;
            NmapEye.Draw(g, r, look, on ? Angle : .3, c, faceBg);
            g.SmoothingMode = old;
        }

        /// <summary>
        /// Haz del faro (ya recortado a la cara del boton): cono que nace en el ojo y se apaga con la distancia
        /// (degradado radial desde el foco), con nucleo mas intenso y un charco de luz donde incide. Su longitud sigue
        /// el giro (cos): de espaldas se ve tenue; de frente se acorta y destella. Guarda los conos para iluminar el texto.
        /// </summary>
        void DrawBeam(Graphics g, int w, int h, int top, Color bg)
        {
            beamWide = beamCore = null;
            if (lamp.IsEmpty) lamp = new PointF(w * .3f, top + h / 2f);
            double a = Angle;
            float env = Envelope, dx = (float)Math.Cos(a), toward = (float)Math.Sin(a);
            beamLight = bg.GetBrightness() > .5f ? Color.White : Theme.Mix(Theme.Gold, Color.White, .35f);
            float len = Math.Abs(dx) * w * 1.15f;
            if (len < S(3)) return;
            float tipX = lamp.X + Math.Sign(dx) * len;
            beamAlpha = env * (toward >= 0 ? .6f + .4f * toward : .3f + .3f * (1 + toward));
            PointF[] Cone(float spread) { float half = len * spread + S(2); return new[] { lamp, new PointF(tipX, lamp.Y - half), new PointF(tipX, lamp.Y + half) }; }
            void Fill(PointF[] cone, int peak)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(cone);
                    using (var b = new PathGradientBrush(path) { CenterPoint = lamp, CenterColor = Color.FromArgb((int)(peak * beamAlpha), beamLight), SurroundColors = new[] { Color.FromArgb(0, beamLight) } })
                        g.FillPath(b, path);
                }
            }
            beamWide = Cone(.5f);
            beamCore = Cone(.14f);
            Fill(Cone(.75f), 60);     // halo
            Fill(beamWide, 110);      // haz
            Fill(beamCore, 210);      // nucleo
            // charco de luz donde el haz toca la superficie
            using (var pool = new GraphicsPath())
            {
                float pcx = lamp.X + Math.Sign(dx) * len * .62f, pw = len * .42f, ph = h * .9f;
                pool.AddEllipse(pcx - pw, lamp.Y - ph / 2, pw * 2, ph);
                using (var b = new PathGradientBrush(pool) { CenterColor = Color.FromArgb((int)(55 * beamAlpha), beamLight), SurroundColors = new[] { Color.FromArgb(0, beamLight) } })
                    g.FillPath(b, pool);
            }
        }

        PointF[] beamWide, beamCore;
        Color beamLight;
        float beamAlpha;

        /// <summary>Texto iluminado por el haz: se repinta mas claro solo dentro de los conos.</summary>
        void LightText(Graphics g, string caption, Rectangle r, Color fg, TextFormatFlags flags)
        {
            if (beamWide == null || beamAlpha < .05f) return;
            foreach (var (cone, k) in new[] { (beamWide, .35f), (beamCore, .8f) })
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(cone);
                    var clip = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    Draw(g, caption, font, r, Theme.Mix(fg, beamLight, k * beamAlpha), flags);
                    g.Clip = clip;
                    clip.Dispose();
                }
        }
    }

    /// <summary>Accion secundaria discreta: icono + etiqueta pequeña en gris; dorado y subrayado al pasar.</summary>
    sealed class LinkBtn : Painted
    {
        readonly string glyph;
        readonly Font fIcon = Theme.Icon(10f), fText = Theme.Strong(8.25f);
        public LinkBtn(string glyph, string text)
        {
            this.glyph = glyph; Text = text; Cursor = Cursors.Hand; TabStop = true;
            Height = S(30);
            Width = S(30) + TextRenderer.MeasureText(Theme.Caps(text), fText, Size.Empty, TextFormatFlags.NoPadding).Width + S(6);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Crisp(e.Graphics);
            g.Clear(Parent?.BackColor ?? Theme.Paper);
            var c = !Enabled ? Theme.Line : hover || Focused ? Theme.GoldDeep : Theme.Muted;
            Draw(g, glyph, fIcon, new Rectangle(0, 0, S(24), Height), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var caps = Theme.Caps(Text);
            int tw = TextRenderer.MeasureText(g, caps, fText, Size.Empty, TextFormatFlags.NoPadding).Width;
            Draw(g, caps, fText, new Rectangle(S(28), 0, tw + S(2), Height), c);
            if (hover || Focused) using (var b = new SolidBrush(Theme.Gold)) g.FillRectangle(b, S(28), Height / 2 + S(9), tw, 1);
        }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Entrada del menu de inicio: numero fino, titulo y una linea; filete inferior. Al pasar: acento dorado y flecha.</summary>
    sealed class Card : Painted
    {
        readonly string index, title, desc;
        readonly Font fIndex = Theme.Light(22f), fTitle = Theme.Title(15f), fDesc = Theme.Sans(9.5f), fArrow = Theme.Sans(15f);
        public Card(string index, string title, string desc) { this.index = index; this.title = title; this.desc = desc; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Crisp(e.Graphics);
            g.Clear(hover ? Theme.Hover : Theme.Paper);
            using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, 0, Height - 1, Width, 1);
            if (hover) using (var b = new SolidBrush(Theme.GoldDeep)) g.FillRectangle(b, 0, 0, S(2), Height - 1);

            int hTitle = TextRenderer.MeasureText(title, fTitle).Height, hDesc = TextRenderer.MeasureText(desc, fDesc).Height;
            int y = (Height - (hTitle + S(4) + hDesc)) / 2, x = S(76), textW = Width - x - S(64);
            Draw(g, index, fIndex, new Rectangle(S(16), 0, S(60), Height), hover ? Theme.GoldDeep : Theme.Gold);
            Draw(g, title, fTitle, new Rectangle(x, y, textW, hTitle), Theme.Black);
            Draw(g, desc, fDesc, new Rectangle(x, y + hTitle + S(4), textW, hDesc), Theme.Muted);
            if (hover) Draw(g, "→", fArrow, new Rectangle(Width - S(56), 0, S(40), Height), Theme.GoldDeep, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Fila seleccionable (casilla cuadrada) con columnas.</summary>
    sealed class CheckRow : Painted
    {
        bool isChecked;
        public bool Checked { get => isChecked; set { isChecked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
        public event EventHandler CheckedChanged;
        public string[] Columns = new string[0];
        public float[] Widths = { 1f };
        public bool Header;
        /// <summary>Columna de estado (ojo verde / rojo) entre la casilla y el texto.</summary>
        public bool StateColumn;
        ScanState state = ScanState.Pending;
        public ScanState State { get => state; set { state = value; Invalidate(); } }
        readonly Font fMain = Theme.Sans(9.75f), fMono = Theme.Mono(8.75f), fHead = Theme.Strong(8f);
        public CheckRow() { Cursor = Cursors.Hand; Height = S(40); Eyes.Attach(this, S(90)); }
        protected override void OnClick(EventArgs e) { if (!Header && Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Crisp(e.Graphics);
            g.Clear(Header ? Theme.Black : (hover && Enabled ? Theme.Hover : Theme.Paper));
            int box = S(14), x = S(14), cy = Height / 2;
            if (!Header)
            {
                var rb = new Rectangle(x, cy - box / 2, box, box);
                using (var p = new Pen(Theme.Ink, 1)) g.DrawRectangle(p, rb);
                if (isChecked) using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, rb.X + S(3), rb.Y + S(3), rb.Width - S(5), rb.Height - S(5));
                using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }
            int cx = x + box + S(14);
            if (StateColumn)
            {
                if (!Header) ScannerForm.DrawState(g, state, new Rectangle(cx, cy - S(7), S(14), S(14)), this, installed: true);
                cx += S(26);
            }
            int avail = Width - cx - S(10);
            for (int i = 0; i < Columns.Length; i++)
            {
                int cw = (int)(avail * Widths[Math.Min(i, Widths.Length - 1)]);
                var r = new Rectangle(cx, 0, cw - S(8), Height);
                if (Header) Draw(g, Theme.Caps(Columns[i]), fHead, r, Color.FromArgb(228, 228, 230));
                else Draw(g, Columns[i], i == 0 ? fMain : fMono, r, i == 0 ? Theme.Black : Theme.Muted);
                cx += cw;
            }
        }
    }

    /// <summary>Campo de texto con etiqueta y subrayado (dorado con foco).</summary>
    sealed class LineField : Panel
    {
        public readonly TextBox Box = new TextBox();
        readonly string label;
        readonly Font fLabel = Theme.Strong(8f);
        public LineField(string label, bool mono)
        {
            this.label = label;
            DoubleBuffered = true;
            BackColor = Theme.Paper;
            Box.BorderStyle = BorderStyle.None;
            Box.Font = mono ? Theme.Mono(11f) : Theme.Sans(11f);
            Box.BackColor = Theme.Paper;
            Box.ForeColor = Theme.Black;
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.EnabledChanged += (s, e) => { Box.BackColor = Theme.Paper; Invalidate(); };
            Height = S(58);
        }
        int S(int px) => Theme.S(px);
        protected override void OnLayout(LayoutEventArgs e)
        {
            Box.SetBounds(0, S(22), Width, Box.PreferredHeight);
            base.OnLayout(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(Theme.Crisp(e.Graphics), Theme.Caps(label), fLabel, new Point(0, 0), Box.Enabled ? Theme.Muted : Theme.Line, TextFormatFlags.NoPadding);
            var c = !Box.Enabled ? Theme.Line : Box.Focused ? Theme.GoldDeep : Theme.Black;
            using (var b = new SolidBrush(c)) e.Graphics.FillRectangle(b, 0, Box.Bottom + S(4), Width, S(Box.Focused ? 2 : 1));
        }
    }

    /// <summary>Barra de progreso fina con segmentos.</summary>
    sealed class ThinProgress : Painted
    {
        int value;
        public int Value { get => value; set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        public Color BarColor = Theme.Black;
        public ThinProgress() { Height = S(6); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Crisp(e.Graphics);
            g.Clear(Theme.Line);
            int w = (int)(Width * value / 100.0);
            using (var b = new SolidBrush(BarColor)) g.FillRectangle(b, 0, 0, w, Height);
            using (var p = new Pen(Theme.Paper, Math.Max(1, S(2))))
                for (int x = S(24); x < Width; x += S(24)) g.DrawLine(p, x, 0, x, Height);
        }
    }
}
