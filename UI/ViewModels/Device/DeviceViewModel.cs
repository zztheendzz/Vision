using Communication.Configuration;
using Communication.Drivers;
using Communication.Transport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
                Message = "Thiết bị đang tắt";
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
            if (!alive) Message = "Mất kết nối";
        }

        public void Dispose() => _owner.Manager.Disconnect(Id);
    }
}
