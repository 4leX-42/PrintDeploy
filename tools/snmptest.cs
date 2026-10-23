using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

public static class SnmpTest
{
    public static string Run(string exe, string[] ips)
    {
        var asm = Assembly.LoadFrom(exe);
        var t = asm.GetType("InstaladorImpresoras.PrinterProbe");
        var bf = BindingFlags.NonPublic | BindingFlags.Static;
        var get = t.GetMethods(bf).First(m => m.Name == "SnmpGet" && m.GetParameters().Length == 6);
        var oids = new Dictionary<string, byte[]>
        {
            { "descr", (byte[])t.GetField("OidSysDescr", bf).GetValue(null) },
            { "pages", (byte[])t.GetField("OidPageCount", bf).GetValue(null) },
            { "status", (byte[])t.GetField("OidPrinterStatus", bf).GetValue(null) },
            { "errors", (byte[])t.GetField("OidPrinterErrors", bf).GetValue(null) },
        };
        var sb = new StringBuilder();
        foreach (var ip in ips)
            foreach (var ver in new[] { 1, 0 })
                foreach (var kv in oids)
                {
                    var r = (List<string>)get.Invoke(null, new object[] { ip, ver, 1200, 1, true, new[] { kv.Value } });
                    sb.AppendLine(string.Format("{0} v{1} {2,-7}: {3}", ip, ver == 1 ? "2c" : "1", kv.Key, r == null ? "(null)" : r[0]));
                }
        return sb.ToString();
    }
}
