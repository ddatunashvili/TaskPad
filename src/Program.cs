using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace TaskPad
{
    public static class Program
    {
        static readonly string InstanceId = "TaskPad-" + Environment.UserName;
        static Mutex _mutex;
        public static bool Standalone;

        /// Lets a freshly started copy (after an update) become the primary instance.
        public static void ReleaseSingleInstance()
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            _mutex = null;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            // AvalonEdit lives inside the exe as a resource -> load it from there.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
            return Run(args);
        }

        static Assembly ResolveEmbedded(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name).Name + ".dll";
            using (var s = typeof(Program).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var buf = new byte[s.Length];
                s.Read(buf, 0, buf.Length);
                return Assembly.Load(buf);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Run(string[] args)
        {
            var files = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                switch (a.ToLowerInvariant())
                {
                    case "--register":
                        Shell.Register(true);
                        return 0;
                    case "--unregister":
                        Shell.Unregister(true);
                        return 0;
                    case "--standalone":
                        break;
                    case "--new":
                        var dir = i + 1 < args.Length ? args[++i] : Environment.CurrentDirectory;
                        files.Add(Shell.CreateNewTaskFile(dir));
                        break;
                    default:
                        files.Add(Path.GetFullPath(a));
                        break;
                }
            }

            // --standalone: skip the single-instance handoff (used for demos / testing)
            bool standalone = Array.Exists(args, a => a.Equals("--standalone", StringComparison.OrdinalIgnoreCase));
            Standalone = standalone;
            if (!standalone)
            {
                _mutex = new Mutex(true, InstanceId, out bool first);
                if (!first && SendToRunning(files)) return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                // keep the app (and unsaved text) alive; log for bug reports
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "TaskPad-error.log"), $"{DateTime.Now}\r\n{e.Exception}\r\n"); } catch { }
                e.Handled = true;
            };
            Styles.Install(app.Resources);
            Workspace.Settings = Settings.Load();
            SmartEditing.IndentSize = () => Workspace.Settings.IndentSize;

            var win = new TaskWindow();
            foreach (var f in files) win.OpenFile(f);
            if (win.TabCount == 0) win.NewTab();
            if (!standalone) StartPipeServer();
            win.Show();
            Updater.CleanupOld();
            if (!standalone) Updater.Start();
            return app.Run();
        }

        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

        static bool SendToRunning(List<string> files)
        {
            try
            {
                AllowSetForegroundWindow(-1);
                using (var c = new NamedPipeClientStream(".", InstanceId, PipeDirection.Out))
                {
                    c.Connect(1500);
                    var data = Encoding.UTF8.GetBytes(string.Join("\n", files));
                    c.Write(data, 0, data.Length);
                }
                return true;
            }
            catch { return false; }
        }

        static void StartPipeServer()
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var s = new NamedPipeServerStream(InstanceId, PipeDirection.In))
                        {
                            s.WaitForConnection();
                            string msg;
                            using (var r = new StreamReader(s, Encoding.UTF8)) msg = r.ReadToEnd();
                            var paths = msg.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                var w = Workspace.LastActive ?? (Workspace.Windows.Count > 0 ? Workspace.Windows[0] : null);
                                if (w == null) { w = new TaskWindow(); w.Show(); }
                                w.ReceiveFromOtherInstance(paths);
                            }));
                        }
                    }
                    catch { Thread.Sleep(200); }
                }
            }) { IsBackground = true, Name = "TaskPad pipe" };
            t.Start();
        }
    }
}
