using Communication.Configuration;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text;
using UI.Services;
using UI.ViewModels.Device;
using UI.ViewModels.Graph;
using UI.ViewModels.ROI;
using UI.ViewModels.RunAndDisplay;
using UI.ViewModels.ToolboxAndParam;
using Vision.Calibration;
using Vision.Graph;

namespace UI.ViewModels
{
    /// <summary>Gom các ViewModel và nối chúng với nhau.</summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly VisionGraph _graph;
        private readonly IDialogService _dialogs;

        public GraphEditorViewModel Editor { get; }
        public ToolboxViewModel Toolbox { get; }
        public PropertyPanelViewModel Properties { get; }

        /// <summary>Chạy thử đồ thị, hiển thị ảnh và kết quả.</summary>
        public RunViewModel Run { get; }

        /// <summary>Vẽ/chỉnh ROI của node đang chọn ngay trên ảnh.</summary>
        public RoiEditorViewModel Roi { get; }

        /// <summary>Danh sách thiết bị (robot, PLC, PC, scanner...) và trạng thái kết nối.</summary>
        public DeviceConfigViewModel Devices { get; }

        /// <summary>Tạo file hiệu chuẩn camera - robot từ file CSV các cặp điểm.</summary>
        public IRelayCommand CalibrateFromCsvCommand { get; }

        public MainViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
            CalibrateFromCsvCommand = new RelayCommand(CalibrateFromCsv);
            _graph = BuildDemoGraph();
            Editor = new GraphEditorViewModel(_graph, dialogs);
            Toolbox = new ToolboxViewModel();
            Properties = new PropertyPanelViewModel();
            Run = new RunViewModel(Editor, dialogs);
            Roi = new RoiEditorViewModel(Editor, Properties);
            Devices = new DeviceConfigViewModel(new DeviceConfigStore(), dialogs);

            // Chọn node nào trên đồ thị thì bảng thuộc tính hiển thị tham số của node đó
            Editor.SelectedNodeChanged += (_, _) => Properties.Node = Editor.SelectedNode;
        }

        private void CalibrateFromCsv()
        {
            string? csv = _dialogs.PickOpenFile(
                "Chọn file CSV các cặp điểm (pixelX, pixelY, robotX, robotY)",
                "CSV|*.csv;*.txt|Tất cả|*.*");
            if (csv == null) return;

            try
            {
                var points = CalibrationStore.ReadCsv(csv);
                var data = CalibrationStore.Create(points);

                string? target = _dialogs.PickSaveFile("Lưu file hiệu chuẩn", "JSON|*.json", "calibration.json");
                if (target == null) return;
                CalibrationStore.Save(data, target);

                var affine = data.ToAffine();
                _dialogs.ShowInfo(
                    $"Đã lưu hiệu chuẩn từ {data.PointCount} điểm.\n" +
                    $"Sai số trung bình (RMS): {data.RmsMm:F3} mm\n" +
                    $"Sai số lớn nhất: {data.MaxErrorMm:F3} mm\n" +
                    $"Tỉ lệ: {affine.MillimetersPerPixel:F4} mm/pixel" +
                    (data.RmsMm > 0.5 ? "\n\nSai số khá lớn: kiểm tra lại các điểm đo hoặc thêm điểm." : ""));
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException or IOException)
            {
                _dialogs.ShowError(ex.Message);
            }
        }

        private static VisionGraph BuildDemoGraph()
        {
            var g = new VisionGraph();
            return g;
        }

        public void Dispose()
        {
            Run.Dispose();         // dừng lần chạy dở, giải phóng ảnh và kết quả
            Devices.Dispose();     // ngắt mọi kết nối
            _graph.Dispose();
        }
    }
}
