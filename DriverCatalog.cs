using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InstaladorImpresoras
{
    /// <summary>Driver elegido para una impresora concreta y de donde sale.</summary>
    sealed class DriverPlan
    {
        public string Brand = "", DriverName = "";
        public string Resource, Inf;      // paquete embebido a instalar; null = ya instalado o de Windows
        public Version Version;           // version que quedara instalada (null = desconocida)
        public string Source = "";        // "instalado", "paquete", "Windows"
        public string Note = "";          // matiz: "actualiza v3.31", "driver generico"...
        public bool Generic;              // driver de clase de Windows: imprime, sin opciones de la marca

        public string Text => $"{DriverName}{(Version != null ? " v" + Version : "")} · {Source}{(Note.Length > 0 ? " · " + Note : "")}";
    }

    /// <summary>
    /// Eleccion de driver por marca y modelo, siempre la version mas reciente disponible:
    /// compara lo instalado en Windows con lo que lleva el .exe y, para marcas sin paquete,
    /// usa su driver universal ya instalado o, en ultimo caso, el de clase PCL6 de Windows.
    /// </summary>
    static class DriverCatalog
    {
        public const string CanonDriver = "Canon Generic Plus PCL6";
        public const string CanonResource = "canon.zip";
        public const string CanonInf = @"Driver\CNP60MA64.INF";
        public const string ClassDriver = "Microsoft PCL6 Class Driver";

        static readonly (string brand, Regex match)[] Brands =
        {
            ("Ricoh", new Regex(@"ricoh|savin|lanier|gestetner|nashuatec|\bnrg\b|rex-rotary|infotec", RegexOptions.IgnoreCase)),
            ("Canon", new Regex(@"canon|imagerunner|imageforce|i-sensys|imageclass", RegexOptions.IgnoreCase)),
            ("HP", new Regex(@"\bhp\b|hewlett|laserjet|officejet|pagewide|jetdirect", RegexOptions.IgnoreCase)),
            ("Kyocera", new Regex(@"kyocera|ecosys|taskalfa", RegexOptions.IgnoreCase)),
            ("Xerox", new Regex(@"xerox|workcentre|versalink|altalink", RegexOptions.IgnoreCase)),
            ("Brother", new Regex(@"brother", RegexOptions.IgnoreCase)),
            ("Konica Minolta", new Regex(@"konica|minolta|bizhub", RegexOptions.IgnoreCase)),
            ("Sharp", new Regex(@"sharp|\bmx-\d", RegexOptions.IgnoreCase)),
            ("Lexmark", new Regex(@"lexmark", RegexOptions.IgnoreCase)),
            ("Epson", new Regex(@"epson", RegexOptions.IgnoreCase)),
            ("Toshiba", new Regex(@"toshiba|e-studio", RegexOptions.IgnoreCase)),
            ("Samsung", new Regex(@"samsung", RegexOptions.IgnoreCase)),
        };

        /// <summary>Marca a partir de lo que dice el equipo (SNMP / IPP / web) o de un nombre de driver.</summary>
        public static string BrandOf(string text) =>
            string.IsNullOrEmpty(text) ? "" : Brands.FirstOrDefault(b => b.match.IsMatch(text)).brand ?? "";

        // ---------- drivers instalados ----------
        static volatile Dictionary<string, Version> installedCache;
        static DateTime installedAt;

        static volatile bool refreshing;

        /// <summary>
        /// Drivers de impresora instalados (nombre -> version). La primera vez (o con refresh) consulta WMI; despues
        /// devuelve la cache al momento y, si tiene mas de 20 s, la renueva en segundo plano: se llama al pintar el
        /// escaner y no debe bloquear la interfaz.
        /// </summary>
        public static Dictionary<string, Version> Installed(bool refresh = false)
        {
            var cache = installedCache;
            if (refresh || cache == null) return Load();
            if ((DateTime.Now - installedAt).TotalSeconds >= 20 && !refreshing)
            {
                refreshing = true;
                Task.Run(() => { try { Load(); } finally { refreshing = false; } });
            }
            return cache;
        }

        static Dictionary<string, Version> Load()
        {
            var map = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT Name, DriverVersion FROM MSFT_PrinterDriver"))
                    foreach (ManagementObject o in q.Get())
                        using (o)
                        {
                            var name = (string)o["Name"];
                            if (name == null) continue;
                            var v = ToVersion(Convert.ToUInt64(o["DriverVersion"] ?? 0UL));
                            if (!map.TryGetValue(name, out var have) || v > have) map[name] = v;
                        }
            }
            catch { }
            installedAt = DateTime.Now;
            return installedCache = map;   // asignacion atomica: los lectores ven la cache vieja o la nueva entera
        }

        /// <summary>Precarga en segundo plano (al arrancar): drivers instalados y versiones de los paquetes embebidos.</summary>
        public static void Warm() => Task.Run(() =>
        {
            Installed();
            foreach (var (brand, model) in new[] { ("Ricoh", "RICOH IM C4500"), ("Ricoh", "RICOH IM C4510A"), ("Ricoh", "RICOH"), ("Canon", "Canon"), ("HP", "HP") })
                try { Resolve(brand, model); } catch { }
        });

        /// <summary>DriverVersion de Windows: cuatro campos de 16 bits en un entero de 64.</summary>
        static Version ToVersion(ulong v) =>
            new Version((int)((v >> 48) & 0xFFFF), (int)((v >> 32) & 0xFFFF), (int)((v >> 16) & 0xFFFF), (int)(v & 0xFFFF));

        // ---------- version de los paquetes embebidos ----------
        static readonly Dictionary<string, Version> packageCache = new Dictionary<string, Version>();

        /// <summary>DriverVer del INF dentro del zip embebido; null si el .exe no lleva ese paquete.</summary>
        public static Version PackageVersion(string resource, string inf)
        {
            var key = resource + "|" + inf;
            lock (packageCache)
            {
                if (packageCache.TryGetValue(key, out var cached)) return cached;
                Version v = null;
                try
                {
                    using (var s = typeof(DriverCatalog).Assembly.GetManifestResourceStream(resource))
                        if (s != null)
                            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                            {
                                var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('/', '\\').Equals(inf, StringComparison.OrdinalIgnoreCase));
                                if (entry != null)
                                    using (var r = new StreamReader(entry.Open()))
                                    {
                                        var m = Regex.Match(r.ReadToEnd(), @"DriverVer\s*=\s*[^,\r\n]*,\s*([\d.]+)", RegexOptions.IgnoreCase);
                                        if (m.Success) Version.TryParse(m.Groups[1].Value, out v);
                                    }
                            }
                }
                catch { }
                return packageCache[key] = v;
            }
        }

        // ---------- eleccion ----------
        /// <summary>
        /// Driver para una impresora: marca (si no se conoce, se deduce de la descripcion) y lo que dice
        /// el equipo de si mismo. Nunca null: como minimo, el driver de clase PCL6 de Windows.
        /// </summary>
        public static DriverPlan Resolve(string brand, string description)
        {
            var installed = Installed();
            if (string.IsNullOrEmpty(brand)) brand = BrandOf(description);
            DriverPlan plan = null;
            switch (brand)
            {
                case "Ricoh":
                    var model = RicohModels.Detect(description);
                    plan = model != null
                        ? Newest(brand, installed, model.DriverName, exact: true, model.DriverName, model.Resource, model.Inf)
                        : Newest(brand, installed, "RICOH PCL6 UniversalDriver", exact: false, RicohModels.Universal.DriverName, RicohModels.Universal.Resource, RicohModels.Universal.Inf);
                    if (plan != null && model == null) plan.Note = Join(plan.Note, "universal (modelo sin driver propio)");
                    break;
                case "Canon":
                    plan = Newest(brand, installed, CanonDriver, exact: true, CanonDriver, CanonResource, CanonInf);
                    break;
                case "HP":
                    plan = Newest(brand, installed, "HP Universal Printing PCL 6", exact: false, null, null, null);
                    break;
            }
            if (plan != null) return plan;

            // otras marcas: su driver universal / PCL si ya esta instalado
            if (brand.Length > 0)
            {
                var own = installed.Where(kv => kv.Key.StartsWith(brand.Split(' ')[0], StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(kv.Key, "universal|pcl ?6|generic", RegexOptions.IgnoreCase))
                                   .OrderByDescending(kv => kv.Value).FirstOrDefault();
                if (own.Key != null) return new DriverPlan { Brand = brand, DriverName = own.Key, Version = own.Value, Source = "instalado" };
            }
            installed.TryGetValue(ClassDriver, out var cls);
            return new DriverPlan
            {
                Brand = brand, DriverName = ClassDriver, Version = cls, Source = "Windows", Generic = true,
                Note = "genérico: imprime, sin bandejas ni acabados de la marca",
            };
        }

        /// <summary>
        /// El mas nuevo entre los instalados (nombre exacto o familia por prefijo) y el paquete embebido.
        /// A igual version gana el instalado: no se reinstala lo que ya esta.
        /// </summary>
        static DriverPlan Newest(string brand, Dictionary<string, Version> installed, string name, bool exact, string pkgName, string resource, string inf)
        {
            var have = installed.Where(kv => exact ? kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase) : kv.Key.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(kv => kv.Value).FirstOrDefault();
            var pkg = resource != null ? PackageVersion(resource, inf) : null;
            if (have.Key != null && (pkg == null || have.Value >= pkg))
                return new DriverPlan { Brand = brand, DriverName = have.Key, Version = have.Value, Source = "instalado", Note = pkg != null && have.Value > pkg ? $"más nuevo que el paquete (v{pkg})" : "" };
            if (pkg != null)
                return new DriverPlan { Brand = brand, DriverName = pkgName, Resource = resource, Inf = inf, Version = pkg, Source = "paquete", Note = have.Key != null ? $"sustituye v{have.Value}" : "" };
            return null;
        }

        static string Join(string a, string b) => a.Length == 0 ? b : a + " · " + b;
    }
}
