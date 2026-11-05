using System.Globalization;
using Communication.Configuration;
using Communication.Drivers;
using Melfa.Robot;   // Melfa.Robot 0.1.22.6 (CHƯA kiểm chứng namespace / tên lớp: chỉnh trong phần "GỌI THƯ VIỆN" bên dưới)

namespace Communication.Mitsubishi
{
    /// <summary>
    /// Robot Mitsubishi MELFA qua Melfa.Robot (R-protocol; cổng Ethernet của bộ điều khiển đặt chế độ Server).
    /// Mọi lời gọi tới thư viện nằm gọn trong 4 hàm ở phần "GỌI THƯ VIỆN" để dễ sửa khi API thật khác.
    /// "Accepted" chỉ có nghĩa là lệnh đã gửi mà không lỗi, KHÔNG có nghĩa robot đã thực hiện xong.
    /// Mẫu lệnh độ bù mặc định CHƯA được kiểm chứng trên RT ToolBox; mẫu lệnh NG mặc định để trống (báo lỗi, không gửi bừa).
    /// </summary>
    public sealed class MitsubishiRobot : IRobotDriver
    {
        public sealed record Settings(
            string Ip, int Port, int RobotNo, string ClientName, bool EnableOnConnect,
            string OffsetTemplate, string RejectTemplate, double MaxOffsetMm, double MaxOffsetDeg);

        private readonly Settings _s;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private object? _robot;
        private bool _connected;

        public MitsubishiRobot(Settings settings) => _s = settings;

        public static MitsubishiRobot FromConfig(DeviceConfig d) => new(new Settings(
            d.GetString(DeviceSchemas.KeyIp),
            d.GetInt(DeviceSchemas.KeyPort, 10009),
            d.GetInt("RobotNo", 1),
            d.GetString("ClientName"),
            d.GetBool("EnableOnConnect"),
            d.GetString("OffsetCommand"),
            d.GetString("RejectCommand"),
            d.GetDouble("MaxOffsetMm", 50),
            d.GetDouble("MaxOffsetDeg", 30)));

        public bool IsConnected => _connected;
        public string Description => $"Mitsubishi MELFA {_s.Ip}:{_s.Port}";

        // ---------- Logic của mình (không phụ thuộc thư viện) ----------

        /// <summary>Thay {dx} {dy} {drz} {id} bằng số, luôn dùng dấu chấm thập phân.</summary>
        public static string FillTemplate(string template, double dx, double dy, double drz, int id)
        {
            var inv = CultureInfo.InvariantCulture;
            return template
                .Replace("{dx}", dx.ToString("0.###", inv))
                .Replace("{dy}", dy.ToString("0.###", inv))
                .Replace("{drz}", drz.ToString("0.###", inv))
                .Replace("{id}", id.ToString(inv));
        }

        /// <summary>Kiểm tra độ bù trước khi gửi. Trả về lỗi hoặc null.</summary>
        public string? Validate(double dx, double dy, double drz)
        {
            if (double.IsNaN(dx) || double.IsNaN(dy) || double.IsNaN(drz) ||
                double.IsInfinity(dx) || double.IsInfinity(dy) || double.IsInfinity(drz))
                return "Độ bù không phải số hợp lệ.";
            if (Math.Abs(dx) > _s.MaxOffsetMm || Math.Abs(dy) > _s.MaxOffsetMm)
                return $"Độ bù ({dx:0.###}, {dy:0.###}) mm vượt giới hạn ±{_s.MaxOffsetMm} mm, không gửi.";
            if (Math.Abs(drz) > _s.MaxOffsetDeg)
                return $"Góc bù {drz:0.###}° vượt giới hạn ±{_s.MaxOffsetDeg}°, không gửi.";
            return null;
        }

        public async Task<RobotReply> SendOffsetAsync(double dxMm, double dyMm, double drzDeg, int cycleId, CancellationToken ct = default)
        {
            if (!_connected) return RobotReply.Fail("Robot chưa kết nối, không gửi độ bù.");
            if (string.IsNullOrWhiteSpace(_s.OffsetTemplate))
                return RobotReply.Fail("Chưa cấu hình lệnh gửi độ bù cho robot.");
            string? invalid = Validate(dxMm, dyMm, drzDeg);
            if (invalid != null) return RobotReply.Fail(invalid);

            return await SendAsync(FillTemplate(_s.OffsetTemplate, dxMm, dyMm, drzDeg, cycleId), ct).ConfigureAwait(false);
        }

        public async Task<RobotReply> SendRejectAsync(int cycleId, CancellationToken ct = default)
        {
            if (!_connected) return RobotReply.Fail("Robot chưa kết nối, không báo NG.");
            if (string.IsNullOrWhiteSpace(_s.RejectTemplate))
                return RobotReply.Fail("Chưa cấu hình lệnh báo NG cho robot, không gửi gì.");

            return await SendAsync(FillTemplate(_s.RejectTemplate, 0, 0, 0, cycleId), ct).ConfigureAwait(false);
        }

        private async Task<RobotReply> SendAsync(string command, CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() => LibExecute(command), ct).ConfigureAwait(false);
                return RobotReply.Ok(command);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return RobotReply.Fail($"Gửi lệnh robot lỗi: {ex.Message}");
            }
            finally { _lock.Release(); }
        }

        public Task ConnectAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            Disconnect();
            LibConnect();
            _connected = true;
        }, ct);

        public void Disconnect()
        {
            _connected = false;
            try { LibClose(); } catch { }
            _robot = null;
        }

        public void Dispose() { Disconnect(); _lock.Dispose(); }

        // ---------- GỌI THƯ VIỆN Melfa.Robot (chưa kiểm chứng; sửa ở đây nếu API khác) ----------

        private MelfaRobot Lib => (MelfaRobot)(_robot ?? throw new InvalidOperationException("Chưa kết nối robot."));

        private void LibConnect()
        {
            var robot = new MelfaRobot("ip",10001);
            robot.Connect();
            robot.Open("name");
            if (_s.EnableOnConnect)
            {
                robot.OperationEnabled=true;
                robot.ServoOn=true;
            }
            _robot = robot;
        }

        private void LibExecute(string command) => Lib.Execute(command);

        private void LibClose()
        {
            if (_robot is not MelfaRobot r) return;
            try { r.Close(); } catch { }
            try { r.Disconnect(); } catch { }
            try { (r as IDisposable)?.Dispose(); } catch { }
        }
    }
}
