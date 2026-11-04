using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Text;
using UI.ViewModels.Graph;
using UI.ViewModels.ROI;
using UI.ViewModels.ToolboxAndParam;
using Vision.Tools.Abstractions;

namespace UI.ViewModels.ROI
{
    /// <summary>
    /// Cầu nối giữa khung ảnh và tham số ROI của node đang chọn.
    ///  - Current: ROI của node đang chọn (null nếu node không có ROI) → khung ảnh vẽ hình kéo được.
    ///  - ApplyCommand: khung ảnh báo người dùng vừa kéo/đổi cỡ → ghi vào tham số node, bảng thuộc tính cập nhật theo.
    ///  - Người dùng sửa số trên bảng thuộc tính → Current đổi → hình trên ảnh đổi theo.
    /// Chỉ làm việc với tool có IRoiEditable. Không biết tool cụ thể nào.
    /// </summary>
    public partial class RoiEditorViewModel : ViewModelBase
    {
        private readonly GraphEditorViewModel _editor;
        private readonly PropertyPanelViewModel _properties;

        [ObservableProperty]
        private RoiGeometry? _current;

        public RoiEditorViewModel(GraphEditorViewModel editor, PropertyPanelViewModel properties)
        {
            _editor = editor;
            _properties = properties;

            _editor.SelectedNodeChanged += (_, _) => Reload();
            _properties.ParameterEdited += (_, _) => Reload();
        }

        /// <summary>Đọc lại ROI từ node đang chọn.</summary>
        private void Reload()
        {
            var node = _editor.SelectedNode?.Model;

            // Đặt null rồi mới đặt giá trị mới: buộc khung ảnh dựng lại hình kể cả khi giá trị trùng bản cũ
            Current = null;
            if (node?.Tool is IRoiEditable)
                Current = RoiGeometry.FromNode(node);
        }

        /// <summary>
        /// Người dùng kéo ROI trên ảnh. CỐ Ý không đặt lại Current: khung ảnh đang giữ hình người dùng đang kéo,
        /// dựng lại giữa chừng sẽ làm đứt thao tác kéo.
        /// </summary>
        [RelayCommand]
        private void Apply(RoiGeometry? geometry)
        {
            if (geometry == null || _editor.IsLocked) return;

            var node = _editor.SelectedNode?.Model;
            if (node?.Tool is not IRoiEditable) return;

            try
            {
                geometry.ApplyTo(node);
                _properties.ReloadValues();
            }
            catch (ArgumentException)
            {
                // Giá trị ngoài khoảng cho phép: bỏ qua, node giữ giá trị trước
            }
        }
    }
}
