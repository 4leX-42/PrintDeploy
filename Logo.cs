using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Emblema de PrintDeploy (ojo sobre impresora con ondas, dorado sobre transparente) para la cabecera de inicio.
    /// Viene embebido como logo.png (512 px) y se reduce con bicubico al tamaño pedido; cache por tamaño.
    /// El icono del .exe es app.ico (version en placa negra del mismo diseño).
    /// </summary>
    static class Logo
    {
        static readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();

        public static Bitmap Get(int size)
        {
            lock (cache)   // tambien se precarga en segundo plano al arrancar
            {
                if (!cache.TryGetValue(size, out var bmp)) cache[size] = bmp = Render(size);
                return bmp;
            }
        }

        static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var s = typeof(Logo).Assembly.GetManifestResourceStream("logo.png"))
            {
                if (s == null) return bmp;   // compilado sin el recurso: cabecera sin emblema
                using (var src = Image.FromStream(s))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, size, size));
                }
            }
            return bmp;
        }
    }
}
