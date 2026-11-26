using Communication.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
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
using Vision.Project;

namespace UI.ViewModels
{
    /// <summary>Gom các ViewModel và nối chúng với nhau.</summary>
    public partial class MainViewModel : ViewModelBase, IDisposable
    {
        private const string RecipeFilter = "Recipe (*.vgraph)|*.vgraph|JSON|*.json|Tất cả|*.*";
        private readonly IDialogService _dialogs;

        private string? _recipePath;
        private bool _isDirty;

        /// <summary>Tiêu đề cửa sổ: tên file, dấu * nếu có thay đổi chưa lưu.</summary>
        [ObservableProperty] private string _title = "Vision Graph";

        public GraphEditorViewModel Editor { get; }
        public ToolboxViewModel Toolbox { get; }
        public PropertyPanelViewModel Properties { get; }

        /// <summary>Chạy thử đồ thị, hiển thị ảnh và kết quả.</summary>
        public Communication.Fov.FovTable FovTable { get; } = new();

        public RunViewModel Run { get; }

        /// <summary>Vẽ/chỉnh ROI của node đang chọn ngay trên ảnh.</summary>
        public RoiEditorViewModel Roi { get; }

        /// <summary>Danh sách thiết bị (robot, PLC, PC, scanner...) và trạng thái kết nối.</summary>
        public DeviceConfigViewModel Devices { get; }

        /// <summary>Hiệu chuẩn tự động bằng robot và chụp tại vị trí cố định.</summary>
        public AutoCalibrationViewModel AutoCal { get; }

        /// <summary>Bản đồ tín hiệu PLC của máy (địa chỉ bit / thanh ghi, sửa trên giao diện).</summary>
        public PlcMapViewModel PlcMap { get; }

        /// <summary>Tạo file hiệu chuẩn camera - robot từ file CSV các cặp điểm.</summary>
        public IRelayCommand CalibrateFromCsvCommand { get; }

        public MainViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
            CalibrateFromCsvCommand = new RelayCommand(CalibrateFromCsv);

            Devices = new DeviceConfigViewModel(new DeviceConfigStore(), dialogs);
            Editor = new GraphEditorViewModel(BuildDemoGraph(), dialogs);
            Toolbox = new ToolboxViewModel();
            Properties = new PropertyPanelViewModel();
            FovTable.Load();
            Run = new RunViewModel(Editor, dialogs, new OffsetDispatcher(Devices.Manager, () => PlcMap?.Map), Devices.Manager) { FixtureRoot = Devices, FovTable = FovTable };
            Roi = new RoiEditorViewModel(Editor, Properties);
            AutoCal = new AutoCalibrationViewModel(Devices.Manager, Run, dialogs, () => PlcMap?.Map);
            PlcMap = new PlcMapViewModel(Devices.Manager, dialogs);

            // Chọn node nào trên đồ thị thì bảng thuộc tính hiển thị tham số của node đó
            Editor.SelectedNodeChanged += (_, _) => Properties.Node = Editor.SelectedNode;

            // Sửa tham số, thêm/xóa node, nối dây... đều đánh dấu "chưa lưu"
            Editor.RunToNodeRequested += async (_, node) => await Run.RunToNodeAsync(node);
            Run.PreviewActiveChanged += (_, _) => Roi.PreviewActive = Run.PreviewActive;
            Properties.ParameterEdited += (_, _) => MarkDirty();
            Properties.DescriptionEdited += (_, _) => MarkDirty();
            Editor.Nodes.CollectionChanged += (_, _) => MarkDirty();
            Editor.Connections.CollectionChanged += (_, _) => MarkDirty();

            _isDirty = false;
            UpdateTitle();
        }

        // ================= RECIPE (lưu / mở) =================

        private void MarkDirty()
        {
            if (_isDirty) return;
            _isDirty = true;
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            string name = _recipePath == null ? "Chưa đặt tên" : Path.GetFileName(_recipePath);
            Title = $"Vision Graph - {name}{(_isDirty ? " *" : "")}";
        }

        /// <summary>
        /// Có thay đổi chưa lưu thì hỏi Lưu / Không lưu / Hủy.
        /// Trả về false nếu người dùng Hủy hoặc việc lưu không xong (đừng làm gì tiếp).
        /// </summary>
        public bool ConfirmDiscardOrSave()
        {
            if (!_isDirty) return true;

            return _dialogs.ConfirmYesNoCancel("Recipe có thay đổi chưa lưu. Lưu lại trước khi tiếp tục?") switch
            {
                true => SaveRecipe(),
                false => true,
                null => false
            };
        }

        [RelayCommand]
        private void NewRecipe()
        {
            if (Run.IsRunning) return;
            if (!ConfirmDiscardOrSave()) return;

            ReplaceGraph(new VisionGraph(), null);
        }

        [RelayCommand]
        private void OpenRecipe()
        {
            if (Run.IsRunning) return;
            if (!ConfirmDiscardOrSave()) return;

            string? path = _dialogs.PickOpenFile("Mở recipe", RecipeFilter);
            if (path == null) return;

            try
            {
                var loaded = GraphSerializer.Load(path);       // đủ hoặc không: lỗi thì graph hiện tại không bị đụng
                ReplaceGraph(loaded.Graph, path);
                if (loaded.Warnings.Count > 0)
                    _dialogs.ShowInfo("Đã mở, có lưu ý:\n - " + string.Join("\n - ", loaded.Warnings));
            }
            catch (RecipeException ex)
            {
                _dialogs.ShowError("Không mở được recipe:\n" + ex.Message);
            }
            catch (IOException ex)
            {
                _dialogs.ShowError("Không đọc được file: " + ex.Message);
            }
        }

        [RelayCommand]
        private void SaveCurrentRecipe() => SaveRecipe();

        [RelayCommand]
        private void SaveRecipeAs() => SaveRecipe(forcePrompt: true);

        private bool SaveRecipe(bool forcePrompt = false)
        {
            if (Run.IsRunning) return false;

            string? path = _recipePath;
            if (path == null || forcePrompt)
            {
                path = _dialogs.PickSaveFile("Lưu recipe", RecipeFilter, "recipe.vgraph");
                if (path == null) return false;
            }

            try
            {
                GraphSerializer.Save(Editor.Model, path, Path.GetFileNameWithoutExtension(path));
                _recipePath = path;
                _isDirty = false;
                UpdateTitle();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _dialogs.ShowError("Không lưu được: " + ex.Message);
                return false;
            }
        }

        private void ReplaceGraph(VisionGraph graph, string? path)
        {
            var old = Editor.Model;
            Properties.Node = null;
            Run.Reset();                       // bỏ ảnh/kết quả của graph cũ trước khi giải phóng nó
            Editor.Load(graph);
            old.Dispose();

            _recipePath = path;
            _isDirty = false;
            UpdateTitle();
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
  // rẽ nhánh
            return g;
        }

        public void Dispose()
        {
            Run.Dispose();         // dừng lần chạy dở, giải phóng ảnh và kết quả
            Devices.Dispose();     // ngắt mọi kết nối
            Editor.Model.Dispose();
        }
    }
}
