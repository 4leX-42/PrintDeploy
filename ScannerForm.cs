using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Escaner de red: comprueba el directorio de oficinas y busca impresoras en un rango de IPs.
    /// La oficina donde esta el equipo va primero y abierta; el resto, plegadas al final.
    /// Cada fila se despliega con su detalle. Instalar = directo, con el modelo ya detectado.
    /// Al cerrar se oculta: los resultados se conservan mientras la app siga abierta.
    /// </summary>
    sealed class ScannerForm : Form
    {
        const int Parallel = 128;

        readonly Action<ScanEntry> onInstall;
        readonly ScanList list = new ScanList { Dock = DockStyle.Fill };
        readonly BufferedPanel head = new BufferedPanel(), toolbar = new BufferedPanel(), bar = new BufferedPanel();
        readonly ThinProgress progress = new ThinProgress { Dock = DockStyle.Top, BarColor = Theme.GoldDeep, Visible = false };
        readonly LineField fRange = new LineField("Rango", mono: true);
        readonly FlatBtn btnScan = new FlatBtn("Scan", primary: true) { Eye = true, SweepOnHover = true };
        readonly FlatBtn btnDir = new FlatBtn("Recheck offices", primary: false);
        readonly FlatBtn btnInstall = new FlatBtn("Instalar", primary: true);
        readonly FlatBtn btnRetry = new FlatBtn("Reintentar", primary: false);
        readonly FlatBtn btnWeb = new FlatBtn("Web", primary: false);
        readonly FlatBtn btnCopy = new FlatBtn("Copiar", primary: false);
        readonly System.Windows.Forms.Timer repaint = new System.Windows.Forms.Timer { Interval = 50 };
        readonly Font fTag = Theme.Strong(8.25f), fSub = Theme.Sans(8.75f), fTitle = Theme.Strong(9.75f), fBody = Theme.Sans(9f), fFoot = Theme.Sans(8.25f);
        readonly string here, hereWhy;
        CancellationTokenSource rangeCts;
        bool checkingDirectory, dirty;
        string dirStatus = "", rangeStatus = "", note = "";
        string Status => string.Join("   ·   ", new[] { rangeStatus, dirStatus, note }.Where(x => x.Length > 0));
        int S(int px) => Theme.S(px);

        public ScannerForm(List<OfficePrinter> directory, Action<ScanEntry> onInstall)
        {
            this.onInstall = onInstall;
            // SNMP, IPP y winspool son llamadas bloqueantes: hilos de sobra para que no hagan cola
            ThreadPool.GetMinThreads(out var workers, out var io);
            ThreadPool.SetMinThreads(Math.Max(workers, 64), Math.Max(io, 64));
            Text = "PrintDeploy · Scanner";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Paper;
            Font = Theme.Sans(9.5f);
            Padding = new Padding(1);
            // grande, pero sin salirse de pantallas pequeñas (portatiles)
            var area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(S(960), area.Width * 92 / 100), Math.Min(S(840), area.Height * 92 / 100));
            KeyPreview = true;
            // Esc = ocultar · F5 = escanear el rango otra vez
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) Hide();
                else if (e.KeyCode == Keys.F5 && rangeCts == null) { e.Handled = true; ScanRange(); }
            };
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            // al mostrarse (tambien al volver de instalar): que impresoras tiene ya este equipo -> ojo azul
            VisibleChanged += (s, e) => { if (Visible) RefreshInstalled(); };

            // donde esta este equipo: su oficina arriba y abierta, las demas plegadas al final
            // con tope de espera: fuera de la red de la empresa, la consulta del sitio de AD puede tardar en fallar
            var detect = Task.Run(() => NetworkScan.DetectOffice(directory));
            var loc = detect.Wait(1200) ? detect.Result : null;
            here = loc?.office;
            hereWhy = loc?.why;
            list.Here = here;
            foreach (var p in directory)
                list.Items.Add(new ScanEntry { Ip = p.Ip, Group = p.Office, Directory = p });

            BuildHead();
            BuildToolbar();
            BuildBar();
            list.SelectionChanged += e => UpdateBar();
            list.Activated += Install;
            list.EmptyText = "Sin oficinas.txt. Scan: buscar en un rango.";

            Controls.Add(list);
            Controls.Add(bar);
            Controls.Add(progress);
            Controls.Add(toolbar);
            Controls.Add(head);

            // repintado agrupado: un escaneo de 254 IPs no repinta 254 veces
            repaint.Tick += (s, e) => { if (!dirty) return; dirty = false; list.Invalidate(); UpdateBar(); };
            repaint.Start();
            UpdateBar();

            bool first = true;
            Shown += (s, e) =>
            {
                if (!first) return;
                first = false;
                // primera apertura: directorio y red local a la vez
                CheckDirectory();
                if (fRange.Box.Text.Length > 0) ScanRange();
            };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // sombra
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Black)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        void BuildHead()
        {
            head.Dock = DockStyle.Top;
            head.Height = S(58);
            head.BackColor = Theme.Black;
            head.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            head.Paint += (s, e) =>
            {
                var g = Theme.Crisp(e.Graphics);
                int x = S(24);
                Theme.DrawMark(g, "Scanner", fTag, new Point(x, S(13)), S);
                var where = here != null ? $"{here}   ·   {hereWhy}" : "Ubicación no reconocida";
                TextRenderer.DrawText(g, where, fSub, new Point(x + S(16), S(33)), Theme.HeadSub, TextFormatFlags.NoPadding);
                using (var b = new SolidBrush(Color.FromArgb(56, 56, 58))) g.FillRectangle(b, 0, head.Height - 1, head.Width, 1);
            };
            var close = new IconBtn("✕", null, 10f, icon: false) { Size = new Size(S(46), S(34)), HoverBack = Theme.Red };
            close.Click += (s, e) => Hide();
            var panel = new IconBtn(Theme.GlyphPrinter, "Dispositivos e impresoras") { Size = new Size(S(40), S(34)) };
            panel.Click += (s, e) => Theme.OpenPrintersPanel();
            head.Controls.Add(close);
            head.Controls.Add(panel);
            var open = new IconBtn(PrinterMenu.Glyph, "Abrir una impresora instalada") { Size = new Size(S(40), S(34)) };
            open.Click += (s, e) => PrinterMenu.Show(open);
            head.Controls.Add(open);
            head.Resize += (s, e) => { close.Location = new Point(head.Width - close.Width, 0); panel.Location = new Point(close.Left - panel.Width, 0); open.Location = new Point(panel.Left - open.Width, 0); };
        }

        void BuildToolbar()
        {
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = S(72);
            toolbar.BackColor = Theme.Paper;
            fRange.Box.Text = NetworkScan.LocalRange();
            fRange.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (rangeCts == null) ScanRange(); } };
            btnScan.Click += (s, e) => ScanRange();
            btnDir.Click += (s, e) => CheckDirectory();
            toolbar.Controls.Add(fRange);
            toolbar.Controls.Add(btnScan);
            toolbar.Controls.Add(btnDir);
            toolbar.Layout += (s, e) =>
            {
                int pad = S(24), y = S(12);
                fRange.SetBounds(pad, y, S(220), S(52));
                btnScan.SetBounds(fRange.Right + S(16), y + S(14), S(120), S(34));
                btnDir.SetBounds(toolbar.Width - pad - S(200), y + S(14), S(200), S(34));
            };
        }

        void BuildBar()
        {
            bar.Dock = DockStyle.Bottom;
            bar.Height = S(96);
            bar.BackColor = Theme.Paper;
            bar.Paint += PaintBar;
            Eyes.Attach(bar);
            btnInstall.Click += (s, e) => { if (list.Selected != null) Install(list.Selected); };
            btnRetry.Click += async (s, e) => { var sel = list.Selected; if (sel != null) await CheckOne(sel, quick: false); };
            btnWeb.Click += (s, e) => { var sel = list.Selected; if (sel != null) try { Process.Start("http://" + sel.Ip); } catch { } };
            btnCopy.Click += (s, e) => { var sel = list.Selected; if (sel != null) try { Clipboard.SetText(sel.Ip); note = "copiado " + sel.Ip; bar.Invalidate(); } catch { } };
            var buttons = new[] { (btnInstall, 136), (btnRetry, 112), (btnWeb, 72), (btnCopy, 88) };
            foreach (var (b, _) in buttons) detailButtons.Add(b);
            foreach (var (b, _) in buttons) bar.Controls.Add(b);
            bar.Layout += (s, e) =>
            {
                int x = bar.Width - S(24);
                foreach (var (b, w) in buttons) { x -= S(w); b.SetBounds(x, S(16), S(w), S(34)); x -= S(8); }
            };
        }

        readonly List<FlatBtn> detailButtons = new List<FlatBtn>();

        void PaintBar(object sender, PaintEventArgs e)
        {
            var g = Theme.Crisp(e.Graphics);
            int pad = S(24), w = bar.Width;
            using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, 0, 0, w, 1);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            int textW = detailButtons.Min(b => b.Left) - pad - S(16);
            var sel = list.Selected;
            var row = new Rectangle(pad, S(16), textW, S(34));
            if (sel == null)
                TextRenderer.DrawText(g, "Sin selección.", fBody, row, Theme.Muted, flags);
            else
                TextRenderer.DrawText(g, sel.Directory != null ? $"{sel.Directory.Office} · {sel.Label}" : sel.Label, fTitle, row, Theme.Black, flags);

            // pie: recuento + estado
            int ok = list.Items.Count(x => x.Detected), bad = list.Items.Count(x => x.Failed), mine = list.Items.Count(x => x.Detected && x.Installed);
            int y = bar.Height - S(30);
            using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, pad, y - S(6), w - 2 * pad, 1);
            int fx = pad;
            foreach (var (st, text, inst) in new[] { (ScanState.Printer, $"{ok} online", false), (ScanState.Printer, $"{mine} instaladas", true), (ScanState.Unreachable, $"{bad} offline", false) })
            {
                DrawState(g, st, new Rectangle(fx, y + S(4), S(12), S(12)), text, inst);
                fx += S(22);
                TextRenderer.DrawText(g, text, fFoot, new Rectangle(fx, y, S(90), S(20)), Theme.Black, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                fx += TextRenderer.MeasureText(text, fFoot).Width + S(18);
            }
            TextRenderer.DrawText(g, Status, fFoot, new Rectangle(fx + S(4), y, w - pad - fx - S(4), S(20)), Theme.Muted, flags);
        }

        /// <summary>Que hara INSTALAR o, si no se detecta, por que.</summary>
        internal static string ActionText(ScanEntry e)
        {
            var r = e.Result;
            switch (e.State)
            {
                case ScanState.Pending: return "Pendiente";
                case ScanState.Checking: return "Comprobando…";
                case ScanState.Printer:
                    var extra = r.Hint.Length > 0 ? "  ·  " + r.Hint : "";
                    if (e.IsShared) return (e.Installed ? "Conectada en este equipo · reconectar: " : "Instalar: ") + "Add-Printer -ConnectionName (driver del servidor)" + extra;
                    if (e.Installed) return $"Instalada como «{e.InstalledAs}» · reinstalar: {DriverCatalog.Resolve(e.Brand, r.Description).Text}";
                    return $"Instalar: {DriverCatalog.Resolve(e.Brand, r.Description).Text}  →  «{e.QueueName}»";
                default: return r?.Hint ?? "";
            }
        }

        /// <summary>Texto corto de la columna modelo / estado.</summary>
        internal static string StateText(ScanEntry e)
        {
            if (e.State == ScanState.Checking) return "comprobando…";
            if (e.State == ScanState.Pending) return "";
            var text = e.Result.Model.Length > 0 ? e.Result.Model : "impresora";
            if (e.Installed && e.Detected) return "Instalada · " + text;
            // offline: la marca del directorio, para saber que es sin desplegar
            return e.Failed && e.Directory != null && e.Directory.Brand.Length > 0 ? $"{e.Directory.Brand} · {text}" : text;
        }

        /// <summary>Columna de direccion: la IP, o solo el servidor si es una cola compartida (la cola ya va en el nombre).</summary>
        internal static string Address(ScanEntry e) => e.IsShared ? @"\\" + e.Ip.TrimStart('\\').Split('\\')[0] : e.Ip;

        Dictionary<string, string> installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string InstalledName(string ip) => installed.TryGetValue(ip, out var q) ? q : null;

        /// <summary>Relee las impresoras instaladas en el equipo y marca las entradas (por IP o por \servidor\cola).</summary>
        async void RefreshInstalled()
        {
            installed = await Task.Run(() => MainForm.InstalledMap());
            foreach (var e in list.Items) e.InstalledAs = InstalledName(e.Ip);
            list.Invalidate();
            bar.Invalidate();
            UpdateBar();
        }

        /// <summary>Indicador: ojo con iris verde (online), rojo (offline / no identificada), dorado entornado (comprobando) o cerrado.</summary>
        internal static void DrawState(Graphics g, ScanState st, Rectangle r, object id = null, bool installed = false)
        {
            Eyes.Draw(g, st, r, id, installed);
        }

        void UpdateBar()
        {
            var sel = list.Selected;
            btnInstall.Enabled = sel != null && (sel.CanInstall || sel.NeedsLogin);
            var label = sel?.NeedsLogin == true ? "Credenciales" : sel?.Installed == true ? "Reinstalar" : "Instalar";
            if (btnInstall.Text != label) { btnInstall.Text = label; btnInstall.Invalidate(); }
            btnRetry.Enabled = sel != null && sel.State != ScanState.Checking;
            btnWeb.Enabled = sel?.Result != null && sel.Result.Ports.Contains(80);
            btnCopy.Enabled = sel != null;
            bar.Invalidate();
        }

        void Redraw() => dirty = true;

        /// <summary>Instalar; si es una cola de servidor que no acepta este usuario, antes pide credenciales.</summary>
        async void Install(ScanEntry e)
        {
            if (e.NeedsLogin && !await Login(e)) return;
            if (e.CanInstall) onInstall(e);
        }

        /// <summary>
        /// Entra en el servidor con un usuario con permiso y vuelve a comprobar sus colas.
        /// La credencial queda en el Administrador de credenciales de Windows.
        /// </summary>
        async Task<bool> Login(ScanEntry e)
        {
            var host = e.Host;
            var text = $"{host} rechaza el usuario actual (error 5 / 1326). Usuario con permiso en el servidor: DOMINIO\\usuario.\n\n" +
                       "Sesión IPC$ + credencial en el Administrador de credenciales de Windows.";
            bool ok = ThemedDialog.Credentials(this, host, text, async (user, pass) =>
            {
                int err = await Task.Run(() => NetworkScan.Login(host, user, pass));
                return err == 0 ? null : NetworkScan.LoginError(err);
            });
            if (!ok) return false;
            note = "credenciales guardadas · " + host;
            // todas las colas de ese servidor, no solo la elegida
            await Task.WhenAll(list.Items.Where(x => x.IsShared && x.Host.Equals(host, StringComparison.OrdinalIgnoreCase)).Select(x => CheckOne(x, quick: false)));
            if (e.Detected) return true;
            ThemedDialog.Info(this, "Servidor", e.Label, $"Sesión con {host} abierta; la cola no abre.\n\n{e.Result?.Hint}");
            return false;
        }

        async Task CheckOne(ScanEntry e, bool quick)
        {
            e.State = ScanState.Checking;
            Redraw();
            try
            {
                e.Result = e.IsShared ? await NetworkScan.CheckShared(e.Ip) : await NetworkScan.Check(e.Ip, quick, CancellationToken.None);
                e.State = e.Result.State;
            }
            catch { e.Result = new ScanResult { State = ScanState.Unreachable, Model = "error", Hint = "Error al comprobar." }; e.State = ScanState.Unreachable; }
            Redraw();
        }

        async void CheckDirectory()
        {
            if (checkingDirectory) return;
            var entries = list.Items.Where(x => x.Directory != null).ToList();
            if (entries.Count == 0) return;
            checkingDirectory = true;
            btnDir.Scanning = true;
            dirStatus = "directorio…";
            // la oficina de este equipo primero: sus resultados llegan antes
            await Task.WhenAll(entries.OrderBy(x => x.Group == here ? 0 : 1).Select(x => CheckOne(x, quick: false)));
            btnDir.Scanning = false;
            checkingDirectory = false;
            dirStatus = $"directorio {entries.Count(x => x.Detected)}/{entries.Count}";
            Redraw();
        }

        async void ScanRange()
        {
            if (rangeCts != null) { rangeCts.Cancel(); return; }
            List<string> ips;
            try { ips = NetworkScan.ParseRange(fRange.Box.Text); }
            catch (FormatException ex) { ThemedDialog.Info(this, "Scanner", "Rango no válido", ex.Message + "\n\nEjemplos: 192.168.4.1-254 · 192.168.4.0/24 · 192.168.4.10-192.168.4.80"); return; }

            string group = ScanList.RangePrefix + fRange.Box.Text.Trim();
            list.Items.RemoveAll(x => x.Group == group);
            if (list.Selected != null && !list.Items.Contains(list.Selected)) list.Select(null);
            rangeCts = new CancellationTokenSource();
            var ct = rangeCts.Token;
            btnScan.Text = "Stop";
            btnScan.Scanning = true;
            btnScan.Invalidate();
            progress.Value = 0;
            progress.Visible = true;
            int done = 0, alive = 0, found = 0;
            var gate = new SemaphoreSlim(Parallel);
            var sw = Stopwatch.StartNew();
            // las del directorio no se repiten en el rango: se actualiza su fila
            var byIp = list.Items.Where(x => x.Directory != null).GroupBy(x => x.Ip).ToDictionary(x => x.Key, x => x.First());

            async Task one(string ip)
            {
                try
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        var r = await NetworkScan.Check(ip, quick: true, ct);
                        if (r.State != ScanState.Unreachable) alive++;
                        if (r.State == ScanState.Printer)
                        {
                            found++;
                            if (byIp.TryGetValue(ip, out var dir)) { if (dir.State != ScanState.Printer) { dir.Result = r; dir.State = r.State; } }
                            else Insert(new ScanEntry { Ip = ip, Group = group, Result = r, State = r.State, InstalledAs = InstalledName(ip) });
                        }
                    }
                    finally { gate.Release(); }
                }
                catch (OperationCanceledException) { }
                catch { }
                done++;
                progress.Value = done * 100 / ips.Count;
                rangeStatus = $"{done}/{ips.Count} IPs · {alive} hosts · {found} impresoras";
                Redraw();
            }

            await Task.WhenAll(ips.Select(one));
            rangeStatus = (ct.IsCancellationRequested ? "detenido · " : "") + $"{ips.Count} IPs en {sw.Elapsed.TotalSeconds:0.0} s · {alive} hosts · {found} impresoras";
            progress.Visible = false;
            rangeCts = null;
            btnScan.Text = "Scan";
            btnScan.Scanning = false;
            btnScan.Invalidate();
            Redraw();
        }

        /// <summary>Inserta en su sitio: cada rango ordenado por IP.</summary>
        void Insert(ScanEntry e)
        {
            uint ip = NetworkScan.ToUInt(e.Ip);
            int i = list.Items.FindIndex(x => x.Group == e.Group && NetworkScan.ToUInt(x.Ip) > ip);
            if (i < 0)
            {
                int last = list.Items.FindLastIndex(x => x.Group == e.Group);
                i = last < 0 ? list.Items.Count : last + 1;
            }
            list.Items.Insert(i, e);
        }
    }

    /// <summary>
    /// Lista pintada a mano: grupos plegables (oficina / rango) y filas que se despliegan con su detalle.
    /// Orden: la oficina de este equipo, los rangos escaneados y el resto de oficinas (plegadas).
    /// </summary>
    sealed class ScanList : Painted
    {
        public const string RangePrefix = "Red ";
        public readonly List<ScanEntry> Items = new List<ScanEntry>();
        public ScanEntry Selected { get; private set; }
        public event Action<ScanEntry> SelectionChanged, Activated;
        public string EmptyText = "";
        /// <summary>Oficina de este equipo (null = no reconocida: todo abierto).</summary>
        public string Here;
        readonly Dictionary<string, bool> collapsed = new Dictionary<string, bool>();
        ScanEntry hoverItem;
        string hoverGroup;
        int scrollY, contentHeight;
        readonly List<(Rectangle rect, ScanEntry e)> hits = new List<(Rectangle, ScanEntry)>();
        readonly List<(Rectangle rect, string key)> groupHits = new List<(Rectangle, string)>();
        Rectangle detailRect;
        readonly Font fGroup = Theme.Strong(8.25f), fHere = Theme.Strong(7.5f), fRow = Theme.Sans(9.5f), fModel = Theme.Sans(8.75f),
                      fIp = Theme.Mono(8.75f), fData = Theme.Mono(8.25f), fBody = Theme.Sans(8.75f), fKey = Theme.Strong(7.5f), fEmpty = Theme.Sans(9.5f);
        int RowH => S(36);
        int DetailH => S(78);
        int GroupH => S(34);
        int MaxScroll => Math.Max(0, contentHeight - Height);

        public ScanList() { TabStop = true; Eyes.Attach(this, S(60)); }

        static bool IsRange(string key) => key.StartsWith(RangePrefix);
        int Rank(string key) => Here != null && key == Here ? 0 : IsRange(key) ? 1 : 2;
        bool IsCollapsed(string key) => collapsed.TryGetValue(key, out var v) ? v : Here != null && !IsRange(key) && key != Here;

        IEnumerable<IGrouping<string, ScanEntry>> Groups() => Items.GroupBy(i => i.Group).OrderBy(g => Rank(g.Key));
        List<ScanEntry> VisibleItems() => Groups().Where(g => !IsCollapsed(g.Key)).SelectMany(g => g).ToList();

        public void Select(ScanEntry e)
        {
            if (e == Selected) return;
            Selected = e;
            Walk();
            EnsureVisible(e);
            Invalidate();
            SelectionChanged?.Invoke(e);
        }

        /// <summary>Posicion de cada cabecera (e = null) y fila en el contenido, sin scroll.</summary>
        List<(int y, IGrouping<string, ScanEntry> grp, ScanEntry e)> Walk()
        {
            var rows = new List<(int, IGrouping<string, ScanEntry>, ScanEntry)>();
            int y = S(4);
            foreach (var grp in Groups())
            {
                rows.Add((y, grp, null));
                y += GroupH;
                if (!IsCollapsed(grp.Key))
                    foreach (var it in grp) { rows.Add((y, grp, it)); y += RowH + (it == Selected ? DetailH : 0); }
                y += S(6);
            }
            contentHeight = y;
            return rows;
        }

        void EnsureVisible(ScanEntry e)
        {
            if (e == null) return;
            var row = Walk().FirstOrDefault(r => r.e == e);
            if (row.e == null) return;
            int top = row.y, bottom = row.y + RowH + DetailH;
            if (top < scrollY) scrollY = top;
            else if (bottom > scrollY + Height) scrollY = bottom - Height;
            scrollY = Math.Max(0, Math.Min(MaxScroll, scrollY));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Crisp(e.Graphics);
            g.Clear(Theme.Paper);
            paintClip = e.ClipRectangle;
            hits.Clear();
            groupHits.Clear();
            detailRect = Rectangle.Empty;
            int pad = S(24), right = Width - S(8) - pad;
            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, fEmpty, new Rectangle(pad, S(20), Width - 2 * pad, S(40)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                return;
            }
            var rows = Walk();
            scrollY = Math.Max(0, Math.Min(MaxScroll, scrollY));
            foreach (var (y0, grp, it) in rows)
            {
                int y = y0 - scrollY;
                if (it == null)
                {
                    var gr = new Rectangle(0, y, Width - S(8), GroupH);
                    groupHits.Add((gr, grp.Key));
                    if (gr.Bottom >= 0 && gr.Top <= Height && gr.IntersectsWith(paintClip)) PaintGroup(g, grp, gr, pad, right);
                    continue;
                }
                var r = new Rectangle(0, y, Width - S(8), RowH);
                hits.Add((r, it));
                if (it == Selected) detailRect = new Rectangle(0, r.Bottom, r.Width, DetailH);
                var full = it == Selected ? new Rectangle(r.X, r.Y, r.Width, r.Height + DetailH) : r;
                if (r.Bottom >= 0 && r.Top <= Height && full.IntersectsWith(paintClip)) PaintRow(g, it, r, pad, right);
            }

            if (MaxScroll > 0)
            {
                int barH = Math.Max(S(24), Height * Height / (contentHeight + 1));
                int barY = (Height - barH) * scrollY / MaxScroll;
                using (var b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, Width - S(5), 0, S(2), Height);
                using (var b = new SolidBrush(Color.FromArgb(180, 180, 182))) g.FillRectangle(b, Width - S(5), barY, S(2), barH);
            }
        }

        void PaintGroup(Graphics g, IGrouping<string, ScanEntry> grp, Rectangle r, int pad, int right)
        {
            bool closed = IsCollapsed(grp.Key);
            if (grp.Key == hoverGroup) using (var b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, r);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

            // flecha de plegado
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = pad + S(4), cy = r.Y + r.Height / 2, a = S(4);
            var arrow = closed
                ? new[] { new Point(cx - a / 2, cy - a), new Point(cx + a / 2 + 1, cy), new Point(cx - a / 2, cy + a) }
                : new[] { new Point(cx - a, cy - a / 2), new Point(cx + a, cy - a / 2), new Point(cx, cy + a / 2 + 1) };
            using (var b = new SolidBrush(Theme.Muted)) g.FillPolygon(b, arrow);
            g.SmoothingMode = old;

            int x = pad + S(20);
            string title = IsRange(grp.Key) ? "RED" : Theme.Caps(grp.Key);
            TextRenderer.DrawText(g, title, fGroup, new Rectangle(x, r.Y, right - x, r.Height), Theme.Black, flags);
            x += TextRenderer.MeasureText(title, fGroup).Width + S(10);
            if (IsRange(grp.Key))
            {
                var range = grp.Key.Substring(RangePrefix.Length);
                TextRenderer.DrawText(g, range, fData, new Rectangle(x, r.Y, right - x, r.Height), Theme.Muted, flags);
                x += TextRenderer.MeasureText(range, fData).Width + S(10);
            }
            if (grp.Key == Here)
                TextRenderer.DrawText(g, "·  TU UBICACIÓN", fHere, new Rectangle(x, r.Y, right - x, r.Height), Theme.GoldDeep, flags);

            // recuento: online / total, y cuantas fallan si las hay
            int ok = grp.Count(i => i.Detected), bad = grp.Count(i => i.Failed);
            var count = $"{ok}/{grp.Count()}";
            int cw = TextRenderer.MeasureText(count, fGroup).Width;
            TextRenderer.DrawText(g, count, fGroup, new Rectangle(right - cw, r.Y, cw, r.Height), Theme.Muted, flags);
            if (bad > 0 && closed)
            {
                var miss = $"{bad} offline";
                int mw = TextRenderer.MeasureText(miss, fModel).Width;
                TextRenderer.DrawText(g, miss, fModel, new Rectangle(right - cw - S(14) - mw, r.Y, mw, r.Height), Theme.Red, flags);
            }
            using (var b = new SolidBrush(closed ? Color.FromArgb(236, 236, 236) : Theme.Line)) g.FillRectangle(b, pad, r.Bottom - 1, right - pad, 1);
        }

        Rectangle paintClip;

        void PaintRow(Graphics g, ScanEntry it, Rectangle r, int pad, int right)
        {
            bool sel = it == Selected;
            var back = sel ? Color.FromArgb(244, 244, 244) : it == hoverItem ? Theme.Hover : Theme.Paper;
            if (back != Theme.Paper) using (var b = new SolidBrush(back)) g.FillRectangle(b, sel ? new Rectangle(r.X, r.Y, r.Width, r.Height + DetailH) : r);
            if (sel) using (var b = new SolidBrush(Theme.GoldDeep)) g.FillRectangle(b, 0, r.Y, S(2), r.Height + DetailH);

            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            int orb = S(14), nameX = pad + S(28), ipW = S(140);
            int nameW = (int)((right - ipW - nameX) * .5);
            ScannerForm.DrawState(g, it.State, new Rectangle(pad + S(1), r.Y + (r.Height - orb) / 2, orb, orb), it, it.Installed);
            if (paintClip.Right <= nameX) return;   // repintado solo de la columna de ojos (llamaradas)

            var label = it.Label;
            TextRenderer.DrawText(g, label, fRow, new Rectangle(nameX, r.Y, nameW - S(12), r.Height), label == "Sin nombre" ? Theme.Muted : Theme.Black, flags);
            var state = ScannerForm.StateText(it);
            if (state == label) state = "";
            TextRenderer.DrawText(g, state, fModel, new Rectangle(nameX + nameW, r.Y, right - ipW - nameX - nameW - S(8), r.Height), it.Failed ? Theme.Red : Theme.Muted, flags);
            TextRenderer.DrawText(g, ScannerForm.Address(it), fIp, new Rectangle(right - ipW, r.Y, ipW, r.Height), it.Detected ? Theme.Black : Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (sel) PaintDetail(g, it, new Rectangle(nameX, r.Bottom, right - nameX, DetailH));
            int bottom = sel ? r.Bottom + DetailH : r.Bottom;
            using (var p = new Pen(Color.FromArgb(238, 238, 238))) g.DrawLine(p, pad, bottom - 1, right, bottom - 1);
        }

        /// <summary>Detalle desplegado: datos de red, lo que dice el equipo de si mismo y que hara Instalar (o por que no).</summary>
        void PaintDetail(Graphics g, ScanEntry it, Rectangle d)
        {
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            var r = it.Result;
            int y = d.Y + S(2);

            var data = it.Ip + (r?.PingMs != null ? $"   ·   {r.PingMs} ms" : "") + (r != null && r.Ports.Count > 0 ? "   ·   tcp " + string.Join(" ", r.Ports) : "");
            TextRenderer.DrawText(g, data, fData, new Rectangle(d.X, y, d.Width, S(20)), Theme.Muted, flags);
            y += S(22);

            var key = it.IsShared ? "COLA" : "SNMP";
            int kw = TextRenderer.MeasureText(key, fKey).Width + S(10);
            var id = r == null ? "" : string.Join("  ·  ", new[] { r.Name, r.Location }.Where(x => x.Length > 0));
            TextRenderer.DrawText(g, key, fKey, new Rectangle(d.X, y, kw, S(20)), Theme.GoldDeep, flags);
            TextRenderer.DrawText(g, id.Length > 0 ? id : "sin nombre ni ubicación", fBody, new Rectangle(d.X + kw, y, d.Width - kw, S(20)), Theme.Muted, flags);
            y += S(22);

            TextRenderer.DrawText(g, ScannerForm.ActionText(it), fBody, new Rectangle(d.X, y, d.Width, S(20)), it.Failed ? Theme.Red : it.Detected ? Theme.Black : Theme.Muted, flags);
        }

        ScanEntry HitTest(Point p) => detailRect.Contains(p) ? null : hits.FirstOrDefault(h => h.rect.Contains(p)).e;
        string GroupAt(Point p) => groupHits.FirstOrDefault(h => h.rect.Contains(p)).key;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int next = Math.Max(0, Math.Min(MaxScroll, scrollY - Math.Sign(e.Delta) * RowH * 3));
            if (next != scrollY) { scrollY = next; Invalidate(); }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = HitTest(e.Location);
            var grp = h == null ? GroupAt(e.Location) : null;
            if (h != hoverItem || grp != hoverGroup)
            {
                hoverItem = h;
                hoverGroup = grp;
                Cursor = h != null || grp != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverItem = null; hoverGroup = null; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                var h = HitTest(e.Location);
                var grp = h == null && !detailRect.Contains(e.Location) ? GroupAt(e.Location) : null;
                if (h != null) Select(h == Selected ? null : h);   // segundo clic: plegar
                else if (grp != null) Toggle(grp);
            }
            base.OnMouseDown(e);
        }

        void Toggle(string key)
        {
            collapsed[key] = !IsCollapsed(key);
            if (collapsed[key] && Selected?.Group == key) Select(null);
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            var h = HitTest(e.Location);
            if (h != null) { Select(h); Activated?.Invoke(h); }
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys k) => k == Keys.Up || k == Keys.Down || base.IsInputKey(k);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var order = VisibleItems();
            if (order.Count == 0) return;
            int i = Selected == null ? -1 : order.IndexOf(Selected);
            if (e.KeyCode == Keys.Down) Select(order[Math.Min(order.Count - 1, i + 1)]);
            else if (e.KeyCode == Keys.Up) Select(order[Math.Max(0, i - 1)]);
            else if (e.KeyCode == Keys.Enter && Selected != null) Activated?.Invoke(Selected);
            base.OnKeyDown(e);
        }
    }
}
