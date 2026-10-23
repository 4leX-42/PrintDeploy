using System.Linq;
using System.Text.RegularExpressions;

namespace InstaladorImpresoras
{
    /// <summary>Modelos Ricoh soportados: cada uno con su paquete de driver embebido.</summary>
    public sealed class RicohModel
    {
        public string Display;     // nombre visible
        public string DriverName;  // nombre exacto del driver en el INF
        public string Resource;    // zip embebido
        public string Inf;         // INF relativo dentro del zip
        public Regex Match;        // detecta el modelo en la descripcion SNMP/web o en el driver actual

        public override string ToString() => Display;
    }

    public static class RicohModels
    {
        public static readonly RicohModel[] All =
        {
            new RicohModel
            {
                Display = "IM C4510A", DriverName = "RICOH IM C4510 PCL 6",
                Resource = "ricoh.zip", Inf = @"disk1\oemsetup.inf",
                Match = new Regex(@"IM\s*C4510", RegexOptions.IgnoreCase),
            },
            new RicohModel
            {
                Display = "IM C4500", DriverName = "RICOH IM C4500 PCL 6",
                Resource = "ricoh_c4500.zip", Inf = @"disk1\MPC4500_.inf",
                Match = new Regex(@"IM\s*C4500", RegexOptions.IgnoreCase),
            },
        };

        /// <summary>Driver universal Ricoh: se usa sin preguntar cuando el modelo no se puede identificar.</summary>
        public static readonly RicohModel Universal = new RicohModel
        {
            Display = "Universal", DriverName = "RICOH PCL6 UniversalDriver V4.46",
            Resource = "ricoh_universal.zip", Inf = @"disk1\oemsetup.inf",
            Match = new Regex(@"UniversalDriver", RegexOptions.IgnoreCase),
        };


        /// <summary>Modelo a partir de un texto (descripcion SNMP, driver instalado...). null si no coincide ninguno.</summary>
        public static RicohModel Detect(string text) =>
            string.IsNullOrEmpty(text) ? null : All.FirstOrDefault(m => m.Match.IsMatch(text));
    }
}
