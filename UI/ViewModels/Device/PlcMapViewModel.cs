using Communication.Devices;
using Communication.Drivers;
using Communication.Mitsubishi;
using Communication.Plc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using UI.Services;
using UI.ViewModels.Graph;

namespace UI.ViewModels.Device
{
    /// <summary>
    /// Giao diện sửa bản đồ tín hiệu PLC của một máy (xem PlcMap).
    /// Mỗi nhóm là một tab: tín hiệu đơn ở trên, các bảng theo chỉ số ở dưới. Khi vào lần đầu dùng bản mặc định
    /// theo bảng địa chỉ máy mẫu; sửa địa chỉ rồi Lưu. Mỗi máy có thể có file riêng (Mở / Lưu thành).
    /// Chưa nối vào luồng chạy: các phần khác của app sẽ lấy địa chỉ qua khóa (Map.Address("Cmd.Start")).
    /// </summary>
    public partial class PlcMapViewModel : ViewModelBase
    {
        private readonly DeviceManager _devices;
        private readonly IDialogService _dialogs;
        private const string FileFilter = "Bản đồ tín hiệu (*.json)|*.json|Tất cả|*.*";

        [ObservableProperty] private PlcMap _map = new();
        [ObservableProperty] private string _filePath = PlcMapStore.DefaultPath;
        [ObservableProperty] private bool _isDirty;
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private string _testAddress = "M1";
        [ObservableProperty] private string _testResult = "";
        [ObservableProperty] private double _testDx;
        [ObservableProperty] private double _testDy;
        [ObservableProperty] private double _testDr;

        public ObservableCollection<MapGroupVm> Groups { get; } = new();
        public ObservableCollection<string> IssueLines { get; } = new();

        public string MachineName
        {
            get => Map.Machine;
            set
            {
                if (Map.Machine == value) return;
                Map.Machine = value;
                OnPropertyChanged();
                MarkDirty();
            }
        }

        public PlcMapViewModel(DeviceManager devices, IDialogService dialogs)
        {
            _devices = devices;
            _dialogs = dialogs;

            PlcMap map;
            string note;
            try
            {
                bool exists = File.Exists(FilePath);
                map = exists ? PlcMapStore.Load(FilePath) : PlcMapDefaults.Create();
                note = exists ? $"Đã mở {FilePath}." : "Chưa có file: đang dùng bản mặc định (chưa lưu).";
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                map = PlcMapDefaults.Create();
                note = $"File bản đồ lỗi ({ex.Message}); đang dùng bản mặc định. File cũ giữ nguyên cho tới khi bạn Lưu.";
            }
            Rebuild(map, dirty: false);
            StatusText = note + " " + StatusText;
        }

        public void MarkDirty() => IsDirty = true;

        // ================= NẠP / LƯU =================

        private void Rebuild(PlcMap map, bool dirty)
        {
            Map = map;
            Groups.Clear();
            foreach (string name in map.GroupNames)
            {
                var group = new MapGroupVm(name,
                    map.Items.Where(i => i.Group == name).ToList(),
                    map.Tables.Where(t => t.Group == name)
                              .Select(t => new MapTableVm(t, MarkDirty, ReportStatus)).ToList());
                Groups.Add(group);
            }
            OnPropertyChanged(nameof(MachineName));
            IsDirty = dirty;
            RefreshIssues();
        }

        private void ReportStatus(string message) => StatusText = message;

        [RelayCommand]
        private void RefreshIssues()
        {
            var issues = PlcMapValidator.Validate(Map);
            IssueLines.Clear();
            foreach (var issue in issues) IssueLines.Add(issue.ToString());

            int errors = issues.Count(i => i.Level == IssueLevel.Error);
            int warnings = issues.Count - errors;
            StatusText = $"{Map.Items.Count} tín hiệu, {Map.Tables.Count} bảng. {errors} lỗi, {warnings} cảnh báo.";
        }

        [RelayCommand]
        private void Open()
        {
            if (IsDirty && !_dialogs.Confirm("Bản đồ đang sửa chưa lưu sẽ bị bỏ. Tiếp tục mở file khác?")) return;

            string? path = _dialogs.PickOpenFile("Mở bản đồ tín hiệu PLC", FileFilter);
            if (path == null) return;

            try
            {
                var map = PlcMapStore.Load(path);
                FilePath = path;
                Rebuild(map, dirty: false);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                _dialogs.ShowError($"Không mở được file: {ex.Message}");
            }
        }

