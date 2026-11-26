using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("拾译")]
[assembly: AssemblyDescription("Windows 划词翻译")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace Shiyi
{
    internal static class Program
    {
        [STAThread]
        public static int Main()
        {
            bool first;
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Shiyi.Translator.Activate"))
            using (var instance = new Mutex(true, @"Local\Shiyi.Translator.Instance", out first))
            {
                if (!first) { wake.Set(); return 0; }
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiyi");
                    using (var desktop = new Desktop(new SettingsStore(directory)))
                    {
                        var watch = ThreadPool.RegisterWaitForSingleObject(wake, delegate {
                            if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(new Action(desktop.ShowMain));
                        }, null, Timeout.Infinite, false);
                        try { return app.Run(); } finally { watch.Unregister(null); }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("拾译无法启动。\n" + ex.GetBaseException().Message, "拾译", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
            }
        }
    }
}
