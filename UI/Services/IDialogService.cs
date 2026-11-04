using System;
using System.Collections.Generic;
using System.Text;

namespace UI.Services
{
    /// <summary>ViewModel không gọi MessageBox trực tiếp, để test được mà không cần mở giao diện.</summary>
    public interface IDialogService
    {
        void ShowError(string message);

        /// <summary>Hỏi Có/Không. Trả true nếu người dùng chọn Có.</summary>
        bool Confirm(string message);

        void ShowInfo(string message);

        /// <summary>Hộp thoại chọn file để mở. Trả null nếu người dùng hủy. filter dạng "CSV|*.csv|Tất cả|*.*".</summary>
        string? PickOpenFile(string title, string filter);

        /// <summary>Hộp thoại chọn nơi lưu file. Trả null nếu người dùng hủy.</summary>
        string? PickSaveFile(string title, string filter, string defaultFileName);
    }
}
