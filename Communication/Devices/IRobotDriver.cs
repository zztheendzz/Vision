using Communication.Devices;

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
        /// <summary>Đang giữ quyền điều khiển robot (OperationEnabled). Đọc/ghi điểm của chương trình cần có quyền này.</summary>
        bool HasControl { get; }

        /// <summary>Chiếm quyền điều khiển. stopAndReset = dừng chương trình đang chạy và reset (như chuỗi lệnh thử nghiệm), để thoát trạng thái RUN/Pause.</summary>
        Task<RobotReply> TakeControlAsync(bool stopAndReset, CancellationToken ct = default);

        /// <summary>Trả quyền điều khiển (tắt OperationEnabled).</summary>
        Task<RobotReply> ReleaseControlAsync(CancellationToken ct = default);

        /// <summary>Dừng chương trình đang chạy rồi reset chương trình. Cần quyền điều khiển.</summary>
        Task<RobotReply> StopAndResetAsync(CancellationToken ct = default);

        /// <summary>Bật/tắt servo. Cần quyền điều khiển. Bật servo có thể làm robot nhúc nhích.</summary>
        Task<RobotReply> SetServoAsync(bool on, CancellationToken ct = default);

        /// <summary>
        /// Di chuyển thẳng tương đối so với vị trí hiện tại (hệ tọa độ gốc robot): dX, dY, dZ (mm) và xoay Rz (độ).
        /// Cần quyền điều khiển + servo ON. Có kiểm tra giới hạn bước. Lệnh được gửi đi, không chờ robot đến nơi.
        /// </summary>
        Task<RobotReply> MoveRelativeAsync(double dxMm, double dyMm, double dzMm, double drzDeg, CancellationToken ct = default);

        /// <summary>Đặt tốc độ (override) 1–100 %.</summary>
        Task<RobotReply> SetSpeedAsync(int percent, CancellationToken ct = default);

        /// <summary>Vị trí hiện tại của robot dạng chữ (để hiện lên màn hình).</summary>
        Task<string> GetPositionTextAsync(CancellationToken ct = default);

        /// <summary>Cấu hình điểm: chế độ gửi, điểm cần bù, biến điểm trung gian, trục xoay Rz.</summary>
        RobotPointSettings PointSettings { get; }

        /// <summary>Kiểm tra độ bù theo giới hạn an toàn. Trả về lỗi, hoặc null nếu hợp lệ.</summary>
        string? ValidateOffset(double dxMm, double dyMm, double drzDeg);

        /// <summary>
        /// Chế độ "độ lệch": ghi (dX, dY, 0, Rz ở trục đã chọn) vào biến điểm trung gian (PointSettings.OffsetPoint).
        /// Có kiểm tra giới hạn an toàn.
        /// </summary>
        Task<RobotReply> SendOffsetAsync(double dxMm, double dyMm, double drzDeg, int cycleId, CancellationToken ct = default);

        /// <summary>Báo sản phẩm không đo được (NG) cho robot.</summary>
        Task<RobotReply> SendRejectAsync(int cycleId, CancellationToken ct = default);

        /// <summary>Đọc danh sách tên điểm đang có trong robot (P1, P2...).</summary>
        Task<IReadOnlyList<string>> ListPointsAsync(CancellationToken ct = default);

        /// <summary>Đọc giá trị một điểm.</summary>
        Task<RobotPointReply> ReadPointAsync(string name, CancellationToken ct = default);

        /// <summary>Ghi giá trị một điểm (chế độ "ghi đè điểm"). Gọi ValidateOffset trước nếu giá trị là điểm đã bù.</summary>
        Task<RobotReply> WritePointAsync(string name, RobotPoint point, CancellationToken ct = default);
    }
}
