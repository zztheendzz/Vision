using Communication.Camera;
using Communication.Configuration;
using Communication.Devices;
using Communication.Drivers;
using Communication.Fixtures;
using Communication.Transport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HalconDotNet;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Controls;
using UI.Models;
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

        /// <summary>Tra ô theo khóa, để bảng khuôn test gắn từng cột: FieldMap[Ip].Text.</summary>
        public Dictionary<string, FieldViewModel> FieldMap { get; } = new();

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
                        if (Config.Type == DeviceType.Fixture) RebuildSlots();
                        _owner.MarkDirty();
                    },
                    config.GetString))
                .ToList();

            foreach (var f in Fields) FieldMap[f.Def.Key] = f;
            if (Config.Type == DeviceType.Fixture) RebuildSlots();

            if (IsCamera) BuildCameraParams();
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
        /// <summary>Danh sách chọn (PLC nguồn...) hiện tên thiết bị thay vì tên kiểu.</summary>
        public override string ToString() => Name;

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

        /// <summary>Danh sách PLC để chọn (dùng cho ô "PLC nguồn" trong bảng khuôn test).</summary>
        public ObservableCollection<DeviceViewModel> PlcChoices => _owner.Plcs;

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
                if (IsConnected) { _wantConnected = true; AttachCamera(); }
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
            _wantConnected = false;           // tự ngắt thì không tự nối lại
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
                if (Config.Type == DeviceType.Fixture) Log("GỬI", TestText);
            }
            catch (Exception ex)
            {
                Message = "Lỗi gửi: " + ex.Message;
                if (Config.Type == DeviceType.Fixture) Log("LỖI", ex.Message);
            }
        }

        // ---------- Tự nối lại ----------

        /// <summary>Thiết bị đã nối thành công và người dùng chưa bấm Ngắt: mất kết nối thì tự nối lại.</summary>
        private bool _wantConnected;
        private DateTime _lostAt = DateTime.MinValue;
        private DateTime _lastAttempt = DateTime.MinValue;
        private bool _reconnecting;

        /// <summary>
        /// Gọi định kỳ: thiết bị mất kết nối quá "Tự nối lại sau" giây thì thử nối lại; chưa được thì thử tiếp sau cùng khoảng đó.
        /// </summary>
        public async Task AutoReconnectAsync()
        {
            if (!_wantConnected || IsConnected || _reconnecting || !Config.Enabled || Config.Type == DeviceType.PC) return;

            string raw = _catalog.GetGroupValueOrDefault(Config.Type, DeviceSchemas.KeyReconnectSec);
            int seconds = int.TryParse(raw, out int v) ? v : 5;
            if (seconds <= 0) return;

            var since = _lostAt > _lastAttempt ? _lostAt : _lastAttempt;
            if ((DateTime.Now - since).TotalSeconds < seconds) return;

            _lastAttempt = DateTime.Now;
            _reconnecting = true;
            try
            {
                await ConnectAsync();
                _owner.StatusMessage = IsConnected ? $"{Name} đã tự nối lại" : $"{Name}: tự nối lại chưa được, sẽ thử lại sau {seconds} giây";
            }
            finally { _reconnecting = false; }
        }

        /// <summary>Được gọi định kỳ để phát hiện thiết bị tự ngắt kết nối.</summary>
        public void RefreshStatus()
        {
            bool alive = _owner.Manager.IsConnected(Id);
            if (alive == IsConnected) return;

            IsConnected = alive;
            if (!alive)
            {
                _lostAt = DateTime.Now;
                DetachCamera();
                string why = _owner.Manager.LostReason(Id);
                Message = why.Length > 0 ? "Mất kết nối: " + why : "Mất kết nối";
                if (Config.Type == DeviceType.Fixture)
                {
                    IsOpen = null;
                    SensorText = "MẤT KẾT NỐI";
                    FixtureStateText = "Mất kết nối";
                    Log("LỖI", $"MẤT KẾT NỐI {Name}: đường truyền bị ngắt");
                    _owner.StatusMessage = $"Mất kết nối {Name}";
                }
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

        // ---------- Quyền điều khiển robot giữa PLC và app ----------
        // Một bit trên PLC nguồn (mặc định M305): 1 = PLC điều khiển robot, 0 = app điều khiển robot.

        private string AuthorityBit => Config.GetString("IoEnaBit").Trim();

        /// <summary>Ghi bit quyền (false = app giữ quyền, true = trả PLC). Không có PLC nguồn / chưa đặt địa chỉ thì bỏ qua.</summary>
        private async Task<string?> WriteAuthorityAsync(bool plcControls)
        {
            string bit = AuthorityBit;
            var plc = _owner.Manager.PlcFor(Config);
            if (bit.Length == 0 || plc == null) return null;
            try
            {
                await plc.WriteBitAsync(bit, plcControls);
                return $"bit quyền {bit} = {(plcControls ? 1 : 0)} ({(plcControls ? "PLC" : "app")} điều khiển)";
            }
            catch (Exception ex) { return $"không ghi được bit quyền {bit}: {ex.Message}"; }
        }

        /// <summary>Trước khi app điều khiển robot: nếu PLC đang giữ quyền (bit = 1) thì từ chối. Không có PLC / không đặt bit thì cho qua.</summary>
        private async Task<bool> EnsureAppControlAsync()
        {
            string bit = AuthorityBit;
            var plc = _owner.Manager.PlcFor(Config);
            if (bit.Length == 0 || plc == null) return true;
            try
            {
                if (!await plc.ReadBitAsync(bit)) return true;
                Message = $"PLC đang điều khiển robot ({bit} = 1). Bấm 'Chiếm quyền' để app điều khiển";
                return false;
            }
            catch (Exception ex)
            {
                Message = $"Không đọc được bit quyền {bit}: {ex.Message}";
                return false;
            }
        }

        [RelayCommand]
        private async Task TakeControlAsync()
        {
            await ControlAsync(r => r.TakeControlAsync(stopAndReset: true));
            string? note = await WriteAuthorityAsync(plcControls: false);
            if (note != null) Message += "; " + note;
        }

        [RelayCommand]
        private async Task ReleaseControlAsync()
        {
            await ControlAsync(r => r.ReleaseControlAsync());
            string? note = await WriteAuthorityAsync(plcControls: true);
            if (note != null) Message += "; " + note;
        }
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
        private int _targetFlag1 = 7, _targetFlag2;      // cờ cấu hình lấy từ vị trí/điểm đã điền, tay robot không đổi tư thế
        private bool _targetFilled;                       // đã điền các ô từ robot chưa (tránh đi tới Z = 0 do chưa nhập)

        private async Task<bool> FillTargetFromCurrentAsync(IRobotDriver robot)
        {
            var r = await robot.GetCurrentPointAsync();
            if (!r.Ok) { Message = "Đọc vị trí hiện tại lỗi: " + r.Error; return false; }

            TargetX = r.Point.X; TargetY = r.Point.Y; TargetZ = r.Point.Z;
            TargetA = r.Point.A; TargetB = r.Point.B; TargetC = r.Point.C;
            _targetFlag1 = r.Point.Flag1; _targetFlag2 = r.Point.Flag2;
            _targetFilled = true;
            // Hiện cả chữ gốc robot trả về để đối chiếu cờ khi gặp lỗi 2810 (cờ tư thế không khớp)
            string rawText = await robot.GetPositionTextAsync();
            PositionText = $"{r.Point}\nLệnh sẽ gửi: {r.Point.ToMelfa()}\nChữ gốc từ robot: {rawText}";
            return true;
        }

        /// <summary>Điền các ô tọa độ bằng vị trí hiện tại của robot (rồi sửa lại số cần đổi).</summary>
        [RelayCommand]
        private async Task FillCurrentPositionAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            if (await FillTargetFromCurrentAsync(robot))
                Message = "Đã điền vị trí hiện tại, sửa số cần đổi rồi bấm Di chuyển";
        }

        [RelayCommand]
        private async Task MoveToCoordinatesAsync()
        {
            if (!TryGetRobot(out var robot)) return;
            if (!await EnsureAppControlAsync()) return;

            // Chưa điền các ô từ robot thì điền sẵn rồi dừng, để không gửi nhầm đích Z = 0
            // Người dùng tự gõ số vào các ô thì coi như đã nhập (cờ giữ mặc định 7,0 nếu chưa điền từ robot)
            bool typed = TargetX != 0 || TargetY != 0 || TargetZ != 0 || TargetA != 0 || TargetB != 0 || TargetC != 0;
            if (!_targetFilled && !typed)
            {
                if (await FillTargetFromCurrentAsync(robot))
                    Message = "Các ô chưa có số: đã điền vị trí hiện tại. Sửa số cần đổi rồi bấm Di chuyển lại";
                return;
            }

            var target = new RobotPoint(TargetX, TargetY, TargetZ, TargetA, TargetB, TargetC, _targetFlag1, _targetFlag2);
            string how = MoveLinear ? "đường thẳng (MVS)" : "nội suy khớp (MOV, đường đi có thể cong)";
            if (!_owner.Dialogs.Confirm($"Robot sẽ chuyển động thật tới:\n{target}\nKiểu: {how}\nTốc độ: {SpeedPercent}%\n\nChắc chắn vùng làm việc trống và tay đang gần nút dừng khẩn?"))
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
            if (!await EnsureAppControlAsync()) return;
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
            _targetFilled = true;
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
            if (!await EnsureAppControlAsync()) return;

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
            _camera.LiveTimeoutSeconds = Config.GetString("Trigger") == "Liên tục" ? 5 : 0;   // chụp liên tục mà 5 giây không có ảnh = mất camera
            _camera.OnImageGrabbed += OnFrame;
            _camera.OnError += OnCameraError;
            _ = ReadCameraParamsAsync();               // nạp khoảng giá trị và giá trị thật của camera vào thanh trượt
        }

        // ---------- Khuôn test: slot và lệnh điều khiển ----------

        /// <summary>Các slot của khuôn, tạo theo ô "Số slot" trong cấu hình.</summary>
        public ObservableCollection<SlotViewModel> Slots { get; } = new();

        [ObservableProperty] private string _fixtureStateText = "Chưa rõ";

        /// <summary>Khuôn đang mở (true), đóng (false) hoặc chưa biết (null). Khóa an toàn dựa vào đây.</summary>
        public bool? IsOpen { get; private set; }

        private void SetOpen(bool open)
        {
            IsOpen = open;
            FixtureStateText = open ? "Đang mở" : "Đang đóng";
        }
        [ObservableProperty] private string _sensorText = "Chưa kiểm";
        [ObservableProperty] private bool _fixtureBusy;

        /// <summary>Số khuôn dùng trong lệnh (SS).</summary>
        public int FixtureNo => Config.GetInt("FixtureNo", 1);

        private bool _refreshingSlots;

        private void RebuildSlots()
        {
            OnPropertyChanged(nameof(FixtureNo));
            int count = Math.Clamp(Config.GetInt("Slots", 1), 1, 64);

            // Giữ trạng thái các slot còn lại khi đổi số slot
            while (Slots.Count > count) Slots.RemoveAt(Slots.Count - 1);
            while (Slots.Count < count)
            {
                var slot = new SlotViewModel(Slots.Count + 1);
                slot.RegisterEdited += OnSlotRegisterEdited;
                slot.SendRequested += s => _ = SendSlotAsync(s);
                Slots.Add(slot);
            }
            RefreshSlotRegisters();
        }

        /// <summary>Điền thanh ghi từng slot từ ô cấu hình (một địa chỉ thì các slot liên tiếp).</summary>
        private void RefreshSlotRegisters()
        {
            _refreshingSlots = true;
            try
            {
                var resolved = SlotAddressing.Resolve(Config.GetString("ResultReg"), Slots.Count, out _);
                for (int i = 0; i < Slots.Count; i++) Slots[i].SetRegisterQuiet(resolved[i] ?? "");
            }
            finally { _refreshingSlots = false; }
        }

        /// <summary>Người dùng sửa thanh ghi của một slot: ghi lại thành danh sách trong cấu hình (liên tiếp thì chỉ giữ địa chỉ đầu).</summary>
        private void OnSlotRegisterEdited(SlotViewModel changed)
        {
            if (_refreshingSlots) return;

            var field = Fields.FirstOrDefault(f => f.Def.Key == "ResultReg");
            if (field == null) return;

            var parsed = new List<Communication.Plc.PlcAddress>();
            foreach (var slot in Slots)
            {
                if (!Communication.Plc.PlcAddress.TryParse(slot.Register, out var addr, out string? error))
                {
                    Message = $"{slot.Title}: {(slot.Register.Length == 0 ? "chưa nhập thanh ghi" : error)}";
                    RefreshSlotRegisters();     // trả ô về giá trị đang lưu
                    return;
                }
                parsed.Add(addr);
            }

            bool sequential = true;
            for (int i = 1; i < parsed.Count && sequential; i++)
                sequential = parsed[0].TryOffset(i, out var expected) && expected == parsed[i];

            field.Text = sequential ? parsed[0].ToString() : string.Join(", ", parsed.Select(a => a.ToString()));
            Message = sequential ? "Thanh ghi các slot liên tiếp từ " + parsed[0] : "Đã lưu danh sách thanh ghi riêng từng slot";
        }

        // Kết quả chọn để gửi cho cả khuôn
        public IReadOnlyList<SlotState> SlotChoices => SlotViewModel.Choices;
        [ObservableProperty] private SlotState _allState = SlotState.Pass;

        /// <summary>Gửi kết quả đang chọn của một slot vào PLC.</summary>
        private async Task SendSlotAsync(SlotViewModel slot)
        {
            if (slot.State == SlotState.Unknown) { Message = $"{slot.Title}: chọn kết quả trước khi gửi"; return; }
            await WriteSlotResultToPlcAsync(slot.Number, slot.State);
        }

        /// <summary>Đặt cùng một kết quả cho mọi slot của khuôn rồi gửi từng slot vào PLC.</summary>
        [RelayCommand]
        private async Task FixtureSendAllAsync()
        {
            var state = AllState;
            foreach (var slot in Slots.ToList())
            {
                slot.State = state;
                await WriteSlotResultToPlcAsync(slot.Number, state);
            }
            Message = $"Đã gửi {state.ToString().ToUpperInvariant()} cho {Slots.Count} slot";
        }

        private FixtureProtocol Protocol => new(k => _catalog.GetGroupValueOrDefault(DeviceType.Fixture, k));

        [RelayCommand] private Task FixtureOpenAsync() => RunFixtureCommandAsync(FixtureCommand.Open);
        [RelayCommand] private Task FixtureCloseAsync() => RunFixtureCommandAsync(FixtureCommand.Close);
        [RelayCommand] private Task FixtureCheckSensorAsync() => RunFixtureCommandAsync(FixtureCommand.CheckSensor);
        [RelayCommand] private Task FixtureStateAsync() => RunFixtureCommandAsync(FixtureCommand.State);

        /// <summary>
        /// Gửi một lệnh tới khuôn, chờ trả lời một dòng rồi cập nhật trạng thái hiển thị.
        /// automatic = true (vòng chạy tự động): đi qua khóa an toàn. Bấm tay trên giao diện thì không bị chặn.
        /// </summary>
        public async Task RunFixtureCommandAsync(FixtureCommand command, bool automatic = false)
        {
            if (_owner.Manager.GetLink(Id) is not ITransport transport || !transport.IsConnected)
            {
                Message = "Chưa kết nối khuôn này";
                return;
            }
            if (FixtureBusy) return;

            FixtureBusy = true;
            try
            {
                if (automatic && InterlockOn)
                {
                    string? reason = await CheckInterlockAsync(transport, command);
                    if (reason != null)
                    {
                        Message = "Khóa an toàn: " + reason;
                        Log("LỖI", "Khóa an toàn chặn lệnh " + command + ": " + reason);
                        return;
                    }
                }
                await SendFixtureCommandAsync(transport, command);
            }
            catch (Exception ex)
            {
                Message = "Lỗi: " + ex.Message;
                Log("LỖI", ex.Message);
            }
            finally { FixtureBusy = false; }
        }

        // CHECKSENSOR chỉ gửi khi bấm nút hoặc trước khi đóng khuôn lúc chạy tự động; không kiểm tra liên tục.
        private bool _quietPoll;     // giữ cho Log() (không còn dùng để nén nhật ký)

        private async Task SendFixtureCommandAsync(ITransport transport, FixtureCommand command, int timeoutMs = 5000)
        {
            var protocol = Protocol;
            int ss = FixtureNo;
            string text = protocol.BuildCommand(command, ss);
            Message = $"Gửi: {text}";
            Log("GỬI", text);

            // Khuôn có thể trả lời trên chính kết nối lệnh, hoặc gửi về "Cổng nhận" (TCP Split): cái nào đến trước thì dùng
            _pendingCommand = command;
            var fromListener = _replyFromListener = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var request = transport.RequestAsync(text, timeoutMs);
                if (await Task.WhenAny(request, fromListener.Task) == fromListener.Task)
                {
                    _ = request.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);   // yêu cầu bỏ dở, nuốt lỗi quá hạn
                    return;                                          // dòng trả lời đã được ghi nhật ký và áp dụng ở cổng nhận
                }

                string reply = await request;
                Log("NHẬN", reply);
                ApplyFixtureReply(command, reply, ss, protocol);
            }
            finally
            {
                _pendingCommand = null;
                _replyFromListener = null;
            }
        }

        private FixtureCommand? _pendingCommand;
        private TaskCompletionSource<string>? _replyFromListener;

        /// <summary>
        /// Dòng nhận được ở "Cổng nhận" có phải trả lời lệnh của khuôn này không (OPEN_01_OK, CLOSE_01_ERROR, YES_01, OPENED_01,
        /// OPEN_ALL_OK...). Khớp thì cập nhật như khi trả lời đến trên kết nối lệnh. Gọi trên luồng giao diện.
        /// </summary>
        public bool TryApplyReply(string line)
        {
            var protocol = Protocol;
            int ss = FixtureNo;

            var order = new List<FixtureCommand>();
            if (_pendingCommand is { } pending) order.Add(pending);          // lệnh đang chờ trả lời thì thử trước
            order.AddRange(Enum.GetValues<FixtureCommand>().Where(c => !order.Contains(c)));

            foreach (var command in order)
            {
                bool matched = command == FixtureCommand.CheckSensor
                    ? protocol.InterpretSensor(line, ss, Slots.Count).Matched
                    : protocol.Interpret(command, line, ss).Matched;
                if (!matched) continue;

                ApplyFixtureReply(command, line, ss, protocol);
                _replyFromListener?.TrySetResult(line);
                return true;
            }
            return false;
        }

        // ---------- Khóa an toàn đóng / mở (chỉ cho chạy tự động; bấm tay không bị chặn) ----------
        //  Đóng: chỉ khi khuôn đang mở và có hàng (tự kiểm CHECKSENSOR trước khi đóng).
        //  Mở:   không mở khi có slot đang test (BUSY) hoặc đã PASS; chỉ mở khi FAIL / REPAIR (retest).
        //  Va chạm robot: khuôn nằm ngay trên không được mở khi khuôn nằm ngay dưới đang mở (hoặc chưa rõ).

        private bool InterlockOn => _catalog.GetGroupValueOrDefault(DeviceType.Fixture, "Interlock") != "false";

        /// <summary>Số khuôn nằm ngay dưới khuôn này (0 = không có).</summary>
        public int BelowNo => Config.GetInt("BelowNo", 0);

        private IEnumerable<DeviceViewModel> AllFixtures()
            => _owner.Devices.Where(d => d.Config.Type == DeviceType.Fixture && d.Enabled);

        /// <summary>Trả về lý do không được đóng, hoặc null nếu đóng được.</summary>
        private string? CloseBlockReason()
        {
            if (IsOpen == false) return $"{Name}: khuôn đang đóng rồi";
            if (Slots.All(x => x.SampleText.Length == 0)) return $"{Name}: chưa biết có hàng không (kiểm sensor chưa được)";
            if (!Slots.Any(x => x.SampleText == "Có bản")) return $"{Name}: không có hàng, không được đóng";
            return null;
        }

        /// <summary>Trả về lý do không được mở, hoặc null nếu mở được. ignoreBelow: bỏ qua luật khuôn dưới (OPEN ALL xét riêng).</summary>
        private string? OpenBlockReason(bool ignoreBelow = false)
        {
            if (IsOpen == true) return $"{Name}: khuôn đang mở rồi";

            var busy = Slots.FirstOrDefault(x => x.State == SlotState.Busy);
            if (busy != null) return $"{Name}: {busy.Title} đang test, chưa được mở";
            var pass = Slots.FirstOrDefault(x => x.State == SlotState.Pass);
            if (pass != null) return $"{Name}: {pass.Title} đang PASS, chỉ khuôn FAIL / REPAIR mới được mở";

            if (!ignoreBelow && BelowNo > 0)
            {
                var below = AllFixtures().FirstOrDefault(d => d != this && d.FixtureNo == BelowNo);
                if (below != null)
                {
                    if (below.IsOpen == true) return $"{Name}: khuôn dưới ({below.Name}, khuôn {BelowNo:00}) đang mở, mở lên sẽ va chạm robot";
                    if (below.IsOpen == null) return $"{Name}: chưa biết khuôn dưới ({below.Name}) đang mở hay đóng, bấm 'Kiểm trạng thái' ở khuôn đó trước";
                }
            }
            return null;
        }

        private async Task<string?> CheckInterlockAsync(ITransport transport, FixtureCommand command)
        {
            switch (command)
            {
                case FixtureCommand.Open:
                    return OpenBlockReason();

                case FixtureCommand.Close:
                    await SendFixtureCommandAsync(transport, FixtureCommand.CheckSensor);   // luôn lấy hiện trạng hàng trước khi đóng
                    return CloseBlockReason();

                case FixtureCommand.OpenAll:
                    {
                        foreach (var d in AllFixtures())
                        {
                            if (d.IsOpen == true) continue;
                            string? r = d.OpenBlockReason(ignoreBelow: true);
                            if (r != null) return r;
                            if (d.BelowNo > 0 && AllFixtures().Any(x => x != d && x.FixtureNo == d.BelowNo))
                                return $"OPEN ALL mở cùng lúc {d.Name} và khuôn dưới nó (khuôn {d.BelowNo:00}), sẽ va chạm robot; mở từng khuôn, khuôn dưới đóng trước";
                        }
                        return null;
                    }

                case FixtureCommand.CloseAll:
                    {
                        foreach (var d in AllFixtures())
                        {
                            if (d.IsOpen == false) continue;
                            if (d._owner.Manager.GetLink(d.Id) is not ITransport t || !t.IsConnected)
                                return $"{d.Name}: chưa kết nối, không kiểm được có hàng";
                            await d.SendFixtureCommandAsync(t, FixtureCommand.CheckSensor);
                            string? r = d.CloseBlockReason();
                            if (r != null) return r;
                        }
                        return null;
                    }
            }
            return null;
        }

        private void ApplyFixtureReply(FixtureCommand command, string reply, int ss, FixtureProtocol protocol)
        {
            if (command == FixtureCommand.CheckSensor)
            {
                ApplySensorReply(reply, ss, protocol);
                return;
            }

            var r = protocol.Interpret(command, reply, ss);
            if (!r.Matched)
            {
                // Có thể là dòng kết quả slot trả về thay cho trả lời lệnh
                if (!ApplyLine(reply)) Message = $"Trả lời không khớp mẫu nào: {reply}";
                else Message = $"Nhận: {reply}";
                return;
            }

            switch (command)
            {
                case FixtureCommand.Open:
                case FixtureCommand.OpenAll:
                    if (r.Positive) SetOpen(true);
                    break;
                case FixtureCommand.Close:
                case FixtureCommand.CloseAll:
                    if (r.Positive) SetOpen(false);
                    break;
                case FixtureCommand.State:
                    SetOpen(r.Positive);
                    break;
            }

            string verdict = r.Positive ? "OK" : "LỖI";
            Message = r.Slots.Length > 0 ? $"{command}: {verdict} (slot: {r.Slots})" : $"{command}: {verdict} ({reply})";
        }

        /// <summary>Đọc trả lời CHECKSENSOR (YES_{SS} / NO_{SS}, có thể kèm trạng thái từng slot) rồi cập nhật slot và bit có bản.</summary>
        private void ApplySensorReply(string reply, int ss, FixtureProtocol protocol)
        {
            var r = protocol.InterpretSensor(reply, ss, Slots.Count);
            if (!r.Matched)
            {
                Message = ApplyLine(reply) ? $"Nhận: {reply}" : $"Trả lời không khớp mẫu nào: {reply}";
                return;
            }
            if (r.Slots == null)
            {
                Message = $"CHECKSENSOR: {r.Warning} ({reply})";
                return;
            }

            for (int i = 0; i < Slots.Count; i++) Slots[i].SampleText = r.Slots[i] ? "Có bản" : "Trống";
            int have = r.Slots.Count(x => x);
            SensorText = have == 0 ? "Không có hàng" : have == r.Slots.Length ? "Có hàng" : $"Có hàng {have}/{r.Slots.Length}";
            Message = $"CHECKSENSOR: {SensorText} ({reply})";
            _ = WriteSampleBitsAsync(r.Slots);
        }

        /// <summary>
        /// Đọc một dòng kết quả slot (ví dụ P_01_02) và cập nhật slot tương ứng nếu đúng số khuôn này.
        /// Trả về true nếu dòng khớp một mẫu kết quả.
        /// </summary>
        public bool ApplyLine(string line)
        {
            if (!Protocol.TryParseSlotResult(line, out int ss, out int nn, out var state)) return false;
            if (ss != FixtureNo) return false;

            RunOnUi(() =>
            {
                var slot = Slots.FirstOrDefault(x => x.Number == nn);
                if (slot != null) slot.State = state;
            });
            _ = WriteSlotResultToPlcAsync(nn, state);
            return true;
        }

        /// <summary>Ghi mã kết quả của một slot vào thanh ghi PLC của slot đó (qua PLC nguồn đã chọn), rồi đọc lại để kiểm tra.</summary>
        /// <summary>Hiện thông báo và ghi vào nhật ký (kind PLC, hoặc LỖI nếu nội dung báo lỗi).</summary>
        private void ReportPlc(string text)
            => RunOnUi(() =>
            {
                Message = text;
                Log(text.Contains("lỗi", StringComparison.OrdinalIgnoreCase) || text.Contains("nhưng đọc lại") ? "LỖI" : "PLC", text);
            });

        private void Log(string kind, string text)
        {
            if (_quietPoll && kind != "LỖI") return;          // kiểm tra định kỳ: chỉ ghi nhật ký khi có lỗi
            _owner.AddFixtureLog(Name, kind, text);
        }

        private async Task WriteSlotResultToPlcAsync(int slotNumber, SlotState state)
        {
            try
            {
                int? code = Protocol.PlcCode(state);
                if (code == null) { ReportPlc($"Slot {slotNumber}: {state.ToString().ToUpperInvariant()} không có mã gửi PLC"); return; }

                var plc = _owner.Manager.PlcFor(Config);
                if (plc == null) { ReportPlc($"Slot {slotNumber}: chưa có PLC kết nối để ghi mã {code}"); return; }

                var addresses = SlotAddressing.Resolve(Config.GetString("ResultReg"), Slots.Count, out string? warning);
                string? address = slotNumber >= 1 && slotNumber <= addresses.Length ? addresses[slotNumber - 1] : null;
                if (address == null)
                {
                    ReportPlc($"Slot {slotNumber}: chưa có thanh ghi kết quả" + (warning != null ? $" ({warning})" : ""));
                    return;
                }

                await plc.WriteWordAsync(address, code.Value);
                short back = await plc.ReadWordAsync(address);
                ReportPlc(back == code.Value
                    ? $"Slot {slotNumber}: {state.ToString().ToUpperInvariant()} → ghi {address} = {code}"
                    : $"Slot {slotNumber}: ghi {address} = {code} nhưng đọc lại được {back}");
            }
            catch (Exception ex)
            {
                ReportPlc($"Slot {slotNumber}: lỗi ghi PLC: {ex.Message}");
            }
        }

        /// <summary>Ghi cờ "có bản" của từng slot (1 = có bản) vào bit riêng của slot đó.</summary>
        private async Task WriteSampleBitsAsync(bool[] hasSample)
        {
            try
            {
                var plc = _owner.Manager.PlcFor(Config);
                if (plc == null) return;

                var addresses = SlotAddressing.Resolve(Config.GetString("HasSampleBit"), hasSample.Length, out _);
                for (int i = 0; i < addresses.Length; i++)
                {
                    if (addresses[i] == null) continue;
                    await plc.WriteBitAsync(addresses[i]!, hasSample[i]);
                    ReportPlc($"Slot {i + 1}: ghi bit có bản {addresses[i]} = {(hasSample[i] ? 1 : 0)}");
                }
            }
            catch (Exception ex)
            {
                ReportPlc("Lỗi ghi bit có bản: " + ex.Message);
            }
        }

        // ---------- Camera: chỉnh thông số ảnh / camera khi xem trực tiếp ----------

        /// <summary>Các thông số chỉnh bằng thanh trượt. Tên theo camera GenICam; camera không có thì ô bị khóa.</summary>
        public ObservableCollection<CameraParamViewModel> CameraParams { get; } = new();

        private CameraParamViewModel? _exposureParam;
        private CameraParamViewModel? _gainParam;

        private void BuildCameraParams()
        {
            _exposureParam = new CameraParamViewModel("ExposureTime", "Phơi sáng", "µs",
                v => _camera?.SetNumericParam("ExposureTime", v, integer: false, "Phơi sáng") ?? false);
            _gainParam = new CameraParamViewModel("Gain", "Gain", "",
                v => _camera?.SetNumericParam("Gain", v, integer: false, "Gain") ?? false);
            var gamma = new CameraParamViewModel("Gamma", "Gamma", "",
                v => _camera?.SetNumericParam("Gamma", v, integer: true, "Gamma") ?? false);
            var contrast = new CameraParamViewModel("Contrast", "Tương phản", "",
                v => _camera?.SetNumericParam("Contrast", v, integer: true, "Tương phản") ?? false);
            var fps = new CameraParamViewModel("AcquisitionFrameRate", "Tốc độ khung hình", "fps",
                v => _camera?.SetFrameRate(v) ?? false);

            // Mỗi lần chỉnh xong thì ghi vào cấu hình và lưu luôn, để mở lại app vẫn còn
            foreach (var (param, key) in new[] { (_exposureParam, "Exposure"), (_gainParam, "Gain"), (gamma, "CamGamma"), (contrast, "CamContrast"), (fps, "CamFrameRate") })
            {
                CameraParams.Add(param);
                string k = key;
                param.Applied += v => PersistCameraParam(k, v);
            }
        }

        private void PersistCameraParam(string key, double value)
        {
            var field = Fields.FirstOrDefault(f => f.Def.Key == key);
            if (field == null) return;
            field.Text = value.ToString("0.###", CultureInfo.InvariantCulture);   // đánh dấu đã sửa
            _owner.TrySaveQuietly();
        }

        /// <summary>Đọc lại khoảng giá trị và giá trị hiện tại của từng thông số từ camera.</summary>
        [RelayCommand]
        private async Task ReadCameraParamsAsync()
        {
            var camera = _camera;
            if (camera == null || !camera.IsConnected) { Message = "Chưa kết nối camera"; return; }

            var items = CameraParams.ToList();
            var results = await Task.Run(() => items.Select(p =>
            {
                bool ok = camera.TryReadNumericParam(p.HalconName, out double current, out double min, out double max);
                return (Param: p, Ok: ok, Current: current, Min: min, Max: max);
            }).ToList());

            foreach (var r in results) r.Param.Load(r.Ok, r.Current, r.Min, r.Max);

            int count = results.Count(r => r.Ok);
            Message = count == 0
                ? "Camera không trả về khoảng giá trị của thông số nào"
                : $"Đọc được {count}/{results.Count} thông số của camera";
        }

        /// <summary>Ghi cả 5 thông số hiện tại vào cấu hình rồi lưu (thanh trượt cũng tự ghi mỗi lần chỉnh).</summary>
        [RelayCommand]
        private void SaveCameraParamsToConfig()
        {
            int saved = 0;
            var keys = new[] { "Exposure", "Gain", "CamGamma", "CamContrast", "CamFrameRate" };
            for (int i = 0; i < CameraParams.Count && i < keys.Length; i++)
            {
                var param = CameraParams[i];
                if (!param.IsSupported) continue;
                var field = Fields.FirstOrDefault(f => f.Def.Key == keys[i]);
                if (field == null) continue;
                field.Text = param.Value.ToString("0.###", CultureInfo.InvariantCulture);
                saved++;
            }

            if (saved == 0) { Message = "Chưa có giá trị để ghi (camera chưa kết nối hoặc không báo khoảng giá trị)"; return; }
            Message = _owner.TrySaveQuietly()
                ? $"Đã lưu {saved} thông số camera vào cấu hình"
                : "Đã ghi vào cấu hình nhưng chưa lưu được (còn ô cấu hình sai). Bấm Lưu cấu hình sau khi sửa.";
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
            RunOnUi(() => { foreach (var p in CameraParams) p.Load(false, 0, 0, 0); });
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