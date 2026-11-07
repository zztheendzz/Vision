using Communication.Devices;
using Communication.Drivers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vision.Calibration;
using Vision.Geometry;

namespace UI.Services
{
    /// <summary>Thông số một lần "đi tới vị trí chụp → đo vật → ghi tọa độ cho robot".</summary>
    public sealed record FixedShotOptions(
        string ShotPoint,            // điểm chụp (tên điểm trong robot, vd "P_SHOT"). Trống = robot đang ở sẵn vị trí chụp, không di chuyển
        string TargetPoint,          // điểm sẽ được ghi đè bằng tọa độ của vật (vd "P_PICK")
        string CalibrationPath,      // file calibration.json (cùng hệ tọa độ với điểm của robot)
        double RzOffsetDeg,          // cộng thêm vào góc vật khi đổi sang trục xoay của robot (hướng gắp so với hướng vật)
        bool InvertRz,               // đảo chiều góc nếu trục xoay của robot ngược chiều
        char RzAxis,                 // trục xoay của robot ứng với Rz (thường 'C')
        int SettleMs,                // chờ sau khi tới điểm chụp
        int SpeedPercent,            // tốc độ khi đi tới điểm chụp
        bool Send);                  // false = chỉ đo và báo kết quả, KHÔNG ghi gì vào robot

    public sealed record FixedShotResult(bool Success, string Message, RobotPose? Pose, RobotPoint? Written);

    /// <summary>
    /// Trường hợp 2: robot tới VỊ TRÍ CHỤP CỐ ĐỊNH, chụp ảnh, tìm vật trong ảnh, đổi sang tọa độ robot rồi GHI vào một điểm
    /// của robot (điểm đích) để chương trình robot đi gắp tại đó.
    ///
    ///  - X, Y, góc lấy từ ảnh qua calibration.json (Affine2D). Z, A, B và cờ giữ nguyên từ điểm đích hiện có
    ///    (Z cố định, đã dạy sẵn). Chỉ trục xoay Rz bị thay.
    ///  - An toàn: độ lệch của điểm mới so với điểm đích cũ phải qua ValidateOffset của robot (giới hạn mm và độ
    ///    trong cấu hình robot). Vượt giới hạn, không thấy vật, calibration lỗi, không đọc được điểm đích: KHÔNG ghi gì.
    ///  - Ghi điểm có đọc lại để kiểm chứng (do driver robot thực hiện).
    ///
    /// Không biết camera: hàm measure do người gọi truyền vào (chụp và trả Pose2D của vật, hoặc null).
    /// </summary>
    public static class FixedShotPicker
    {
        private static readonly TimeSpan ArriveTimeout = TimeSpan.FromSeconds(30);
        private const double ArriveToleranceMm = 0.05;
        private const double ArriveToleranceDeg = 0.05;

