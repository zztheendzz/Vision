
using Vision.Core;
using Vision.Graph;

namespace UI.ViewModels.ToolboxAndParam
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
            _isChecked = Read();
        }

        public override void Reload()
        {
            SetProperty(ref _isChecked, Read(), nameof(IsChecked));
            ErrorMessage = "";
        }

        private bool Read() => Node.Parameters[Name] is bool b && b;
    }
}
