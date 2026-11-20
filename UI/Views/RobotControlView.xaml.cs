using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace UI.Views
{
    public partial class RobotControlView : UserControl
    {
        public RobotControlView()
        {
            InitializeComponent();
            Unloaded += (_, _) => (DataContext as ViewModels.Device.DeviceViewModel)?.StopJog();
        }

        /// <summary>Đặt true ở cửa sổ thả nổi: ẩn nút "Mở cửa sổ riêng".</summary>
        public bool IsFloating
        {
            set => FloatButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Float_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow main)
                main.OpenRobotControl(DataContext as ViewModels.Device.DeviceViewModel);
        }

        // Nút nhích: nhấn = bắt đầu, thả nút / mất chuột / mất focus / đóng cửa sổ = dừng
        private void Jog_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Button { IsEnabled: true } button || button.DataContext is not ViewModels.Device.DeviceViewModel vm) return;
            button.CaptureMouse();                // thả chuột ngoài nút vẫn nhận được sự kiện thả
            vm.StartJog(button.Tag as string);
            e.Handled = true;
        }

        private void Jog_Up(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is not Button button) return;
            if (button.DataContext is ViewModels.Device.DeviceViewModel vm) vm.StopJog();
            if (button.IsMouseCaptured) button.ReleaseMouseCapture();
        }
    }
}
