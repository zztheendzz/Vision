using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Vision.Calibration;
using Vision.Core;
using Vision.Geometry;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Locate
{
    /// <summary>
    /// Đổi tư thế của vật từ ảnh (pixel) sang hệ tọa độ robot (mm), dùng file hiệu chuẩn.
    ///
    /// Robot : vị trí và góc để robot GẮP vật = tâm vật đã đổi sang robot, cộng độ lệch điểm gắp.
    /// Delta : (chỉ khi có input Reference) độ lệch của vật so với vị trí mẫu, trong hệ robot.
    ///         Dx, Dy = vị trí tâm vật hiện tại trừ vị trí lúc học mẫu; Drz = góc hiện tại trừ góc lúc học mẫu.
    ///
    /// Độ lệch điểm gắp (GraspOffsetX, GraspOffsetY theo mm, GraspOffsetRz theo độ) đo như sau: đặt vật đúng như ảnh mẫu
    /// (góc 0), đo vị trí gắp so với tâm vật trong hệ tọa độ robot. Khi vật xoay, độ lệch tự xoay theo vật.
    ///
    /// Hiệu chuẩn nạp lười (cùng lý do với FindShape): nạp lại khi đổi đường dẫn hoặc file được ghi lại.
    /// Cách tạo file hiệu chuẩn: CalibrationStore (từ CSV các cặp điểm pixel - robot).
    /// </summary>
    [VisionTool("PixelToRobot", "Pixel → Robot", Group = "Hiệu chuẩn",
                Description = "Đổi vị trí và góc từ ảnh (pixel) sang tọa độ robot (mm)")]
    public class PixelToRobotTool : StatefulToolBase
    {
        private Affine2D? _affine;
        private string? _loadedPath;
        private DateTime _loadedStamp;

        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Pose",      typeof(Pose2D)),
            new PortDef("Reference", typeof(Pose2D), Required: false)
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Robot", typeof(RobotPose)),
            new PortDef("Delta", typeof(RobotDelta))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("CalibrationPath", typeof(string), "calibration.json", Description: "File hiệu chuẩn camera - robot"),
            new ParamDef("GraspOffsetX",  typeof(double), 0.0, Description: "Lệch điểm gắp theo X robot (mm), đo khi vật ở góc 0"),
            new ParamDef("GraspOffsetY",  typeof(double), 0.0, Description: "Lệch điểm gắp theo Y robot (mm), đo khi vật ở góc 0"),
            new ParamDef("GraspOffsetRz", typeof(double), 0.0, Description: "Lệch góc gắp so với góc vật (độ)")
        };

        protected override void OnInitialize(ToolContext context) { }

        protected override void OnRelease()
        {
            _affine = null;
            _loadedPath = null;
        }

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            string path = input.Get<string>("CalibrationPath");
            if (string.IsNullOrWhiteSpace(path))
                return VisionToolResult.ConfigError("Chưa đặt CalibrationPath");

            var pose = input.Get<Pose2D>("Pose");
            if (!pose.IsFinite)
                return VisionToolResult.SystemError($"Tư thế đầu vào không hợp lệ: {pose}");

            if (!EnsureLoaded(path, out string? error))
                return VisionToolResult.ConfigError(error!);
            var affine = _affine!;

            // Tâm vật và góc vật trong hệ robot
            var (x, y) = affine.Apply(pose.X, pose.Y);
            double rz = affine.TransformAngle(pose.AngleDeg);

            // Độ lệch điểm gắp: đo ở góc 0 nên quay theo phần vật đã xoay so với góc 0
            double turn = (Pose2D.NormalizeAngle(rz - affine.TransformAngle(0.0))) * Math.PI / 180.0;
            double ox = input.Get<double>("GraspOffsetX");
            double oy = input.Get<double>("GraspOffsetY");
            double gx = x + ox * Math.Cos(turn) - oy * Math.Sin(turn);
            double gy = y + ox * Math.Sin(turn) + oy * Math.Cos(turn);
            double grz = Pose2D.NormalizeAngle(rz + input.Get<double>("GraspOffsetRz"));

            var robot = new RobotPose(gx, gy, grz);
            var result = VisionToolResult.Ok($"Robot {robot}").Add("Robot", robot);
            result.Values["X"] = Fmt(gx);
            result.Values["Y"] = Fmt(gy);
            result.Values["Rz"] = Fmt(grz);

            if (input.TryGet<Pose2D>("Reference", out var reference) && reference.IsFinite)
            {
                var (rx, ry) = affine.Apply(reference.X, reference.Y);
                double rrz = affine.TransformAngle(reference.AngleDeg);
                var delta = new RobotDelta(x - rx, y - ry, Pose2D.NormalizeAngle(rz - rrz));

                result.Add("Delta", delta);
                result.Values["dX"] = Fmt(delta.Dx);
                result.Values["dY"] = Fmt(delta.Dy);
                result.Values["dRz"] = Fmt(delta.Drz);
            }
            return result;
        }

        private bool EnsureLoaded(string path, out string? error)
        {
            error = null;
            if (!File.Exists(path))
            {
                error = $"Chưa hiệu chuẩn: không thấy file '{path}'";
                return false;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_affine != null && _loadedPath == path && _loadedStamp == stamp) return true;

            try
            {
                _affine = CalibrationStore.Load(path).ToAffine();
                _loadedPath = path;
                _loadedStamp = stamp;
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                                          or System.Text.Json.JsonException)
            {
                _affine = null;
                error = $"File hiệu chuẩn '{path}' không hợp lệ: {ex.Message}";
                return false;
            }
        }

        private static string Fmt(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
