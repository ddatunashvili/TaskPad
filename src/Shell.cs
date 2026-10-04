using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TaskPad
{
    /// Explorer context-menu integration. Writes to HKCU only, so no admin needed.
    public static class Shell
    {
        static readonly string[] Extensions = { ".txt", ".md", ".todo", ".log" };
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

        public static void Register(bool showResult)
        {
            var exe = ExePath;
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
                        k.SetValue("", "New task list (TaskPad)");
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

        public static void Unregister(bool showResult)
        {
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
            var path = Path.Combine(dir, "tasks.txt");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"tasks ({i}).txt");
            File.WriteAllText(path, $"# Tasks  {DateTime.Now:yyyy-MM-dd}\r\n\r\n[ ] ");
            return path;
        }
    }
}
