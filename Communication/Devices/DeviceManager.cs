using Communication.Camera;
using Communication.Configuration;

namespace Communication.Drivers
{
    /// <summary>Giữ các kết nối đang mở, theo Id thiết bị. Đóng hết khi Dispose.</summary>
    public sealed class DeviceManager : IDisposable
    {
        private readonly DeviceCatalog _catalog;
        private readonly Dictionary<Guid, IDeviceLink> _links = new();
        private readonly object _gate = new();

        public DeviceManager(DeviceCatalog catalog) => _catalog = catalog;

        /// <summary>Nối thiết bị. Trả về mô tả kết nối, hoặc null nếu thiết bị này không có gì để nối.</summary>
        public async Task<string?> ConnectAsync(DeviceConfig d, CancellationToken ct = default)
        {
            Disconnect(d.Id);
            var link = DeviceFactory.CreateLink(_catalog, d);
            if (link == null) return null;
            try { await link.ConnectAsync(ct).ConfigureAwait(false); }
            catch { link.Dispose(); throw; }

            lock (_gate) _links[d.Id] = link;
            return link.Description;
        }

        public void Disconnect(Guid id)
        {
            IDeviceLink? link;
            lock (_gate)
            {
                if (!_links.Remove(id, out link)) return;
            }
            try { link.Disconnect(); } finally { link.Dispose(); }
        }

        public void DisconnectAll()
        {
            List<IDeviceLink> all;
            lock (_gate) { all = _links.Values.ToList(); _links.Clear(); }
            foreach (var l in all) { try { l.Disconnect(); } catch { } l.Dispose(); }
        }

        public bool IsConnected(Guid id) => GetLink(id)?.IsConnected == true;

        public IDeviceLink? GetLink(Guid id)
        {
            lock (_gate) return _links.TryGetValue(id, out var l) ? l : null;
        }

        /// <summary>Robot đang nối, theo thứ tự trong danh sách thiết bị.</summary>
        public IReadOnlyList<(DeviceConfig Config, IRobotDriver Driver)> ConnectedRobots()
            => Connected<IRobotDriver>(DeviceType.Robot);

        public IReadOnlyList<(DeviceConfig Config, HalconCamera Camera)> ConnectedCameras()
            => Connected<HalconCamera>(DeviceType.Camera);

        public IReadOnlyList<(DeviceConfig Config, IPlcDriver Driver)> ConnectedPlcs()
            => Connected<IPlcDriver>(DeviceType.PLC);

        /// <summary>Cấu hình các robot (kể cả robot chưa nối), để biết cách gửi độ bù khi robot nhận qua PLC.</summary>
        public IReadOnlyList<DeviceConfig> RobotConfigs
            => _catalog.Devices.Where(d => d.Type == DeviceType.Robot).ToList();

        /// <summary>
        /// PLC dùng cho thiết bị d: PLC đã chọn cho thiết bị (nếu có, và phải đang nối); chưa chọn thì PLC đầu tiên đang nối.
        /// Null nếu không có PLC phù hợp.
        /// </summary>
        public IPlcDriver? PlcFor(DeviceConfig d)
        {
            if (d.SourcePlcId is Guid id)
                return GetLink(id) is IPlcDriver { IsConnected: true } chosen ? chosen : null;
            return ConnectedPlcs().Select(p => p.Driver).FirstOrDefault();
        }

        private List<(DeviceConfig, T)> Connected<T>(DeviceType type) where T : class, IDeviceLink
        {
            var result = new List<(DeviceConfig, T)>();
            foreach (var d in _catalog.Devices.Where(x => x.Type == type))
                if (GetLink(d.Id) is T t && t.IsConnected) result.Add((d, t));
            return result;
        }

        public void Dispose() => DisconnectAll();
    }
}
