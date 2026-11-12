using Communication.Configuration;
using Communication.Drivers;
using Communication.Fixtures;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using UI.Services;
using UI.ViewModels.RunAndDisplay;

namespace UI.ViewModels.Device
{
    /// <summary>
    /// Cấu hình kết nối: 6 tab theo loại thiết bị, mỗi tab thêm / đổi tên / xóa thiết bị.
    /// Còn giữ danh sách phẳng Devices cho thanh trạng thái và tab Kết nối.
    /// </summary>
    public partial class DeviceConfigViewModel : ViewModelBase, IDisposable
    {
        private readonly DeviceConfigStore _store;
        private readonly IDialogService _dialogs;
        private readonly DeviceCatalog _catalog;
        private readonly DispatcherTimer _timer;

        public IDialogService Dialogs => _dialogs;

        /// <summary>Cấu hình thiết bị đang dùng (cùng đối tượng với các ô trên cửa sổ cấu hình).</summary>
        public DeviceCatalog Catalog => _catalog;

        /// <summary>Giữ các kết nối đang sống; vòng chạy lấy robot/PLC đang nối từ đây.</summary>
        public DeviceManager Manager { get; }

        /// <summary>Có thay đổi chưa lưu không (cửa sổ cấu hình hỏi trước khi đóng).</summary>
        public bool IsDirty { get; private set; }

        public ObservableCollection<DeviceTypeViewModel> Types { get; } = new();

        /// <summary>Mọi thiết bị của mọi loại.</summary>
        public ObservableCollection<DeviceViewModel> Devices { get; } = new();

        /// <summary>Riêng các PLC, làm danh sách chọn "PLC nguồn".</summary>
        public ObservableCollection<DeviceViewModel> Plcs { get; } = new();

        /// <summary>Nhật ký gửi/nhận của các khuôn test (giữ tối đa 500 dòng gần nhất).</summary>
        public ObservableCollection<LogLine> FixtureLog { get; } = new();

        /// <summary>Thêm một dòng vào nhật ký khuôn test (gọi từ luồng nào cũng được).</summary>
        public void AddFixtureLog(string source, string kind, string text)
        {
            var line = new LogLine(DateTime.Now.ToString("HH:mm:ss.fff"), source, kind, text);
            void Add()
            {
                FixtureLog.Add(line);
                while (FixtureLog.Count > 500) FixtureLog.RemoveAt(0);
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) Add();
            else dispatcher.BeginInvoke(Add);
        }

        // ---------- Cổng nhận của khuôn test ----------

        /// <summary>Tình trạng cổng nhận, hiện ở tab Khuôn test: đang nghe, hoặc lý do chưa mở.</summary>
        [ObservableProperty] private string _listenerStatus = "Cổng nhận: chưa kiểm tra";

        private FixtureListener? _listener;
        private string _listenerKey = "";       // cổng + ký tự kết thúc đang dùng (hoặc đã thử mà lỗi)

        /// <summary>
        /// Giữ máy chủ nghe "Cổng nhận" đúng với cấu hình: chạy khi có khuôn đang bật ở chế độ TCP Split, tắt khi không còn.
        /// Gọi định kỳ; đổi cổng hoặc ký tự kết thúc thì tự nghe lại. Cổng bị chiếm chỉ báo lỗi một lần cho tới khi cấu hình đổi.
        /// </summary>
        private void SyncFixtureListener()
        {
            try { SyncFixtureListenerCore(); }
            catch (Exception ex) { ListenerStatus = "Cổng nhận: lỗi " + ex.Message; }
        }

