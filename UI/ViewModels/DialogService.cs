using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace UI.ViewModels
{
    public sealed class DialogService : IDialogService
    {
        public void ShowError(string message)
            => MessageBox.Show(message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);

        public bool Confirm(string message)
            => MessageBox.Show(message, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;
    }
}
