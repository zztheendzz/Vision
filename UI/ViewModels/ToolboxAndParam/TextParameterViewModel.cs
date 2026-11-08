using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Vision.Core;
using Vision.Graph;
namespace UI.ViewModels.ToolboxAndParam
{
    /// <summary>Ô nhập chữ, dùng cho tham số kiểu chuỗi và kiểu số. Chữ được ép về đúng kiểu bởi ParamDef.Coerce.</summary>
    public sealed class TextParameterViewModel : ParameterViewModel
    {
        private string _text;

        public string Text
        {
            get => _text;
            set
            {
                if (SetProperty(ref _text, value))
                    TrySetValue(value);
            }
        }

        public TextParameterViewModel(VisionNode node, ParamDef definition) : base(node, definition)
        {
            // Số được hiển thị với dấu chấm thập phân (ví dụ 1.5), không phụ thuộc cài đặt ngôn ngữ của máy
            _text = Read();
        }

        public override void Reload()
        {
            SetProperty(ref _text, Read(), nameof(Text));      // gán trường: không đi qua setter nên không ghi ngược
            ErrorMessage = "";
        }

        private string Read()
            => Convert.ToString(Node.Parameters[Name], CultureInfo.InvariantCulture) ?? "";
    }
}
