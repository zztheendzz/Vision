using Communication.Camera;
using Communication.Configuration;
using Communication.Drivers;
using Communication.Transport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HalconDotNet;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using UI.ViewModels.RunAndDisplay;
using Vision.Graph;

namespace UI.ViewModels.Device
{
    /// <summary>
    /// Một thiết bị trên giao diện: tên (đổi được), bật/tắt, PLC nguồn, các ô thuộc tính sinh từ schema,
    /// và trạng thái kết nối. Sửa là sửa thẳng vào DeviceConfig trong catalog; lưu file là việc của DeviceConfigViewModel.
    /// </summary>
    public partial class DeviceViewModel : ViewModelBase, IDisposable
    {
        private readonly DeviceCatalog _catalog;
        private readonly DeviceConfigViewModel _owner;

        public DeviceConfig Config { get; }

        public IReadOnlyList<FieldViewModel> Fields { get; }

        public DeviceViewModel(DeviceConfig config, DeviceCatalog catalog, DeviceConfigViewModel owner)
        {
            Config = config;
            _catalog = catalog;
            _owner = owner;

            var schema = DeviceSchemas.For(config.Type);
            Fields = schema.DeviceFields
                .Select(def => new FieldViewModel(
                    def,
                    () => config.GetString(def.Key),
                    v => config.Set(def.Key, v),
                    () =>
                    {
                        foreach (var f in Fields) f.RefreshVisibility();   // đổi Hãng / Kiểu truyền thông thì ẩn-hiện ô liên quan
                        OnPropertyChanged(nameof(Summary));
                        OnPropertyChanged(nameof(CanConnect));
                        _owner.MarkDirty();
                    },
                    config.GetString))
                .ToList();
        }

        public Guid Id => Config.Id;
        public DeviceType Type => Config.Type;
        public string TypeName => DeviceSchemas.For(Config.Type).DisplayName;
        public bool HasPlcSource => DeviceSchemas.For(Config.Type).HasPlcSource;
        public bool IsPlc => Config.Type == DeviceType.PLC;
        public bool IsCamera => Config.Type == DeviceType.Camera;
        public bool IsNotCamera => !IsCamera;
        /// <summary>Có ô "Gửi thử" chữ không (chỉ thiết bị nói chuyện bằng chữ qua TCP).</summary>
        public bool CanSendText => Config.Type is DeviceType.Fixture or DeviceType.PC;

        // ---------- Tên ----------

        [ObservableProperty] private string _nameError = "";

        /// <summary>Đổi tên. Tên rỗng hoặc trùng bị từ chối: giữ tên cũ và hiện lỗi.</summary>
        public string Name
        {
            get => Config.Name;
            set
            {
                string? error = _catalog.CheckName(Config, value);
                if (error != null)
                {
                    NameError = error;
                    // Báo thay đổi sau khi lượt cập nhật hiện tại xong, để ô nhập chắc chắn quay về tên cũ
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        new Action(() => OnPropertyChanged(nameof(Name))));
                    return;
                }

                NameError = "";
                string newName = value.Trim();
                if (Config.Name == newName) return;
                Config.Name = newName;
                OnPropertyChanged(nameof(Name));
                _owner.MarkDirty();
            }
        }

        public bool Enabled
        {
            get => Config.Enabled;
            set
            {
                if (Config.Enabled == value) return;
                Config.Enabled = value;
                OnPropertyChanged(nameof(Enabled));
                OnPropertyChanged(nameof(Summary));
                if (!value) Disconnect();
                _owner.MarkDirty();
            }
        }

        // ---------- PLC nguồn ----------

        /// <summary>PLC đang được chọn làm nguồn (null: chưa chọn). Danh sách lựa chọn là _owner.Plcs.</summary>
        public DeviceViewModel? SourcePlc
        {
            get => Config.SourcePlcId is Guid id ? _owner.Plcs.FirstOrDefault(p => p.Id == id) : null;
            set
            {
                if (value?.Id == Config.SourcePlcId) return;
                Config.SourcePlcId = value?.Id;
                OnPropertyChanged(nameof(SourcePlc));
                _owner.MarkDirty();
            }
        }

        public void RefreshSourcePlc() => OnPropertyChanged(nameof(SourcePlc));

        // ---------- Trạng thái ----------

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private bool _isConnected;

