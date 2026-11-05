using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Graph;

namespace UI.ViewModels.ToolboxAndParam
{
    /// <summary>Ô chọn từ danh sách, dùng cho tham số kiểu enum (ví dụ Convention, Shape). Không phải gõ tên.</summary>
    public sealed class EnumParameterViewModel : ParameterViewModel
    {
        private string _selected;

        public IReadOnlyList<string> Choices { get; }

        public string Selected
        {
            get => _selected;
            set
            {
                if (value != null && SetProperty(ref _selected, value))
                    TrySetValue(value);          // ParamDef.Coerce đổi chữ thành enum
            }
        }

        public EnumParameterViewModel(VisionNode node, ParamDef definition) : base(node, definition)
        {
            Choices = Enum.GetNames(definition.Type);
            _selected = Read();
        }

        public override void Reload()
        {
            SetProperty(ref _selected, Read(), nameof(Selected));
            ErrorMessage = "";
        }

        private string Read() => Node.Parameters[Name]?.ToString() ?? Choices.FirstOrDefault() ?? "";
    }
}
