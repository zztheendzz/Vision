using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Vision.Core;
using Vision.Graph;
using Vision.Tools.Abstractions;
using UI.ViewModels.Graph;
namespace UI.ViewModels.ToolboxAndParam
{
    /// <summary>
    /// Bảng thuộc tính: hiển thị các tham số của node đang được chọn trên đồ thị.
    /// MainViewModel gán Node mỗi khi người dùng chọn node khác.
    /// </summary>
    public partial class PropertyPanelViewModel : ViewModelBase
    {
        public ObservableCollection<ParameterViewModel> Parameters { get; } = new();

        /// <summary>Người dùng vừa sửa một tham số trên bảng (hợp lệ, đã ghi vào node).</summary>
        public event EventHandler? ParameterEdited;

        /// <summary>Đọc lại mọi giá trị từ node, khi node bị đổi từ nơi khác (kéo ROI). Không bắn ParameterEdited.</summary>
        public void ReloadValues()
        {
            foreach (var p in Parameters) p.Reload();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNode))]
        [NotifyPropertyChangedFor(nameof(HasNoParameters))]
        [NotifyPropertyChangedFor(nameof(KindText))]
        private NodeViewModel? _node;

        public bool HasNode => Node != null;

        /// <summary>Đã chọn node nhưng tool đó không có tham số nào.</summary>
        public bool HasNoParameters => Node != null && Parameters.Count == 0;

        public string KindText => Node == null ? "" : "Loại: " + DescribeKind(Node.Model.Tool.Kind);

        // Tự chạy mỗi khi Node đổi
        partial void OnNodeChanged(NodeViewModel? value)
        {
            Parameters.Clear();

            if (value != null)
            {
                foreach (var definition in value.Model.Tool.Parameters)
                {
                    var editor = CreateEditor(value.Model, definition);
                    editor.ValueApplied += (_, _) => ParameterEdited?.Invoke(this, EventArgs.Empty);
                    Parameters.Add(editor);
                }
            }

            OnPropertyChanged(nameof(HasNoParameters));
        }

        /// <summary>Chọn kiểu ô nhập theo kiểu dữ liệu của tham số.</summary>
        private static ParameterViewModel CreateEditor(VisionNode node, ParamDef definition)
        {
            if (definition.Type == typeof(bool))
                return new BoolParameterViewModel(node, definition);

            return new TextParameterViewModel(node, definition);
        }

        private static string DescribeKind(ToolKind kind) => kind switch
        {
            ToolKind.Source => "Nguồn dữ liệu",
            ToolKind.Pure => "Xử lý (không giữ trạng thái)",
            ToolKind.Stateful => "Xử lý có mô hình (nạp một lần)",
            ToolKind.Action => "Hành động (tác động ra ngoài)",
            _ => "Chưa xác định"
        };
    }
}
