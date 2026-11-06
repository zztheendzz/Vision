using Communication.Configuration;
using Communication.Devices;
using Communication.Drivers;
using Communication.Mitsubishi;
using System.Globalization;

namespace Communication.Simulated
{
    /// <summary>Robot giả lập: nhận mọi lệnh, giữ vài điểm mẫu trong bộ nhớ và ghi lại lệnh cuối để xem.</summary>
    //public sealed class SimulatedRobot : IRobotDriver
    //{
    //    private readonly object _gate = new();
    //    private readonly Dictionary<string, RobotPoint> _points = new(StringComparer.OrdinalIgnoreCase)
    //    {
    //        ["P1"] = new(300, 100, 50, 180, 0, 90, 7, 0),
    //        ["P2"] = new(250, -80, 50, 180, 0, 0, 7, 0),
    //        ["P3"] = new(200, 0, 100, 180, 0, -45, 7, 0),
    //    };
    //    private bool _connected;

    //    public SimulatedRobot() : this(new RobotPointSettings(RobotSendMode.OffsetViaPoint, "P1", "P_VOFS", 'C')) { }
    //    public SimulatedRobot(RobotPointSettings settings, double maxOffsetMm = 50, double maxOffsetDeg = 30)
    //    {
    //        PointSettings = settings;
    //        _maxMm = maxOffsetMm;
    //        _maxDeg = maxOffsetDeg;
    //    }

    //    private readonly double _maxMm, _maxDeg;

    //    public static SimulatedRobot FromConfig(DeviceConfig d) => new(
    //        MitsubishiPointSettings.Read(d), d.GetDouble("MaxOffsetMm", 50), d.GetDouble("MaxOffsetDeg", 30));

    //    public RobotPointSettings PointSettings { get; }
    //    public bool HasControl { get; private set; }
    //    public bool ServoOn { get; private set; }
    //    private RobotPoint _position = new(300, 0, 200, 180, 0, 0, 7, 0);

    //    public Task<RobotReply> MoveRelativeAsync(double dx, double dy, double dz, double drz, CancellationToken ct = default)
    //    {
    //        if (!HasControl) return Task.FromResult(RobotReply.Fail("Chưa có quyền điều khiển."));
    //        if (!ServoOn) return Task.FromResult(RobotReply.Fail("Servo chưa bật."));
    //        string? invalid = ValidateOffset(dx, dy, drz);
    //        if (invalid != null) return Task.FromResult(RobotReply.Fail(invalid));
    //        _position = _position with { X = _position.X + dx, Y = _position.Y + dy, Z = _position.Z + dz }
    //            .WithAxis(PointSettings.RzAxis, _position.GetAxis(PointSettings.RzAxis) + drz);
    //        LastCommand = $"MOVE REL ({dx:0.###},{dy:0.###},{dz:0.###},{drz:0.###})";
    //        return Task.FromResult(RobotReply.Ok($"Đã di chuyển (giả lập): {LastCommand}"));
    //    }

    //    public Task<RobotReply> SetSpeedAsync(int percent, CancellationToken ct = default)
    //        => Task.FromResult(HasControl ? RobotReply.Ok($"Đã đặt tốc độ {percent}% (giả lập)") : RobotReply.Fail("Chưa có quyền điều khiển."));

    //    public Task<string> GetPositionTextAsync(CancellationToken ct = default) => Task.FromResult(_position.ToString());

    //    public Task<RobotReply> TakeControlAsync(bool stopAndReset, CancellationToken ct = default)
    //    {
    //        if (!_connected) return Task.FromResult(RobotReply.Fail("Robot giả lập chưa kết nối."));
    //        HasControl = true;
    //        return Task.FromResult(RobotReply.Ok("Đã chiếm quyền điều khiển (giả lập)"));
    //    }

    //    public Task<RobotReply> ReleaseControlAsync(CancellationToken ct = default)
    //    {
    //        HasControl = false; ServoOn = false;
    //        return Task.FromResult(RobotReply.Ok("Đã trả quyền điều khiển (giả lập)"));
    //    }

    //    public Task<RobotReply> StopAndResetAsync(CancellationToken ct = default)
    //        => Task.FromResult(HasControl ? RobotReply.Ok("Đã dừng + reset (giả lập)") : RobotReply.Fail("Chưa có quyền điều khiển."));

