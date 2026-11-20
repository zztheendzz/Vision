using Communication.Camera;
using Communication.Configuration;
using Communication.Mitsubishi;
using Communication.Simulated;
using Communication.Transport;

namespace Communication.Drivers
{
    /// <summary>
    /// Chọn lớp kết nối theo loại và Chế độ của thiết bị.
    /// PLC / Robot: Chế độ Mitsubishi (mặc định) → MitsubishiPlc / MitsubishiRobot, Simulated → bản giả lập.
    /// Thêm hãng khác: viết thêm một lớp IPlcDriver / IRobotDriver, thêm giá trị vào ô Chế độ ở DeviceSchemas và một nhánh ở đây.
    /// </summary>
    public static class DeviceFactory
    {
        /// <summary>Tạo kết nối (chưa nối). Trả về null nếu loại này chưa có code kết nối.</summary>
        public static IDeviceLink? CreateLink(DeviceCatalog catalog, DeviceConfig d)
        {
            switch (d.Type)
            {
                case DeviceType.PLC:
                    return d.GetString(DeviceSchemas.KeyMode) switch
                    {
                        DeviceSchemas.Mitsubishi => MitsubishiPlc.FromConfig(d),
#if MX_COMPONENT
                        DeviceSchemas.MxComponent => MitsubishiMxPlc.FromConfig(d),
#endif
                        _ => new SimulatedPlc()
                    };
                case DeviceType.Robot:
                    return catalog.IsMeantToConnect(d) ? MitsubishiRobot.FromConfig(d) : SimulatedRobot.FromConfig(d);
                case DeviceType.Camera:
                    return catalog.IsMeantToConnect(d) ? HalconCamera.FromConfig(d) : null;
                case DeviceType.Fixture:
                case DeviceType.PC:
                    var tcp = catalog.GetTcpOptions(d);
                    return tcp == null ? null : new TcpTransport(tcp);
                default:
                    return null;
            }
        }

        /// <summary>Thiết bị này có nút "Kết nối thử" không (có code nối và đang bật).</summary>
        public static bool CanConnect(DeviceCatalog catalog, DeviceConfig d)
        {
            if (!d.Enabled) return false;
            return d.Type switch
            {
                DeviceType.PLC or DeviceType.Robot => true,
                DeviceType.Camera => catalog.IsMeantToConnect(d),
                DeviceType.Fixture or DeviceType.PC => catalog.GetTcpOptions(d) != null,
                _ => false
            };
        }

        /// <summary>Dòng mô tả cách nối để hiển thị.</summary>
        public static string Describe(DeviceCatalog catalog, DeviceConfig d)
        {
            switch (d.Type)
            {
                case DeviceType.PLC:
                case DeviceType.Robot:
                    if (!catalog.IsMeantToConnect(d)) return "Giả lập";
#if MX_COMPONENT
                    if (d.Type == DeviceType.PLC && d.GetString(DeviceSchemas.KeyMode) == DeviceSchemas.MxComponent)
                        return $"MX Component trạm {d.GetInt("StationNo", 1)}";
#endif
                    string brand = d.Type == DeviceType.PLC ? "Mitsubishi MC" : "Mitsubishi MELFA";
                    return $"{brand} {d.GetString(DeviceSchemas.KeyIp)}:{d.GetInt(DeviceSchemas.KeyPort)}";
                case DeviceType.Fixture:
                case DeviceType.PC:
                    if (!catalog.IsMeantToConnect(d)) return "Giả lập";
                    return $"TCP {d.GetString(DeviceSchemas.KeyIp)}:{d.GetInt(DeviceSchemas.KeyPort)}";
                case DeviceType.Camera:
                    return catalog.IsMeantToConnect(d) ? $"GigE Vision {d.GetString("DeviceId")}" : "Chưa nối";
                default:
                    return "Chưa có code kết nối";
            }
        }
    }
}
