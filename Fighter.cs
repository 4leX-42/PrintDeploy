using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace InstaladorImpresoras
{
    enum St { Idle, Act, Cling, Hit, AirHit, Down, GetUp, Block, Frozen, Pulled, Thrown, Dizzy, Win, Gone, Corpse }
    enum Kind { High, Mid, Low }
    enum Who { Scorpion, SubZero, Mileena, JohnnyCage, Baraka, Kratos }

    readonly struct Key
    {
        public readonly float[] P; public readonly float D;
        public Key(float[] p, float d) { P = p; D = d; }
    }

    /// <summary>Golpe o accion: poses clave, ventana activa con la articulacion que golpea, efecto del golpe y eventos en el tiempo.</summary>
    sealed class Move
    {
        public string Id; public Key[] Keys;
        public float A0 = -1, A1 = -1;          // ventana activa (s); A1 = 99 en los aereos: hasta aterrizar
        public int Limb = -1;                   // articulacion que golpea (indice de J)
        public float Reach = .07f;              // radio del golpe (en alturas H)
        public int Dmg; public float Stun = .3f, KbX, KbY; public Kind Kind = Kind.High;
        public bool Knock, Launch, Air, Hold;   // derriba, lanza al aire, accion aerea, no acaba sola
        public bool Spin;                       // el propio movimiento gira el cuerpo (rodar, voltereta): no se suaviza el giro
        public float Cool;                      // recarga (s)
        public (float t, Action<Fighter> f)[] Ev = new (float, Action<Fighter>)[0];
        public Action<Fighter> Tick;            // cada paso mientras dura
        public Action<Fighter> End;             // al acabar
        float dur = -1;
        public float Dur { get { if (dur < 0) { dur = 0; foreach (var k in Keys) dur += k.D; } return dur; } }
    }

    /// <summary>Paleta de un luchador.</summary>
    sealed class Look
    {
        public Color Suit, Acc, AccHi, Trim, Mask, Eye, Rim, Wrap, Skin;
        public int Style;                       // 0 ninja, 1 Mileena, 2 Johnny Cage, 3 Baraka, 4 Kratos
        public bool SkinTorso, SkinArms;
        public static readonly Look Scorpion = new Look
        {
            Suit = Color.FromArgb(34, 31, 29), Acc = Color.FromArgb(232, 176, 28), AccHi = Color.FromArgb(255, 224, 120), Trim = Color.FromArgb(128, 90, 24),
            Mask = Color.FromArgb(52, 49, 46), Eye = Color.FromArgb(255, 246, 214), Rim = Color.FromArgb(255, 140, 30), Wrap = Color.FromArgb(200, 150, 40)
        };
        public static readonly Look SubZero = new Look
        {
            Suit = Color.FromArgb(22, 28, 42), Acc = Color.FromArgb(38, 116, 214), AccHi = Color.FromArgb(150, 205, 255), Trim = Color.FromArgb(196, 210, 226),
            Mask = Color.FromArgb(156, 178, 204), Eye = Color.FromArgb(170, 235, 255), Rim = Color.FromArgb(110, 205, 255), Wrap = Color.FromArgb(62, 124, 200)
        };
        public static readonly Look Mileena = new Look
        {
            Suit = Color.FromArgb(196, 46, 156), Acc = Color.FromArgb(222, 70, 178), AccHi = Color.FromArgb(255, 150, 220), Trim = Color.FromArgb(42, 34, 40),
            Mask = Color.FromArgb(214, 52, 168), Eye = Color.FromArgb(255, 205, 70), Rim = Color.FromArgb(255, 90, 200), Wrap = Color.FromArgb(196, 46, 156),
            Skin = Color.FromArgb(214, 168, 132), Style = 1, SkinArms = true
        };
        // MKX: torso al aire, pantalon oscuro, guantes negros, gafas de sol; luz de borde verde (sombra)
        public static readonly Look Cage = new Look
        {
            Suit = Color.FromArgb(30, 32, 44), Acc = Color.FromArgb(40, 42, 56), AccHi = Color.FromArgb(200, 200, 210), Trim = Color.FromArgb(16, 16, 20),
            Mask = Color.FromArgb(20, 20, 24), Eye = Color.FromArgb(60, 255, 120), Rim = Color.FromArgb(70, 255, 130), Wrap = Color.FromArgb(24, 24, 28),
            Skin = Color.FromArgb(222, 172, 134), Style = 2, SkinTorso = true, SkinArms = true
        };
        // MKX: tarkatano de piel gris, cuchillas en los antebrazos, armadura oscura con tela roja
        public static readonly Look Baraka = new Look
        {
            Suit = Color.FromArgb(58, 46, 38), Acc = Color.FromArgb(150, 30, 28), AccHi = Color.FromArgb(210, 80, 60), Trim = Color.FromArgb(34, 28, 24),
            Mask = Color.FromArgb(150, 140, 120), Eye = Color.FromArgb(255, 210, 90), Rim = Color.FromArgb(255, 200, 120), Wrap = Color.FromArgb(70, 56, 44),
            Skin = Color.FromArgb(166, 154, 132), Style = 3, SkinArms = true
        };
        // invitado de MK9: piel ceniza con el tatuaje rojo, falda roja, bronce, piernas al aire con grebas
        public static readonly Look Kratos = new Look
        {
            Suit = Color.FromArgb(206, 200, 190), Acc = Color.FromArgb(168, 26, 22), AccHi = Color.FromArgb(230, 96, 64), Trim = Color.FromArgb(150, 108, 48),
            Mask = Color.FromArgb(150, 108, 48), Eye = Color.FromArgb(255, 184, 70), Rim = Color.FromArgb(255, 120, 40), Wrap = Color.FromArgb(128, 92, 42),
            Skin = Color.FromArgb(214, 208, 198), Style = 4, SkinTorso = true, SkinArms = true
        };
        public static readonly Look Shadow = new Look
        {
            Suit = Color.FromArgb(40, 200, 90), Acc = Color.FromArgb(60, 230, 110), AccHi = Color.FromArgb(160, 255, 190), Trim = Color.FromArgb(30, 160, 70),
            Mask = Color.FromArgb(60, 230, 110), Eye = Color.FromArgb(200, 255, 220), Rim = Color.FromArgb(200, 255, 220), Wrap = Color.FromArgb(40, 200, 90),
            Skin = Color.FromArgb(60, 220, 100), SkinArms = true, SkinTorso = true, Style = 2
        };
        public static readonly Look Ice = new Look
        {
            Suit = Color.FromArgb(140, 200, 238), Acc = Color.FromArgb(190, 230, 255), AccHi = Color.White, Trim = Color.FromArgb(230, 245, 255),
            Mask = Color.FromArgb(205, 236, 255), Eye = Color.FromArgb(235, 250, 255), Rim = Color.White, Wrap = Color.FromArgb(170, 215, 245)
        };
    }

    /// <summary>
    /// Poses (grados, mirando a la derecha): pelvis x e y (en H), torso (+ inclinado adelante), cabeza (sobre el torso), brazo delantero
    /// (hombro desde abajo + adelante, sobre el torso; codo), brazo trasero, pierna delantera (muslo desde abajo + adelante; rodilla), trasera.
    /// </summary>
    static class Poses
    {
        public static readonly float[]
            Stance = { 0, .44f, 8, -4, 35, 95, 15, 110, 22, 18, -18, 14 },
            Stance2 = { 0, .42f, 11, -2, 40, 98, 20, 112, 25, 30, -15, 26 },
            Walk1 = { 0, .44f, 10, -4, 30, 95, 25, 105, 32, 8, -28, 32 },
            Walk2 = { 0, .43f, 10, -4, 42, 92, 8, 110, -14, 32, 20, 6 },
            JabWind = { 0, .44f, 4, -4, 22, 125, 15, 110, 22, 18, -18, 14 },
            Jab = { 0, .44f, 16, -2, 88, 4, 22, 115, 26, 16, -22, 10 },
            Cross = { 0, .43f, 24, -2, 30, 110, 94, 4, 32, 24, -28, 8 },
            Chamber = { 0, .47f, -8, 4, 40, 90, 10, 100, 78, 100, -4, 4 },
            HighKick = { 0, .48f, -28, 10, 52, 80, -24, 60, 120, 4, -6, 4 },
            SweepWind = { 0, .26f, 30, -10, -10, 40, 30, 60, 50, 110, -30, 100 },
            Sweep = { 0, .2f, 36, -14, -30, 20, 72, 30, 90, 2, 40, 135 },
            UpperWind = { 0, .3f, 28, -10, 42, 100, -18, 95, 52, 92, -30, 82 },
            Upper = { 0, .52f, -12, -8, 20, 100, 168, 4, 32, 72, -12, 4 },
            Block = { 0, .42f, 0, 12, 72, 122, 62, 128, 22, 26, -16, 20 },
            CBlock = { 0, .26f, 12, 10, 72, 122, 62, 128, 62, 112, -20, 100 },
            HitHigh = { -.04f, .44f, -26, -22, -20, 40, -38, 30, 14, 16, -24, 10 },
            HitMid = { -.04f, .4f, 34, 24, 22, 30, 10, 42, 26, 32, -14, 26 },
            AirHit = { 0, .45f, -50, -25, -70, 20, -100, 10, 45, 30, -30, 40 },
            Down = { 0, .08f, -90, -5, -10, 20, 10, 10, 92, 4, 86, 10 },
            GetUp = { 0, .2f, 30, -10, 10, 20, 40, 10, 75, 120, -10, 100 },
            Tuck = { 0, .44f, 12, -10, 50, 100, 40, 110, 86, 122, 40, 120 },
            JumpKick = { 0, .44f, -16, 6, 25, 70, -40, 40, 96, 2, -22, 96 },
            DiveKick = { 0, .44f, -34, 10, 30, 80, -30, 50, 55, 2, -10, 100 },
            Cling = { 0, .44f, 6, -6, 40, 95, -82, 20, 62, 112, -88, 40 },
            Grab = { 0, .44f, 18, 0, 80, 20, 76, 24, 26, 18, -24, 14 },
            Toss = { 0, .46f, -30, -10, 165, 10, 172, 10, 15, 15, -22, 10 },
            SpearWind = { 0, .44f, -14, -6, -70, 60, 30, 100, 24, 16, -22, 14 },
            SpearThrow = { 0, .44f, 22, 0, 92, 2, -20, 60, 32, 10, -26, 14 },
            Pull = { -.04f, .44f, -22, -4, 55, 95, 20, 100, 36, 22, -32, 10 },
            TeleCrouch = { 0, .3f, 30, 10, 72, 140, 72, 140, 52, 100, -20, 90 },
            TelePunch = { 0, .44f, 42, 8, 118, 2, -40, 40, 10, 40, -40, 60 },
            CastUp = { 0, .46f, -12, -14, 160, 20, 166, 20, 22, 18, -18, 14 },
            CastSlam = { 0, .3f, 32, 12, 70, 10, 78, 10, 52, 92, -24, 72 },
            IceWind = { 0, .44f, -12, -4, -20, 90, -30, 100, 26, 20, -22, 14 },
            IceThrow = { 0, .43f, 18, 0, 84, 8, 78, 14, 30, 16, -26, 14 },
            Slide = { 0, .13f, -42, 10, 40, 60, -110, 10, 88, 2, 62, 120 },
            CloneCast = { 0, .44f, 0, 0, 78, 140, 78, 140, 22, 18, -18, 14 },
            WinFire = { 0, .46f, -2, -6, 62, 122, 64, 118, 14, 6, -12, 6 },
            WinIce = { 0, .46f, -6, -10, 165, 20, 20, 110, 16, 6, -14, 6 },
            Dizzy = { 0, .43f, -8, 16, -10, 20, -15, 25, 18, 12, -16, 14 },
            Dizzy2 = { 0, .42f, 10, -12, 5, 30, -25, 20, 14, 18, -20, 10 },
            Breath = { 0, .43f, 26, 18, -30, 30, -40, 30, 30, 20, -26, 14 },
            SwordUp = { 0, .44f, -12, -6, 150, 60, 20, 100, 26, 20, -24, 14 },
            Slash = { 0, .4f, 28, 4, 95, 2, -30, 60, 40, 30, -30, 20 },
            RipReach = { 0, .44f, 14, 0, 105, 30, 20, 100, 30, 20, -26, 14 },
            RipLift = { 0, .46f, -14, -10, 172, 8, 30, 100, 16, 8, -16, 8 },
            Splits = { 0, .06f, 10, 0, 40, 95, 20, 100, 90, 0, -90, 0 },
            SplitPunch = { 0, .06f, 22, 4, 62, 2, 20, 100, 90, 0, -90, 0 },
            Spin1 = { 0, .44f, 6, 0, 92, 4, -92, 4, 26, 18, -22, 14 },
            Spin2 = { 0, .44f, 6, 0, -92, 4, 92, 4, 22, 22, -26, 10 },
            Kneel = { 0, .25f, 22, 20, 0, 10, -10, 10, 82, 112, -10, 100 },
            DownFwd = { 0, .08f, 90, 10, 100, 10, 90, 10, -88, 4, -84, 10 },
            Land = { 0, .32f, 18, 0, 40, 90, 20, 100, 52, 82, -30, 72 },
            // guardias propias (dos poses que respiran)
            SzStance = { 0, .44f, 4, -2, 52, 70, 32, 88, 20, 16, -20, 12 }, SzStance2 = { 0, .43f, 6, -2, 56, 66, 36, 84, 22, 22, -18, 18 },
            MiStance = { 0, .38f, 16, -8, 62, 78, 44, 92, 34, 40, -24, 34 }, MiStance2 = { 0, .37f, 18, -6, 66, 72, 40, 98, 36, 46, -22, 40 },
            JcStance = { 0, .45f, 2, 4, 30, 122, 22, 126, 18, 12, -16, 10 }, JcStance2 = { 0, .43f, 4, 6, 28, 126, 20, 130, 20, 24, -14, 22 },
            BaStance = { 0, .37f, 26, -16, 72, 38, 52, 48, 36, 44, -26, 38 }, BaStance2 = { 0, .36f, 28, -12, 76, 34, 56, 44, 38, 50, -24, 44 },
            KrStance = { 0, .41f, 14, -8, 26, 42, 12, 52, 36, 38, -32, 32 }, KrStance2 = { 0, .4f, 16, -6, 30, 38, 16, 48, 38, 44, -30, 38 },
            // especiales nuevos
            TwinDraw = { 0, .44f, -8, -6, 170, 30, 175, 30, 24, 18, -22, 14 },
            TwinCross = { 0, .42f, 30, 6, 100, 4, 110, 4, 40, 30, -30, 20 },
            HammerUp = { 0, .46f, -18, -14, 175, 4, 172, 6, 22, 14, -20, 10 },
            HammerSlam = { 0, .26f, 46, 20, 78, 4, 82, 4, 60, 100, -26, 84 },
            Flip = { 0, .44f, -10, 0, -40, 60, -50, 60, 120, 6, 40, 110 },
            LiftStab = { 0, .44f, 10, -4, 120, 10, 30, 100, 30, 20, -26, 14 };
    }

    /// <summary>
    /// Luchador: esqueleto 2D por cinematica directa (poses clave interpoladas), fisica simple, repertorio de golpes y especiales, IA que
    /// elige segun distancia y estado del rival sin repetir, y dibujo por partes con luz, contorno, luz de su elemento, faldon y cintas.
    /// Coordenadas del escenario: X desde la pared izquierda, Y altura sobre el suelo.
    /// </summary>
    sealed class Fighter
    {
        public readonly Arena A; public readonly Who Who; public readonly string Name; public readonly Look Look;
        public bool Fire => Who == Who.Scorpion;
        public float Form = 1;                  // "dia" de la ronda: multiplica el daño que hace (0,85..1,15, al azar)
        public Fighter Opp;
        public float X, Y, VX, VY, Rot; public int Face = 1;
        public float Hp = 100, ShownHp = 100; public int Wins; public bool Dead, Fatal, Hidden;
        public float Char, Fade = 1;            // calcinado (remate de fuego) y aparicion
        public bool Headless; public int Blade; // sin cabeza (remate); arma en la mano: 1 espada, 2 hoja de hielo
        string fatalId;                         // remate elegido para este FINISH HIM
        public float SplitT; public bool SplitH; public int SplitDir;   // partido en dos: tiempo desde el corte, por la cintura, hacia donde sale el tronco
        public St State; public float T, Timer;
        public Move Mv; public bool MvHit, Crouch;
        public string Queued;
        int ev, bounces; bool grabbed, usedSpear; float think;
        readonly Dictionary<string, float> coolUntil = new Dictionary<string, float>();
        readonly List<string> hist = new List<string>();
        public readonly float[] Pose = (float[])Poses.Stance.Clone();
        readonly float[] from = new float[12];
        Key[] anim; float animT; bool loop;
        public readonly PointF[] J = new PointF[18];   // esqueleto en pantalla
        public RectangleF Box;
        float clothA, clothV, backA, backV;
        readonly PointF[] sash = new PointF[5]; bool sashInit;

        float H => A.H;
        public bool Grounded => Y <= .001f;
        public bool Busy => State == St.Gone || Hidden;

        public Fighter(Arena a, Who who)
        {
            A = a; Who = who;
            switch (who)
            {
                case Who.Scorpion: Name = "SCORPION"; Look = Look.Scorpion; break;
                case Who.SubZero: Name = "SUB-ZERO"; Look = Look.SubZero; break;
                case Who.Mileena: Name = "MILEENA"; Look = Look.Mileena; break;
                case Who.JohnnyCage: Name = "JOHNNY CAGE"; Look = Look.Cage; break;
                case Who.Baraka: Name = "BARAKA"; Look = Look.Baraka; break;
                default: Name = "KRATOS"; Look = Look.Kratos; break;
            }
            Key = who.ToString().ToLowerInvariant();
            switch (who)
            {
                case Who.SubZero: idleK = K(Poses.SzStance, .5f, Poses.SzStance2, .5f); break;
                case Who.Mileena: idleK = K(Poses.MiStance, .36f, Poses.MiStance2, .36f); break;
                case Who.JohnnyCage: idleK = K(Poses.JcStance, .26f, Poses.JcStance2, .26f); break;   // boxeador: rebota rapido
                case Who.Baraka: idleK = K(Poses.BaStance, .55f, Poses.BaStance2, .55f); break;
                case Who.Kratos: idleK = K(Poses.KrStance, .6f, Poses.KrStance2, .6f); break;
                default: idleK = IdleK; break;
            }
        }

        public readonly string Key;             // prefijo de sus sonidos (scorpion_hit, baraka_ko...)
        bool impaled;
        float whip;                             // latigazo de las Espadas del Caos (0..1)
        public float Storm = -1;                // tormenta de espadas: angulo de giro (grados); -1 parado
        public bool AxeOut;                     // el hacha Leviatan esta en el aire: mano vacia hasta que vuelva
        public float Flash;                     // destello blanco al recibir un golpe
        bool twin;                              // Scorpion con las dos espadas
        Key[] idleK;                            // guardia propia
        // estela del golpe en curso (coordenadas del escenario, la mas reciente al final)
        readonly PointF[] trail = new PointF[9]; int trailN;

        int DefaultBlade => Who == Who.Mileena ? 3 : Who == Who.Baraka ? 4 : Who == Who.Kratos ? (AxeOut ? 0 : 5) : 0;

        /// <summary>
        /// Corta lo que el movimiento en curso dejaba a medias (giro de espadas, latigazo, agarre, ensarte, armas temporales) cuando le
        /// golpean, le congelan o empieza otra cosa: sin esto, la tormenta de Kratos o el arma invocada seguian dibujandose.
        /// </summary>
        void Interrupt()
        {
            Storm = -1; whip = 0; twin = false;
            if (impaled) { impaled = false; if (Opp.State == St.Thrown) Opp.Launch(4, Face * 2 * H, 2 * H); }
            if (grabbed) { grabbed = false; if (Opp.State == St.Thrown) Opp.Launch(0, Face * H, 2 * H); }
            Blade = DefaultBlade;
        }

        // ---------- repertorio ----------
        static Key[] K(params object[] kv)
        {
            var k = new Key[kv.Length / 2];
            for (int i = 0; i < k.Length; i++) k[i] = new Key((float[])kv[i * 2], Convert.ToSingle(kv[i * 2 + 1]));
            return k;
        }
        static readonly Key[] IdleK = K(Poses.Stance, .42f, Poses.Stance2, .42f), BlockK = K(Poses.Block, .07f), CBlockK = K(Poses.CBlock, .07f),
            HitHighK = K(Poses.HitHigh, .06f, Poses.Stance, .3f), HitMidK = K(Poses.HitMid, .06f, Poses.Stance, .3f), AirHitK = K(Poses.AirHit, .08f),
            DownK = K(Poses.Down, .12f), GetUpK = K(Poses.GetUp, .2f, Poses.Stance, .2f), DizzyK = K(Poses.Dizzy, .5f, Poses.Dizzy2, .5f),
            ClingK = K(Poses.Cling, .06f), PullK = K(Poses.Pull, .1f);

        static readonly Dictionary<string, Move> Moves = BuildMoves();

        static Dictionary<string, Move> BuildMoves()
        {
            var m = new Dictionary<string, Move>();
            void Add(Move x) => m[x.Id] = x;
            // basicos
            Add(new Move { Id = "jab", Keys = K(Poses.JabWind, .07, Poses.Jab, .07, Poses.Stance, .15), A0 = .07f, A1 = .15f, Limb = 6, Dmg = 4, Stun = .3f, KbX = 1.4f,
                End = f => { if (f.MvHit && f.A.Rnd() < .75f) f.Queued = "cross"; } });
            Add(new Move { Id = "cross", Keys = K(Poses.Stance, .04, Poses.Cross, .08, Poses.Stance, .16), A0 = .04f, A1 = .13f, Limb = 9, Dmg = 5, Stun = .32f, KbX = 1.8f, Kind = Kind.Mid,
                End = f => { if (f.MvHit && f.A.Rnd() < .6f) f.Queued = f.A.Rnd() < .5f ? "highkick" : "uppercut"; } });
            Add(new Move { Id = "highkick", Keys = K(Poses.Chamber, .1, Poses.HighKick, .09, Poses.Chamber, .08, Poses.Stance, .14), A0 = .1f, A1 = .22f, Limb = 12, Reach = .09f,
                Dmg = 8, KbX = 3.5f, Stun = .4f });
            Add(new Move { Id = "sweep", Keys = K(Poses.SweepWind, .1, Poses.Sweep, .12, Poses.SweepWind, .1, Poses.Stance, .14), A0 = .12f, A1 = .25f, Limb = 13, Reach = .09f,
                Dmg = 7, KbX = .8f, Kind = Kind.Low, Knock = true });
            Add(new Move { Id = "uppercut", Keys = K(Poses.UpperWind, .12, Poses.Upper, .1, Poses.Upper, .08, Poses.Stance, .2), A0 = .12f, A1 = .26f, Limb = 9, Reach = .09f,
                Dmg = 12, KbX = 1.2f, KbY = 10, Kind = Kind.Mid, Launch = true, Ev = new (float, Action<Fighter>)[] { (.12f, f => f.VY = 3 * f.H) } });
            Add(new Move { Id = "throw", Keys = K(Poses.Grab, .12, Poses.Toss, .32, Poses.Stance, .16), Cool = 2.5f,
                Ev = new (float, Action<Fighter>)[] { (.12f, f => f.TryGrab()), (.44f, f => f.Release()) } });
            Add(new Move { Id = "walk", Keys = K(Poses.Walk1, .14, Poses.Walk2, .14, Poses.Walk1, .14, Poses.Walk2, .14),
                Ev = new (float, Action<Fighter>)[] { (0, f => f.VX = f.Face * 2.4f * f.H), (.54f, f => f.VX = 0) } });
            Add(new Move { Id = "dash", Keys = K(Poses.Walk2, .08, Poses.Walk1, .08, Poses.Stance, .1),
                Ev = new (float, Action<Fighter>)[] { (0, f => { f.VX = f.Face * 7 * f.H; f.A.Dust(f.X, 4); }), (.18f, f => f.VX = 0) } });
            Add(new Move { Id = "backdash", Keys = K(Poses.Tuck, .12, Poses.Stance, .2),
                Ev = new (float, Action<Fighter>)[] { (0, f => { f.VX = -f.Face * 5.5f * f.H; f.VY = 3 * f.H; }) } });
            Add(new Move { Id = "hop", Keys = K(Poses.Tuck, .2, Poses.Tuck, 9), Air = true,
                Ev = new (float, Action<Fighter>)[] { (0, f => { f.VY = 9.5f * f.H; f.VX = f.Face * 3.5f * f.H; }) } });
            Add(new Move { Id = "jumpkick", Keys = K(Poses.Tuck, .16, Poses.JumpKick, 9), A0 = .16f, A1 = 99, Limb = 12, Reach = .09f, Dmg = 9, KbX = 3.2f, Air = true,
                Ev = new (float, Action<Fighter>)[] { (0, f => { f.VY = 8.5f * f.H; f.VX = f.Face * 4.6f * f.H; }) } });
            Add(new Move { Id = "wallrun", Keys = K(Poses.Tuck, .2, Poses.Tuck, 9), Air = true, Cool = 3,
                Ev = new (float, Action<Fighter>)[] { (0, f => f.ToWall()) } });
            Add(new Move { Id = "divekick", Keys = K(Poses.DiveKick, .08, Poses.DiveKick, 9), A0 = .05f, A1 = 99, Limb = 12, Reach = .1f, Dmg = 10, KbX = 4, Air = true, Knock = true });
            Add(new Move { Id = "land", Keys = K(Poses.Land, .08, Poses.Stance, .12) });
            // Scorpion
            Add(new Move { Id = "spear", Keys = K(Poses.SpearWind, .16, Poses.SpearThrow, .08), Hold = true, Cool = 4,
                Ev = new (float, Action<Fighter>)[] { (0, f => f.A.Callout(f, "GET OVER HERE!")), (.16f, f => f.A.ThrowChain(f)) } });
            Add(new Move { Id = "teleport", Keys = K(Poses.TeleCrouch, .14, Poses.TeleCrouch, .26, Poses.TelePunch, .1, Poses.TelePunch, 9), A0 = .42f, A1 = 99, Limb = 6, Reach = .1f,
                Dmg = 10, KbX = 3, Knock = true, Air = true, Cool = 4.5f,
                Ev = new (float, Action<Fighter>)[] { (.12f, f => { f.A.Burst(f, true); f.Hidden = true; f.A.Meme("enrique", .2f, .1f); }), (.4f, f => f.Reappear(false)) } });
            Add(new Move { Id = "hellfire", Keys = K(Poses.CastUp, .2, Poses.CastSlam, .12, Poses.Stance, .3), Cool = 5,
                Ev = new (float, Action<Fighter>)[] { (.3f, f => f.A.Pillar(f)) } });
            Add(new Move { Id = "fatalFire", Keys = K(Poses.Breath, .25, Poses.Breath, 1.5, Poses.WinFire, .5), Hold = true,
                Tick = f => f.BreathFire(), Ev = new (float, Action<Fighter>)[] { (1.75f, f => { f.A.Ashes(f.Opp); f.A.Fatality(f); }), (2.2f, f => f.Win()) } });
            // Sub-Zero
            Add(new Move { Id = "iceball", Keys = K(Poses.IceWind, .16, Poses.IceThrow, .1, Poses.Stance, .25), Cool = 3.5f,
                Ev = new (float, Action<Fighter>)[] { (.18f, f => f.A.IceBall(f)) } });
            Add(new Move { Id = "slide", Keys = K(Poses.Slide, .06, Poses.Slide, .32, Poses.Stance, .16), A0 = .06f, A1 = .36f, Limb = 12, Reach = .09f, Dmg = 8, KbX = 1,
                Kind = Kind.Low, Knock = true, Cool = 2.5f,
                Ev = new (float, Action<Fighter>)[] { (0, f => { f.VX = f.Face * 9.5f * f.H; f.A.Dust(f.X, 6); }), (.38f, f => f.VX = 0) },
                Tick = f => { if (f.T < .38f) f.A.Mist(f.J[12], 1); } });
            Add(new Move { Id = "clone", Keys = K(Poses.CloneCast, .18, Poses.Stance, .3), Cool = 6,
                Ev = new (float, Action<Fighter>)[] { (.16f, f => f.A.MakeClone(f)) } });
            Add(new Move { Id = "puddle", Keys = K(Poses.CastSlam, .2, Poses.Stance, .25), Cool = 6,
                Ev = new (float, Action<Fighter>)[] { (.18f, f => f.A.Puddle(f)) } });
            Add(new Move { Id = "freezeBlast", Keys = K(Poses.IceWind, .2, Poses.IceThrow, .7, Poses.Stance, .2),
                Tick = f => { if (f.T > .2f && f.T < .85f) f.A.FrostStream(f); }, Ev = new (float, Action<Fighter>)[] { (.75f, f => f.Opp.Freeze(0)) } });
            Add(new Move { Id = "fatalIce", Keys = K(Poses.UpperWind, .2, Poses.Upper, .15, Poses.Upper, .25, Poses.WinIce, .5), Hold = true,
                Ev = new (float, Action<Fighter>)[] { (.24f, f => { f.A.Shatter(f.Opp, true); f.A.Fatality(f); }), (1.2f, f => f.Win()) } });
            // remates con sangre: Scorpion decapita con la espada, Sub-Zero arranca la cabeza con la columna
            Add(new Move { Id = "fatalSword", Keys = K(Poses.SwordUp, .4, Poses.SwordUp, .25, Poses.Slash, .08, Poses.Slash, .8, Poses.WinFire, .5), Hold = true,
                Ev = new (float, Action<Fighter>)[] { (0, f => f.Blade = 1), (.7f, f => f.A.Decapitate(f.Opp, f, false)), (1.55f, f => f.Blade = 0), (1.9f, f => f.Win()) } });
            // Scorpion: desenvaina las dos espadas y lo parte en dos de arriba abajo
            Add(new Move { Id = "fatalSplit", Keys = K(Poses.TwinDraw, .3, Poses.TwinDraw, .15, Poses.SwordUp, .25, Poses.Slash, .08, Poses.Slash, .9, Poses.WinFire, .5), Hold = true,
                Tick = f => { if (f.T > .6f && f.T < .8f) { f.A.Trail(f, f.WeaponTip(true), Color.FromArgb(255, 140, 30)); f.A.Trail(f, f.WeaponTip(false), Color.FromArgb(255, 140, 30)); } },
                Ev = new (float, Action<Fighter>)[] { (.02f, f => { f.Blade = 1; f.twin = true; f.A.Sound("slash", .7f); for (int q = 0; q < 3; q++) { f.A.Trail(f, f.J[6]); f.A.Trail(f, f.J[9]); } }),
                    (.72f, f => f.A.SplitBody(f.Opp, f, false)), (1.75f, f => { f.Blade = 0; f.twin = false; }), (2.1f, f => f.Win()) } });
            // Sub-Zero: martillo de hielo sobre el rival congelado; no queda nada entero
            Add(new Move { Id = "fatalHammer", Keys = K(Poses.HammerUp, .35, Poses.HammerUp, .12, Poses.HammerSlam, .08, Poses.HammerSlam, .7, Poses.WinIce, .5), Hold = true,
                Tick = f => { if (f.T < .35f) f.A.FrostSpark(f.J[6]); },
                Ev = new (float, Action<Fighter>)[] { (.04f, f => f.A.Sound("ice", .7f)), (.3f, f => { f.Blade = 7; f.A.FrostBurstAt(f.J[6]); }), (.5f, f => f.A.Gib(f.Opp, f, true)),
                    (1.25f, f => { f.Blade = 0; f.A.FrostBurstAt(f.J[6]); }), (1.6f, f => f.Win()) } });
            Add(new Move { Id = "fatalRip", Keys = K(Poses.RipReach, .3, Poses.RipReach, .2, Poses.RipLift, .3, Poses.RipLift, 9), Hold = true,
                Ev = new (float, Action<Fighter>)[] { (.5f, f => f.A.Decapitate(f.Opp, f, true)) } });
            // Mileena
            Add(new Move { Id = "sai", Keys = K(Poses.IceWind, .14, Poses.Jab, .08, Poses.Stance, .22), Cool = 2.5f,
                Ev = new (float, Action<Fighter>)[] { (.15f, f => f.A.Sai(f)) } });
            Add(new Move { Id = "roll", Keys = K(Poses.Tuck, .08, Poses.Tuck, .5, Poses.Stance, .15), A0 = .08f, A1 = .55f, Limb = 0, Reach = .3f, Dmg = 9, KbX = 3, Kind = Kind.Low,
                Knock = true, Cool = 3, Spin = true,
                Ev = new (float, Action<Fighter>)[] { (.06f, f => { f.VX = f.Face * 8.5f * f.H; f.A.Sound("swing"); }), (.58f, f => f.VX = 0) },
                Tick = f => { if (f.T > .06f && f.T < .58f) { f.Rot = f.Face * (f.T - .06f) * 1300; if (f.A.Rnd() < .5f) f.A.Trail(f, f.J[0], Color.FromArgb(255, 90, 200)); } } });
            Add(new Move { Id = "teleKick", Keys = K(Poses.TeleCrouch, .14, Poses.TeleCrouch, .26, Poses.DiveKick, .1, Poses.DiveKick, 9), A0 = .42f, A1 = 99, Limb = 12, Reach = .1f,
                Dmg = 10, KbX = 3.5f, Knock = true, Air = true, Cool = 4,
                Ev = new (float, Action<Fighter>)[] { (.12f, f => { f.A.PinkBurst(f); f.Hidden = true; }), (.4f, f => f.Reappear(true)) } });
            Add(new Move { Id = "fatalSai", Keys = K(Poses.RipReach, .2, Poses.Jab, .08, Poses.Cross, .08, Poses.Jab, .08, Poses.Cross, .08, Poses.RipReach, .2, Poses.RipLift, .3, Poses.RipLift, 9), Hold = true,
                Ev = new (float, Action<Fighter>)[] { (.24f, f => f.A.Stab(f)), (.36f, f => f.A.Stab(f)), (.48f, f => f.A.Stab(f)), (.6f, f => f.A.Stab(f)), (.9f, f => f.A.Decapitate(f.Opp, f, true)) } });
            // Mileena: se teletransporta a su espalda, lo agarra y lo arranca por la cintura
            Add(new Move { Id = "fatalTear", Keys = K(Poses.TeleCrouch, .14, Poses.TeleCrouch, .26, Poses.Grab, .25, Poses.RipLift, .35, Poses.RipLift, .9, Poses.WinFire, .4), Hold = true,
                Ev = new (float, Action<Fighter>)[] { (.12f, f => { f.A.PinkBurst(f); f.Hidden = true; }), (.4f, f => f.Behind()), (.85f, f => f.A.SplitBody(f.Opp, f, true)), (2.2f, f => f.Win()) } });
            // Johnny Cage
            Add(new Move { Id = "shadowKick", Keys = K(Poses.Chamber, .06, Poses.JumpKick, .34, Poses.Stance, .14), A0 = .06f, A1 = .4f, Limb = 12, Reach = .1f, Dmg = 9, KbX = 4,
                Knock = true, Cool = 2.5f, Ev = new (float, Action<Fighter>)[] { (0, f => f.VX = f.Face * 10 * f.H), (.4f, f => f.VX = 0) },
                Tick = f => { if (f.T < .4f) f.A.Ghost(f); } });
            Add(new Move { Id = "forceBall", Keys = K(Poses.IceWind, .14, Poses.IceThrow, .1, Poses.Stance, .22), Cool = 3,
                Ev = new (float, Action<Fighter>)[] { (.15f, f => f.A.Shot(f, 2)) } });
            Add(new Move { Id = "nutPunch", Keys = K(Poses.Splits, .1, Poses.SplitPunch, .1, Poses.Splits, .3, Poses.Stance, .2), A0 = .1f, A1 = .22f, Limb = 6, Reach = .1f,
                Dmg = 7, Stun = .9f, KbX = .5f, Kind = Kind.Mid, Cool = 4 });
            Add(new Move { Id = "shadowUpper", Keys = K(Poses.UpperWind, .1, Poses.Upper, .1, Poses.Upper, .08, Poses.Stance, .2), A0 = .1f, A1 = .26f, Limb = 9, Reach = .1f,
                Dmg = 11, KbX = 1.2f, KbY = 10, Kind = Kind.Mid, Launch = true, Cool = 3,
                Ev = new (float, Action<Fighter>)[] { (.1f, f => { f.VY = 4 * f.H; f.VX = f.Face * 2 * f.H; }) }, Tick = f => { if (f.T < .3f) f.A.Ghost(f); } });
            Add(new Move { Id = "fatalCage", Keys = K(Poses.UpperWind, .3, Poses.Upper, .1, Poses.Upper, .7, Poses.WinFire, .4), Hold = true,
                Ev = new (float, Action<Fighter>)[] { (.32f, f => f.A.Decapitate(f.Opp, f, false)), (1.4f, f => f.Win()) } });
            // Johnny Cage: el puñetazo bajo de siempre, pero esta vez lo revienta entero
            Add(new Move { Id = "fatalNut", Keys = K(Poses.Splits, .15, Poses.SplitPunch, .1, Poses.SplitPunch, .7, Poses.Splits, .3, Poses.WinIce, .5), Hold = true,
                Ev = new (float, Action<Fighter>)[] { (.02f, f => f.A.Meme("heehee", 1, 0, true)), (.22f, f => { f.A.Sound("nutpunch"); f.A.Gib(f.Opp, f, false); }), (1.75f, f => f.Win()) } });
            // Baraka
            Add(new Move { Id = "bladeSpin", Keys = K(Poses.Spin1, .07, Poses.Spin2, .07, Poses.Spin1, .07, Poses.Spin2, .07, Poses.Spin1, .07, Poses.Spin2, .07, Poses.Stance, .15),
                A0 = .05f, A1 = .42f, Limb = 6, Reach = .3f, Dmg = 10, KbX = 3, Knock = true, Cool = 3,
                Ev = new (float, Action<Fighter>)[] { (0, f => { f.VX = f.Face * 5 * f.H; f.A.Sound("swing"); }), (.42f, f => f.VX = 0) },
                Tick = f => { if (f.T < .42f) f.A.BladeSparks(f); } });
            Add(new Move { Id = "bladeSpark", Keys = K(Poses.CloneCast, .18, Poses.IceThrow, .1, Poses.Stance, .22), Cool = 3,
                Ev = new (float, Action<Fighter>)[] { (.2f, f => f.A.Shot(f, 3)) } });
            Add(new Move { Id = "chopChop", Keys = K(Poses.Jab, .06, Poses.Cross, .06, Poses.Jab, .06, Poses.Cross, .06, Poses.Jab, .06, Poses.Stance, .15), A0 = .03f, A1 = .32f,
                Limb = 6, Reach = .3f, Dmg = 12, Stun = .5f, KbX = 2, Kind = Kind.Mid, Cool = 2 });
            Add(new Move { Id = "bladeStab", Keys = K(Poses.JabWind, .12, Poses.Jab, .08, Poses.Stance, .2), A0 = .12f, A1 = .2f, Limb = 6, Reach = .36f, Dmg = 9, KbX = 2.5f });
            Add(new Move { Id = "fatalBaraka", Keys = K(Poses.JabWind, .2, Poses.Jab, .1, Poses.RipLift, .5, Poses.RipLift, .7, Poses.Slash, .1, Poses.Slash, .5, Poses.WinFire, .4), Hold = true,
                Tick = f => f.CarryImpaled(),
                Ev = new (float, Action<Fighter>)[] { (.28f, f => f.Impale()), (1.5f, f => { f.impaled = false; f.A.Decapitate(f.Opp, f, false); }), (2.2f, f => f.Win()) } });
            // Baraka: lluvia de cuchilladas hasta dejarlo en trozos
            {
                var ev = new List<(float, Action<Fighter>)>();
                for (int q = 0; q < 8; q++) ev.Add((.05f + q * .07f, f => { f.A.Stab(f); f.A.Sound("slash", .55f); }));
                ev.Add((.72f, f => f.A.Gib(f.Opp, f, false))); ev.Add((1.6f, f => f.Win()));
                Add(new Move { Id = "fatalChop", Keys = K(Poses.Jab, .07, Poses.Cross, .07, Poses.Jab, .07, Poses.Cross, .07, Poses.Jab, .07, Poses.Cross, .07, Poses.Jab, .07, Poses.Cross, .07,
                    Poses.Slash, .1, Poses.Slash, .5, Poses.WinFire, .4), Hold = true, Ev = ev.ToArray() });
            }
            AddKratos(m);
            // Scorpion: desenvaina las dos espadas a la espalda entre llamas y corta en cruz
            Add(new Move { Id = "twinSwords", Keys = K(Poses.TwinDraw, .2, Poses.TwinDraw, .06, Poses.TwinCross, .08, Poses.TwinCross, .14, Poses.Stance, .2), A0 = .26f, A1 = .4f, Limb = 6, Reach = .55f,
                Dmg = 11, KbX = 3.5f, Kind = Kind.Mid, Cool = 4,
                Ev = new (float, Action<Fighter>)[] { (.02f, f => { f.Blade = 1; f.twin = true; f.A.Sound("slash", .7f); for (int q = 0; q < 3; q++) { f.A.Trail(f, f.J[6]); f.A.Trail(f, f.J[9]); } }), (.62f, f => { f.Blade = 0; f.twin = false; }) },
                Tick = f => { if (f.T > .2f && f.T < .42f) { f.A.Trail(f, f.WeaponTip(true), Color.FromArgb(255, 140, 30)); f.A.Trail(f, f.WeaponTip(false), Color.FromArgb(255, 140, 30)); } } });
            // Sub-Zero: martillo de hielo que se forma en las manos y revienta el suelo con estacas
            Add(new Move { Id = "iceHammer", Keys = K(Poses.HammerUp, .3, Poses.HammerUp, .08, Poses.HammerSlam, .08, Poses.HammerSlam, .3, Poses.Stance, .2), Cool = 5,
                Tick = f => { if (f.T < .3f) f.A.FrostSpark(f.J[6]); },
                Ev = new (float, Action<Fighter>)[] { (.04f, f => f.A.Sound("ice", .7f)), (.26f, f => { f.Blade = 7; f.A.FrostBurstAt(f.J[6]); }), (.44f, f => f.A.IceQuake(f)), (.8f, f => { f.Blade = 0; f.A.FrostBurstAt(f.J[6]); }) } });
            // Mileena: salta y lanza un sai en diagonal hacia abajo
            Add(new Move { Id = "airSai", Keys = K(Poses.Tuck, .22, Poses.IceThrow, .1, Poses.Tuck, 9), Air = true, Cool = 3,
                Ev = new (float, Action<Fighter>)[] { (0, f => { f.VY = 9 * f.H; f.VX = -f.Face * 1.2f * f.H; }), (.26f, f => f.A.Sai(f, true)) } });
            // Johnny Cage: voltereta hacia atras con la pierna estirada que lanza por los aires, con estela verde
            Add(new Move { Id = "flipKick", Keys = K(Poses.Chamber, .08, Poses.Flip, .3, Poses.Tuck, .12, Poses.Land, .1, Poses.Stance, .12), A0 = .1f, A1 = .36f, Limb = 12, Reach = .12f,
                Dmg = 10, KbX = 1.5f, KbY = 9, Launch = true, Kind = Kind.Mid, Cool = 3, Spin = true,
                Ev = new (float, Action<Fighter>)[] { (.06f, f => { f.VY = 6.5f * f.H; f.VX = -f.Face * 1.2f * f.H; f.A.Sound("swing"); f.A.Meme("heehee", .45f, .3f); }) },
                Tick = f => { if (f.T > .06f && f.T < .5f) { f.Rot = -f.Face * Math.Min(360, (f.T - .06f) / .44f * 360); if (((int)(f.T * 60)) % 3 == 0) f.A.Ghost(f); } } });
            // Baraka: ensarta al rival en las cuchillas, lo levanta y lo lanza a su espalda
            Add(new Move { Id = "bladeLift", Keys = K(Poses.JabWind, .12, Poses.Jab, .08, Poses.LiftStab, .35, Poses.Toss, .15, Poses.Stance, .2), Cool = 5,
                Tick = f => f.CarryImpaled(),
                Ev = new (float, Action<Fighter>)[] { (.16f, f => { if (Math.Abs(f.Opp.X - f.X) < .8f * f.H && f.Opp.Hittable && f.Opp.Grounded && f.Opp.State != St.Block) f.Impale(); else f.A.Meme("mecago|mecagoleche", .4f, .2f); }),
                    (.6f, f => { if (!f.impaled) return; f.impaled = false; f.Opp.X = f.X - f.Face * .4f * f.H; f.Opp.Launch(12, -f.Face * 6 * f.H, 5 * f.H); f.A.Stab(f); f.A.Kick(4); f.A.Sound("heavyhit"); }) } });
            return m;
        }

        // Kratos
        static void AddKratos(Dictionary<string, Move> m)
        {
            void Add(Move x) => m[x.Id] = x;
            Add(new Move { Id = "chaosWhip", Keys = K(Poses.SpearWind, .14, Poses.SpearThrow, .12, Poses.SpearThrow, .1, Poses.Stance, .2), A0 = .14f, A1 = .32f, Limb = 6, Reach = .9f,
                Dmg = 9, KbX = 3, Kind = Kind.Mid, Cool = 2,
                Tick = f => { f.whip = f.T > .1f && f.T < .38f ? (float)Math.Sin((f.T - .1f) / .28f * Math.PI) : 0; if (f.whip > .3f) f.A.Trail(f, f.J[6]); } });
            Add(new Move { Id = "apollo", Keys = K(Poses.IceWind, .2, Poses.IceThrow, .1, Poses.Stance, .22), Cool = 3,
                Ev = new (float, Action<Fighter>)[] { (.2f, f => f.A.Shot(f, 4)) } });
            Add(new Move { Id = "hermes", Keys = K(Poses.Walk2, .06, Poses.JumpKick, .3, Poses.Stance, .14), A0 = .06f, A1 = .36f, Limb = 12, Reach = .1f, Dmg = 9, KbX = 4,
                Knock = true, Cool = 2.5f, Ev = new (float, Action<Fighter>)[] { (0, f => f.VX = f.Face * 11 * f.H), (.36f, f => f.VX = 0) },
                Tick = f => { if (f.T < .36f) f.A.Trail(f, f.J[12]); } });
            Add(new Move { Id = "spartan", Keys = K(Poses.Grab, .1, Poses.Grab, .25, Poses.Stance, .15), A0 = .08f, A1 = .35f, Limb = 4, Reach = .22f, Dmg = 10, KbX = 4.5f,
                Knock = true, Kind = Kind.Mid, Cool = 2.5f, Ev = new (float, Action<Fighter>)[] { (0, f => f.VX = f.Face * 8 * f.H), (.35f, f => f.VX = 0) } });
            // hacha Leviatan: aparece de la nada en la mano entre escarcha, la lanza girando y vuelve sola
            Add(new Move { Id = "axeSummon", Keys = K(Poses.CastUp, .32, Poses.CastUp, .1, Poses.IceThrow, .12, Poses.Stance, .22), Cool = 5,
                Tick = f => { if (f.T < .32f) f.A.Mist(f.J[6], 1); if (f.T > .2f && f.T < .34f) f.A.FrostSpark(f.J[6]); },
                Ev = new (float, Action<Fighter>)[] { (.05f, f => f.A.Sound("ice", .6f)), (.3f, f => { f.Blade = 6; f.A.FrostBurstAt(f.J[6]); }), (.48f, f => { f.AxeOut = true; f.Blade = 0; f.A.ThrowAxe(f); }) } });
            // tormenta de espadas: las Espadas del Caos giran en circulo de fuego alrededor del cuerpo mientras avanza
            Add(new Move { Id = "bladeStorm", Keys = K(Poses.Spin1, .1, Poses.Spin2, .1, Poses.Spin1, .1, Poses.Spin2, .1, Poses.Spin1, .1, Poses.Spin2, .1, Poses.Spin1, .1, Poses.Spin2, .1, Poses.Stance, .15),
                A0 = .1f, A1 = .8f, Limb = 1, Reach = .85f, Dmg = 12, KbX = 4, Knock = true, Kind = Kind.Mid, Cool = 4,
                Ev = new (float, Action<Fighter>)[] { (0, f => f.A.Sound("fire")), (.82f, f => { f.Storm = -1; f.VX = 0; }) },
                Tick = f =>
                {
                    if (f.T >= .82f) return;
                    f.Storm = f.T * 1000; f.VX = f.Face * 1.6f * f.H;
                    if (f.A.Rnd() < .5f) foreach (var p in f.StormBlades()) f.A.Trail(f, p);
                } });
            // el hacha Leviatan aparece entre escarcha, la alza y lo parte en dos de un tajo
            Add(new Move { Id = "fatalKratos", Keys = K(Poses.CastUp, .3, Poses.SwordUp, .3, Poses.Slash, .08, Poses.Slash, .9, Poses.WinFire, .5), Hold = true,
                Tick = f => { if (f.T < .3f) f.A.Mist(f.J[6], 1); },
                Ev = new (float, Action<Fighter>)[] { (.02f, f => f.A.Sound("kratos_fatal")), (.05f, f => f.A.Sound("ice", .6f)), (.28f, f => { f.Blade = 6; f.A.FrostBurstAt(f.J[6]); }),
                    (.68f, f => { f.A.SplitBody(f.Opp, f, false); f.A.FrostBurstAt(f.J[6]); }), (2f, f => f.Win()) } });
            // las Espadas del Caos se clavan en el rival y, de un tiron, lo arrancan por la cintura entre llamas
            Add(new Move { Id = "fatalChaos", Keys = K(Poses.SpearWind, .15, Poses.SpearThrow, .12, Poses.SpearThrow, .35, Poses.RipLift, .3, Poses.RipLift, .8, Poses.WinFire, .4), Hold = true,
                Tick = f => { f.whip = f.T > .12f && f.T < .62f ? Math.Min(1, (f.T - .12f) / .12f) : 0; if (f.whip > .3f) f.A.Trail(f, f.J[6]); },
                Ev = new (float, Action<Fighter>)[] { (.02f, f => f.A.Sound("kratos_fatal")), (.14f, f => f.A.Sound("chain")), (.62f, f => { f.whip = 0; f.A.Burst(f.Opp, false); f.A.SplitBody(f.Opp, f, true); }), (2.1f, f => f.Win()) } });
        }

        /// <summary>Punta del arma (o del puño o pie) con la que golpea: de ahi sale la estela.</summary>
        public PointF WeaponTip(bool front)
        {
            PointF el = front ? J[5] : J[8], hd = front ? J[6] : J[9];
            float len = Blade == 1 ? .66f : Blade == 3 ? .33f : Blade == 4 ? .42f : Blade == 5 ? .3f + whip * .85f : Blade == 6 ? .5f : Blade == 7 ? .42f : 0;
            if (len == 0) return hd;
            float dx = hd.X - el.X, dy = hd.Y - el.Y, l = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-3f;
            return new PointF(hd.X + dx / l * len * H, hd.Y + dy / l * len * H);
        }

        /// <summary>Guarda la estela del golpe en curso (tras Solve); fuera de la ventana activa se va acortando.</summary>
        public void RecordTrail()
        {
            bool active = State == St.Act && Mv != null && Mv.Limb >= 0 && T >= Mv.A0 - .05f && T <= Math.Min(Mv.A1, Mv.A0 + .6f) + .02f && !Hidden;
            if (active)
            {
                var tip = Mv.Limb == 6 || Mv.Limb == 9 ? WeaponTip(Mv.Limb == 6) : J[Mv.Limb];
                var p = new PointF(tip.X - A.L, A.Floor - tip.Y);
                if (trailN == trail.Length) { Array.Copy(trail, 1, trail, 0, trail.Length - 1); trailN--; }
                trail[trailN++] = p;
            }
            else if (trailN > 0) { Array.Copy(trail, 1, trail, 0, trailN - 1); trailN--; }
        }

        /// <summary>
        /// Degradado lineal seguro. GDI+ lanza "memoria insuficiente" si los dos puntos casi coinciden en X o en Y (p. ej. 1061,584 y
        /// 1061,581: un miembro casi vertical u horizontal): ajusta esas coordenadas a exactamente iguales, que si acepta.
        /// </summary>
        internal static LinearGradientBrush Grad(PointF a, PointF b, Color c1, Color c2, WrapMode? wrap = null)
        {
            if (Math.Abs(a.X - b.X) < .5f) b.X = a.X;
            if (Math.Abs(a.Y - b.Y) < .5f) b.Y = a.Y;
            if (a.X == b.X && a.Y == b.Y) b.Y += 1;
            var g = new LinearGradientBrush(a, b, c1, c2);
            if (wrap.HasValue) g.WrapMode = wrap.Value;
            return g;
        }

        internal static LinearGradientBrush Grad(RectangleF r, Color c1, Color c2, float angle, WrapMode? wrap = null)
        {
            r.Width = Math.Max(1, r.Width); r.Height = Math.Max(1, r.Height);
            var g = new LinearGradientBrush(r, c1, c2, angle);
            if (wrap.HasValue) g.WrapMode = wrap.Value;
            return g;
        }

        static bool segLogged;
        // volumen de cada miembro: luz en el borde, sombra hacia un cuarto y vuelta a media luz (antes SetSigmaBellShape, 256 puntos por pincel)
        static readonly Blend SegBlend = new Blend(3) { Factors = new[] { 0f, 1f, .35f }, Positions = new[] { 0f, .25f, 1f } };
        static readonly Pen outlinePen = new Pen(Color.Black), rimPen = new Pen(Color.White);
        static readonly Pen trailPen =new Pen(Color.White) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        /// <summary>Estela: trazo que se afina y se apaga hacia atras, del color del luchador con nucleo claro.</summary>
        public void DrawTrail(Graphics g)
        {
            if (trailN < 2) return;
            var rim = Look.Rim; float wmax = (Blade > 0 ? .07f : .05f) * H;
            for (int i = 1; i < trailN; i++)
            {
                float k = i / (float)(trailN - 1);
                var a = A.Screen(trail[i - 1].X, trail[i - 1].Y); var b = A.Screen(trail[i].X, trail[i].Y);
                trailPen.Color = Color.FromArgb((int)(150 * k), rim); trailPen.Width = Math.Max(1, wmax * k); g.DrawLine(trailPen, a, b);
                trailPen.Color = Color.FromArgb((int)(200 * k * k), Theme.Mix(rim, Color.White, .7f)); trailPen.Width = Math.Max(1, wmax * k * .35f); g.DrawLine(trailPen, a, b);
            }
        }

        /// <summary>Posicion en pantalla de las dos espadas que orbitan en la tormenta.</summary>
        public PointF[] StormBlades()
        {
            float a = Storm * Deg, r = .8f * H; var c = J[1];
            return new[] { new PointF(c.X + (float)Math.Cos(a) * r, c.Y + (float)Math.Sin(a) * r * .45f), new PointF(c.X - (float)Math.Cos(a) * r, c.Y - (float)Math.Sin(a) * r * .45f) };
        }

        // ensartado en las cuchillas: se levanta al rival y sangra
        void Impale() { impaled = true; Opp.State = St.Thrown; Opp.T = 0; Opp.Rot = 0; Opp.Play(K(Poses.HitMid, .1f)); A.Stab(this); }

        void CarryImpaled()
        {
            if (!impaled) return;
            float u = Math.Max(0, Math.Min(1, (T - .3f) / .6f));
            Opp.X = X + Face * .5f * H; Opp.Y = u * .55f * H; Opp.Face = -Face;
            if (A.Rnd() < .3f) A.Stab(this, true);
        }

        static readonly string[] Shared = { "jab", "highkick", "sweep", "uppercut", "throw", "jumpkick", "wallrun" };

        // ---------- ciclo ----------
        public void Reset(float x, int face)
        {
            Blade = Who == Who.Mileena ? 3 : Who == Who.Baraka ? 4 : Who == Who.Kratos ? 5 : 0; impaled = false; whip = 0; Storm = -1;
            X = x; Y = 0; VX = VY = Rot = 0; Face = face; Hp = ShownHp = 100; Dead = Fatal = Hidden = grabbed = usedSpear = Headless = false; Char = 0; fatalId = null; SplitT = 0;
            State = St.Idle; T = 0; Mv = null; Queued = null; coolUntil.Clear(); hist.Clear(); sashInit = false;
            Array.Copy(idleK[0].P, Pose, 12); Play(idleK, true); AxeOut = false; Flash = 0; trailN = 0; twin = false; Blade = DefaultBlade;
        }

        public void Update(float h)
        {
            T += h; if (SplitT > 0) SplitT += h;
            switch (State)
            {
                case St.Idle:
                    if (Grounded) Face = Opp.X >= X ? 1 : -1;
                    if (anim != idleK) Play(idleK, true);
                    Think(h);
                    break;
                case St.Act: RunMove(h); break;
                case St.Cling: if (T >= Timer) LeaveWall(); break;
                case St.Hit: case St.Block: if (T >= Timer) ToIdle(); break;
                case St.AirHit: Rot = -Face * Math.Min(330, T * 560); break;
                case St.Down: if ((!Dead || A.Phase == Phase.Finish) && T >= Timer) { State = St.GetUp; T = 0; Play(GetUpK); } break;
                case St.GetUp: if (T >= .4f) { if (Dead) Dizzy(0); else ToIdle(); } break;
                case St.Frozen: if (Timer > 0 && T >= Timer) Thaw(); break;
                case St.Dizzy: if (Timer > 0 && T >= Timer) ToIdle(); break;
            }
            Physics(h);
            if (State != St.Frozen) Animate(h);
            bool spinning = State == St.AirHit || State == St.Thrown || (State == St.Act && Mv != null && Mv.Spin);
            if (!spinning && Rot != 0)
            {
                Rot = ((Rot + 180) % 360 + 360) % 360 - 180;
                Rot *= (float)Math.Exp(-18 * h); if (Math.Abs(Rot) < .5f) Rot = 0;
            }
            if (Flash > 0) Flash = Math.Max(0, Flash - h * 8);
            ShownHp += (Hp - ShownHp) * Math.Min(1, h * 2.5f);
        }

        void Physics(float h)
        {
            if (State == St.Thrown || State == St.Pulled || State == St.Gone || State == St.Cling) return;
            if (!Grounded || VY > 0) VY -= A.G * h;
            X += VX * h; Y += VY * h;
            if (Y <= 0) { Y = 0; float vy = VY; VY = 0; if (vy < 0) Landed(vy); }
            if (Grounded && State != St.Act) VX *= (float)Math.Exp(-12 * h);
            float hw = .2f * H;
            if (X < hw || X > A.W - hw)
            {
                int side = X < hw ? -1 : 1;
                X = Math.Max(hw, Math.Min(A.W - hw, X));
                bool airAct = State == St.Act && Mv != null && (Mv.Id == "wallrun" || (Mv.Id == "divekick" && bounces > 0));
                if (A.Walls && State == St.AirHit && Math.Abs(VX) > 3 * H) { A.WallSplat(this, side); VX = -VX * .25f; }
                else if (A.Walls && airAct && Y > .45f * H) GrabWall(side);
                else if (VX * side > 0) VX = 0;
            }
            float top = A.Floor - A.Top - 1.05f * H;
            if (Y > top) { Y = Math.Max(0, top); if (VY > 0) VY = 0; }
        }

        void Landed(float vy)
        {
            if (State == St.AirHit)
            {
                State = St.Down; T = 0; Timer = .55f; Play(DownK);
                A.Dust(X, 8); A.Kick(2.5f);
                return;
            }
            if (State == St.Act && Mv.Air) { A.Dust(X, 5); Do("land"); return; }
            if (vy < -4 * H) A.Dust(X, 3);
        }

        void RunMove(float h)
        {
            var m = Mv;
            while (Mv == m && ev < m.Ev.Length && T >= m.Ev[ev].t) m.Ev[ev++].f(this);
            if (Mv != m) return;
            m.Tick?.Invoke(this);
            if (grabbed) CarryVictim();
            if (!m.Hold && !m.Air && T >= m.Dur) EndMove();
        }

        public void EndMove()
        {
            var m = Mv;
            ToIdle();
            m?.End?.Invoke(this);
        }

        void ToIdle() { State = St.Idle; T = 0; Crouch = false; Hidden = false; Play(idleK, true); }

        public void Do(string id)
        {
            var m = Moves[id];
            Interrupt();
            Mv = m; ev = 0; MvHit = false; State = St.Act; T = 0; Crouch = id == "sweep" || id == "slide"; trailN = 0;
            if (m.Cool > 0) coolUntil[id] = A.Clock + m.Cool;
            if (m.Cool > 0 && hist.Count > 0 && hist[0] == id) A.Meme("otravez|dejatedejoder", .6f, .3f);
            if (id != "land") { hist.Insert(0, id); if (hist.Count > 4) hist.RemoveAt(4); }
            if (m.Limb >= 0) { A.Sound("swing", .45f); if (A.Rnd() < .3f) A.Sound(Key + "_attack", .7f); }
            if (id.StartsWith("fatal")) A.Sound("prefatal");
            Play(m.Keys);
        }

        // ---------- IA ----------
        void Think(float h)
        {
            bool fight = A.Phase == Phase.Fight, finish = A.Phase == Phase.Finish && Fatal;
            if (!fight && !finish) return;
            if ((think -= h) > 0) return;
            think = .04f + A.Rnd() * .2f;
            if (finish) { FinishThink(); return; }
            if (Queued != null) { var q = Queued; Queued = null; Do(q); return; }
            float d = Math.Abs(Opp.X - X) / H;
            // defensa: bloquea parte de los golpes que vienen; salta o se aparta de los proyectiles y de la columna de fuego
            if (Opp.State == St.Act && Opp.Mv.Limb >= 0 && Opp.T < Opp.Mv.A1 && d < 1.4f && A.Rnd() < .36f)
            { Guard(Opp.Mv.Kind == Kind.Low ? A.Rnd() < .75f : A.Rnd() < .15f, Math.Min(.55f, Opp.Mv.A1 - Opp.T + .12f)); return; }
            if (A.Threat(this) && A.Rnd() < .7f) { Do(A.Rnd() < .55f ? "hop" : "backdash"); return; }
            if (Opp.State == St.Frozen || Opp.State == St.Dizzy) { Do(d < .8f ? Pick("uppercut", "highkick", "jab") : "dash"); return; }
            if (Opp.State == St.Down || Opp.State == St.GetUp || Opp.State == St.AirHit)
            { if (A.Rnd() < .5f) Do(d > 1.3f ? "walk" : "backdash"); else think = .25f; return; }

            var c = new List<(string id, float w)>();
            bool wallNear = A.Walls && (X < 1.3f * H || X > A.W - 1.3f * H) && A.Floor - A.Top > 3 * H;
            if (d < .75f)
            {
                c.Add(("jab", 3)); c.Add(("highkick", 2.2f)); c.Add(("sweep", 2)); c.Add(("uppercut", 1.6f)); c.Add(("throw", 1.5f)); c.Add(("backdash", .7f));
                if (Who == Who.JohnnyCage) { c.Add(("nutPunch", 1.7f)); c.Add(("shadowUpper", 1.4f)); c.Add(("flipKick", 1.6f)); }
                if (Who == Who.Baraka) { c.Add(("chopChop", 2.4f)); c.Add(("bladeStab", 1.6f)); c.Add(("bladeLift", 1.4f)); }
                if (Who == Who.Scorpion) c.Add(("twinSwords", 2));
                if (Who == Who.SubZero) c.Add(("iceHammer", 1.6f));
                if (Who == Who.Kratos) { c.Add(("chaosWhip", 2.2f)); c.Add(("spartan", 1.6f)); c.Add(("bladeStorm", 2)); }
            }
            else if (d < 1.7f)
            {
                c.Add(("walk", 2.4f)); c.Add(("jumpkick", 2)); c.Add(("dash", 1.4f));
                if (Who == Who.Scorpion) { c.Add(("teleport", 1.3f)); c.Add(("twinSwords", 1.2f)); }
                else if (Who == Who.SubZero) { c.Add(("slide", 2.4f)); c.Add(("iceHammer", 1.6f)); }
                else if (Who == Who.Mileena) { c.Add(("roll", 2.4f)); c.Add(("teleKick", 1.4f)); }
                else if (Who == Who.JohnnyCage) { c.Add(("shadowKick", 2.6f)); c.Add(("forceBall", 1.2f)); }
                else if (Who == Who.Kratos) { c.Add(("hermes", 2.4f)); c.Add(("chaosWhip", 2.2f)); c.Add(("bladeStorm", 1.8f)); c.Add(("axeSummon", 1.4f)); }
                else { c.Add(("bladeSpin", 2.4f)); c.Add(("bladeStab", 1.4f)); }
            }
            else
            {
                c.Add(("walk", 1.5f)); c.Add(("jumpkick", 1)); c.Add(("dash", 1));
                if (Who == Who.Scorpion) { c.Add(("spear", 2.6f)); c.Add(("teleport", 1.8f)); c.Add(("hellfire", 2)); }
                else if (Who == Who.SubZero) { c.Add(("iceball", 2.6f)); if (!A.HasClone) c.Add(("clone", 1.3f)); if (!A.HasPuddle) c.Add(("puddle", 1)); }
                else if (Who == Who.Mileena) { c.Add(("sai", 2.8f)); c.Add(("teleKick", 2)); c.Add(("roll", 1.6f)); c.Add(("airSai", 1.8f)); }
                else if (Who == Who.JohnnyCage) { c.Add(("forceBall", 2.6f)); c.Add(("shadowKick", 2)); }
                else if (Who == Who.Kratos) { c.Add(("axeSummon", 2.6f)); c.Add(("apollo", 1.8f)); c.Add(("hermes", 1.6f)); }
                else { c.Add(("bladeSpark", 2.6f)); c.Add(("bladeSpin", 1.6f)); }
            }
            if (wallNear) c.Add(("wallrun", 1.8f));
            if (Who == Who.SubZero && A.HasClone && d < 2) c.Add(("backdash", 1.5f));   // espera a que el rival se coma el clon
            Do(Choose(c));
        }

        string Choose(List<(string id, float w)> c)
        {
            float sum = 0;
            for (int i = 0; i < c.Count; i++)
            {
                var (id, w) = c[i];
                if (coolUntil.TryGetValue(id, out var until) && A.Clock < until) w = 0;
                int at = hist.IndexOf(id);
                if (at == 0) w *= .12f; else if (at > 0) w *= .5f;   // no repetir
                c[i] = (id, w); sum += w;
            }
            float r = A.Rnd() * sum;
            foreach (var (id, w) in c) { if (w <= 0) continue; if ((r -= w) <= 0) return id; }
            return "walk";
        }

        string Pick(params string[] ids) => Choose(new List<(string, float)>(Array.ConvertAll(ids, s => (s, 1f))));

        void Guard(bool low, float dur) { State = St.Block; Crouch = low; T = 0; Timer = dur; Play(low ? CBlockK : BlockK); }

        static readonly Dictionary<Who, string[]> Fatals = new Dictionary<Who, string[]>
        {
            [Who.Scorpion] = new[] { "fatalFire", "fatalSword", "fatalSplit" },
            [Who.SubZero] = new[] { "fatalIce", "fatalRip", "fatalHammer" },
            [Who.Mileena] = new[] { "fatalSai", "fatalTear" },
            [Who.JohnnyCage] = new[] { "fatalCage", "fatalNut" },
            [Who.Baraka] = new[] { "fatalBaraka", "fatalChop" },
            [Who.Kratos] = new[] { "fatalKratos", "fatalChaos" },
        };
        static readonly Dictionary<Who, string> lastFatal = new Dictionary<Who, string>();

        void FinishThink()
        {
            float d = Math.Abs(Opp.X - X) / H;
            if (fatalId == null)
            {
                // uno de los suyos al azar, nunca el mismo que hizo la vez anterior
                var all = Fatals[Who]; lastFatal.TryGetValue(Who, out var last);
                do fatalId = all[Math.Min(all.Length - 1, (int)(A.Rnd() * all.Length))]; while (fatalId == last);
                lastFatal[Who] = fatalId;
            }
            switch (fatalId)
            {
                case "fatalTear": break;   // se teletransporta: cualquier distancia vale
                case "fatalIce":
                case "fatalHammer":
                    if (Opp.State != St.Frozen) { if (d > 2.4f) Do("walk"); else Do("freezeBlast"); return; }
                    if (d > .8f) { Do("walk"); return; }
                    break;
                case "fatalFire":
                    if (d > 1.4f && !usedSpear) { usedSpear = true; Do("spear"); return; }
                    if (d > .95f) { Do("walk"); return; }
                    break;
                case "fatalChaos":
                    if (d > 1.1f) { Do(d > 2 ? "dash" : "walk"); return; }
                    break;
                default:
                    if (Fire && d > 1.4f && !usedSpear) { usedSpear = true; Do("spear"); return; }
                    if (d > .8f) { Do(d > 2 && !Fire && Who != Who.SubZero ? "dash" : "walk"); return; }
                    break;
            }
            Do(fatalId);
        }

        // ---------- golpes ----------
        /// <summary>Comprueba el golpe en curso contra el rival (y contra el clon de hielo, que congela a quien lo golpea).</summary>
        public void Strike()
        {
            if (State != St.Act || Mv.Limb < 0 || MvHit || T < Mv.A0 || T > Mv.A1 || Hidden) return;
            var tip = J[Mv.Limb]; float r = Mv.Reach * H;
            if (A.CloneHit(this, tip, r)) { MvHit = true; Freeze(1.3f); return; }
            if (!Opp.Hittable) return;
            var b = RectangleF.Inflate(Opp.Box, r, r);
            if (!b.Contains(tip)) return;
            MvHit = true;
            A.Hit(this, Opp, tip, Mv);
        }

        public bool Hittable => !Hidden && State != St.Gone && State != St.Thrown && State != St.Down && State != St.Pulled && !(Dead && A.Phase != Phase.Finish);

        public void TakeHit(Fighter att, Move m)
        {
            Interrupt(); Flash = 1;
            Hp = Math.Max(0, Hp - m.Dmg * 1.5f * att.Form);   // rondas de unos 20 s
            if (A.Destined == this && A.Phase == Phase.Fight) Hp = Math.Max(1, Hp);
            int dir = Math.Abs(att.X - X) < 1 ? att.Face : (X > att.X ? 1 : -1);
            Face = -dir; Queued = null; Hidden = false; grabbed = false;
            VX = dir * m.KbX * H;
            if (Hp <= 0 && !Dead) { Dead = true; A.KO(this); A.Sound(Key + "_ko"); }
            else if (A.Rnd() < .55f) A.Sound(Key + "_hit", .8f);
            if (m.Id == "uppercut" && A.Rnd() < .12f) A.Sound("toasty");
            if (m.Id == "nutPunch") { A.Sound("nutpunch"); if (!A.Meme("cocos", .7f, 1.5f)) A.Meme("fart", .6f, .5f); }
            if (m.Launch || m.Knock || Dead || !Grounded)
            {
                State = St.AirHit; T = 0; Play(AirHitK);
                VY = Math.Max(VY, (m.Launch ? m.KbY : m.Knock ? 3.5f : 4.5f) * H);
                if (Dead) { VY = Math.Max(VY, 7 * H); VX = dir * Math.Max(Math.Abs(VX), 2.5f * H); }
            }
            else { State = St.Hit; T = 0; Timer = m.Stun; Play(m.Kind == Kind.High ? HitHighK : HitMidK); }
        }

        /// <summary>Daño y lanzamiento sin golpe de por medio (proyeccion, resbalon, columna de fuego).</summary>
        public void Launch(int dmg, float vx, float vy)
        {
            Interrupt(); if (dmg > 0) Flash = 1;
            Hp = Math.Max(0, Hp - dmg * Opp.Form);
            if (A.Destined == this && A.Phase == Phase.Fight) Hp = Math.Max(1, Hp); grabbed = false; Hidden = false; Queued = null;
            if (Hp <= 0 && !Dead) { Dead = true; A.KO(this); }
            State = St.AirHit; T = 0; VX = vx; VY = vy; Play(AirHitK);
        }

        public void Freeze(float dur)
        {
            if (State == St.Gone) return;
            Interrupt();
            State = St.Frozen; T = 0; Timer = dur; VX = 0; Queued = null; grabbed = false;
            A.FrostBurst(this);
        }

        void Thaw() { A.Shatter(this, false); State = St.Hit; T = 0; Timer = .25f; Play(HitMidK); }

        public void Dizzy(float dur) { Interrupt(); State = St.Dizzy; T = 0; Timer = dur; VX = 0; Play(DizzyK, true); }

        public void Pulled() { Interrupt(); State = St.Pulled; T = 0; Queued = null; Play(K(Poses.HitMid, .06f)); }

        public void Win() { Interrupt(); State = St.Win; T = 0; VX = 0; Play(K(Who == Who.SubZero || Who == Who.JohnnyCage ? Poses.WinIce : Poses.WinFire, .3f)); }

        public void Gone() { Interrupt(); State = St.Gone; Hidden = true; trailN = 0; }

        /// <summary>Cuerpo sin cabeza: aguanta un instante, cae de rodillas y de bruces.</summary>
        public void Corpse()
        {
            Interrupt(); State = St.Corpse; T = 0; VX = 0; Queued = null;
            Play(K(Poses.Dizzy, .7f, Poses.Kneel, .35f, Poses.DownFwd, .35f));
        }

        // proyeccion: lo agarra, lo pasa por encima de la cabeza y lo lanza contra la pared de detras
        void TryGrab()
        {
            if (Math.Abs(Opp.X - X) < .8f * H && Opp.Grounded && Opp.Hittable && Opp.State != St.Frozen && Opp.State != St.Block)
            { grabbed = true; Opp.State = St.Thrown; Opp.T = 0; Opp.Play(AirHitK); }
            else A.Meme("mecago|mecagoleche", .5f, .2f);
        }

        void CarryVictim()
        {
            float u = Math.Max(0, Math.Min(1, (T - .12f) / .32f)), ang = (float)(u * Math.PI);
            Opp.X = X + Face * (float)Math.Cos(ang) * .55f * H;
            Opp.Y = (float)Math.Sin(ang) * .9f * H + u * .2f * H;
            Opp.Rot = -Face * u * 200; Opp.Face = -Face;
        }

        void Release()
        {
            if (!grabbed) return;
            grabbed = false;
            Opp.X = X - Face * .5f * H; Opp.Y = .4f * H;
            Opp.Launch(10, -Face * 7.5f * H, 3.5f * H);
            A.Kick(3); A.Sound("heavyhit"); A.Crack(); A.Meme("ole", .5f, .4f);
        }

        // carrera por la pared: salta a la ventana mas cercana, se agarra, rebota y cae en patada sobre el rival
        void ToWall()
        {
            int side = X < A.W / 2 ? -1 : 1;
            Face = side; bounces = A.Rnd() < .45f && A.Floor - A.Top > 4.5f * H ? 1 : 0;
            VX = side * 7.5f * H; VY = 9 * H;
        }

        void GrabWall(int side)
        {
            State = St.Cling; T = 0; Timer = .2f + A.Rnd() * .15f; VX = VY = 0; Face = -side; Rot = 0;
            Play(ClingK); A.WallDust(this, side);
        }

        void LeaveWall()
        {
            Face = X < A.W / 2 ? 1 : -1;
            Do("divekick");
            if (bounces > 0) { bounces--; VX = Face * 7.5f * H; VY = 6 * H; }   // a la otra ventana
            else
            {
                float dx = Opp.X - X, t = .35f;
                VX = Math.Max(-9 * H, Math.Min(9 * H, dx / t)); VY = Math.Min(4 * H, (Opp.Y + .5f * H - Y) / t + .5f * A.G * t);
                if (Math.Abs(VX) < 2 * H) VX = Face * 2 * H;
                bounces = 0;
            }
        }

        void Reappear(bool above = false)
        {
            var o = Opp; int side = -o.Face;   // a la espalda del rival
            float x = o.X + side * .7f * H, hw = .3f * H;
            if (x < hw || x > A.W - hw) x = o.X - side * .7f * H;
            X = Math.Max(hw, Math.Min(A.W - hw, x)); Y = Math.Min(1.5f * H, Math.Max(0, A.Floor - A.Top - 1.3f * H));
            Face = o.X >= X ? 1 : -1; Hidden = false;
            VX = Face * 3 * H; VY = -1 * H;
            if (above) { X = Math.Max(hw, Math.Min(A.W - hw, o.X - Face * .35f * H)); Y = Math.Min(1.8f * H, Math.Max(0, A.Floor - A.Top - 1.3f * H)); VX = Face * 2 * H; VY = -3 * H; A.PinkBurst(this); }
            else A.Burst(this, false);
        }

        /// <summary>Reaparece pegado a la espalda del rival, mirandole.</summary>
        void Behind()
        {
            var o = Opp; float hw = .3f * H, x = o.X - o.Face * .45f * H;
            if (x < hw || x > A.W - hw) x = o.X + o.Face * .45f * H;
            X = Math.Max(hw, Math.Min(A.W - hw, x)); Y = 0; VX = VY = 0;
            Face = o.X >= X ? 1 : -1; Hidden = false; A.PinkBurst(this);
        }

        /// <summary>Partido en dos (remate): se queda quieto y las mitades se separan al dibujarlo.</summary>
        public void Split(bool waist, int dir)
        {
            Interrupt(); State = St.Corpse; T = 0; VX = 0; Rot = 0; Queued = null;
            Play(K(Poses.Dizzy, 9f)); SplitT = .001f; SplitH = waist; SplitDir = dir;
        }

        void BreathFire()
        {
            if (T < .25f || T > 1.7f) return;
            A.FireStream(this);
            Opp.Char = Math.Min(1, Math.Max(Opp.Char, (T - .35f) / 1.1f));
        }

        // ---------- animacion ----------
        public void Play(Key[] keys, bool lp = false) { Array.Copy(Pose, from, 12); anim = keys; animT = 0; loop = lp; }

        void Animate(float h)
        {
            if (anim == null) return;
            animT += h;
            float total = 0;
            foreach (var k in anim) total += k.D;
            if (loop && animT > total) { animT -= total; Array.Copy(anim[anim.Length - 1].P, from, 12); }
            float t = animT; float[] a = from;
            for (int i = 0; i < anim.Length; i++)
            {
                var k = anim[i];
                if (t <= k.D || i == anim.Length - 1)
                {
                    float u = Math.Max(0, Math.Min(1, t / k.D)); u = u * u * (3 - 2 * u);
                    for (int j = 0; j < 12; j++) Pose[j] = a[j] + (k.P[j] - a[j]) * u;
                    return;
                }
                t -= k.D; a = k.P;
            }
        }

        // ---------- esqueleto ----------
        const float Deg = (float)(Math.PI / 180);

        /// <summary>Cinematica directa de la pose a pantalla; en el suelo, el punto mas bajo toca el suelo.</summary>
        public void Solve()
        {
            float h = H, f = Face; var p = Pose;
            PointF Dn(PointF o, float a, float len) => new PointF(o.X + (float)Math.Sin(a * Deg) * len * f, o.Y + (float)Math.Cos(a * Deg) * len);
            PointF Upw(PointF o, float a, float len) => new PointF(o.X + (float)Math.Sin(a * Deg) * len * f, o.Y - (float)Math.Cos(a * Deg) * len);
            var pel = new PointF(p[0] * h * f, -p[1] * h);
            float tor = p[2];
            var ch = Upw(pel, tor, .3f * h);
            var nk = Upw(ch, tor + p[3], .04f * h);
            var hd = Upw(nk, tor + p[3], .085f * h);
            var sf = Upw(pel, tor, .27f * h);
            var sb = new PointF(sf.X - .02f * h * f, sf.Y);
            var ef = Dn(sf, tor + p[4], .19f * h); var hf = Dn(ef, tor + p[4] + p[5], .17f * h);
            var eb = Dn(sb, tor + p[6], .19f * h); var hb = Dn(eb, tor + p[6] + p[7], .17f * h);
            var pf = new PointF(pel.X + .012f * h * f, pel.Y); var pb = new PointF(pel.X - .012f * h * f, pel.Y);
            var kf = Dn(pf, p[8], .245f * h); var af = Dn(kf, p[8] - p[9], .235f * h); var tf = Dn(af, p[8] - p[9] + 90, .075f * h);
            var kb = Dn(pb, p[10], .245f * h); var ab = Dn(kb, p[10] - p[11], .235f * h); var tb = Dn(ab, p[10] - p[11] + 90, .075f * h);
            J[0] = pel; J[1] = ch; J[2] = nk; J[3] = hd; J[4] = sf; J[5] = ef; J[6] = hf; J[7] = sb; J[8] = eb; J[9] = hb;
            J[10] = pf; J[11] = kf; J[12] = af; J[13] = tf; J[14] = pb; J[15] = kb; J[16] = ab; J[17] = tb;
            if (Rot != 0)
            {
                float c = (float)Math.Cos(Rot * Deg), s = (float)Math.Sin(Rot * Deg);
                for (int i = 1; i < 18; i++) { float dx = J[i].X - pel.X, dy = J[i].Y - pel.Y; J[i] = new PointF(pel.X + dx * c - dy * s, pel.Y + dx * s + dy * c); }
            }
            float low = float.MinValue;
            for (int i = 0; i < 18; i++) low = Math.Max(low, J[i].Y);
            bool align = Grounded && State != St.Thrown && State != St.Pulled && State != St.AirHit;
            float lift = align ? -(low + .03f * h) : 0;
            var bs = A.Screen(X, Y);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 18; i++)
            {
                var q = new PointF(bs.X + J[i].X, bs.Y + J[i].Y + lift); J[i] = q;
                x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
            }
            Box = RectangleF.FromLTRB(x0 - .05f * h, y0 - .1f * h, x1 + .05f * h, y1 + .02f * h);
        }

        /// <summary>Telas: faldon delantero y trasero con muelle, cintas de la espalda que siguen al cuerpo con retraso.</summary>
        public void Cloth(float h)
        {
            float tgt = Math.Max(-8, Pose[8] * .75f) - VX / H * 6 * Face;
            clothV += ((tgt - clothA) * 120 - clothV * 12) * h; clothA += clothV * h;
            float tgtB = Math.Min(8, Pose[10] * .75f) - VX / H * 9 * Face;
            backV += ((tgtB - backA) * 90 - backV * 10) * h; backA += backV * h;
            var anchor = BeltBack();
            if (!sashInit) { for (int i = 0; i < sash.Length; i++) sash[i] = anchor; sashInit = true; }
            sash[0] = anchor;
            float seg = .065f * H;
            for (int i = 1; i < sash.Length; i++)
            {
                var tg = new PointF(sash[i - 1].X - Face * seg * .55f, sash[i - 1].Y + seg * .8f);
                float k = 1 - (float)Math.Exp(-14 * h);
                var q = new PointF(sash[i].X + (tg.X - sash[i].X) * k, sash[i].Y + (tg.Y - sash[i].Y) * k + (float)Math.Sin(A.Clock * 7 + i) * .004f * H);
                float dx = q.X - sash[i - 1].X, dy = q.Y - sash[i - 1].Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l > seg) q = new PointF(sash[i - 1].X + dx / l * seg, sash[i - 1].Y + dy / l * seg);
                sash[i] = q;
            }
        }

        PointF BeltBack()
        {
            float tor = Pose[2] * Deg; var pel = J[0];
            return new PointF(pel.X - (float)Math.Cos(tor) * Face * .07f * H, pel.Y - (float)Math.Sin(tor) * .07f * H * 1);
        }

        // ---------- dibujo ----------
        static readonly Color Outline = Color.FromArgb(220, 6, 6, 8);

        public void Draw(Graphics g)
        {
            if (Hidden || Fade <= 0) return;
            if (SplitT > 0) DrawHalves(g); else Paint(g, J, Look, Face, Pose, Fade, Char, State == St.Frozen);
        }

        static readonly Pen cutPen = new Pen(Color.FromArgb(235, 150, 8, 14));

        /// <summary>
        /// Las dos mitades del cuerpo partido, recortando el mismo dibujo: de arriba abajo, cada una cae hacia su lado girando sobre su
        /// pie; por la cintura, el tronco sale volando en arco y cae tumbado mientras las piernas se doblan.
        /// </summary>
        void DrawHalves(Graphics g)
        {
            float h = H, u = Math.Min(1, SplitT / 1.1f), e = u * u * (3 - 2 * u);
            var b = Box; var pel = J[0];
            cutPen.Width = Math.Max(2, .035f * h);
            if (!SplitH)
            {
                float cx = (J[3].X + pel.X) / 2;
                for (int s = -1; s <= 1; s += 2)
                {
                    var st = g.Save();
                    var foot = new PointF(cx + s * .1f * h, b.Bottom);
                    g.TranslateTransform(s * e * .12f * h, 0);
                    g.TranslateTransform(foot.X, foot.Y); g.RotateTransform(s * e * 80); g.TranslateTransform(-foot.X, -foot.Y);
                    g.SetClip(s < 0 ? RectangleF.FromLTRB(b.Left - h, b.Top - h, cx, b.Bottom + h) : RectangleF.FromLTRB(cx, b.Top - h, b.Right + h, b.Bottom + h), CombineMode.Intersect);
                    Paint(g, J, Look, Face, Pose, Fade, Char, false);
                    g.DrawLine(cutPen, cx, J[3].Y - .08f * h, cx, pel.Y + .06f * h);
                    g.Restore(st);
                }
                return;
            }
            float cy = pel.Y - .08f * h, k = Math.Max(0, Math.Min(1, (u - .25f) / .75f)), e2 = k * k * (3 - 2 * k);
            // piernas: se quedan y caen hacia atras sobre los pies
            var sl = g.Save();
            var feet = new PointF(pel.X, b.Bottom);
            g.TranslateTransform(feet.X, feet.Y); g.RotateTransform(-SplitDir * e2 * 75); g.TranslateTransform(-feet.X, -feet.Y);
            g.SetClip(RectangleF.FromLTRB(b.Left - h, cy, b.Right + h, b.Bottom + h), CombineMode.Intersect);
            Paint(g, J, Look, Face, Pose, Fade, Char, false);
            g.DrawLine(cutPen, pel.X - .07f * h, cy, pel.X + .07f * h, cy);
            g.Restore(sl);
            // tronco: arco por encima y cae tumbado al suelo
            var su = g.Save();
            var waist = new PointF(pel.X, cy);
            g.TranslateTransform(SplitDir * e * .9f * h, -(float)Math.Sin(Math.PI * e) * .55f * h + e * (b.Bottom - cy - .1f * h));
            g.TranslateTransform(waist.X, waist.Y); g.RotateTransform(SplitDir * e * 95); g.TranslateTransform(-waist.X, -waist.Y);
            g.SetClip(RectangleF.FromLTRB(b.Left - h, b.Top - h, b.Right + h, cy), CombineMode.Intersect);
            Paint(g, J, Look, Face, Pose, Fade, Char, false);
            g.DrawLine(cutPen, pel.X - .08f * h, cy, pel.X + .08f * h, cy);
            g.Restore(su);
        }

        /// <summary>Dibuja un cuerpo (luchador o estatua de hielo) a partir de sus articulaciones en pantalla.</summary>
        public void Paint(Graphics g, PointF[] j, Look lk, int face, float[] pose, float alpha, float burnt, bool frozen)
        {
            float h = H, flash = j == J ? Flash : 0;
            Color C(Color c, float dk = 0)
            {
                if (burnt > 0) c = Theme.Mix(c, Color.FromArgb(22, 15, 11), Math.Min(1, burnt * 1.1f));
                if (dk > 0) c = Theme.Mix(c, Color.Black, dk);
                if (flash > 0) c = Theme.Mix(c, Color.White, flash * .75f);
                return Color.FromArgb((int)(255 * alpha), c);
            }
            int rimA = (int)(alpha * (150 + 90 * burnt));
            Color rim = burnt > 0 ? Color.FromArgb(255, 120, 20) : lk.Rim;
            float ow = Math.Max(1f, .014f * h);

            void Seg(PointF a, PointF b, float ra, float rb, Color c, bool rimLight = true)
            {
                float dx = b.X - a.X, dy = b.Y - a.Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l < .5f) { b = new PointF(a.X + .5f, a.Y); dx = .5f; dy = 0; l = .5f; }
                float ang = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);
                float nx = -dy / l, ny = dx / l; if (nx + ny > 0) { nx = -nx; ny = -ny; }   // n hacia la luz (arriba a la izquierda)
                using (var path = new GraphicsPath())
                {
                    path.AddArc(a.X - ra, a.Y - ra, 2 * ra, 2 * ra, ang + 90, 180);
                    path.AddArc(b.X - rb, b.Y - rb, 2 * rb, 2 * rb, ang - 90, 180);
                    path.CloseFigure();
                    float r = Math.Max(1, Math.Max(ra, rb));
                    var m = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    var p1 = new PointF(m.X + nx * r, m.Y + ny * r); var p2 = new PointF(m.X - nx * r, m.Y - ny * r);
                    LinearGradientBrush br = null;
                    try { br = Grad(p1, p2, Theme.Mix(c, Color.White, .22f), Theme.Mix(c, Color.Black, .55f), WrapMode.TileFlipXY); br.Blend = SegBlend; }
                    catch (OutOfMemoryException ex)
                    {
                        br?.Dispose(); br = null;
                        if (!segLogged) { segLogged = true; Crash.Log($"pelea: degradado p1={p1} p2={p2} a={a} b={b} ra={ra} rb={rb} c={c}", ex); }
                    }
                    if (br != null) using (br) g.FillPath(br, path);
                    else using (var sb = new SolidBrush(c)) g.FillPath(sb, path);
                    outlinePen.Color = Color.FromArgb((int)(alpha * Outline.A), Outline); outlinePen.Width = ow; g.DrawPath(outlinePen, path);
                }
                if (rimLight)
                {
                    rimPen.Color = Color.FromArgb(rimA, rim); rimPen.Width = Math.Max(1f, .013f * h);
                    g.DrawLine(rimPen, a.X - nx * ra * .8f, a.Y - ny * ra * .8f, b.X - nx * rb * .8f, b.Y - ny * rb * .8f);
                }
            }
            void Ball(PointF c, float r, Color col)
            {
                using (var br = Fighter.Grad(new PointF(c.X - r, c.Y - r), new PointF(c.X + r, c.Y + r), Theme.Mix(col, Color.White, .3f), Theme.Mix(col, Color.Black, .5f)))
                    g.FillEllipse(br, c.X - r, c.Y - r, 2 * r, 2 * r);
                using (var pen = new Pen(Color.FromArgb((int)(alpha * 200), 6, 6, 8), ow)) g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
            }

            float tor = pose[2] * Deg;
            var fw = new PointF((float)Math.Cos(tor) * face, (float)Math.Sin(tor));   // adelante, girado con el torso
            var pel = j[0];

            bool mil = lk.Style == 1;
            // brazo y pierna de detras, mas oscuros
            Arm(j[7], j[8], j[9], .3f);
            Seg(j[14], j[15], .058f * h, .045f * h, C(lk.Suit, .3f));
            Seg(j[15], j[16], .045f * h, .034f * h, C(lk.Suit, .3f));
            Seg(j[16], j[17], .032f * h, .022f * h, C(lk.Trim, .5f), false);
            // cintas de la espalda (Mileena: la melena va con la cabeza)
            if (lk.Style == 0 || lk.Style == 3)
                using (var pen = new Pen(C(lk.Acc, .35f), .03f * h) { StartCap = LineCap.Round, EndCap = LineCap.Triangle, LineJoin = LineJoin.Round })
                    if (sashInit && j == J) g.DrawLines(pen, sash);
            // faldon trasero
            Flap(g, new PointF(pel.X - fw.X * .03f * h, pel.Y - fw.Y * .03f * h), backA, .26f * h, C(lk.Acc, .45f), C(lk.Trim, .4f), face, h);
            // torso
            Seg(pel, j[1], .078f * h, .098f * h, C(lk.SkinTorso ? lk.Skin : lk.Suit));
            if (lk.Style == 4)
            {
                // tatuaje rojo de Kratos: del hombro bajando en diagonal por el pecho, y correa de bronce
                using (var pen = new Pen(C(Color.FromArgb(176, 24, 20)), .03f * h) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, Lerp(j[4], j[1], .3f), new PointF(pel.X + fw.X * .03f * h, pel.Y + fw.Y * .03f * h));
                using (var pen = new Pen(C(lk.Trim), .022f * h)) g.DrawLine(pen, j[7], new PointF(pel.X + fw.X * .07f * h, pel.Y + fw.Y * .07f * h));
            }
            if (lk.SkinTorso)
            {
                // pecho y abdominales
                var front = new PointF(fw.X * .06f * h, fw.Y * .06f * h);
                using (var pen = new Pen(C(Theme.Mix(lk.Skin, Color.Black, .35f)), Math.Max(1f, .01f * h)))
                {
                    var pec = Lerp(pel, j[1], .72f);
                    g.DrawLine(pen, pec.X + front.X * 1.3f, pec.Y + front.Y * 1.3f, pec.X - front.X * .2f, pec.Y - front.Y * .2f);
                    for (int k = 0; k < 3; k++) { var q = Lerp(pel, j[1], .25f + k * .13f); g.DrawLine(pen, q.X + front.X * 1.2f, q.Y + front.Y * 1.2f, q.X + front.X * .55f, q.Y + front.Y * .55f); }
                }
            }
            else if (mil)
            {
                // MK9: cordones oscuros cruzados por delante del traje y cuello alto con ribete
                var top = Lerp(pel, j[1], .92f); var bot = Lerp(pel, j[1], .2f);
                var front = new PointF(fw.X * .07f * h, fw.Y * .07f * h);
                // escote en V del traje (MK9): piel desde el cuello alto hasta el vientre, por la cara delantera
                PointF F(PointF q, float k) => new PointF(q.X + front.X * k, q.Y + front.Y * k);
                var neck = Lerp(pel, j[1], .97f); var belly = Lerp(pel, j[1], .12f);
                var vee = new[] { F(neck, 1.3f), F(neck, .55f), F(Lerp(neck, belly, .55f), .75f), F(belly, 1.15f), F(Lerp(neck, belly, .5f), 1.4f) };
                using (var br = Fighter.Grad(vee[1], vee[0], C(Theme.Mix(lk.Skin, Color.Black, .25f)), C(Theme.Mix(lk.Skin, Color.White, .12f)), WrapMode.TileFlipXY))
                    g.FillPolygon(br, vee);
                using (var pen = new Pen(C(lk.Trim), Math.Max(1f, .012f * h))) g.DrawLines(pen, new[] { vee[0], vee[1], vee[2], vee[3] });
                top = neck; bot = belly;
                using (var pen = new Pen(C(lk.Trim), Math.Max(1f, .014f * h)))
                    for (int k = 0; k < 4; k++)
                    {
                        var q0 = Lerp(top, bot, k / 4f); var q1 = Lerp(top, bot, (k + 1) / 4f);
                        g.DrawLine(pen, q0.X + front.X, q0.Y + front.Y, q1.X + front.X * .3f, q1.Y + front.Y * .3f);
                        g.DrawLine(pen, q0.X + front.X * .3f, q0.Y + front.Y * .3f, q1.X + front.X, q1.Y + front.Y);
                    }
                using (var pen = new Pen(C(Theme.Gold, .2f), Math.Max(1f, .012f * h))) g.DrawLine(pen, Lerp(pel, j[1], .25f), Lerp(pel, j[1], .25f + .01f));
            }
            else
            {
                // correas cruzadas
                using (var pen = new Pen(C(lk.Acc), .026f * h)) { g.DrawLine(pen, j[4], new PointF(pel.X - fw.X * .05f * h, pel.Y - fw.Y * .05f * h)); }
                using (var pen = new Pen(C(lk.AccHi, .1f), .011f * h)) { g.DrawLine(pen, j[4], new PointF(pel.X - fw.X * .05f * h, pel.Y - fw.Y * .05f * h)); }
            }
            var b0 = new PointF(pel.X - fw.X * .085f * h, pel.Y - fw.Y * .085f * h); var b1 = new PointF(pel.X + fw.X * .085f * h, pel.Y + fw.Y * .085f * h);
            using (var pen = new Pen(C(lk.Trim), .034f * h)) g.DrawLine(pen, b0, b1);
            using (var br = new SolidBrush(C(lk.AccHi))) g.FillRectangle(br, pel.X + fw.X * .05f * h - .02f * h, pel.Y + fw.Y * .05f * h - .02f * h, .04f * h, .04f * h);
            // pierna delantera y faldon
            Seg(j[10], j[11], .06f * h, .046f * h, C(lk.Suit));
            Seg(j[11], j[12], .046f * h, .035f * h, C(lk.Suit));
            Seg(j[12], j[13], .033f * h, .023f * h, C(lk.Trim, .35f), false);
            Flap(g, new PointF(pel.X + fw.X * .045f * h, pel.Y + fw.Y * .045f * h), clothA, .3f * h, C(lk.Acc), C(lk.Trim), face, h);
            // cabeza
            Seg(j[1], j[2], .04f * h, .036f * h, C(lk.Suit), false);
            if (Headless && j == J)
            {
                // cuello cortado
                using (var br = new SolidBrush(C(Color.FromArgb(150, 10, 12)))) g.FillEllipse(br, j[2].X - .035f * h, j[2].Y - .022f * h, .07f * h, .044f * h);
                using (var br = new SolidBrush(C(Color.FromArgb(225, 210, 190)))) g.FillEllipse(br, j[2].X - .01f * h, j[2].Y - .008f * h, .02f * h, .016f * h);
            }
            else Head(g, j[3], (pose[2] + pose[3]) * face, face, lk, alpha, burnt, h, C);
            // hombrera y brazo delantero
            if (!lk.SkinArms || lk.Style >= 3) Ball(j[4], .052f * h, C(Fire ? lk.Trim : lk.Mask, .1f));
            Arm(j[4], j[5], j[6], 0);

            // brazo: Mileena con piel, brazalete dorado y muñequera magenta (y un sai en cada mano); los demas, manga y vendas
            void Arm(PointF sh, PointF el, PointF hd, float dk)
            {
                if (lk.SkinArms)
                {
                    Seg(sh, el, .04f * h, .034f * h, C(lk.Skin, dk));
                    if (mil)
                    {
                        var band = Lerp(sh, el, .45f); var n = Perp(sh, el, .042f * h);
                        using (var pen = new Pen(C(Theme.Gold, dk + .1f), .028f * h)) g.DrawLine(pen, band.X - n.X, band.Y - n.Y, band.X + n.X, band.Y + n.Y);
                    }
                    Seg(el, Lerp(el, hd, .45f), .034f * h, .031f * h, C(lk.Skin, dk));
                    Seg(Lerp(el, hd, .4f), hd, .035f * h, .033f * h, C(mil ? lk.Suit : lk.Wrap, dk));   // muñequera o guante
                    if (Blade == 3 && j == J) Sai(g, el, hd, h, dk);
                    if (Blade == 4 && j == J) ArmBlade(g, el, hd, h, dk);
                    if (Blade == 5 && j == J && Storm < 0) ChaosBlade(g, el, hd, h, dk, dk == 0 ? whip : 0);
                    if (Blade == 6 && j == J && dk == 0) LeviathanAxe(g, hd, (float)(Math.Atan2(hd.Y - el.Y, hd.X - el.X) * 180 / Math.PI) + (Face > 0 ? 0 : 0), h, 1);
                    Ball(hd, .036f * h, C(lk.Style == 2 ? lk.Wrap : lk.Skin, dk + .1f));
                    return;
                }
                Seg(sh, el, .044f * h, .037f * h, C(lk.Suit, dk));
                Seg(el, hd, .037f * h, .031f * h, C(lk.Wrap, dk + (dk > 0 ? .05f : 0)));
                using (var pen = new Pen(C(lk.AccHi, dk + .2f), Math.Max(1f, .01f * h)))
                    for (int k = 1; k <= 2; k++) { var q = Lerp(el, hd, k / 3f); var n = Perp(el, hd, .034f * h); g.DrawLine(pen, q.X - n.X, q.Y - n.Y, q.X + n.X, q.Y + n.Y); }
                Ball(hd, .038f * h, C(lk.Wrap, dk + .1f));
                if ((dk == 0 || twin) && Blade > 0 && Blade < 3 && j == J) Sword(g, el, hd, h, Blade == 2);
                if (dk == 0 && Blade == 7 && j == J) IceHammer(g, el, hd, h);
            }

            if (Storm >= 0 && j == J && State == St.Act && Mv != null && Mv.Id == "bladeStorm")
            {
                // tormenta: cada espada deja detras un arco de fuego que se apaga; cadenas tensas hasta las manos
                var c0 = j[1]; float r = .8f * h;
                for (int k = 0; k < 2; k++)
                {
                    float a0 = (Storm + k * 180) * Deg;
                    var prev = new PointF(c0.X + (float)Math.Cos(a0) * r, c0.Y + (float)Math.Sin(a0) * r * .45f);
                    for (int s2 = 1; s2 <= 7; s2++)
                    {
                        float a1 = a0 - s2 * 14 * Deg, q = 1 - s2 / 8f;
                        var cur = new PointF(c0.X + (float)Math.Cos(a1) * r, c0.Y + (float)Math.Sin(a1) * r * .45f);
                        trailPen.Color = Color.FromArgb((int)(170 * q), 255, 120 + (int)(80 * q), 30); trailPen.Width = Math.Max(1, .07f * h * q);
                        g.DrawLine(trailPen, prev, cur); prev = cur;
                    }
                }
                var bl = StormBlades();
                using (var pen = new Pen(Color.FromArgb(200, 110, 92, 74), Math.Max(1f, .012f * h)) { DashStyle = DashStyle.Dot }) { g.DrawLine(pen, j[6], bl[0]); g.DrawLine(pen, j[9], bl[1]); }
                for (int k = 0; k < 2; k++)
                {
                    var p0 = bl[k]; var hand = k == 0 ? j[6] : j[9];
                    ChaosBlade(g, new PointF(p0.X - (p0.X - hand.X) * .15f, p0.Y - (p0.Y - hand.Y) * .15f), p0, h, 0, 0);
                }
            }

            if (frozen)
            {
                var bx = RectangleF.Inflate(Box, .03f * h, .02f * h); float c = .06f * h;
                var ice = new[] { new PointF(bx.Left + c, bx.Top), new PointF(bx.Right, bx.Top), new PointF(bx.Right, bx.Bottom - c), new PointF(bx.Right - c, bx.Bottom), new PointF(bx.Left, bx.Bottom), new PointF(bx.Left, bx.Top + c) };
                using (var br = Fighter.Grad(new PointF(bx.Left, bx.Top), new PointF(bx.Right, bx.Bottom), Color.FromArgb(140, 210, 240, 255), Color.FromArgb(90, 90, 170, 230))) g.FillPolygon(br, ice);
                using (var pen = new Pen(Color.FromArgb(220, 230, 248, 255), Math.Max(1f, .015f * h))) g.DrawPolygon(pen, ice);
                using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, .02f * h)))
                {
                    g.DrawLine(pen, bx.Left + bx.Width * .2f, bx.Top + bx.Height * .1f, bx.Left + bx.Width * .05f, bx.Top + bx.Height * .45f);
                    g.DrawLine(pen, bx.Left + bx.Width * .45f, bx.Top + bx.Height * .08f, bx.Left + bx.Width * .3f, bx.Top + bx.Height * .3f);
                }
            }
        }

        /// <summary>Espada en la mano, en la direccion del antebrazo: hoja de acero (o de hielo) con filo brillante, guarda dorada y empuñadura.</summary>
        static void Sword(Graphics g, PointF elbow, PointF hand, float h, bool ice)
        {
            float dx = hand.X - elbow.X, dy = hand.Y - elbow.Y, l = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-3f, ux = dx / l, uy = dy / l, nx = -uy, ny = ux;
            float len = .62f * h, w = .022f * h;
            var b0 = new PointF(hand.X + ux * .04f * h, hand.Y + uy * .04f * h);
            var blade = new[] { new PointF(b0.X + nx * w, b0.Y + ny * w), new PointF(b0.X + ux * len * .9f + nx * w * .8f, b0.Y + uy * len * .9f + ny * w * .8f),
                new PointF(b0.X + ux * len, b0.Y + uy * len), new PointF(b0.X + ux * len * .9f - nx * w, b0.Y + uy * len * .9f - ny * w), new PointF(b0.X - nx * w, b0.Y - ny * w) };
            using (var br = Fighter.Grad(blade[0], blade[4], ice ? Color.FromArgb(220, 230, 250, 255) : Color.FromArgb(245, 245, 250), ice ? Color.FromArgb(200, 90, 170, 240) : Color.FromArgb(120, 122, 132), WrapMode.TileFlipXY))
                g.FillPolygon(br, blade);
            using (var pen = new Pen(Color.FromArgb(10, 10, 12), Math.Max(1f, .01f * h))) g.DrawPolygon(pen, blade);
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, .008f * h))) g.DrawLine(pen, blade[0], blade[2]);
            using (var pen = new Pen(Theme.Gold, .03f * h)) g.DrawLine(pen, b0.X + nx * .05f * h, b0.Y + ny * .05f * h, b0.X - nx * .05f * h, b0.Y - ny * .05f * h);
            using (var pen = new Pen(Color.FromArgb(40, 30, 24), .03f * h)) g.DrawLine(pen, b0, new PointF(hand.X - ux * .07f * h, hand.Y - uy * .07f * h));
        }

        /// <summary>Cuchilla de Baraka: sale de la muñeca en la direccion del antebrazo, larga y algo curva.</summary>
        static void ArmBlade(Graphics g, PointF elbow, PointF hand, float h, float dk)
        {
            float dx = hand.X - elbow.X, dy = hand.Y - elbow.Y, l = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-3f, ux = dx / l, uy = dy / l, nx = -uy, ny = ux;
            var b0 = new PointF(hand.X - ux * .05f * h, hand.Y - uy * .05f * h); float len = .4f * h, w = .022f * h;
            var blade = new[] { new PointF(b0.X + nx * w, b0.Y + ny * w), new PointF(b0.X + ux * len * .7f + nx * w * 1.4f, b0.Y + uy * len * .7f + ny * w * 1.4f),
                new PointF(b0.X + ux * len + nx * w * 2.4f, b0.Y + uy * len + ny * w * 2.4f), new PointF(b0.X + ux * len * .75f - nx * w * .3f, b0.Y + uy * len * .75f - ny * w * .3f), new PointF(b0.X - nx * w, b0.Y - ny * w) };
            using (var br = Fighter.Grad(blade[0], blade[4], Theme.Mix(Color.FromArgb(235, 235, 240), Color.Black, dk), Theme.Mix(Color.FromArgb(120, 120, 130), Color.Black, dk), WrapMode.TileFlipXY))
                g.FillPolygon(br, blade);
            using (var pen = new Pen(Color.FromArgb(14, 12, 12), Math.Max(1f, .01f * h))) g.DrawPolygon(pen, blade);
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, .007f * h))) g.DrawLine(pen, blade[0], blade[2]);
        }

        /// <summary>Sai de Mileena: hoja central larga y dos puntas laterales curvas, empuñadura magenta.</summary>
        static void Sai(Graphics g, PointF elbow, PointF hand, float h, float dk)
        {
            float dx = hand.X - elbow.X, dy = hand.Y - elbow.Y, l = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-3f, ux = dx / l, uy = dy / l, nx = -uy, ny = ux;
            var steel = Theme.Mix(Color.FromArgb(228, 230, 238), Color.Black, dk);
            var b0 = new PointF(hand.X + ux * .03f * h, hand.Y + uy * .03f * h);
            using (var pen = new Pen(steel, Math.Max(1.2f, .016f * h)) { EndCap = LineCap.Triangle }) g.DrawLine(pen, b0, new PointF(b0.X + ux * .3f * h, b0.Y + uy * .3f * h));
            using (var pen = new Pen(steel, Math.Max(1f, .011f * h)))
                foreach (var sd in new[] { 1, -1 })
                    g.DrawBezier(pen, b0, new PointF(b0.X + nx * sd * .05f * h, b0.Y + ny * sd * .05f * h),
                        new PointF(b0.X + nx * sd * .06f * h + ux * .04f * h, b0.Y + ny * sd * .06f * h + uy * .04f * h), new PointF(b0.X + nx * sd * .045f * h + ux * .09f * h, b0.Y + ny * sd * .045f * h + uy * .09f * h));
            using (var pen = new Pen(Theme.Mix(Color.FromArgb(150, 30, 100), Color.Black, dk), .028f * h)) g.DrawLine(pen, b0, new PointF(hand.X - ux * .06f * h, hand.Y - uy * .06f * h));
        }

        /// <summary>Capucha, mascara, banda de los ojos y ojos brillantes, en el sistema de la cabeza (mirando a la derecha).</summary>
        internal static void Head(Graphics g, PointF c, float ang, int face, Look lk, float alpha, float burnt, float h, Func<Color, float, Color> C)
        {
            var st = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(ang);
            if (face < 0) g.ScaleTransform(-1, 1);
            float r = .09f * h;
            if (lk.Style == 1) { MileenaHead(g, r, h, alpha, C); g.Restore(st); return; }
            if (lk.Style == 2) { CageHead(g, r, h, alpha, C, lk == Look.Shadow); g.Restore(st); return; }
            if (lk.Style == 3) { BarakaHead(g, r, h, alpha, C, lk); g.Restore(st); return; }
            if (lk.Style == 4) { KratosHead(g, r, h, alpha, C, lk); g.Restore(st); return; }
            using (var br = Fighter.Grad(new PointF(-r, -r), new PointF(r, r), C(Theme.Mix(lk.Suit, Color.White, .2f), 0), C(lk.Suit, .5f)))
                g.FillEllipse(br, -r, -r, 2 * r, 2 * r);
            using (var path = new GraphicsPath())
            {
                path.AddPie(-r, -r, 2 * r, 2 * r, -25, 150);
                using (var br = Fighter.Grad(new PointF(0, -r), new PointF(0, r), C(Theme.Mix(lk.Mask, Color.White, .3f), 0), C(lk.Mask, .45f))) g.FillPath(br, path);
            }
            using (var pen = new Pen(C(lk.Acc, 0), Math.Max(1f, .016f * h))) g.DrawArc(pen, -r * .92f, -r * .92f, r * 1.84f, r * 1.84f, -60, 200);
            if (lk == Look.SubZero)
                using (var pen = new Pen(C(lk.Trim, .3f), Math.Max(1f, .009f * h))) for (int k = 0; k < 3; k++) g.DrawLine(pen, r * (.35f + k * .17f), r * .2f, r * (.3f + k * .17f), r * .7f);
            else
                using (var pen = new Pen(C(lk.Acc, .1f), Math.Max(1f, .012f * h))) g.DrawLine(pen, r * .15f, r * .45f, r * .95f, r * .35f);
            using (var br = new SolidBrush(C(Color.FromArgb(6, 6, 8), 0))) g.FillRectangle(br, -r * .05f, -r * .4f, r * 1.02f, r * .3f);
            var eye = burnt > .5f ? Color.FromArgb(255, 120, 30) : lk.Eye;
            using (var path = new GraphicsPath())
            {
                float er = r * .42f; var e = new PointF(r * .56f, -r * .25f);
                path.AddEllipse(e.X - er, e.Y - er * .7f, er * 2, er * 1.4f);
                using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(alpha * 230), eye), SurroundColors = new[] { Color.FromArgb(0, eye) } }) g.FillPath(br, path);
                using (var br = new SolidBrush(Color.FromArgb((int)(alpha * 255), Theme.Mix(eye, Color.White, .6f)))) g.FillEllipse(br, e.X - r * .2f, e.Y - r * .06f, r * .4f, r * .12f);
            }
            using (var pen = new Pen(Color.FromArgb((int)(alpha * 220), 6, 6, 8), Math.Max(1f, .014f * h))) g.DrawEllipse(pen, -r, -r, 2 * r, 2 * r);
            g.Restore(st);
        }

        /// <summary>Mileena (MK9): melena negra lisa hasta los hombros con flequillo, cara, mascara magenta con ribete y ojos amarillos.</summary>
        static void MileenaHead(Graphics g, float r, float h, float alpha, Func<Color, float, Color> C)
        {
            var hair = Color.FromArgb(20, 18, 24);
            // melena por detras: cae recta hasta los hombros
            var back = new[] { new PointF(-r * .2f, -r * 1.08f), new PointF(-r * 1.15f, -r * .5f), new PointF(-r * 1.3f, r * 1.2f), new PointF(-r * 1.05f, r * 2.3f),
                new PointF(-r * .2f, r * 2.2f), new PointF(r * .1f, r * 1.1f), new PointF(r * .5f, r * .2f) };
            using (var br = Fighter.Grad(new PointF(-r * 1.3f, -r), new PointF(r * .3f, r * 2.2f), C(Theme.Mix(hair, Color.White, .12f), 0), C(hair, 0)))
                g.FillClosedCurve(br, back, FillMode.Winding, .4f);
            // cara
            using (var br = Fighter.Grad(new PointF(-r, -r), new PointF(r, r), C(Color.FromArgb(232, 192, 160), 0), C(Color.FromArgb(178, 128, 98), 0)))
                g.FillEllipse(br, -r * .92f, -r * .95f, r * 1.84f, r * 1.9f);
            // mascara magenta de la nariz a la barbilla, con ribete oscuro y costura central
            using (var path = new GraphicsPath())
            {
                path.AddPie(-r * .92f, -r * .95f, r * 1.84f, r * 1.9f, -12, 140);
                using (var br = Fighter.Grad(new PointF(0, -r * .1f), new PointF(0, r), C(Color.FromArgb(236, 86, 190), 0), C(Color.FromArgb(150, 24, 110), 0))) g.FillPath(br, path);
                using (var pen = new Pen(C(Color.FromArgb(40, 20, 34), 0), Math.Max(1f, .012f * h))) g.DrawPath(pen, path);
            }
            using (var pen = new Pen(C(Color.FromArgb(90, 20, 70), 0), Math.Max(1f, .008f * h))) g.DrawLine(pen, r * .62f, r * .05f, r * .55f, r * .8f);
            // ojos amarillos
            foreach (var ex in new[] { r * .62f, r * .2f })
            {
                using (var br = new SolidBrush(C(Color.FromArgb(30, 16, 20), 0))) g.FillEllipse(br, ex - r * .17f, -r * .34f, r * .34f, r * .16f);
                using (var br = new SolidBrush(Color.FromArgb((int)(alpha * 255), 255, 205, 70))) g.FillEllipse(br, ex - r * .07f, -r * .32f, r * .14f, r * .12f);
            }
            // flequillo y raya por encima de los ojos
            var fringe = new[] { new PointF(-r * 1.05f, -r * .1f), new PointF(-r * .9f, -r * .85f), new PointF(r * .1f, -r * 1.12f), new PointF(r * .95f, -r * .55f),
                new PointF(r * .85f, -r * .38f), new PointF(r * .35f, -r * .52f), new PointF(-r * .2f, -r * .45f), new PointF(-r * .55f, -r * .1f) };
            using (var br = Fighter.Grad(new PointF(0, -r * 1.1f), new PointF(0, -r * .1f), C(Theme.Mix(hair, Color.White, .22f), 0), C(hair, 0)))
                g.FillClosedCurve(br, fringe, FillMode.Winding, .35f);
            using (var pen = new Pen(Color.FromArgb((int)(alpha * 90), 255, 255, 255), Math.Max(1f, .008f * h))) g.DrawArc(pen, -r * .7f, -r * 1.02f, r * 1.2f, r * .9f, 200, 70);
        }

        /// <summary>Johnny Cage (MKX): pelo corto oscuro peinado, cara, mandibula marcada y gafas de sol con brillo.</summary>
        static void CageHead(Graphics g, float r, float h, float alpha, Func<Color, float, Color> C, bool ghost)
        {
            var skin = ghost ? Color.FromArgb(60, 220, 100) : Color.FromArgb(226, 178, 140);
            using (var br = Fighter.Grad(new PointF(-r, -r), new PointF(r, r), C(Theme.Mix(skin, Color.White, .1f), 0), C(Theme.Mix(skin, Color.Black, .3f), 0)))
                g.FillEllipse(br, -r * .9f, -r * .95f, r * 1.8f, r * 1.95f);
            var hair = ghost ? Color.FromArgb(30, 160, 70) : Color.FromArgb(46, 32, 24);
            var cap = new[] { new PointF(-r * .95f, r * .1f), new PointF(-r * .9f, -r * .7f), new PointF(-r * .1f, -r * 1.12f), new PointF(r * .8f, -r * .85f), new PointF(r * .95f, -r * .45f), new PointF(r * .2f, -r * .6f), new PointF(-r * .45f, -r * .2f) };
            using (var br = new SolidBrush(C(hair, 0))) g.FillClosedCurve(br, cap, FillMode.Winding, .35f);
            using (var br = new SolidBrush(C(Color.FromArgb(8, 8, 10), 0))) { g.FillRectangle(br, r * .05f, -r * .36f, r * .95f, r * .28f); g.FillRectangle(br, -r * .2f, -r * .3f, r * .3f, r * .08f); }
            using (var pen = new Pen(Color.FromArgb((int)(alpha * 200), 200, 220, 255), Math.Max(1f, .008f * h))) g.DrawLine(pen, r * .3f, -r * .3f, r * .75f, -r * .3f);
            using (var pen = new Pen(C(Theme.Mix(skin, Color.Black, .45f), 0), Math.Max(1f, .01f * h))) { g.DrawArc(pen, -r * .2f, r * .1f, r * 1.1f, r * .8f, 10, 70); g.DrawLine(pen, r * .45f, r * .38f, r * .75f, r * .34f); }
        }

        /// <summary>Kratos: calvo de piel ceniza, franja roja del tatuaje sobre el ojo, ceño marcado, ojo ambar y perilla oscura.</summary>
        static void KratosHead(Graphics g, float r, float h, float alpha, Func<Color, float, Color> C, Look lk)
        {
            using (var br = Fighter.Grad(new PointF(-r, -r), new PointF(r, r), C(Theme.Mix(lk.Skin, Color.White, .2f), 0), C(Theme.Mix(lk.Skin, Color.Black, .4f), 0)))
                g.FillEllipse(br, -r * .95f, -r, r * 1.9f, r * 2);
            var tattoo = new[] { new PointF(-r * .5f, -r * .98f), new PointF(-r * .1f, -r * .98f), new PointF(r * .55f, -r * .3f), new PointF(r * .5f, r * .55f), new PointF(r * .3f, r * .5f), new PointF(r * .3f, -r * .2f) };
            using (var br = new SolidBrush(C(Color.FromArgb(178, 22, 18), 0))) g.FillPolygon(br, tattoo);
            using (var br = new SolidBrush(C(Color.FromArgb(40, 30, 26), 0)))
            {
                g.FillPie(br, -r * .1f, r * .1f, r * 1.05f, r * .95f, 0, 180);          // perilla y barba
                g.FillRectangle(br, r * .2f, -r * .42f, r * .75f, r * .1f);              // ceño
            }
            using (var br = new SolidBrush(Color.FromArgb((int)(alpha * 255), lk.Eye))) g.FillEllipse(br, r * .52f, -r * .3f, r * .2f, r * .13f);
            using (var pen = new Pen(Color.FromArgb((int)(alpha * 220), 6, 6, 8), Math.Max(1f, .014f * h))) g.DrawEllipse(pen, -r * .95f, -r, r * 1.9f, r * 2);
        }

        /// <summary>
        /// Hacha Leviatan: hoja barbada grande con el filo curvo paralelo al mango y la barba que baja hacia la mano, contrafilo de
        /// martillo, abrazadera de bronce, runas azules y mango de cuero con pomo. 'grip' es la mano (o el centro de giro si spin).
        /// </summary>
        internal static void LeviathanAxe(Graphics g, PointF grip, float ang, float h, float alpha, bool spin = false)
        {
            var st = g.Save();
            g.TranslateTransform(grip.X, grip.Y); g.RotateTransform(ang);
            float L = .5f * h;
            if (spin) g.TranslateTransform(-L * .78f, .08f * h);   // gira sobre el centro de masa (cerca de la cabeza)
            int a = (int)(255 * alpha);
            // mango con vendas y pomo
            using (var pen = new Pen(Color.FromArgb(a, 62, 40, 26), .032f * h) { StartCap = LineCap.Round }) g.DrawLine(pen, -.06f * h, 0, L * 1.02f, 0);
            using (var pen = new Pen(Color.FromArgb(a, 120, 92, 56), Math.Max(1f, .009f * h)))
                for (int k = 0; k < 6; k++) { float x = k * .045f * h; g.DrawLine(pen, x, -.017f * h, x + .025f * h, .017f * h); }
            using (var br = new SolidBrush(Color.FromArgb(a, 170, 128, 60))) g.FillEllipse(br, -.09f * h, -.026f * h, .052f * h, .052f * h);
            // hoja: la barba baja hacia la mano, filo largo y curvo, punta alta
            var bit = new[]
            {
                new PointF(L * .82f, -.03f * h), new PointF(L * .74f, -.09f * h), new PointF(L * .58f, -.19f * h), new PointF(L * .64f, -.27f * h),
                new PointF(L * .84f, -.31f * h), new PointF(L * 1.04f, -.3f * h), new PointF(L * 1.1f, -.22f * h), new PointF(L * 1.02f, -.1f * h), new PointF(L * .98f, -.03f * h)
            };
            using (var br = Fighter.Grad(new PointF(L * .7f, -.3f * h), new PointF(L * 1f, 0), Color.FromArgb(a, 196, 210, 224), Color.FromArgb(a, 70, 82, 96)))
                g.FillClosedCurve(br, bit, FillMode.Winding, .28f);
            // bisel del filo, mas claro y helado
            var edge = new[] { new PointF(L * .6f, -.22f * h), new PointF(L * .66f, -.28f * h), new PointF(L * .84f, -.315f * h), new PointF(L * 1.04f, -.305f * h), new PointF(L * 1.1f, -.24f * h) };
            using (var pen = new Pen(Color.FromArgb(a, 230, 246, 255), Math.Max(1.2f, .02f * h)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(pen, edge, .4f);
            using (var pen = new Pen(Color.FromArgb(a, 16, 18, 22), Math.Max(1f, .011f * h))) g.DrawClosedCurve(pen, bit, .28f, FillMode.Winding);
            // contrafilo de martillo al otro lado
            using (var br = Fighter.Grad(new PointF(0, .02f * h), new PointF(0, .1f * h), Color.FromArgb(a, 150, 160, 170), Color.FromArgb(a, 60, 66, 74)))
                g.FillRectangle(br, L * .84f, .02f * h, L * .14f, .075f * h);
            // abrazadera de bronce en el ojo
            using (var br = Fighter.Grad(new PointF(0, -.05f * h), new PointF(0, .05f * h), Color.FromArgb(a, 222, 176, 92), Color.FromArgb(a, 120, 82, 30)))
            { g.FillRectangle(br, L * .8f, -.05f * h, L * .06f, .1f * h); g.FillRectangle(br, L * .96f, -.05f * h, L * .05f, .1f * h); }
            // runas que brillan
            using (var pen = new Pen(Color.FromArgb(a * 9 / 10, 130, 226, 255), Math.Max(1f, .011f * h)))
            {
                g.DrawLine(pen, L * .8f, -.13f * h, L * .86f, -.2f * h); g.DrawLine(pen, L * .86f, -.2f * h, L * .9f, -.14f * h);
                g.DrawLine(pen, L * .94f, -.12f * h, L * .98f, -.21f * h); g.DrawLine(pen, L * .92f, -.17f * h, L * 1.01f, -.16f * h);
            }
            g.Restore(st);
        }

        /// <summary>Martillo de hielo de Sub-Zero: mango helado y cabeza de bloque de hielo con aristas brillantes.</summary>
        static void IceHammer(Graphics g, PointF elbow, PointF hand, float h)
        {
            float dx = hand.X - elbow.X, dy = hand.Y - elbow.Y, l = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-3f, ux = dx / l, uy = dy / l, nx = -uy, ny = ux;
            var end = new PointF(hand.X + ux * .36f * h, hand.Y + uy * .36f * h);
            using (var pen = new Pen(Color.FromArgb(220, 170, 220, 250), .028f * h)) g.DrawLine(pen, new PointF(hand.X - ux * .05f * h, hand.Y - uy * .05f * h), end);
            float hw = .13f * h, hl = .09f * h;
            var head = new[] { new PointF(end.X + nx * hw - ux * hl, end.Y + ny * hw - uy * hl), new PointF(end.X + nx * hw + ux * hl, end.Y + ny * hw + uy * hl),
                new PointF(end.X - nx * hw + ux * hl, end.Y - ny * hw + uy * hl), new PointF(end.X - nx * hw - ux * hl, end.Y - ny * hw - uy * hl) };
            using (var br = Fighter.Grad(head[0], head[2], Color.FromArgb(230, 220, 244, 255), Color.FromArgb(200, 80, 150, 220))) g.FillPolygon(br, head);
            using (var pen = new Pen(Color.FromArgb(240, 240, 252, 255), Math.Max(1f, .012f * h))) g.DrawPolygon(pen, head);
            using (var pen = new Pen(Color.FromArgb(160, 255, 255, 255), Math.Max(1f, .01f * h))) g.DrawLine(pen, head[0], head[2]);
        }

        /// <summary>Espadas del Caos: hoja curva incandescente en la mano, con cadena enrollada al antebrazo; en el latigazo sale lejos.</summary>
        static void ChaosBlade(Graphics g, PointF elbow, PointF hand, float h, float dk, float ext)
        {
            float dx = hand.X - elbow.X, dy = hand.Y - elbow.Y, l = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-3f, ux = dx / l, uy = dy / l, nx = -uy, ny = ux;
            float reach = ext * .85f * h;
            var b0 = new PointF(hand.X + ux * (.03f * h + reach), hand.Y + uy * (.03f * h + reach));
            if (ext > 0)
                using (var pen = new Pen(Color.FromArgb(200, 120, 100, 80), Math.Max(1f, .012f * h)) { DashStyle = DashStyle.Dot }) g.DrawLine(pen, hand, b0);
            using (var pen = new Pen(Theme.Mix(Color.FromArgb(110, 90, 70), Color.Black, dk), Math.Max(1f, .01f * h)))
                for (int k = 0; k < 3; k++) { var q = Lerp(elbow, hand, .25f + k * .25f); g.DrawLine(pen, q.X - nx * .035f * h, q.Y - ny * .035f * h, q.X + nx * .035f * h, q.Y + ny * .035f * h); }
            float len = .26f * h, w = .03f * h;
            var blade = new[] { new PointF(b0.X - nx * w * .6f, b0.Y - ny * w * .6f), new PointF(b0.X + ux * len * .5f + nx * w * 1.4f, b0.Y + uy * len * .5f + ny * w * 1.4f),
                new PointF(b0.X + ux * len + nx * w * 2.2f, b0.Y + uy * len + ny * w * 2.2f), new PointF(b0.X + ux * len * .6f + nx * w * .2f, b0.Y + uy * len * .6f + ny * w * .2f) };
            using (var br = Fighter.Grad(blade[0], blade[2], Theme.Mix(Color.FromArgb(255, 190, 90), Color.Black, dk), Theme.Mix(Color.FromArgb(200, 60, 20), Color.Black, dk)))
                g.FillPolygon(br, blade);
            using (var pen = new Pen(Color.FromArgb(20, 10, 6), Math.Max(1f, .009f * h))) g.DrawPolygon(pen, blade);
        }

        /// <summary>Baraka (MKX): cabeza calva gris con crestas, ojos amarillos hundidos y boca enorme de dientes afilados.</summary>
        static void BarakaHead(Graphics g, float r, float h, float alpha, Func<Color, float, Color> C, Look lk)
        {
            using (var br = Fighter.Grad(new PointF(-r, -r), new PointF(r, r), C(Theme.Mix(lk.Skin, Color.White, .15f), 0), C(Theme.Mix(lk.Skin, Color.Black, .45f), 0)))
                g.FillEllipse(br, -r, -r * 1.05f, r * 2, r * 2.05f);
            using (var pen = new Pen(C(Theme.Mix(lk.Skin, Color.Black, .5f), 0), Math.Max(1f, .012f * h)))
                for (int k = 0; k < 3; k++) g.DrawArc(pen, -r * (.9f - k * .15f), -r * (1.02f - k * .1f), r * (1.6f - k * .3f), r * .9f, 200, 110);
            // boca: encias rojas y dientes en sierra de oreja a oreja
            var mouth = new RectangleF(-r * .1f, r * .05f, r * 1.05f, r * .55f);
            using (var br = new SolidBrush(C(Color.FromArgb(120, 20, 24), 0))) g.FillEllipse(br, mouth);
            using (var br = new SolidBrush(C(Color.FromArgb(240, 232, 210), 0)))
                for (int k = 0; k < 6; k++)
                {
                    float x0 = mouth.Left + mouth.Width * k / 6f, x1 = x0 + mouth.Width / 6f, xm = (x0 + x1) / 2;
                    g.FillPolygon(br, new[] { new PointF(x0, mouth.Top + r * .08f), new PointF(x1, mouth.Top + r * .08f), new PointF(xm, mouth.Top + r * .3f) });
                    g.FillPolygon(br, new[] { new PointF(x0, mouth.Bottom - r * .06f), new PointF(x1, mouth.Bottom - r * .06f), new PointF(xm, mouth.Bottom - r * .28f) });
                }
            using (var br = new SolidBrush(C(Color.FromArgb(20, 14, 10), 0))) g.FillEllipse(br, r * .25f, -r * .4f, r * .55f, r * .26f);
            using (var br = new SolidBrush(Color.FromArgb((int)(alpha * 255), lk.Eye))) g.FillEllipse(br, r * .45f, -r * .34f, r * .2f, r * .14f);
            using (var pen = new Pen(Color.FromArgb((int)(alpha * 220), 6, 6, 8), Math.Max(1f, .014f * h))) g.DrawEllipse(pen, -r, -r * 1.05f, r * 2, r * 2.05f);
        }

        /// <summary>Faldon: panel que cuelga del cinturon con angulo a (grados desde abajo, + adelante).</summary>
        static void Flap(Graphics g, PointF top, float a, float len, Color fill, Color trim, int face, float h)
        {
            float s = (float)Math.Sin(a * Deg) * face, c = (float)Math.Cos(a * Deg);
            var dir = new PointF(s, c); var n = new PointF(-c, s);
            float w0 = .055f * h, w1 = .04f * h;
            var poly = new[]
            {
                new PointF(top.X - n.X * w0, top.Y - n.Y * w0), new PointF(top.X + n.X * w0, top.Y + n.Y * w0),
                new PointF(top.X + dir.X * len * .88f + n.X * w1, top.Y + dir.Y * len * .88f + n.Y * w1),
                new PointF(top.X + dir.X * len, top.Y + dir.Y * len),
                new PointF(top.X + dir.X * len * .88f - n.X * w1, top.Y + dir.Y * len * .88f - n.Y * w1),
            };
            using (var br = Fighter.Grad(poly[0], poly[1], Theme.Mix(fill, Color.White, .15f), Theme.Mix(fill, Color.Black, .35f), WrapMode.TileFlipXY))
                g.FillPolygon(br, poly);
            using (var pen = new Pen(trim, Math.Max(1f, .014f * h))) g.DrawPolygon(pen, poly);
        }

        static PointF Lerp(PointF a, PointF b, float t) => new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        static PointF Perp(PointF a, PointF b, float k)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, l = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-3f;
            return new PointF(-dy / l * k, dx / l * k);
        }
    }
}
