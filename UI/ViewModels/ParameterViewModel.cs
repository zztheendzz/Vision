using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Graph;

namespace UI.ViewModels
{
    /// <summary>
    /// Một tham số của node trên bảng thuộc tính. Lớp gốc cho từng kiểu ô nhập (chữ/số, bật/tắt).
    /// Giá trị người dùng nhập được đưa vào node qua node.SetParameter (có kiểm tra kiểu và khoảng).
    /// </summary>
    public abstract partial class ParameterViewModel : ViewModelBase
    {
        protected VisionNode Node { get; }

        /// <summary>Báo khi người dùng nhập một giá trị hợp lệ và đã được ghi vào node.</summary>
        public event EventHandler? ValueApplied;

        public string Name { get; }
        public string Description { get; }

        /// <summary>Rỗng nếu giá trị hợp lệ. Có chữ thì hiện màu đỏ dưới ô nhập.</summary>
        [ObservableProperty]
        private string _errorMessage = "";

        protected ParameterViewModel(VisionNode node, ParamDef definition)
        {
            Node = node;
            Name = definition.Name;
            Description = definition.Description ?? "";
        }

        /// <summary>Đọc lại giá trị từ node (khi node bị đổi từ nơi khác, ví dụ kéo ROI trên ảnh). Không ghi ngược vào node.</summary>
        public abstract void Reload();

        /// <summary>Đặt giá trị vào node. Sai thì hiện lỗi và node vẫn giữ giá trị cũ.</summary>
        protected bool TrySetValue(object? value)
        {
            try
            {
                Node.SetParameter(Name, value);
                ErrorMessage = "";
                ValueApplied?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
                return false;
            }
        }
    }
}
