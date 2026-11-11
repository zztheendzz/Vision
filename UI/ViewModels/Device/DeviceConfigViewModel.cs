using Communication.Configuration;
using Communication.Drivers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using UI.Services;

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
            _timer.Tick += (_, _) => { foreach (var d in Devices) d.RefreshStatus(); };
            _timer.Start();
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

        internal void MarkDirty()
        {
            IsDirty = true;
            StatusMessage = "Có thay đổi chưa lưu";
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
            Manager.Dispose();
        }
    }
}
