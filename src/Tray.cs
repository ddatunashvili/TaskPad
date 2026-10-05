using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace TaskPad
{
    /// Tray icon, Windows notifications for deadlines, start-with-Windows and the global quick-capture hotkey.
    /// With the tray on, closing the last window keeps TaskPad running quietly so reminders keep working.
    public static class Tray
    {
        static Forms.NotifyIcon _icon;
        static HwndSource _hotkeyWindow;
        static Agenda.Entry _balloonEntry;
        public static string HotkeyText { get; private set; } = "";

        public static bool Active => _icon != null;

        public static void Start()
        {
            if (!Workspace.Settings.TrayEnabled || _icon != null) return;
            try
            {
                _icon = new Forms.NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    Text = "TaskPad",
                    Visible = true,
                };
                _icon.DoubleClick += (s, e) => ShowMain();
                _icon.BalloonTipClicked += (s, e) =>
                {
                    var entry = _balloonEntry;
                    ShowMain();
                    if (entry != null) Agenda.Reveal(Workspace.LastActive, entry);
                };
                _icon.MouseUp += (s, e) => { if (e.Button == Forms.MouseButtons.Right) RebuildMenu(); };
                RebuildMenu();
                RegisterHotkey();
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Application.Current.Exit += (s, e) => Stop();
            }
            catch (Exception ex)
            {
                Log("tray start", ex);
                Stop();
                Application.Current.ShutdownMode = ShutdownMode.OnLastWindowClose;
            }
        }

        static void Log(string what, Exception ex)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TaskPad-error.log"), $"{DateTime.Now} {what}{Environment.NewLine}{ex}{Environment.NewLine}"); } catch { }
        }

        public static void Stop()
        {
            try { if (_icon != null) { _icon.Visible = false; _icon.Dispose(); } } catch { }
            _icon = null;
            try { if (_hotkeyWindow != null) { UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId); _hotkeyWindow.Dispose(); } } catch { }
            _hotkeyWindow = null;
        }

        static void RebuildMenu()
        {
            if (_icon == null) return;
            var m = new Forms.ContextMenuStrip();
            m.Items.Add("Open TaskPad", null, (s, e) => ShowMain());
            m.Items.Add("Quick capture" + (HotkeyText.Length > 0 ? "\t" + HotkeyText : ""), null, (s, e) => QuickCapture.Open());
            m.Items.Add("Agenda", null, (s, e) => { ShowMain(); Workspace.LastActive?.ShowAgenda(); });
            m.Items.Add(new Forms.ToolStripSeparator());
            var startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = StartsWithWindows };
            startup.Click += (s, e) => SetStartWithWindows(!StartsWithWindows);
            m.Items.Add(startup);
            m.Items.Add(new Forms.ToolStripSeparator());
            m.Items.Add("Quit TaskPad", null, (s, e) => Quit());
            var old = _icon.ContextMenuStrip;
            _icon.ContextMenuStrip = m;
            old?.Dispose();
        }

        /// Brings back the windows (restoring the session), or focuses the last one.
        public static void ShowMain()
        {
            var w = Workspace.LastActive != null && Workspace.LastActive.IsVisible ? Workspace.LastActive : Workspace.Windows.LastOrDefault(x => x.IsVisible);
            if (w == null)
            {
                if (!Session.Restore())
                {
                    w = new TaskWindow();
                    w.NewTab();
                    w.Show();
                }
                w = Workspace.LastActive ?? Workspace.Windows.LastOrDefault();
            }
            if (w == null) return;
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }

        public static void Quit() => Updater.QuitAll();

        /// Windows notification (falls back to an in-app toast without the tray).
        public static void Notify(string title, string text, Agenda.Entry entry = null)
        {
            _balloonEntry = entry;
            if (_icon != null)
            {
                try { _icon.ShowBalloonTip(10000, title, text, Forms.ToolTipIcon.Info); return; } catch { }
            }
            Workspace.LastActive?.Toast(title + " — " + text, seconds: 10);
        }

        // ---------------- start with Windows ----------------

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool StartsWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return (k?.GetValue("TaskPad") as string ?? "").IndexOf(System.Reflection.Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static void SetStartWithWindows(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("TaskPad", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --tray");
                    else k.DeleteValue("TaskPad", false);
                }
                Workspace.LastActive?.Toast(on ? "TaskPad starts with Windows (in the tray)" : "TaskPad no longer starts with Windows");
            }
            catch (Exception ex) { Workspace.LastActive?.Toast("Couldn't change startup: " + ex.Message); }
            RebuildMenu();
        }

        // ---------------- global hotkey ----------------

        const int HotkeyId = 0x7A51, WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_WIN = 8, MOD_NOREPEAT = 0x4000, VK_T = 0x54;
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        static void RegisterHotkey()
        {
            _hotkeyWindow = new HwndSource(new HwndSourceParameters("TaskPadHotkey") { Width = 0, Height = 0, WindowStyle = 0 });
            _hotkeyWindow.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == WM_HOTKEY && w.ToInt32() == HotkeyId) { QuickCapture.Open(); handled = true; }
                return IntPtr.Zero;
            });
            // first free combination wins (Win+Alt+T is taken by Xbox Game Bar on many PCs)
            var options = new (uint Mods, uint Key, string Text)[]
            {
                (MOD_WIN | MOD_ALT, 0x4E, "Win+Alt+N"),
                (MOD_CONTROL | MOD_ALT, 0x4E, "Ctrl+Alt+N"),
                (MOD_CONTROL | MOD_ALT, 0x20, "Ctrl+Alt+Space"),
                (MOD_WIN | MOD_ALT, VK_T, "Win+Alt+T"),
                (MOD_CONTROL | MOD_ALT | 4, VK_T, "Ctrl+Shift+Alt+T"),
            };
            HotkeyText = "";
            foreach (var o in options)
                if (RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, o.Mods | MOD_NOREPEAT, o.Key)) { HotkeyText = o.Text; break; }
            if (HotkeyText.Length == 0) Log("hotkey", new Exception("no free quick-capture shortcut, error " + Marshal.GetLastWin32Error()));
        }

        // ---------------- reminders ----------------

        static readonly HashSet<string> Notified = new HashSet<string>();
        static DateTime _lastTick = DateTime.Now;

        /// Called every 30 s: notifies deadlines that arrived (and timed ones 15 minutes ahead).
        public static void CheckReminders()
        {
            var now = DateTime.Now;
            bool rang = false;
            List<Agenda.Entry> items;
            try { items = Agenda.Collect(); } catch { return; }
            foreach (var e in items.Where(x => !x.Done))
            {
                var key = (e.Path ?? e.Name) + "|" + e.Text + "|" + e.When.ToString("o");
                if (e.When > _lastTick && e.When <= now && Notified.Add(key + "|due"))
                {
                    Notify("⏰ Due now", e.Text + "  ·  " + e.Name, e);
                    rang = true;
                }
                else if (e.HasTime && e.When - TimeSpan.FromMinutes(15) > _lastTick && e.When - TimeSpan.FromMinutes(15) <= now && Notified.Add(key + "|soon"))
                {
                    Notify("⏰ Due in 15 minutes", e.Text + "  ·  " + e.Name, e);
                    rang = true;
                }
            }
            if (rang) Sound.Play(Sound.Fx.Reminder);
            _lastTick = now;
        }
    }

    /// Spotlight-style box for capturing a task from anywhere (global hotkey). Adds to Inbox.task.
    public sealed class QuickCapture : Window
    {
        static QuickCapture _open;
        readonly TextBox _box;

        public static void Open()
        {
            if (_open != null) { _open.Activate(); return; }
            _open = new QuickCapture();
            _open.Show();
            _open.Activate();
        }

        QuickCapture()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 600;
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + wa.Height * 0.18;

            _box = new TextBox
            {
                FontFamily = new FontFamily("Segoe UI"), FontSize = 18, Foreground = Theme.Fg, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), CaretBrush = Theme.Accent, SelectionBrush = Theme.Accent, Padding = new Thickness(0, 4, 0, 4),
            };
            var hint = new TextBlock
            {
                Text = "Add a task…   e.g.  call mom due:+2h   ·   ! urgent   ·   #work",
                Foreground = Theme.FgFaint, FontFamily = new FontFamily("Segoe UI"), FontSize = 18, Margin = new Thickness(2, 4, 0, 4), IsHitTestVisible = false,
            };
            _box.TextChanged += (s, e) => hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var input = new Grid();
            input.Children.Add(hint);
            input.Children.Add(_box);

            var row = new DockPanel();
            var icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, Foreground = Theme.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(input);

            var foot = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11.5, Foreground = Theme.FgDim, Margin = new Thickness(30, 8, 0, 0) };
            foot.Inlines.Add(new System.Windows.Documents.Run("Enter"));
            foot.Inlines.Add(new System.Windows.Documents.Run(" add to " + System.IO.Path.GetFileName(Agenda.InboxPath) + "    ") { Foreground = Theme.FgFaint });
            foot.Inlines.Add(new System.Windows.Documents.Run("Ctrl+Enter"));
            foot.Inlines.Add(new System.Windows.Documents.Run(" add & open Inbox    ") { Foreground = Theme.FgFaint });
            foot.Inlines.Add(new System.Windows.Documents.Run("Esc"));
            foot.Inlines.Add(new System.Windows.Documents.Run(" cancel") { Foreground = Theme.FgFaint });

            var stack = new StackPanel();
            stack.Children.Add(row);
            stack.Children.Add(foot);
            Content = new Border
            {
                Background = Theme.Popup, BorderBrush = Theme.Accent, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 14, 18, 12), Child = stack,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 },
                Margin = new Thickness(16),
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    bool open = Keyboard.Modifiers == ModifierKeys.Control;
                    if (_box.Text.Trim().Length > 0) Add(_box.Text.Trim());
                    Close();
                    if (open) { Tray.ShowMain(); Workspace.LastActive?.OpenFile(Agenda.InboxPath); }
                }
            };
            Deactivated += (s, e) => { if (IsVisible) Close(); };
            Closed += (s, e) => _open = null;
            Loaded += (s, e) => { _box.Focus(); Keyboard.Focus(_box); };
        }

        /// Appends a task line to the Inbox note (creating it on first use).
        public static void Add(string text)
        {
            text = Due.ExpandText(text);
            var info = LineParser.Parse(text);
            bool hasMarker = info.Check != Check.None || info.Tag != TaskPad.Tag.None || info.NumberStart >= 0 || info.Heading > 0;
            var line = hasMarker ? text : "[ ] " + text;
            var path = Agenda.InboxPath;
            try
            {
                var doc = Workspace.FindByPath(path);
                if (doc != null)
                {
                    var d = doc.Document;
                    var nl = d.TextLength > 0 && !d.Text.EndsWith("\n") ? Environment.NewLine : "";
                    d.Insert(d.TextLength, nl + line + Environment.NewLine);
                    if (Workspace.Settings.AutoSave) doc.Save(null, false);
                }
                else if (!System.IO.File.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    var content = "# Inbox" + Environment.NewLine + Environment.NewLine + line + Environment.NewLine;
                    if (TaskFile.Is(path)) TaskFile.CreateNew(path, content); else System.IO.File.WriteAllText(path, content);
                }
                else
                {
                    var tmp = Doc.Open(path);
                    var d = tmp.Document;
                    var nl = d.TextLength > 0 && !d.Text.EndsWith("\n") ? Environment.NewLine : "";
                    d.Insert(d.TextLength, nl + line + Environment.NewLine);
                    tmp.Save(null, false);
                    tmp.Dispose();
                }
                foreach (var w in Workspace.Windows) w.Explorer?.PokeAgenda();
                if (Workspace.Windows.Any(w => w.IsActive)) Workspace.LastActive?.Toast("Added to Inbox: " + line);
                else Tray.Notify("Added to Inbox", line);
            }
            catch (Exception ex) { Tray.Notify("Couldn't add to Inbox", ex.Message); }
        }
    }
}
