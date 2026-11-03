using Communication.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;

namespace UI.ViewModels
{
    /// <summary>Quản lý danh sách thiết bị: thêm, xóa, lưu, kết nối tất cả, và cập nhật trạng thái định kỳ.</summary>
    public partial class DeviceConfigViewModel : ViewModelBase, IDisposable
    {
        private readonly DeviceConfigStore _store;
        private readonly IDialogService _dialogs;
        private readonly DispatcherTimer _timer;

        public ObservableCollection<DeviceViewModel> Devices { get; } = new();

        // Danh sách lựa chọn cho các ô chọn (ComboBox)
        public IReadOnlyList<DeviceType> DeviceTypes { get; } = Enum.GetValues<DeviceType>();
        public IReadOnlyList<TerminatorKind> TerminatorKinds { get; } = Enum.GetValues<TerminatorKind>();
        public IReadOnlyList<string> Encodings { get; } = new[] { "ASCII", "UTF-8" };

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveDeviceCommand))]
        private DeviceViewModel? _selectedDevice;

        [ObservableProperty]
        private string _statusMessage = "";

        public DeviceConfigViewModel(DeviceConfigStore store, IDialogService dialogs)
        {
            _store = store;
            _dialogs = dialogs;

            foreach (var config in _store.Load())
                Devices.Add(new DeviceViewModel(config));
            SelectedDevice = Devices.FirstOrDefault();

            // Cứ 2 giây kiểm tra lại xem thiết bị còn nối không
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) =>
            {
                foreach (var device in Devices) device.RefreshStatus();
            };
            _timer.Start();

            // Thiết bị nào đánh dấu "Tự kết nối" thì kết nối ngay khi mở phần mềm
            foreach (var device in Devices.Where(d => d.AutoConnect))
                _ = device.ConnectCommand.ExecuteAsync(null);
        }

        [RelayCommand]
        private void AddDevice()
        {
            var device = new DeviceViewModel(new DeviceConfig { Name = MakeNewName() });
            Devices.Add(device);
            SelectedDevice = device;
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void RemoveDevice()
        {
            var device = SelectedDevice;
            if (device == null) return;
            if (!_dialogs.Confirm($"Xóa thiết bị '{device.Name}'?")) return;

            device.Dispose();                        // ngắt kết nối trước khi xóa
            Devices.Remove(device);
            SelectedDevice = Devices.FirstOrDefault();
        }

        private bool HasSelection() => SelectedDevice != null;

        [RelayCommand]
        private void Save()
        {
            string? error = Validate();
            if (error != null)
            {
                _dialogs.ShowError(error);
                return;
            }

            try
            {
                _store.Save(Devices.Select(d => d.ToConfig()));
                StatusMessage = $"Đã lưu lúc {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                _dialogs.ShowError("Không lưu được: " + ex.Message);
            }
        }

        [RelayCommand]
        private async Task ConnectAllAsync()
        {
            await Task.WhenAll(Devices.Select(d => d.ConnectCommand.ExecuteAsync(null)));
        }

        [RelayCommand]
        private void DisconnectAll()
        {
            foreach (var device in Devices) device.DisconnectCommand.Execute(null);
        }

        /// <summary>Kiểm tra dữ liệu trước khi lưu. Trả về nội dung lỗi, hoặc null nếu hợp lệ.</summary>
        private string? Validate()
        {
            foreach (var d in Devices)
            {
                if (string.IsNullOrWhiteSpace(d.Name))
                    return "Có thiết bị chưa đặt tên.";
                if (string.IsNullOrWhiteSpace(d.IpAddress))
                    return $"Thiết bị '{d.Name}' chưa có địa chỉ IP.";
                if (d.Port < 1 || d.Port > 65535)
                    return $"Thiết bị '{d.Name}': cổng phải từ 1 đến 65535.";
                if (d.TimeoutMs < 100)
                    return $"Thiết bị '{d.Name}': thời gian chờ tối thiểu 100 ms.";
            }

            var duplicate = Devices
                .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                return $"Có hai thiết bị trùng tên '{duplicate.Key}'.";

            return null;
        }

        private string MakeNewName()
        {
            int i = Devices.Count + 1;
            while (Devices.Any(d => d.Name == $"Thiết bị {i}")) i++;
            return $"Thiết bị {i}";
        }

        public void Dispose()
        {
            _timer.Stop();
            foreach (var device in Devices) device.Dispose();
        }
    }
}
