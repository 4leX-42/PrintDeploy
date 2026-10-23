using System;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // el logo de la cabecera se pinta en segundo plano mientras se crea la ventana
            System.Threading.Tasks.Task.Run(() => Logo.Get(Theme.S(96)));
            Application.ThreadException += (s, e) =>
                ThemedDialog.Info(null, "Error", "Error inesperado", e.Exception.Message);
            Application.Run(new MainForm());
        }
    }
}
