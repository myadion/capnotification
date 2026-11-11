using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

[assembly: AssemblyTitle("Adion CapNotification")]
[assembly: AssemblyDescription("Affiche l'état des touches Verr. Maj, Verr. Num et Arrêt défil")]
[assembly: AssemblyCompany("Adion Technologies")]
[assembly: AssemblyProduct("Adion CapNotification")]
[assembly: AssemblyCopyright("Adion Technologies")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Adion.CapNotification
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string mode = "";
            bool silent = false;
            foreach (string a in args)
            {
                string l = a.ToLowerInvariant();
                if (l == "/silent") silent = true;
                else if (mode == "") mode = l;
            }

            Application.EnableVisualStyles();

            if (mode == "/install") return Installer.Install();
            if (mode == "/uninstall") return Installer.Uninstall(silent);
            if (mode != "/run" && !Installer.RunningInstalled()) return Installer.Setup(silent);

            // Une seule instance par session Windows.
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\AdionCapNotification", out first))
            {
                if (!first) return 0;
                Application.Run(new Agent());
            }
            return 0;
        }
    }

    // Textes choisis d'après la langue d'affichage de Windows.
    static class Texts
    {
        public const int CapsOn = 0, CapsOff = 1, NumOn = 2, NumOff = 3, ScrollOn = 4, ScrollOff = 5,
                         Installed = 6, Failed = 7, ConfirmUninstall = 8, Uninstalled = 9;

        static readonly string[] T = Pick();

        public static string Get(int id) { return T[id]; }

        static string[] Pick()
        {
            switch (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            {
                case "fr":
                    return new[] {
                        "Verr. Maj activé", "Verr. Maj désactivé",
                        "Verr. Num activé", "Verr. Num désactivé",
                        "Arrêt défil activé", "Arrêt défil désactivé",
                        "Adion CapNotification est installé.\nIl démarrera tout seul à chaque ouverture de session.",
                        "L'installation n'a pas pu se terminer.",
                        "Désinstaller Adion CapNotification ?",
                        "Adion CapNotification a été désinstallé." };
                case "es":
                    return new[] {
                        "Bloq Mayús activado", "Bloq Mayús desactivado",
                        "Bloq Num activado", "Bloq Num desactivado",
                        "Bloq Despl activado", "Bloq Despl desactivado",
                        "Adion CapNotification está instalado.\nSe iniciará automáticamente en cada inicio de sesión.",
                        "No se pudo completar la instalación.",
                        "¿Desinstalar Adion CapNotification?",
                        "Adion CapNotification se ha desinstalado." };
                case "de":
                    return new[] {
                        "Feststelltaste ein", "Feststelltaste aus",
                        "Num-Taste ein", "Num-Taste aus",
                        "Rollen-Taste ein", "Rollen-Taste aus",
                        "Adion CapNotification ist installiert.\nEs startet automatisch bei jeder Anmeldung.",
                        "Die Installation konnte nicht abgeschlossen werden.",
                        "Adion CapNotification deinstallieren?",
                        "Adion CapNotification wurde deinstalliert." };
                case "it":
                    return new[] {
                        "Blocco maiuscole attivato", "Blocco maiuscole disattivato",
                        "Blocco numerico attivato", "Blocco numerico disattivato",
                        "Blocco scorrimento attivato", "Blocco scorrimento disattivato",
                        "Adion CapNotification è installato.\nSi avvierà automaticamente a ogni accesso.",
                        "Impossibile completare l'installazione.",
                        "Disinstallare Adion CapNotification?",
                        "Adion CapNotification è stato disinstallato." };
                case "pt":
                    return new[] {
                        "Caps Lock ativado", "Caps Lock desativado",
                        "Num Lock ativado", "Num Lock desativado",
                        "Scroll Lock ativado", "Scroll Lock desativado",
                        "O Adion CapNotification está instalado.\nSerá iniciado automaticamente em cada início de sessão.",
                        "Não foi possível concluir a instalação.",
                        "Desinstalar o Adion CapNotification?",
                        "O Adion CapNotification foi desinstalado." };
                case "nl":
                    return new[] {
                        "Caps Lock aan", "Caps Lock uit",
                        "Num Lock aan", "Num Lock uit",
                        "Scroll Lock aan", "Scroll Lock uit",
                        "Adion CapNotification is geïnstalleerd.\nHet start automatisch bij elke aanmelding.",
                        "De installatie kon niet worden voltooid.",
                        "Adion CapNotification verwijderen?",
                        "Adion CapNotification is verwijderd." };
                default:
                    return new[] {
                        "Caps Lock on", "Caps Lock off",
                        "Num Lock on", "Num Lock off",
                        "Scroll Lock on", "Scroll Lock off",
                        "Adion CapNotification is installed.\nIt will start automatically at every sign-in.",
                        "The installation could not be completed.",
                        "Uninstall Adion CapNotification?",
                        "Adion CapNotification has been uninstalled." };
            }
        }
    }

    // Surveille l'état des trois touches et affiche la notification à chaque changement.
    sealed class Agent : ApplicationContext
    {
        const int VK_CAPITAL = 0x14, VK_NUMLOCK = 0x90, VK_SCROLL = 0x91;

        [DllImport("user32.dll")]
        static extern short GetKeyState(int vk);

        [DllImport("kernel32.dll")]
        static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        // Rend à Windows la mémoire dont le programme ne se sert pas (utile sur un vieux PC).
        public static void TrimMemory()
        {
            GC.Collect();
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
        }

        readonly Osd osd = new Osd();
        readonly WinTimer poll = new WinTimer();
        bool caps, num, scroll;

        [StructLayout(LayoutKind.Sequential)]
        struct TOGGLEKEYS { public uint Size, Flags; }

        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(uint action, uint param, ref TOGGLEKEYS data, uint winIni);

        // Coupe le bip « Touches bascules » de Windows et son raccourci (Verr. Num maintenue 5 s),
        // pour le compte en cours. Le bandeau le remplace.
        static void MuteToggleKeys()
        {
            const uint Get = 0x34, Set = 0x35, On = 0x1, Hotkey = 0x4;
            TOGGLEKEYS tk = new TOGGLEKEYS();
            tk.Size = (uint)Marshal.SizeOf(typeof(TOGGLEKEYS));
            if (!SystemParametersInfo(Get, tk.Size, ref tk, 0)) return;
            if ((tk.Flags & (On | Hotkey)) == 0) return;
            tk.Flags &= ~(On | Hotkey);
            SystemParametersInfo(Set, tk.Size, ref tk, 3); // enregistré et annoncé aux autres programmes
        }

        public Agent()
        {
            MuteToggleKeys();
            caps = IsOn(VK_CAPITAL);
            num = IsOn(VK_NUMLOCK);
            scroll = IsOn(VK_SCROLL);
            poll.Interval = 60;
            poll.Tick += delegate { Check(); };
            poll.Start();
            TrimMemory();
        }

        static bool IsOn(int vk) { return (GetKeyState(vk) & 1) != 0; }

        void Check()
        {
            bool c = IsOn(VK_CAPITAL), n = IsOn(VK_NUMLOCK), s = IsOn(VK_SCROLL);
            if (s != scroll) { scroll = s; osd.ShowState("↕", Texts.Get(s ? Texts.ScrollOn : Texts.ScrollOff), s); }
            if (n != num) { num = n; osd.ShowState("1", Texts.Get(n ? Texts.NumOn : Texts.NumOff), n); }
            if (c != caps) { caps = c; osd.ShowState("A", Texts.Get(c ? Texts.CapsOn : Texts.CapsOff), c); }
        }
    }

    // Carré flottant : ne prend jamais le focus, laisse passer les clics, s'efface seul.
    sealed class Osd : Form
    {
        const int WS_POPUP = unchecked((int)0x80000000);
        const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                  WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
        const int VisibleMs = 1500;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr dst, ref POINT pos, ref SIZE size,
            IntPtr src, ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hDc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

        readonly WinTimer timer = new WinTimer();
        Bitmap bitmap;
        Point origin;
        int shownAt;
        int alpha;

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            timer.Interval = 30;
            timer.Tick += delegate { Fade(); };
            IntPtr unused = Handle; // crée la fenêtre sans l'afficher
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style = WS_POPUP;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void ShowState(string glyph, string text, bool on)
        {
            float s;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f;

            int height = (int)(190 * s);

            using (Font font = new Font("Segoe UI", 17 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font glyphFont = new Font("Segoe UI", 36 * s, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                Size textSize = TextRenderer.MeasureText(text, font);
                int width = Math.Max(height, textSize.Width + (int)(36 * s));
                Color dark = Color.FromArgb(28, 28, 30);
                Color ink = on ? Color.White : Color.FromArgb(150, 150, 156);

                Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    using (GraphicsPath path = Rounded(new RectangleF(0, 0, width, height), 22 * s))
                    using (Brush back = new SolidBrush(Color.FromArgb(225, dark)))
                        g.FillPath(back, path);

                    // Cadenas : anse fermée quand la touche est active, levée et ouverte sinon.
                    float cx = width / 2f;
                    float bodyW = 78 * s, bodyH = 60 * s, bodyX = cx - bodyW / 2, bodyY = 70 * s;
                    float arc = 44 * s, left = cx - arc / 2, top = bodyY - 34 * s - (on ? 0 : 12 * s);
                    using (Pen pen = new Pen(ink, 9 * s))
                    using (GraphicsPath shackle = new GraphicsPath())
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        shackle.AddLine(left, bodyY + 4 * s, left, top + arc / 2);
                        shackle.AddArc(left, top, arc, arc, 180, 180);
                        shackle.AddLine(left + arc, top + arc / 2, left + arc, on ? bodyY + 4 * s : top + arc / 2 + 8 * s);
                        g.DrawPath(pen, shackle);
                    }

                    RectangleF body = new RectangleF(bodyX, bodyY, bodyW, bodyH);
                    using (GraphicsPath path = Rounded(body, 10 * s))
                    using (Brush fill = new SolidBrush(ink))
                        g.FillPath(fill, path);

                    using (StringFormat center = new StringFormat(StringFormatFlags.NoWrap))
                    using (Brush glyphBrush = new SolidBrush(dark))
                    using (Brush textBrush = new SolidBrush(on ? Color.White : Color.FromArgb(205, 205, 210)))
                    {
                        center.Alignment = StringAlignment.Center;
                        center.LineAlignment = StringAlignment.Center;
                        g.DrawString(glyph, glyphFont, glyphBrush, body, center);
                        RectangleF label = new RectangleF(0, bodyY + bodyH + 6 * s, width, height - bodyY - bodyH - 12 * s);
                        g.DrawString(text, font, textBrush, label, center);
                    }
                }

                if (bitmap != null) bitmap.Dispose();
                bitmap = bmp;

                // Sur l'écran de la fenêtre active, centré en bas, au-dessus de la barre des tâches.
                IntPtr fg = GetForegroundWindow();
                Rectangle area2 = (fg != IntPtr.Zero ? Screen.FromHandle(fg) : Screen.PrimaryScreen).WorkingArea;
                origin = new Point(area2.Left + (area2.Width - width) / 2, area2.Bottom - height - (int)(64 * s));
            }

            alpha = 255;
            shownAt = Environment.TickCount;
            Push();
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // au premier plan, sans focus
            timer.Start();
        }

        void Fade()
        {
            if (unchecked(Environment.TickCount - shownAt) < VisibleMs) return;
            alpha -= 32;
            if (alpha <= 0)
            {
                timer.Stop();
                ShowWindow(Handle, SW_HIDE);
                Agent.TrimMemory();
                return;
            }
            Push();
        }

        void Push()
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hBitmap = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hBitmap);
                POINT pos = new POINT { X = origin.X, Y = origin.Y };
                POINT src = new POINT();
                SIZE size = new SIZE { W = bitmap.Width, H = bitmap.Height };
                BLENDFUNCTION blend = new BLENDFUNCTION { SourceConstantAlpha = (byte)alpha, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(mem, old);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Installation dans Program Files, démarrage à l'ouverture de session, entrée de désinstallation.
    static class Installer
    {
        const string Title = "Adion CapNotification";
        const string ExeName = "AdionCapNotification.exe";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "AdionCapNotification";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AdionCapNotification";

        static string SelfPath { get { return Assembly.GetExecutingAssembly().Location; } }

        static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Adion\CapNotification"); }
        }

        static string TargetPath { get { return Path.Combine(InstallDir, ExeName); } }

        public static bool RunningInstalled()
        {
            return string.Equals(Path.GetFullPath(SelfPath), Path.GetFullPath(TargetPath), StringComparison.OrdinalIgnoreCase);
        }

        // Lancé par l'utilisateur : demande les droits, installe, puis démarre le programme.
        public static int Setup(bool silent)
        {
            int code = RunElevated("/install");
            if (code != 0)
            {
                if (!silent) MessageBox.Show(Texts.Get(Texts.Failed), Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return code;
            }
            Process.Start(TargetPath);
            if (!silent) MessageBox.Show(Texts.Get(Texts.Installed), Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // Partie qui tourne avec les droits d'administrateur.
        public static int Install()
        {
            try
            {
                KillOthers();
                Directory.CreateDirectory(InstallDir);
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Copy(SelfPath, TargetPath, true); break; }
                    catch (IOException) { if (attempt >= 10) throw; Thread.Sleep(300); }
                }

                using (RegistryKey run = Registry.LocalMachine.CreateSubKey(RunKey))
                    run.SetValue(RunValue, "\"" + TargetPath + "\"");

                using (RegistryKey un = Registry.LocalMachine.CreateSubKey(UninstallKey))
                {
                    un.SetValue("DisplayName", Title);
                    un.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                    un.SetValue("Publisher", "Adion Technologies");
                    un.SetValue("InstallLocation", InstallDir);
                    un.SetValue("DisplayIcon", TargetPath);
                    un.SetValue("UninstallString", "\"" + TargetPath + "\" /uninstall");
                    un.SetValue("QuietUninstallString", "\"" + TargetPath + "\" /uninstall /silent");
                    un.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    un.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    un.SetValue("EstimatedSize", (int)(new FileInfo(TargetPath).Length / 1024), RegistryValueKind.DWord);
                }
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        public static int Uninstall(bool silent)
        {
            if (!IsElevated()) return RunElevated(silent ? "/uninstall /silent" : "/uninstall");

            if (!silent && MessageBox.Show(Texts.Get(Texts.ConfirmUninstall), Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
            try
            {
                KillOthers();
                using (RegistryKey run = Registry.LocalMachine.OpenSubKey(RunKey, true))
                    if (run != null) run.DeleteValue(RunValue, false);
                Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);

                if (!silent) MessageBox.Show(Texts.Get(Texts.Uninstalled), Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Le programme ne peut pas s'effacer lui-même : une commande différée s'en charge.
                string dir = InstallDir, parent = Path.GetDirectoryName(dir);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\" & rmdir \"" + parent + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                Process.Start(psi);
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        static int RunElevated(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(SelfPath, arguments);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Win32Exception)
            {
                return 1223; // demande de droits refusée
            }
        }

        static bool IsElevated()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void KillOthers()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try
                {
                    if (p.Id == self) continue;
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
    }
}
