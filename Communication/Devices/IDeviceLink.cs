namespace Communication.Drivers
{
    /// <summary>Một kết nối tới thiết bị (PLC, robot, TCP...).</summary>
    public interface IDeviceLink : IDisposable
    {
        bool IsConnected { get; }
        /// <summary>Mô tả ngắn để hiển thị, ví dụ "Mitsubishi MC 192.168.0.195:3006".</summary>
        string Description { get; }
        Task ConnectAsync(CancellationToken ct = default);
        void Disconnect();
    }
}
