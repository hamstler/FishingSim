// FishingSim's own Windows installer (no third-party installer tool: Inno Setup needs a paid licence for commercial use).
// Compiled with the C# compiler that ships with Windows (.NET Framework 4.x, present on every Windows 10/11):
//   powershell -File tools/release/build_installer.ps1
// setup.exe looks for the game's data parts next to itself (FishingSim-<ver>-Windows-czesc*.zip: GitHub takes files up
// to 2 GB), unpacks them into the chosen folder (Program Files by default), adds Start-menu and desktop shortcuts and an
// entry in "Apps / Uninstall a program". "setup.exe /uninstall" (copied as Odinstaluj.exe into the game's folder) removes it.
// C# 5 only (the compiler in Windows): no string interpolation, no ?. operator.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Fishing Sim - instalator")]
[assembly: AssemblyProduct("Fishing Sim")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Krystian Naskręski")]

namespace FishingSimSetup
{
    static class Info
    {
        public const string Name = "Fishing Sim";
        public const string Exe = "FishingSim.exe";
        public const string Publisher = "Krystian Naskręski";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FishingSim";
        public const string Uninstaller = "Odinstaluj.exe";
        public const long NeedBytes = 3300L * 1024 * 1024;   // the unpacked game (~2.7 GB) and a margin

        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static string StartMenuLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Name + ".lnk"); }
        }

        public static string DesktopLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Name + ".lnk"); }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool quiet = Array.IndexOf(args, "/quiet") >= 0;
            if (args.Length > 0 && args[0] == "/uninstall")
                return Uninstall.FromGameFolder(quiet);
            if (args.Length > 2 && args[0] == "/uninstall2")
            {
                int parent;
                int.TryParse(args[2], out parent);
                return Uninstall.Run(args[1], parent, quiet);
            }
            if (quiet)
            {
                // unattended install (tests): setup.exe /quiet [folder]
                string dir = DefaultFolder();
                for (int i = 0; i < args.Length; i++)
                    if (args[i] != "/quiet") dir = args[i];
                try
                {
                    Installer.Run(Installer.FindParts(), dir, true, null);
                    return 0;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message);
                    return 1;
                }
            }
            Application.Run(new SetupForm());
            return 0;
        }

        public static string DefaultFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FishingSim");
        }
    }

    // unpacking, shortcuts, registry
    static class Installer
    {
        public static string Here
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }

        public static List<string> FindParts()
        {
            List<string> parts = new List<string>(Directory.GetFiles(Here, "FishingSim-" + Info.Version + "-Windows-czesc*.zip"));
            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return parts;
        }

        // a part's entries start with the release folder ("FishingSim-0.12.0/..."): that prefix is dropped
        static string Relative(string entry)
        {
            entry = entry.Replace('\\', '/');
            int slash = entry.IndexOf('/');
            return slash < 0 ? entry : entry.Substring(slash + 1);
        }

        public delegate void Progress(double share, string text);

        public static void Run(List<string> parts, string dir, bool desktopLink, Progress progress)
        {
            if (parts.Count < 2)
                throw new Exception("Brakuje plików z danymi gry. Pobierz z wydania wszystkie pliki (setup.exe oraz "
                                    + "FishingSim-" + Info.Version + "-Windows-czesc1.zip i czesc2.zip) do jednego folderu.");
            dir = Path.GetFullPath(dir);
            string root = Path.GetPathRoot(dir);
            DriveInfo drive = new DriveInfo(root);
            long have = Directory.Exists(Path.Combine(dir, "FishingSim_Data")) ? Info.NeedBytes : 0;   // an update reuses its space
            if (drive.AvailableFreeSpace + have < Info.NeedBytes)
                throw new Exception("Za mało miejsca na dysku " + root + " (potrzeba ok. " + (Info.NeedBytes >> 30) + " GB).");

            long total = 0;
            foreach (string p in parts)
                using (ZipArchive z = ZipFile.OpenRead(p))
                    foreach (ZipArchiveEntry e in z.Entries)
                        total += e.Length;

            // an older version's files go first (renamed or dropped files would linger)
            string data = Path.Combine(dir, "FishingSim_Data");
            if (Directory.Exists(data))
            {
                if (progress != null) progress(0, "Usuwanie poprzedniej wersji...");
                Directory.Delete(data, true);
            }
            Directory.CreateDirectory(dir);

            long done = 0;
            byte[] buffer = new byte[1 << 20];
            foreach (string p in parts)
            {
                using (ZipArchive z = ZipFile.OpenRead(p))
                {
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        string rel = Relative(e.FullName);
                        if (rel.Length == 0) continue;
                        string target = Path.GetFullPath(Path.Combine(dir, rel));
                        if (!target.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                            continue;   // never outside the game's folder
                        if (rel.EndsWith("/"))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (Stream from = e.Open())
                        using (FileStream to = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                        {
                            int n;
                            while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                to.Write(buffer, 0, n);
                                done += n;
                                if (progress != null)
                                    progress(total > 0 ? (double)done / total : 1, "Kopiowanie: " + rel);
                            }
                        }
                    }
                }
            }

            if (progress != null) progress(1, "Tworzenie skrótów...");
            string exe = Path.Combine(dir, Info.Exe);
            if (!File.Exists(exe))
                throw new Exception("Po rozpakowaniu nie ma " + Info.Exe + " - pliki z danymi są uszkodzone, pobierz je ponownie.");
            string uninstaller = Path.Combine(dir, Info.Uninstaller);
            File.Copy(Assembly.GetExecutingAssembly().Location, uninstaller, true);
            Shortcut(Info.StartMenuLink, exe, dir);
            if (desktopLink)
                Shortcut(Info.DesktopLink, exe, dir);
            else if (File.Exists(Info.DesktopLink))
                File.Delete(Info.DesktopLink);

            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(Info.UninstallKey))
            {
                k.SetValue("DisplayName", Info.Name);
                k.SetValue("DisplayVersion", Info.Version);
                k.SetValue("Publisher", Info.Publisher);
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /quiet");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(total / 1024), RegistryValueKind.DWord);
            }
        }

        // a .lnk through the Windows Script Host (late bound: no COM reference needed)
        static void Shortcut(string link, string target, string workDir)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
            Type t = lnk.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }
    }

    static class Uninstall
    {
        // started from the game's folder: a copy in %TEMP% does the work (a running exe cannot delete its own folder)
        public static int FromGameFolder(bool quiet)
        {
            string dir = Installer.Here;
            if (!quiet && MessageBox.Show("Odinstalować " + Info.Name + " z folderu\n" + dir + "?\n\nZapisy gry i ustawienia "
                                          + "zostają w profilu użytkownika.", Info.Name, MessageBoxButtons.YesNo,
                                          MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;
            string temp = Path.Combine(Path.GetTempPath(), "FishingSimUninstall.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, temp, true);
            ProcessStartInfo psi = new ProcessStartInfo(temp, "/uninstall2 \"" + dir + "\" " + Process.GetCurrentProcess().Id
                                                              + (quiet ? " /quiet" : ""));
            psi.UseShellExecute = false;
            Process.Start(psi);
            return 0;
        }

        public static int Run(string dir, int parent, bool quiet)
        {
            // the copy in the game's folder must have exited before its exe can go
            try { Process.GetProcessById(parent).WaitForExit(15000); }
            catch (Exception) { }
            Thread.Sleep(300);
            string error = null;
            try
            {
                if (!File.Exists(Path.Combine(dir, Info.Exe)))
                    throw new Exception("W folderze " + dir + " nie ma gry.");
                string exePath = Path.GetFullPath(Path.Combine(dir, Info.Exe));
                foreach (Process g in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Info.Exe)))
                {
                    string running = null;
                    try { running = g.MainModule.FileName; }
                    catch (Exception) { }
                    if (running == null || string.Equals(Path.GetFullPath(running), exePath, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Gra jest uruchomiona - zamknij ją i spróbuj ponownie.");
                }
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        break;
                    }
                    catch (Exception e)
                    {
                        if (!(e is IOException || e is UnauthorizedAccessException) || attempt >= 10) throw;
                        Thread.Sleep(500);
                    }
                }
                if (File.Exists(Info.StartMenuLink)) File.Delete(Info.StartMenuLink);
                if (File.Exists(Info.DesktopLink)) File.Delete(Info.DesktopLink);
                Registry.LocalMachine.DeleteSubKeyTree(Info.UninstallKey, false);
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "FishingSimUninstall.log"), error ?? "OK " + dir); }
            catch (Exception) { }
            if (!quiet)
            {
                if (error == null)
                    MessageBox.Show(Info.Name + " został odinstalowany.", Info.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show("Nie udało się odinstalować: " + error, Info.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return error == null ? 0 : 1;
        }
    }

    // the wizard: one page (folder, desktop shortcut, licence), then progress, then "Run the game"
    class SetupForm : Form
    {
        readonly TextBox _folder = new TextBox();
        readonly Button _browse = new Button();
        readonly CheckBox _desktop = new CheckBox();
        readonly CheckBox _accept = new CheckBox();
        readonly CheckBox _launch = new CheckBox();
        readonly Button _install = new Button();
        readonly Button _cancel = new Button();
        readonly ProgressBar _bar = new ProgressBar();
        readonly Label _status = new Label();
        readonly Label _parts = new Label();
        List<string> _found;
        bool _busy, _finished;

        public SetupForm()
        {
            Text = "Instalator - " + Info.Name + " " + Info.Version;
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(620, 420);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch (Exception) { }

            Panel banner = new Panel();
            banner.Bounds = new Rectangle(0, 0, 620, 78);
            banner.BackColor = Color.FromArgb(24, 38, 40);
            Controls.Add(banner);
            Label title = new Label();
            title.Text = Info.Name;
            title.ForeColor = Color.FromArgb(232, 178, 74);
            title.Font = new Font("Segoe UI Semibold", 18f);
            title.Bounds = new Rectangle(20, 10, 560, 36);
            banner.Controls.Add(title);
            Label sub = new Label();
            sub.Text = "Symulator wędkarstwa - wersja " + Info.Version;
            sub.ForeColor = Color.FromArgb(205, 218, 220);
            sub.Bounds = new Rectangle(22, 48, 560, 22);
            banner.Controls.Add(sub);

            Add(new Label(), "Folder instalacji:", new Rectangle(20, 96, 580, 22));
            _folder.Bounds = new Rectangle(20, 120, 480, 26);
            _folder.Text = ExistingFolder() ?? Program.DefaultFolder();
            Controls.Add(_folder);
            _browse.Text = "Zmień...";
            _browse.Bounds = new Rectangle(508, 119, 92, 28);
            _browse.Click += delegate { Browse(); };
            Controls.Add(_browse);

            _parts.Bounds = new Rectangle(20, 154, 580, 40);
            Controls.Add(_parts);

            _desktop.Text = "Skrót na pulpicie";
            _desktop.Checked = true;
            _desktop.Bounds = new Rectangle(20, 198, 580, 24);
            Controls.Add(_desktop);
            _accept.Text = "Akceptuję licencję (gra jest własnością autora, wszelkie prawa zastrzeżone)";
            _accept.Bounds = new Rectangle(20, 224, 520, 24);
            _accept.CheckedChanged += delegate { Refresh(); };
            Controls.Add(_accept);
            LinkLabel licence = new LinkLabel();
            licence.Text = "Pokaż";
            licence.Bounds = new Rectangle(544, 226, 60, 22);
            licence.LinkClicked += delegate { ShowLicence(); };
            Controls.Add(licence);

            _bar.Bounds = new Rectangle(20, 290, 580, 22);
            _bar.Maximum = 1000;
            Controls.Add(_bar);
            _status.Bounds = new Rectangle(20, 316, 580, 40);
            _status.AutoEllipsis = true;
            Controls.Add(_status);

            _launch.Text = "Uruchom grę";
            _launch.Checked = true;
            _launch.Visible = false;
            _launch.Bounds = new Rectangle(20, 372, 200, 26);
            Controls.Add(_launch);
            _install.Text = "Zainstaluj";
            _install.Bounds = new Rectangle(404, 370, 100, 32);
            _install.Click += delegate { Install(); };
            Controls.Add(_install);
            AcceptButton = _install;
            _cancel.Text = "Anuluj";
            _cancel.Bounds = new Rectangle(510, 370, 90, 32);
            _cancel.Click += delegate { Close(); };
            Controls.Add(_cancel);

            _found = Installer.FindParts();
            if (_found.Count >= 2)
            {
                long size = 0;
                foreach (string p in _found) size += new FileInfo(p).Length;
                _parts.Text = "Znalezione pliki z danymi: " + _found.Count + " (" + (size >> 20) + " MB). Gra zajmie ok. 2,7 GB.";
            }
            else
            {
                _parts.ForeColor = Color.Firebrick;
                _parts.Text = "Brakuje plików z danymi. Pobierz z wydania setup.exe oraz oba pliki FishingSim-" + Info.Version
                              + "-Windows-czesc*.zip do jednego folderu i uruchom setup.exe ponownie.";
            }
            Refresh();
        }

        void Add(Label l, string text, Rectangle r)
        {
            l.Text = text;
            l.Bounds = r;
            Controls.Add(l);
        }

        static string ExistingFolder()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Info.UninstallKey))
                return k == null ? null : k.GetValue("InstallLocation") as string;
        }

        new void Refresh()
        {
            _install.Enabled = !_busy && (_finished || (_accept.Checked && _found.Count >= 2));
        }

        void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Wybierz folder, w którym zostanie zainstalowana gra";
                if (d.ShowDialog(this) == DialogResult.OK)
                    _folder.Text = d.SelectedPath.EndsWith("FishingSim", StringComparison.OrdinalIgnoreCase)
                        ? d.SelectedPath : Path.Combine(d.SelectedPath, "FishingSim");
            }
        }

        void ShowLicence()
        {
            string text = "Copyright (c) 2026 " + Info.Publisher + ". Wszelkie prawa zastrzeżone.";
            string file = Path.Combine(Installer.Here, "LICENSE.txt");
            if (File.Exists(file)) text = File.ReadAllText(file);
            MessageBox.Show(this, text, "Licencja", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Install()
        {
            if (_finished)
            {
                if (_launch.Checked)
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(_folder.Text, Info.Exe));
                    psi.WorkingDirectory = _folder.Text;
                    Process.Start(psi);
                }
                Close();
                return;
            }
            _busy = true;
            Refresh();
            _folder.Enabled = _browse.Enabled = _desktop.Enabled = _accept.Enabled = false;
            _cancel.Enabled = false;
            string dir = _folder.Text;
            bool desktop = _desktop.Checked;
            DateTime last = DateTime.MinValue;
            Thread worker = new Thread(delegate ()
            {
                string error = null;
                try
                {
                    Installer.Run(_found, dir, desktop, delegate (double share, string text)
                    {
                        if ((DateTime.UtcNow - last).TotalMilliseconds < 80 && share < 1) return;
                        last = DateTime.UtcNow;
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _bar.Value = (int)Math.Max(0, Math.Min(1000, share * 1000));
                            _status.Text = text;
                        });
                    });
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                BeginInvoke((MethodInvoker)delegate { Finished(error); });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void Finished(string error)
        {
            _busy = false;
            _cancel.Enabled = true;
            if (error != null)
            {
                _status.ForeColor = Color.Firebrick;
                _status.Text = "Błąd: " + error;
                _folder.Enabled = _browse.Enabled = _desktop.Enabled = _accept.Enabled = true;
                Refresh();
                return;
            }
            _finished = true;
            _bar.Value = 1000;
            _status.Text = "Gotowe. " + Info.Name + " jest zainstalowany w " + _folder.Text + ".";
            _install.Text = "Zakończ";
            _cancel.Visible = false;
            _launch.Visible = true;
            Refresh();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy) e.Cancel = true;   // half-unpacked game: let it finish
            base.OnFormClosing(e);
        }
    }
}
