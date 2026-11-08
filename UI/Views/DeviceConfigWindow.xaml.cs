
using System.Windows;


namespace UI.Views
{
    public partial class DeviceConfigWindow : Window
    {
        public DeviceConfigWindow()
        {
            InitializeComponent();

            // Đóng khi còn thay đổi chưa lưu: hỏi Lưu / Không lưu / Hủy
            Closing += (_, e) =>
            {
                if (DataContext is not ViewModels.Device.DeviceConfigViewModel vm || !vm.IsDirty) return;

                var answer = MessageBox.Show(this, "Cấu hình thiết bị có thay đổi chưa lưu. Lưu lại?", "Xác nhận",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel) e.Cancel = true;
                else if (answer == MessageBoxResult.Yes)
                {
                    vm.SaveCommand.Execute(null);
                    if (vm.IsDirty) e.Cancel = true;      // lưu không được (có lỗi): giữ cửa sổ để sửa
                }
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
