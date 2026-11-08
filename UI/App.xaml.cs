using System.Configuration;
using System.Data;
using System.Windows;

namespace UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Bắt lỗi trên luồng UI chính của WPF
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Bắt lỗi trên các luồng ngầm (Background threads, Task)
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Lỗi UI: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Crash Error", MessageBoxButton.OK, MessageBoxImage.Error);
            // Nếu muốn ứng dụng tiếp tục chạy thay vì văng:
            // e.Handled = true; 
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            MessageBox.Show($"Lỗi toàn cục: {ex?.Message}\n\n{ex?.StackTrace}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
