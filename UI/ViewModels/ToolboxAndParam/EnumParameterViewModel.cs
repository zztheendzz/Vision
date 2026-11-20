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

        // Giải thích ngắn từng lựa chọn, khóa "KiểuEnum.GiáTrị". Enum không có trong bảng thì không hiện gì.
        private static readonly Dictionary<string, string> HelpTexts = new()
        {
            ["OffsetConvention.AboutRobotOrigin"] = "Robot xoay điểm gắp đã dạy quanh GỐC robot rồi dịch (Dx, Dy). Không cần nhập điểm dạy.",
            ["OffsetConvention.AboutReferencePoint"] = "Robot xoay điểm gắp đã dạy quanh TÂM MẪU lúc dạy (RefX, RefY) rồi dịch. Robot phải biết tâm mẫu.",
            ["OffsetConvention.Additive"] = "PLC cộng thẳng Dx, Dy, Drz vào điểm gắp đã dạy. Phải nhập TaughtX / TaughtY / TaughtRz.",
            ["PlaceOffsetConvention.Additive"] = "Cộng thẳng Dx, Dy, Drz vào điểm đặt đã dạy. Phổ biến nhất, chỉ cần nhập PlaceRz.",
            ["PlaceOffsetConvention.ToolFrame"] = "Dịch theo hệ tọa độ của tay gắp (tool). Robot phải hỗ trợ lệnh dịch theo tool.",
            ["PlaceOffsetConvention.AboutRobotOrigin"] = "Xoay điểm đặt quanh GỐC robot rồi dịch (Dx, Dy). Phải nhập PlaceX / PlaceY / PlaceRz.",
        };
        private readonly string _enumName;
        private readonly bool _dynamic;
        private const string EmptyChoice = "(mặc định: camera đầu tiên)";

        /// <summary>Giải thích ngắn lựa chọn đang chọn (rỗng nếu không có).</summary>
        public string Help => HelpTexts.TryGetValue($"{_enumName}.{_selected}", out var text) ? text : "";

        public IReadOnlyList<string> Choices { get; }

        public string Selected
        {
            get => _selected;
            set
            {
                if (value != null && SetProperty(ref _selected, value))
                {
                    OnPropertyChanged(nameof(Help));
                    TrySetValue(_dynamic && value == EmptyChoice ? "" : value);          // ParamDef.Coerce đổi chữ thành enum
                }
            }
        }

        public EnumParameterViewModel(VisionNode node, ParamDef definition) : base(node, definition)
        {
            _enumName = definition.Type.Name;
            if (definition.ChoicesKey != null)
            {
                // Chuỗi chọn từ danh sách động (tên camera...). Mục đầu = để trống.
                var list = new List<string> { EmptyChoice };
                list.AddRange(ParamChoices.Get(definition.ChoicesKey));
                string current = Node.Parameters[Name]?.ToString() ?? "";
                if (current.Length > 0 && !list.Contains(current)) list.Add(current);   // tên cũ không còn trong cài đặt: vẫn hiện để thấy
                Choices = list;
                _dynamic = true;
                _selected = current.Length == 0 ? EmptyChoice : current;
                return;
            }
            Choices = Enum.GetNames(definition.Type);
            _selected = Read();
        }

        public override void Reload()
        {
            SetProperty(ref _selected, _dynamic ? DynamicRead() : Read(), nameof(Selected));
            OnPropertyChanged(nameof(Help));
            ErrorMessage = "";
        }

        private string DynamicRead()
        {
            string v = Node.Parameters[Name]?.ToString() ?? "";
            return v.Length == 0 ? EmptyChoice : v;
        }

        private string Read() => Node.Parameters[Name]?.ToString() ?? Choices.FirstOrDefault() ?? "";
    }
}
