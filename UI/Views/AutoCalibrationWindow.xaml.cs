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
using UI.ViewModels.RunAndDisplay;
namespace UI.Views
{
    public partial class AutoCalibrationWindow : Window
    {
        public AutoCalibrationWindow()
        {
            InitializeComponent();

            // Robot đang tự chạy thì không cho đóng cửa sổ: phải bấm Dừng trước
            Closing += (_, e) =>
            {
                if (DataContext is ViewModels.RunAndDisplay.AutoCalibrationViewModel { CanClose: false })
                {
                    MessageBox.Show(this, "Robot đang chạy. Bấm Dừng rồi mới đóng cửa sổ.", "Đang chạy",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    e.Cancel = true;
                }
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Nhật ký tự cuộn xuống dòng mới nhất.</summary>
        private void Log_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox box) box.ScrollToEnd();
        }
    }
}
