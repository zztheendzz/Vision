
using System.Windows;
using UI.View;
using UI.ViewModels;

namespace UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private DeviceConfigWindow? _deviceWindow;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel(new DialogService());
            DataContext = _viewModel;

            Closed += (_, _) => _viewModel.Dispose();
        }

        /// <summary>
        /// Mở cửa sổ cấu hình thiết bị.
        /// Đã mở rồi thì đưa lên phía trước.
        /// </summary>
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
                //DataContext = _viewModel.Devices
            };

            _deviceWindow.Closed += (_, _) => _deviceWindow = null;
            _deviceWindow.Show();
        }

        public void OpenDeviceConfig_Click(object sender, RoutedEventArgs e)
        {
            OpenDeviceConfig();
        }
    }
}