        [RelayCommand]
        private void Save()
        {
            var issues = PlcMapValidator.Validate(Map);
            int errors = issues.Count(i => i.Level == IssueLevel.Error);
            if (errors > 0)
            {
                RefreshIssues();
                _dialogs.ShowError($"Còn {errors} lỗi trong bản đồ, sửa rồi mới lưu được (xem khung Kiểm tra bên dưới).");
                return;
            }

            try
            {
                PlcMapStore.Save(Map, FilePath);
                IsDirty = false;
                RefreshIssues();
                StatusText = $"Đã lưu {FilePath}. " + StatusText;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _dialogs.ShowError($"Không lưu được: {ex.Message}");
            }
        }

        [RelayCommand]
        private void SaveAs()
        {
            string suggested = string.IsNullOrWhiteSpace(Map.Machine) ? "plc_map.json" : $"plc_map_{Map.Machine.Trim()}.json";
            string? path = _dialogs.PickSaveFile("Lưu bản đồ tín hiệu PLC", FileFilter, suggested);
            if (path == null) return;

            FilePath = path;
            Save();
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            if (!_dialogs.Confirm("Đặt lại toàn bộ về bản mặc định (theo bảng địa chỉ máy mẫu)?\nMọi sửa đổi chưa lưu sẽ mất.")) return;
            Rebuild(PlcMapDefaults.Create(), dirty: true);
        }

        // ================= ĐỌC / GHI THỬ MỘT ĐỊA CHỈ =================

        private bool TryGetPlc(DeviceClass need, out IPlcDriver plc, out PlcAddress address)
        {
            plc = null!;
            address = default;

            var plcs = _devices.ConnectedPlcs();
            if (plcs.Count == 0)
            {
                TestResult = "PLC chưa kết nối. Mở Cấu hình thiết bị, tab PLC, bấm Kết nối.";
                return false;
            }
            if (!PlcAddress.TryParse(TestAddress, out address, out string? error))
            {
                TestResult = "Địa chỉ: " + error;
                return false;
            }
            if (address.Class != need)
            {
                TestResult = need == DeviceClass.Bit
                    ? $"{address} là thanh ghi: dùng nút Đọc thanh ghi."
                    : $"{address} là bit: dùng nút Đọc bit.";
                return false;
            }
            plc = plcs[0].Driver;
            return true;
        }

        [RelayCommand]
        private async Task TestReadBitAsync()
        {
            if (!TryGetPlc(DeviceClass.Bit, out var plc, out var a)) return;
            try { TestResult = $"{a} = {(await plc.ReadBitAsync(a.ToString()) ? 1 : 0)}"; }
            catch (Exception ex) { TestResult = $"Đọc {a} lỗi: {ex.Message}"; }
        }

        [RelayCommand]
        private async Task TestReadWordAsync()
        {
            if (!TryGetPlc(DeviceClass.Word, out var plc, out var a)) return;
            try { TestResult = $"{a} = {await plc.ReadWordAsync(a.ToString())}"; }
            catch (Exception ex) { TestResult = $"Đọc {a} lỗi: {ex.Message}"; }
        }

        [RelayCommand]
        private Task TestWriteBitOnAsync() => TestWriteBitAsync(true);

        [RelayCommand]
        private Task TestWriteBitOffAsync() => TestWriteBitAsync(false);

        /// <summary>Thử gửi độ bù qua PLC: ghi (dX, dY, 0, dRz) vào các thanh ghi Calib X, Y, Z, R theo bản đồ đang sửa.</summary>
        [RelayCommand]
        private async Task TestSendOffsetAsync()
        {
            var cfg = _devices.RobotConfigs.FirstOrDefault(
                c => c.Enabled && MitsubishiPointSettings.Read(c).Mode == RobotSendMode.ViaPlc);
            var plc = cfg != null ? _devices.PlcFor(cfg) : _devices.ConnectedPlcs().Select(p => p.Driver).FirstOrDefault();
            if (plc == null)
            {
                TestResult = "PLC chưa kết nối. Mở Cấu hình thiết bị, tab PLC, bấm Kết nối.";
                return;
            }

            if (!_dialogs.Confirm(
                    $"GHI độ bù (dX {TestDx:0.###}, dY {TestDy:0.###}, dRz {TestDr:0.###}) vào các thanh ghi Calib của PLC đang nối?\n" +
                    "PLC có thể chuyển giá trị này cho robot."))
                return;

            var result = await PlcOffsetWriter.WriteAsync(plc, Map, TestDx, TestDy, 0, TestDr,
                cfg?.GetDouble("MaxOffsetMm", 50) ?? 50, cfg?.GetDouble("MaxOffsetDeg", 30) ?? 30);
            TestResult = (result.Success ? "Đã ghi: " : "LỖI: ") + result.Message;
        }

