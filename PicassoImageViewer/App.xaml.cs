using System;
using System.Threading;
using System.Windows;

namespace PicassoImageViewer
{
    public partial class App : Application
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        public static void Main()
        {
            _singleInstanceMutex = new Mutex(true, "7A779B0C-D327-4F38-8815-9C6360437CD4", out var createdNew);
            if (!createdNew)
            {
                Environment.Exit(0);
                return;
            }

            var app = new App();
            app.InitializeComponent();
            app.Run();

            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }
}
