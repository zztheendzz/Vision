
using Communication.Configuration;
using System.Windows;
using UI.View;
using UI.ViewModels;
using Vision.Graph;

namespace UI
{
 public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private DeviceConfigWindow? _deviceWindow;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel(new DialogService());
            DataContext = _viewModel;

            Closed += (_, _) => _viewModel.Dispose();   // ngắt thiết bị, giải phóng graph và tool
        }

        /// <summary>Mở cửa sổ cấu hình thiết bị. Đã mở rồi thì đưa lên phía trước, không mở thêm cửa sổ nữa.</summary>
        public void OpenDeviceConfig()
        {
            if (_deviceWindow != null)
            {
                _deviceWindow.Activate();
                return;
            }

            _deviceWindow = new DeviceConfigWindow
            {
                Owner = this,
                DataContext = _viewModel.Devices
            };
            _deviceWindow.Closed += (_, _) => _deviceWindow = null;
            _deviceWindow.Show();
        }

        /// <summary>Output tự cuộn xuống dòng mới nhất.</summary>
        private void OutputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox box) box.ScrollToEnd();
        }

        private void OpenDeviceConfig_Click(object sender, RoutedEventArgs e)
        {
            OpenDeviceConfig();
        }
    }
}