        private async Task TestWriteBitAsync(bool value)
        {
            if (!TryGetPlc(DeviceClass.Bit, out var plc, out var a)) return;

            // Ghi vào PLC thật có thể làm máy chuyển động (jog, start...): luôn hỏi lại
            if (!_dialogs.Confirm($"GHI {(value ? 1 : 0)} vào bit {a} của PLC đang nối?\nBit này có thể điều khiển máy chuyển động.")) return;
            try
            {
                await plc.WriteBitAsync(a.ToString(), value);
                TestResult = $"Đã ghi {a} = {(value ? 1 : 0)}";
            }
            catch (Exception ex) { TestResult = $"Ghi {a} lỗi: {ex.Message}"; }
        }
    }

    /// <summary>Một tab: tín hiệu đơn ở trên, các bảng ở dưới.</summary>
    public sealed class MapGroupVm
    {
        public MapGroupVm(string title, List<SignalItem> items, List<MapTableVm> tables)
        {
            Title = title;
            Items = items;
            Tables = tables;
        }

        public string Title { get; }
        public List<SignalItem> Items { get; }
        public List<MapTableVm> Tables { get; }
        public bool HasItems => Items.Count > 0;
    }

    /// <summary>Một bảng theo chỉ số, kèm công cụ tạo dãy địa chỉ từ địa chỉ đầu và bước.</summary>
    public sealed partial class MapTableVm : ObservableObject
    {
        private readonly Action _markDirty;
        private readonly Action<string> _report;

        public MapTableVm(SignalTable table, Action markDirty, Action<string> report)
        {
            Table = table;
            _markDirty = markDirty;
            _report = report;
            Rows = table.Rows.Select(r => new MapRowVm(r, markDirty)).ToList();
        }

        public SignalTable Table { get; }
        public string Title => Table.Title;
        public string Note => Table.Note;
        public bool HasNote => !string.IsNullOrWhiteSpace(Table.Note);
        public List<MapRowVm> Rows { get; }
        public IReadOnlyList<string> ColumnTitles => Table.Columns.Select(c => c.Title).ToList();

        /// <summary>Thanh ghi PLC báo đang xử lý hàng số mấy (ví dụ D600).</summary>
        public string IndexAddress
        {
            get => Table.IndexAddress;
            set
            {
                string v = (value ?? "").Trim().ToUpperInvariant();
                if (Table.IndexAddress == v) return;
                Table.IndexAddress = v;
                OnPropertyChanged();
                _markDirty();
            }
        }

        [ObservableProperty] private int _fillColumnIndex;
        [ObservableProperty] private string _fillBase = "";
        [ObservableProperty] private int _fillStep = 1;

        /// <summary>Điền cả một cột: hàng thứ i = địa chỉ đầu + i * bước. Cột nào không đều thì sửa tay từng ô sau đó.</summary>
        [RelayCommand]
        private void Fill()
        {
            if (FillColumnIndex < 0 || FillColumnIndex >= Table.Columns.Count)
            {
                _report("Chọn cột cần tạo dãy.");
                return;
            }
            if (!PlcAddress.TryParse(FillBase, out var start, out string? error))
            {
                _report("Địa chỉ đầu: " + error);
                return;
            }

            // Tính hết trước, hợp lệ cả dãy mới ghi
            var result = new List<string>();
            for (int i = 0; i < Rows.Count; i++)
            {
                if (!start.TryOffset(i * FillStep, out var a))
                {
                    _report($"Hàng {i} vượt giới hạn địa chỉ. Kiểm tra địa chỉ đầu và bước.");
                    return;
                }
                result.Add(a.ToString());
            }

            for (int i = 0; i < Rows.Count; i++) Rows[i][FillColumnIndex] = result[i];
            _report($"Đã tạo dãy {result.First()} đến {result.Last()} cho cột '{Table.Columns[FillColumnIndex].Title}'.");
        }
    }

    /// <summary>Một hàng của bảng. Chỉ mục [i] là địa chỉ ở cột thứ i (để DataGrid gắn cột tạo động).</summary>
    public sealed class MapRowVm : ObservableObject
    {
        private readonly SignalRow _row;
        private readonly Action _markDirty;

        public MapRowVm(SignalRow row, Action markDirty)
        {
            _row = row;
            _markDirty = markDirty;
        }

        public string Label => _row.Label;

        public string this[int column]
        {
            get => column >= 0 && column < _row.Addresses.Count ? _row.Addresses[column] : "";
            set
            {
                if (column < 0 || column >= _row.Addresses.Count) return;
                string v = (value ?? "").Trim().ToUpperInvariant();
                if (_row.Addresses[column] == v) return;
                _row.Addresses[column] = v;
                OnPropertyChanged("Item[]");          // báo lưới vẽ lại ô (kể cả khi đổi từ lệnh Tạo dãy)
                _markDirty();
            }
        }
    }
}