        public static async Task<FixedShotResult> RunAsync(
            IRobotDriver robot,
            Func<CancellationToken, Task<Pose2D?>> measure,
            FixedShotOptions o,
            IProgress<string> progress,
            CancellationToken ct)
        {
            if (!robot.IsConnected) return Fail("Robot chưa kết nối.");
            if (!RobotPoint.IsValidName(o.TargetPoint))
                return Fail("Chưa nhập đúng tên 'Điểm ghi tọa độ' (chữ, số, gạch dưới; bắt đầu bằng chữ).");
            if (string.IsNullOrWhiteSpace(o.CalibrationPath) || !File.Exists(o.CalibrationPath))
                return Fail("Không thấy file hiệu chuẩn: " + o.CalibrationPath);
            if (o.SpeedPercent is < 1 or > 100) return Fail("Tốc độ phải từ 1 đến 100 %.");
            if (o.SettleMs is < 0 or > 10_000) return Fail("Thời gian chờ ổn định phải từ 0 đến 10000 ms.");

            Affine2D affine;
            try { affine = CalibrationStore.Load(o.CalibrationPath).ToAffine(); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                return Fail("File hiệu chuẩn lỗi: " + ex.Message);
            }

            // Đọc và ghi điểm, đi tới điểm chụp đều cần quyền điều khiển
            if (!robot.HasControl)
                return Fail("Chưa chiếm quyền điều khiển robot (tab Điều khiển của robot → Chiếm quyền).");

            // ---------- 1. Đi tới vị trí chụp ----------
            if (!string.IsNullOrWhiteSpace(o.ShotPoint))
            {
                if (!RobotPoint.IsValidName(o.ShotPoint)) return Fail("Tên 'Điểm chụp' không hợp lệ.");

                var read = await robot.ReadPointAsync(o.ShotPoint, ct);
                if (!read.Ok) return Fail($"Không đọc được điểm chụp {o.ShotPoint}: {read.Error}");

                var speed = await robot.SetSpeedAsync(o.SpeedPercent, ct);
                if (!speed.Accepted) return Fail($"Không đặt được tốc độ: {speed.Error}");

                progress.Report($"Đi tới điểm chụp {o.ShotPoint} {read.Point.ToMelfa()}...");
                // Điểm chụp là điểm đã dạy: người dùng đã xác nhận trước khi chạy nên không chặn theo khoảng cách
                var move = await robot.MoveToAsync(read.Point, linear: false, enforceDistanceLimit: false, ct);
                if (!move.Accepted) return Fail($"Robot không nhận lệnh đi tới điểm chụp: {move.Error}");

                string? arrive = await WaitArrivedAsync(robot, read.Point, o.RzAxis, ct);
                if (arrive != null) return Fail(arrive);
                if (o.SettleMs > 0) await Task.Delay(o.SettleMs, ct);
            }
            else
            {
                progress.Report("Không có điểm chụp: coi như robot đang ở sẵn vị trí chụp.");
            }

            // ---------- 2. Chụp và đo ----------
            progress.Report("Chụp ảnh và tìm vật...");
            var pose = await measure(ct);
            if (pose is not { } p || !p.IsFinite)
                return Fail("Không thấy vật trong ảnh: KHÔNG ghi gì cho robot.");

            // ---------- 3. Đổi sang tọa độ robot ----------
            var (x, y) = affine.Apply(p.Column, p.Row);
            double rz = affine.TransformAngle(p.AngleDeg);
            if (o.InvertRz) rz = -rz;
            rz = Pose2D.NormalizeAngle(rz + o.RzOffsetDeg);
            var robotPose = new RobotPose(x, y, rz);
            progress.Report($"Vật trong ảnh {p} → robot {robotPose}");

            // ---------- 4. Điểm đích hiện có: lấy Z, A, B, cờ và để kiểm tra giới hạn ----------
            var target = await robot.ReadPointAsync(o.TargetPoint, ct);
            if (!target.Ok)
                return Fail($"Không đọc được điểm đích {o.TargetPoint}: {target.Error}. KHÔNG ghi gì.", robotPose);

            var old = target.Point;
            double dx = x - old.X;
            double dy = y - old.Y;
            double drz = Pose2D.NormalizeAngle(rz - old.GetAxis(o.RzAxis));

            string? limit = robot.ValidateOffset(dx, dy, drz);
            if (limit != null)
                return Fail($"{limit} (so với điểm {o.TargetPoint} hiện tại: dX {dx:F2}, dY {dy:F2}, dRz {drz:F2}°). KHÔNG ghi gì.", robotPose);

            var moved = (old with { X = x, Y = y }).WithAxis(o.RzAxis, rz);

            if (!o.Send)
                return new FixedShotResult(true,
                    $"Chỉ đo, chưa ghi: {o.TargetPoint} sẽ đổi {old.ToMelfa()} → {moved.ToMelfa()}.", robotPose, null);

            // ---------- 5. Ghi (driver tự đọc lại để kiểm chứng) ----------
            var write = await robot.WritePointAsync(o.TargetPoint, moved, ct);
            return write.Accepted
                ? new FixedShotResult(true, $"Đã ghi {o.TargetPoint}: {old.ToMelfa()} → {moved.ToMelfa()}.", robotPose, moved)
                : Fail($"Ghi điểm {o.TargetPoint} thất bại: {write.Error}", robotPose);
        }

        private static async Task<string?> WaitArrivedAsync(IRobotDriver robot, RobotPoint target, char rzAxis, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + ArriveTimeout;
            int stable = 0;
            while (stable < 2)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline) return "Quá thời gian chờ robot tới điểm chụp (kiểm tra servo, lỗi, tốc độ).";

                var now = await robot.GetCurrentPointAsync(ct);
                if (!now.Ok) return $"Không đọc được vị trí robot: {now.Error}";

                var c = now.Point;
                bool near = Math.Abs(c.X - target.X) <= ArriveToleranceMm
                         && Math.Abs(c.Y - target.Y) <= ArriveToleranceMm
                         && Math.Abs(c.Z - target.Z) <= ArriveToleranceMm
                         && Math.Abs(Pose2D.NormalizeAngle(c.GetAxis(rzAxis) - target.GetAxis(rzAxis))) <= ArriveToleranceDeg;
                stable = near ? stable + 1 : 0;
                if (stable < 2) await Task.Delay(150, ct);
            }
            return null;
        }

        private static FixedShotResult Fail(string message, RobotPose? pose = null) => new(false, message, pose, null);
    }
}
