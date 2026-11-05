using Communication.Drivers;

namespace Communication.Simulated
{
    /// <summary>PLC giả lập trong bộ nhớ, dùng khi chưa có PLC thật hoặc khi kiểm thử.</summary>
    public sealed class SimulatedPlc : IPlcDriver
    {
        private readonly Dictionary<string, int> _mem = new(StringComparer.OrdinalIgnoreCase);
        private bool _connected;

        public bool IsConnected => _connected;
        public string Description => "PLC giả lập";

        public Task ConnectAsync(CancellationToken ct = default) { _connected = true; return Task.CompletedTask; }
        public void Disconnect() => _connected = false;
        public void Dispose() => _connected = false;

        private void Require() { if (!_connected) throw new InvalidOperationException("PLC giả lập chưa kết nối."); }

        public Task<bool> ReadBitAsync(string address, CancellationToken ct = default)
        { Require(); return Task.FromResult(_mem.GetValueOrDefault(address) != 0); }

        public Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
        { Require(); _mem[address] = value ? 1 : 0; return Task.CompletedTask; }

        public Task<short> ReadWordAsync(string address, CancellationToken ct = default)
        { Require(); return Task.FromResult((short)_mem.GetValueOrDefault(address)); }

        public Task WriteWordAsync(string address, int value, CancellationToken ct = default)
        { Require(); _mem[address] = value; return Task.CompletedTask; }
    }
}
