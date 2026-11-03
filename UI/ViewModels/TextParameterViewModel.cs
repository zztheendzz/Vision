using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Vision.Core;
using Vision.Graph;

namespace UI.ViewModels
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
            _text = Convert.ToString(node.Parameters[definition.Name], CultureInfo.InvariantCulture) ?? "";
        }
    }
}
