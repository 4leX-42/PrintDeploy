using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    public sealed partial class MainForm : Form
    {
        static readonly string[] VirtualPrinters = { "PDF", "OneNote", "Fax", "XPS", "AnyDesk", "PDFelement", "Print to" };

        readonly Panel header = new Panel();
        readonly Panel body = new Panel();
        readonly Label lblClose = new Label(), lblMin = new Label();
        string hTag = "", hTitle = "", hSub = "";
        bool closeAllowed = true;
        readonly string logPath;
        readonly string workDir;
        readonly string version = typeof(MainForm).Assembly.GetName().Version.ToString(3);

        // pagina progreso
        Label lblStep, lblPct;
        ThinProgress progress;
        RichTextBox logBox;
        FlatBtn btnClose;
        LinkBtn lnkQueue, lnkTest;     // tras instalar: Impresoras y escaneres y pagina de prueba
        string installedPrinter;       // impresora que acaba de instalarse (para checkRow)
        readonly List<string> summary = new List<string>();

        int S(int px) => Theme.S(px);

        public MainForm()
        {
            Text = "PrintDeploy";
            Icon = AppIcon();
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Sans(9.5f);
            BackColor = Theme.Paper;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(780), S(660));
            Padding = new Padding(1);
            KeyPreview = true;
            // Esc: en el resumen de una instalacion vuelve al inicio; en el inicio, sale
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && closeAllowed) { if (onProgress) Back(); else Close(); } };
            // inicio: 1 escaner · 2 instalar por IP · 3 colas instaladas, sin raton
            KeyDown += (s, e) =>
            {
                if (!onChooser || !closeAllowed) return;
                if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) ShowScanner();
                else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) { returnToScanner = false; ShowInstall(); }
                else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) { returnToScanner = false; ShowQueues(); }
            };

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrintDeploy", "Logs");
            Directory.CreateDirectory(logDir);
            logPath = Path.Combine(logDir, $"instalacion_{stamp}.log");
            // carpeta de trabajo (motor y drivers que se ejecutan elevados): fuera del %TEMP% del usuario, ver SecureWorkDir
            workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrintDeploy", "Work", Guid.NewGuid().ToString("N"));

            BuildHeader();
            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Paper;
            body.Padding = new Padding(S(36), S(28), S(36), S(24));
            Controls.Add(body);
            Controls.Add(header);

            FormClosing += (s, e) =>
            {
                if (!closeAllowed) { e.Cancel = true; return; }
                try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
            };
            ShowChooser();
            // con la ventana ya pintada: precarga en segundo plano del catalogo de drivers y de los ojos del scanner
            Shown += (s, e) => BeginInvoke((Action)(() =>
            {
                DriverCatalog.Warm();
                Eyes.Warm(S(12), S(14), S(16));
            }));
        }

        // ---------- ventana sin bordes: sombra, borde, arrastre ----------
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; cp.Style |= 0x20000; return cp; } // CS_DROPSHADOW + WS_MINIMIZEBOX
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Black)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        void BuildHeader()
        {
            header.Dock = DockStyle.Top;
            header.Height = S(118);
            header.BackColor = Theme.Black;
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(header, true);
            header.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            header.Paint += (s, e) =>
            {
                var g = Theme.Crisp(e.Graphics);
                int x = S(36);
                if (hTag.Length == 0)
                {
                    // inicio: emblema (ojo dorado) + marca en trazo fino; "Deploy" en dorado
                    var flags = TextFormatFlags.NoPadding;
                    int em = S(96);
                    g.DrawImageUnscaled(Logo.Get(em), x - S(14), (header.Height - em) / 2);
                    x += em - S(4);
                    TextRenderer.DrawText(g, "Print", fWordmark, new Point(x - S(2), S(30)), Theme.Paper, flags);
                    int pw = TextRenderer.MeasureText(g, "Print", fWordmark, Size.Empty, flags).Width;
                    TextRenderer.DrawText(g, "Deploy", fWordmark, new Point(x - S(2) + pw, S(30)), Theme.Gold, flags);
                    TextRenderer.DrawText(g, hSub, fHeadSub, new Point(x, S(80)), Theme.HeadSub, flags);
                }
                else
                {
                    Theme.DrawMark(g, hTag, fMark, new Point(x, S(20)), S);
                    TextRenderer.DrawText(g, hTitle, fHeadTitle, new Point(x - S(2), S(42)), Theme.Paper, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, hSub, fHeadSub, new Point(x, S(82)), Theme.HeadSub, TextFormatFlags.NoPadding);
                }
                using (var b = new SolidBrush(Color.FromArgb(56, 56, 58))) g.FillRectangle(b, 0, header.Height - 1, header.Width, 1);
            };

            foreach (var (lbl, text, act) in new[] { (lblClose, "✕", (Action)(() => Close())), (lblMin, "—", (Action)(() => WindowState = FormWindowState.Minimized)) })
            {
                lbl.Text = text;
                lbl.Font = Theme.Sans(10f);
                lbl.ForeColor = Color.FromArgb(222, 222, 224);
                lbl.BackColor = Theme.Black;
                lbl.TextAlign = ContentAlignment.MiddleCenter;
                lbl.Size = new Size(S(46), S(34));
                lbl.Cursor = Cursors.Hand;
                lbl.MouseEnter += (s, e) => { lbl.BackColor = lbl == lblClose ? Theme.Red : Color.FromArgb(56, 56, 58); lbl.ForeColor = Theme.Paper; };
                lbl.MouseLeave += (s, e) => { lbl.BackColor = Theme.Black; lbl.ForeColor = Color.FromArgb(222, 222, 224); };
                lbl.Click += (s, e) => act();
                header.Controls.Add(lbl);
            }
            // acceso directo a "Dispositivos e impresoras"
            var lblPanel = new IconBtn(Theme.GlyphPrinter, "Dispositivos e impresoras") { Size = new Size(S(40), S(34)) };
            lblPanel.Click += (s, e) => Theme.OpenPrintersPanel();
            header.Controls.Add(lblPanel);
            // acceso directo a una impresora instalada (cola / propiedades)
            var lblOpen = new IconBtn(PrinterMenu.Glyph, "Abrir una impresora instalada") { Size = new Size(S(40), S(34)) };
            lblOpen.Click += (s, e) => PrinterMenu.Show(lblOpen);
            header.Controls.Add(lblOpen);
            // escaner de red: directorio de oficinas + busqueda por rango
            var btnDir = new FlatBtn("Scanner", primary: false) { Size = new Size(S(150), S(34)), OnDark = true, TabStop = false, Eye = true, SweepOnHover = true };
            Shown += (s, e) => btnDir.StartSweep();   // una pasada al abrir
            btnDir.Click += (s, e) => ShowScanner();
            header.Controls.Add(btnDir);

            header.Resize += (s, e) =>
            {
                lblClose.Location = new Point(header.Width - lblClose.Width - 1, 1);
                lblMin.Location = new Point(lblClose.Left - lblMin.Width, 1);
                lblPanel.Location = new Point(lblMin.Left - lblPanel.Width, 1);
                lblOpen.Location = new Point(lblPanel.Left - lblOpen.Width, 1);
                btnDir.Location = new Point(header.Width - btnDir.Width - S(36), S(58));
            };
        }

        readonly Font fMark = Theme.Strong(8.25f), fHeadTitle = Theme.Title(20f), fHeadSub = Theme.Sans(9.5f), fWordmark = Theme.Light(24f);

        /// <summary>
        /// Icono de la ventana. ExtractAssociatedIcon falla con rutas de red (\\servidor\carpeta\PrintDeploy.exe):
        /// en ese caso, sin icono propio antes que no arrancar.
        /// </summary>
        static Icon AppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (ArgumentException) { return null; }
        }

        ScannerForm scanner;
        bool returnToScanner, onProgress, onChooser;

        void ShowScanner()
        {
            if (scanner == null)
            {
                List<OfficePrinter> items;
                try { items = PrinterDirectory.Load(); }
                catch (Exception ex) { Warn("Error al leer oficinas.txt (el escaneo de red sigue disponible):\n" + ex.Message); items = new List<OfficePrinter>(); }
                scanner = new ScannerForm(items, InstallFromScan);
                new TentacleLink(this, scanner);   // se mantiene vivo con los eventos de las dos ventanas
            }
            if (!scanner.Visible)
            {
                // a la derecha de la ventana principal, sin taparla; si no cabe, se centra el conjunto en la pantalla
                // (la principal se desplaza a la izquierda) y, en pantallas estrechas, se solapan lo minimo
                var area = Screen.FromControl(this).WorkingArea;
                // hueco para el tentaculo: hasta 150 px, menos si la pantalla no da
                int gap = Math.Max(S(40), Math.Min(S(150), area.Width - Width - scanner.Width - S(24)));
                if (Right + gap + scanner.Width > area.Right && WindowState == FormWindowState.Normal)
                    Left = Math.Max(area.Left, area.Left + (area.Width - (Width + gap + scanner.Width)) / 2);
                int top = Math.Max(area.Top, Math.Min(area.Bottom - scanner.Height, Top - (scanner.Height - Height) / 2));
                scanner.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - scanner.Width, Right + gap)), top);
                scanner.Show(this);
            }
            scanner.Activate();
        }

        /// <summary>
        /// Instalar desde el escaner: IP y modelo ya detectados, sin volver a sondear. Driver segun DriverCatalog;
        /// si ya hay una cola en esa IP se reinstala (conserva su configuracion); si no, cola nueva.
        /// </summary>
        async void InstallFromScan(ScanEntry e)
        {
            if (!closeAllowed) { Warn("Instalación en curso."); return; }
            try { Clipboard.SetText(e.Ip); } catch { }
            returnToScanner = true;
            if (e.Directory?.IsShared == true)
            {
                if (!ThemedDialog.Confirm(scanner, "Servidor", e.Label, $"{e.Result.Model}\n\n· Add-Printer -ConnectionName {e.Ip} (usuario actual)\n· printui /ga (todos los usuarios, al iniciar sesión)\n· Conexión previa: se elimina y se recrea", "Conectar")) return;
                scanner.Hide();
                Present();
                await RunShared(e.Ip);
                return;
            }
            var plan = DriverCatalog.Resolve(e.Brand, e.Result.Description);
            var existing = await Task.Run(() => GetPrinters().FirstOrDefault(p => p.Host == e.Ip));
            var job = existing != null
                ? new InstallJob { Kind = 'E', Name = existing.Name, Ip = e.Ip, Plan = plan }
                : new InstallJob { Kind = 'N', Name = e.QueueName, Ip = e.Ip, Plan = plan };
            var action = existing != null ? $"Reinstalar «{existing.Name}» (puerto {e.Ip})" : $"Nueva cola «{e.QueueName}» · {e.Ip}";
            if (!ThemedDialog.Confirm(scanner, plan.Brand.Length > 0 ? plan.Brand : "Impresora", e.Label, $"{e.Result.Model}\n\n· {action}\n· Driver: {plan.Text}")) return;
            scanner.Hide();
            Present();
            await RunJobs(new List<InstallJob> { job }, plan.Brand);
        }

        Timer presentAnim;

        /// <summary>
        /// Trae la ventana al centro de la pantalla y al frente con un deslizamiento suave (salida del escaner al instalar).
        /// Queda por encima de todo mientras se mueve; al llegar se activa y deja de estar fija encima.
        /// </summary>
        void Present()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            var area = Screen.FromControl(this).WorkingArea;
            var from = Location;
            var to = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            presentAnim?.Stop();
            TopMost = true;
            Activate();
            var clock = Stopwatch.StartNew();
            const double ms = 380;
            presentAnim = new Timer { Interval = 15 };
            presentAnim.Tick += (s, e) =>
            {
                double t = Math.Min(1, clock.ElapsedMilliseconds / ms), k = 1 - Math.Pow(1 - t, 3);   // sale rapido y frena
                Location = new Point((int)(from.X + (to.X - from.X) * k), (int)(from.Y + (to.Y - from.Y) * k));
                Opacity = .82 + .18 * k;
                if (t < 1) return;
                presentAnim.Stop();
                presentAnim.Dispose();
                presentAnim = null;
                Opacity = 1;
                TopMost = false;
                Activate();
                BringToFront();
            };
            Opacity = .82;
            presentAnim.Start();
        }

        int page;   // cambia con cada pantalla: lo que termine de cargar tarde para otra pantalla se descarta

        /// <summary>
        /// Vacia el cuerpo y libera los controles de la pantalla anterior (ventanas, temporizadores, suscripciones).
        /// Se liberan despues del evento en curso: el boton que se ha pulsado puede ser uno de ellos.
        /// </summary>
        void ClearBody()
        {
            page++;
            var old = body.Controls.Cast<Control>().ToArray();
            body.Controls.Clear();
            if (old.Length > 0) BeginInvoke((Action)(() => { foreach (var c in old) c.Dispose(); }));
        }

        void SetHeader(string tag, string title, string sub)
        {
            hTag = tag; hTitle = title; hSub = sub;
            header.Invalidate();
        }

        // Ritmo vertical unico para todas las pantallas:
        //   titulo de seccion (texto + filete) -> SectionGap -> contenido -> BlockGap -> siguiente seccion
        const int SectionGap = 12, BlockGap = 28;

        /// <summary>Titulo de seccion: etiqueta en mayusculas con un filete debajo y separacion fija con el contenido.</summary>
        Panel Section(string text)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = S(26 + SectionGap), BackColor = Theme.Paper };
            var font = Theme.Strong(8.25f);
            p.Paint += (s, e) =>
            {
                TextRenderer.DrawText(Theme.Crisp(e.Graphics), Theme.Caps(text), font, new Point(0, 0), Theme.Muted, TextFormatFlags.NoPadding);
                using (var b = new SolidBrush(Theme.Line)) e.Graphics.FillRectangle(b, 0, S(24), p.Width, 1);
                using (var b = new SolidBrush(Theme.GoldDeep)) e.Graphics.FillRectangle(b, 0, S(23), S(24), S(2));
            };
            return p;
        }

        Panel Gap(int h) => new Panel { Dock = DockStyle.Top, Height = S(h), BackColor = Theme.Paper };

        Panel ButtonBar(params FlatBtn[] buttons)
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = S(48), BackColor = Theme.Paper };
            int x = 0;
            foreach (var b in buttons)
            {
                b.Size = new Size(S(160), S(42));
                b.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                bar.Controls.Add(b);
            }
            bar.Layout += (s, e) =>
            {
                x = bar.Width;
                foreach (var b in buttons) { x -= b.Width; b.Location = new Point(x, bar.Height - b.Height); x -= S(12); }
            };
            return bar;
        }

        Label Footer(string text) => new Label
        {
            Text = text, Font = Theme.Sans(8f), ForeColor = Theme.Muted,
            Dock = DockStyle.Bottom, Height = S(22), TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = true,
        };

        // ================= inicio =================
        void ShowChooser()
        {
            ClearBody();
            AcceptButton = null;
            onProgress = false;
            onChooser = true;
            SetHeader("", "", "Instalación de impresoras de red");

            var scan = new Card("01", "Scanner", "Oficinas y red local: estado de cada impresora e instalación directa.");
            var install = new Card("02", "Instalar por IP", "Cualquier marca. Modelo y driver detectados automáticamente.");
            var queues = new Card("03", "Impresoras instaladas", "Estado, reinstalación y desinstalación.");
            scan.Click += (s, e) => ShowScanner();
            install.Click += (s, e) => { returnToScanner = false; ShowInstall(); };
            queues.Click += (s, e) => { returnToScanner = false; ShowQueues(); };
            // las tres entradas, con filete arriba, centradas en vertical en el espacio libre
            var menu = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Paper };
            var rule = new Panel { Height = 1, BackColor = Theme.Line };
            menu.Controls.Add(rule);
            foreach (var c in new[] { scan, install, queues }) menu.Controls.Add(c);
            menu.Layout += (s, e) =>
            {
                int rowH = Math.Min(S(112), (menu.ClientSize.Height - 1) / 3), y = Math.Max(0, (menu.ClientSize.Height - (3 * rowH + 1)) / 2);
                rule.SetBounds(0, y, menu.ClientSize.Width, 1);
                y += 1;
                foreach (var c in new[] { scan, install, queues }) { c.SetBounds(0, y, menu.ClientSize.Width, rowH); y += rowH; }
            };
            body.Controls.Add(menu);
            body.Controls.Add(Footer($"1  ·  2  ·  3  ·  Esc        v{version}"));
        }

        void Warn(string text) => ThemedDialog.Info(scanner != null && scanner.Visible ? (IWin32Window)scanner : this, "Aviso", "Revisar", text);
        bool Ask(string text) => ThemedDialog.Confirm(this, "Aviso", "¿Continuar?", text, "Continuar");

        // ================= 03: progreso =================
        void ShowProgress(string tag, string title, string sub)
        {
            ClearBody();
            AcceptButton = null;
            SetHeader(tag, title, sub);
            summary.Clear();
            closeAllowed = false;
            onProgress = true;
            onChooser = false;
            lblClose.Visible = false;

            var top = new Panel { Dock = DockStyle.Top, Height = S(34), BackColor = Theme.Paper };
            lblStep = new Label { Dock = DockStyle.Fill, Font = Theme.Title(12.5f), ForeColor = Theme.Black, Text = "Preparando…", TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            lblPct = new Label { Dock = DockStyle.Right, Width = S(70), Font = Theme.Title(12.5f), ForeColor = Theme.GoldDeep, Text = "000%", TextAlign = ContentAlignment.MiddleRight };
            top.Controls.Add(lblStep);
            top.Controls.Add(lblPct);
            progress = new ThinProgress { Dock = DockStyle.Top, Height = S(6) };

            logBox = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Black, ForeColor = Color.FromArgb(228, 228, 230),
                BorderStyle = BorderStyle.None, Font = Theme.Mono(8.75f), DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical,
            };
            var logFrame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Black, Padding = new Padding(S(16), S(12), S(8), S(12)) };
            logFrame.Controls.Add(logBox);

            // al terminar se vuelve al inicio (o al escaner): la app sigue abierta; para salir, la X
            btnClose = new FlatBtn("Volver", primary: true) { Enabled = false };
            var btnLog = new FlatBtn("Ver registro", primary: false);
            btnClose.Click += (s, e) => Back();
            btnLog.Click += (s, e) => { if (File.Exists(logPath)) Process.Start("notepad.exe", logPath); };

            body.Controls.Add(logFrame);
            body.Controls.Add(Section("Registro"));
            body.Controls.Add(Gap(BlockGap));
            body.Controls.Add(progress);
            body.Controls.Add(Gap(SectionGap));
            body.Controls.Add(top);
            var spacer = Gap(BlockGap - 8); spacer.Dock = DockStyle.Bottom;
            body.Controls.Add(spacer);
            var bar = ButtonBar(btnClose, btnLog);
            // comprobacion rapida de la impresora instalada: aparece al terminar bien, a la izquierda de la barra
            installedPrinter = null;
            lnkQueue = new LinkBtn("", "Impresoras y escáneres") { Visible = false };
            lnkTest = new LinkBtn("", "Página de prueba") { Visible = false };
            // Configuracion > Impresoras y escaneres: estado e historial de impresion de Windows 11
            lnkQueue.Click += (s, e) => { try { Process.Start("ms-settings:printers"); } catch { PrintUi("/o", installedPrinter); } };
            lnkTest.Click += (s, e) => SendTestPage();
            bar.Controls.Add(lnkQueue);
            bar.Controls.Add(lnkTest);
            bar.Layout += (s, e) =>
            {
                int y = bar.Height - S(42) + (S(42) - lnkQueue.Height) / 2;
                lnkQueue.Location = new Point(0, y);
                lnkTest.Location = new Point(lnkQueue.Right + S(20), y);
            };
            body.Controls.Add(bar);
        }

        /// <summary>printui sobre una impresora: /o cola de impresion, /k pagina de prueba.</summary>
        static void PrintUi(string arg, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try { Process.Start("rundll32.exe", $"printui.dll,PrintUIEntry {arg} /n \"{name}\""); } catch { }
        }

        /// <summary>Del resumen de una instalacion al inicio; si se lanzo desde el escaner, se reabre.</summary>
        void Back()
        {
            if (!closeAllowed) return;
            ShowChooser();
            if (returnToScanner) { returnToScanner = false; ShowScanner(); }
        }

        void Log(string kind, string text)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Log(kind, text))); return; }
            // sobre fondo oscuro el rojo solo marca (etiqueta); el texto va claro para que se lea
            string tag; Color tagColor, c;
            switch (kind)
            {
                case "OK":   tag = "[ OK ] "; tagColor = Color.FromArgb(184, 184, 188); c = Theme.Paper; break;
                case "WARN": tag = "[ !! ] "; tagColor = Theme.Amber; c = Color.FromArgb(240, 220, 180); break;
                case "ERR":  tag = "[ERR ] "; tagColor = Theme.RedOnDark; c = Color.FromArgb(255, 200, 205); break;
                case "STEP": tag = "▌ ";      tagColor = Theme.Gold; c = Theme.Paper; break;
                default:     tag = "       "; tagColor = c = Color.FromArgb(200, 200, 204); break;
            }
            if (kind == "STEP" && logBox.TextLength > 0) logBox.AppendText("\n");
            int start = logBox.TextLength;
            logBox.AppendText(tag + text + "\n");
            var font = kind == "STEP" ? Theme.Mono(8.75f, FontStyle.Bold) : logBox.Font;
            logBox.Select(start, tag.Length);
            logBox.SelectionColor = tagColor;
            logBox.SelectionFont = font;
            logBox.Select(start + tag.Length, logBox.TextLength - start - tag.Length);
            logBox.SelectionColor = c;
            logBox.SelectionFont = font;
            logBox.Select(logBox.TextLength, 0);
            logBox.ScrollToCaret();
            try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss} [{kind}] {text}\r\n", Encoding.UTF8); } catch { }
        }

        void SetStep(int pct, string text)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => SetStep(pct, text))); return; }
            progress.Value = pct;
            lblPct.Text = Math.Max(0, Math.Min(100, pct)).ToString("000") + "%";
            lblStep.Text = text;
            Log("STEP", text.ToUpperInvariant());
        }

        void Finish(bool ok, string message)
        {
            progress.Value = 100;
            progress.BarColor = ok ? Theme.Black : Theme.Red;
            lblPct.Text = ok ? "100%" : "ERR";
            closeAllowed = true;
            lblClose.Visible = true;
            btnClose.Enabled = true;
            AcceptButton = null;
            btnClose.Focus();
            lblStep.Text = ok ? "Instalación completada" : "Finalizado con errores";
            lblStep.ForeColor = ok ? Theme.Black : Theme.Red;
            if (ok && installedPrinter != null) lnkQueue.Visible = lnkTest.Visible = true;   // ya verificada en el equipo
            SetHeader(hTag, ok ? "Completado" : "Revisión necesaria", ok ? "Volver: Esc · Salir: ✕" : "Detalle en el registro.");
            if (summary.Count > 0)
            {
                Log("STEP", "RESUMEN");
                foreach (var s in summary) Log(s.Contains("ERROR") ? "ERR" : "OK", s);
            }
            if (message != null) { Log("LOG", ""); Log("LOG", message); }
            Activate();
            TopMost = true;
            TopMost = false;
        }

        bool workDirReady;
        readonly Dictionary<string, string> extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Crea la carpeta de trabajo de la sesion. El motor y los drivers que contiene se ejecutan como administrador,
        /// asi que no puede estar donde escriba un proceso sin elevar: %ProgramData%\PrintDeploy\Work\{GUID}, creada aqui
        /// con permisos solo para Administradores y SYSTEM (sin herencia). Si ya existe, no se usa.
        /// </summary>
        void SecureWorkDir()
        {
            if (workDirReady) return;
            if (Directory.Exists(workDir)) throw new IOException("La carpeta de trabajo ya existía: " + workDir);
            var acl = new System.Security.AccessControl.DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var who in new[] { System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, System.Security.Principal.WellKnownSidType.LocalSystemSid })
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    new System.Security.Principal.SecurityIdentifier(who, null),
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));
            Directory.CreateDirectory(Path.GetDirectoryName(workDir));
            Directory.CreateDirectory(workDir, acl);
            workDirReady = true;
        }

        /// <summary>
        /// Paquete de driver embebido, descomprimido en la carpeta de trabajo protegida: directamente desde el .exe
        /// (sin copiar antes el zip a disco) y una sola vez por sesion (lo recuerda en memoria, no en disco).
        /// </summary>
        string ExtractPayload(string resource, string folder)
        {
            lock (extracted) if (extracted.TryGetValue(resource, out var ready)) return ready;
            SecureWorkDir();
            var dest = Path.Combine(workDir, folder);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            using (var s = typeof(MainForm).Assembly.GetManifestResourceStream(resource))
            {
                if (s == null) throw new FileNotFoundException($"Esta compilación no incluye el paquete de driver '{resource}'. Colóquelo en Payload\\ y vuelva a compilar (ver Payload\\README.md).");
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read)) zip.ExtractToDirectory(dest);
            }
            lock (extracted) extracted[resource] = dest;
            return dest;
        }

        int RunEngine(string args)
        {
            var psi = new ProcessStartInfo("powershell.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) HandleEngineLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { foreach (var err in StdErrLines(e.Data)) Log("ERR", err); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        /// <summary>
        /// Errores reales de la salida de error de PowerShell. Con la salida redirigida, PowerShell manda
        /// progreso y errores serializados en CLIXML ("#&lt; CLIXML" + &lt;Objs&gt;): el progreso se descarta y de
        /// los errores se saca solo el texto.
        /// </summary>
        static IEnumerable<string> StdErrLines(string data)
        {
            if (string.IsNullOrWhiteSpace(data) || data.StartsWith("#< CLIXML")) yield break;
            if (!data.StartsWith("<Objs")) { yield return data; yield break; }
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(data, "<S S=\"Error\">(.*?)</S>"))
            {
                var text = WebUtility.HtmlDecode(m.Groups[1].Value).Replace("_x000D_", "").Replace("_x000A_", " ").Trim();
                if (text.Length > 0) yield return text;
            }
        }

        void HandleEngineLine(string line)
        {
            var parts = line.Split(new[] { '|' }, 2);
            if (parts.Length < 2) { if (line.Trim().Length > 0) Log("LOG", line); return; }
            switch (parts[0])
            {
                case "STEP":
                    var sp = parts[1].Split(new[] { '|' }, 2);
                    SetStep(int.TryParse(sp[0], out var pct) ? pct : 0, sp.Length > 1 ? sp[1] : "");
                    break;
                case "SUMMARY":
                    lock (summary) summary.Add(parts[1]);
                    break;
                case "OK": case "WARN": case "ERR": case "LOG":
                    Log(parts[0], parts[1]);
                    break;
                default:
                    Log("LOG", line);
                    break;
            }
        }

        // ================= cola compartida en servidor =================
        /// <summary>Conecta \\servidor\cola: Add-Printer para el usuario actual y printui /ga para todos los usuarios.</summary>
        async Task RunShared(string unc)
        {
            ShowProgress("Servidor", "Conectando impresora", unc);
            var q = unc.Replace("'", "''");
            var script = string.Join("\n",
                "$ErrorActionPreference = 'Stop'",
                "$ProgressPreference = 'SilentlyContinue'",
                $"$c = '{q}'",
                "Write-Output 'STEP|15|Comprobando conexiones existentes'",
                "if (Get-Printer -Name $c -ErrorAction SilentlyContinue) { Remove-Printer -Name $c; Write-Output \"OK|Conexion anterior eliminada: $c\" }",
                "Write-Output 'STEP|40|Conectando y descargando el driver del servidor'",
                "Add-Printer -ConnectionName $c",
                "Write-Output \"OK|Conectada para este usuario: $c\"",
                "Write-Output 'STEP|80|Conexion para todos los usuarios del equipo'",
                "$p = Start-Process rundll32.exe -ArgumentList \"printui.dll,PrintUIEntry /ga /n`\"$c`\"\" -Wait -PassThru",
                "if ($p.ExitCode -eq 0) { Write-Output 'OK|Conexion global registrada (resto de usuarios al iniciar sesion)' } else { Write-Output \"WARN|printui /ga devolvio $($p.ExitCode)\" }",
                "Write-Output \"SUMMARY|Conectada $c\"",
                "Write-Output 'STEP|100|Finalizado'");
            var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            int exit = await Task.Run(() => RunEngine($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {b64}"));
            installedPrinter = exit == 0 && await Task.Run(() => PrinterExists(unc)) ? unc : null;
            Finish(exit == 0, exit == 0 ? "La impresora aparece como \"" + unc.TrimStart('\\').Split('\\').Last() + " en " + unc.TrimStart('\\').Split('\\')[0] + "\"." : null);
        }

        // ================= Canon: asistente oficial =================
        async Task RunCanon()
        {
            ShowProgress("Canon", "Generic Plus PCL6", "Siga los pasos del asistente de Canon.");
            bool ok = false;
            int code = -1;
            try
            {
                SetStep(10, "Preparando archivos del driver Canon");
                var dir = await Task.Run(() => ExtractPayload("canon.zip", "canon"));
                SetStep(40, "Asistente de Canon en curso");
                // el asistente es un proceso aparte: cerrarlo (termine o no) nunca cierra PrintDeploy
                code = await Task.Run(() =>
                {
                    using (var p = Process.Start(new ProcessStartInfo(Path.Combine(dir, "Setup.exe")) { WorkingDirectory = dir, UseShellExecute = true }))
                    {
                        p?.WaitForExit();
                        int exit = p?.ExitCode ?? 0;
                        WaitForProcessesIn(dir);   // Setup.exe puede delegar en otro instalador y salir antes
                        return exit;
                    }
                });
                ok = true;
                if (code == 0) Log("OK", "Asistente de Canon finalizado.");
                else Log("WARN", $"Asistente de Canon cerrado sin completar (código {code}).");
                SetStep(100, "Finalizado");
            }
            catch (Exception ex)
            {
                Log("ERR", ex.Message);
            }
            Finish(ok, !ok ? null : code == 0
                ? "«Versión posterior ya instalada»: usar Instalar en 03 Canon (sin asistente)."
                : "Asistente cerrado antes de terminar.");
        }

        /// <summary>Espera a que no quede ningun proceso ejecutandose desde esa carpeta (instaladores encadenados).</summary>
        static void WaitForProcessesIn(string dir)
        {
            var prefix = dir.TrimEnd('\\') + "\\";
            for (int i = 0; i < 3600; i++)   // tope: 1 hora
            {
                bool running;
                try
                {
                    using (var q = new ManagementObjectSearcher("SELECT ExecutablePath FROM Win32_Process"))
                        running = q.Get().Cast<ManagementObject>().Any(m => ((m["ExecutablePath"] as string) ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                }
                catch { return; }
                if (!running) return;
                System.Threading.Thread.Sleep(1000);
            }
        }
    }
}

