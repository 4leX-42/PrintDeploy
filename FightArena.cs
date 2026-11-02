using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace InstaladorImpresoras
{
    enum Phase { Intro, Fight, KO, Outro, Finish, Fatal }

    /// <summary>
    /// Escenario de la pelea en el hueco entre las dos ventanas: los bordes enfrentados son las paredes (se rebota, se agarra y se marcan
    /// con quemaduras, escarcha o sangre), el suelo es una viga encastrada en las dos ventanas (lado a lado) o el techo de la ventana de abajo.
    /// Escalera: Scorpion contra Sub-Zero, el ganador contra Mileena, luego el otro contra Mileena, y vuelta a empezar.
    /// Lleva particulas, proyectiles, cadena, columna de fuego, clon de hielo, cabezas cortadas, rondas, marcador, textos y sonido.
    /// Todo en coordenadas del escenario (X desde la pared izquierda, Y altura sobre el suelo), asi se mueve con las ventanas.
    /// </summary>
    sealed class Arena
    {
        public float L, R, Top, Floor, H, G, Clock;
        public bool Walls;
        public Rectangle WinA, WinB;            // ventana principal y escaner (luces y marcas se recortan a ellas)
        public float W => R - L;
        public Phase Phase; float pt; int round = 1; Fighter winner;
        public Fighter P1, P2;                  // izquierda y derecha
        public Fighter Destined;                // ganador sorteado de la ronda
        float timeLeft = 99;
        readonly Fighter[] roster;
        int next = 2; Fighter lastWinner;
        public Sfx Snd;
        readonly Random rng = new Random();
        float stop, slow = 1, slowFor, shake, lastL = float.NaN, lastF, oldW, oldH;
        bool started, deploy;
        float beamP = 1;                        // despliegue de la viga de ventana a ventana (0..1)

        public Arena()
        {
            roster = new[] { new Fighter(this, Who.Scorpion), new Fighter(this, Who.SubZero), new Fighter(this, Who.Mileena),
                new Fighter(this, Who.JohnnyCage), new Fighter(this, Who.Baraka), new Fighter(this, Who.Kratos) };
            Setup(roster[0], roster[1]);
        }

        void Setup(Fighter a, Fighter b) { P1 = a; P2 = b; a.Opp = b; b.Opp = a; both[0] = a; both[1] = b; }
        readonly Fighter[] both = new Fighter[2];

        public float Rnd() => (float)rng.NextDouble();
        float Rs(float a) => (Rnd() * 2 - 1) * a;
        public PointF Screen(float x, float y) => new PointF(L + x, Floor - y);
        PointF Local(PointF s) => new PointF(s.X - L, Floor - s.Y);
        Fighter[] Both => both;

        /// <summary>
        /// Compila de antemano (en segundo plano, mientras las ventanas se separan) todo el codigo de la pelea: sin esto, la primera
        /// fatality o el primer especial de cada luchador daban un tiron de unos 50 ms al compilarse en ese mismo fotograma.
        /// </summary>
        public static void Prewarm()
        {
            foreach (var t in new[] { typeof(Arena), typeof(Fighter), typeof(Sfx), typeof(FightLink) })
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    if (!m.IsAbstract && !m.ContainsGenericParameters)
                        try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle); } catch { }
        }

        /// <summary>Suelta las caches de dibujo (halos, textos, dragones, fondo) cuando la pelea deja de verse.</summary>
        public void ReleaseCaches()
        {
            foreach (var b in sizedGlows.Values) b.Dispose(); sizedGlows.Clear(); glowUsed.Clear(); sizedPixels = 0;
            foreach (var t in textPaths.Values) t.path.Dispose(); textPaths.Clear();
            foreach (var b in dragons.Values) b.Dispose(); dragons.Clear();
            bg?.Dispose(); bg = null;
        }

        /// <summary>Combate nuevo con presentacion completa (al abrir el escaner).</summary>
        public void Restart() => started = false;

        // ---------- geometria ----------
        /// <summary>Escenario segun las dos ventanas (a = principal, b = escaner); false si no hay hueco util entre ellas.</summary>
        public bool Fit(Rectangle a, Rectangle b)
        {
            float sc = Theme.Scale;
            WinA = a; WinB = b;
            int gx = Math.Max(b.Left - a.Right, a.Left - b.Right), gy = Math.Max(b.Top - a.Bottom, a.Top - b.Bottom);
            if (gx >= gy)
            {
                if (gx < (Opening ? 0 : 70 * sc) || gx > 1400 * sc) return false;
                var lw = a.Right <= b.Left ? a : b; var rw = lw == a ? b : a;
                L = lw.Right; R = rw.Left; Top = Math.Max(a.Top, b.Top); Floor = Math.Min(a.Bottom, b.Bottom);
                if (Floor - Top < 170 * sc) return false;
                Walls = true;
                H = Math.Max(48 * sc, Math.Min(170 * sc, Math.Min(W * .5f, (Floor - Top) * .28f)));   // crecen con el hueco
            }
            else
            {
                if (gy < 90 * sc || gy > 700 * sc) return false;
                var up = a.Bottom <= b.Top ? a : b; var dn = up == a ? b : a;
                Top = up.Bottom; Floor = dn.Top;
                float x0 = Math.Max(a.Left, b.Left), x1 = Math.Min(a.Right, b.Right);
                if (x1 - x0 < 200 * sc) return false;
                float c = (x0 + x1) / 2, half = Math.Min((x1 - x0) / 2, 420 * sc);
                L = c - half; R = c + half; Walls = false;
                H = Math.Max(40 * sc, Math.Min(170 * sc, Math.Min((Floor - Top) * .55f, W * .3f)));
            }
            G = 30 * H;
            Rescale();
            if (!started) { started = true; NewMatch(true); }
            return true;
        }

        /// <summary>Si el hueco cambia de tamaño, todo se recoloca en proporcion (X con el ancho, alturas con H).</summary>
        void Rescale()
        {
            if (oldW > 20 && (Math.Abs(W - oldW) > .5f || Math.Abs(H - oldH) > .5f))
            {
                float kx = W / oldW, ky = H / oldH;
                foreach (var f in Both) { f.X *= kx; f.Y *= ky; f.VX *= ky; f.VY *= ky; }
                for (int i = 0; i < np; i++) { parts[i].X *= kx; parts[i].Y *= ky; parts[i].Size *= ky; }
                foreach (var b in balls) { b.X *= kx; b.Y *= ky; b.VX *= ky; }
                foreach (var p in pillars) p.X *= kx;
                foreach (var hd in heads) { hd.X *= kx; hd.Y *= ky; }
                if (clone != null) clone.X *= kx;
                if (puddle != null) { puddle.X *= kx; puddle.Wd *= ky; }
                if (chain != null) { chain.X *= kx; chain.X0 *= kx; chain.Y *= ky; }
                for (int i = 0; i < stains.Count; i++) { var s = stains[i]; s.X *= kx; s.R *= ky; stains[i] = s; }
            }
            oldW = W; oldH = H;
        }

        /// <summary>Zona de dibujo en pantalla: el escenario, un margen sobre cada ventana y sitio para los textos.</summary>
        public Rectangle Bounds
        {
            get
            {
                float sc = Theme.Scale, mx = Math.Max(70 * sc, (380 * sc - W) / 2);
                return Rectangle.FromLTRB((int)(L - mx), (int)(Top - 6 * sc), (int)(R + mx), (int)(Floor + 34 * sc));
            }
        }

        // ---------- simulacion ----------
        public void Step(float dt)
        {
            dt = Math.Min(dt, .1f);
            StageMotion(dt);
            shake = Math.Max(0, shake - dt * 30);
            if (stop > 0) { stop -= dt; return; }
            if (slowFor > 0) { slowFor -= dt; if (slowFor <= 0) slow = 1; }
            float sdt = dt * slow;
            int n = Math.Max(1, (int)Math.Ceiling(sdt / (1 / 60f)));
            for (int i = 0; i < n; i++) Tick(sdt / n);
        }

        /// <summary>Las ventanas se mueven: si el suelo pega un tiron, los luchadores saltan; si todo se desplaza de golpe, se tambalean.</summary>
        void StageMotion(float dt)
        {
            if (float.IsNaN(lastL) || dt <= 0) { lastL = L; lastF = Floor; return; }
            float vx = (L - lastL) / dt, vy = (Floor - lastF) / dt, sc = Theme.Scale;
            lastL = L; lastF = Floor;
            foreach (var f in Both)
            {
                if (f.Busy || f.State == St.Cling || f.State == St.Thrown || f.State == St.Pulled) continue;
                if (Math.Abs(vy) > 650 * sc && f.Grounded) f.VY = Math.Min(6, Math.Abs(vy) / (180 * sc)) * H;
                if (Math.Abs(vx) > 900 * sc && f.Grounded) f.VX -= Math.Max(-4 * H, Math.Min(4 * H, vx * .15f));
            }
        }

        void Tick(float h)
        {
            Clock += h; pt += h;
            // quien actua primero se sortea cada paso: sin ventaja para el de la izquierda en los golpes simultaneos
            var (fa, fb) = Rnd() < .5f ? (P1, P2) : (P2, P1);
            fa.Update(h); fb.Update(h);
            Separate();
            P1.Solve(); P2.Solve();
            P1.RecordTrail(); P2.RecordTrail();
            P1.Cloth(h); P2.Cloth(h);
            fa.Strike(); fb.Strike();
            UpdateChain(h); UpdateBalls(h); UpdatePillars(h); UpdateClone(h); UpdatePuddle(h); UpdateHeads(h); Bleed(h); UpdateSpikes(h); Torches(h);
            UpdateParticles(h);
            for (int i = rings.Count - 1; i >= 0; i--) { var r = rings[i]; r.Life -= h; if (r.Life <= 0) rings.RemoveAt(i); else rings[i] = r; }
            for (int i = ghosts.Count - 1; i >= 0; i--) { ghosts[i].Life -= h; if (ghosts[i].Life <= 0) ghosts.RemoveAt(i); }
            for (int i = flashes.Count - 1; i >= 0; i--) { var f = flashes[i]; f.Life -= h; if (f.Life <= 0) flashes.RemoveAt(i); else flashes[i] = f; }
            for (int i = marks.Count - 1; i >= 0; i--) { var m = marks[i]; m.Life -= h; if (m.Life <= 0) marks.RemoveAt(i); else marks[i] = m; }
            for (int i = stains.Count - 1; i >= 0; i--) { var s = stains[i]; s.Life -= h; if (s.Life <= 0) stains.RemoveAt(i); else stains[i] = s; }
            if (callT > 0) callT -= h;
            bannerT += h;
            Ambient(h);
            Memes(h);
            Rounds(h);
        }

        void Separate()
        {
            if (P1.Busy || P2.Busy || P1.State == St.Thrown || P2.State == St.Thrown || P1.State == St.Pulled || P2.State == St.Pulled) return;
            if (P1.Y > .5f * H || P2.Y > .5f * H) return;
            float d = P2.X - P1.X, min = .32f * H;
            if (Math.Abs(d) >= min) return;
            float push = (min - Math.Abs(d)) / 2 * (d >= 0 ? 1 : -1);
            P1.X -= push; P2.X += push;
        }

        // ---------- rondas y escalera ----------
        void NewMatch(bool full = false)
        {
            // escalera: Scorpion contra Sub-Zero; luego el que gana se queda y entra el siguiente de la plantilla
            if (full) { Setup(roster[0], roster[1]); next = 2; }
            else if (lastWinner != null)
            {
                var ch = roster[next % roster.Length];
                if (ch == lastWinner) { next++; ch = roster[next % roster.Length]; }
                next++;
                Setup(lastWinner, ch);
            }
            round = 1; P1.Wins = P2.Wins = 0; deploy = full;
            StartRound();
        }

        void StartRound()
        {
            P1.Reset(Math.Max(.3f * H, W * .2f), 1); P2.Reset(Math.Min(W - .3f * H, W * .8f), -1);
            P1.Fade = P2.Fade = 0;
            // ganador de la ronda sorteado al 50 % (sin importar personaje ni lado): pega algo mas y el otro no puede tumbarlo,
            // asi que si va perdiendo aguanta con 1 de vida y remonta
            Destined = Rnd() < .5f ? P1 : P2; timeLeft = 99;
            Destined.Form = 1.15f; Destined.Opp.Form = .9f;
            chain = null; balls.Clear(); pillars.Clear(); clone = null; puddle = null;
            heads.Clear(); stains.Clear(); bleeder = null; cutBody = null; ghosts.Clear(); memeUsed.Clear(); combo = 0; spikes.Clear(); rings.Clear();
            Phase = Phase.Intro; pt = 0; slow = 1; banner = null;
            beamP = deploy && Walls ? 0 : 1;
        }

        bool RootLeft => WinA.Right <= WinB.Left + 1;   // la ventana principal esta a la izquierda

        void Rounds(float h)
        {
            if (Phase == Phase.Fight && (timeLeft -= h) <= 0) { Banner("TIME", .9f); KO(Destined.Opp); Meme("enrique", 1, .6f, true); }   // la madre llamando a casa
            switch (Phase)
            {
                case Phase.Intro:
                    {
                        if (Opening) { OpeningFx(); pt = 0; break; }
                        // presentacion completa (al abrir el escaner): la viga sale de la ventana principal hasta el escaner, aparece el
                        // primero, luego el segundo, ROUND 1 y FIGHT; unos 5 s. Entre rondas y combates, la version corta.
                        bool newcomer = round == 1;
                        float tA = deploy ? 1.5f : .05f, tB = deploy ? 2.2f : newcomer ? .7f : .3f, tR = deploy ? 3 : newcomer ? 1.3f : .2f, tF = tR + 1.3f, tS = tF + .7f;
                        if (beamP < 1)
                        {
                            float u = Math.Min(1, pt / 1.3f); beamP = u * u * (3 - 2 * u);
                            float tip = RootLeft ? W * beamP : W * (1 - beamP);
                            for (int k = 0; k < 2; k++) Emit(Pk.Spark, tip, Rnd() * .05f * H, (RootLeft ? 1 : -1) * Rnd() * 3 * H, (1 + Rnd() * 3) * H, .25f, 0);
                            if (beamP >= 1) { Sparks(RootLeft ? W : 0, .1f * H, 14, Color.FromArgb(255, 230, 160)); Kick(2); Snd?.Play("splat", .5f); }
                        }
                        foreach (var f in Both)
                        {
                            float at = f == P1 ? tA : tB;
                            if (pt < at) continue;
                            if (f.Fade == 0) Appear(f);
                            f.Fade = Math.Min(1, f.Fade + h / .5f);
                        }
                        if (pt >= tR && banner == null) Banner("ROUND " + round, 1.2f);
                        if (pt >= tF && banner != "FIGHT!")
                        {
                            Banner("FIGHT!", .7f); Meme("alataque|tevoyamatar", .5f, .8f);
                            Snd?.Cut("theme"); Snd?.Loop("music", .45f);   // musica de fondo, baja, mientras dure la pelea
                        }
                        if (pt >= tS) { Phase = Phase.Fight; pt = 0; deploy = false; }
                        break;
                    }
                case Phase.KO:
                    if (pt > 1.3f)
                    {
                        var loser = winner.Opp;
                        if (winner.Wins >= 2)
                        {
                            Phase = Phase.Finish; pt = 0; winner.Fatal = true; Banner(loser.Who == Who.Mileena ? "FINISH HER!" : "FINISH HIM!", 1.6f); loser.Hp = 0;
                            if (Rnd() < .4f && Meme("kager", 1, 1.8f, true)) memeLater.Add(("fart", Clock + 2.8f));   // el aturdido, a punto de morir, con apretones
                        }
                        else
                        {
                            Phase = Phase.Outro; pt = 0; winner.Win(); Banner(winner.Name + " WINS", 2.2f);
                            if (winner.Hp >= 99.5f) { Sub("FLAWLESS VICTORY"); Meme("sadviolin", 1, 1.6f, true); }
                            else if (winner.Who == Who.JohnnyCage) Meme("cagewin", 1, 1.6f, true);
                            else if (winner.Who == Who.Mileena) Meme("goodboy", 1, 1.6f, true);
                            else Meme("siuu", .6f, 1.4f);   // celebracion de la ronda
                        }
                    }
                    break;
                case Phase.Outro: if (pt > 2.6f) { round++; StartRound(); } break;
                case Phase.Finish:
                    if (pt > 14) { winner.Opp.Gone(); Fatality(winner); }   // por si el remate no llega a completarse
                    break;
                case Phase.Fatal:
                    if (pt > 1.9f && banner == "FATALITY")
                    {
                        Banner(winner.Name + " WINS", 2.4f);
                        if (vivaLeft > 0 && Rnd() < .3f) { vivaLeft--; Meme("vivaespana", 1, 1.5f, true); }   // muy de vez en cuando: dos veces por sesion como mucho
                        else Meme(winner.Who == Who.JohnnyCage ? "cagewin" : winner.Who == Who.Mileena ? "goodboy" : "lucas|alamierda|siuu", 1, 1.5f, true);
                    }
                    if (pt > 4.8f) { lastWinner = winner; NewMatch(); }
                    break;
            }
        }

        public void KO(Fighter loser)
        {
            if (Phase != Phase.Fight) return;
            winner = loser.Opp; winner.Wins++;
            if (!loser.Dead && loser.State != St.AirHit && loser.State != St.Down) loser.Dizzy(0);   // fin por tiempo: se queda aturdido
            Phase = Phase.KO; pt = 0; slow = .3f; slowFor = .7f; Kick(6);
            chain = null; balls.Clear();
            if (loser.Who == Who.Kratos || loser.Who == Who.Baraka) Meme("calvo", .7f, .6f);
            else if (winner.Hp > 75) Meme("nelson|vetealamierda", .8f, 1.1f);
            else Meme("fortnite", .7f, .5f);
            Snd?.Play("splat");
        }

        public void Fatality(Fighter w)
        {
            if (Phase == Phase.Fatal) return;
            Phase = Phase.Fatal; pt = 0; w.Fatal = false;
            Banner("FATALITY", 1.9f); Kick(8);
            flashes.Add(new Flash { X = W / 2, Y = H, C = w.Look.Rim, R = 4 * H, Life = 1.2f, Max = 1.2f });
        }

        void Appear(Fighter f)
        {
            if (f.Who == Who.Scorpion) Burst(f, false);
            else if (f.Who == Who.SubZero) FrostBurst(f);
            else if (f.Who == Who.Mileena) { PinkBurst(f); if (round == 1) Meme("drybones", 1, .15f, true); }   // su entrada en cada combate
            else if (f.Who == Who.JohnnyCage) { for (int i = 0; i < 6; i++) Ghost(f); flashes.Add(new Flash { X = f.X, Y = H * .5f, C = f.Look.Rim, R = 2 * H, Life = .5f, Max = .5f }); Snd?.Play("teleport"); }
            else if (f.Who == Who.Baraka) { BladeSparks(f); BladeSparks(f); Snd?.Play("block"); }
            else { Burst(f, false); Snd?.Play("kratos_intro"); }
        }

        /// <summary>Estela de fuego (Kratos): brasas y llamitas en un punto del cuerpo.</summary>
        public void Trail(Fighter f, PointF at, Color c)
        {
            var p = Local(at);
            if (c.R > 200 && c.B > 150) Emit(Pk.Petal, p.X, p.Y, Rs(.4f * H), Rnd() * .6f * H, .5f, .02f * H);
            else Trail(f, at);
        }

        public void Trail(Fighter f, PointF at)
        {
            var p = Local(at);
            Emit(Pk.Fire, p.X, p.Y, Rs(.6f * H), Rnd() * H, .25f, .05f * H);
            if (Rnd() < .5f) Emit(Pk.Ember, p.X, p.Y, Rs(H), Rnd() * H, .5f, .02f * H);
        }

        // ---------- apertura: las ventanas se separan entre fuego y humo ----------
        public bool Opening { get; private set; }
        public void BeginOpening() { Opening = true; started = false; }
        public void EndOpening() { if (!Opening) return; Opening = false; StartRound(); Kick(1.5f); }

        /// <summary>Durante la apertura: pocas brasas que suben despacio desde los bordes y algun hilo de humo abajo; el brillo lo pone Seams.</summary>
        void OpeningFx()
        {
            float hgt = Math.Max(H, Floor - Top - .3f * H);
            foreach (var side in new[] { 0f, W })
            {
                int dir = side == 0 ? 1 : -1;
                if (Rnd() < .22f) Emit(Pk.Ember, side + dir * Rnd() * .05f * H, Rnd() * hgt, dir * (.2f + Rnd() * .6f) * H, (.3f + Rnd() * .8f) * H, 1.4f, (.01f + Rnd() * .01f) * H);
                if (Rnd() < .05f) Emit(Pk.Smoke, side + dir * .1f * H, Rnd() * .4f * H, dir * Rnd() * .4f * H, (.2f + Rnd() * .4f) * H, 1.6f, .1f * H);
            }
        }

        /// <summary>Bordes enfrentados de las ventanas encendidos como metal al rojo: filo fino que late, con halo suave hacia el hueco.</summary>
        void Seams(Graphics g)
        {
            float sc = Theme.Scale, k = .75f + .25f * (float)Math.Sin(Clock * 7), y0 = Top, y1 = Floor;
            foreach (var x in new[] { L, R })
            {
                int dir = x == L ? 1 : -1; float halo = 16 * sc;
                var rc = dir > 0 ? new RectangleF(x, y0, halo, y1 - y0) : new RectangleF(x - halo, y0, halo, y1 - y0);
                using (var br = Fighter.Grad(rc, Color.FromArgb((int)(dir > 0 ? 70 * k : 0), 255, 130, 40), Color.FromArgb((int)(dir > 0 ? 0 : 70 * k), 255, 130, 40), 0f))
                    g.FillRectangle(br, rc);
                using (var br = Fighter.Grad(new PointF(0, y0), new PointF(0, y1), Color.FromArgb((int)(120 * k), 255, 200, 120), Color.FromArgb((int)(230 * k), 255, 236, 180)))
                using (var pen = new Pen(br, Math.Max(1f, 1.4f * sc)))
                    g.DrawLine(pen, x, y0, x, y1);
            }
        }

        // ---------- seleccion directa (menu de caras) ----------
        public IReadOnlyList<Fighter> Roster => roster;

        /// <summary>El elegido entra ya como jugador 1 (izquierda) o 2 (derecha); si estaba en el otro lado, cambian de sitio.</summary>
        public void Pick(Who w, int slot)
        {
            var f = Array.Find(roster, x => x.Who == w);
            if (f == null) return;
            Fighter a = P1, b = P2;
            if (slot == 1) { if (f == a) return; if (f == b) b = a; a = f; }
            else { if (f == b) return; if (f == a) a = b; b = f; }
            Setup(a, b);
            round = 1; P1.Wins = P2.Wins = 0; deploy = false; lastWinner = null;
            StartRound();
        }

        /// <summary>Estela verde de Johnny Cage: copia del cuerpo que se desvanece.</summary>
        public void Ghost(Fighter f)
        {
            if (ghosts.Count > 14 || f.Hidden) return;
            ghosts.Add(new Ghost0 { J = (PointF[])f.J.Clone(), Pose = (float[])f.Pose.Clone(), Face = f.Face, Owner = f, Life = .3f, X0 = L, F0 = Floor });
        }
        sealed class Ghost0 { public PointF[] J; public float[] Pose; public int Face; public Fighter Owner; public float Life, X0, F0; }
        readonly List<Ghost0> ghosts = new List<Ghost0>();

        /// <summary>Chispas de las cuchillas de Baraka.</summary>
        public void BladeSparks(Fighter f)
        {
            var p = Local(f.J[Rnd() < .5f ? 6 : 9]);
            Sparks(p.X + f.Face * .2f * H, p.Y, 2, Color.White);
        }

        // ---------- golpes ----------
        public void Hit(Fighter a, Fighter d, PointF at, Move m)
        {
            bool facing = d.Face == (a.X >= d.X ? 1 : -1);
            var p = Local(at);
            if (d.State == St.Block && facing && (m.Kind != Kind.Low || d.Crouch) && !(m.Kind == Kind.High && d.Crouch))
            {
                Sparks(p.X, p.Y, 8, Color.FromArgb(255, 220, 140));
                d.VX = -d.Face * 2.5f * H; d.Timer = Math.Max(d.Timer, d.T + .12f);
                combo = 0;
                stop = .04f; Kick(1); Snd?.Play("block");
                return;
            }
            if (m.Kind == Kind.High && d.Crouch) return;
            d.TakeHit(a, m);
            if (comboBy == a && comboOn == d && Clock - comboAt < 1.3f) combo++; else { combo = 1; comboBy = a; comboOn = d; }
            comboAt = Clock;
            if (combo == 3 && !Meme("lacasitos", .85f)) Meme("quehaces|lamadre", .8f);   // pin, pan, toma lacasitos
            if (m.Id == "sweep") Meme("oof", .5f, .15f);
            if ((m.Launch || m.Knock) && m.Dmg >= 10 && Rnd() < .25f) Crack(.8f);
            Sparks(p.X, p.Y, 6 + m.Dmg / 2, Color.FromArgb(255, 240, 200));
            Blood(p.X, p.Y, 4 + m.Dmg, a.Face);
            for (int i = 0; i < 4; i++)
                if (a.Who == Who.Scorpion) Emit(Pk.Ember, p.X, p.Y, Rs(3 * H), Rs(3 * H), .5f, .03f * H);
                else if (a.Who == Who.SubZero) Emit(Pk.Shard, p.X, p.Y, Rs(3 * H), Rnd() * 3 * H, .6f, .04f * H);
            flashes.Add(new Flash { X = p.X, Y = p.Y, C = Color.White, R = 1.4f * H, Life = .12f, Max = .12f });
            if (m.Dmg >= 9 || m.Launch || m.Knock) rings.Add(new Ring { X = p.X, Y = p.Y, Life = .22f, Max = .22f, R = (.5f + m.Dmg * .05f) * H, C = a.Look.Rim });
            stop = .045f + m.Dmg * .005f; Kick(1 + m.Dmg * .35f);
            Snd?.Play(m.Launch || m.Knock ? "heavyhit" : m.Limb == 12 || m.Limb == 13 ? "kick" : "punch");
        }

        void Blood(float x, float y, int n, int dir)
        {
            for (int i = 0; i < n; i++)
                Emit(Pk.Blood, x, y, dir * (.5f + Rnd() * 3.5f) * H + Rs(H), (Rnd() * 3.5f) * H, 1.4f, (.012f + Rnd() * .018f) * H);
        }

        public void WallSplat(Fighter f, int side)
        {
            float y = f.Y + .5f * H, x = side < 0 ? 0 : W;
            for (int i = 0; i < 14; i++) Emit(Pk.Debris, x, y, -side * Rnd() * 3 * H, Rs(3 * H), .8f, .03f * H);
            Sparks(x, y, 10, Color.FromArgb(255, 235, 190));
            Blood(x, y, 10, -side);
            int kind = f.Opp.Who == Who.Scorpion ? 0 : f.Opp.Who == Who.SubZero ? 1 : 2;   // la marca es del que golpea
            marks.Add(new Mark { Side = side < 0 ? 0 : 1, P = y, Kind = kind, Life = 9, Seed = rng.Next() });
            marks.Add(new Mark { Side = side < 0 ? 0 : 1, P = y - .1f * H, Kind = 2, Life = 10, Seed = rng.Next() });
            flashes.Add(new Flash { X = x, Y = y, C = f.Opp.Look.Rim, R = 2 * H, Life = .4f, Max = .4f });
            f.Hp = Math.Max(f.Dead ? 0 : 1, f.Hp - 3);
            stop = .07f; Kick(5); Snd?.Play("splat"); Crack(.9f);
            Meme("pipe|hostia", .6f);
        }

        public void WallDust(Fighter f, int side)
        {
            float x = side < 0 ? 0 : W;
            for (int i = 0; i < 6; i++) Emit(Pk.Debris, x, f.Y + .4f * H, -side * Rnd() * 1.5f * H, Rs(1.5f * H), .5f, .02f * H);
        }

        public void Dust(float x, int n) { for (int i = 0; i < n; i++) Emit(Pk.Smoke, x + Rs(.2f * H), .02f * H, Rs(1.5f * H), Rnd() * .6f * H, .5f, .08f * H); }

        public void Kick(float s) => shake = Math.Max(shake, s * Theme.Scale);

        public void Sound(string id, float vol = 1) => Snd?.Play(id, vol);

        /// <summary>Crujido de hueso en los golpes gordos; como mucho uno cada 2,5 s para que no canse.</summary>
        public void Crack(float vol = 1)
        {
            if (crackCool > 0) return;
            crackCool = 2.5f; Snd?.Play("bonecrack", vol);
        }
        float crackCool;

        // ---------- memes: suenan en su jugada (no al azar), uno cada 5 s como mucho y cada uno una vez por ronda ----------
        float memeCool;
        readonly HashSet<string> memeUsed = new HashSet<string>();
        static int vivaLeft = 2;
        readonly List<(string id, float at)> memeLater = new List<(string, float)>();
        Fighter comboBy, comboOn; int combo; float comboAt;

        public bool Meme(string id, float chance = .7f, float delay = 0, bool force = false)
        {
            if (Snd == null) return false;
            if (!force && (memeCool > 0 || memeUsed.Contains(id) || Rnd() > chance)) return false;
            memeUsed.Add(id); memeCool = 5;
            var opts = id.Split('|'); var pick = opts[rng.Next(opts.Length)];   // "a|b": una de las dos, para no repetir siempre la misma
            if (delay > 0) memeLater.Add((pick, Clock + delay)); else Snd.Play("meme_" + pick);
            return true;
        }

        void Memes(float h)
        {
            memeCool -= h; crackCool -= h;
            for (int i = memeLater.Count - 1; i >= 0; i--)
                if (Clock >= memeLater[i].at) { Snd?.Play("meme_" + memeLater[i].id); memeLater.RemoveAt(i); }
        }

        /// <summary>¿Le viene encima un proyectil, la cadena o una columna de fuego a punto de salir?</summary>
        public bool Threat(Fighter f)
        {
            foreach (var b in balls) if (b.Owner != f && Math.Sign(f.X - b.X) == Math.Sign(b.VX) && Math.Abs(f.X - b.X) < 3 * H) return true;
            foreach (var p in pillars) if (p.Owner != f && p.T < .4f && Math.Abs(p.X - f.X) < .5f * H) return true;
            return chain != null && chain.Owner != f && chain.Phase == 0 && Math.Abs(f.X - chain.X) < 2.2f * H;
        }

        // ---------- cadena (lanza con kunai) ----------
        sealed class Chain { public Fighter Owner, Victim; public float X, Y, X0; public int Dir, Phase; }
        Chain chain;

        public void ThrowChain(Fighter f)
        {
            var p = Local(f.J[6]);
            Snd?.Play("chain", .8f);
            chain = new Chain { Owner = f, X = p.X, Y = p.Y, X0 = p.X, Dir = f.Face };
            Snd?.Play("chain");
        }

        void UpdateChain(float h)
        {
            var c = chain; if (c == null) return;
            var o = c.Owner;
            if (o.State != St.Act || o.Mv == null || o.Mv.Id != "spear") { if (c.Victim != null && c.Victim.State == St.Pulled) c.Victim.Dizzy(.3f); chain = null; return; }
            var hand = Local(o.J[6]);
            if (c.Phase == 0)
            {
                c.X += c.Dir * 17 * H * h;
                c.Y += (Math.Max(.4f * H, o.Opp.Y + .6f * H) - c.Y) * Math.Min(1, h * 6);
                var tip = Screen(c.X, c.Y);
                if (CloneHit(o, tip, .05f * H)) { chain = null; o.Freeze(1.3f); return; }
                var v = o.Opp;
                if (v.Hittable && RectangleF.Inflate(v.Box, .04f * H, .04f * H).Contains(tip))
                {
                    if (v.State == St.Block && v.Face == -c.Dir) { Sparks(c.X, c.Y, 10, Color.FromArgb(255, 220, 140)); c.Phase = 2; Snd?.Play("block"); return; }
                    c.Victim = v; c.Phase = 1; v.Pulled(); if (Phase == Phase.Fight) v.Hp = Math.Max(1, v.Hp - 4);
                    o.Play(new[] { new Key(Poses.Pull, .1f) });
                    Sparks(c.X, c.Y, 8, Color.FromArgb(255, 230, 160)); Blood(c.X, c.Y, 8, -c.Dir); Kick(3); stop = .06f;
                    Snd?.Play("spearhit"); Snd?.Play(v.Key + "_hit", .8f);
                    return;
                }
                if (c.X < 0 || c.X > W || Math.Abs(c.X - c.X0) > 4.5f * H) c.Phase = 2;
            }
            else if (c.Phase == 1)
            {
                var v = c.Victim; float target = o.X + o.Face * .6f * H;
                float step = 10 * H * h, dx = target - v.X;
                v.X += Math.Abs(dx) < step ? dx : Math.Sign(dx) * step;
                v.Y += (0 - v.Y) * Math.Min(1, h * 10);
                c.X = v.X; c.Y = v.Y + .55f * H;
                if (Math.Abs(target - v.X) < 1)
                {
                    v.Y = 0; v.Dizzy(Phase == Phase.Finish ? 0 : .8f);
                    chain = null; o.EndMove();
                    if (Phase == Phase.Fight) o.Queued = rng.Next(3) switch { 0 => "uppercut", 1 => "highkick", _ => "jab" };
                }
            }
            else
            {
                float dx = hand.X - c.X, dy = hand.Y - c.Y, l = (float)Math.Sqrt(dx * dx + dy * dy), step = 22 * H * h;
                if (l <= step) { chain = null; o.EndMove(); return; }
                c.X += dx / l * step; c.Y += dy / l * step;
            }
        }

        // ---------- proyectiles: bola de hielo y sai ----------
        sealed class Ball { public Fighter Owner; public float X, Y, VX, VY, T; public bool Sai, Back; public int Kind; }
        readonly List<Ball> balls = new List<Ball>();
        static readonly Move SaiHit = new Move { Id = "saihit", Dmg = 7, KbX = 2, Stun = .35f, Kind = Kind.Mid };

        public void IceBall(Fighter f)
        {
            var p = Local(f.J[6]);
            balls.Add(new Ball { Owner = f, X = p.X, Y = Math.Max(.35f * H, p.Y), VX = f.Face * 8 * H });
            Snd?.Play("ice", .7f);
        }

        public void Shot(Fighter f, int kind)
        {
            var p = Local(f.J[6]);
            balls.Add(new Ball { Owner = f, X = p.X, Y = Math.Max(.4f * H, p.Y), VX = f.Face * (kind == 2 ? 9 : kind == 4 ? 13 : 11) * H, Sai = true, Kind = kind });
            Snd?.Play(kind == 2 ? "teleport" : "swing");
        }

        struct Spike { public float X, T, Delay; public Fighter Owner; }
        readonly List<Spike> spikes = new List<Spike>();
        bool quakeHit;

        /// <summary>Martillo de hielo contra el suelo: seis estacas salen en fila hacia el rival; la primera que lo pilla lo lanza.</summary>
        public void IceQuake(Fighter f)
        {
            quakeHit = false;
            for (int k = 0; k < 6; k++) spikes.Add(new Spike { X = f.X + f.Face * (.45f + k * .3f) * H, Delay = k * .05f, Owner = f });
            Shards(f.X + f.Face * .4f * H, .05f * H, 14); Kick(5); Snd?.Play("shatter"); Snd?.Play("heavyhit", .7f);
            rings.Add(new Ring { X = f.X + f.Face * .4f * H, Y = .05f * H, Life = .35f, Max = .35f, R = 1.4f * H, C = Color.FromArgb(170, 230, 255) });
        }

        void UpdateSpikes(float h)
        {
            for (int i = spikes.Count - 1; i >= 0; i--)
            {
                var sp = spikes[i]; sp.T += h; spikes[i] = sp;
                float t = sp.T - sp.Delay;
                if (t > .7f || sp.X < 0 || sp.X > W) { spikes.RemoveAt(i); continue; }
                if (t > 0 && t < .05f && Rnd() < .5f) Shards(sp.X, .1f * H, 3);
                var v = sp.Owner.Opp;
                if (!quakeHit && t > 0 && t < .35f && v.Hittable && v.Y < .6f * H && Math.Abs(v.X - sp.X) < .24f * H)
                { quakeHit = true; v.Launch(11, sp.Owner.Face * 1.5f * H, 8.5f * H); Kick(4); Snd?.Play("heavyhit"); Blood(sp.X, .5f * H, 6, sp.Owner.Face); }
            }
        }

        void DrawSpikes(Graphics g)
        {
            foreach (var sp in spikes)
            {
                float t = sp.T - sp.Delay; if (t <= 0) continue;
                float grow = Math.Min(1, t / .06f), sink = t > .45f ? 1 - (t - .45f) / .25f : 1, hgt = .75f * H * grow * Math.Max(0, sink), w = .13f * H;
                var c = Screen(sp.X, 0);
                var tri = new[] { new PointF(c.X - w, c.Y), new PointF(c.X + w * .2f, c.Y - hgt), new PointF(c.X + w, c.Y) };
                if (hgt < 1) continue;
                using (var br = Fighter.Grad(new PointF(c.X - w, c.Y), new PointF(c.X + w, c.Y - hgt), Color.FromArgb(230, 230, 248, 255), Color.FromArgb(200, 70, 140, 210))) g.FillPolygon(br, tri);
                using (var pen = new Pen(Color.FromArgb(230, 245, 252, 255), Math.Max(1f, .01f * H))) g.DrawPolygon(pen, tri);
            }
        }

        /// <summary>Hacha Leviatan lanzada: gira hacia el rival, congela al que toca y vuelve sola a la mano de Kratos.</summary>
        public void ThrowAxe(Fighter f)
        {
            var p = Local(f.J[6]);
            balls.Add(new Ball { Owner = f, X = p.X, Y = Math.Max(.5f * H, p.Y), VX = f.Face * 10 * H, Sai = true, Kind = 5 });
            Snd?.Play("swing"); Snd?.Play("teleport", .5f);
        }

        public void FrostSpark(PointF at) { var p = Local(at); Emit(Pk.Shard, p.X + Rs(.1f * H), p.Y + Rs(.1f * H), Rs(H), Rnd() * H, .4f, .02f * H); }

        public void FrostBurstAt(PointF at)
        {
            var p = Local(at);
            for (int i = 0; i < 10; i++) Emit(Pk.Shard, p.X, p.Y, Rs(2 * H), Rs(2 * H), .5f, .025f * H);
            for (int i = 0; i < 6; i++) Emit(Pk.Mist, p.X, p.Y, Rs(H), Rs(H), .5f, .08f * H);
            flashes.Add(new Flash { X = p.X, Y = p.Y, C = Color.FromArgb(140, 220, 255), R = 1.6f * H, Life = .35f, Max = .35f });
            Snd?.Play("shatter", .5f);
        }

        void UpdateAxe(Ball b, int i, float h)
        {
            var o = b.Owner;
            if (Rnd() < .5f) Emit(Pk.Mist, b.X, b.Y, Rs(.3f * H), Rs(.3f * H), .3f, .05f * H);
            if (!b.Back)
            {
                var v = o.Opp; var s = Screen(b.X, b.Y);
                if (v.Hittable && RectangleF.Inflate(v.Box, .08f * H, .08f * H).Contains(s))
                {
                    b.Back = true;
                    if (v.State == St.Block && v.Face == -Math.Sign(b.VX)) { Sparks(b.X, b.Y, 10, Color.White); Snd?.Play("block"); return; }
                    v.TakeHit(o, SaiHit); v.Freeze(.7f); Blood(b.X, b.Y, 8, Math.Sign(b.VX)); Kick(4); Snd?.Play("heavyhit");
                    return;
                }
                if (b.X < 0 || b.X > W || b.T > 1.2f) { b.Back = true; if (Walls) Sparks(Math.Max(0, Math.Min(W, b.X)), b.Y, 8, Color.FromArgb(200, 230, 255)); }
                return;
            }
            // vuelve a la mano
            var hand = Local(o.J[6]);
            float dx = hand.X - b.X, dy = hand.Y - b.Y, l = (float)Math.Sqrt(dx * dx + dy * dy), step = 14 * H * h;
            if (l <= step || o.State == St.Gone)
            {
                balls.RemoveAt(i);
                o.AxeOut = false;
                if (o.Who == Who.Kratos && o.State != St.Gone) { if (o.Blade == 0) o.Blade = 5; Snd?.Play("block", .7f); Kick(1.5f); FrostBurstAt(o.J[6]); Meme("obrigado", .35f, .15f); }
                return;
            }
            b.X += dx / l * step; b.Y += dy / l * step; b.VX = Math.Sign(dx) * 10 * H;
        }

        public void Sai(Fighter f, bool air = false)
        {
            var p = Local(f.J[6]);
            balls.Add(new Ball { Owner = f, X = p.X, Y = Math.Max(.4f * H, p.Y), VX = f.Face * (air ? 9 : 12) * H, VY = air ? -8 * H : 0, Sai = true });
            Snd?.Play("swing");
        }

        void UpdateBalls(float h)
        {
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                if (i >= balls.Count) continue;   // un impacto que da el KO vacia la lista a mitad del recorrido
                var b = balls[i];
                if (b.Kind == 5) { b.T += h; if (!b.Back) b.X += b.VX * h; UpdateAxe(b, i, h); continue; }
                b.X += b.VX * h; b.Y += b.VY * h; b.T += h;
                if (b.Y <= .02f * H) { balls.RemoveAt(i); Sparks(b.X, 0, 8, Color.White); Dust(b.X, 3); Snd?.Play("block", .5f); continue; }   // se clava en el suelo
                if (b.Kind == 4) Emit(Pk.Fire, b.X - Math.Sign(b.VX) * .15f * H, b.Y, -b.VX * .1f, Rnd() * H, .25f, .05f * H);
                if (!b.Sai && Rnd() < .7f) Emit(Pk.Mist, b.X, b.Y + Rs(.05f * H), -b.VX * .1f, Rs(.3f * H), .45f, .09f * H);
                var v = b.Owner.Opp; var s = Screen(b.X, b.Y);
                if (v.Hittable && RectangleF.Inflate(v.Box, .06f * H, .06f * H).Contains(s))
                {
                    balls.RemoveAt(i);
                    if (v.State == St.Block && v.Face == -Math.Sign(b.VX)) { if (b.Sai) Sparks(b.X, b.Y, 8, Color.White); else Shards(b.X, b.Y, 14); Snd?.Play("block"); Meme("mecago|mecagoleche", .4f); continue; }
                    if (b.Sai) { v.TakeHit(b.Owner, SaiHit); Blood(b.X, b.Y, b.Kind == 2 ? 4 : 12, Math.Sign(b.VX)); Kick(3); Snd?.Play("punch"); }
                    else { v.Hp = Math.Max(1, v.Hp - 4); v.Freeze(1.4f); Kick(2); Meme("winerror", .6f, .25f); }
                    continue;
                }
                if (b.X < 0 || b.X > W)
                {
                    balls.RemoveAt(i);
                    if (b.Sai) Sparks(Math.Max(0, Math.Min(W, b.X)), b.Y, 8, Color.White);
                    else Shards(Math.Max(0, Math.Min(W, b.X)), b.Y, 16);
                    if (Walls && !b.Sai) marks.Add(new Mark { Side = b.X < 0 ? 0 : 1, P = b.Y, Kind = 1, Life = 8, Seed = rng.Next() });
                }
            }
        }

        // ---------- columna de fuego ----------
        sealed class Pillar0 { public Fighter Owner; public float X, T; public bool Hit, Lit; }
        readonly List<Pillar0> pillars = new List<Pillar0>();

        public void Pillar(Fighter f) => pillars.Add(new Pillar0 { Owner = f, X = f.Opp.X });

        void UpdatePillars(float h)
        {
            for (int i = pillars.Count - 1; i >= 0; i--)
            {
                var p = pillars[i]; p.T += h;
                if (p.T < .45f) { if (Rnd() < .6f) Emit(Pk.Ember, p.X + Rs(.3f * H), 0, Rs(.3f * H), Rnd() * 2 * H, .5f, .025f * H); }
                else if (p.T < 1.05f)
                {
                    if (!p.Lit) { p.Lit = true; Snd?.Play("fire"); }
                    for (int k = 0; k < 4; k++) Emit(Pk.Fire, p.X + Rs(.22f * H), Rnd() * .2f * H, Rs(.5f * H), (6 + Rnd() * 5) * H, .35f + Rnd() * .2f, (.07f + Rnd() * .06f) * H);
                    var v = p.Owner.Opp;
                    if (!p.Hit && v.Hittable && Math.Abs(v.X - p.X) < .34f * H && v.Y < 1.8f * H)
                    { p.Hit = true; v.Launch(11, Math.Sign(v.X - p.X + .01f) * 1.5f * H, 9 * H); Kick(4); Meme("torrente|aycabron", .6f, .5f); }
                }
                else if (p.T > 1.2f) pillars.RemoveAt(i);
            }
        }

        // ---------- clon de hielo y charco ----------
        sealed class Clone0 { public Fighter Owner; public PointF[] J; public float X, Life; public int Face; public float[] Pose; public RectangleF Box; }
        Clone0 clone;
        sealed class Puddle0 { public Fighter Owner; public float X, Wd, Life; public bool Used; }
        Puddle0 puddle;
        public bool HasClone => clone != null;
        public bool HasPuddle => puddle != null;

        public void MakeClone(Fighter f)
        {
            float dx = f.Face * .55f * H;
            var j = new PointF[18];
            for (int i = 0; i < 18; i++) j[i] = new PointF(f.J[i].X + dx, f.J[i].Y);
            clone = new Clone0 { Owner = f, J = j, X = f.X + dx, Life = 3.2f, Face = f.Face, Pose = (float[])f.Pose.Clone() };
            for (int i = 0; i < 12; i++) Emit(Pk.Mist, clone.X + Rs(.2f * H), Rnd() * H, Rs(.5f * H), Rnd() * .5f * H, .6f, .12f * H);
            Snd?.Play("ice");
        }

        /// <summary>Un golpe del rival del dueño que toca el clon lo congela a el y rompe el clon.</summary>
        public bool CloneHit(Fighter f, PointF tip, float r)
        {
            if (clone == null || clone.Owner == f) return false;
            if (!RectangleF.Inflate(clone.Box, r, r).Contains(tip)) return false;
            Shards(clone.X, H * .5f, 22); clone = null; Kick(3);
            Meme("erestonto|soygilipollas", .9f, .4f);
            return true;
        }

        void UpdateClone(float h)
        {
            if (clone == null) return;
            if (clone.Owner != P1 && clone.Owner != P2) { clone = null; return; }
            clone.Box = RectangleF.FromLTRB(L + clone.X - .25f * H, Floor - 1.05f * H, L + clone.X + .25f * H, Floor);
            if ((clone.Life -= h) <= 0) { Shards(clone.X, .5f * H, 14); clone = null; }
        }

        public void Puddle(Fighter f) { puddle = new Puddle0 { Owner = f, X = f.Opp.X, Wd = 1.3f * H, Life = 3.5f }; Snd?.Play("ice", .6f); }

        void UpdatePuddle(float h)
        {
            if (puddle == null) return;
            if ((puddle.Life -= h) <= 0) { puddle = null; return; }
            var v = puddle.Owner.Opp;
            if (!puddle.Used && v.Grounded && Math.Abs(v.X - puddle.X) < puddle.Wd / 2 && (v.State == St.Act || v.State == St.Idle) && Math.Abs(v.VX) > .8f * H)
            { puddle.Used = true; v.Launch(4, v.VX * .6f, 2.5f * H); Shards(v.X, .1f * H, 8); Meme("tontito|lamadre", .9f, .3f); }
        }

        // ---------- remates con sangre: cabezas y cuello que sangra ----------
        sealed class Head0 { public float X, Y, VX, VY, Rot, VR, T; public Look Look; public int Face; public Fighter Holder; public bool Spine, Landed; }
        readonly List<Head0> heads = new List<Head0>();
        Fighter bleeder; float bleedT;
        struct Stain { public float X, R, Life; }
        readonly List<Stain> stains = new List<Stain>();

        /// <summary>Cabeza fuera: sale volando (espada) o se queda en la mano del que la arranca (con la columna si es Sub-Zero).</summary>
        public void Decapitate(Fighter v, Fighter by, bool held)
        {
            var hp = Local(v.J[3]);
            heads.Add(new Head0
            {
                X = hp.X, Y = hp.Y, VX = held ? 0 : by.Face * 3.5f * H, VY = held ? 0 : 7 * H, VR = by.Face * 900, Look = v.Look, Face = v.Face,
                Holder = held ? by : null, Spine = held && by.Who == Who.SubZero
            });
            v.Headless = true; v.Corpse(); bleeder = v; bleedT = 2.4f; Snd?.Play(v.Key + "_scream");
            var n = Local(v.J[2]);
            for (int i = 0; i < 45; i++) Emit(Pk.Blood, n.X, n.Y, Rs(3 * H) + by.Face * 1.5f * H, (2 + Rnd() * 6) * H, 1.6f, (.015f + Rnd() * .02f) * H);
            stop = .12f; Kick(8);
            Snd?.Play("slash"); Snd?.Play("blood"); Crack();
            Fatality(by);
        }

        /// <summary>Lo parte en dos (de arriba abajo, o por la cintura) y la herida sigue sangrando mientras las mitades se separan.</summary>
        public void SplitBody(Fighter v, Fighter by, bool waist)
        {
            v.Split(waist, -by.Face); cutBody = v; cutT = 1.1f;
            var top = Local(v.J[3]); var pel = Local(v.J[0]);
            for (int i = 0; i < 55; i++)
            {
                float q = Rnd();
                var p = waist ? new PointF(pel.X + Rs(.1f * H), pel.Y + .08f * H) : new PointF(top.X + (pel.X - top.X) * q, top.Y + (pel.Y - top.Y) * q);
                Emit(Pk.Blood, p.X, p.Y, Rs(3 * H) + (waist ? -by.Face * 2 * H : 0), (1 + Rnd() * 5) * H, 1.6f, (.015f + Rnd() * .02f) * H);
            }
            for (int i = 0; i < 6; i++) Emit(Pk.Gore, pel.X, pel.Y + .1f * H, Rs(2.5f * H), (2 + Rnd() * 3) * H, 2.4f, (.035f + Rnd() * .03f) * H);
            flashes.Add(new Flash { X = pel.X, Y = pel.Y + .3f * H, C = Color.FromArgb(255, 60, 40), R = 2.4f * H, Life = .4f, Max = .4f });
            stop = .14f; Kick(8);
            Snd?.Play("slash"); Snd?.Play("blood"); Snd?.Play(v.Key + "_scream"); Crack();
            Fatality(by);
        }
        Fighter cutBody; float cutT;

        /// <summary>Lo revienta en trozos (carne, huesos y sangre; con el martillo, tambien hielo).</summary>
        public void Gib(Fighter v, Fighter by, bool ice)
        {
            var b = v.Box;
            for (int i = 0; i < 30; i++)
            {
                var p = Local(new PointF(b.Left + Rnd() * b.Width, b.Top + Rnd() * b.Height));
                Emit(Pk.Gore, p.X, p.Y, Rs(5 * H) + by.Face * 1.5f * H, (1.5f + Rnd() * 6) * H, 2.6f, (.03f + Rnd() * .05f) * H);
            }
            var c = Local(v.J[1]);
            for (int i = 0; i < 8; i++) Emit(Pk.Bone, c.X, c.Y, Rs(4 * H), (2 + Rnd() * 5) * H, 2.6f, (.03f + Rnd() * .02f) * H);
            for (int i = 0; i < 80; i++) Emit(Pk.Blood, c.X + Rs(.2f * H), c.Y + Rs(.4f * H), Rs(5 * H), (1 + Rnd() * 6) * H, 1.6f, (.015f + Rnd() * .022f) * H);
            if (ice) { Shards(c.X, c.Y, 20); for (int i = 0; i < 12; i++) Emit(Pk.Chunk, c.X, c.Y, Rs(5 * H), (2 + Rnd() * 5) * H, 2.2f, (.05f + Rnd() * .05f) * H); Snd?.Play("shatter"); }
            flashes.Add(new Flash { X = c.X, Y = c.Y, C = ice ? Color.FromArgb(140, 220, 255) : Color.FromArgb(255, 50, 30), R = 3 * H, Life = .5f, Max = .5f });
            v.Gone(); stop = .14f; Kick(9);
            Snd?.Play("heavyhit"); Snd?.Play("blood"); Snd?.Play("splat"); Snd?.Play(v.Key + "_scream", .8f); Crack();
            Fatality(by);
        }

        /// <summary>Puñaladas del remate de Mileena: sangre del pecho del rival.</summary>
        public void Stab(Fighter by, bool drip = false)
        {
            var c = Local(by.Opp.J[1]);
            if (drip) { Blood(c.X, c.Y, 2, by.Face); return; }
            Blood(c.X, c.Y, 14, by.Face); Kick(3); Snd?.Play("blood", .8f);
        }

        void Bleed(float h)
        {
            if (cutBody != null && (cutT -= h) > 0)
            {
                // la herida del cuerpo partido: chorros a lo largo del corte, cada vez mas flojos
                float kk = cutT / 1.1f; var top = Local(cutBody.J[3]); var pel = Local(cutBody.J[0]);
                for (int i = 0; i < 2; i++)
                {
                    float q = Rnd();
                    var p = cutBody.SplitH ? new PointF(pel.X + Rs(.08f * H), pel.Y + .08f * H) : new PointF(top.X + (pel.X - top.X) * q, top.Y + (pel.Y - top.Y) * q);
                    Emit(Pk.Blood, p.X, p.Y, Rs(2 * H) * kk, (1 + Rnd() * 3) * H * kk, 1.2f, (.012f + Rnd() * .014f) * H);
                }
            }
            if (bleeder == null || bleedT <= 0) return;
            bleedT -= h;
            float k = Math.Max(0, bleedT / 2.4f);
            var n = Local(bleeder.J[2]);
            var up = new PointF(bleeder.J[2].X - bleeder.J[1].X, bleeder.J[1].Y - bleeder.J[2].Y);   // direccion del cuello (hacia arriba en el escenario)
            float l = (float)Math.Sqrt(up.X * up.X + up.Y * up.Y) + 1e-3f;
            for (int i = 0; i < 3; i++)
                Emit(Pk.Blood, n.X, n.Y, up.X / l * (2 + Rnd() * 5) * H * k + Rs(.6f * H), up.Y / l * (2 + Rnd() * 5) * H * k + Rnd() * H, 1.4f, (.012f + Rnd() * .016f) * H);
        }

        void UpdateHeads(float h)
        {
            foreach (var hd in heads)
            {
                hd.T += h;
                if (hd.Holder != null)
                {
                    var p = Local(hd.Holder.J[6]);
                    hd.X = p.X; hd.Y = p.Y + .1f * H; hd.Rot = (float)Math.Sin(hd.T * 3) * 12;
                    if (Rnd() < h * 10) Emit(Pk.Blood, hd.X, hd.Y - .1f * H - (hd.Spine ? .4f * H : 0), Rs(.2f * H), 0, 1, .015f * H);
                    continue;
                }
                hd.VY -= G * h; hd.X += hd.VX * h; hd.Y += hd.VY * h; hd.Rot += hd.VR * h;
                float r = .09f * H;
                if (hd.Y < r) { if (hd.VY < -2 * H && !hd.Landed) { hd.Landed = true; Meme("vineboom", 1, 0, true); } hd.Y = r; hd.VY = -hd.VY * .35f; hd.VX *= .6f; hd.VR = hd.VX / r * 57; if (Math.Abs(hd.VY) > H) Snd?.Play("thud", .5f); }
                if (Walls && (hd.X < r || hd.X > W - r)) { hd.X = Math.Max(r, Math.Min(W - r, hd.X)); hd.VX = -hd.VX * .4f; }
                if (Math.Abs(hd.VX) + Math.Abs(hd.VY) > .5f * H && Rnd() < h * 20) Emit(Pk.Blood, hd.X, hd.Y, Rs(.3f * H), 0, 1, .015f * H);
            }
        }

        // ---------- efectos ----------
        public void Burst(Fighter f, bool vanish)
        {
            var c = Local(f.J[1]);
            for (int i = 0; i < 26; i++) Emit(Pk.Fire, c.X + Rs(.15f * H), c.Y + Rs(.4f * H), Rs(2.5f * H), Rnd() * 4 * H, .4f + Rnd() * .3f, (.06f + Rnd() * .07f) * H);
            for (int i = 0; i < 8; i++) Emit(Pk.Smoke, c.X, c.Y, Rs(1.5f * H), Rnd() * 1.5f * H, .8f, .14f * H);
            flashes.Add(new Flash { X = c.X, Y = c.Y, C = Color.FromArgb(255, 130, 30), R = 2.6f * H, Life = .5f, Max = .5f });
            Snd?.Play("fire");
        }

        public void PinkBurst(Fighter f)
        {
            var c = Local(f.J[1]);
            for (int i = 0; i < 14; i++) Emit(Pk.Smoke, c.X + Rs(.2f * H), c.Y + Rs(.4f * H), Rs(1.5f * H), Rnd() * 1.2f * H, .8f, .14f * H);
            for (int i = 0; i < 18; i++) Emit(Pk.Petal, c.X, c.Y, Rs(2.5f * H), Rnd() * 3 * H, 1.1f, .03f * H);
            flashes.Add(new Flash { X = c.X, Y = c.Y, C = Color.FromArgb(255, 80, 190), R = 2.2f * H, Life = .5f, Max = .5f });
            Snd?.Play("teleport");
        }

        public void FrostBurst(Fighter f)
        {
            var c = Local(f.J[1]);
            for (int i = 0; i < 16; i++) Emit(Pk.Mist, c.X + Rs(.2f * H), c.Y + Rs(.4f * H), Rs(1.5f * H), Rs(1.2f * H), .7f, .12f * H);
            for (int i = 0; i < 10; i++) Emit(Pk.Shard, c.X, c.Y, Rs(2.5f * H), Rnd() * 3 * H, .6f, .035f * H);
            flashes.Add(new Flash { X = c.X, Y = c.Y, C = Color.FromArgb(120, 210, 255), R = 2.2f * H, Life = .5f, Max = .5f });
            Snd?.Play("ice");
        }

        public void Shatter(Fighter f, bool full)
        {
            var c = Local(f.J[1]);
            Shards(c.X, c.Y, full ? 10 : 16);
            Snd?.Play("shatter");
            if (full)
            {
                for (int i = 0; i < 18; i++) Emit(Pk.Chunk, c.X + Rs(.2f * H), c.Y + Rs(.5f * H), Rs(5 * H), (2 + Rnd() * 6) * H, 2.2f, (.05f + Rnd() * .06f) * H);
                f.Gone(); Kick(7); Snd?.Play(f.Key + "_scream", .8f);
            }
        }

        void Shards(float x, float y, int n)
        {
            for (int i = 0; i < n; i++) Emit(Pk.Shard, x, y, Rs(4 * H), Rnd() * 4 * H, .8f, (.025f + Rnd() * .03f) * H);
            for (int i = 0; i < n / 3; i++) Emit(Pk.Mist, x, y, Rs(1 * H), Rnd() * H, .5f, .12f * H);
        }

        public void Ashes(Fighter f)
        {
            var b = f.Box;
            for (int i = 0; i < 70; i++)
            {
                var p = Local(new PointF(b.Left + Rnd() * b.Width, b.Top + Rnd() * b.Height));
                Emit(i % 3 == 0 ? Pk.Ember : Pk.Ash, p.X, p.Y, Rs(1.2f * H), Rnd() * 1.2f * H, 1.2f + Rnd(), .03f * H);
            }
            f.Gone(); Kick(4); Snd?.Play("fire"); Snd?.Play(f.Key + "_scream"); Snd?.Play("crispy");
        }

        public void FireStream(Fighter f)
        {
            var m = Local(f.J[3]);
            for (int i = 0; i < 3; i++) Emit(Pk.Fire, m.X + f.Face * .08f * H, m.Y - .03f * H, f.Face * (6 + Rnd() * 3) * H, Rs(.8f * H), .3f + Rnd() * .15f, (.05f + Rnd() * .05f) * H);
        }

        public void FrostStream(Fighter f)
        {
            var m = Local(f.J[6]);
            for (int i = 0; i < 2; i++) Emit(Pk.Mist, m.X, m.Y, f.Face * (5 + Rnd() * 3) * H, Rs(.6f * H), .4f, .1f * H);
            Emit(Pk.Shard, m.X, m.Y, f.Face * 7 * H, Rs(1 * H), .35f, .025f * H);
        }

        public void Mist(PointF at, int n) { var p = Local(at); for (int i = 0; i < n; i++) Emit(Pk.Mist, p.X, p.Y, Rs(.5f * H), Rnd() * .5f * H, .4f, .08f * H); }

        void Sparks(float x, float y, int n, Color c)
        {
            for (int i = 0; i < n; i++) { float a = Rnd() * 6.283f, v = (3 + Rnd() * 6) * H; Emit(Pk.Spark, x, y, (float)Math.Cos(a) * v, (float)Math.Sin(a) * v, .18f + Rnd() * .12f, 0); }
        }

        /// <summary>Detalle continuo: brasas en las manos de Scorpion, vaho frio en las de Sub-Zero, petalos alrededor de Mileena.</summary>
        void Ambient(float h)
        {
            foreach (var f in Both)
            {
                if (f.Hidden || f.State == St.Gone || f.Fade < 1) continue;
                if (f.Who == Who.Scorpion && Rnd() < h * 6) { var p = Local(f.J[Rnd() < .5f ? 6 : 9]); Emit(Pk.Ember, p.X, p.Y, Rs(.3f * H), (.5f + Rnd()) * H, .7f, .02f * H); }
                if (f.Who == Who.SubZero && Rnd() < h * 5) { var p = Local(f.J[Rnd() < .5f ? 6 : 9]); Emit(Pk.Mist, p.X, p.Y, Rs(.2f * H), -.2f * H, .6f, .05f * H); }
                if (f.Who == Who.Mileena && Rnd() < h * 1.5f) { var p = Local(f.J[0]); Emit(Pk.Petal, p.X, p.Y + .3f * H, Rs(.4f * H), .3f * H, 1.4f, .022f * H); }
            }
        }

        // ---------- escenario: fondo en cache y braseros ----------
        Bitmap bg; float bgW, bgH; float bgBuilt = -9;

        /// <summary>
        /// Fondo del hueco, pintado una vez por tamaño (y como mucho cada 0,2 s mientras se arrastra): panel de carbon que tapa el
        /// escritorio, luna dorada tenue, dos capas de montañas, la silueta de un templo con ventanas encendidas, niebla baja y viñeta.
        /// </summary>
        void Backdrop(Graphics g)
        {
            float w = W, h = Floor - Top;
            if (w < 20 || h < 20) return;
            if (bg == null || ((Math.Abs(w - bgW) > 1 || Math.Abs(h - bgH) > 1) && Clock - bgBuilt > .2f)) BuildBackdrop(w, h);
            var dst = new RectangleF(L, Top, w, h);
            if (beamP < 1)   // se descubre a la vez que la viga, desde la ventana principal
            {
                float shown = w * beamP;
                var d = RootLeft ? new RectangleF(L, Top, shown, h) : new RectangleF(R - shown, Top, shown, h);
                var srcR = RootLeft ? new RectangleF(0, 0, bg.Width * beamP, bg.Height) : new RectangleF(bg.Width * (1 - beamP), 0, bg.Width * beamP, bg.Height);
                if (shown > 1) g.DrawImage(bg, d, srcR, GraphicsUnit.Pixel);
                return;
            }
            // es lo primero sobre el lienzo vacio: se copia en vez de mezclarse (mucho mas barato)
            var cm = g.CompositingMode; g.CompositingMode = CompositingMode.SourceCopy;
            if (Math.Abs(bg.Width - w) < 1 && Math.Abs(bg.Height - h) < 1) g.DrawImageUnscaled(bg, (int)L, (int)Top);
            else g.DrawImage(bg, dst);
            g.CompositingMode = cm;
        }

        void BuildBackdrop(float w, float h)
        {
            bg?.Dispose();
            int iw = Math.Max(1, (int)w), ih = Math.Max(1, (int)h);
            bg = new Bitmap(iw, ih, System.Drawing.Imaging.PixelFormat.Format32bppPArgb); bgW = w; bgH = h; bgBuilt = Clock;
            var rnd = new Random(7);
            using (var g = Graphics.FromImage(bg))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = Fighter.Grad(new RectangleF(0, 0, iw, ih), Color.FromArgb(238, 14, 14, 18), Color.FromArgb(242, 30, 26, 24), 90f)) g.FillRectangle(br, 0, 0, iw, ih);
                // luna con halo
                float mr = Math.Max(6, Math.Min(iw, ih) * .07f), mx = iw * .7f, my = ih * .2f;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(mx - mr * 3, my - mr * 3, mr * 6, mr * 6);
                    using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb(46, 230, 200, 140), SurroundColors = new[] { Color.FromArgb(0, 230, 200, 140) } }) g.FillPath(br, path);
                }
                using (var br = new SolidBrush(Color.FromArgb(120, 236, 220, 178))) g.FillEllipse(br, mx - mr, my - mr, mr * 2, mr * 2);
                // montañas: lejanas y cercanas
                foreach (var (y0, amp, col) in new[] { (.62f, .16f, Color.FromArgb(210, 38, 32, 32)), (.76f, .12f, Color.FromArgb(230, 22, 19, 19)) })
                {
                    var pts = new List<PointF> { new PointF(0, ih) };
                    for (float x = 0; x <= iw + 20; x += Math.Max(12, iw / 9f)) pts.Add(new PointF(x, ih * (y0 - amp * (float)rnd.NextDouble())));
                    pts.Add(new PointF(iw, ih));
                    using (var br = new SolidBrush(col)) g.FillPolygon(br, pts.ToArray());
                }
                // templo de tres tejados curvos con ventanas encendidas
                if (iw > 140 && ih > 160)
                {
                    float tx = iw * .3f, tw = Math.Min(iw * .28f, ih * .22f), baseY = ih * .78f;
                    using (var br = new SolidBrush(Color.FromArgb(240, 12, 10, 10)))
                    using (var win = new SolidBrush(Color.FromArgb(140, 210, 150, 60)))
                        for (int k = 0; k < 3; k++)
                        {
                            float bw = tw * (1 - k * .22f), bh = tw * .32f, y = baseY - k * bh * 1.05f;
                            g.FillRectangle(br, tx - bw * .32f, y - bh, bw * .64f, bh);
                            var roof = new[] { new PointF(tx - bw * .62f, y - bh), new PointF(tx - bw * .36f, y - bh * 1.35f), new PointF(tx + bw * .36f, y - bh * 1.35f), new PointF(tx + bw * .62f, y - bh) };
                            g.FillClosedCurve(br, roof, FillMode.Winding, .3f);
                            for (int q = -1; q <= 1; q++) g.FillRectangle(win, tx + q * bw * .16f - bw * .03f, y - bh * .7f, bw * .06f, bh * .32f);
                        }
                }
                // niebla baja
                using (var br = Fighter.Grad(new RectangleF(0, ih * .7f, iw, ih * .3f + 1), Color.FromArgb(0, 70, 66, 72), Color.FromArgb(95, 70, 66, 72), 90f)) g.FillRectangle(br, 0, ih * .7f, iw, ih * .3f);
                // viñeta y filete dorado contra las ventanas
                using (var br = Fighter.Grad(new RectangleF(0, 0, iw, ih), Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 0f, WrapMode.TileFlipX))
                { var cb = new ColorBlend { Positions = new[] { 0f, .18f, .82f, 1f }, Colors = new[] { Color.FromArgb(90, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(90, 0, 0, 0) } }; br.InterpolationColors = cb; g.FillRectangle(br, 0, 0, iw, ih); }
                using (var pen = new Pen(Color.FromArgb(110, Theme.Gold))) { g.DrawLine(pen, 0, 0, 0, ih); g.DrawLine(pen, iw - 1, 0, iw - 1, ih); }
            }
        }

        readonly PointF[] torchSpots = new PointF[2];
        PointF[] TorchSpots { get { torchSpots[0] = new PointF(.16f * H, 0); torchSpots[1] = new PointF(W - .16f * H, 0); return torchSpots; } }

        /// <summary>Llama de los braseros: dos lenguas pequeñas por paso y alguna brasa.</summary>
        void Torches(float h)
        {
            if (Opening || beamP < 1 || W < 2.2f * H) return;
            foreach (var t in TorchSpots)
            {
                float y = .3f * H;
                if (Rnd() < .06f) Emit(Pk.Ember, t.X, y + .1f * H, Rs(.3f * H), (.8f + Rnd()) * H, .9f, .012f * H);
            }
        }

        void DrawTorches(Graphics g)
        {
            if (Opening || beamP < 1 || W < 2.2f * H) return;
            foreach (var t in TorchSpots)
            {
                var c = Screen(t.X, 0); float w = .09f * H, hh = .3f * H;
                using (var pen = new Pen(Color.FromArgb(40, 36, 34), Math.Max(1.5f, .02f * H))) { g.DrawLine(pen, c.X, c.Y, c.X, c.Y - hh * .8f); g.DrawLine(pen, c.X - w * .6f, c.Y, c.X + w * .6f, c.Y); }
                var bowl = new[] { new PointF(c.X - w, c.Y - hh), new PointF(c.X + w, c.Y - hh), new PointF(c.X + w * .55f, c.Y - hh * .78f), new PointF(c.X - w * .55f, c.Y - hh * .78f) };
                using (var br = Fighter.Grad(bowl[0], bowl[2], Color.FromArgb(70, 64, 60), Color.FromArgb(18, 16, 16))) g.FillPolygon(br, bowl);
                using (var pen = new Pen(Theme.Gold, Math.Max(1f, .012f * H))) g.DrawLine(pen, bowl[0], bowl[1]);
                // llama dibujada (tres lenguas que parpadean) en vez de decenas de particulas: mismo aspecto, una fraccion del coste
                float fx = c.X, fy = c.Y - hh, ph = Clock * 9 + t.X;
                for (int k = 0; k < 3; k++)
                {
                    float fl = 1 + .18f * (float)Math.Sin(ph * (1.3f + k * .4f) + k), fh = (.32f - k * .08f) * H * fl, fw = (.075f - k * .018f) * H, sway = (float)Math.Sin(ph * .7f + k) * .02f * H;
                    var tongue = new[] { new PointF(fx - fw, fy), new PointF(fx - fw * .5f + sway, fy - fh * .55f), new PointF(fx + sway * 2, fy - fh), new PointF(fx + fw * .5f + sway, fy - fh * .5f), new PointF(fx + fw, fy) };
                    pb.Color = k == 0 ? Color.FromArgb(200, 230, 90, 20) : k == 1 ? Color.FromArgb(220, 255, 170, 40) : Color.FromArgb(235, 255, 236, 170);
                    g.FillClosedCurve(pb, tongue, FillMode.Winding, .45f);
                }
            }
        }

        // ---------- particulas ----------
        enum Pk : byte { Fire, Ember, Smoke, Shard, Mist, Spark, Debris, Chunk, Ash, Blood, Petal, Gore, Bone }
        struct Part { public float X, Y, VX, VY, Life, Max, Size, Rot; public Pk K; }
        readonly Part[] parts = new Part[1200]; int np;

        void Emit(Pk k, float x, float y, float vx, float vy, float life, float size)
        {
            if (np >= parts.Length) return;
            parts[np++] = new Part { K = k, X = x, Y = y, VX = vx, VY = vy, Life = life, Max = life, Size = size, Rot = Rnd() * 360 };
        }

        void UpdateParticles(float h)
        {
            for (int i = 0; i < np; i++)
            {
                ref var p = ref parts[i];
                p.Life -= h;
                if (p.Life <= 0) { parts[i] = parts[--np]; i--; continue; }
                switch (p.K)
                {
                    case Pk.Fire: p.VY += 2 * H * h; p.VX *= 1 - 2 * h; break;
                    case Pk.Ember: case Pk.Ash: p.VY += .6f * H * h; p.VX += (float)Math.Sin(Clock * 5 + i) * H * h; break;
                    case Pk.Petal: p.VY -= .8f * H * h; p.VX += (float)Math.Sin(Clock * 4 + i) * 1.5f * H * h; p.Rot += 200 * h; break;
                    case Pk.Smoke: case Pk.Mist: p.VX *= 1 - 2.5f * h; p.VY *= 1 - 2.5f * h; break;
                    case Pk.Spark: p.VY -= G * .4f * h; break;
                    case Pk.Blood: p.VY -= G * .9f * h; break;
                    default: p.VY -= G * .8f * h; p.Rot += p.VX * h * 4; break;
                }
                p.X += p.VX * h; p.Y += p.VY * h;
                if (p.K == Pk.Blood && p.Y < 0)
                {
                    if (p.X > 0 && p.X < W && stains.Count < 60) stains.Add(new Stain { X = p.X, R = p.Size * (3 + Rnd() * 3), Life = 12 });
                    parts[i] = parts[--np]; i--; continue;
                }
                if (p.Y < 0 && (p.K == Pk.Shard || p.K == Pk.Debris || p.K == Pk.Chunk)) { p.Y = 0; p.VY = -p.VY * .35f; p.VX *= .6f; }
                if (p.Y < 0 && (p.K == Pk.Gore || p.K == Pk.Bone))
                {
                    if (p.K == Pk.Gore && p.VY < -H && p.X > 0 && p.X < W && stains.Count < 60) stains.Add(new Stain { X = p.X, R = p.Size * 2.5f, Life = 12 });
                    p.Y = 0; p.VY = -p.VY * .2f; p.VX *= .45f;
                }
                if (Walls && (p.X < 0 || p.X > W) && (p.K == Pk.Shard || p.K == Pk.Debris || p.K == Pk.Chunk || p.K == Pk.Blood || p.K == Pk.Gore || p.K == Pk.Bone)) { p.X = Math.Max(0, Math.Min(W, p.X)); p.VX = -p.VX * .3f; }
            }
        }

        // ---------- luces sobre las ventanas, marcas y textos ----------
        struct Flash { public float X, Y, R, Life, Max; public Color C; }
        struct Ring { public float X, Y, R, Life, Max; public Color C; }
        readonly List<Ring> rings = new List<Ring>();
        readonly List<Flash> lightSrc = new List<Flash>();
        readonly List<Flash> flashes = new List<Flash>();
        struct Mark { public int Side, Seed, Kind; public float P, Life; }   // Kind: 0 quemadura, 1 escarcha, 2 sangre
        readonly List<Mark> marks = new List<Mark>();
        string banner, sub; float bannerT, bannerDur;
        string call; float callT; Fighter caller;

        void Banner(string text, float dur) { banner = text; bannerT = 0; bannerDur = dur; sub = null; Snd?.Say(text); }
        void Sub(string text) { sub = text; Snd?.Say(text); }
        public void Callout(Fighter f, string text) { call = text; callT = 1.1f; caller = f; Snd?.Say(text); }

        // ---------- dibujo ----------
        // perfil por secciones del dibujo (solo para pruebas: se activa desde fuera con Profile = true)
        public static bool Profile;
        public static readonly double[] Prof = new double[8];
        static readonly System.Diagnostics.Stopwatch profSw = new System.Diagnostics.Stopwatch();
        static void Lap(int i) { if (!Profile) return; Prof[i] += profSw.Elapsed.TotalMilliseconds; profSw.Restart(); }

        public void Draw(Graphics g, float realDt)
        {
            float sc = Theme.Scale;
            if (Profile) profSw.Restart();
            if (!Opening) Backdrop(g);
            Lap(0);
            Lights(g);
            Lap(1);
            var st = g.Save();
            if (shake > 0) g.TranslateTransform(Rs(shake), Rs(shake));
            if (Opening) Seams(g);
            if (Walls) Beam(g);
            FloorFx(g);
            foreach (var f in Both) Shadow(g, f);
            if (clone != null) DrawClone(g);
            foreach (var gh in ghosts)
            {
                var j = gh.J; float dx = L - gh.X0, dy = Floor - gh.F0;
                if (dx != 0 || dy != 0) { j = (PointF[])j.Clone(); for (int i = 0; i < j.Length; i++) j[i] = new PointF(j[i].X + dx, j[i].Y + dy); }
                gh.Owner.Paint(g, j, Look.Shadow, gh.Face, gh.Pose, gh.Life / .3f * .45f, 0, false);
            }
            var first = P1.State == St.Act ? P2 : P1;
            DrawTorches(g);
            Lap(2);
            first.Draw(g); first.Opp.Draw(g);
            Lap(3);
            first.DrawTrail(g); first.Opp.DrawTrail(g);
            DrawSpikes(g);
            foreach (var r in rings)
            {
                var c = Screen(r.X, r.Y); float k = 1 - r.Life / r.Max, rr = r.R * (.3f + k);
                using (var pen = new Pen(Color.FromArgb((int)(200 * (1 - k)), r.C), Math.Max(1f, .05f * H * (1 - k)))) g.DrawEllipse(pen, c.X - rr, c.Y - rr * .7f, rr * 2, rr * 1.4f);
            }
            DrawHeads(g);
            DrawChain(g);
            DrawBalls(g);
            Lap(4);
            DrawParticles(g);
            Lap(5);
            g.Restore(st);
            if (beamP >= 1) Hud(g, sc);
            Lap(6);
            Texts(g, sc);
            Lap(7);
        }

        void Lights(Graphics g)
        {
            var src = lightSrc; src.Clear(); src.AddRange(flashes);
            if (!Opening && beamP >= 1 && W >= 2.2f * H)
                foreach (var t in TorchSpots) src.Add(new Flash { X = t.X, Y = .45f * H, C = Color.FromArgb(255, 140, 50), R = 1.1f * H, Life = .5f + .2f * (float)Math.Sin(Clock * 13 + t.X), Max = 1 });
            foreach (var f in Both)
                if (!f.Hidden && f.State != St.Gone && f.Fade > 0) src.Add(new Flash { X = f.X, Y = f.Y + .5f * H, C = f.Look.Rim, R = 1.3f * H, Life = .16f * f.Fade, Max = 1 });
            foreach (var p in pillars) if (p.T > .2f) src.Add(new Flash { X = p.X, Y = H, C = Color.FromArgb(255, 120, 20), R = 3 * H, Life = p.T < .45f ? .3f : .9f, Max = 1 });
            foreach (var b in balls) if (b.Kind == 5) src.Add(new Flash { X = b.X, Y = b.Y, C = Color.FromArgb(130, 210, 255), R = 1.4f * H, Life = .5f, Max = 1 });
            foreach (var b in balls) if (b.Kind >= 2 && b.Kind != 5) src.Add(new Flash { X = b.X, Y = b.Y, C = b.Kind == 2 ? Color.FromArgb(70, 255, 130) : Color.FromArgb(255, 220, 140), R = 1.6f * H, Life = .6f, Max = 1 });
            foreach (var b in balls) if (!b.Sai) src.Add(new Flash { X = b.X, Y = b.Y, C = Color.FromArgb(120, 210, 255), R = 1.8f * H, Life = .6f, Max = 1 });
            wins[0] = WinA; wins[1] = WinB; glowBuilds = 0; glowFrame++;
            var im = g.InterpolationMode; g.InterpolationMode = InterpolationMode.Bilinear;
            foreach (var win in wins)
            {
                // marcas: recorte rectangular con guardar/restaurar (sin crear regiones cada fotograma)
                if (marks.Count > 0)
                {
                    var st = g.Save(); g.SetClip(win, CombineMode.Intersect);
                    foreach (var m in marks) Mark0(g, m, win);
                    g.Restore(st);
                }
                // luces: solo el trozo del halo que cae sobre la ventana
                foreach (var f in src)
                {
                    var c = Screen(f.X, f.Y); float r = f.R, a = Math.Min(1, f.Life / f.Max);
                    if (a <= 0) continue;
                    r = Math.Min(r, 2 * H);   // mas alla apenas se ve sobre la ventana y cuesta mucho
                    var bmp = GlowAt(f.C, a, (int)(2 * r));
                    if (bmp == null)
                    {
                        var full = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
                        var pf = RectangleF.Intersect(full, win);
                        if (pf.Width < 1 || pf.Height < 1) continue;
                        float k = 96 / full.Width;
                        g.DrawImage(Glow(f.C, a), pf, new RectangleF((pf.X - full.X) * k, (pf.Y - full.Y) * k, pf.Width * k, pf.Height * k), GraphicsUnit.Pixel);
                        continue;
                    }
                    int d = bmp.Width, x0 = (int)Math.Round(c.X - d / 2f), y0 = (int)Math.Round(c.Y - d / 2f);
                    var part = Rectangle.Intersect(new Rectangle(x0, y0, d, d), win);
                    if (part.Width < 1 || part.Height < 1) continue;
                    g.DrawImage(bmp, part, part.X - x0, part.Y - y0, part.Width, part.Height, GraphicsUnit.Pixel);
                }
            }
            g.InterpolationMode = im;
        }

        static readonly Dictionary<int, Bitmap> glows = new Dictionary<int, Bitmap>();
        readonly Rectangle[] wins = new Rectangle[2];

        static readonly Dictionary<long, Bitmap> sizedGlows = new Dictionary<long, Bitmap>();
        static readonly Dictionary<long, int> glowUsed = new Dictionary<long, int>();
        static int glowFrame;
        static long sizedPixels;
        static int glowBuilds;

        /// <summary>Halo radial de un color (8 niveles de intensidad), pintado una vez a 96 px (para escalar en lo poco frecuente).</summary>
        static Bitmap Glow(Color c, float a) => GlowAt(c, a, 96);

        /// <summary>
        /// Halo ya pintado a su tamaño (diametro redondeado a 16 px): se dibuja sin escalar, 6 veces mas rapido que escalar uno de 96 px
        /// con filtro bilineal en cada fotograma. Cache con tope de memoria (se vacia si pasa de unos 24 MB).
        /// </summary>
        static Bitmap GlowAt(Color c, float a, int diameter)
        {
            // pocas variantes (4 intensidades, tamaños de 32 en 32 px): la cache se llena una vez y no se vacia en plena pelea
            int lvl = Math.Max(1, Math.Min(4, (int)Math.Round(a * 4))) * 2;
            int d = diameter == 96 ? 96 : Math.Max(32, (diameter + 31) / 32 * 32);
            long key = ((long)(c.R << 16 | c.G << 8 | c.B) << 20) | ((long)lvl << 16) | (uint)d;
            if (sizedGlows.TryGetValue(key, out var bmp)) { glowUsed[key] = glowFrame; return bmp; }
            if (d != 96 && glowBuilds >= 2) return null;   // como mucho dos nuevos por fotograma: el resto, escalado mientras tanto
            if (d != 96) glowBuilds++;
            // llena: se retira la menos usada recientemente (vaciarla entera daba tirones de 25 ms al regenerar todo)
            while (sizedPixels > 4_000_000 && sizedGlows.Count > 0)
            {
                long old = 0; int oldest = int.MaxValue;
                foreach (var kv in glowUsed) if (kv.Value < oldest) { oldest = kv.Value; old = kv.Key; }
                var ob = sizedGlows[old]; sizedPixels -= (long)ob.Width * ob.Height; ob.Dispose(); sizedGlows.Remove(old); glowUsed.Remove(old);
            }
            bmp = new Bitmap(d, d, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                if (d == 96)
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(0, 0, d, d);
                        using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(110 * lvl / 8f), c), SurroundColors = new[] { Color.FromArgb(0, c) } })
                            g.FillPath(br, path);
                    }
                else
                {
                    // de otro tamaño: se escala una sola vez el halo base (rasterizar un degradado radial grande daba tirones de 60 ms)
                    g.InterpolationMode = InterpolationMode.Bilinear; g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(GlowAt(c, lvl / 8f, 96), 0, 0, d, d);
                }
            }
            sizedPixels += (long)d * d; glowUsed[key] = glowFrame;
            return sizedGlows[key] = bmp;
        }

        /// <summary>Quemadura, escarcha o sangre en el borde de la ventana donde alguien se estampo.</summary>
        void Mark0(Graphics g, Mark m, Rectangle win)
        {
            float x = m.Side == 0 ? L : R, y = Floor - m.P;
            if (Math.Abs(x - win.Left) > 2 && Math.Abs(x - win.Right) > 2) return;
            float a = Math.Min(1, m.Life / 2), r = .45f * H;
            var rnd = new Random(m.Seed);
            if (m.Kind == 0)
            {
                var gl = Glow(Color.FromArgb(20, 14, 10), a);
                g.DrawImage(gl, x - r, y - r * .8f, 2 * r, 1.6f * r); g.DrawImage(gl, x - r * .7f, y - r * .55f, 1.4f * r, 1.1f * r);
                using (var pen = new Pen(Color.FromArgb((int)(200 * a * (.6f + .4f * (float)Math.Sin(Clock * 6 + m.Seed))), 255, 130, 30), Math.Max(1f, .012f * H)))
                    for (int k = 0; k < 5; k++) { double an = rnd.NextDouble() * Math.PI * 2; float l = r * (.3f + (float)rnd.NextDouble() * .6f); g.DrawLine(pen, x, y, x + (float)Math.Cos(an) * l, y + (float)Math.Sin(an) * l); }
            }
            else if (m.Kind == 1)
            {
                g.DrawImage(Glow(Color.FromArgb(220, 240, 255), a), x - r, y - r, 2 * r, 2 * r);
                using (var pen = new Pen(Color.FromArgb((int)(220 * a), 235, 248, 255), Math.Max(1f, .01f * H)))
                    for (int k = 0; k < 7; k++)
                    {
                        double an = rnd.NextDouble() * Math.PI * 2; float l = r * (.4f + (float)rnd.NextDouble() * .6f);
                        float ex = x + (float)Math.Cos(an) * l, ey = y + (float)Math.Sin(an) * l;
                        g.DrawLine(pen, x, y, ex, ey);
                        g.DrawLine(pen, (x + ex) / 2, (y + ey) / 2, (x + ex) / 2 + (float)Math.Cos(an + .7) * l * .3f, (y + ey) / 2 + (float)Math.Sin(an + .7) * l * .3f);
                    }
            }
            else
            {
                // salpicadura: gotas alrededor del impacto y regueros que caen por el borde
                int dir = Math.Abs(x - win.Left) <= 2 ? 1 : -1;
                using (var br = new SolidBrush(Color.FromArgb((int)(200 * a), 120, 4, 8)))
                {
                    for (int k = 0; k < 9; k++)
                    {
                        double an = rnd.NextDouble() * Math.PI - Math.PI / 2; float l = r * (float)rnd.NextDouble() * .8f, s = .02f * H + (float)rnd.NextDouble() * .05f * H;
                        g.FillEllipse(br, x + dir * (float)Math.Cos(an) * l - s, y + (float)Math.Sin(an) * l - s, 2 * s, 2 * s);
                    }
                    float age = 10 - m.Life;
                    for (int k = 0; k < 3; k++) { float dx = dir * (.02f + k * .04f) * H, len = Math.Min(1.2f * H, age * .25f * H * (1 + k * .3f)); g.FillRectangle(br, x + dx - .008f * H, y, .016f * H, len); }
                }
            }
        }

        /// <summary>Viga de carbon con filete dorado que une las dos ventanas a la altura del suelo; al empezar sale de la principal.</summary>
        void Beam(Graphics g)
        {
            float sc = Theme.Scale, th = 8 * sc;
            float x0 = L - 12 * sc, x1 = R + 12 * sc;
            if (beamP < 1) { if (RootLeft) x1 = L + W * beamP; else x0 = R - W * beamP; }
            if (x1 - x0 < 1) return;
            using (var br = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(br, x0 + 3 * sc, Floor + th, Math.Max(1, x1 - x0 - 6 * sc), 4 * sc);
            using (var br = Fighter.Grad(new PointF(0, Floor), new PointF(0, Floor + th), Color.FromArgb(70, 70, 76), Color.FromArgb(10, 10, 12)))
                g.FillRectangle(br, x0, Floor, x1 - x0, th);
            using (var pen = new Pen(Theme.Gold, Math.Max(1f, 1.3f * sc))) g.DrawLine(pen, x0, Floor + .7f * sc, x1, Floor + .7f * sc);
            using (var pen = new Pen(Color.FromArgb(6, 6, 8), Math.Max(1f, sc))) g.DrawRectangle(pen, x0, Floor, x1 - x0, th);
            foreach (var x in new[] { L, R })
            {
                bool rootSide = (x == L) == RootLeft;
                if (beamP < 1 && !rootSide) continue;
                var plate = new RectangleF(x - 7 * sc, Floor - 3 * sc, 14 * sc, th + 6 * sc);
                using (var br = Fighter.Grad(plate, Color.FromArgb(62, 62, 68), Color.FromArgb(12, 12, 14), 90f)) g.FillRectangle(br, plate);
                using (var pen = new Pen(Color.FromArgb(6, 6, 8), Math.Max(1f, sc))) g.DrawRectangle(pen, plate.X, plate.Y, plate.Width, plate.Height);
                using (var br = new SolidBrush(Theme.Gold)) { g.FillEllipse(br, x - 1.6f * sc, Floor - .5f * sc, 3.2f * sc, 3.2f * sc); g.FillEllipse(br, x - 1.6f * sc, Floor + th - 2.5f * sc, 3.2f * sc, 3.2f * sc); }
            }
            if (beamP < 1)
            {
                float tip = RootLeft ? x1 : x0;
                using (var path = new GraphicsPath())
                {
                    float r = 10 * sc; path.AddEllipse(tip - r, Floor + th / 2 - r, 2 * r, 2 * r);
                    using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb(230, 255, 236, 180), SurroundColors = new[] { Color.FromArgb(0, Theme.Gold) } }) g.FillPath(br, path);
                }
            }
        }

        void FloorFx(Graphics g)
        {
            foreach (var s in stains)
            {
                var c = Screen(s.X, 0); float a = Math.Min(1, s.Life / 2);
                using (var br = new SolidBrush(Color.FromArgb((int)(190 * a), 110, 4, 8))) g.FillEllipse(br, c.X - s.R, c.Y - s.R * .25f, 2 * s.R, s.R * .5f);
            }
            if (puddle != null)
            {
                var c = Screen(puddle.X, 0); float a = Math.Min(1, puddle.Life / .6f), w = puddle.Wd;
                using (var br = new SolidBrush(Color.FromArgb((int)(130 * a), 150, 210, 250))) g.FillEllipse(br, c.X - w / 2, c.Y - .05f * H, w, .1f * H);
                using (var pen = new Pen(Color.FromArgb((int)(200 * a), 235, 250, 255), Math.Max(1f, .012f * H))) g.DrawArc(pen, c.X - w / 2, c.Y - .05f * H, w, .1f * H, 190, 110);
            }
            foreach (var p in pillars)
            {
                var c = Screen(p.X, 0);
                if (p.T < .45f)
                {
                    float a = .5f + .5f * (float)Math.Sin(p.T * 40);
                    using (var br = new SolidBrush(Color.FromArgb((int)(160 * a), 255, 120, 20))) g.FillEllipse(br, c.X - .3f * H, c.Y - .04f * H, .6f * H, .08f * H);
                }
                else if (p.T < 1.1f)
                {
                    float a = Math.Min(1, (1.1f - p.T) * 4), top = 1.9f * H;
                    var rc = new RectangleF(c.X - .2f * H, c.Y - top, .4f * H, top);
                    using (var br = Fighter.Grad(rc, Color.FromArgb(0, 255, 120, 20), Color.FromArgb((int)(220 * a), 255, 230, 160), 90f)) g.FillRectangle(br, rc);
                }
            }
        }

        void Shadow(Graphics g, Fighter f)
        {
            if (f.Hidden || f.State == St.Gone || f.State == St.Cling) return;
            float k = Math.Max(0, 1 - f.Y / (2.5f * H)) * f.Fade, w = .6f * H * (.6f + .4f * k);
            var c = Screen(f.X, 0);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - w / 2, c.Y - .05f * H, w, .1f * H);
                using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(150 * k), 0, 0, 0), SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) } }) g.FillPath(br, path);
            }
        }

        void DrawClone(Graphics g)
        {
            var c = clone; float a = Math.Min(1, c.Life / .4f) * .8f;
            c.Owner.Paint(g, c.J, Look.Ice, c.Face, c.Pose, a, 0, false);
        }

        void DrawHeads(Graphics g)
        {
            foreach (var hd in heads)
            {
                var c = Screen(hd.X, hd.Y); float r = .09f * H, rot = hd.Rot * (float)Math.PI / 180;
                var neck = new PointF(c.X - (float)Math.Sin(rot) * r * .95f, c.Y + (float)Math.Cos(rot) * r * .95f);
                if (hd.Spine)
                {
                    // columna colgando: vertebras de hueso con juntas rojas
                    var p = neck;
                    for (int i = 0; i < 9; i++)
                    {
                        var q = new PointF(p.X + (float)Math.Sin(hd.T * 4 + i * .6f) * .01f * H, p.Y + .045f * H);
                        using (var pen = new Pen(Color.FromArgb(140, 10, 10), .025f * H)) g.DrawLine(pen, p, q);
                        float vw = (.035f - i * .002f) * H;
                        using (var br = Fighter.Grad(new PointF(q.X - vw, q.Y), new PointF(q.X + vw, q.Y), Color.FromArgb(240, 232, 214), Color.FromArgb(170, 150, 120)))
                            g.FillEllipse(br, q.X - vw, q.Y - .015f * H, vw * 2, .03f * H);
                        p = q;
                    }
                }
                Fighter.Head(g, c, hd.Rot, hd.Face, hd.Look, 1, 0, H, (col, dk) => dk > 0 ? Theme.Mix(col, Color.Black, dk) : col);
                using (var br = new SolidBrush(Color.FromArgb(150, 10, 12))) g.FillEllipse(br, neck.X - .035f * H, neck.Y - .02f * H, .07f * H, .04f * H);
            }
        }

        void DrawChain(Graphics g)
        {
            var c = chain; if (c == null) return;
            var a = c.Owner.J[6]; var b = Screen(c.X, c.Y);
            float dx = b.X - a.X, dy = b.Y - a.Y, l = (float)Math.Sqrt(dx * dx + dy * dy); if (l < 1) return;
            float ux = dx / l, uy = dy / l, sag = c.Phase == 1 ? 0 : Math.Min(.25f * H, l * .06f);
            int n = Math.Max(2, (int)(l / (.05f * H)));
            var pts = new PointF[n + 1];
            for (int i = 0; i <= n; i++) { float s = i / (float)n; pts[i] = new PointF(a.X + dx * s, a.Y + dy * s + sag * (float)Math.Sin(Math.PI * s)); }
            using (var pen = new Pen(Color.FromArgb(30, 26, 22), Math.Max(1.5f, .03f * H))) g.DrawLines(pen, pts);
            using (var pen = new Pen(Color.FromArgb(190, 170, 120), Math.Max(1f, .014f * H)))
                for (int i = 0; i < n; i++) { var p = pts[i]; float r = .018f * H; if (i % 2 == 0) g.DrawEllipse(pen, p.X - r * 1.4f, p.Y - r, r * 2.8f, r * 2); else g.DrawLine(pen, p.X - ux * r, p.Y - uy * r, p.X + ux * r, p.Y + uy * r); }
            float k = .16f * H, w = .05f * H; var nx = -uy; var ny = ux;
            var blade = new[] { new PointF(b.X + ux * k, b.Y + uy * k), new PointF(b.X + nx * w, b.Y + ny * w), new PointF(b.X - ux * k * .25f, b.Y - uy * k * .25f), new PointF(b.X - nx * w, b.Y - ny * w) };
            using (var br = Fighter.Grad(blade[1], blade[3], Color.FromArgb(240, 240, 245), Color.FromArgb(110, 110, 120))) g.FillPolygon(br, blade);
            using (var pen = new Pen(Color.FromArgb(10, 10, 12), Math.Max(1f, .012f * H))) g.DrawPolygon(pen, blade);
            using (var pen = new Pen(Theme.Gold, Math.Max(1f, .02f * H))) g.DrawEllipse(pen, b.X - ux * k * .4f - .025f * H, b.Y - uy * k * .4f - .025f * H, .05f * H, .05f * H);
        }

        void DrawBalls(Graphics g)
        {
            foreach (var b in balls)
            {
                var c = Screen(b.X, b.Y);
                if (b.Kind == 5)
                {
                    Fighter.LeviathanAxe(g, c, b.T * 1300 * Math.Sign(b.VX), H, 1, true);
                    continue;
                }
                if (b.Kind == 4)
                {
                    float d = Math.Sign(b.VX), len = .34f * H;
                    using (var pen = new Pen(Color.FromArgb(120, 80, 40), Math.Max(1.5f, .016f * H))) g.DrawLine(pen, c.X - d * len, c.Y, c.X, c.Y);
                    using (var br = new SolidBrush(Color.FromArgb(255, 236, 170))) g.FillPolygon(br, new[] { new PointF(c.X + d * .08f * H, c.Y), new PointF(c.X, c.Y - .035f * H), new PointF(c.X, c.Y + .035f * H) });
                    continue;
                }
                if (b.Kind >= 2)
                {
                    // bola verde de Cage o chispa de Baraka: nucleo brillante con estela
                    var col = b.Kind == 2 ? Color.FromArgb(70, 255, 130) : Color.FromArgb(255, 236, 170);
                    float rr = .11f * H * (1 + .1f * (float)Math.Sin(b.T * 40)), d = Math.Sign(b.VX);
                    using (var pen = new Pen(Color.FromArgb(110, col), rr * 1.2f) { EndCap = LineCap.Round }) g.DrawLine(pen, c.X - d * rr * 4, c.Y, c.X, c.Y);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(c.X - rr * 1.8f, c.Y - rr * 1.8f, rr * 3.6f, rr * 3.6f);
                        using (var br = new PathGradientBrush(path) { CenterColor = Color.White, SurroundColors = new[] { Color.FromArgb(0, col) } }) g.FillPath(br, path);
                    }
                    continue;
                }
                if (b.Sai)
                {
                    float d = Math.Sign(b.VX), len = .3f * H, rot = b.T * 0;
                    if (b.VY != 0)
                    {
                        var st2 = g.Save(); g.TranslateTransform(c.X, c.Y); g.RotateTransform((float)(Math.Atan2(-b.VY, b.VX) * 180 / Math.PI) - (d > 0 ? 0 : 180)); c = new PointF(0, 0);
                        using (var pen = new Pen(Color.FromArgb(225, 228, 236), Math.Max(1.5f, .02f * H))) g.DrawLine(pen, -d * len * .5f, 0, d * len * .5f, 0);
                        using (var pen = new Pen(Color.FromArgb(150, 30, 100), .03f * H)) g.DrawLine(pen, -d * len * .5f, 0, -d * len * .25f, 0);
                        g.Restore(st2);
                        continue;
                    }
                    using (var pen = new Pen(Color.FromArgb(225, 228, 236), Math.Max(1.5f, .02f * H))) g.DrawLine(pen, c.X - d * len * .5f, c.Y, c.X + d * len * .5f, c.Y);
                    using (var pen = new Pen(Color.FromArgb(200, 205, 215), Math.Max(1f, .014f * H)))
                    { g.DrawLine(pen, c.X - d * len * .2f, c.Y, c.X - d * len * .05f, c.Y - .06f * H); g.DrawLine(pen, c.X - d * len * .2f, c.Y, c.X - d * len * .05f, c.Y + .06f * H); }
                    using (var pen = new Pen(Color.FromArgb(150, 30, 100), .03f * H)) g.DrawLine(pen, c.X - d * len * .5f, c.Y, c.X - d * len * .25f, c.Y);
                    using (var pen = new Pen(Color.FromArgb(80, 255, 255, 255), Math.Max(1f, .01f * H))) g.DrawLine(pen, c.X - d * len * 1.4f, c.Y, c.X - d * len * .5f, c.Y);
                    _ = rot;
                    continue;
                }
                float r = .14f * H * (1 + .08f * (float)Math.Sin(b.T * 30));
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(c.X - r * 1.8f, c.Y - r * 1.8f, r * 3.6f, r * 3.6f);
                    using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb(150, 120, 210, 255), SurroundColors = new[] { Color.FromArgb(0, 120, 210, 255) } }) g.FillPath(br, path);
                }
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(c.X - r, c.Y - r, 2 * r, 2 * r);
                    using (var br = new PathGradientBrush(path) { CenterColor = Color.White, SurroundColors = new[] { Color.FromArgb(220, 90, 170, 240) }, CenterPoint = new PointF(c.X - r * .3f, c.Y - r * .3f) }) g.FillPath(br, path);
                }
            }
        }

        readonly SolidBrush pb = new SolidBrush(Color.White);
        readonly Pen pp = new Pen(Color.White) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        void DrawParticles(Graphics g)
        {
            for (int i = 0; i < np; i++)
            {
                var p = parts[i]; float t = p.Life / p.Max; var c = Screen(p.X, p.Y); float s = p.Size;
                switch (p.K)
                {
                    case Pk.Fire:
                        {
                            var col = t > .7f ? Color.FromArgb(255, 245, 200) : t > .45f ? Color.FromArgb(255, 190, 60) : t > .2f ? Color.FromArgb(240, 100, 20) : Color.FromArgb(120, 40, 20);
                            // lengua de fuego: lagrima estirada segun la velocidad, con halo tenue
                            float r = s * (.5f + (1 - t) * .5f), vx = p.VX, vy = -p.VY, vl = (float)Math.Sqrt(vx * vx + vy * vy) + 1e-3f;
                            float len = r * (1.6f + Math.Min(2.5f, vl / (6 * H)) * 1.6f), ux = vx / vl, uy = vy / vl;
                            var tear = new[] { new PointF(c.X - ux * len, c.Y - uy * len), new PointF(c.X - uy * r, c.Y + ux * r), new PointF(c.X + ux * r * .8f, c.Y + uy * r * .8f), new PointF(c.X + uy * r, c.Y - ux * r) };
                            { pb.Color = Color.FromArgb((int)(40 * t), 255, 110, 20); g.FillEllipse(pb, c.X - r * 1.7f, c.Y - r * 1.7f, r * 3.4f, r * 3.4f); }
                            { pb.Color = Color.FromArgb((int)(235 * t), col); g.FillClosedCurve(pb, tear, FillMode.Winding, .55f); }
                            break;
                        }
                    case Pk.Ember:
                        { pb.Color = Color.FromArgb((int)(255 * t), 255, 170 + (int)(60 * t), 60); g.FillEllipse(pb, c.X - s, c.Y - s, 2 * s, 2 * s); }
                        break;
                    case Pk.Ash:
                        { pb.Color = Color.FromArgb((int)(220 * t), 50, 44, 40); g.FillRectangle(pb, c.X - s, c.Y - s, 2 * s, 2 * s); }
                        break;
                    case Pk.Petal:
                        { pb.Color = Color.FromArgb((int)(230 * Math.Min(1, t * 2)), 255, 90, 180); g.FillEllipse(pb, c.X - s, c.Y - s * .5f, 2 * s, s); }
                        break;
                    case Pk.Blood:
                        {
                            float vx = p.VX, vy = -p.VY, vl = (float)Math.Sqrt(vx * vx + vy * vy) + 1e-3f, len = Math.Min(s * 3, vl * .012f);
                            { pp.Color = Color.FromArgb((int)(240 * Math.Min(1, t * 3)), 150, 6, 10); pp.Width = s * 1.6f; g.DrawLine(pp, c.X, c.Y, c.X - vx / vl * len, c.Y - vy / vl * len); }
                            break;
                        }
                    case Pk.Smoke:
                        { float r = s * (1 + (1 - t) * 1.5f); { pb.Color = Color.FromArgb((int)(70 * t), 40, 38, 36); g.FillEllipse(pb, c.X - r, c.Y - r, 2 * r, 2 * r); } break; }
                    case Pk.Mist:
                        { float r = s * (1 + (1 - t)); { pb.Color = Color.FromArgb((int)(70 * t), 210, 236, 255); g.FillEllipse(pb, c.X - r, c.Y - r, 2 * r, 2 * r); } break; }
                    case Pk.Spark:
                        { pp.Color = Color.FromArgb((int)(255 * t), 255, 240, 190); pp.Width = Math.Max(1f, .015f * H); g.DrawLine(pp, c.X, c.Y, c.X - p.VX * .025f, c.Y + p.VY * .025f); }
                        break;
                    case Pk.Debris:
                        { pb.Color = Color.FromArgb((int)(255 * Math.Min(1, t * 2)), 30, 30, 34); g.FillRectangle(pb, c.X - s, c.Y - s, 2 * s, 2 * s); }
                        break;
                    case Pk.Shard:
                    case Pk.Chunk:
                        {
                            float a = p.Rot * (float)Math.PI / 180, cs = (float)Math.Cos(a) * s, sn = (float)Math.Sin(a) * s;
                            var poly = new[] { new PointF(c.X + cs * 1.6f, c.Y + sn * 1.6f), new PointF(c.X - sn, c.Y + cs), new PointF(c.X - cs * 1.2f, c.Y - sn * 1.2f), new PointF(c.X + sn * .8f, c.Y - cs * .8f) };
                            int al = (int)(255 * Math.Min(1, t * 2.5f));
                            { pb.Color = Color.FromArgb(al * (p.K == Pk.Chunk ? 200 : 230) / 255, 170, 220, 250); g.FillPolygon(pb, poly); }
                            { pp.Color = Color.FromArgb(al, 240, 250, 255); pp.Width = Math.Max(1f, .008f * H); g.DrawPolygon(pp, poly); }
                            break;
                        }
                    case Pk.Gore:
                        {
                            float a = p.Rot * (float)Math.PI / 180, cs = (float)Math.Cos(a) * s, sn = (float)Math.Sin(a) * s;
                            var poly = new[] { new PointF(c.X + cs * 1.3f, c.Y + sn * 1.3f), new PointF(c.X - sn * 1.1f, c.Y + cs * 1.1f), new PointF(c.X - cs, c.Y - sn), new PointF(c.X + sn * .9f, c.Y - cs * .9f) };
                            int al = (int)(255 * Math.Min(1, t * 2.5f));
                            { pb.Color = Color.FromArgb(al, 128, 14, 18); g.FillClosedCurve(pb, poly, FillMode.Winding, .4f); }
                            { pp.Color = Color.FromArgb(al, 60, 2, 6); pp.Width = Math.Max(1f, .008f * H); g.DrawClosedCurve(pp, poly, .4f, FillMode.Winding); }
                            { pb.Color = Color.FromArgb(al * 3 / 5, 210, 70, 70); g.FillEllipse(pb, c.X - s * .35f + sn * .3f, c.Y - s * .35f - cs * .3f, s * .5f, s * .4f); }   // brillo humedo
                            break;
                        }
                    case Pk.Bone:
                        {
                            float a = p.Rot * (float)Math.PI / 180, dx = (float)Math.Cos(a) * s * 1.8f, dy = (float)Math.Sin(a) * s * 1.8f;
                            int al = (int)(255 * Math.Min(1, t * 2.5f));
                            pp.Color = Color.FromArgb(al, 236, 228, 208); pp.Width = Math.Max(1.5f, s * .6f); g.DrawLine(pp, c.X - dx, c.Y - dy, c.X + dx, c.Y + dy);
                            pb.Color = pp.Color; float r = Math.Max(1f, s * .5f);
                            g.FillEllipse(pb, c.X - dx - r, c.Y - dy - r, 2 * r, 2 * r); g.FillEllipse(pb, c.X + dx - r, c.Y + dy - r, 2 * r, 2 * r);
                            break;
                        }
                }
            }
        }

        /// <summary>
        /// Marcador al estilo del juego: barras largas desde cada lado con el extremo interior en bisel, marco de metal, relleno dorado
        /// con el daño reciente en blanco y rojo; nombres en cursiva debajo; en el centro el medallon del dragon y el tiempo; rondas
        /// ganadas con dragones pequeños.
        /// </summary>
        void Hud(Graphics g, float sc)
        {
            float w = Math.Min(W - 10 * sc, 560 * sc), x0 = L + (W - w) / 2, y0 = Top + 8 * sc, bh = Math.Max(8 * sc, Math.Min(13 * sc, H * .13f));
            float cg = 40 * sc, bw = (w - cg) / 2, cx = L + W / 2;
            Bar(g, x0, y0, bw, bh, P1, false, sc);
            Bar(g, x0 + w - bw, y0, bw, bh, P2, true, sc);
            Name(g, P1.Name, new PointF(x0 + 2 * sc, y0 + bh + 2 * sc), bh * 1.05f, false);
            Name(g, P2.Name, new PointF(x0 + w - 2 * sc, y0 + bh + 2 * sc), bh * 1.05f, true);
            for (int k = 0; k < 2; k++)
            {
                Dragon(g, new PointF(x0 + bw - (8 + k * 13) * sc, y0 + bh + 9 * sc), 5 * sc, P1.Wins > k ? 1 : .25f);
                Dragon(g, new PointF(x0 + w - bw + (8 + k * 13) * sc, y0 + bh + 9 * sc), 5 * sc, P2.Wins > k ? 1 : .25f);
            }
            Dragon(g, new PointF(cx, y0 + bh * .5f), 15 * sc, 1);
            int secs = (int)Math.Ceiling(Math.Max(0, timeLeft));
            BigText(g, secs.ToString("00"), new PointF(cx, y0 + bh * .5f + 24 * sc), 15 * sc, 60 * sc, 1, 1, secs <= 10);
        }

        static void Bar(Graphics g, float x, float y, float w, float h, Fighter f, bool rtl, float sc)
        {
            float bev = h;   // extremo interior en bisel
            var poly = rtl
                ? new[] { new PointF(x, y), new PointF(x + w, y), new PointF(x + w, y + h), new PointF(x + bev, y + h) }
                : new[] { new PointF(x, y), new PointF(x + w, y), new PointF(x + w - bev, y + h), new PointF(x, y + h) };
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(poly);
                using (var br = new SolidBrush(Color.FromArgb(230, 70, 6, 8))) g.FillPath(br, path);
                var old = g.Clip; g.SetClip(path, CombineMode.Intersect);
                float lag = w * f.ShownHp / 100, now = w * f.Hp / 100;
                // vida: crece desde el borde exterior (izquierda para el 1, derecha para el 2)
                RectangleF Part(float len) => rtl ? new RectangleF(x + w - len, y, len, h) : new RectangleF(x, y, len, h);
                using (var br = Fighter.Grad(new RectangleF(x, y, w, h), Color.FromArgb(255, 250, 240), Color.FromArgb(220, 30, 30), 90f)) g.FillRectangle(br, Part(lag));
                if (now > 0)
                    using (var br = Fighter.Grad(new RectangleF(x, y, w, h), Color.FromArgb(255, 238, 110), Color.FromArgb(214, 140, 0), 90f)) g.FillRectangle(br, Part(now));
                using (var br = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillRectangle(br, x, y, w, h * .32f);   // brillo
                g.Clip = old; old.Dispose();
                using (var pen = new Pen(Color.FromArgb(14, 12, 12), Math.Max(1.5f, 1.6f * sc)) { LineJoin = LineJoin.Miter }) g.DrawPath(pen, path);
                using (var pen = new Pen(Color.FromArgb(200, Theme.GoldDeep), Math.Max(1f, .8f * sc)) { LineJoin = LineJoin.Miter })
                {
                    path.Transform(new Matrix(1, 0, 0, 1, 0, 0));
                    g.DrawLine(pen, poly[0], poly[1]);
                }
            }
        }

        static readonly Dictionary<(string, int, int), (GraphicsPath path, RectangleF box)> textPaths = new Dictionary<(string, int, int), (GraphicsPath, RectangleF)>();

        /// <summary>Contorno de un texto a tamaño dado, en el origen; se guarda (crearlo cada fotograma era de lo mas caro del marcador).</summary>
        internal static (GraphicsPath path, RectangleF box) TextPath(string s, float size, FontStyle style)
        {
            var key = (s, (int)(size * 2), (int)style);
            if (textPaths.TryGetValue(key, out var v)) return v;
            if (textPaths.Count > 64) { foreach (var t in textPaths.Values) t.path.Dispose(); textPaths.Clear(); }
            var path = new GraphicsPath();
            path.AddString(s, Big(), (int)style, size, new PointF(0, 0), StringFormat.GenericTypographic);
            return textPaths[key] = (path, path.GetBounds());
        }

        static void Name(Graphics g, string text, PointF at, float size, bool rtl)
        {
            var (path, b) = TextPath(text, size, FontStyle.Italic);
            var st = g.Save();
            g.TranslateTransform(rtl ? at.X - b.Right : at.X - b.Left, at.Y - b.Top);
            using (var pen = new Pen(Color.FromArgb(220, 0, 0, 0), Math.Max(2f, size * .22f)) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
            using (var br = new SolidBrush(Color.FromArgb(245, 240, 230))) g.FillPath(br, path);
            g.Restore(st);
        }

        /// <summary>Medallon del dragon (logo de MK): aro dorado y dragon enroscado con la boca abierta.</summary>
        static readonly Dictionary<(int, int), Bitmap> dragons = new Dictionary<(int, int), Bitmap>();

        static void Dragon(Graphics g, PointF c, float r, float a)
        {
            var key = ((int)Math.Round(r * 2), (int)Math.Round(a * 4));
            if (!dragons.TryGetValue(key, out var bmp))
            {
                if (dragons.Count > 16) { foreach (var b in dragons.Values) b.Dispose(); dragons.Clear(); }
                int sz = (int)Math.Ceiling(r * 2) + 4;
                bmp = new Bitmap(sz, sz, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var bg2 = Graphics.FromImage(bmp)) { bg2.SmoothingMode = SmoothingMode.AntiAlias; DragonCore(bg2, new PointF(sz / 2f, sz / 2f), r, a); }
                dragons[key] = bmp;
            }
            g.DrawImageUnscaled(bmp, (int)Math.Round(c.X - bmp.Width / 2f), (int)Math.Round(c.Y - bmp.Height / 2f));
        }

        static void DragonCore(Graphics g, PointF c, float r, float a)
        {
            int al = (int)(255 * a);
            var st = g.Save();
            g.TranslateTransform(c.X, c.Y); g.ScaleTransform(r, r);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-1, -1, 2, 2);
                using (var br = new PathGradientBrush(path) { CenterPoint = new PointF(-.3f, -.35f), CenterColor = Color.FromArgb(al, 60, 40, 14), SurroundColors = new[] { Color.FromArgb(al, 10, 8, 6) } }) g.FillPath(br, path);
            }
            using (var br = Fighter.Grad(new PointF(-1, -1), new PointF(1, 1), Color.FromArgb(al, 255, 226, 130), Color.FromArgb(al, 150, 96, 20)))
            {
                using (var pen = new Pen(br, .16f)) g.DrawEllipse(pen, -.9f, -.9f, 1.8f, 1.8f);
                // cuerpo: de la cabeza (arriba a la derecha) baja por la izquierda, recorre el fondo y la cola sube a la derecha
                using (var pen = new Pen(br, .2f) { StartCap = LineCap.Round, EndCap = LineCap.Triangle })
                    g.DrawBezier(pen, new PointF(.38f, -.5f), new PointF(-.75f, -.75f), new PointF(-.85f, .55f), new PointF(.05f, .55f));
                using (var pen = new Pen(br, .13f) { EndCap = LineCap.Triangle })
                    g.DrawBezier(pen, new PointF(.05f, .55f), new PointF(.55f, .55f), new PointF(.62f, .1f), new PointF(.3f, .02f));
                // cabeza con la mandibula abierta y cuerno
                g.FillPolygon(br, new[] { new PointF(.22f, -.62f), new PointF(.7f, -.72f), new PointF(.52f, -.5f), new PointF(.3f, -.42f) });
                g.FillPolygon(br, new[] { new PointF(.3f, -.42f), new PointF(.68f, -.34f), new PointF(.45f, -.5f) });
                g.FillPolygon(br, new[] { new PointF(.2f, -.6f), new PointF(.05f, -.85f), new PointF(.3f, -.62f) });
                // pinchos del lomo
                foreach (var (px, py) in new[] { (-.45f, -.45f), (-.62f, -.05f), (-.45f, .38f) })
                    g.FillPolygon(br, new[] { new PointF(px, py), new PointF(px - .2f, py - .08f), new PointF(px - .05f, py + .12f) });
            }
            g.Restore(st);
        }

        static FontFamily bigFamily;
        static FontFamily Big()
        {
            if (bigFamily != null) return bigFamily;
            foreach (var n in new[] { "Impact", "Arial Black", "Segoe UI Black", "Arial" })
                try { return bigFamily = new FontFamily(n); } catch { }
            return bigFamily = FontFamily.GenericSansSerif;
        }

        void Texts(Graphics g, float sc)
        {
            if (banner != null && bannerT < bannerDur)
            {
                float k = Math.Min(1, bannerT / .12f), out0 = Math.Min(1, (bannerDur - bannerT) / .2f), a = Math.Min(k, out0);
                var box = Bounds; float maxW = box.Width - 12 * sc;
                bool red = banner == "FATALITY" || banner.StartsWith("FINISH");
                BigText(g, banner, new PointF(L + W / 2, Top + (Floor - Top) * .32f), Math.Min(34 * sc, Math.Max(18 * sc, (Floor - Top) * .12f)), maxW, a, 1.25f - .25f * k, red);
                if (sub != null) BigText(g, sub, new PointF(L + W / 2, Top + (Floor - Top) * .32f + 28 * sc), 13 * sc, maxW, a, 1, false);
            }
            if (callT > 0 && caller != null && !caller.Hidden)
            {
                float a = Math.Min(1, callT / .25f), x = caller.J[3].X, y = caller.Box.Top - 8 * sc;
                var box = Bounds;
                x = Math.Max(box.Left + 60 * sc, Math.Min(box.Right - 60 * sc, x));
                BigText(g, call, new PointF(x, y), 12 * sc, box.Width - 10 * sc, a, 1, true);
            }
        }

        static void BigText(Graphics g, string s, PointF c, float size, float maxW, float a, float scale, bool red)
        {
            {
                var (path, b) = TextPath(s, size, FontStyle.Regular);
                float k = Math.Min(scale, maxW / Math.Max(1, b.Width));
                var st = g.Save();
                g.TranslateTransform(c.X, c.Y); g.ScaleTransform(k, k); g.TranslateTransform(-b.X - b.Width / 2, -b.Y - b.Height / 2);
                var r = b;
                int al = (int)(255 * a);
                using (var pen = new Pen(Color.FromArgb(al * 3 / 5, 0, 0, 0), Math.Max(3f, size * .28f) / k) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                using (var br = Fighter.Grad(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), red ? Color.FromArgb(al, 255, 90, 60) : Color.FromArgb(al, 255, 236, 150),
                    red ? Color.FromArgb(al, 150, 10, 10) : Color.FromArgb(al, 200, 120, 20), 90f)) g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(al, 20, 10, 6), Math.Max(1f, size * .05f) / k)) g.DrawPath(pen, path);
                g.Restore(st);
            }
        }
    }
}
