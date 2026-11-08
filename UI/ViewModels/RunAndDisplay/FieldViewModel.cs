using Communication.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels.RunAndDisplay
{
    /// <summary>
    /// Một ô nhập trên giao diện, sinh từ FieldDef. Đọc/ghi giá trị qua hai hàm get/set của chỗ chứa
    /// (cấu hình chung của loại hoặc Values của thiết bị), nên không phải viết ViewModel riêng cho từng thuộc tính.
    /// </summary>
    public class FieldViewModel : ViewModelBase
    {
        private readonly Func<string> _get;
        private readonly Action<string> _set;
        private readonly Action _changed;
        private readonly Func<string, string> _sibling;
        private string _error = "";

        public FieldDef Def { get; }

        public FieldViewModel(FieldDef def, Func<string> get, Action<string> set, Action changed,
                              Func<string, string> sibling)
        {
            Def = def;
            _get = get;
            _set = set;
            _changed = changed;
            _sibling = sibling;
            _error = def.Validate(get()) ?? "";
        }

        /// <summary>Có đang hiện không (phụ thuộc giá trị của ô khác, ví dụ ô Hãng hoặc Kiểu truyền thông).</summary>
        public bool IsVisible => Def.IsVisible(_sibling);

        public void RefreshVisibility() => OnPropertyChanged(nameof(IsVisible));

        public string Label => Def.Label;
        public string Hint => Def.Hint;
        public string Section => Def.Section;
        public IReadOnlyList<string> Choices => Def.Choices ?? Array.Empty<string>();

        public bool IsBool => Def.Kind == FieldKind.Bool;
        public bool IsChoice => Def.Kind == FieldKind.Choice;
        public bool IsText => !IsBool && !IsChoice;
        public bool IsTerminator => Def.Kind == FieldKind.Terminator;

        /// <summary>Giá trị dạng chữ cho ô gõ và ô chọn. Giá trị sai vẫn được giữ để người dùng sửa, lỗi hiện bên cạnh.</summary>
        public string Text
        {
            get => _get();
            set
            {
                value ??= "";
                if (_get() == value) return;
                _set(value);
                Error = Def.Validate(value) ?? "";
                OnPropertyChanged(nameof(Text));
                OnPropertyChanged(nameof(Preview));
                _changed();
            }
        }

        public bool BoolValue
        {
            get => _get() == "true";
            set => Text = value ? "true" : "false";
        }

        public string Error
        {
            get => _error;
            private set
            {
                if (SetProperty(ref _error, value))
                    OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => _error.Length > 0;

        /// <summary>Với ký tự kết thúc: các byte thực tế, ví dụ "0D 0A".</summary>
        public string Preview => IsTerminator ? "= " + Escapes.ToHex(_get()) : "";

        /// <summary>Gọi khi giá trị bị đổi từ nơi khác (ví dụ nạp lại).</summary>
        public void Reload()
        {
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(Preview));
            Error = Def.Validate(_get()) ?? "";
        }
    }
}
