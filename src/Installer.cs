using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TaskPad
{
    /// Per-user installer built into the exe (no admin, no extra tools):
    ///   %LOCALAPPDATA%\Programs\TaskPad\TaskPad.exe  + Start Menu / Desktop shortcuts, Explorer menu,
    ///   "Open with" entry and an Apps & Features uninstall entry. The folder is user-writable, so auto-update works.
    public static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskPad";
        const string AppKey = @"Software\Classes\Applications\TaskPad.exe";
        static readonly string[] Exts = { ".txt", ".md", ".todo", ".log" };

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TaskPad");
        public static string InstalledExe => Path.Combine(InstallDir, "TaskPad.exe");
        static string SelfPath => Assembly.GetExecutingAssembly().Location;
        static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "TaskPad.lnk");
        static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TaskPad.lnk");

        public static bool IsInstalled => File.Exists(InstalledExe);

        public static bool IsSetupLaunch() =>
            Path.GetFileNameWithoutExtension(SelfPath).IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 &&
            !string.Equals(Path.GetFullPath(SelfPath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        // ---------------- install ----------------

        public static int RunSetup()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Styles.Install(app.Resources);
            var w = new SetupWindow();
            return app.Run(w);
        }

        public sealed class Options
        {
            public bool Desktop, ContextMenu = true, OpenWith = true, Launch = true;
        }

        public static void Install(Options o)
        {
            Directory.CreateDirectory(InstallDir);
            var target = InstalledExe;
            if (!string.Equals(Path.GetFullPath(SelfPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(target))
                {
                    // upgrade over a running copy: a running exe can be renamed but not overwritten
                    var old = target + ".old";
                    try { File.Delete(old); } catch { }
                    File.Move(target, old);
                }
                File.Copy(SelfPath, target, true);
            }

            CreateShortcut(StartMenuLink, target);
            if (o.Desktop) CreateShortcut(DesktopLink, target);
            else TryDelete(DesktopLink);

            if (o.ContextMenu) Shell.Register(false, target);
            if (o.OpenWith) RegisterOpenWith(target);
            Shell.RegisterTaskFiles(target);   // .task notes always open in TaskPad

            var version = Updater.Short(Updater.Current);
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "TaskPad");
                k.SetValue("DisplayVersion", version);
                k.SetValue("Publisher", "ddatunashvili");
                k.SetValue("DisplayIcon", $"\"{target}\",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", $"\"{target}\" --uninstall");
                k.SetValue("QuietUninstallString", $"\"{target}\" --uninstall --quiet");
                k.SetValue("URLInfoAbout", "https://github.com/ddatunashvili/TaskPad");
                k.SetValue("HelpLink", "https://github.com/ddatunashvili/TaskPad/issues");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)(new FileInfo(target).Length / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// Lists TaskPad under "Open with" for text files (Windows doesn't let apps silently become the default).
        static void RegisterOpenWith(string exe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
            {
                k.SetValue("FriendlyAppName", "TaskPad");
                using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", $"\"{exe}\" \"%1\"");
                using (var t = k.CreateSubKey("SupportedTypes")) foreach (var e in Exts) t.SetValue(e, "");
                using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", $"\"{exe}\",0");
            }
            using (var p = Registry.CurrentUser.CreateSubKey(@"Software\Classes\TaskPad.Document"))
            {
                p.SetValue("", "Text document (TaskPad)");
                using (var i = p.CreateSubKey("DefaultIcon")) i.SetValue("", $"\"{exe}\",0");
                using (var c = p.CreateSubKey(@"shell\open\command")) c.SetValue("", $"\"{exe}\" \"%1\"");
            }
            foreach (var e in Exts)
                using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{e}\OpenWithProgids"))
                    k.SetValue("TaskPad.Document", new byte[0], RegistryValueKind.None);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
        }

        static void CreateShortcut(string link, string target)
        {
            // WScript.Shell COM via late binding (no extra references)
            var t = Type.GetTypeFromProgID("WScript.Shell");
            var shell = Activator.CreateInstance(t);
            try
            {
                var sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "TaskPad — plain-text task lists" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }

        // ---------------- uninstall ----------------

        public static int RunUninstall(bool quiet)
        {
            if (!quiet && MessageBox.Show("Remove TaskPad from this PC?\n\nYour notes are not touched. Settings (TaskPad.ini) in the install folder are removed.",
                    "Uninstall TaskPad", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return 1;

            // close other copies running from the install folder (portable copies elsewhere are left alone)
            foreach (var p in Process.GetProcessesByName("TaskPad"))
            {
                try
                {
                    if (p.Id == Process.GetCurrentProcess().Id) continue;
                    if (!string.Equals(p.MainModule.FileName, InstalledExe, StringComparison.OrdinalIgnoreCase)) continue;
                    p.CloseMainWindow();
                    p.WaitForExit(5000);
                }
                catch { }
            }

            TryDelete(StartMenuLink);
            TryDelete(DesktopLink);
            Shell.Unregister(false);
            Registry.CurrentUser.DeleteSubKeyTree(AppKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\TaskPad.Document", false);
            foreach (var e in Exts)
                using (var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{e}\OpenWithProgids", true))
                    try { k?.DeleteValue("TaskPad.Document", false); } catch { }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

            // the running exe can't delete itself: let a detached cmd remove the folder once we exit
            if (Directory.Exists(InstallDir))
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{InstallDir}\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });

            if (!quiet) MessageBox.Show("TaskPad was removed.", "TaskPad", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        // ---------------- UI ----------------

        sealed class SetupWindow : Window
        {
            readonly CheckBox _desktop = Check("Create a Desktop shortcut", false);
            readonly CheckBox _menu = Check("Add \"Open with TaskPad\" to the Explorer right-click menu", true);
            readonly CheckBox _openWith = Check("List TaskPad under \"Open with\" for .txt, .md, .todo, .log", true);
            readonly CheckBox _launch = Check("Start TaskPad when done", true);
            readonly TextBlock _status = new TextBlock { Foreground = Theme.FgDim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
            readonly Border _install, _cancel;
            bool _done;

            public SetupWindow()
            {
                Title = "TaskPad Setup";
                Width = 520;
                SizeToContent = SizeToContent.Height;
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Background = Theme.Bg;
                UseLayoutRounding = true;
                System.Windows.Documents.TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
                System.Windows.Documents.TextElement.SetForeground(this, Theme.Fg);
                try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/TaskPad;component/assets/taskpad.ico")); } catch { }
                SourceInitialized += (s, e) => TaskWindow.ApplyDarkTitleBar(this);

                var logo = new Image { Width = 56, Height = 56, Margin = new Thickness(0, 0, 16, 0) };
                try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/TaskPad;component/assets/taskpad.png")); } catch { }
                RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
                head.Children.Add(logo);
                var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                bool upgrade = IsInstalled;
                titles.Children.Add(new TextBlock { Text = (upgrade ? "Update TaskPad to " : "Install TaskPad ") + Updater.Short(Updater.Current), FontSize = 20, FontWeight = FontWeights.SemiBold });
                titles.Children.Add(new TextBlock { Text = "Plain-text task lists, the nice way.", Foreground = Theme.FgDim, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });
                head.Children.Add(titles);

                var where = new Border
                {
                    Background = Theme.Chrome, BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 14),
                    Child = new TextBlock
                    {
                        Text = "Installs for you only (no admin) to\n" + InstallDir,
                        Foreground = Theme.FgDim, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    },
                };

                _install = Button(upgrade ? "Update" : "Install", true, DoInstall);
                _cancel = Button("Cancel", false, Close);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
                buttons.Children.Add(_cancel);
                buttons.Children.Add(_install);

                var stack = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
                stack.Children.Add(head);
                stack.Children.Add(where);
                stack.Children.Add(_menu);
                stack.Children.Add(_openWith);
                stack.Children.Add(_desktop);
                stack.Children.Add(_launch);
                stack.Children.Add(_status);
                stack.Children.Add(buttons);
                Content = stack;
                KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter) DoInstall(); };
            }

            void DoInstall()
            {
                if (_done) { Close(); return; }
                var o = new Options { Desktop = _desktop.IsChecked == true, ContextMenu = _menu.IsChecked == true, OpenWith = _openWith.IsChecked == true, Launch = _launch.IsChecked == true };
                _install.IsEnabled = false;
                _status.Text = "Installing…";
                Task.Run(() => Install(o)).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _status.Foreground = Theme.Bang;
                        _status.Text = "Install failed: " + t.Exception?.InnerException?.Message;
                        _install.IsEnabled = true;
                        return;
                    }
                    _done = true;
                    _status.Foreground = Theme.Slash;
                    _status.Text = "✓ Installed. Find TaskPad in the Start menu" + (o.ContextMenu ? " or right-click any .txt file (Windows 11: Show more options)." : ".");
                    ((TextBlock)_install.Child).Text = "Done";
                    _install.IsEnabled = true;
                    _cancel.Visibility = Visibility.Collapsed;
                    if (o.Launch)
                    {
                        try { Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir }); } catch { }
                        Close();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }

            static CheckBox Check(string text, bool on)
            {
                var box = new Border
                {
                    Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.5),
                    BorderBrush = Theme.BoxOpen, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                };
                var mark = new TextBlock { Text = "✓", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
                box.Child = mark;
                var label = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(box);
                sp.Children.Add(label);
                var cb = new CheckBox { IsChecked = on, Margin = new Thickness(0, 5, 0, 5), Cursor = Cursors.Hand, Foreground = Theme.Fg };
                cb.Template = new ControlTemplate(typeof(CheckBox)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
                cb.Content = sp;
                void Upd()
                {
                    box.Background = cb.IsChecked == true ? Theme.Accent : Brushes.Transparent;
                    box.BorderBrush = cb.IsChecked == true ? Theme.Accent : Theme.BoxOpen;
                    mark.Visibility = cb.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
                }
                cb.Checked += (s, e) => Upd();
                cb.Unchecked += (s, e) => Upd();
                Upd();
                return cb;
            }

            static Border Button(string text, bool primary, Action click)
            {
                var b = new Border
                {
                    Padding = new Thickness(18, 7, 18, 8), Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(6),
                    Background = primary ? Theme.Accent : Theme.Hover,
                    Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = text, Foreground = primary ? Brushes.White : Theme.Fg, FontSize = 13, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal },
                };
                var bg = b.Background;
                b.MouseEnter += (s, e) => b.Opacity = 0.88;
                b.MouseLeave += (s, e) => b.Opacity = 1;
                b.MouseLeftButtonUp += (s, e) => { if (b.IsEnabled) click(); };
                b.IsEnabledChanged += (s, e) => b.Opacity = b.IsEnabled ? 1 : 0.5;
                return b;
            }
        }
    }
}
