using Communication.Configuration;
using Communication.Drivers;
using HslCommunication.Core;               // IReadWriteNet
using HslCommunication.Profinet.Melsec;
using System.IO;    // MelsecMcNet, MelsecMcAsciiNet

namespace Communication.Mitsubishi
{
    /// <summary>
    /// PLC Mitsubishi (giao thức MC, Ethernet) qua HslCommunication 7.0.1.
    /// CHƯA biên dịch / chạy thử với thư viện thật: nếu tên lớp hoặc namespace khác, chỉ sửa trong file này.
    /// Địa chỉ dạng "M303", "D600"... Thư viện HSL bản mới có thể cần mã kích hoạt (license).
    /// </summary>
    public sealed class MitsubishiPlc : IPlcDriver
    {
        private readonly string _ip;
        private readonly int _port;
        private readonly bool _ascii;
        private readonly int _timeoutMs;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private IReadWriteNet? _net;
        private bool _connected;

        public MitsubishiPlc(string ip, int port, bool ascii, int timeoutMs)
        {
            _ip = ip; _port = port; _ascii = ascii; _timeoutMs = timeoutMs;
        }

        public static MitsubishiPlc FromConfig(DeviceConfig d) => new(
            d.GetString(DeviceSchemas.KeyIp),
            d.GetInt(DeviceSchemas.KeyPort, 3006),
            d.GetString("Frame") == "Ascii",
            d.GetInt(DeviceSchemas.KeyTimeoutMs, 3000));

        public bool IsConnected => _connected;
        public string Description => $"Mitsubishi MC {_ip}:{_port} ({(_ascii ? "Ascii" : "Binary")})";

        public Task ConnectAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            Disconnect();
            if (_ascii)
            {
                var plc = new MelsecMcAsciiNet(_ip, _port) { ConnectTimeOut = _timeoutMs, ReceiveTimeOut = _timeoutMs };
                var r = plc.ConnectServer();
                if (!r.IsSuccess) throw new IOException($"Không nối được PLC {_ip}:{_port}: {r.Message}");
                _net = plc;
                _close = plc.ConnectClose;
            }
            else
            {
                var plc = new MelsecMcNet(_ip, _port) { ConnectTimeOut = _timeoutMs, ReceiveTimeOut = _timeoutMs };
                var r = plc.ConnectServer();
                if (!r.IsSuccess) throw new IOException($"Không nối được PLC {_ip}:{_port}: {r.Message}");
                _net = plc;
                _close = plc.ConnectClose;
            }
            _connected = true;
        }, ct);

        private Func<object>? _close;

        public void Disconnect()
        {
            _connected = false;
            try { _close?.Invoke(); } catch { }
            _close = null;
            _net = null;
        }

        private IReadWriteNet Net => _net is { } n && _connected ? n
            : throw new InvalidOperationException("Chưa kết nối PLC " + _ip);

        private async Task<T> RunAsync<T>(Func<IReadWriteNet, T> op, CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try { var net = Net; return await Task.Run(() => op(net), ct).ConfigureAwait(false); }
            finally { _lock.Release(); }
        }

        private static T Check<T>(HslCommunication.OperateResult<T> r, string what)
            => r.IsSuccess ? r.Content : throw new IOException($"PLC: {what} thất bại: {r.Message}");

        private static void Check(HslCommunication.OperateResult r, string what)
        { if (!r.IsSuccess) throw new IOException($"PLC: {what} thất bại: {r.Message}"); }

        public Task<bool> ReadBitAsync(string address, CancellationToken ct = default)
            => RunAsync(n => Check(n.ReadBool(address), "đọc bit " + address), ct);

        public Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
            => RunAsync(n => { Check(n.Write(address, value), "ghi bit " + address); return 0; }, ct);

        public Task<short> ReadWordAsync(string address, CancellationToken ct = default)
            => RunAsync(n => Check(n.ReadInt16(address), "đọc thanh ghi " + address), ct);

        public Task WriteWordAsync(string address, int value, CancellationToken ct = default)
        {
            if (value < short.MinValue || value > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), $"Giá trị {value} ngoài khoảng 16 bit.");
            return RunAsync(n => { Check(n.Write(address, (short)value), "ghi thanh ghi " + address); return 0; }, ct);
        }

        public void Dispose() { Disconnect(); _lock.Dispose(); }
    }
}
