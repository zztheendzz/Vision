using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace UI.ViewModels
{
    public class DialogService : IDialogService
    {
        public void ShowError(string message)
    => MessageBox.Show(message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);

    }
}
