using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TaskPad
{
    /// Explorer context-menu integration. Writes to HKCU only, so no admin needed.
    public static class Shell
    {
        static readonly string[] Extensions = { ".txt", ".md", ".todo", ".log", ".task" };
        const string Verb = "TaskPad";
        const string NewVerb = "TaskPadNew";

        static string ExePath => System.Reflection.Assembly.GetExecutingAssembly().Location;

        public static bool IsRegistered
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\SystemFileAssociations\.txt\shell\{Verb}\command"))
                    return k != null && ((k.GetValue("") as string) ?? "").Contains(ExePath);
            }
        }

        public static void Register(bool showResult, string exePath = null)
        {
            var exe = exePath ?? ExePath;
            RegisterTaskFiles(exe);
            try
            {
                foreach (var ext in Extensions)
                {
                    using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\{Verb}"))
                    {
                        k.SetValue("", "Open with TaskPad");
                        k.SetValue("Icon", $"\"{exe}\",0");
                    }
                    using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\{Verb}\command"))
                        k.SetValue("", $"\"{exe}\" \"%1\"");
                }
                foreach (var root in new[] { @"Directory\Background", "Directory" })
                {
                    var arg = root == "Directory" ? "%1" : "%V";
                    using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{root}\shell\{NewVerb}"))
                    {
                        k.SetValue("", "New TaskPad note (.task)");
                        k.SetValue("Icon", $"\"{exe}\",0");
                    }
                    using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{root}\shell\{NewVerb}\command"))
                        k.SetValue("", $"\"{exe}\" --new \"{arg}\"");
                }
                if (showResult)
                    MessageBox.Show("Context menu added.\n\nRight-click a .txt / .md / .todo / .log file -> \"Open with TaskPad\".\nRight-click a folder background -> \"New task list (TaskPad)\".\n\nOn Windows 11 these are under \"Show more options\" (Shift+F10).\nIf you move TaskPad.exe, register again.",
                        "TaskPad", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not register: " + ex.Message, "TaskPad", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        const string TaskProgId = "TaskPad.Task";

        /// Makes .task files open in TaskPad (our own extension, so we may be the default),
        /// with the TaskPad icon and an Explorer "New > TaskPad note" entry.
        public static void RegisterTaskFiles(string exePath = null)
        {
            var exe = exePath ?? ExePath;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.task"))
                {
                    k.SetValue("", TaskProgId);
                    k.SetValue("Content Type", "application/x-taskpad");
                    k.SetValue("PerceivedType", "document");
                    using (var n = k.CreateSubKey("ShellNew")) n.SetValue("NullFile", "");
                    using (var o = k.CreateSubKey("OpenWithProgids")) o.SetValue(TaskProgId, new byte[0], RegistryValueKind.None);
                }
                using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{TaskProgId}"))
                {
                    k.SetValue("", "TaskPad note");
                    k.SetValue("FriendlyTypeName", "TaskPad note");
                    using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", $"\"{exe}\",0");
                    using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", $"\"{exe}\" \"%1\"");
                }
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        /// First run of any copy: claim .task if nothing handles it yet (never steals it from another TaskPad).
        public static void EnsureTaskFiles()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{TaskProgId}\shell\open\command"))
                {
                    var cmd = k?.GetValue("") as string;
                    if (cmd != null && File.Exists(cmd.Split('"')[1])) return;
                }
            }
            catch { }
            RegisterTaskFiles();
        }

        static void UnregisterTaskFiles()
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.task", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{TaskProgId}", false);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        public static void Unregister(bool showResult)
        {
            UnregisterTaskFiles();
            foreach (var ext in Extensions)
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{ext}\shell\{Verb}", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\Background\shell\{NewVerb}", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\shell\{NewVerb}", false);
            if (showResult)
                MessageBox.Show("Context menu removed.", "TaskPad", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static string CreateNewTaskFile(string dir)
        {
            dir = dir.Trim('"');
            if (!Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path = Path.Combine(dir, "tasks" + TaskFile.Ext);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"tasks ({i}){TaskFile.Ext}");
            TaskFile.CreateNew(path, $"# Tasks  {DateTime.Now:yyyy-MM-dd}\r\n\r\n[ ] ");
            return path;
        }
    }
}
