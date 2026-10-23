using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace InstaladorImpresoras
{
    public sealed partial class MainForm
    {
        bool trackingTest;

        sealed class PrintJob { public uint Id, Mask, Pages, Printed; public string Document, Status; }

        /// <summary>
        /// Pagina de prueba con seguimiento propio: la envia (printui /k) y sigue el trabajo en la cola de Windows
        /// (Win32_PrintJob) y los contadores de la cola (trabajos impresos / errores) hasta que se entrega o falla.
        /// Despues lo confirma en la propia impresora por SNMP: su contador de paginas tiene que subir (hasta 25 s
        /// desde el envio); si no sube, lee sus averias (sin papel, atasco...). El resultado va al registro y a la
        /// linea de estado.
        /// </summary>
        async void SendTestPage()
        {
            var name = installedPrinter;
            if (string.IsNullOrEmpty(name) || trackingTest) return;
            trackingTest = true;
            lnkTest.Enabled = false;
            try
            {
                Log("STEP", "PÁGINA DE PRUEBA");
                var before = await Task.Run(() => Jobs(name).Select(j => j.Id).ToList());
                var countersBefore = await Task.Run(() => Counters(name));
                // la impresora en si: IP del puerto (las conexiones \\servidor\cola no tienen) y su contador de paginas
                var host = await Task.Run(() => GetPrinters().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Host);
                var device = host != null ? await Task.Run(() => PrinterProbe.PrinterCounters(host, 700)) : null;
                long? pagesBefore = device?.pages;
                if (pagesBefore != null) Log("LOG", $"Contador de la impresora ({host}): {pagesBefore:N0} páginas");
                if (!string.IsNullOrEmpty(device?.errors)) Log("WARN", "La impresora avisa: " + device?.errors);
                var sent = Stopwatch.StartNew();
                PrintUi("/k", name);
                Log("OK", $"Enviada a «{name}»");
                Status("esperando el trabajo en la cola…", false);

                PrintJob seen = null;
                string last = null;
                bool failed = false;
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 90)
                {
                    await Task.Delay(250);
                    var jobs = await Task.Run(() => Jobs(name));
                    var job = seen == null ? jobs.FirstOrDefault(j => !before.Contains(j.Id)) : jobs.FirstOrDefault(j => j.Id == seen.Id);
                    if (seen == null)
                    {
                        if (job != null) { seen = job; Log("OK", $"Trabajo {job.Id} en la cola · {job.Document}"); }
                        else if (sw.Elapsed.TotalSeconds > 6) break;   // no aparecio: o ya se proceso o no se creo
                        else continue;
                    }
                    if (job == null) break;                            // salio de la cola
                    var state = JobState(job);
                    bool error = (job.Mask & (2 | 32 | 64 | 512 | 1024)) != 0;
                    if (state != last)
                    {
                        last = state;
                        Log(error ? "ERR" : "LOG", state);
                        Status(state, error);
                    }
                    if (error) failed = true;
                }

                var countersAfter = await Task.Run(() => Counters(name));
                bool delivered = countersAfter.printed > countersBefore.printed || (seen != null && last != null && !failed && sw.Elapsed.TotalSeconds < 90);
                if (countersAfter.errors > countersBefore.errors || (failed && !delivered))
                {
                    Log("ERR", "Windows no ha podido entregarla a la impresora. Revise conexión o la cola en «Impresoras y escáneres».");
                    Status("error al enviar", true);
                    return;
                }
                if (!delivered)
                {
                    Log("WARN", seen == null ? "No se ha visto el trabajo en la cola de Windows." : "Sin confirmación en 90 s: el trabajo sigue en la cola.");
                }
                else Log("OK", "Entregada a la impresora por Windows");

                // confirmacion fisica: el contador de paginas de la impresora
                if (host == null)
                {
                    if (name.StartsWith(@"\\"))
                        Log("LOG", "Cola de servidor: la impresión depende del servidor (PaperCut la retiene hasta validarse en la impresora).");
                    else
                        Log("LOG", "Cola sin IP (USB, WSD o archivo): solo se puede confirmar la entrega.");
                    Status(delivered ? "entregada" : "sin confirmación", !delivered);
                    return;
                }
                if (pagesBefore == null)
                {
                    Log("WARN", "La impresora no responde a SNMP: no se puede comprobar si ha salido la hoja.");
                    Status(delivered ? "entregada (sin confirmar en la impresora)" : "sin confirmación", true);
                    return;
                }
                Status("esperando a que la impresora la imprima…", false);
                (long? pages, int? status, string errors)? now = null;
                while (sent.Elapsed.TotalSeconds < 25)
                {
                    now = await Task.Run(() => PrinterProbe.PrinterCounters(host, 700));
                    if (now?.pages > pagesBefore)
                    {
                        Log("OK", $"Página impresa: contador {pagesBefore:N0} → {now?.pages:N0} ({sent.Elapsed.TotalSeconds:N0} s desde el envío)");
                        Status("impresa correctamente", false);
                        return;
                    }
                    await Task.Delay(1000);
                }
                var why = now?.errors;
                if (!string.IsNullOrEmpty(why))
                {
                    Log("ERR", "No ha salido la hoja. La impresora avisa: " + why);
                    Status("no impresa · " + why, true);
                }
                else if (now?.status == 5)
                {
                    Log("WARN", "La impresora sigue calentando: compruebe la salida en un momento.");
                    Status("calentando", true);
                }
                else
                {
                    Log("WARN", "Entregada, pero el contador no ha cambiado en 25 s: puede estar retenida en la impresora (ID de departamento, impresión segura) o en espera.");
                    Status("entregada, sin imprimir aún", true);
                }
            }
            catch (Exception ex) { Log("ERR", ex.Message); }
            finally
            {
                trackingTest = false;
                lnkTest.Enabled = true;
            }
        }

        void Status(string text, bool bad)
        {
            lblStep.Text = "Página de prueba: " + text;
            lblStep.ForeColor = bad ? Theme.Red : Theme.Black;
        }

        /// <summary>Trabajos de una impresora (Win32_PrintJob.Name = "impresora, id").</summary>
        static List<PrintJob> Jobs(string printer)
        {
            var list = new List<PrintJob>();
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Name, JobId, Document, JobStatus, StatusMask, TotalPages, PagesPrinted FROM Win32_PrintJob"))
                    foreach (ManagementObject o in q.Get())
                        using (o)
                        {
                            var n = (string)o["Name"] ?? "";
                            int comma = n.LastIndexOf(',');
                            if (comma < 0 || !n.Substring(0, comma).Equals(printer, StringComparison.OrdinalIgnoreCase)) continue;
                            list.Add(new PrintJob
                            {
                                Id = Convert.ToUInt32(o["JobId"] ?? 0u),
                                Document = (string)o["Document"] ?? "",
                                Status = (string)o["JobStatus"] ?? "",
                                Mask = Convert.ToUInt32(o["StatusMask"] ?? 0u),
                                Pages = Convert.ToUInt32(o["TotalPages"] ?? 0u),
                                Printed = Convert.ToUInt32(o["PagesPrinted"] ?? 0u),
                            });
                        }
            }
            catch { }
            return list;
        }

        /// <summary>Contadores de la cola desde que arranco el spooler: trabajos impresos y errores.</summary>
        static (ulong printed, ulong errors) Counters(string printer)
        {
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Name, TotalJobsPrinted, JobErrors FROM Win32_PerfRawData_Spooler_PrintQueue"))
                    foreach (ManagementObject o in q.Get())
                        using (o)
                            if (((string)o["Name"] ?? "").Equals(printer, StringComparison.OrdinalIgnoreCase))
                                return (Convert.ToUInt64(o["TotalJobsPrinted"] ?? 0ul), Convert.ToUInt64(o["JobErrors"] ?? 0ul));
            }
            catch { }
            return (0, 0);
        }

        /// <summary>Estado legible a partir de StatusMask (JOB_STATUS_*).</summary>
        static string JobState(PrintJob j)
        {
            var m = j.Mask;
            if ((m & 64) != 0) return "sin papel";
            if ((m & 32) != 0) return "impresora sin conexión";
            if ((m & 1024) != 0) return "requiere intervención en la impresora";
            if ((m & 2) != 0) return "error" + (j.Status.Length > 0 ? ": " + j.Status : "");
            if ((m & 512) != 0) return "bloqueado";
            if ((m & 1) != 0) return "en pausa";
            if ((m & 4) != 0) return "eliminándose";
            if ((m & 16) != 0) return j.Pages > 0 ? $"imprimiendo ({j.Printed}/{j.Pages} págs.)" : "imprimiendo";
            if ((m & 8) != 0) return "preparando (spooling)";
            if ((m & (128 | 4096)) != 0) return "impreso";
            return "en cola";
        }
    }
}
