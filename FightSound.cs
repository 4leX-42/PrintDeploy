using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Sonido de la pelea: efectos sintetizados (ruido filtrado, golpes, chasquidos, metal), musica de fondo generada y los audios
    /// incrustados (Payload\fight-audio, IMA ADPCM 4:1). Mezclador propio sobre waveOut (22 kHz mono, 16 bits) en un hilo.
    /// Start (al encender la pelea) carga todo en segundo plano sin bloquear la ventana; lo que se pide antes de que este listo se
    /// guarda y suena al terminar la carga. Stop (al apagarla) cierra el dispositivo y suelta la memoria.
    /// </summary>
    sealed class Sfx : IDisposable
    {
        const int Rate = 22050, Block = 1024, Buffers = 4;
        Dictionary<string, float[]> bank = new Dictionary<string, float[]>();
        Dictionary<string, float[]> voices = new Dictionary<string, float[]>();
        readonly List<(float[] s, int pos, float vol, bool loop, string id)> playing = new List<(float[], int, float, bool, string)>();
        readonly List<(string id, float vol, bool loop, bool voice, long at)> pending = new List<(string, float, bool, bool, long)>();
        readonly object gate = new object();
        readonly Random rng = new Random(7);
        Thread thread; volatile bool run, loaded; int gen;
        IntPtr wave;

        /// <summary>Abre el dispositivo, carga los sonidos en segundo plano y empieza a mezclar.</summary>
        public void Start()
        {
            if (run) return;
            run = true;
            int g = ++gen;
            thread = new Thread(() => { Load(g); Pump(); }) { IsBackground = true, Name = "fight-sound", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        /// <summary>Corta todo, cierra el dispositivo y suelta los sonidos de memoria.</summary>
        public void Stop()
        {
            run = false; gen++;
            thread?.Join(800); thread = null;
            lock (gate) { playing.Clear(); pending.Clear(); bank = new Dictionary<string, float[]>(); voices = new Dictionary<string, float[]>(); loaded = false; }
        }

        public void Dispose() => Stop();

        public void Play(string id, float vol = 1) => Queue(id, vol * .8f, false, false);
        /// <summary>Sonido en bucle (musica de fondo) hasta que se corte.</summary>
        public void Loop(string id, float vol) => Queue(id, vol, true, false);
        public void Say(string key) => Queue(key, 1, false, true);

        void Queue(string id, float vol, bool loop, bool voice)
        {
            if (!run) return;
            lock (gate)
            {
                if (!loaded) { if (pending.Count < 12) pending.Add((id, vol, loop, voice, Environment.TickCount)); return; }
                Add(id, vol, loop, voice);
            }
        }

        // con el cerrojo tomado
        void Add(string id, float vol, bool loop, bool voice)
        {
            if (!(voice ? voices : bank).TryGetValue(id, out var s)) return;
            if (loop && playing.Exists(p => p.id == id)) return;
            if (playing.Count < 24) playing.Add((s, 0, vol, loop, id));
        }

        /// <summary>Corta un sonido concreto (p. ej. el tema de entrada cuando empieza la musica).</summary>
        public void Cut(string id) { lock (gate) { playing.RemoveAll(p => p.id == id); pending.RemoveAll(p => p.id == id); } }

        /// <summary>Corta todo lo que suena.</summary>
        public void Hush() { lock (gate) { playing.Clear(); pending.Clear(); } }

        /// <summary>Sintetiza y decodifica fuera del cerrojo; publica de golpe y suelta lo que se pidio mientras tanto (si es reciente).</summary>
        void Load(int g)
        {
            var b = new Dictionary<string, float[]>(); var v = new Dictionary<string, float[]>();
            try { Synth(b); LoadOwn(b, v); } catch (Exception ex) { Crash.Log("sonido: carga", ex); }
            lock (gate)
            {
                if (g != gen) return;   // se apago mientras cargaba
                bank = b; voices = v; loaded = true;
                long now = Environment.TickCount;
                foreach (var q in pending) if (now - q.at < 2500) Add(q.id, q.vol, q.loop, q.voice);
                pending.Clear();
            }
        }

        // ---------- audios propios ----------
        /// <summary>
        /// Carpetas con audios propios (.wav PCM o IMA ADPCM, cualquier frecuencia, mono o estereo): "fight-audio" junto al .exe y en
        /// %LocalAppData%\PrintDeploy. Cada fichero sustituye al sonido sintetizado del mismo nombre, a la voz del locutor o añade voces
        /// de luchador y memes. No van al repo.
        /// </summary>
        public static string[] AudioDirs => new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fight-audio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintDeploy", "fight-audio"),
        };

        static readonly Dictionary<string, string> FileToLine = new Dictionary<string, string>
        {
            ["round1"] = "ROUND 1", ["round2"] = "ROUND 2", ["round3"] = "ROUND 3", ["round4"] = "ROUND 4", ["round5"] = "ROUND 5", ["fight"] = "FIGHT!",
            ["finishhim"] = "FINISH HIM!", ["finishher"] = "FINISH HER!", ["fatality"] = "FATALITY", ["scorpionwins"] = "SCORPION WINS",
            ["subzerowins"] = "SUB-ZERO WINS", ["mileenawins"] = "MILEENA WINS", ["johnnycagewins"] = "JOHNNY CAGE WINS", ["barakawins"] = "BARAKA WINS",
            ["kratoswins"] = "KRATOS WINS", ["flawless"] = "FLAWLESS VICTORY", ["getoverhere"] = "GET OVER HERE!",
        };

        /// <summary>Audios incrustados en el .exe y, encima, los de las carpetas.</summary>
        static void LoadOwn(Dictionary<string, float[]> b, Dictionary<string, float[]> v)
        {
            var asm = typeof(Sfx).Assembly;
            foreach (var res in asm.GetManifestResourceNames())
            {
                if (!res.StartsWith("fight-audio/", StringComparison.OrdinalIgnoreCase)) continue;
                try { using (var st = asm.GetManifestResourceStream(res)) Own(b, v, Path.GetFileNameWithoutExtension(res.Substring(12)), ReadWav(st)); } catch { }
            }
            foreach (var dir in AudioDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*.wav"))
                    try { using (var st = File.OpenRead(file)) Own(b, v, Path.GetFileNameWithoutExtension(file), ReadWav(st)); } catch { }
            }
        }

        static void Own(Dictionary<string, float[]> b, Dictionary<string, float[]> v, string name, float[] s)
        {
            if (s == null) return;
            name = name.ToLowerInvariant().Replace(" ", "").Replace("-", "");
            if (FileToLine.TryGetValue(name, out var line)) v[line] = s; else b[name] = s;
        }

        static readonly int[] ImaStep =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230,
            253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327,
            3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };
        static readonly int[] ImaIndex = { -1, -1, -1, -1, 2, 4, 6, 8 };

        /// <summary>WAV PCM (8 o 16 bits) o IMA ADPCM, mono o estereo, a mono 22 kHz en float.</summary>
        static float[] ReadWav(Stream stream)
        {
            using (var r = new BinaryReader(stream))
            {
                if (new string(r.ReadChars(4)) != "RIFF") return null;
                r.ReadInt32();
                if (new string(r.ReadChars(4)) != "WAVE") return null;
                int ch = 1, rate = Rate, bits = 16, align = 0, tag = 1; byte[] pcm = null;
                while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
                {
                    string id = new string(r.ReadChars(4)); int len = r.ReadInt32();
                    if (id == "fmt ")
                    {
                        tag = r.ReadInt16(); ch = r.ReadInt16(); rate = r.ReadInt32(); r.ReadInt32(); align = r.ReadInt16(); bits = r.ReadInt16();
                        if (tag != 1 && tag != 0x11 && tag != -2) return null;
                        r.BaseStream.Seek(len - 16, SeekOrigin.Current);
                    }
                    else if (id == "data") { pcm = r.ReadBytes(len); break; }
                    else r.BaseStream.Seek(len + (len & 1), SeekOrigin.Current);
                }
                if (pcm == null) return null;
                float[] mono;
                if (tag == 0x11) mono = Ima(pcm, align, ch);
                else
                {
                    if (bits != 8 && bits != 16) return null;
                    int bps = bits / 8, frames = pcm.Length / (bps * ch);
                    mono = new float[frames];
                    for (int i = 0; i < frames; i++)
                    {
                        float v = 0;
                        for (int c = 0; c < ch; c++) { int at = (i * ch + c) * bps; v += bits == 16 ? BitConverter.ToInt16(pcm, at) / 32768f : (pcm[at] - 128) / 128f; }
                        mono[i] = v / ch;
                    }
                }
                if (rate == Rate) return mono;
                int n = (int)((long)mono.Length * Rate / rate), last = mono.Length - 1;
                var o = new float[n]; float step = rate / (float)Rate;
                for (int i = 0; i < n; i++) { float q = i * step; int k = (int)q; float f = q - k; o[i] = k < last ? mono[k] * (1 - f) + mono[k + 1] * f : mono[last]; }
                return o;
            }
        }

        /// <summary>Decodificador IMA ADPCM (formato WAV, bloques con cabecera de predictor e indice; solo se usa el primer canal).</summary>
        static float[] Ima(byte[] d, int align, int ch)
        {
            if (align <= 4 * ch) return null;
            int perBlock = (align - 4 * ch) * 2 / ch + 1, blocks = d.Length / align;
            var o = new float[blocks * perBlock]; int w = 0;
            for (int b = 0; b < blocks; b++)
            {
                int at = b * align;
                int pred = (short)(d[at] | d[at + 1] << 8), idx = Math.Min(88, (int)d[at + 2]);
                o[w++] = pred / 32768f;
                int dataAt = at + 4 * ch, end = at + align;
                for (int p = dataAt; p < end; p += ch == 1 ? 1 : 8)
                    for (int q = 0; q < (ch == 1 ? 1 : 4); q++)
                    {
                        if (p + q >= end) break;
                        byte by = d[p + q];
                        for (int nib = 0; nib < 2; nib++)
                        {
                            int code = nib == 0 ? by & 15 : by >> 4, step = ImaStep[idx], diff = step >> 3;
                            if ((code & 4) != 0) diff += step; if ((code & 2) != 0) diff += step >> 1; if ((code & 1) != 0) diff += step >> 2;
                            pred += (code & 8) != 0 ? -diff : diff; pred = Math.Max(-32768, Math.Min(32767, pred));
                            idx = Math.Max(0, Math.Min(88, idx + ImaIndex[code & 7]));
                            if (w < o.Length) o[w++] = pred / 32768f;
                        }
                    }
            }
            if (w < o.Length) Array.Resize(ref o, w);
            return o;
        }

        // ---------- ciclo del dispositivo ----------
        void Pump()
        {
            try { PumpLoop(); }
            catch (Exception ex) { run = false; Crash.Log("sonido", ex); }
        }

        void PumpLoop()
        {
            var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = Rate * 2 };
            if (waveOutOpen(out wave, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) { run = false; return; }
            int hdrSize = Marshal.SizeOf(typeof(WAVEHDR)), flagsAt = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
            var hdrs = new IntPtr[Buffers]; var data = new IntPtr[Buffers];
            var mix = new float[Block]; var pcm = new short[Block];
            try
            {
                for (int i = 0; i < Buffers; i++)
                {
                    data[i] = Marshal.AllocHGlobal(Block * 2);
                    hdrs[i] = Marshal.AllocHGlobal(hdrSize);
                    Marshal.StructureToPtr(new WAVEHDR { lpData = data[i], dwBufferLength = Block * 2 }, hdrs[i], false);
                    waveOutPrepareHeader(wave, hdrs[i], hdrSize);
                    Fill(mix, pcm, data[i]);
                    waveOutWrite(wave, hdrs[i], hdrSize);
                }
                while (run)
                {
                    for (int i = 0; i < Buffers; i++)
                        if ((Marshal.ReadInt32(hdrs[i], flagsAt) & 1) != 0)   // WHDR_DONE
                        { Fill(mix, pcm, data[i]); waveOutWrite(wave, hdrs[i], hdrSize); }
                    Thread.Sleep(8);
                }
            }
            finally
            {
                waveOutReset(wave);
                for (int i = 0; i < Buffers; i++)
                {
                    if (hdrs[i] != IntPtr.Zero) { waveOutUnprepareHeader(wave, hdrs[i], hdrSize); Marshal.FreeHGlobal(hdrs[i]); }
                    if (data[i] != IntPtr.Zero) Marshal.FreeHGlobal(data[i]);
                }
                waveOutClose(wave); wave = IntPtr.Zero;
            }
        }

        void Fill(float[] mix, short[] pcm, IntPtr dst)
        {
            Array.Clear(mix, 0, mix.Length);
            lock (gate)
            {
                for (int v = playing.Count - 1; v >= 0; v--)
                {
                    var (s, pos, vol, loop, id) = playing[v];
                    for (int i = 0; i < Block; i++)
                    {
                        if (pos >= s.Length) { if (!loop) break; pos = 0; }
                        mix[i] += s[pos++] * vol;
                    }
                    if (pos >= s.Length && !loop) playing.RemoveAt(v); else playing[v] = (s, pos, vol, loop, id);
                }
            }
            for (int i = 0; i < Block; i++)
            {
                float x = mix[i] * .55f;
                x = x / (1 + Math.Abs(x));   // limitador suave
                pcm[i] = (short)(x * 32000);
            }
            Marshal.Copy(pcm, 0, dst, Block);
        }

        // ---------- sintesis de efectos ----------
        static void Fill(Dictionary<string, float[]> bank, string id, float[] s) => bank[id] = s;

        void Synth(Dictionary<string, float[]> bank)
        {
            bank["punch"] = Hit(.09f, 95, 2600, .9f);
            bank["kick"] = Hit(.14f, 70, 1800, 1);
            bank["thud"] = Noise(.18f, 500, .7f);
            bank["heavyhit"] = Hit(.2f, 60, 1500, 1);
            bank["block"] = Metal(.12f, 1900, 2850, .55f);
            bank["swing"] = Whoosh(.16f, 1400, .35f);
            bank["slash"] = Whoosh(.22f, 3800, .7f);
            bank["fire"] = Fire(.7f);
            bank["teleport"] = Whoosh(.35f, 900, .6f);
            bank["ice"] = Ice(.35f);
            bank["shatter"] = Shatter(.6f);
            bank["chain"] = Chain(.45f);
            bank["splat"] = Mix(Hit(.25f, 48, 900, 1), Noise(.18f, 600, .5f));
            bank["blood"] = Wet(.35f);
            bank["music"] = Techno();
        }

        /// <summary>
        /// Musica de fondo instrumental (sin voces), techno oscuro al estilo de MK: bombo a 132 BPM, palmada en 2 y 4, charles a
        /// contratiempo, bajo de sierra en mi menor a semicorcheas, pad de acordes (Em, Em, C, D) que respira con el bombo y, en la
        /// segunda mitad, un arpegio agudo. 16 compases que encajan en bucle sin corte.
        /// </summary>
        float[] Techno()
        {
            const float bpm = 132; float beat = 60f / bpm, step = beat / 4; const int bars = 16;
            var b = Buf(bars * 4 * beat); int n = b.Length;
            float[] roots = { 41.2f, 41.2f, 32.7f, 36.7f };                       // E1, E1, C1, D1
            float[][] chords = { new[] { 164.8f, 196f, 246.9f }, new[] { 164.8f, 196f, 246.9f }, new[] { 130.8f, 164.8f, 196f }, new[] { 146.8f, 185f, 220f } };
            int[] bassPat = { 0, 0, 12, 0, 7, 0, 12, 0, 0, 0, 12, 0, 10, 0, 7, 3 };   // semitonos sobre la fundamental
            float[] arp = { 329.6f, 392f, 493.9f, 659.3f, 493.9f, 392f, 329.6f, 246.9f };
            float bassLp = 0, padLp = 0, hatPrev = 0, clapLp = 0; double bp = 0;
            var rnd = new Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, inBeat = t % beat, inStep = t % step;
                int s16 = (int)(t / step), bar = (s16 / 16) % bars, chord = bar % 4, stepInBar = s16 % 16;
                // bombo: golpe grave corto en cada tiempo
                float kEnv = (float)Math.Exp(-inBeat * 22);
                float kick = (float)Math.Sin(2 * Math.PI * (48 * inBeat + 90 * (1 - Math.Exp(-inBeat * 40)) / 40)) * kEnv;
                float duck = 1 - .7f * kEnv;                                    // el resto respira con el bombo
                // palmada en 2 y 4
                int beatIdx = (int)(t / beat) % 4;
                float nz = (float)(rnd.NextDouble() * 2 - 1);
                clapLp += (nz - clapLp) * .35f;
                float clap = (beatIdx == 1 || beatIdx == 3) ? (nz - clapLp) * (float)Math.Exp(-inBeat * 30) * .5f : 0;
                // charles a contratiempo
                float off = (t + beat / 2) % beat, hp = nz - hatPrev; hatPrev = nz;
                float hat = hp * (float)Math.Exp(-off * 90) * .18f;
                // bajo: sierra filtrada a semicorcheas
                float f = roots[chord] * (float)Math.Pow(2, bassPat[stepInBar] / 12.0) * 2;
                bp += f / Rate; float saw = (float)(2 * (bp - Math.Floor(bp)) - 1);
                bassLp += (saw - bassLp) * .09f;
                float bass = bassLp * (float)Math.Exp(-inStep * 14) * .55f * duck;
                // pad: acorde suave con algo de desafinado
                float pad = 0;
                foreach (var cf in chords[chord]) pad += (float)(Math.Sin(2 * Math.PI * cf * t) + .5 * Math.Sin(2 * Math.PI * cf * 1.004 * t));
                padLp += (pad - padLp) * .05f;
                float padOut = padLp * .05f * duck;
                // arpegio agudo en la segunda mitad
                float lead = 0;
                if (bar >= 8)
                {
                    float af = arp[s16 % arp.Length] * (chord == 2 ? .794f : chord == 3 ? .891f : 1);
                    lead = (float)Math.Sign(Math.Sin(2 * Math.PI * af * t)) * (float)Math.Exp(-inStep * 18) * .06f * duck;
                }
                b[i] = kick * .55f + clap + hat + bass + padOut + lead;
            }
            float peak = 1e-3f; foreach (var v in b) peak = Math.Max(peak, Math.Abs(v));
            for (int i = 0; i < n; i++) b[i] = b[i] / peak * .6f;
            return b;
        }

        float[] Buf(float sec) => new float[(int)(sec * Rate)];
        float N() => (float)(rng.NextDouble() * 2 - 1);

        /// <summary>Golpe: tono grave que cae y ruido corto filtrado.</summary>
        float[] Hit(float sec, float f0, float cut, float vol)
        {
            var b = Buf(sec); double ph = 0; float lp = 0, k = cut / Rate * 6.28f / (1 + cut / Rate * 6.28f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate, env = (float)Math.Exp(-t * 28);
                ph += 2 * Math.PI * f0 * (1 + .2f * (float)Math.Exp(-t * 60)) / Rate;
                lp += (N() - lp) * k;
                b[i] = vol * ((float)Math.Sin(ph) * env * 1.1f + lp * (float)Math.Exp(-t * 55) * 1.6f);
            }
            return b;
        }

        float[] Metal(float sec, float f1, float f2, float vol)
        {
            var b = Buf(sec);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate, env = (float)Math.Exp(-t * 30);
                b[i] = vol * env * (float)(Math.Sin(2 * Math.PI * f1 * t) + .7 * Math.Sin(2 * Math.PI * f2 * t) + .4 * Math.Sin(2 * Math.PI * 4400 * t)) * .5f + N() * .2f * (float)Math.Exp(-t * 120);
            }
            return b;
        }

        /// <summary>Barrido de aire: ruido con filtro que sube y baja.</summary>
        float[] Whoosh(float sec, float center, float vol)
        {
            var b = Buf(sec); float lp = 0, hp = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float u = i / (float)b.Length, env = (float)Math.Sin(Math.PI * u);
                float f = center * (.5f + u), k = f / Rate * 6.28f; k = k / (1 + k);
                lp += (N() - lp) * k; hp += (lp - hp) * .02f;
                b[i] = vol * (lp - hp) * env * 2.2f;
            }
            return b;
        }

        float[] Noise(float sec, float cut, float vol)
        {
            var b = Buf(sec); float lp = 0, k = cut / Rate * 6.28f; k = k / (1 + k);
            for (int i = 0; i < b.Length; i++) { float t = i / (float)Rate; lp += (N() - lp) * k; b[i] = vol * lp * 2 * (float)Math.Exp(-t * 14); }
            return b;
        }

        /// <summary>Fuego: rugido grave con chasquidos.</summary>
        float[] Fire(float sec)
        {
            var b = Buf(sec); float lp = 0, lp2 = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float u = i / (float)b.Length, env = (float)(Math.Min(1, u * 8) * Math.Pow(1 - u, 1.5));
                lp += (N() - lp) * .08f; lp2 += (lp - lp2) * .2f;
                float crack = rng.NextDouble() < .004 ? N() * 2 : 0;
                b[i] = (lp2 * 3.2f + crack) * env * .8f;
            }
            return b;
        }

        /// <summary>Hielo: chasquido agudo con brillo cristalino.</summary>
        float[] Ice(float sec)
        {
            var b = Buf(sec); float prev = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate, n = N(), hp = n - prev; prev = n;
                b[i] = hp * .5f * (float)Math.Exp(-t * 18) + .35f * (float)(Math.Sin(2 * Math.PI * 3100 * t) * Math.Sin(2 * Math.PI * 7 * t)) * (float)Math.Exp(-t * 9);
            }
            return b;
        }

        float[] Shatter(float sec)
        {
            var b = Buf(sec);
            for (int c = 0; c < 26; c++)
            {
                int at = (int)(Math.Pow(rng.NextDouble(), 2) * (b.Length - 2000)); float f = 2500 + (float)rng.NextDouble() * 4000, v = .3f + (float)rng.NextDouble() * .4f;
                for (int i = 0; i < 1800 && at + i < b.Length; i++) { float t = i / (float)Rate; b[at + i] += v * (float)Math.Sin(2 * Math.PI * f * t) * (float)Math.Exp(-t * 60) + N() * .15f * (float)Math.Exp(-t * 200); }
            }
            return b;
        }

        float[] Chain(float sec)
        {
            var b = Buf(sec);
            for (int c = 0; c < 14; c++)
            {
                int at = (int)(c / 14f * (b.Length - 1500)); float f = 1500 + (float)rng.NextDouble() * 1500;
                for (int i = 0; i < 1400 && at + i < b.Length; i++) { float t = i / (float)Rate; b[at + i] += .35f * (float)Math.Sin(2 * Math.PI * f * t) * (float)Math.Exp(-t * 80); }
            }
            return b;
        }

        /// <summary>Sonido humedo: ruido grave que tiembla.</summary>
        float[] Wet(float sec)
        {
            var b = Buf(sec); float lp = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate; lp += (N() - lp) * .05f;
                b[i] = lp * 3.5f * (float)Math.Exp(-t * 9) * (float)(.6 + .4 * Math.Sin(2 * Math.PI * 23 * t));
            }
            return b;
        }

        static float[] Mix(float[] a, float[] b)
        {
            var r = new float[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < r.Length; i++) r[i] = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
            return r;
        }

        // ---------- waveOut ----------
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
    }
}
