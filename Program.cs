using System;
using System.IO;
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
            {
                Crash.Log("ventana", e.Exception);
                ThemedDialog.Info(null, "Error", "Error inesperado", e.Exception.Message);
            };
            Application.Run(new MainForm());
        }
    }

    /// <summary>Registro de errores con su traza completa en %LocalAppData%\PrintDeploy\errores.log (se recorta a 256 KB).</summary>
    static class Crash
    {
        static readonly object gate = new object();
        public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintDeploy", "errores.log");

        public static void Log(string where, Exception ex)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 256 * 1024) File.Delete(FilePath);
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {ex}{Environment.NewLine}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
