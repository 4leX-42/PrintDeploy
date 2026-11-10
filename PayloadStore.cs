using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Paquetes de driver: embebidos en el .exe (compilación completa) o en la carpeta Payload\ junto al .exe
    /// (compilación portable, -p:EmbedPayload=false). Un zip externo solo se ejecuta si su SHA-256 coincide con
    /// el que se fijó al compilar (recurso payload.sha256): lo que contiene se instala elevado.
    /// </summary>
    static class PayloadStore
    {
        static readonly Assembly asm = typeof(PayloadStore).Assembly;
        static Dictionary<string, string> pinned;

        static string ExternalPath(string resource)
        {
            // junto al propio ensamblado (no Application.ExecutablePath): también vale cargado desde tools\harness.ps1
            var p = Path.Combine(Path.GetDirectoryName(asm.Location), "Payload", resource);
            return File.Exists(p) ? p : null;
        }

        /// <summary>Huella fijada al compilar para ese zip; null si la compilación no lo conocía.</summary>
        static string PinnedHash(string resource)
        {
            if (pinned == null)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var s = asm.GetManifestResourceStream("payload.sha256"))
                    if (s != null)
                        using (var r = new StreamReader(s))
                            for (string line; (line = r.ReadLine()) != null;)
                            {
                                var p = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                                if (p.Length == 2) map[p[1].Trim()] = p[0].Trim();
                            }
                pinned = map;
            }
            return pinned.TryGetValue(resource, out var h) ? h : null;
        }

        /// <summary>Solo para leer datos (la versión del INF): sin verificar. null si el paquete no está.</summary>
        public static Stream OpenForRead(string resource)
        {
            var s = asm.GetManifestResourceStream(resource);
            if (s != null) return s;
            var path = ExternalPath(resource);
            return path == null ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
        }

        /// <summary>
        /// Para instalar: el embebido o, si no, el de Payload\ con la huella comprobada. La comprobación y la
        /// extracción usan el mismo fichero abierto sin permiso de escritura para otros: no se puede cambiar entre medias.
        /// </summary>
        public static Stream OpenVerified(string resource)
        {
            var s = asm.GetManifestResourceStream(resource);
            if (s != null) return s;
            var path = ExternalPath(resource);
            if (path == null)
                throw new FileNotFoundException($"Falta el paquete de driver '{resource}': no va dentro del .exe ni está en la carpeta Payload\\ junto a él (ver Payload\\README.md).");
            var expected = PinnedHash(resource);
            if (expected == null)
                throw new InvalidDataException($"El paquete '{resource}' de Payload\\ no existía al compilar este .exe: no se puede comprobar y no se usa.");
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            try
            {
                string got;
                using (var sha = SHA256.Create()) got = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                if (!got.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"El paquete '{resource}' de Payload\\ no es el que acompañaba a este .exe (huella distinta): no se usa.");
                fs.Position = 0;
                return fs;
            }
            catch { fs.Dispose(); throw; }
        }
    }
}
