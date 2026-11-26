using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Text;
using UI.ViewModels.Graph;
using UI.ViewModels.ROI;
using UI.ViewModels.ToolboxAndParam;
using Vision.Tools.Abstractions;
using Vision.Tools.Locate;

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
        [NotifyPropertyChangedFor(nameof(OriginalRoi), nameof(ProcessedRoi))]
        private RoiGeometry? _current;

        /// <summary>true: ROI vẽ / kéo trên "Ảnh xử lý" (ảnh sau khi qua tool), false: trên "Ảnh gốc".</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OriginalRoi), nameof(ProcessedRoi))]
        private bool _onProcessed;

        private bool _autoSwitched;
        private bool _savedOnProcessed;

        public RoiGeometry? OriginalRoi => OnProcessed ? null : Current;
        public RoiGeometry? ProcessedRoi => OnProcessed ? Current : null;
        public string? OriginalErase => EraseAreas;                       // vùng xóa nhập tay: luôn ở khung Ảnh gốc

        /// <summary>Vùng xóa vẽ tay (SketchErase): chỉ hiện ở khung Ảnh xử lý ngay sau "Xem mẫu"; học mẫu xong thì ẩn.</summary>
        public string? ProcessedSketch => PreviewActive ? SketchAreas : null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProcessedSketch))]
        private bool _previewActive;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProcessedSketch))]
        private string? _sketchAreas;

        /// <summary>Vùng xóa viền của node đang chọn (chữ). null: node không có tham số EraseAreas.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OriginalErase))]
        private string? _eraseAreas;

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

            // Node Select By ROI: tự chuyển ROI sang ảnh xử lý (hiện toàn ảnh) để kéo vùng chọn dễ hơn; chọn node khác thì trả lại lựa chọn cũ
            bool wantsFullImage = node?.ToolName == "SelectByRoi";
            if (wantsFullImage && !_autoSwitched) { _savedOnProcessed = OnProcessed; _autoSwitched = true; OnProcessed = true; }
            else if (!wantsFullImage && _autoSwitched) { _autoSwitched = false; OnProcessed = _savedOnProcessed; }

            // Đặt null rồi mới đặt giá trị mới: buộc khung ảnh dựng lại hình kể cả khi giá trị trùng bản cũ
            Current = null;
            if (node?.Tool is IRoiEditable)
                Current = RoiGeometry.FromNode(node);

            EraseAreas = node != null && node.Tool.Parameters.Any(p => p.Name == EraseParam)
                ? node.GetParameter<string>(EraseParam)
                : null;
            SketchAreas = node != null && node.Tool.Parameters.Any(p => p.Name == SketchParam)
                ? node.GetParameter<string>(SketchParam)
                : null;
        }

        private const string EraseParam = "EraseAreas";
        private const string SketchParam = "SketchErase";

        /// <summary>Thêm một vùng xóa vẽ tay (SketchErase) vào node đang chọn.</summary>
        [RelayCommand]
        private void AddSketchArea(string? area)
        {
            var node = _editor.SelectedNode?.Model;
            if (node == null || _editor.IsLocked || SketchAreas == null || !PreviewActive) return;

            var parsed = ShapeEraseAreas.Parse(area, out _);
            if (parsed == null || parsed.Count != 1) return;
            SetSketch(node, ShapeEraseAreas.Append(SketchAreas, parsed[0]));
        }

        /// <summary>Hoàn tác nét tẩy vừa thêm (bỏ vùng cuối cùng trong SketchErase).</summary>
        [RelayCommand]
        private void UndoSketchArea()
        {
            var node = _editor.SelectedNode?.Model;
            if (node == null || _editor.IsLocked || SketchAreas == null) return;
            var list = ShapeEraseAreas.Parse(SketchAreas, out _);
            if (list == null || list.Count == 0) return;
            list.RemoveAt(list.Count - 1);
            SetSketch(node, ShapeEraseAreas.Format(list));
        }

        [RelayCommand]
        private void ClearSketchAreas()
        {
            var node = _editor.SelectedNode?.Model;
            if (node == null || _editor.IsLocked || SketchAreas == null) return;
            SetSketch(node, "");
        }

        private void SetSketch(Vision.Graph.VisionNode node, string text)
        {
            try
            {
                node.SetParameter(SketchParam, text);
                SketchAreas = text;
                _properties.ReloadValues();
                _properties.NotifyEdited();
            }
            catch (ArgumentException) { }
        }

        /// <summary>Thêm một vùng xóa viền (chữ "R ..." hoặc "C ...") vào node đang chọn.</summary>
        [RelayCommand]
        private void AddEraseArea(string? area)
        {
            var node = _editor.SelectedNode?.Model;
            if (node == null || _editor.IsLocked || EraseAreas == null) return;

            var parsed = ShapeEraseAreas.Parse(area, out _);
            if (parsed == null || parsed.Count != 1) return;

            SetErase(node, ShapeEraseAreas.Append(EraseAreas, parsed[0]));
        }

        /// <summary>Bỏ hết vùng xóa viền của node đang chọn.</summary>
        [RelayCommand]
        private void ClearEraseAreas()
        {
            var node = _editor.SelectedNode?.Model;
            if (node == null || _editor.IsLocked || EraseAreas == null) return;
            SetErase(node, "");
        }

        private void SetErase(Vision.Graph.VisionNode node, string text)
        {
            try
            {
                node.SetParameter(EraseParam, text);
                EraseAreas = text;
                _properties.ReloadValues();
            }
            catch (ArgumentException)
            {
                // Giá trị bị từ chối: node giữ giá trị trước
            }
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
