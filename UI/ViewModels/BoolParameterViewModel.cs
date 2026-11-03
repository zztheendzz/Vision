using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Graph;

namespace UI.ViewModels
{
    /// <summary>Ô tích bật/tắt, dùng cho tham số kiểu bool.</summary>
    public sealed class BoolParameterViewModel : ParameterViewModel
    {
        private bool _isChecked;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value))
                    TrySetValue(value);
            }
        }

        public BoolParameterViewModel(VisionNode node, ParamDef definition) : base(node, definition)
        {
            _isChecked = node.Parameters[definition.Name] is bool b && b;
        }
    }
}
