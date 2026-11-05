namespace Communication.Drivers
{
    /// <summary>Kết quả gửi lệnh: Accepted = lệnh đã gửi không lỗi (chưa chắc robot đã chạy xong).</summary>
    public sealed record RobotReply(bool Accepted, string Raw, string? Error = null)
    {
        public static RobotReply Ok(string raw = "") => new(true, raw);
        public static RobotReply Fail(string error) => new(false, "", error);
    }

    public interface IRobotDriver : IDeviceLink
    {
        /// <summary>Gửi độ bù (mm, mm, độ) về robot. Có kiểm tra giới hạn an toàn.</summary>
        Task<RobotReply> SendOffsetAsync(double dxMm, double dyMm, double drzDeg, int cycleId, CancellationToken ct = default);

        /// <summary>Báo sản phẩm không đo được (NG) cho robot.</summary>
        Task<RobotReply> SendRejectAsync(int cycleId, CancellationToken ct = default);
    }
}
