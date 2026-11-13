using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Vision.Tools.Registry;

namespace UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Lỗi không bắt được ở đâu cũng ghi vào crash.log để biết nguyên nhân; lỗi ở luồng giao diện thì hiện hộp thoại và chạy tiếp
            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += (_, ev) => WriteCrashLog("AppDomain", ev.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, ev) => { WriteCrashLog("Task", ev.Exception); ev.SetObserved(); };

            base.OnStartup(e);
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string path = WriteCrashLog("UI", e.Exception);
            e.Handled = true;       // không sập chương trình vì một lỗi ở giao diện
            try
            {
                MessageBox.Show($"Có lỗi: {e.Exception.Message}\n\nChi tiết đã ghi vào:\n{path}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { }
        }

        private static string WriteCrashLog(string source, Exception? ex)
        {
            string path = "";
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VisionGraph");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "crash.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source})\r\n{ex}\r\n\r\n");
            }
            catch { }
            return path;
        }
    }
}
