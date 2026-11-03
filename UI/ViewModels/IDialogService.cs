using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels
{
    public interface IDialogService
    {
        void ShowError(string message);

        /// <summary>Hỏi Có/Không. Trả true nếu người dùng chọn Có.</summary>
        bool Confirm(string message);

    }
}
