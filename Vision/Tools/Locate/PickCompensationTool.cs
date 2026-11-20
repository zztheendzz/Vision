using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Vision.Calibration;
using Vision.Core;
using Vision.Geometry;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Locate
{
    /// <summary>Cách biểu diễn độ bù, phải khớp với cách robot áp dụng.</summary>
    public enum OffsetConvention
    {
        /// <summary>
        /// Phép biến đổi cứng quanh GỐC hệ tọa độ robot:  P' = R(Drz)·P + (Dx, Dy).
        /// Robot chỉ cần áp (Dx, Dy, Drz) lên điểm đã dạy, không cần biết vị trí mẫu.
        /// </summary>
        AboutRobotOrigin,

        /// <summary>
        /// (Dx, Dy) = tâm vật hiện tại trừ tâm vật lúc dạy, Drz = góc xoay thêm.
        /// Robot xoay điểm đã dạy QUANH TÂM MẪU:  P' = O_now + R(Drz)·(P - O_ref). Robot phải biết O_ref (xem Values RefX, RefY).
        /// </summary>
        AboutReferencePoint,

        /// <summary>
        /// Cộng thẳng vào điểm gắp đã dạy:  X' = X + Dx,  Y' = Y + Dy,  Rz' = Rz + Drz. Dùng khi PLC / robot chỉ CỘNG độ bù vào điểm dạy.
        /// (Dx, Dy) = điểm gắp mới trừ điểm gắp đã dạy, nên PHẢI nhập TaughtX / TaughtY / TaughtRz (điểm gắp đã dạy, hệ robot).
        /// </summary>
        Additive
    }

    /// <summary>
    /// Tính độ bù cho robot gắp ở điểm CỐ ĐỊNH đã dạy: so tư thế vật hiện tại với tư thế lúc dạy mẫu.
    ///
    /// Điều kiện: vật lúc dạy mẫu và điểm gắp của robot dạy cùng một lúc (vật đứng yên ở vị trí mẫu).
    /// Hiệu chuẩn camera - robot phải lập trong CÙNG hệ tọa độ mà robot áp độ bù (cùng user frame / base).
    ///
    /// Đầu ra:
    ///  - Offset: độ bù theo quy ước đã chọn (gửi cho robot nếu robot tự áp độ bù).
    ///  - Pick  : điểm gắp mới tuyệt đối = điểm đã dạy được bù (chỉ khi bật HasTaughtPick và nhập TaughtX/Y/Rz;
    ///            gửi cho robot nếu robot nhận tọa độ tuyệt đối).
    ///
    /// An toàn: vật lệch hơn MaxShiftMm hoặc xoay hơn MaxAngleDeg so với vị trí mẫu thì trả NoResult (NG), không tính độ bù,
    /// để robot không bị đưa tới vị trí bất thường (nhận nhầm vật, tìm sai...).
    /// Kiểm tra nhanh: đặt vật đúng vị trí mẫu thì Offset phải bằng 0 (quanh tâm mẫu) và Pick trùng điểm đã dạy.
    /// </summary>
    [VisionTool("PickCompensation", "Pick Compensation", Group = "Hiệu chuẩn",
                Description = "Tính độ bù cho robot gắp ở điểm cố định đã dạy, theo độ lệch của vật so với mẫu")]
    public class PickCompensationTool : StatefulToolBase
    {
        private readonly CalibrationCache _calibration = new();

        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Pose",      typeof(Pose2D)),
            new PortDef("Reference", typeof(Pose2D))
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Offset", typeof(RobotOffset)),
            new PortDef("Pick",   typeof(RobotPose))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("CalibrationPath", typeof(string), "calibration.json", Description: "File hiệu chuẩn camera - robot"),
            new ParamDef("Convention", typeof(OffsetConvention), OffsetConvention.AboutRobotOrigin, Description: "Cách robot áp độ bù. Additive = PLC/robot cộng thẳng vào điểm dạy (phải nhập TaughtX/Y/Rz)"),
            new ParamDef("MaxShiftMm",  typeof(double), 100.0, Min: 0, Description: "Vật lệch quá số mm này so với mẫu thì từ chối"),
            new ParamDef("MaxAngleDeg", typeof(double), 180.0, Min: 0, Max: 180, Description: "Vật xoay quá số độ này so với mẫu thì từ chối"),
            new ParamDef("HasTaughtPick", typeof(bool), false, Description: "Bật để tính thêm điểm gắp tuyệt đối từ điểm đã dạy"),
            new ParamDef("TaughtX",  typeof(double), 0.0, Description: "X điểm gắp đã dạy (mm), hệ robot"),
            new ParamDef("TaughtY",  typeof(double), 0.0, Description: "Y điểm gắp đã dạy (mm), hệ robot"),
            new ParamDef("TaughtRz", typeof(double), 0.0, Description: "Góc Rz điểm gắp đã dạy (độ), hệ robot")
        };

        protected override void OnInitialize(ToolContext context) { }
        protected override void OnRelease() => _calibration.Clear();

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            string template = input.Get<string>("CalibrationPath");
            if (string.IsNullOrWhiteSpace(template))
                return VisionToolResult.ConfigError("Chưa đặt CalibrationPath");
            if (!context.TryResolvePath(template, out string path, out string? pathError))
                return VisionToolResult.ConfigError(pathError!);

            var now = input.Get<Pose2D>("Pose");
            var reference = input.Get<Pose2D>("Reference");
            if (!now.IsFinite || !reference.IsFinite)
                return VisionToolResult.SystemError($"Tư thế không hợp lệ: Pose {now}, Reference {reference}");

            if (!_calibration.TryGet(path, out var affine, out string? error))
                return VisionToolResult.ConfigError(error!);

            // Tâm vật hiện tại (O_now) và lúc dạy (O_ref) trong hệ robot
            var (nowX, nowY) = affine.Apply(now.X, now.Y);
            var (refX, refY) = affine.Apply(reference.X, reference.Y);
            double rzNow = affine.TransformAngle(now.AngleDeg);
            double rzRef = affine.TransformAngle(reference.AngleDeg);

            double dTheta = Pose2D.NormalizeAngle(rzNow - rzRef);
            double shift = Math.Sqrt((nowX - refX) * (nowX - refX) + (nowY - refY) * (nowY - refY));

            if (shift > input.Get<double>("MaxShiftMm"))
                return VisionToolResult.NoResult($"Vật lệch {shift:F1} mm so với mẫu, vượt giới hạn {input.Get<double>("MaxShiftMm"):F1} mm");
            if (Math.Abs(dTheta) > input.Get<double>("MaxAngleDeg"))
                return VisionToolResult.NoResult($"Vật xoay {dTheta:F1}° so với mẫu, vượt giới hạn {input.Get<double>("MaxAngleDeg"):F1}°");

            double rad = dTheta * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            // Phép biến đổi cứng T đưa vật từ O_ref tới O_now: T(P) = R(dθ)·P + t, với t = O_now - R(dθ)·O_ref
            double tx = nowX - (cos * refX - sin * refY);
            double ty = nowY - (sin * refX + cos * refY);

            var convention = input.Get<OffsetConvention>("Convention");
            double taughtX = input.Get<double>("TaughtX");
            double taughtY = input.Get<double>("TaughtY");

            RobotOffset offset = convention switch
            {
                OffsetConvention.AboutRobotOrigin => new RobotOffset(tx, ty, dTheta),
                // Cộng thẳng: điểm gắp mới trừ điểm đã dạy (xoay điểm dạy quanh tâm mẫu rồi dịch theo vật)
                OffsetConvention.Additive => new RobotOffset(
                    cos * taughtX - sin * taughtY + tx - taughtX,
                    sin * taughtX + cos * taughtY + ty - taughtY,
                    dTheta),
                _ => new RobotOffset(nowX - refX, nowY - refY, dTheta)
            };

            var result = VisionToolResult.Ok($"Bù {offset}").Add("Offset", offset);
            result.Values["dX"] = Fmt(offset.Dx);
            result.Values["dY"] = Fmt(offset.Dy);
            result.Values["dRz"] = Fmt(offset.Drz);
            result.Values["RefX"] = Fmt(refX);       // robot cần khi dùng quy ước quanh điểm mẫu
            result.Values["RefY"] = Fmt(refY);

            if (input.Get<bool>("HasTaughtPick") || convention == OffsetConvention.Additive)
            {
                double px = input.Get<double>("TaughtX");
                double py = input.Get<double>("TaughtY");
                var pick = new RobotPose(
                    cos * px - sin * py + tx,
                    sin * px + cos * py + ty,
                    Pose2D.NormalizeAngle(input.Get<double>("TaughtRz") + dTheta));

                result.Add("Pick", pick);
                result.Values["PickX"] = Fmt(pick.X);
                result.Values["PickY"] = Fmt(pick.Y);
                result.Values["PickRz"] = Fmt(pick.Rz);
            }
            return result;
        }

        private static string Fmt(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
