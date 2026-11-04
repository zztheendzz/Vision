using Communication.Configuration;
using Communication.Connection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vision.Graph;

namespace UI.ViewModels.Device
{
    /// <summary>
    /// Một thiết bị trên giao diện: các ô cấu hình + trạng thái kết nối + các lệnh Kết nối / Ngắt / Gửi thử.
    /// Các ô được sửa tự do; chỉ khi bấm Kết nối thì mới đọc giá trị hiện tại để tạo kết nối mới.
    /// </summary>
    public partial class DeviceViewModel : ViewModelBase, IDisposable
    {
        private DeviceConnection? _connection;

        public Guid Id { get; }

        // ---------- Cấu hình (người dùng sửa được) ----------
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private DeviceType _type;
        [ObservableProperty] private string _ipAddress = "";
        [ObservableProperty] private int _port;
        [ObservableProperty] private int _timeoutMs = 3000;
        [ObservableProperty] private bool _autoConnect;
        [ObservableProperty] private string _textEncoding = "ASCII";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustomTerminator))]
        [NotifyPropertyChangedFor(nameof(TerminatorPreview))]
        private TerminatorKind _terminator;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TerminatorPreview))]
        private string _customTerminator = "";

        // ---------- Trạng thái ----------
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private bool _isConnected;

        [ObservableProperty] private string _message = "";
        [ObservableProperty] private string _testText = "";

        public string StatusText => IsConnected ? "Đã kết nối" : "Chưa kết nối";

        /// <summary>Chỉ hiện ô gõ ký tự tùy chọn khi chọn Custom.</summary>
        public bool IsCustomTerminator => Terminator == TerminatorKind.Custom;

        /// <summary>Các byte của ký tự kết thúc, ví dụ "0D 0A". Để người dùng kiểm tra đã đúng ý chưa.</summary>
        public string TerminatorPreview
        {
            get
            {
                byte[] bytes = ToConfig().GetTerminatorBytes();
                return bytes.Length == 0 ? "(không có)" : BitConverter.ToString(bytes).Replace("-", " ");
            }
        }

        public DeviceViewModel(DeviceConfig config)
        {
            Id = config.Id;
            _name = config.Name;
            _type = config.Type;
            _ipAddress = config.IpAddress;
            _port = config.Port;
            _timeoutMs = config.TimeoutMs;
            _autoConnect = config.AutoConnect;
            _textEncoding = config.TextEncoding;
            _terminator = config.Terminator;
            _customTerminator = config.CustomTerminator;
        }

        /// <summary>Đóng gói các ô hiện tại thành DeviceConfig (để lưu file hoặc tạo kết nối).</summary>
        public DeviceConfig ToConfig() => new DeviceConfig
        {
            Id = Id,
            Name = Name,
            Type = Type,
            IpAddress = IpAddress,
            Port = Port,
            TimeoutMs = TimeoutMs,
            AutoConnect = AutoConnect,
            TextEncoding = TextEncoding,
            Terminator = Terminator,
            CustomTerminator = CustomTerminator
        };

        // ---------- Lệnh ----------

        [RelayCommand]
        private async Task ConnectAsync()
        {
            try
            {
                _connection?.Dispose();
                _connection = new DeviceConnection(ToConfig());

                Message = "Đang kết nối...";
                await _connection.ConnectAsync();

                IsConnected = _connection.IsConnected;
                Message = IsConnected ? "Kết nối thành công" : "Không kết nối được";
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
            _connection?.Dispose();
            _connection = null;
            IsConnected = false;
            Message = "Đã ngắt kết nối";
        }

        [RelayCommand]
        private async Task SendTestAsync()
        {
            if (_connection == null || !_connection.IsConnected)
            {
                Message = "Chưa kết nối, không gửi được";
                return;
            }

            try
            {
                await _connection.SendAsync(TestText);
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
            bool alive = _connection?.IsConnected ?? false;
            if (alive == IsConnected) return;

            IsConnected = alive;
            if (!alive) Message = "Mất kết nối";
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
