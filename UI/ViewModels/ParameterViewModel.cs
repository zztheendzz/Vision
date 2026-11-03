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

        /// <summary>Đặt giá trị vào node. Sai thì hiện lỗi và node vẫn giữ giá trị cũ.</summary>
        protected bool TrySetValue(object? value)
        {
            try
            {
                Node.SetParameter(Name, value);
                ErrorMessage = "";
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