    //    public Task<RobotReply> SetServoAsync(bool on, CancellationToken ct = default)
    //    {
    //        if (!HasControl) return Task.FromResult(RobotReply.Fail("Chưa có quyền điều khiển."));
    //        ServoOn = on;
    //        return Task.FromResult(RobotReply.Ok(on ? "Đã bật servo (giả lập)" : "Đã tắt servo (giả lập)"));
    //    }
    //    public bool IsConnected => _connected;
    //    public string Description => "Robot giả lập";
    //    /// <summary>Lệnh cuối cùng "gửi" tới robot (để kiểm thử / hiển thị).</summary>
    //    public string LastCommand { get; private set; } = "";

    //    public Task ConnectAsync(CancellationToken ct = default) { _connected = true; return Task.CompletedTask; }
    //    public void Disconnect() { _connected = false; HasControl = false; ServoOn = false; }
    //    public void Dispose() => _connected = false;

    //    public string? ValidateOffset(double dx, double dy, double drz)
    //    {
    //        if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(drz)) return "Độ bù không phải số hợp lệ.";
    //        if (Math.Abs(dx) > _maxMm || Math.Abs(dy) > _maxMm) return $"Độ bù ({dx:0.###}, {dy:0.###}) mm vượt giới hạn ±{_maxMm} mm, không gửi.";
    //        if (Math.Abs(drz) > _maxDeg) return $"Góc bù {drz:0.###}° vượt giới hạn ±{_maxDeg}°, không gửi.";
    //        return null;
    //    }

    //    public Task<RobotReply> SendOffsetAsync(double dxMm, double dyMm, double drzDeg, int cycleId, CancellationToken ct = default)
    //    {
    //        if (!_connected) return Task.FromResult(RobotReply.Fail("Robot giả lập chưa kết nối."));
    //        string? invalid = ValidateOffset(dxMm, dyMm, drzDeg);
    //        if (invalid != null) return Task.FromResult(RobotReply.Fail(invalid));

    //        var point = new RobotPoint(dxMm, dyMm, 0, 0, 0, 0, 0, 0).WithAxis(PointSettings.RzAxis, drzDeg);
    //        lock (_gate) _points[PointSettings.OffsetPoint] = point;
    //        LastCommand = string.Create(CultureInfo.InvariantCulture, $"{PointSettings.OffsetPoint}={point.ToMelfa()} #{cycleId}");
    //        return Task.FromResult(RobotReply.Ok(LastCommand));
    //    }

    //    public Task<RobotReply> SendRejectAsync(int cycleId, CancellationToken ct = default)
    //    {
    //        if (!_connected) return Task.FromResult(RobotReply.Fail("Robot giả lập chưa kết nối."));
    //        LastCommand = $"REJECT #{cycleId}";
    //        return Task.FromResult(RobotReply.Ok(LastCommand));
    //    }

    //    public Task<IReadOnlyList<string>> ListPointsAsync(CancellationToken ct = default)
    //    {
    //        if (!_connected) throw new InvalidOperationException("Robot giả lập chưa kết nối.");
    //        lock (_gate)
    //            return Task.FromResult<IReadOnlyList<string>>(_points.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList());
    //    }

    //    public Task<RobotPointReply> ReadPointAsync(string name, CancellationToken ct = default)
    //    {
    //        if (!_connected) return Task.FromResult(RobotPointReply.Fail("Robot giả lập chưa kết nối."));
    //        lock (_gate)
    //            return Task.FromResult(_points.TryGetValue(name, out var p)
    //                ? RobotPointReply.Success(p)
    //                : RobotPointReply.Fail($"Điểm {name} chưa có trong robot giả lập."));
    //    }

    //    public Task<RobotReply> WritePointAsync(string name, RobotPoint point, CancellationToken ct = default)
    //    {
    //        if (!_connected) return Task.FromResult(RobotReply.Fail("Robot giả lập chưa kết nối."));
    //        if (!RobotPoint.IsValidName(name)) return Task.FromResult(RobotReply.Fail($"Tên điểm '{name}' không hợp lệ."));
    //        lock (_gate) _points[name] = point;
    //        LastCommand = $"{name}={point.ToMelfa()}";
    //        return Task.FromResult(RobotReply.Ok(LastCommand));
    //    }
    //}
}
