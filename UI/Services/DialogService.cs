using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace UI.Services
{
    public sealed class DialogService : IDialogService
    {
        public void ShowError(string message)
            => MessageBox.Show(message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);

        public bool Confirm(string message)
            => MessageBox.Show(message, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;

        public bool? ConfirmYesNoCancel(string message)
            => MessageBox.Show(message, "Xác nhận", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null
            };

        public void ShowInfo(string message)
            => MessageBox.Show(message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);

        public string? PickOpenFile(string title, string filter)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? PickSaveFile(string title, string filter, string defaultFileName)
        {
            var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
