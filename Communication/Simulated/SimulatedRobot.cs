using System.Globalization;
using Communication.Drivers;

namespace Communication.Simulated
{
    /// <summary>Robot giả lập: nhận mọi lệnh và ghi lại lệnh cuối để xem.</summary>
    public sealed class SimulatedRobot : IRobotDriver
    {
        private bool _connected;

        public bool IsConnected => _connected;
        public string Description => "Robot giả lập";
        /// <summary>Lệnh cuối cùng "gửi" tới robot (để kiểm thử / hiển thị).</summary>
        public string LastCommand { get; private set; } = "";

        public Task ConnectAsync(CancellationToken ct = default) { _connected = true; return Task.CompletedTask; }
        public void Disconnect() => _connected = false;
        public void Dispose() => _connected = false;

        public Task<RobotReply> SendOffsetAsync(double dxMm, double dyMm, double drzDeg, int cycleId, CancellationToken ct = default)
        {
            if (!_connected) return Task.FromResult(RobotReply.Fail("Robot giả lập chưa kết nối."));
            LastCommand = string.Create(CultureInfo.InvariantCulture, $"OFFSET {dxMm:F3} {dyMm:F3} {drzDeg:F3} #{cycleId}");
            return Task.FromResult(RobotReply.Ok(LastCommand));
        }

        public Task<RobotReply> SendRejectAsync(int cycleId, CancellationToken ct = default)
        {
            if (!_connected) return Task.FromResult(RobotReply.Fail("Robot giả lập chưa kết nối."));
            LastCommand = $"REJECT #{cycleId}";
            return Task.FromResult(RobotReply.Ok(LastCommand));
        }
    }
}