        [ObservableProperty] private string _message = "";
        [ObservableProperty] private string _testText = "";

        public string StatusText => IsConnected ? "Đã kết nối" : "Chưa kết nối";

        /// <summary>Một dòng tóm tắt cho thẻ trong tab Kết nối.</summary>
        public string Summary => DeviceFactory.Describe(_catalog, Config);

        /// <summary>Có đường kết nối TCP để bấm Kết nối không.</summary>
        public bool CanConnect => DeviceFactory.CanConnect(_catalog, Config);

        // ---------- Lệnh ----------

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (!Config.Enabled)
            {
                Message = "Thiết bị đang tắt: tick ô \"Bật\" ở trên rồi bấm Kết nối lại";
                return;
            }

            try
            {
                Message = "Đang kết nối...";
                string? description = await _owner.Manager.ConnectAsync(Config);

                if (description == null)
                {
                    IsConnected = false;
                    Message = "Thiết bị này không có gì để nối (thông số chưa đủ)";
                    return;
                }

                IsConnected = _owner.Manager.IsConnected(Id);
                Message = IsConnected ? $"Kết nối thành công ({description})" : "Không kết nối được";
                if (IsConnected) AttachCamera();
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Message = "Lỗi: " + ex.Message;
            }
        }

        [RelayCommand]
        private void Disconnect()
        {
            DetachCamera();
            _owner.Manager.Disconnect(Id);
            IsConnected = false;
            Message = "Đã ngắt kết nối";
        }

        [RelayCommand]
        private async Task SendTestAsync()
        {
            if (_owner.Manager.GetLink(Id) is not ITransport transport || !transport.IsConnected)
            {
                Message = "Chưa kết nối hoặc thiết bị này không gửi chữ thô được";
                return;
            }

            try
            {
                await transport.SendAsync(TestText);
                Message = $"Đã gửi: {TestText}";
            }
            catch (Exception ex)
            {
                Message = "Lỗi gửi: " + ex.Message;
            }
        }

        /// <summary>Được gọi định kỳ để phát hiện thiết bị tự ngắt kết nối.</summary>
        public void RefreshStatus()
        {
            bool alive = _owner.Manager.IsConnected(Id);
            if (alive == IsConnected) return;

            IsConnected = alive;
            if (!alive)
            {
                DetachCamera();
                Message = "Mất kết nối";
            }
        }

        public void Dispose()
        {
            DetachCamera();
            _owner.Manager.Disconnect(Id);
        }

        // ---------- PLC: đọc thử ----------

        [ObservableProperty] private string _readAddress = "M1";

        [RelayCommand]
        private async Task ReadBitAsync()
        {
            if (!TryGetPlc(out var plc, out string address)) return;
            try
            {
                bool value = await plc.ReadBitAsync(address);
                Message = $"Đọc bit {address} = {(value ? 1 : 0)}";
            }
            catch (Exception ex)
            {
                Message = $"Đọc bit {address} lỗi: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ReadWordAsync()
        {
            if (!TryGetPlc(out var plc, out string address)) return;
            try
            {
                short value = await plc.ReadWordAsync(address);
                Message = $"Đọc thanh ghi {address} = {value}";
            }
            catch (Exception ex)
            {
                Message = $"Đọc thanh ghi {address} lỗi: {ex.Message}";
            }
        }

        private bool TryGetPlc(out IPlcDriver plc, out string address)
        {
            plc = null!;
            address = (ReadAddress ?? "").Trim();

            if (_owner.Manager.GetLink(Id) is not IPlcDriver driver || !driver.IsConnected)
            {
                Message = "Chưa kết nối PLC";
                return false;
            }
            if (address.Length == 0)
            {
                Message = "Nhập địa chỉ cần đọc, ví dụ M1 hoặc D100";
                return false;
            }
            plc = driver;
            return true;
        }

        // ---------- Camera: quét, xem trực tiếp, chụp ----------

        /// <summary>Ảnh đang xem trước. Ảnh thuộc về ViewModel này: nó tự Dispose khi thay ảnh mới hoặc ngắt kết nối.</summary>
        [ObservableProperty] private ImageDisplay _preview = ImageDisplay.Empty;
        [ObservableProperty] private bool _isLive;
        [ObservableProperty] private string? _selectedFound;

        public ObservableCollection<string> FoundCameras { get; } = new();

        private HalconCamera? _camera;
        private int _framePending;     // 1 = giao diện còn đang vẽ ảnh trước, bỏ khung mới để không dồn hàng đợi
        private int _frameCount;

        partial void OnSelectedFoundChanged(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var field = Fields.FirstOrDefault(f => f.Def.Key == "DeviceId");
            if (field != null) field.Text = value;         // ghi vào cấu hình + đánh dấu đã sửa
        }

        [RelayCommand]
        private async Task ScanCamerasAsync()
        {
            Message = "Đang quét camera...";
            var (list, error) = await Task.Run(() =>
            {
                var l = HalconCamera.ScanCameras(out string? e);
                return (l, e);
            });

            FoundCameras.Clear();
            foreach (var name in list) FoundCameras.Add(name);
            Message = error ?? (list.Count == 0 ? "Không thấy camera nào" : $"Thấy {list.Count} camera");
        }

        private void AttachCamera()
        {
            if (!IsCamera) return;
            DetachCamera();
            _camera = _owner.Manager.GetLink(Id) as HalconCamera;
            if (_camera == null) return;
            _camera.OnImageGrabbed += OnFrame;
            _camera.OnError += OnCameraError;
        }

        private void DetachCamera()
        {
            if (_camera != null)
            {
                _camera.OnImageGrabbed -= OnFrame;
                _camera.OnError -= OnCameraError;
                try { _camera.StopGrabbing(); } catch { }
                _camera = null;
            }
            SetLive(false);
            ClearPreview();
        }

        private void SetLive(bool live)
        {
            void Apply() => IsLive = live;
            RunOnUi(Apply);
        }

        private static void RunOnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        private void OnCameraError(object? sender, string message) => RunOnUi(() => Message = message);

        // Chạy trên luồng chụp của camera. Người nhận sự kiện phải Dispose ảnh.
        private void OnFrame(object? sender, HObject frame)
        {
            if (Interlocked.Exchange(ref _framePending, 1) == 1)
            {
                frame.Dispose();                           // giao diện chưa kịp vẽ khung trước: bỏ khung này
                return;
            }

            HImage image;
            try { image = new HImage(frame); }
            catch { frame.Dispose(); Interlocked.Exchange(ref _framePending, 0); return; }
            frame.Dispose();

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                image.Dispose();
                Interlocked.Exchange(ref _framePending, 0);
                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                var old = Preview.Image;
                _frameCount++;
                Preview = new ImageDisplay(image, Array.Empty<HObject>(), $"khung {_frameCount}");
                old?.Dispose();                            // khung cũ đã được thay, an toàn để giải phóng
                Interlocked.Exchange(ref _framePending, 0);
            }));
        }

        private void ClearPreview()
        {
            RunOnUi(() =>
            {
                var old = Preview.Image;
                Preview = ImageDisplay.Empty;              // đặt lại trước, rồi mới giải phóng ảnh
                old?.Dispose();
                _frameCount = 0;
                Interlocked.Exchange(ref _framePending, 0);
            });
        }

        [RelayCommand]
        private void ToggleLive()
        {
            if (_camera == null || !_camera.IsConnected) { Message = "Chưa kết nối camera"; return; }

            if (IsLive)
            {
                _camera.StopGrabbing();
                IsLive = false;
                Message = "Đã dừng xem trực tiếp";
                return;
            }

            _camera.StartGrabbing();
            IsLive = true;
            Message = Config.GetString("Trigger") == "Liên tục"
                ? "Đang xem trực tiếp"
                : "Đang chờ trigger: bấm Chụp để lấy ảnh (Software) hoặc cấp tín hiệu (Hardware)";
        }

        [RelayCommand]
        private void Snap()
        {
            if (_camera == null || !_camera.IsConnected) { Message = "Chưa kết nối camera"; return; }

            if (!IsLive)
            {
                _camera.StartGrabbing();
                IsLive = true;
            }

            string mode = Config.GetString("Trigger");
            if (mode == "Software")
                Message = _camera.TriggerSoftware() ? "Đã gửi lệnh chụp" : _camera.LastError;
            else if (mode == "Hardware")
                Message = "Đang chờ tín hiệu trigger phần cứng";
            else
                Message = "Camera đang chạy liên tục";
        }
    }
}