        private void SyncFixtureListenerCore()
        {
            string mode = _catalog.GetGroupValue(DeviceType.Fixture, DeviceSchemas.KeyMode);
            bool anyEnabled = Devices.Any(d => d.Config.Type == DeviceType.Fixture && d.Enabled);
            bool want = mode == "TCP Split" && anyEnabled;

            string key = "";
            int port = 0;
            string term = "";
            if (want)
            {
                term = _catalog.GetGroupValue(DeviceType.Fixture, DeviceSchemas.KeyRx);
                int.TryParse(_catalog.GetGroupValue(DeviceType.Fixture, "ListenPort"), out port);
                key = $"{port}|{term}";
            }
            else
            {
                key = "";
                ListenerStatus = mode != "TCP Split"
                    ? $"Cổng nhận: chưa mở (chế độ khuôn test là '{mode}', cần 'TCP Split')"
                    : "Cổng nhận: chưa mở (chưa có khuôn nào được tick Bật)";
            }

            if (key == _listenerKey) return;

            _listener?.Dispose();
            _listener = null;
            _listenerKey = key;
            if (!want || port <= 0) return;

            try
            {
                var listener = new FixtureListener(port, Escapes.ToBytes(term));
                listener.LineReceived += (remote, line) => OnFixtureLine(remote, line);
                listener.Info += text => AddFixtureLog("Cổng nhận", "INFO", text);
                listener.Start();
                _listener = listener;
                ListenerStatus = $"Cổng nhận: đang nghe cổng {port}";
                AddFixtureLog("Cổng nhận", "INFO", $"Đang nghe cổng {port}");
            }
            catch (Exception ex)
            {
                ListenerStatus = $"Cổng nhận: KHÔNG nghe được cổng {port} ({ex.Message})";
                AddFixtureLog("Cổng nhận", "LỖI", $"Không nghe được cổng {port}: {ex.Message}");
            }
        }

        /// <summary>Có dòng từ khuôn gửi về: ghi nhật ký và đưa cho khuôn có số khuôn (SS) khớp.</summary>
        private void OnFixtureLine(string remote, string line)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            void Handle()
            {
                AddFixtureLog("Cổng nhận", "NHẬN", $"{remote}: {line}");
                bool handled = Devices.Where(d => d.Config.Type == DeviceType.Fixture).Any(d => d.ApplyLine(line));
                if (!handled) AddFixtureLog("Cổng nhận", "INFO", "Dòng này không khớp mẫu kết quả nào hoặc số khuôn không có trong danh sách");
            }

            if (dispatcher == null || dispatcher.CheckAccess()) Handle();
            else dispatcher.BeginInvoke(Handle);
        }

        [RelayCommand]
        private void ClearFixtureLog() => FixtureLog.Clear();

        /// <summary>Chép toàn bộ nhật ký vào clipboard.</summary>
        [RelayCommand]
        private void CopyFixtureLog()
        {
            try
            {
                System.Windows.Clipboard.SetText(string.Join(Environment.NewLine,
                    FixtureLog.Select(l => $"{l.Time}  {l.Source}  {l.Kind}  {l.Text}")));
                StatusMessage = "Đã chép nhật ký vào clipboard";
            }
            catch (Exception ex) { StatusMessage = "Không chép được: " + ex.Message; }
        }

        [ObservableProperty] private DeviceTypeViewModel? _selectedType;
        [ObservableProperty] private string _statusMessage = "";

