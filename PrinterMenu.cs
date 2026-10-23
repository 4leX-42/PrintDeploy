using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Acceso rapido a una impresora instalada (icono junto al de "Dispositivos e impresoras"): menu con las
    /// impresoras del equipo; clic = su ventana de cola de Windows; submenu = propiedades / preferencias.
    /// </summary>
    static class PrinterMenu
    {
        public const string Glyph = "";   // OpenInNewWindow (Fluent / MDL2)

        public static void Show(Control anchor)
        {
            var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Theme.Sans(9.5f), Padding = new Padding(0, Theme.S(4), 0, Theme.S(4)) };
            string preferred = null;
            try { preferred = new PrinterSettings().PrinterName; } catch { }
            int count = 0;
            foreach (string name in PrinterSettings.InstalledPrinters)
            {
                var item = new ToolStripMenuItem(name == preferred ? name + "   ·   predeterminada" : name) { ForeColor = Theme.Paper, Padding = new Padding(Theme.S(6), Theme.S(3), Theme.S(6), Theme.S(3)) };
                item.Click += (s, e) => Open("/o", name);
                foreach (var (text, arg) in new[] { ("Cola de impresión", "/o"), ("Propiedades de la impresora", "/p"), ("Preferencias de impresión", "/e") })
                {
                    var sub = new ToolStripMenuItem(text) { ForeColor = Theme.Paper };
                    sub.Click += (s, e) => Open(arg, name);
                    item.DropDownItems.Add(sub);
                }
                ((ToolStripDropDownMenu)item.DropDown).ShowImageMargin = false;
                item.DropDown.Renderer = menu.Renderer;
                menu.Items.Add(item);
                count++;
            }
            if (count == 0) menu.Items.Add(new ToolStripMenuItem("Sin impresoras instaladas") { Enabled = false });
            menu.Closed += (s, e) => anchor.BeginInvoke((Action)menu.Dispose);
            menu.Show(anchor, new Point(anchor.Width - menu.PreferredSize.Width, anchor.Height));
        }

        /// <summary>printui: /o cola de impresion, /p propiedades, /e preferencias.</summary>
        static void Open(string arg, string name)
        {
            try { Process.Start("rundll32.exe", $"printui.dll,PrintUIEntry {arg} /n \"{name}\""); } catch { }
        }

        /// <summary>Menu carbon con seleccion dorada.</summary>
        sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new Colors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Theme.Crisp(e.Graphics);
                e.TextColor = !e.Item.Enabled ? Theme.Muted : e.Item.Selected ? Theme.Ink : Theme.Paper;
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = e.Item.Selected ? Theme.Ink : Theme.Gold;
                base.OnRenderArrow(e);
            }
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Black;
            public override Color MenuBorder => Theme.Gold;
            public override Color MenuItemBorder => Theme.Gold;
            public override Color MenuItemSelected => Theme.Gold;
            public override Color MenuItemSelectedGradientBegin => Theme.Gold;
            public override Color MenuItemSelectedGradientEnd => Theme.Gold;
            public override Color ImageMarginGradientBegin => Theme.Black;
            public override Color ImageMarginGradientMiddle => Theme.Black;
            public override Color ImageMarginGradientEnd => Theme.Black;
            public override Color SeparatorDark => Color.FromArgb(56, 56, 58);
        }
    }
}
