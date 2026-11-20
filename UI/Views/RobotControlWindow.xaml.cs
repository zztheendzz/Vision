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
using System.Windows.Shapes;

namespace UI.Views
{
    public partial class RobotControlWindow : Window
    {
        public RobotControlWindow()
        {
            InitializeComponent();
            Control.IsFloating = true;

            // Mất focus giữa lúc đang giữ nút nhích thì dừng robot
            Deactivated += (_, _) => (Control.DataContext as ViewModels.Device.DeviceViewModel)?.StopJog();
            Closing += (_, _) => (Control.DataContext as ViewModels.Device.DeviceViewModel)?.StopJog();
        }
    }
}
