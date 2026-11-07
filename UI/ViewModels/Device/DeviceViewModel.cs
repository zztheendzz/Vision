using Communication.Camera;
using Communication.Configuration;
using Communication.Devices;
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
                        OnPropertyChanged(nameof(IsSimulatedPlc));
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
        public bool IsRobot => Config.Type == DeviceType.Robot;
        /// <summary>Có tab "Thử nghiệm" không (robot dùng tab "Điều khiển" riêng).</summary>
        public bool HasTestTab => IsPlc || IsCamera || CanSendText;
        /// <summary>PLC chạy chế độ Simulated (trong bộ nhớ): cho phép đặt bit bằng tay để thử luồng tự động.</summary>
        public bool IsSimulatedPlc => IsPlc && Config.GetString(DeviceSchemas.KeyMode) == DeviceSchemas.Simulated;
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

        [RelayCommand]
        private Task WriteBitOnAsync() => WriteSimulatedBitAsync(true);

        [RelayCommand]
        private Task WriteBitOffAsync() => WriteSimulatedBitAsync(false);

        // Chỉ cho ghi vào PLC giả lập: ghi vào PLC thật từ ô thử có thể làm máy chạy ngoài ý muốn
        private async Task WriteSimulatedBitAsync(bool value)
        {
            if (!IsSimulatedPlc) { Message = "Chỉ ghi thử được với PLC chế độ Simulated"; return; }
            if (!TryGetPlc(out var plc, out string address)) return;
            try
            {
                await plc.WriteBitAsync(address, value);
                Message = $"Đã ghi bit {address} = {(value ? 1 : 0)}";
            }
            catch (Exception ex)
            {
                Message = $"Ghi bit {address} lỗi: {ex.Message}";
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

        // ---------- Robot: danh sách điểm, chọn điểm cần bù ----------

        [ObservableProperty] private string? _selectedPoint;
        public ObservableCollection<string> RobotPoints { get; } = new();

        partial void OnSelectedPointChanged(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var field = Fields.FirstOrDefault(f => f.Def.Key == "TargetPoint");
            if (field != null) field.Text = value;          // ghi vào cấu hình + đánh dấu đã sửa
        }

        private bool TryGetRobot(out IRobotDriver robot)
        {
            robot = null!;
            if (_owner.Manager.GetLink(Id) is not IRobotDriver driver || !driver.IsConnected)
            {
                Message = "Chưa kết nối robot";
                return false;
            }
            robot = driver;
            return true;
        }

        // Điều khiển robot: chiếm/trả quyền, dừng + reset, servo
        private async Task ControlAsync(Func<IRobotDriver, Task<RobotReply>> action)
        {
            if (!TryGetRobot(out var robot)) return;
            var reply = await action(robot);
            Message = reply.Accepted ? reply.Raw : reply.Error ?? "Lệnh điều khiển thất bại";
        }

        [RelayCommand] private Task TakeControlAsync() => ControlAsync(r => r.TakeControlAsync(stopAndReset: true));
        [RelayCommand] private Task ReleaseControlAsync() => ControlAsync(r => r.ReleaseControlAsync());
        [RelayCommand] private Task StopAndResetAsync() => ControlAsync(r => r.StopAndResetAsync());
        [RelayCommand] private Task ServoOnAsync() => ControlAsync(r => r.SetServoAsync(true));
        [RelayCommand] private Task ServoOffAsync() => ControlAsync(r => r.SetServoAsync(false));

        // Điều khiển thủ công: nhích X, Y, Z, Rz theo bước đã chọn
        public double[] JogStepsMm { get; } = { 0.1, 0.5, 1, 5, 10, 20 };
        public double[] JogStepsDeg { get; } = { 0.1, 0.5, 1, 5, 10 };
        [ObservableProperty] private double _jogStepMm = 1;
        [ObservableProperty] private double _jogStepDeg = 1;
        [ObservableProperty] private int _speedPercent = 10;
        [ObservableProperty] private string _positionText = "";

        // Điều khiển theo tọa độ: nhập X, Y, Z, A, B, C rồi di chuyển tới đó
        [ObservableProperty] private double _targetX;
        [ObservableProperty] private double _targetY;
        [ObservableProperty] private double _targetZ;
        [ObservableProperty] private double _targetA;
        [ObservableProperty] private double _targetB;
        [ObservableProperty] private double _targetC;
        [ObservableProperty] private bool _moveLinear = true;
        private int _targetFlag1 = 7, _targetFlag2;      // cờ cấu hình lấy từ vị trí hiện tại, tay robot không đổi tư thế

        /// <summary>Điền các ô tọa độ bằng vị trí hiện tại của robot (rồi sửa lại số cần đổi).</summary>
        [RelayCommand]
        private async Task FillCurrentPositionAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            var r = await robot.GetCurrentPointAsync();
            if (!r.Ok) { Message = "Đọc vị trí hiện tại lỗi: " + r.Error; return; }

            TargetX = r.Point.X; TargetY = r.Point.Y; TargetZ = r.Point.Z;
            TargetA = r.Point.A; TargetB = r.Point.B; TargetC = r.Point.C;
            _targetFlag1 = r.Point.Flag1; _targetFlag2 = r.Point.Flag2;
            PositionText = r.Point.ToString();
            Message = "Đã điền vị trí hiện tại, sửa số cần đổi rồi bấm Di chuyển";
        }

        [RelayCommand]
        private async Task MoveToCoordinatesAsync()
        {
            if (!TryGetRobot(out var robot)) return;

            var target = new RobotPoint(TargetX, TargetY, TargetZ, TargetA, TargetB, TargetC, _targetFlag1, _targetFlag2);
            string how = MoveLinear ? "đường thẳng (MVS)" : "nội suy khớp (MOV, đường đi có thể cong)";
            if (!_owner.Dialogs.Confirm($"Robot sẽ chuyển động thật tới:\n{target}\nKiểu: {how}\n\nChắc chắn vùng làm việc trống và tay đang gần nút dừng khẩn?"))
                return;

            var speed = await robot.SetSpeedAsync(SpeedPercent);
            if (!speed.Accepted) { Message = speed.Error ?? "Không đặt được tốc độ"; return; }

            var reply = await robot.MoveToAsync(target, MoveLinear);
            Message = reply.Accepted ? reply.Raw : reply.Error ?? "Lệnh di chuyển thất bại";
            if (reply.Accepted)
            {
                await Task.Delay(600);
                PositionText = await robot.GetPositionTextAsync();
            }
        }

        /// <summary>Đi tới điểm đã chọn trong danh sách điểm của chương trình (điểm đã dạy sẵn).</summary>
        [RelayCommand]
        private async Task GoToPointAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            string name = (SelectedPoint ?? "").Trim();
            if (name.Length == 0) { Message = "Chọn một điểm trong danh sách trước (bấm 'Đọc danh sách điểm')"; return; }

            var read = await robot.ReadPointAsync(name);
            if (!read.Ok) { Message = $"Đọc điểm {name} lỗi: {read.Error}"; return; }

            string distanceText = "";
            var now = await robot.GetCurrentPointAsync();
            if (now.Ok)
            {
                double dx = read.Point.X - now.Point.X, dy = read.Point.Y - now.Point.Y, dz = read.Point.Z - now.Point.Z;
                distanceText = $"\nCách vị trí hiện tại: {Math.Sqrt(dx * dx + dy * dy + dz * dz):0.#} mm";
            }

            string how = MoveLinear ? "đường thẳng (MVS)" : "nội suy khớp (MOV, đường đi có thể cong)";
            if (!_owner.Dialogs.Confirm(
                    $"Robot sẽ chuyển động thật tới điểm {name}:\n{read.Point}{distanceText}\nKiểu: {how}\nTốc độ: {SpeedPercent}%\n\nChắc chắn vùng làm việc trống và tay đang gần nút dừng khẩn?"))
                return;

            var speed = await robot.SetSpeedAsync(SpeedPercent);
            if (!speed.Accepted) { Message = speed.Error ?? "Không đặt được tốc độ"; return; }

            // Điểm đã dạy sẵn: không áp giới hạn khoảng cách (đã xác nhận ở trên)
            var reply = await robot.MoveToAsync(read.Point, MoveLinear, enforceDistanceLimit: false);
            Message = reply.Accepted ? $"Đi tới {name}: {reply.Raw}" : reply.Error ?? "Lệnh di chuyển thất bại";
            if (reply.Accepted)
            {
                await Task.Delay(600);
                PositionText = await robot.GetPositionTextAsync();
            }
        }

        /// <summary>Điền tọa độ của điểm đã chọn vào các ô "Di chuyển tới tọa độ" để xem hoặc sửa trước khi đi.</summary>
        [RelayCommand]
        private async Task FillFromPointAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            string name = (SelectedPoint ?? "").Trim();
            if (name.Length == 0) { Message = "Chọn một điểm trong danh sách trước"; return; }

            var read = await robot.ReadPointAsync(name);
            if (!read.Ok) { Message = $"Đọc điểm {name} lỗi: {read.Error}"; return; }

            TargetX = read.Point.X; TargetY = read.Point.Y; TargetZ = read.Point.Z;
            TargetA = read.Point.A; TargetB = read.Point.B; TargetC = read.Point.C;
            _targetFlag1 = read.Point.Flag1; _targetFlag2 = read.Point.Flag2;
            Message = $"Đã điền tọa độ của {name} vào các ô, sửa nếu cần rồi bấm Di chuyển";
        }

        // Gửi thử độ lệch theo cách đã chọn (điểm trung gian hoặc gói tin) để kiểm tra phía robot nhận đúng
        [ObservableProperty] private double _testDx = 1;
        [ObservableProperty] private double _testDy;
        [ObservableProperty] private double _testDrz;

        [RelayCommand]
        private async Task SendTestOffsetAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            var reply = await robot.SendOffsetAsync(TestDx, TestDy, TestDrz, 0);
            Message = reply.Accepted ? reply.Raw : reply.Error ?? "Gửi thử độ lệch thất bại";
        }

        /// <summary>axis: "X+", "X-", "Y+", "Y-", "Z+", "Z-", "R+", "R-" (R = xoay Rz).</summary>
        [RelayCommand]
        private async Task JogAsync(string? axis)
        {
            if (string.IsNullOrEmpty(axis) || axis.Length != 2) return;
            if (!TryGetRobot(out var robot)) return;

            double sign = axis[1] == '-' ? -1 : 1;
            double dx = 0, dy = 0, dz = 0, drz = 0;
            switch (char.ToUpperInvariant(axis[0]))
            {
                case 'X': dx = sign * JogStepMm; break;
                case 'Y': dy = sign * JogStepMm; break;
                case 'Z': dz = sign * JogStepMm; break;
                case 'R': drz = sign * JogStepDeg; break;
                default: return;
            }

            // Đặt tốc độ chậm trước mỗi lần nhích (mặc định 10 %)
            var speed = await robot.SetSpeedAsync(SpeedPercent);
            if (!speed.Accepted) { Message = speed.Error ?? "Không đặt được tốc độ"; return; }

            var reply = await robot.MoveRelativeAsync(dx, dy, dz, drz);
            Message = reply.Accepted ? reply.Raw : reply.Error ?? "Lệnh di chuyển thất bại";
            if (reply.Accepted)
            {
                await Task.Delay(600);        // lệnh không chờ robot đến nơi: đợi một chút rồi đọc lại vị trí
                PositionText = await robot.GetPositionTextAsync();
            }
        }

        [RelayCommand]
        private async Task RefreshPositionAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            PositionText = await robot.GetPositionTextAsync();
        }

        [RelayCommand]
        private async Task ApplySpeedAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            var reply = await robot.SetSpeedAsync(SpeedPercent);
            Message = reply.Accepted ? reply.Raw : reply.Error ?? "Không đặt được tốc độ";
        }

        [RelayCommand]
        private async Task ReadPointListAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            Message = "Đang đọc danh sách điểm từ robot...";
            try
            {
                var names = await robot.ListPointsAsync();
                RobotPoints.Clear();
                foreach (var n in names) RobotPoints.Add(n);

                // Giữ điểm đã chọn trước đó nếu vẫn còn trong danh sách
                string current = Config.GetString("TargetPoint");
                SelectedPoint = names.Contains(current) ? current : null;
                Message = $"Thấy {names.Count} điểm. Chọn điểm cần bù trong danh sách.";
            }
            catch (Exception ex)
            {
                Message = $"Đọc danh sách điểm lỗi: {ex.Message}";
            }
        }

        /// <summary>Thử đường ghi điểm an toàn: đọc điểm rồi ghi lại đúng giá trị đó (điểm không đổi). Thành công nghĩa là ghi điểm hoạt động.</summary>
        [RelayCommand]
        private async Task WritePointBackAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            string name = (SelectedPoint ?? Config.GetString("TargetPoint")).Trim();
            if (name.Length == 0) { Message = "Chọn một điểm trước"; return; }

            var read = await robot.ReadPointAsync(name);
            if (!read.Ok) { Message = $"Đọc điểm {name} lỗi: {read.Error}"; return; }

            var write = await robot.WritePointAsync(name, read.Point);
            Message = write.Accepted
                ? $"Ghi lại {name} thành công ({read.Point.ToMelfa()}), điểm không đổi"
                : $"Ghi điểm {name} lỗi: {write.Error}";
        }

        [RelayCommand]
        private async Task ShowPointAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            string name = SelectedPoint ?? Config.GetString("TargetPoint");
            if (string.IsNullOrWhiteSpace(name)) { Message = "Chọn một điểm trước"; return; }

            var reply = await robot.ReadPointAsync(name.Trim());
            Message = reply.Ok ? $"{name} = {reply.Point}" : $"Đọc điểm {name} lỗi: {reply.Error}";
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