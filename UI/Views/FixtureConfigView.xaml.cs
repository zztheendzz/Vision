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
    /// <summary>Tab "Khuôn test": cài đặt chung gọn, bảng tất cả khuôn (sửa tại chỗ), bảng lệnh và bảng kết quả slot.</summary>
    public partial class FixtureConfigView : UserControl
    {
        public FixtureConfigView() => InitializeComponent();

        // Bảng nằm trong khung cuộn của cả trang: chuyển con lăn chuột cho khung ngoài để cuộn được
        private void ForwardWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not Control control || control.Parent is not UIElement parent) return;
            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            });
        }
    }
}
