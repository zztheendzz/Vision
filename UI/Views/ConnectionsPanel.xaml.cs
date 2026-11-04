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
    /// <summary>
    /// Interaction logic for ConnectionsPanel.xaml
    /// </summary>
    public partial class ConnectionsPanel : UserControl
    {
        public ConnectionsPanel()
        {
            InitializeComponent();
        }
        // Mở cửa sổ cấu hình thiết bị (do cửa sổ chính quản lý để chỉ mở một cửa sổ)
        //private void OpenConfig_Click(object sender, RoutedEventArgs e)
        //{
        //    if (Window.GetWindow(this) is MainWindow main)
        //        main.OpenDeviceConfig();
        //}
    }
}
