using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Indicadores de estado como ojos de fuego (estilo Ojo de Sauron): ovalo vertical de fibras de fuego, hendidura negra
    /// con filo incandescente y nubes de fuego, generado por pixel con ruido fractal 3D. Fuego azul = instalada en este equipo y online, verde = online, rojo = offline, dorado tenue = comprobando, brasa apagada = pendiente.
    /// Se encienden al aparecer; despues cada ojo aviva sus llamas por su cuenta cada 8-16 s (al azar), nunca dos
    /// arrancando a la vez. Fotogramas pintados una vez y guardados en cache; un unico temporizador para toda la app,
    /// que entre llamaradas no repinta nada.
    /// </summary>
    static class Eyes
    {
        const double IgniteAt = 0.3, FlareDur = 1.2, MinGap = 8, MaxGap = 16, Apart = 0.4;
        const int Fps = 24, Frames = (int)(FlareDur * Fps);
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly List<(WeakReference<Control> view, int strip)> views = new List<(WeakReference<Control>, int)>();
        static readonly Dictionary<object, double> nextFlare = new Dictionary<object, double>();
        static readonly Random rnd = new Random();
        static Timer timer;

        /// <summary>Repinta el control mientras alguna llama se mueve; 'strip' > 0: solo esa franja izquierda (la columna de los ojos).</summary>
        public static void Attach(Control c, int strip = 0)
        {
            views.Add((new WeakReference<Control>(c), strip));
            if (timer == null)
            {
                timer = new Timer { Interval = 1000 / Fps };
                timer.Tick += (s, e) => Tick();
                timer.Start();
            }
        }

        static void Tick()
        {
            double t = clock.Elapsed.TotalSeconds, wait = UntilMotion(t);
            if (wait > 0) { timer.Interval = Math.Max(1000 / Fps, (int)(wait * 1000)); return; }
            timer.Interval = 1000 / Fps;
            for (int i = views.Count - 1; i >= 0; i--)
            {
                if (!views[i].view.TryGetTarget(out var c) || c.IsDisposed) { views.RemoveAt(i); continue; }
                if (!c.Visible || !c.IsHandleCreated) continue;
                if (views[i].strip > 0) c.Invalidate(new Rectangle(0, 0, views[i].strip, c.Height));
                else c.Invalidate();
            }
        }

        /// <summary>Segundos hasta la siguiente llamarada (0 = alguna en curso).</summary>
        static double UntilMotion(double t)
        {
            if (t < IgniteAt + FlareDur + .05) return 0;
            double wait = 1;   // como mucho 1 s: recoge ojos nuevos sin sondear deprisa
            List<object> stale = null;
            foreach (var kv in nextFlare)
            {
                double at = kv.Value;
                if (t >= at - .02 && t <= at + FlareDur + .05) return 0;
                if (at > t) wait = Math.Min(wait, at - t);
                else if (t > at + FlareDur + 2) (stale = stale ?? new List<object>()).Add(kv.Key);   // ojo que ya no se pinta
            }
            if (stale != null) foreach (var k in stale) nextFlare.Remove(k);
            return wait;
        }

        /// <summary>Proxima llamarada: al azar dentro de 8-16 s, sin arrancar a la vez que otra.</summary>
        static double Schedule(double from)
        {
            double at = 0;
            for (int tries = 0; tries < 60; tries++)
            {
                at = tries < 30 ? from + MinGap + rnd.NextDouble() * (MaxGap - MinGap) : at + Apart;
                bool clash = false;
                foreach (var other in nextFlare.Values) if (Math.Abs(other - at) < Apart) { clash = true; break; }
                if (!clash) break;
            }
            return at;
        }

        /// <summary>Fotograma de la llamarada del ojo 'id' (-1 = en calma): encendido inicial y luego las suyas.</summary>
        static int Frame(object id)
        {
            double t = clock.Elapsed.TotalSeconds, start;
            if (t < IgniteAt) return -1;
            if (t < IgniteAt + FlareDur) start = IgniteAt;
            else
            {
                if (!nextFlare.TryGetValue(id, out start)) { nextFlare[id] = Schedule(t); return -1; }
                if (t < start) return -1;
                if (t > start + FlareDur) { nextFlare[id] = Schedule(t); return -1; }
            }
            return Math.Min(Frames - 1, (int)((t - start) * Fps));
        }

        /// <summary>Ojo de estado en r. 'id' identifica el ojo para su ritmo propio (sin id: su posicion).</summary>
        public static void Draw(Graphics g, ScanState st, Rectangle r, object id = null, bool installed = false)
        {
            id = id ?? (r.X << 16 ^ r.Y);
            switch (st)
            {
                case ScanState.Printer: Draw(g, r, installed ? Fire.Blue : Fire.Green, Frame(id)); break;
                case ScanState.Unreachable: case ScanState.NotPrinter: Draw(g, r, Fire.Red, Frame(id)); break;
                case ScanState.Checking: Draw(g, r, Fire.Gold, -1); break;
                default: Draw(g, r, Fire.Ember, -1); break;
            }
        }

        // ---------- paletas: rampa de calor de 0 (negro) a 1 (blanco); Power escala el brillo ----------
        sealed class Fire
        {
            public Color[] Ramp; public float Power;
            public static readonly Fire Red = new Fire { Power = 1, Ramp = new[] { Color.FromArgb(20, 0, 0), Color.FromArgb(110, 8, 0), Color.FromArgb(196, 36, 4), Color.FromArgb(244, 104, 14), Color.FromArgb(255, 176, 40), Color.FromArgb(255, 226, 120), Color.FromArgb(255, 252, 228) } };
            public static readonly Fire Green = new Fire { Power = 1, Ramp = new[] { Color.FromArgb(0, 16, 6), Color.FromArgb(0, 74, 30), Color.FromArgb(10, 140, 50), Color.FromArgb(40, 200, 80), Color.FromArgb(120, 236, 96), Color.FromArgb(200, 255, 150), Color.FromArgb(240, 255, 226) } };
            public static readonly Fire Blue = new Fire { Power = 1, Ramp = new[] { Color.FromArgb(0, 4, 22), Color.FromArgb(0, 22, 96), Color.FromArgb(10, 72, 196), Color.FromArgb(40, 142, 255), Color.FromArgb(112, 200, 255), Color.FromArgb(192, 236, 255), Color.FromArgb(242, 250, 255) } };
            public static readonly Fire Gold = new Fire { Power = .7f, Ramp = new[] { Color.FromArgb(24, 16, 4), Color.FromArgb(90, 62, 18), Color.FromArgb(146, 114, 56), Color.FromArgb(201, 169, 110), Color.FromArgb(230, 204, 150), Color.FromArgb(248, 232, 196), Color.FromArgb(255, 250, 236) } };
            public static readonly Fire Ember = new Fire { Power = .45f, Ramp = new[] { Color.FromArgb(14, 14, 16), Color.FromArgb(40, 40, 44), Color.FromArgb(70, 70, 74), Color.FromArgb(100, 100, 104), Color.FromArgb(130, 130, 134), Color.FromArgb(160, 160, 164), Color.FromArgb(190, 190, 194) } };

            public Color At(float heat)
            {
                heat = Math.Max(0, Math.Min(.999f, heat)) * (Ramp.Length - 1);
                int i = (int)heat; float t = heat - i;
                return Theme.Mix(Ramp[i], Ramp[i + 1], t);
            }
        }

        // ---------- dibujo: "shader" por pixel (ruido 3D fractal), a 3x y reducido con bicubico; cache por paleta, tamaño y fotograma ----------
        const int SS = 3;
        static readonly Dictionary<(Fire, int, int), Bitmap> cache = new Dictionary<(Fire, int, int), Bitmap>();

        static void Draw(Graphics g, Rectangle r, Fire fire, int frame)
        {
            var bmp = Sprite(fire, r.Width, frame);
            g.DrawImageUnscaled(bmp, r.X + r.Width / 2 - bmp.Width / 2, r.Y + r.Height / 2 - bmp.Height / 2);
        }

        /// <summary>
        /// Ojo de Sauron: ovalo vertical de fuego con fibras radiales que fluyen hacia fuera, borde exterior incandescente,
        /// hendidura negra con filo al rojo blanco y nubes de fuego que suben alrededor. En la llamarada las nubes crecen
        /// y todo brilla mas. Transparente donde no hay fuego (se pinta sobre blanco o carbon).
        /// </summary>
        /// <summary>Fotograma de la cache (lo pinta si falta). Seguro entre hilos: la precarga va en segundo plano.</summary>
        static Bitmap Sprite(Fire fire, int size, int frame)
        {
            var key = (fire, size, frame);
            lock (cache) if (cache.TryGetValue(key, out var hit)) return hit;
            var bmp = Render(fire, size, frame);
            lock (cache)
            {
                if (cache.TryGetValue(key, out var other)) { bmp.Dispose(); return other; }
                return cache[key] = bmp;
            }
        }

        /// <summary>Precarga en segundo plano los ojos en calma (los que se ven al abrir el scanner) para estos tamaños.</summary>
        public static void Warm(params int[] sizes) => System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var size in sizes)
                foreach (var f in new[] { Fire.Green, Fire.Blue, Fire.Red, Fire.Gold, Fire.Ember })
                    try { Sprite(f, size, -1); } catch { }
        });

        static Bitmap Render(Fire f, int size, int frame)
        {
            int side = (int)Math.Ceiling(size * 2.1f) | 1, n = side * SS;
            float flare = frame < 0 ? 0 : (float)Math.Pow(Math.Sin(Math.PI * frame / (Frames - 1)), .8) * f.Power;
            float time = frame < 0 ? 0 : frame / (float)Fps * 1.6f;
            using (var big = new Bitmap(n, n, PixelFormat.Format32bppPArgb))
            {
                var data = big.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                var px = new int[n * n];
                float ax = .4f, ay = .5f;                        // ovalo casi redondo (como el original), algo mas alto que ancho
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
                        float th = (float)Math.Atan2(v, u);
                        // radio eliptico con borde irregular: el ojo no tiene contorno, se deshace en llamas
                        float edge = Fbm((float)Math.Cos(th) * 3, (float)Math.Sin(th) * 3, time * .6f + 3, 3) - .5f;
                        float re = (float)Math.Sqrt(u * u / (ax * ax) + v * v / (ay * ay)) * (1 + edge * .28f);
                        float dark = 0;   // alfa del fondo negro bajo el fuego

                        // dentro: fibras radiales que fluyen hacia fuera; brillante junto a la hendidura, rojo oscuro en medio
                        float fib = Fbm((float)Math.Cos(th) * 7, (float)Math.Sin(th) * 7, re * 2.2f - time * .9f, 4);
                        float ring = .55f + .32f * Smooth(.6f, 1f, re) + .5f * (1 - Smooth(0f, .38f, re));
                        float heatIn = ring * (.6f + .62f * fib) * (.9f + .2f * flare);

                        // fuera: corona ancha con textura de nube + nubes que suben (mas extensas en la llamarada)
                        float warp = Fbm(u * 2.1f + 3, v * 2.1f + time * .8f, time * .3f, 2) - .5f;
                        float cloud = Fbm(u * 3.2f + warp * 1.6f, v * 3.2f + time * 1.4f, time * .5f + 7, 4);
                        float spread = .9f + 1.6f * flare;
                        float bias = .75f + .5f * Smooth(-.4f, .4f, v);                               // suben mas que bajan
                        float fall = (float)Math.Exp(-Math.Max(0, re - 1) * (3.2f / spread) * bias);
                        float corona = (float)Math.Exp(-Math.Max(0, re - 1) / (.22f + .15f * flare)) * (.45f + .7f * cloud);
                        float dens = f == Fire.Ember ? 0 : Smooth(.3f, .75f, cloud) * fall * (.75f + .8f * flare);
                        if (f == Fire.Ember) corona *= .3f;
                        float heatOut = Math.Min(.86f, corona * .7f + dens * .72f);
                        float alphaOut = Math.Min(1, corona * 1.3f + dens * 1.7f);

                        // transicion suave entre ojo y llamas
                        float wIn = Smooth(1.1f, .86f, re);
                        float heat = heatOut + (heatIn - heatOut) * wIn;
                        float alpha = alphaOut + (1 - alphaOut) * wIn;
                        // fondo: penumbra circular que se funde con la pagina (el fuego brilla sobre oscuro, como el original)
                        float dist = (float)Math.Sqrt(u * u + v * v);
                        float vig = f == Fire.Ember ? 0 : .9f * Smooth(.9f, .55f, dist);
                        alpha *= Smooth(1f, .82f, dist);
                        dark = vig * (1 - alpha);

                        // hendidura: lente negra, con filo al rojo blanco que se difumina hacia el iris
                        float pw = .085f * (float)Math.Sqrt(Math.Max(0, 1 - v * v / (.46f * .46f))) * (1 - .2f * flare);
                        float d = Math.Abs(u) - pw;
                        if (pw > 0 && d < 0) { heat = 0; alpha = 1; }
                        else if (pw > 0) heat = Math.Max(heat, (float)Math.Exp(-d / .07f) * .96f);
                        heat *= .35f + .65f * f.Power;
                        var c = f.At(heat);
                        // premultiplicado: fuego (alfa) sobre fondo negro (dark)
                        float fa = Math.Max(0, Math.Min(1, alpha));
                        int a = (int)(Math.Min(1, fa + dark) * 255);
                        px[y * n + x] = a << 24 | (int)(c.R * fa) << 16 | (int)(c.G * fa) << 8 | (int)(c.B * fa);
                    }
                Marshal.Copy(px, 0, data.Scan0, px.Length);
                big.UnlockBits(data);
                var small = new Bitmap(side, side, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(big, new Rectangle(0, 0, side, side));
                }
                return small;
            }
        }

        static float Smooth(float a, float b, float x) { float t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }

        /// <summary>Ruido de valor 3D fractal (0..1), determinista: los fotogramas se pueden cachear.</summary>
        static float Fbm(float x, float y, float z, int octaves)
        {
            float sum = 0, amp = .5f, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += amp * Noise(x, y, z); norm += amp; x *= 2.03f; y *= 2.03f; z *= 2.03f; amp *= .5f; }
            return sum / norm;
        }

        static float Noise(float x, float y, float z)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), w = zf * zf * (3 - 2 * zf);
            float L(float a, float b, float t) => a + (b - a) * t;
            return L(L(L(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), u), L(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), u), v),
                     L(L(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), u), L(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), u), v), w);
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
