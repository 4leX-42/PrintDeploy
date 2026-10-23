using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    sealed class OfficePrinter
    {
        public string Office, Location, Brand, Ip;   // Ip: direccion IP o cola compartida \servidor\cola
        public bool IsShared => Ip.StartsWith(@"\\");
        /// <summary>"Marca - Ubicación" (p. ej. "Canon - Cultura"); sin ubicación, la oficina.</summary>
        public string QueueName => $"{(Brand.Length > 0 ? Brand : "Impresora")} - {(Location.Length > 0 ? Location : Office)}";
        /// <summary>Nombre en el escaner: la ubicación ("Cultura"); la marca va en la columna del modelo.</summary>
        public string Label => Location.Length > 0 ? Location : Brand.Length > 0 ? Brand : "Impresora";
    }

    /// <summary>
    /// Directorio de impresoras por oficina. Orden de busqueda: oficinas.txt junto al .exe,
    /// %ProgramData%\PrintDeploy\oficinas.txt y, por ultimo, el embebido al compilar (si lo hay).
    /// </summary>
    static class PrinterDirectory
    {
        public static readonly string[] ExternalPaths =
        {
            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "oficinas.txt"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrintDeploy", "oficinas.txt"),
        };

        public static List<OfficePrinter> Load()
        {
            string text = null;
            var external = ExternalPaths.FirstOrDefault(File.Exists);
            if (external != null) text = File.ReadAllText(external, Encoding.UTF8);
            else
                using (var s = typeof(PrinterDirectory).Assembly.GetManifestResourceStream("oficinas.txt"))
                    if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();

            var list = new List<OfficePrinter>();
            if (text == null) return list;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split('|').Select(x => x.Trim()).ToArray();
                if (p.Length < 4) continue;
                string clean(string v) => v == "-" ? "" : v;
                list.Add(new OfficePrinter { Office = p[0], Location = clean(p[1]), Brand = clean(p[2]), Ip = p[3] });
            }
            return list;
        }
    }
}
