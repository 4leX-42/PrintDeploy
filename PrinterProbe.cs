using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Consultas a una impresora: SNMP (sysDescr / hrDeviceDescr, nombre, ubicacion), IPP y web.
    /// Las usa NetworkScan.Check.
    /// </summary>
    public static class PrinterProbe
    {
        internal static string IppMakeAndModel(string ip)
        {
            foreach (var path in new[] { "/printer", "/ipp/print", "/ipp", "/" })
            {
                try
                {
                    var body = BuildIppRequest($"ipp://{ip}:631{path}");
                    var req = (HttpWebRequest)WebRequest.Create($"http://{ip}:631{path}");
                    req.Method = "POST";
                    req.ContentType = "application/ipp";
                    req.Timeout = 2500;
                    req.ReadWriteTimeout = 2500;
                    req.ContentLength = body.Length;
                    using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var ms = new System.IO.MemoryStream())
                    {
                        resp.GetResponseStream().CopyTo(ms);
                        var value = IppFindText(ms.ToArray(), "printer-make-and-model");
                        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                    }
                }
                catch (WebException ex) when (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.Timeout)
                {
                    return null; // puerto 631 cerrado: no insistir con otras rutas
                }
                catch { }
            }
            return null;
        }

        static byte[] BuildIppRequest(string uri)
        {
            var b = new List<byte> { 0x01, 0x01, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x01, 0x01 }; // IPP 1.1, Get-Printer-Attributes, id 1
            void attr(byte tag, string name, string value)
            {
                var n = Encoding.ASCII.GetBytes(name); var v = Encoding.UTF8.GetBytes(value);
                b.Add(tag); b.Add((byte)(n.Length >> 8)); b.Add((byte)n.Length); b.AddRange(n);
                b.Add((byte)(v.Length >> 8)); b.Add((byte)v.Length); b.AddRange(v);
            }
            attr(0x47, "attributes-charset", "utf-8");
            attr(0x48, "attributes-natural-language", "en");
            attr(0x45, "printer-uri", uri);
            attr(0x44, "requested-attributes", "printer-make-and-model");
            b.Add(0x03);
            return b.ToArray();
        }

        static string IppFindText(byte[] d, string name)
        {
            var n = Encoding.ASCII.GetBytes(name);
            for (int i = 3; i + n.Length + 2 < d.Length; i++)
            {
                if (d[i - 2] != 0 || d[i - 1] != n.Length) continue;
                bool hit = true;
                for (int k = 0; k < n.Length && hit; k++) hit = d[i + k] == n[k];
                if (!hit) continue;
                int p = i + n.Length;
                int len = (d[p] << 8) | d[p + 1];
                if (p + 2 + len <= d.Length) return Encoding.UTF8.GetString(d, p + 2, len);
            }
            return null;
        }

        // ---- SNMP GET minimo (community "public"): v2c y, si no responde, v1 ----
        static readonly byte[] OidSysDescr = { 0x2B, 6, 1, 2, 1, 1, 1, 0 };
        static readonly byte[] OidSysName = { 0x2B, 6, 1, 2, 1, 1, 5, 0 };
        static readonly byte[] OidSysLocation = { 0x2B, 6, 1, 2, 1, 1, 6, 0 };
        static readonly byte[] OidHrDeviceDescr = { 0x2B, 6, 1, 2, 1, 25, 3, 2, 1, 3, 1 };

        /// <summary>
        /// Una sola consulta: descripcion (sysDescr + hrDeviceDescr), nombre (sysName) y ubicacion (sysLocation).
        /// null si no hay SNMP.
        /// </summary>
        internal static (string descr, string model, string name, string location)? SnmpInfo(string ip, int timeoutMs, int attempts = 2)
        {
            // v2c devuelve "no existe" por cada OID que falte; v1 rechaza la consulta entera, asi que en v1 solo los obligatorios
            var v = SnmpGet(ip, 1, timeoutMs, attempts, OidSysDescr, OidSysName, OidSysLocation, OidHrDeviceDescr);
            if (v == null)
            {
                v = SnmpGet(ip, 0, timeoutMs, attempts, OidSysDescr, OidSysName, OidSysLocation);
                if (v == null) return null;
                v.Add("");
            }
            var descr = string.Join(" | ", new[] { v[0], v[3] }.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct());
            // modelo: hrDeviceDescr describe el aparato; sysDescr a veces es la tarjeta de red ("JETDIRECT") o lleva el firmware
            var model = v[3].Trim().Length > 0 ? v[3].Trim() : v[0].Trim();
            return (descr.Length > 0 ? descr : null, model, v[1].Trim(), v[2].Trim());
        }

        static readonly Dictionary<string, int> snmpVersion = new Dictionary<string, int>();
        static readonly byte[] OidPageCount = { 0x2B, 6, 1, 2, 1, 43, 10, 2, 1, 4, 1, 1 };        // prtMarkerLifeCount.1.1
        static readonly byte[] OidPrinterStatus = { 0x2B, 6, 1, 2, 1, 25, 3, 5, 1, 1, 1 };      // hrPrinterStatus.1
        static readonly byte[] OidPrinterErrors = { 0x2B, 6, 1, 2, 1, 25, 3, 5, 1, 2, 1 };      // hrPrinterDetectedErrorState.1

        /// <summary>
        /// Estado de la maquina por SNMP: paginas impresas en toda su vida (sube al imprimir de verdad), estado
        /// (3 en espera, 4 imprimiendo, 5 calentando) y averias detectadas como texto. null si no responde.
        /// </summary>
        internal static (long? pages, int? status, string errors)? PrinterCounters(string ip, int timeoutMs)
        {
            // v2c y, si no responde, v1 (las Canon solo contestan v1); se recuerda la que funciona con cada IP
            List<string> v = null;
            int known = -1;
            lock (snmpVersion) if (snmpVersion.TryGetValue(ip, out var k)) known = k;
            foreach (var version in known == 0 ? new[] { 0 } : new[] { 1, 0 })
            {
                v = SnmpGet(ip, version, timeoutMs, 1, true, OidPageCount, OidPrinterStatus, OidPrinterErrors);
                // v1 rechaza la consulta entera si falta un OID: entonces solo el contador
                if (v == null && version == 0)
                {
                    var only = SnmpGet(ip, 0, timeoutMs, 1, true, OidPageCount);
                    if (only != null) v = new List<string> { only[0], "", "" };
                }
                if (v != null) { lock (snmpVersion) snmpVersion[ip] = version; break; }
            }
            if (v == null) return null;
            long? pages = long.TryParse(v[0], out var pc) ? pc : (long?)null;
            int? status = int.TryParse(v[1], out var st) ? st : (int?)null;
            return (pages, status, ErrorText(v[2]));
        }

        /// <summary>hrPrinterDetectedErrorState: mapa de bits (RFC 3805) -> averias legibles.</summary>
        static string ErrorText(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "";
            int bits = 0;
            for (int i = 0; i < Math.Min(2, hex.Length / 2); i++) bits = bits << 8 | Convert.ToInt32(hex.Substring(i * 2, 2), 16);
            if (hex.Length < 4) bits <<= 8;
            var names = new[] { "poco papel", "sin papel", "poco tóner", "sin tóner", "tapa abierta", "atasco", "fuera de línea", "requiere servicio técnico",
                                "falta una bandeja de entrada", "falta la bandeja de salida", "falta un consumible", "bandeja de salida casi llena", "bandeja de salida llena", "bandeja de entrada vacía", "mantenimiento pendiente" };
            var found = new List<string>();
            for (int i = 0; i < names.Length; i++) if ((bits & (0x8000 >> i)) != 0) found.Add(names[i]);
            return string.Join(", ", found);
        }

        /// <summary>GET de los OID pedidos (version 0 = v1, 1 = v2c): un texto por OID, en orden ("" si no es texto). null si no responde.</summary>
        static List<string> SnmpGet(string ip, int version, int timeoutMs, int attempts, params byte[][] oids) =>
            SnmpGet(ip, version, timeoutMs, attempts, false, oids);

        /// <summary>raw: los OCTET STRING se devuelven en hexadecimal (para mapas de bits), no como texto.</summary>
        static List<string> SnmpGet(string ip, int version, int timeoutMs, int attempts, bool raw, params byte[][] oids)
        {
            try
            {
                var req = BuildGet("public", version, oids);
                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    var ep = new IPEndPoint(IPAddress.Parse(ip), 161);
                    for (int attempt = 0; attempt < attempts; attempt++)
                    {
                        udp.Send(req, req.Length, ep);
                        try
                        {
                            var remote = new IPEndPoint(IPAddress.Any, 0);
                            var vals = ParseResponse(udp.Receive(ref remote), raw);
                            if (vals != null && vals.Count == oids.Length) return vals;
                            return null;   // respuesta de error (v1: algun OID no existe)
                        }
                        catch (SocketException) { }
                    }
                }
            }
            catch { }
            return null;
        }

        static byte[] BuildGet(string community, int version, params byte[][] oids)
        {
            var varbinds = new List<byte>();
            foreach (var oid in oids)
                varbinds.AddRange(Tlv(0x30, Concat(Tlv(0x06, oid), new byte[] { 0x05, 0x00 })));
            var pdu = Tlv(0xA0, Concat(Int(new Random().Next(1, 0x7FFF)), Int(0), Int(0), Tlv(0x30, varbinds.ToArray())));
            return Tlv(0x30, Concat(Int(version), Tlv(0x04, Encoding.ASCII.GetBytes(community)), pdu));
        }

        static byte[] Int(int v)
        {
            var b = new List<byte>();
            do { b.Insert(0, (byte)(v & 0xFF)); v >>= 8; } while (v > 0);
            if ((b[0] & 0x80) != 0) b.Insert(0, 0);
            return Tlv(0x02, b.ToArray());
        }

        static byte[] Tlv(byte tag, byte[] value)
        {
            var r = new List<byte> { tag };
            int len = value.Length;
            if (len < 0x80) r.Add((byte)len);
            else if (len < 0x100) { r.Add(0x81); r.Add((byte)len); }
            else { r.Add(0x82); r.Add((byte)(len >> 8)); r.Add((byte)len); }
            r.AddRange(value);
            return r.ToArray();
        }

        static byte[] Concat(params byte[][] parts)
        {
            var r = new List<byte>();
            foreach (var p in parts) r.AddRange(p);
            return r.ToArray();
        }

        // muchas impresoras guardan el texto en Latin-1 ("8ª PLANTA"): UTF-8 si es valido, si no Windows-1252
        static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        static string Decode(byte[] d, int pos, int len)
        {
            try { return StrictUtf8.GetString(d, pos, len); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(d, pos, len); }
        }

        /// <summary>
        /// GetResponse: SEQ { version, community, A2 { request-id, error-status, error-index, SEQ { SEQ { OID, valor } ... } } }.
        /// Devuelve el valor de cada varbind en orden; null si hay error-status o el paquete no cuadra.
        /// </summary>
        static List<string> ParseResponse(byte[] d, bool raw)
        {
            int p = 0;
            if (!Header(d, ref p, out var tag, out _) || tag != 0x30) return null;
            Skip(d, ref p); Skip(d, ref p);                                       // version, community
            if (!Header(d, ref p, out tag, out _) || tag != 0xA2) return null;
            Skip(d, ref p);                                                       // request-id
            if (!Header(d, ref p, out tag, out var len) || tag != 0x02) return null;
            int status = 0;
            for (int i = 0; i < len; i++) status = (status << 8) | d[p + i];
            p += len;
            if (status != 0) return null;
            Skip(d, ref p);                                                       // error-index
            if (!Header(d, ref p, out tag, out len) || tag != 0x30) return null;
            int end = p + len;
            var vals = new List<string>();
            while (p < end && Header(d, ref p, out tag, out len))
            {
                int next = p + len;
                Skip(d, ref p);                                                   // OID
                string val = "";
                if (Header(d, ref p, out tag, out len))
                {
                    if (tag == 0x04) val = raw ? BitConverter.ToString(d, p, len).Replace("-", "") : Decode(d, p, len);
                    // INTEGER, Counter32, Gauge32, TimeTicks, Counter64: numero sin signo en big-endian
                    else if (tag == 0x02 || tag == 0x41 || tag == 0x42 || tag == 0x43 || tag == 0x46)
                    {
                        ulong n = 0;
                        for (int i = 0; i < len && i < 9; i++) n = (n << 8) | d[p + i];
                        val = n.ToString();
                    }
                }
                vals.Add(val);
                p = next;
            }
            return vals;
        }

        /// <summary>Lee etiqueta y longitud (BER); deja p al inicio del valor.</summary>
        static bool Header(byte[] d, ref int p, out byte tag, out int len)
        {
            tag = 0; len = 0;
            if (p + 2 > d.Length) return false;
            tag = d[p++];
            len = d[p++];
            if ((len & 0x80) != 0)
            {
                int n = len & 0x7F;
                if (n > 3 || p + n > d.Length) return false;
                len = 0;
                for (int i = 0; i < n; i++) len = (len << 8) | d[p++];
            }
            return p + len <= d.Length;
        }

        static void Skip(byte[] d, ref int p)
        {
            if (Header(d, ref p, out _, out var len)) p += len;
            else p = d.Length;
        }

        // ---- Web de la impresora (alternativa si SNMP no responde) ----
        internal static string HttpDescription(string ip)
        {
            foreach (var path in new[] { "/", "/web/guest/es/websys/webArch/mainFrame.cgi" })
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create($"http://{ip}{path}");
                    req.Timeout = 3000;
                    req.ReadWriteTimeout = 3000;
                    req.AllowAutoRedirect = true;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    {
                        string body = sr.ReadToEnd();
                        string server = resp.Headers["Server"] ?? "";
                        var m = Regex.Match(body, @"(RICOH|Savin|Lanier|Gestetner|nashuatec)[^<""]{0,40}", RegexOptions.IgnoreCase);
                        if (m.Success) return m.Value;
                        var title = Regex.Match(body, @"<title>([^<]{1,80})</title>", RegexOptions.IgnoreCase);
                        var parts = new List<string>();
                        if (server.Length > 0) parts.Add(server);
                        if (title.Success) parts.Add(title.Groups[1].Value.Trim());
                        if (parts.Count > 0) return string.Join(" | ", parts);
                    }
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse r)
                {
                    var server = r.Headers["Server"];
                    if (!string.IsNullOrEmpty(server)) return server;
                }
                catch { }
            }
            return null;
        }
    }
}
