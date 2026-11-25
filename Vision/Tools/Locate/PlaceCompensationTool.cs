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
    /// <summary>Cách robot áp độ bù lên điểm đặt đã dạy. Chọn đúng cách robot của bạn dùng.</summary>
    public enum PlaceOffsetConvention
    {
        /// <summary>
        /// Cộng thẳng vào điểm đã dạy:  X' = X + Dx,  Y' = Y + Dy,  Rz' = Rz + Drz  (trong hệ tọa độ robot / user frame).
        /// Cách phổ biến nhất ("position register" cộng vào điểm dạy). Chỉ cần nhập PlaceRz.
        /// </summary>
        Additive,

        /// <summary>
        /// Dịch trong hệ tọa độ CỦA TAY GẮP (tool frame): P' = P · (Dx, Dy, Drz). Không cần nhập PlaceX/Y/Rz.
        /// Dùng khi robot hỗ trợ lệnh dịch theo tool.
        /// </summary>
        ToolFrame,

        /// <summary>Phép biến đổi cứng quanh gốc hệ robot:  P' = R(Drz)·P + (Dx, Dy). Cần nhập PlaceX/Y/Rz.</summary>
        AboutRobotOrigin
    }

    /// <summary>
    /// Hiệu chỉnh vị trí ĐẶT khi vật đang nằm trong tay gắp.
    ///
    /// Quy trình: robot gắp vật lên, đi tới TRẠM CHỤP (luôn cùng một tư thế TCP), camera đo tư thế vật trong tay,
    /// tool so với tư thế "vật nằm đúng trong tay" lúc dạy rồi tính tư thế TCP cần để vật rơi đúng khuôn.
    ///
    /// Toán (tất cả là phép biến đổi cứng 2D trong hệ robot):
    ///   T       = O_now · O_ref⁻¹          vật đang lệch bao nhiêu so với lý tưởng (camera đo)
    ///   D       = F_ph⁻¹ · T · F_ph        độ lệch đó nhìn trong hệ của tay gắp (F_ph = TCP lúc chụp)
    ///   F_place = F_taught · D⁻¹           TCP đặt mới = TCP đặt đã dạy, bù ngược độ lệch trong tay
    /// Đã kiểm bằng mô phỏng: vật rơi đúng khuôn với sai số bằng 0 cho mọi TCP lúc chụp và lúc đặt.
    ///
    /// CÁCH DẠY (thứ tự quan trọng):
    ///  1. Đặt vật vào tay gắp ĐÚNG như khi đặt chuẩn vào khuôn.
    ///  2. Robot đi tới trạm chụp; ghi TCP lúc chụp vào PhotoX/Y/Rz; bấm Học mẫu (FindShape học vật trong tay).
    ///  3. Robot đi tới khuôn, nhích tới khi vật khớp; ghi TCP vào PlaceX/Y/Rz.
    /// Trạm chụp phải cố định: mọi lần chạy robot dừng đúng TCP đã ghi ở PhotoX/Y/Rz.
    /// Hiệu chuẩn camera - robot lập trong cùng hệ tọa độ robot, tại cao độ mặt vật nhìn từ camera.
    ///
    /// Đầu ra:
    ///  - Correction: ĐỘ BÙ (Dx mm, Dy mm, Drz độ) gửi cho robot, theo quy ước Convention (mặc định cộng thẳng vào điểm đã dạy).
    ///  - Place     : TCP đặt mới tuyệt đối, chỉ để đối chiếu/kiểm tra (cần nhập PlaceX/Y/Rz mới đúng).
    /// An toàn: vật lệch trong tay quá MaxShiftMm / MaxAngleDeg thì trả NoResult (NG), không tính vị trí đặt
    /// (vật bị gắp hỏng, rơi lệch nhiều, hoặc nhận nhầm).
    /// </summary>
    [VisionTool("PlaceCompensation", "Place Compensation", Group = "Hiệu chuẩn",
                Description = "Hiệu chỉnh vị trí đặt theo độ lệch của vật trong tay gắp")]
    public class PlaceCompensationTool : StatefulToolBase
    {
        private readonly CalibrationCache _calibration = new();

        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Pose",      typeof(Pose2D)),
            new PortDef("Reference", typeof(Pose2D))
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Place",      typeof(RobotPose)),
            new PortDef("Correction", typeof(RobotOffset))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("CalibrationPath", typeof(string), "calibration.json", Description: "File hiệu chuẩn camera - robot"),
            new ParamDef("PhotoX",  typeof(double), 0.0, Description: "X của TCP tại trạm chụp (mm)"),
            new ParamDef("PhotoY",  typeof(double), 0.0, Description: "Y của TCP tại trạm chụp (mm)"),
            new ParamDef("PhotoRz", typeof(double), 0.0, Description: "Rz của TCP tại trạm chụp (độ)"),
            new ParamDef("Convention", typeof(PlaceOffsetConvention), PlaceOffsetConvention.Additive, Description: "Cách robot áp độ bù"),
            new ParamDef("PlaceRz", typeof(double), 0.0, Description: "Rz của TCP lúc đặt vào khuôn đã dạy (độ). Cần cho Additive"),
            new ParamDef("PlaceX",  typeof(double), 0.0, Description: "X của TCP lúc đặt đã dạy (mm). Chỉ cần cho AboutRobotOrigin và đầu ra Place"),
            new ParamDef("PlaceY",  typeof(double), 0.0, Description: "Y của TCP lúc đặt đã dạy (mm). Chỉ cần cho AboutRobotOrigin và đầu ra Place"),
            new ParamDef("InvertRz", typeof(bool), false, Description: "Đảo dấu góc Rz gửi cho robot. Bật khi calib báo \"Dấu góc có vẻ NGƯỢC\" (robot xoay +, ảnh đo -): trục xoay của robot ngược chiều với hệ tọa độ X-Y đã calib"),
            new ParamDef("MaxShiftMm",  typeof(double), 20.0, Min: 0, Description: "Vật lệch trong tay quá số mm này thì từ chối"),
            new ParamDef("MaxAngleDeg", typeof(double), 15.0, Min: 0, Max: 180, Description: "Vật xoay trong tay quá số độ này thì từ chối")
        };

        protected override void OnInitialize(ToolContext context) { }
        protected override void OnRelease() => _calibration.Clear();

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            string template = input.Get<string>("CalibrationPath");
            if (string.IsNullOrWhiteSpace(template))
                return VisionToolResult.ConfigError("Chưa đặt CalibrationPath");
            if (!context.TryResolveCalibrationPath(template, out string path, out string? pathError))
                return VisionToolResult.ConfigError(pathError!);

            var now = input.Get<Pose2D>("Pose");
            var reference = input.Get<Pose2D>("Reference");
            if (!now.IsFinite || !reference.IsFinite)
                return VisionToolResult.SystemError($"Tư thế không hợp lệ: Pose {now}, Reference {reference}");

            if (!_calibration.TryGet(path, out var affine, out string? error))
                return VisionToolResult.ConfigError(error!);

            // Tư thế vật trong hệ robot: hiện tại và lý tưởng (lúc dạy)
            var (nowX, nowY) = affine.Apply(now.X, now.Y);
            var (refX, refY) = affine.Apply(reference.X, reference.Y);
            var oNow = new Rigid2D(nowX, nowY, affine.TransformAngle(now.AngleDeg));
            var oRef = new Rigid2D(refX, refY, affine.TransformAngle(reference.AngleDeg));

            // Giới hạn an toàn trên độ lệch của vật trong tay
            double shift = Math.Sqrt((nowX - refX) * (nowX - refX) + (nowY - refY) * (nowY - refY));
            double angle = Pose2D.NormalizeAngle(oNow.ThetaDeg - oRef.ThetaDeg);
            double maxShift = input.Get<double>("MaxShiftMm");
            double maxAngle = input.Get<double>("MaxAngleDeg");
            if (shift > maxShift)
                return VisionToolResult.NoResult($"Vật lệch trong tay {shift:F2} mm, vượt giới hạn {maxShift:F2} mm");
            if (Math.Abs(angle) > maxAngle)
                return VisionToolResult.NoResult($"Vật xoay trong tay {angle:F2}°, vượt giới hạn {maxAngle:F2}°");

            // Trục xoay robot ngược chiều: đổi góc robot đọc sang hệ hình học (đảo dấu) để tính, rồi đảo lại khi xuất
            double s = input.Get<bool>("InvertRz") ? -1.0 : 1.0;
            var photo = new Rigid2D(input.Get<double>("PhotoX"), input.Get<double>("PhotoY"), s * input.Get<double>("PhotoRz"));
            var taught = new Rigid2D(input.Get<double>("PlaceX"), input.Get<double>("PlaceY"), s * input.Get<double>("PlaceRz"));

            var t = oNow.Compose(oRef.Inverse());                              // T = O_now · O_ref⁻¹
            var d = photo.Inverse().Compose(t).Compose(photo);                 // D = F_ph⁻¹ · T · F_ph
            var place = taught.Compose(d.Inverse());                           // F_place = F_taught · D⁻¹

            var placePose = place.ToPose();
            if (s < 0) placePose = new RobotPose(placePose.X, placePose.Y, Pose2D.NormalizeAngle(-placePose.Rz));
            RobotOffset offset;
            switch (input.Get<PlaceOffsetConvention>("Convention"))
            {
                case PlaceOffsetConvention.ToolFrame:
                    var inHand = d.Inverse();                                   // D⁻¹: dịch trong hệ của tay gắp
                    offset = new RobotOffset(inHand.X, inHand.Y, Pose2D.NormalizeAngle(inHand.ThetaDeg));
                    break;

                case PlaceOffsetConvention.AboutRobotOrigin:
                    var about = place.Compose(taught.Inverse());                // C = F_place · F_taught⁻¹
                    offset = new RobotOffset(about.X, about.Y, Pose2D.NormalizeAngle(about.ThetaDeg));
                    break;

                default:                                                        // Additive: hiệu giữa điểm đặt mới và điểm đã dạy
                    offset = new RobotOffset(
                        place.X - taught.X, place.Y - taught.Y,
                        Pose2D.NormalizeAngle(place.ThetaDeg - taught.ThetaDeg));
                    break;
            }

            if (s < 0) offset = new RobotOffset(offset.Dx, offset.Dy, Pose2D.NormalizeAngle(-offset.Drz));

            var result = VisionToolResult.Ok($"Lệch trong tay {shift:F2} mm, {angle:F2}° → bù {offset}")
                .Add("Place", placePose)
                .Add("Correction", offset);
            result.Values["dX"] = Fmt(offset.Dx);
            result.Values["dY"] = Fmt(offset.Dy);
            result.Values["dRz"] = Fmt(offset.Drz);
            result.Values["PlaceX"] = Fmt(placePose.X);
            result.Values["PlaceY"] = Fmt(placePose.Y);
            result.Values["PlaceRz"] = Fmt(placePose.Rz);
            result.Values["InHandShift"] = Fmt(shift);
            result.Values["InHandAngle"] = Fmt(angle);
            return result;
        }

        private static string Fmt(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
