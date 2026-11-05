using Communication.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UI.Services;
using UI.ViewModels.RunAndDisplay;

namespace UI.ViewModels.Device
{
    /// <summary>Một tab của một loại thiết bị: cài đặt chung + danh sách thiết bị + thêm / xóa.</summary>
    public partial class DeviceTypeViewModel : ViewModelBase
    {
        private readonly DeviceCatalog _catalog;
        private readonly DeviceConfigViewModel _owner;
        private readonly IDialogService _dialogs;

        public DeviceTypeSchema Schema { get; }

        public string Title => Schema.DisplayName;
        public ObservableCollection<DeviceViewModel> Devices { get; } = new();
        public IReadOnlyList<FieldViewModel> GroupFields { get; }
        public bool HasGroupFields => GroupFields.Count > 0;
        public bool IsFixture => Schema.Type == DeviceType.Fixture;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveDeviceCommand))]
        private DeviceViewModel? _selectedDevice;

        public DeviceTypeViewModel(DeviceTypeSchema schema, DeviceCatalog catalog,
                                   DeviceConfigViewModel owner, IDialogService dialogs)
        {
            Schema = schema;
            _catalog = catalog;
            _owner = owner;
            _dialogs = dialogs;

            GroupFields = schema.GroupFields
                .Select(def => new FieldViewModel(
                    def,
                    () => catalog.GetGroupValue(schema.Type, def.Key),
                    v => catalog.SetGroupValue(schema.Type, def.Key, v),
                    () =>
                    {
                        foreach (var f in GroupFields!) f.RefreshVisibility();
                        _owner.MarkDirty();
                    },
                    k => catalog.GetGroupValue(schema.Type, k)))
                .ToList();
        }

        [RelayCommand]
        private void AddDevice()
        {
            var device = _owner.CreateDevice(Schema.Type);
            SelectedDevice = device;
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void RemoveDevice()
        {
            var device = SelectedDevice;
            if (device == null) return;

            var users = device.IsPlc ? _catalog.UsersOfPlc(device.Config) : new List<DeviceConfig>();
            string message = users.Count == 0
                ? $"Xóa '{device.Name}'?"
                : $"Xóa '{device.Name}'? {users.Count} thiết bị đang dùng PLC này ({string.Join(", ", users.Select(u => u.Name))}) sẽ bị bỏ chọn PLC nguồn.";
            if (!_dialogs.Confirm(message)) return;

            int index = Devices.IndexOf(device);
            _owner.DeleteDevice(device);
            SelectedDevice = Devices.Count == 0 ? null : Devices[Math.Min(index, Devices.Count - 1)];
        }

        private bool HasSelection() => SelectedDevice != null;

        /// <summary>Hiện địa chỉ IPv4 của máy này (khuôn test cần gửi về đúng địa chỉ và "Cổng nhận").</summary>
        [RelayCommand]
        private void ScanIp()
        {
            var addresses = new List<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        addresses.Add($"{ua.Address}  ({nic.Name})");
            }

            _dialogs.ShowInfo(addresses.Count == 0
                ? "Không tìm thấy địa chỉ IPv4 nào đang hoạt động."
                : "Địa chỉ IP của máy này:\n" + string.Join("\n", addresses));
        }
    }
}
