using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    /// <summary>Paginas 02 (instalar por IP) y 03 (colas instaladas) y el lanzador del motor para cualquier marca.</summary>
    public sealed partial class MainForm
    {
        /// <summary>Trabajo para engine.ps1: E reinstalar, N nueva en una IP, R quitar.</summary>
        sealed class InstallJob { public char Kind; public string Name, Ip; public DriverPlan Plan; }

        sealed class PrinterInfo { public string Name, Driver, Port, Host; public bool IsConnection; }

        /// <summary>Colas del equipo sin las virtuales (PDF, OneNote...). Host = IP del puerto TCP/IP, si lo tiene.</summary>
        static List<PrinterInfo> GetPrinters(bool connections = false)
        {
            var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Name, HostAddress FROM Win32_TCPIPPrinterPort"))
                    foreach (ManagementObject o in q.Get()) hosts[(string)o["Name"]] = (string)o["HostAddress"];
            }
            catch { }
            var list = new List<PrinterInfo>();
            using (var q = new ManagementObjectSearcher("SELECT Name, DriverName, PortName FROM Win32_Printer"))
                foreach (ManagementObject o in q.Get())
                {
                    var p = new PrinterInfo { Name = (string)o["Name"], Driver = (string)o["DriverName"] ?? "", Port = (string)o["PortName"] ?? "" };
                    if (VirtualPrinters.Any(v => p.Name.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0 || p.Driver.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    p.IsConnection = p.Name.StartsWith(@"\\");
                    if (p.IsConnection && !connections) continue;
                    hosts.TryGetValue(p.Port, out p.Host);
                    list.Add(p);
                }
            return list.OrderBy(p => p.IsConnection).ThenBy(p => p.Name).ToList();
        }

        /// <summary>La cola existe en el equipo (local o conexion de servidor).</summary>
        static bool PrinterExists(string name)
        {
            try { return GetPrinters(connections: true).Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }

        /// <summary>Impresoras instaladas en el equipo: IP del puerto o nombre de la conexion \\servidor\cola -> nombre de la cola.</summary>
        internal static Dictionary<string, string> InstalledMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in GetPrinters(connections: true))
                {
                    if (p.IsConnection) map[p.Name] = p.Name;
                    else if (p.Host != null && !map.ContainsKey(p.Host)) map[p.Host] = p.Name;
                }
            }
            catch { }
            return map;
        }

        /// <summary>
        /// Verde / rojo en las colas instaladas: la IP de su puerto responde como impresora (o la cola del
        /// servidor se abre). En paralelo; colas sin IP (USB, WSD) quedan con el indicador apagado.
        /// </summary>
        static async void CheckInstalled(List<CheckRow> rows)
        {
            await Task.WhenAll(rows.Select(async r =>
            {
                var p = (PrinterInfo)r.Tag;
                if (p.Host == null && !p.IsConnection) return;
                r.State = ScanState.Checking;
                try { r.State = (p.IsConnection ? await NetworkScan.CheckShared(p.Name) : await NetworkScan.Check(p.Host, quick: false, CancellationToken.None)).State; }
                catch { r.State = ScanState.Unreachable; }
            }));
        }

        static bool ValidIp(string ip) =>
            IPAddress.TryParse(ip, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ip.Count(c => c == '.') == 3;

        // ================= 02: instalar por IP =================
        void ShowInstall()
        {
            ClearBody();
            onChooser = false;
            AcceptButton = null;
            SetHeader("Instalar", "Impresora por IP", "Detección de fabricante y modelo · driver más reciente disponible.");

            ScanResult res = null;
            DriverPlan plan = null;
            PrinterInfo existing = null;
            bool busy = false, nameTouched = false, settingName = false;
            int detectId = 0;

            var fields = new TableLayoutPanel { Dock = DockStyle.Top, Height = S(68), ColumnCount = 4, RowCount = 1, BackColor = Theme.Paper, Padding = new Padding(0, S(8), 0, 0) };
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(190)));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(24)));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(170)));
            var ip = new LineField("Dirección IP", mono: true) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var name = new LineField("Nombre de la cola", mono: false) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var btnDetect = new FlatBtn("Comprobar", primary: false) { Size = new Size(S(146), S(36)), Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(S(24), S(14), 0, 0), Eye = true, SweepOnHover = true };
            fields.Controls.Add(ip, 0, 0);
            fields.Controls.Add(new Panel { BackColor = Theme.Paper }, 1, 0);
            fields.Controls.Add(name, 2, 0);
            fields.Controls.Add(btnDetect, 3, 0);
            name.Box.TextChanged += (s, e) => { if (!settingName) nameTouched = true; };

            // tarjeta de deteccion
            var card = new BufferedPanel { Dock = DockStyle.Top, Height = S(150), BackColor = Theme.Paper };
            var fTitle = Theme.Title(12f); var fData = Theme.Mono(8.75f); var fText = Theme.Sans(9.25f); var fKey = Theme.Strong(7.5f);
            Eyes.Attach(card);
            card.Paint += (s, e) =>
            {
                var g = Theme.Crisp(e.Graphics);
                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                int x = S(30), w = card.Width - x, y = S(18);
                using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, 0, S(6), card.Width, 1);
                if (busy) { ScannerForm.DrawState(g, ScanState.Checking, new Rectangle(S(5), y + S(3), S(16), S(16)), card); TextRenderer.DrawText(g, "Comprobando " + ip.Box.Text.Trim() + "…", fText, new Rectangle(x, y, w, S(22)), Theme.Muted, flags); return; }
                if (res == null) { TextRenderer.DrawText(g, "Escriba la IP: la detección empieza sola.", fText, new Rectangle(0, y, card.Width, S(22)), Theme.Muted, flags); return; }
                ScannerForm.DrawState(g, res.State, new Rectangle(S(5), y + S(4), S(16), S(16)), card);
                if (res.State != ScanState.Printer)
                {
                    TextRenderer.DrawText(g, res.Model, fTitle, new Rectangle(x, y, w, S(24)), Theme.Red, flags);
                    TextRenderer.DrawText(g, res.Hint, fText, new Rectangle(x, y + S(30), w, S(20)), Theme.Muted, flags);
                    return;
                }
                TextRenderer.DrawText(g, (plan.Brand.Length > 0 && res.Model.IndexOf(plan.Brand, StringComparison.OrdinalIgnoreCase) < 0 ? plan.Brand + " · " : "") + res.Model, fTitle, new Rectangle(x, y, w, S(24)), Theme.Black, flags);
                var data = ip.Box.Text.Trim() + (res.PingMs != null ? $"   ·   {res.PingMs} ms" : "") + (res.Ports.Count > 0 ? "   ·   tcp " + string.Join(" ", res.Ports) : "");
                TextRenderer.DrawText(g, data, fData, new Rectangle(x, y + S(28), w, S(18)), Theme.Muted, flags);
                var rows = new[]
                {
                    ("SNMP", string.Join("  ·  ", new[] { res.Name, res.Location }.Where(v => v.Length > 0)) is var id && id.Length > 0 ? id : "sin nombre ni ubicación", Theme.Muted),
                    ("DRIVER", plan.Text, plan.Generic ? Theme.Red : Theme.Black),
                    ("COLA", existing != null ? $"«{existing.Name}» ya usa esta IP: se reinstala con este driver" : "nueva · puerto TCP/IP con SNMP", Theme.Black),
                };
                int ry = y + S(52);
                foreach (var (key, text, color) in rows)
                {
                    TextRenderer.DrawText(g, key, fKey, new Rectangle(x, ry, S(64), S(20)), Theme.GoldDeep, flags);
                    TextRenderer.DrawText(g, text, fText, new Rectangle(x + S(64), ry, w - S(64), S(20)), color, flags);
                    ry += S(22);
                }
            };

            var btnGo = new FlatBtn("Instalar", primary: true) { Enabled = false };
            var btnWizard = new FlatBtn("Asistente Canon", primary: false) { Visible = false };
            var btnBack = new FlatBtn("Atrás", primary: false);

            async void Detect()
            {
                var addr = ip.Box.Text.Trim();
                if (!ValidIp(addr)) { res = null; plan = null; btnGo.Enabled = false; card.Invalidate(); return; }
                int id = ++detectId;
                busy = true; btnGo.Enabled = false; btnDetect.Scanning = true; card.Invalidate();
                var r = await NetworkScan.Check(addr, quick: false, CancellationToken.None);
                var pl = r.State == ScanState.Printer ? await Task.Run(() => DriverCatalog.Resolve(r.Brand, r.Description)) : null;
                var ex = await Task.Run(() => GetPrinters().FirstOrDefault(p => p.Host == addr));
                if (id != detectId || IsDisposed || card.IsDisposed) return;   // otra IP escrita mientras tanto, o se cambio de pantalla
                busy = false; btnDetect.Scanning = false;
                res = r; plan = pl; existing = ex;
                btnGo.Enabled = plan != null;
                btnWizard.Visible = plan?.Brand == "Canon";
                if (plan != null && !nameTouched)
                {
                    settingName = true;
                    name.Box.Text = existing?.Name ?? new ScanEntry { Ip = addr, Result = r, State = r.State }.QueueName;
                    settingName = false;
                }
                card.Invalidate();
            }

            var debounce = new System.Windows.Forms.Timer { Interval = 600 };
            debounce.Tick += (s, e) => { debounce.Stop(); Detect(); };
            ip.Box.TextChanged += (s, e) => { debounce.Stop(); res = null; btnGo.Enabled = false; card.Invalidate(); if (ValidIp(ip.Box.Text.Trim())) debounce.Start(); };
            ip.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; debounce.Stop(); if (btnGo.Enabled) Go(); else Detect(); } };
            name.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && btnGo.Enabled) { e.SuppressKeyPress = true; Go(); } };
            btnDetect.Click += (s, e) => Detect();
            card.Disposed += (s, e) => debounce.Dispose();   // se libera con la pagina

            async void Go()
            {
                if (plan == null || busy) return;
                var addr = ip.Box.Text.Trim();
                var queue = name.Box.Text.Trim().Length > 0 ? name.Box.Text.Trim() : (plan.Brand.Length > 0 ? plan.Brand : "Impresora");
                var job = existing != null
                    ? new InstallJob { Kind = 'E', Name = existing.Name, Ip = addr, Plan = plan }
                    : new InstallJob { Kind = 'N', Name = queue, Ip = addr, Plan = plan };
                var action = existing != null ? $"Reinstalar «{existing.Name}» (puerto {addr})" : $"Nueva cola «{queue}» · {addr}";
                var warn = plan.Generic ? "\n\nDriver genérico de Windows: sin bandejas ni acabados de la marca." : "";
                if (!ThemedDialog.Confirm(this, plan.Brand.Length > 0 ? plan.Brand : "Impresora", res.Model, $"· {action}\n· Driver: {plan.Text}{warn}")) return;
                await RunJobs(new List<InstallJob> { job }, plan.Brand);
            }
            btnGo.Click += (s, e) => Go();
            btnWizard.Click += async (s, e) => await RunCanon();
            btnBack.Click += (s, e) => ShowChooser();

            body.Controls.Add(card);
            body.Controls.Add(fields);
            body.Controls.Add(Section("Impresora por IP"));
            body.Controls.Add(ButtonBar(btnGo, btnWizard, btnBack));
            ip.Box.Focus();
        }

        // ================= 03: colas instaladas =================
        async void ShowQueues()
        {
            ClearBody();
            int mine = page;
            onChooser = false;
            AcceptButton = null;
            SetHeader("Impresoras", "Impresoras instaladas", "Marcadas: reinstalar con el driver óptimo o quitar (cola, puerto y driver sin uso).");

            // WMI fuera del hilo de la interfaz (con colas de servidor caidas puede tardar segundos)
            var printers = await Task.Run(() => GetPrinters(connections: true));
            if (mine != page || IsDisposed) return;   // ya se cambio de pantalla
            var rowsList = new List<CheckRow>();
            int visibles = Math.Max(1, Math.Min(8, printers.Count));
            var list = new Panel { Dock = DockStyle.Top, Height = S(32 + visibles * 40), AutoScroll = true, BackColor = Theme.Paper };
            var head = new CheckRow { Header = true, StateColumn = true, Columns = new[] { "Cola", "Driver actual", "Puerto / IP" }, Widths = new[] { .38f, .38f, .24f }, Dock = DockStyle.Top, Height = S(32) };
            var controls = new List<Control>();
            foreach (var p in printers)
            {
                var row = new CheckRow { Columns = new[] { p.Name, p.Driver, p.IsConnection ? "servidor" : p.Host ?? p.Port }, Widths = head.Widths, Tag = p, Dock = DockStyle.Top, Height = S(40), StateColumn = true };
                rowsList.Add(row);
                controls.Add(row);
            }
            if (printers.Count == 0)
                controls.Add(new Label { Text = "Sin colas de red en este equipo.", ForeColor = Theme.Muted, Dock = DockStyle.Top, Height = S(40), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(S(14), 0, 0, 0) });
            for (int i = controls.Count - 1; i >= 0; i--) list.Controls.Add(controls[i]);
            list.Controls.Add(head);
            CheckInstalled(rowsList);

            var note = new Label { Dock = DockStyle.Top, Height = S(30), Font = Theme.Sans(9f), ForeColor = Theme.Muted, Padding = new Padding(0, S(10), 0, 0),
                Text = "Colas de servidor (\\\\servidor\\cola): solo quitar; para reconectar, scanner." };

            var btnReinstall = new FlatBtn("Reinstalar", primary: true);
            var btnRemove = new FlatBtn("Quitar", primary: false);
            var btnBack = new FlatBtn("Atrás", primary: false);
            List<PrinterInfo> Selected() => rowsList.Where(r => r.Checked).Select(r => (PrinterInfo)r.Tag).ToList();

            btnReinstall.Click += async (s, e) =>
            {
                var sel = Selected().Where(p => !p.IsConnection).ToList();
                if (sel.Count == 0) { Warn("Ninguna cola de IP marcada."); return; }
                btnReinstall.Enabled = btnRemove.Enabled = false;
                UseWaitCursor = true;
                // modelo por la IP de cada cola; si no responde, por su driver o nombre actual
                var plans = await Task.WhenAll(sel.Select(async p =>
                {
                    ScanResult r = null;
                    if (p.Host != null) r = await NetworkScan.Check(p.Host, quick: false, CancellationToken.None);
                    bool live = r?.State == ScanState.Printer;
                    var desc = live ? r.Description : p.Driver + " " + p.Name;
                    var brand = live ? r.Brand : DriverCatalog.BrandOf(desc);
                    return (p, live, plan: await Task.Run(() => DriverCatalog.Resolve(brand, desc)));
                }));
                UseWaitCursor = false;
                btnReinstall.Enabled = btnRemove.Enabled = true;
                var offline = plans.Where(x => !x.live).Select(x => $"· {x.p.Name} ({x.p.Host ?? x.p.Port})").ToList();
                if (offline.Count > 0 && !Ask("Sin respuesta (driver elegido por el driver / nombre actual):\n\n" + string.Join("\n", offline)))
                    return;
                var text = new StringBuilder();
                foreach (var x in plans) text.AppendLine($"· {x.p.Name}: {x.p.Driver}  →  {x.plan.Text}");
                text.Append("· Drivers sin uso: se eliminan");
                if (!ThemedDialog.Confirm(this, "Colas", "Reinstalar", text.ToString(), "Reinstalar")) return;
                await RunJobs(plans.Select(x => new InstallJob { Kind = 'E', Name = x.p.Name, Ip = x.p.Host, Plan = x.plan }).ToList(), "Colas");
            };
            btnRemove.Click += async (s, e) =>
            {
                var sel = Selected();
                if (sel.Count == 0) { Warn("Ninguna cola marcada."); return; }
                var text = string.Join("\n", sel.Select(p => $"· {p.Name}  ({p.Driver})")) + "\n· Puerto TCP/IP y driver: se eliminan si nadie más los usa";
                if (!ThemedDialog.Confirm(this, "Colas", "Quitar", text, "Quitar")) return;
                await RunJobs(sel.Select(p => new InstallJob { Kind = 'R', Name = p.Name }).ToList(), "Colas");
            };
            btnBack.Click += (s, e) => ShowChooser();

            body.Controls.Add(note);
            body.Controls.Add(list);
            body.Controls.Add(Section("Colas de red en este equipo"));
            body.Controls.Add(ButtonBar(btnReinstall, btnRemove, btnBack));
        }

        // ================= motor =================
        /// <summary>Extrae solo los paquetes necesarios y lanza engine.ps1 con los trabajos.</summary>
        async Task RunJobs(List<InstallJob> jobs, string tag)
        {
            bool removing = jobs.All(j => j.Kind == 'R');
            ShowProgress(tag.Length > 0 ? tag : "Impresoras", removing ? "Quitando" : "Instalando", "No cerrar hasta finalizar.");
            int exit = -1;
            try
            {
                var dirs = new Dictionary<string, string>();
                foreach (var plan in jobs.Where(j => j.Plan?.Resource != null).Select(j => j.Plan).GroupBy(p => p.Resource).Select(g => g.First()))
                {
                    SetStep(2, "Preparando paquete " + plan.DriverName);
                    var res = plan.Resource;
                    dirs[res] = await Task.Run(() => ExtractPayload(res, Path.GetFileNameWithoutExtension(res)));
                }
                SecureWorkDir();
                var engine = Path.Combine(workDir, "engine.ps1");
                using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("engine.ps1"))
                using (var f = File.Create(engine)) s.CopyTo(f);

                string clean(string v) => (v ?? "").Replace("\t", " ").Replace("\n", " ").Replace("\r", "");
                var lines = jobs.Select(j =>
                {
                    if (j.Kind == 'R') return "R\t" + clean(j.Name);
                    var inf = j.Plan.Resource != null ? Path.Combine(dirs[j.Plan.Resource], j.Plan.Inf) : "";
                    var f = new List<string> { j.Kind.ToString(), clean(j.Name), j.Plan.DriverName, inf };
                    if (j.Kind == 'N') f.Add(j.Ip);
                    return string.Join("\t", f);
                });
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
                exit = await Task.Run(() => RunEngine($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{engine}\" -JobsB64 {b64}"));
            }
            catch (Exception ex)
            {
                Log("ERR", ex.Message);
            }
            bool ok = exit == 0;
            // una sola impresora instalada: su nombre real (si la IP ya tenia cola, el motor reinstala esa)
            if (ok && !removing && jobs.Count == 1)
                installedPrinter = await Task.Run(() => GetPrinters().FirstOrDefault(p => p.Host == jobs[0].Ip)?.Name);
            await Task.Run(() => DriverCatalog.Installed(refresh: true));
            Finish(ok, ok && jobs.Any(j => j.Plan?.Brand == "Ricoh") ? "Bandeja bypass: Imprimir > Preferencias > Bandeja de entrada > \"Bandeja bypass\"." : null);
        }
    }
}
