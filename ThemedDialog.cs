using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Ventana de aviso / confirmacion con el estilo de la app (sustituye a MessageBox):
    /// cabecera carbon con la marca dorada, texto, campos opcionales y botones.
    /// </summary>
    sealed class ThemedDialog : Form
    {
        readonly BufferedPanel head = new BufferedPanel();
        readonly FlatBtn ok, cancel;
        readonly string text;
        readonly Font fMark = Theme.Strong(8.25f), fTitle = Theme.Title(13f), fText = Theme.Sans(9.5f), fNote = Theme.Sans(9f);
        readonly LineField fUser, fPass;
        Rectangle textRect, noteRect;
        string note = "";
        bool noteIsError;
        Func<string, string, Task<string>> login;
        int S(int px) => Theme.S(px);

        ThemedDialog(string tag, string title, string text, string okText, string cancelText, bool credentials)
        {
            this.text = text;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Paper;
            Font = Theme.Sans(9.5f);
            KeyPreview = true;
            Text = "PrintDeploy";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            int w = S(500), pad = S(28), inner = w - 2 * pad;
            head.Dock = DockStyle.Top;
            head.Height = S(72);
            head.BackColor = Theme.Black;
            head.Paint += (s, e) =>
            {
                Theme.DrawMark(Theme.Crisp(e.Graphics), tag, fMark, new Point(pad, S(16)), S);
                TextRenderer.DrawText(Theme.Crisp(e.Graphics), title, fTitle, new Rectangle(pad, S(36), inner, S(26)), Theme.Paper, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            };
            head.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            Controls.Add(head);

            int y = head.Height + S(22);
            int textH = TextRenderer.MeasureText(text, fText, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            textRect = new Rectangle(pad, y, inner, textH);
            y += textH + S(18);

            if (credentials)
            {
                fUser = new LineField("Usuario", mono: false);
                fPass = new LineField("Contraseña", mono: false);
                fPass.Box.UseSystemPasswordChar = true;
                fUser.SetBounds(pad, y, inner, S(52));
                y += S(60);
                fPass.SetBounds(pad, y, inner, S(52));
                y += S(58);
                Controls.Add(fUser);
                Controls.Add(fPass);
                noteRect = new Rectangle(pad, y, inner, S(20));
                y += S(24);
            }

            ok = new FlatBtn(okText, primary: true);
            int bw = S(132), bh = S(38);
            ok.SetBounds(w - pad - bw, y + S(6), bw, bh);
            Controls.Add(ok);
            if (cancelText != null)
            {
                cancel = new FlatBtn(cancelText, primary: false);
                cancel.SetBounds(ok.Left - S(10) - bw, y + S(6), bw, bh);
                cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
                Controls.Add(cancel);
            }
            ok.Click += (s, e) => Accept();
            ClientSize = new Size(w, y + S(6) + bh + S(24));

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape && (cancel == null || cancel.Enabled)) { e.Handled = true; DialogResult = cancel != null ? DialogResult.Cancel : DialogResult.OK; }
                else if (e.KeyCode == Keys.Enter && !(ActiveControl is FlatBtn)) { e.Handled = e.SuppressKeyPress = true; Accept(); }
            };
            Shown += (s, e) => { if (fUser != null) (fUser.Box.Text.Length > 0 ? fPass : fUser).Box.Focus(); else ok.Focus(); };
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // sombra
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = Theme.Crisp(e.Graphics);
            TextRenderer.DrawText(g, text, fText, textRect, Theme.Black, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            if (note.Length > 0)
                TextRenderer.DrawText(g, note, fNote, noteRect, noteIsError ? Theme.Red : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(Theme.Black)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        void SetNote(string text, bool error) { note = text; noteIsError = error; Invalidate(noteRect); }

        async void Accept()
        {
            if (!ok.Enabled) return;
            if (login == null) { DialogResult = DialogResult.OK; return; }
            var user = fUser.Box.Text.Trim();
            if (user.Length == 0) { SetNote("Escriba el usuario.", true); fUser.Box.Focus(); return; }
            ok.Enabled = cancel.Enabled = fUser.Enabled = fPass.Enabled = false;
            SetNote("Conectando…", false);
            string error;
            try { error = await login(user, fPass.Box.Text); }
            catch (Exception ex) { error = ex.Message; }
            fPass.Box.Text = "";
            ok.Enabled = cancel.Enabled = fUser.Enabled = fPass.Enabled = true;
            if (error == null) { DialogResult = DialogResult.OK; return; }
            SetNote(error, true);
            fPass.Box.Focus();
        }

        static DialogResult Run(ThemedDialog d, IWin32Window owner)
        {
            if (owner == null) d.StartPosition = FormStartPosition.CenterScreen;
            using (d) return d.ShowDialog(owner);
        }

        /// <summary>Confirmacion: true si se pulsa el boton principal.</summary>
        public static bool Confirm(IWin32Window owner, string tag, string title, string text, string okText = "Instalar") =>
            Run(new ThemedDialog(tag, title, text, okText, "Cancelar", false), owner) == DialogResult.OK;

        /// <summary>Aviso con un solo boton.</summary>
        public static void Info(IWin32Window owner, string tag, string title, string text) =>
            Run(new ThemedDialog(tag, title, text, "Aceptar", null, false), owner);

        /// <summary>
        /// Pide usuario y contraseña y llama a login hasta que devuelva null (correcto) o se cancele.
        /// login devuelve el texto del error para mostrarlo sin cerrar la ventana.
        /// </summary>
        public static bool Credentials(IWin32Window owner, string host, string text, Func<string, string, Task<string>> login) =>
            Run(new ThemedDialog("Servidor", "Credenciales · " + host, text, "Conectar", "Cancelar", true) { login = login }, owner) == DialogResult.OK;
    }
}