        public DeviceConfigViewModel(DeviceConfigStore store, IDialogService dialogs)
        {
            _store = store;
            _dialogs = dialogs;
            _catalog = store.Load();
            Manager = new DeviceManager(_catalog);

            foreach (var schema in DeviceSchemas.All)
                Types.Add(new DeviceTypeViewModel(schema, _catalog, this, dialogs));

            // PLC dựng trước để các thiết bị khác tìm được PLC nguồn
            foreach (var config in _catalog.Devices.OrderBy(d => d.Type != DeviceType.PLC))
                AddViewModel(config);
            foreach (var t in Types) t.SelectedDevice = t.Devices.FirstOrDefault();
            foreach (var d in Devices) d.RefreshSourcePlc();
            SelectedType = Types.FirstOrDefault();

            if (store.LoadNotes.Count > 0)
                StatusMessage = string.Join(" ", store.LoadNotes);

            // Cứ 2 giây kiểm tra lại xem thiết bị còn nối không
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) =>
            {
                SyncFixtureListener();
                foreach (var d in Devices) d.RefreshStatus();
            };
            _timer.Start();
            SyncFixtureListener();
        }

        // ---------- Thêm / xóa (gọi từ DeviceTypeViewModel) ----------

        internal DeviceViewModel CreateDevice(DeviceType type)
        {
            var config = _catalog.AddDevice(type);
            var vm = AddViewModel(config);
            vm.RefreshSourcePlc();
            MarkDirty();
            return vm;
        }

        internal void DeleteDevice(DeviceViewModel device)
        {
            device.Dispose();
            _catalog.Remove(device.Config);          // PLC bị xóa: thiết bị dùng nó được bỏ chọn trong catalog

            Devices.Remove(device);
            Plcs.Remove(device);
            Types.First(t => t.Schema.Type == device.Type).Devices.Remove(device);
            foreach (var d in Devices) d.RefreshSourcePlc();
            MarkDirty();
        }

        private DeviceViewModel AddViewModel(DeviceConfig config)
        {
            var vm = new DeviceViewModel(config, _catalog, this);
            Devices.Add(vm);
            if (config.Type == DeviceType.PLC) Plcs.Add(vm);
            Types.First(t => t.Schema.Type == config.Type).Devices.Add(vm);
            return vm;
        }

        // Tự lưu: sau mỗi thay đổi chờ 1 giây (gom nhiều thao tác liền nhau) rồi lưu nếu cấu hình hợp lệ,
        // để lần sau mở app là nạp lại đúng. Còn lỗi thì giữ trạng thái "chưa lưu" cho tới khi sửa xong.
        private DispatcherTimer? _autoSave;

        private void ScheduleAutoSave()
        {
            if (_autoSave == null)
            {
                _autoSave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _autoSave.Tick += (_, _) =>
                {
                    _autoSave!.Stop();
                    if (IsDirty && !TrySaveQuietly()) StatusMessage = "Có thay đổi chưa lưu (còn ô sai, sửa xong sẽ tự lưu)";
                };
            }
            _autoSave.Stop();
            _autoSave.Start();
        }

        internal void MarkDirty()
        {
            IsDirty = true;
            StatusMessage = "Có thay đổi chưa lưu";
            ScheduleAutoSave();
            SyncFixtureListener();     // đổi chế độ / cổng / Bật thì cổng nhận đổi theo ngay
        }

        // ---------- Lưu / kết nối ----------

        [RelayCommand]
        private void Save()
        {
            var errors = _catalog.Validate();
            if (errors.Count > 0)
            {
                string shown = string.Join("\n", errors.Take(12));
                if (errors.Count > 12) shown += $"\n... và {errors.Count - 12} lỗi nữa";
                _dialogs.ShowError("Chưa lưu được:\n" + shown);
                return;
            }

            try
            {
                _store.Save(_catalog);
                IsDirty = false;
                StatusMessage = $"Đã lưu lúc {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                _dialogs.ShowError("Không lưu được: " + ex.Message);
            }
        }

        /// <summary>Lưu cấu hình không hiện hộp thoại. Trả về false nếu còn lỗi hoặc không ghi được file.</summary>
        internal bool TrySaveQuietly()
        {
            try
            {
                if (_catalog.Validate().Count > 0) return false;
                _store.Save(_catalog);
                IsDirty = false;
                StatusMessage = $"Đã lưu lúc {DateTime.Now:HH:mm:ss}";
                return true;
            }
            catch { return false; }
        }

        [RelayCommand]
        private async Task ConnectAllAsync()
        {
            await Task.WhenAll(Devices.Where(d => d.CanConnect).Select(d => d.ConnectCommand.ExecuteAsync(null)));
        }

        [RelayCommand]
        private void DisconnectAll()
        {
            foreach (var d in Devices) d.DisconnectCommand.Execute(null);
        }

        public void Dispose()
        {
            _timer.Stop();
            _autoSave?.Stop();
            if (IsDirty) TrySaveQuietly();        // lưu nốt khi đóng
            _listener?.Dispose();
            Manager.Dispose();
        }
    }
}
