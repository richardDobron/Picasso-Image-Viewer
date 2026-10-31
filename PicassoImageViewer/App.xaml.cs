using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace PicassoImageViewer
{
    public partial class App : Application
    {
        private const string InstanceId = "7A779B0C-D327-4F38-8815-9C6360437CD4";
        private const int ASFW_ANY = -1;

        private static readonly string PipeName = $"{InstanceId}-{Environment.UserName}";
        private static Mutex _singleInstanceMutex;

        [STAThread]
        public static void Main()
        {
            _singleInstanceMutex = new Mutex(true, $@"Local\{InstanceId}", out var createdNew);
            if (!createdNew)
            {
                ForwardArgumentsToRunningInstance();
                return;
            }

            var app = new App();
            app.InitializeComponent();
            StartInstanceListener(app);
            app.Run();

            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        private static void ForwardArgumentsToRunningInstance()
        {
            var args = Environment.GetCommandLineArgs();
            var path = args.Length > 1 ? Path.GetFullPath(args[1]) : string.Empty;

            try
            {
                AllowSetForegroundWindow(ASFW_ANY);

                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(2000);
                    var payload = Encoding.UTF8.GetBytes(path);
                    client.Write(payload, 0, payload.Length);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void StartInstanceListener(App app)
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In))
                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            server.WaitForConnection();
                            var path = reader.ReadToEnd();
                            app.Dispatcher.BeginInvoke(new Action(() => OnInstanceActivated(app, path)));
                        }
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(200);
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Single instance listener"
            };
            thread.Start();
        }

        private static void OnInstanceActivated(App app, string path)
        {
            if (!(app.MainWindow is MainWindow window))
                return;

            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Activate();

            if (!string.IsNullOrWhiteSpace(path))
                window.OpenImageFromExternalRequest(path);
        }

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
