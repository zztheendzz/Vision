using Communication.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels.Device
{
    /// <summary>Dữ liệu cho cửa sổ thả nổi "Robot + camera": chọn robot để điều khiển và camera để xem trực tiếp.</summary>
    public partial class RobotControlViewModel : ViewModelBase
    {
        public IReadOnlyList<DeviceViewModel> Robots { get; }
        public IReadOnlyList<DeviceViewModel> Cameras { get; }

        [ObservableProperty] private DeviceViewModel? _selectedRobot;
        [ObservableProperty] private DeviceViewModel? _selectedCamera;

        public RobotControlViewModel(DeviceConfigViewModel root, DeviceViewModel? robot)
        {
            Robots = root.Devices.Where(d => d.Config.Type == DeviceType.Robot).ToList();
            Cameras = root.Devices.Where(d => d.Config.Type == DeviceType.Camera).ToList();
            SelectedRobot = robot ?? Robots.FirstOrDefault();
            SelectedCamera = Cameras.FirstOrDefault(c => c.IsConnected) ?? Cameras.FirstOrDefault();
        }
    }
}
