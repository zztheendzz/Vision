
using Communication.Configuration;
using System.Windows;
using UI.Services;
using UI.ViewModels;
using UI.Views;


namespace UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private DeviceConfigWindow? _deviceWindow;
        private AutoCalibrationWindow? _autoCalWindow;
        private RobotControlWindow? _robotWindow;
        private PlcMapWindow? _plcMapWindow;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel(new DialogService());
            DataContext = _viewModel;

            // Còn thay đổi chưa lưu thì hỏi; chọn Hủy thì không đóng
            Closing += (_, e) =>
            {
                if (!_viewModel.ConfirmDiscardOrSave()) e.Cancel = true;
            };

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

        /// <summary>Mở cửa sổ thả nổi "Robot + camera" (chỉ một cửa sổ). Không khóa cửa sổ chính, kéo đặt cạnh ảnh được.</summary>
        public void OpenRobotControl(ViewModels.Device.DeviceViewModel? robot = null)
        {
            if (_robotWindow != null)
            {
                _robotWindow.Activate();
                return;
            }

            _robotWindow = new RobotControlWindow
            {
                Owner = this,
                DataContext = new ViewModels.Device.RobotControlViewModel(_viewModel.Devices, robot)
            };
            _robotWindow.Closed += (_, _) => _robotWindow = null;
            _robotWindow.Show();
        }

        private void OpenRobotControl_Click(object sender, RoutedEventArgs e) => OpenRobotControl();

        /// <summary>Output tự cuộn xuống dòng mới nhất.</summary>
        private void OutputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox box) box.ScrollToEnd();
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private void OpenDeviceConfig_Click(object sender, RoutedEventArgs e)
        {
            OpenDeviceConfig();
        }

        /// <summary>Mở cửa sổ bản đồ tín hiệu PLC (chỉ một cửa sổ).</summary>
        private void OpenPlcMap_Click(object sender, RoutedEventArgs e)
        {
            if (_plcMapWindow != null)
            {
                _plcMapWindow.Activate();
                return;
            }

            _plcMapWindow = new PlcMapWindow
            {
                Owner = this,
                DataContext = _viewModel.PlcMap
            };
            _plcMapWindow.Closed += (_, _) => _plcMapWindow = null;
            _plcMapWindow.Show();
        }

        /// <summary>Mở cửa sổ hiệu chuẩn tự động / chụp tại vị trí cố định (chỉ một cửa sổ).</summary>
        private void OpenAutoCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (_autoCalWindow != null)
            {
                _autoCalWindow.Activate();
                return;
            }

            _autoCalWindow = new AutoCalibrationWindow
            {
                Owner = this,
                DataContext = _viewModel.AutoCal
            };
            _autoCalWindow.Closed += (_, _) => _autoCalWindow = null;
            _autoCalWindow.Show();
        }
    }
}
