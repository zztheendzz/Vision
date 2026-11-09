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
    /// <summary>Thông số một lần "đi tới vị trí chụp → đo vật → tính độ bù".</summary>
    public sealed record FixedShotOptions(
        string ShotPoint,            // điểm chụp (tên điểm trong robot). Trống = robot / máy đang ở sẵn vị trí chụp, KHÔNG cần robot kết nối
        string CalibrationPath,      // file calibration.json (cùng hệ tọa độ với điểm của robot)
        bool InvertRz,               // đảo chiều góc nếu trục xoay của robot ngược chiều
        char RzAxis,                 // trục xoay của robot ứng với Rz (thường 'C'); chỉ dùng khi phải đi tới điểm chụp
        int SettleMs,                // chờ sau khi tới điểm chụp
        int SpeedPercent,            // tốc độ khi đi tới điểm chụp
        RobotPose? Reference);       // tư thế vật lúc dạy (hệ robot). null = chưa dạy: chỉ đo được, chưa tính được độ bù

    public sealed record FixedShotResult(bool Success, string Message, RobotPose? Pose, RobotOffset? Offset);

    /// <summary>Hàm gửi độ bù đi (ghi vào thanh ghi PLC). Trả về (thành công, thông báo).</summary>
    public delegate Task<(bool Ok, string Message)> OffsetSender(RobotOffset offset, CancellationToken ct);

    /// <summary>
    /// Trường hợp 2: máy tới VỊ TRÍ CHỤP CỐ ĐỊNH, chụp ảnh, tìm vật, đổi sang tọa độ robot qua calibration.json, rồi tính
    /// ĐỘ BÙ = tư thế vật bây giờ trừ tư thế vật lúc dạy (Reference), và gửi độ bù đó đi (hàm send: ghi thanh ghi PLC).
    ///
    ///  - Không ghi gì vào robot. Chỉ gửi (dX, dY, dRz); PLC cộng vào điểm đã dạy.
    ///  - Phép cộng thẳng chính xác khi điểm gắp / đặt đã dạy nằm ở tâm vật. Điểm lệch khỏi tâm vật thì vật xoay càng
    ///    nhiều sai càng lớn (sai ≈ khoảng cách điểm tới tâm × sin góc xoay).
    ///  - An toàn: không thấy vật, calibration lỗi, chưa dạy vị trí chuẩn, độ bù vượt giới hạn (kiểm ở bước ghi PLC),
    ///    ghi PLC lỗi: KHÔNG có độ bù nào được báo là đã gửi.
    ///  - Robot chỉ cần khi có điểm chụp để robot tự đi tới (robot nối trực tiếp qua driver).
    ///
    /// Không biết camera: hàm measure do người gọi truyền vào (chụp và trả Pose2D của vật, hoặc null).
    /// </summary>
    public static class FixedShotPicker
    {
        private static readonly TimeSpan ArriveTimeout = TimeSpan.FromSeconds(30);
        private const double ArriveToleranceMm = 0.05;
        private const double ArriveToleranceDeg = 0.05;

        /// <summary>Chụp, đo, tính độ bù; gửi đi nếu send khác null (null = chỉ đo và báo kết quả).</summary>
        public static async Task<FixedShotResult> RunAsync(
            IRobotDriver? robot,
            Func<CancellationToken, Task<Pose2D?>> measure,
            FixedShotOptions o,
            OffsetSender? send,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var (pose, error) = await ShootAsync(robot, measure, o, progress, ct);
            if (pose is not { } p) return Fail(error!);

            if (o.Reference is not { } reference)
            {
                return send == null
                    ? new FixedShotResult(true, $"Chỉ đo: vật ở robot {p}. Chưa dạy vị trí chuẩn nên chưa tính được độ bù.", p, null)
                    : Fail("Chưa dạy vị trí chuẩn (bấm 'Lấy vị trí vật hiện tại làm chuẩn' khi vật đang đúng vị trí dạy). KHÔNG gửi gì.", p);
            }

            var offset = new RobotOffset(
                p.X - reference.X,
                p.Y - reference.Y,
                Pose2D.NormalizeAngle(p.Rz - reference.Rz));
            progress.Report($"Vị trí chuẩn {reference} → độ bù {offset}");

            if (send == null)
                return new FixedShotResult(true, $"Chỉ đo, chưa gửi: độ bù sẽ là {offset}.", p, offset);

            var sent = await send(offset, ct);
            return sent.Ok
                ? new FixedShotResult(true, $"Đã gửi độ bù {offset}. {sent.Message}", p, offset)
                : new FixedShotResult(false, $"Gửi độ bù {offset} thất bại: {sent.Message}", p, offset);
        }

        /// <summary>Chụp và đo một lần, trả tư thế vật trong hệ robot (dùng để dạy vị trí chuẩn).</summary>
        public static async Task<FixedShotResult> MeasureAsync(
            IRobotDriver? robot,
            Func<CancellationToken, Task<Pose2D?>> measure,
            FixedShotOptions o,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var (pose, error) = await ShootAsync(robot, measure, o, progress, ct);
            return pose is { } p
                ? new FixedShotResult(true, $"Vật ở robot {p}.", p, null)
                : Fail(error!);
        }

        private static async Task<(RobotPose? Pose, string? Error)> ShootAsync(
            IRobotDriver? robot, Func<CancellationToken, Task<Pose2D?>> measure,
            FixedShotOptions o, IProgress<string> progress, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(o.CalibrationPath) || !File.Exists(o.CalibrationPath))
                return (null, "Không thấy file hiệu chuẩn: " + o.CalibrationPath);
            if (o.SpeedPercent is < 1 or > 100) return (null, "Tốc độ phải từ 1 đến 100 %.");
            if (o.SettleMs is < 0 or > 10_000) return (null, "Thời gian chờ ổn định phải từ 0 đến 10000 ms.");

            Affine2D affine;
            try { affine = CalibrationStore.Load(o.CalibrationPath).ToAffine(); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                return (null, "File hiệu chuẩn lỗi: " + ex.Message);
            }

            // ---------- 1. Đi tới vị trí chụp (nếu có điểm chụp) ----------
            if (!string.IsNullOrWhiteSpace(o.ShotPoint))
            {
                if (!RobotPoint.IsValidName(o.ShotPoint)) return (null, "Tên 'Điểm chụp' không hợp lệ.");
                if (robot == null || !robot.IsConnected)
                    return (null, "Có điểm chụp nên robot phải kết nối trực tiếp. Để trống 'Điểm chụp' nếu máy đã ở sẵn vị trí chụp.");
                if (!robot.HasControl)
                    return (null, "Chưa chiếm quyền điều khiển robot (tab Điều khiển của robot → Chiếm quyền).");

                var read = await robot.ReadPointAsync(o.ShotPoint, ct);
                if (!read.Ok) return (null, $"Không đọc được điểm chụp {o.ShotPoint}: {read.Error}");

                var speed = await robot.SetSpeedAsync(o.SpeedPercent, ct);
                if (!speed.Accepted) return (null, $"Không đặt được tốc độ: {speed.Error}");

                progress.Report($"Đi tới điểm chụp {o.ShotPoint} {read.Point.ToMelfa()}...");
                // Điểm chụp là điểm đã dạy: người dùng đã xác nhận trước khi chạy nên không chặn theo khoảng cách
                var move = await robot.MoveToAsync(read.Point, linear: false, enforceDistanceLimit: false, ct);
                if (!move.Accepted) return (null, $"Robot không nhận lệnh đi tới điểm chụp: {move.Error}");

                string? arrive = await WaitArrivedAsync(robot, read.Point, o.RzAxis, ct);
                if (arrive != null) return (null, arrive);
                if (o.SettleMs > 0) await Task.Delay(o.SettleMs, ct);
            }
            else
            {
                progress.Report("Không có điểm chụp: coi như máy đang ở sẵn vị trí chụp.");
                if (o.SettleMs > 0) await Task.Delay(o.SettleMs, ct);
            }

            // ---------- 2. Chụp và đo ----------
            progress.Report("Chụp ảnh và tìm vật...");
            var pose = await measure(ct);
            if (pose is not { } p || !p.IsFinite)
                return (null, "Không thấy vật trong ảnh: KHÔNG gửi gì.");

            // ---------- 3. Đổi sang tọa độ robot ----------
            var (x, y) = affine.Apply(p.Column, p.Row);
            double rz = affine.TransformAngle(p.AngleDeg);
            if (o.InvertRz) rz = -rz;
            var robotPose = new RobotPose(x, y, Pose2D.NormalizeAngle(rz));
            progress.Report($"Vật trong ảnh {p} → robot {robotPose}");
            return (robotPose, null);
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
