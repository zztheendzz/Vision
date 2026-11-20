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
    /// Robot = tâm vật trong hệ robot, cộng độ lệch điểm gắp (GraspOffsetX/Y theo mm, GraspOffsetRz theo độ).
    /// Độ lệch điểm gắp đo khi vật ở góc 0 (đúng như ảnh mẫu), trong hệ tọa độ robot; khi vật xoay, độ lệch tự xoay theo vật.
    ///
    /// Dùng khi robot nhận TỌA ĐỘ GẮP TUYỆT ĐỐI. Nếu robot nhận độ bù so với điểm đã dạy, dùng PickCompensation.
    /// </summary>
    [VisionTool("PixelToRobot", "Pixel → Robot", Group = "Hiệu chuẩn",
                Description = "Đổi vị trí và góc từ ảnh (pixel) sang tọa độ robot (mm)")]
    public class PixelToRobotTool : StatefulToolBase
    {
        private readonly CalibrationCache _calibration = new();

        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Pose", typeof(Pose2D))
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Robot", typeof(RobotPose))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("CalibrationPath", typeof(string), "calibration.json", Description: "File hiệu chuẩn camera - robot"),
            new ParamDef("GraspOffsetX",  typeof(double), 0.0, Description: "Lệch điểm gắp theo X robot (mm), đo khi vật ở góc 0"),
            new ParamDef("GraspOffsetY",  typeof(double), 0.0, Description: "Lệch điểm gắp theo Y robot (mm), đo khi vật ở góc 0"),
            new ParamDef("GraspOffsetRz", typeof(double), 0.0, Description: "Lệch góc gắp so với góc vật (độ)")
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

            var pose = input.Get<Pose2D>("Pose");
            if (!pose.IsFinite)
                return VisionToolResult.SystemError($"Tư thế đầu vào không hợp lệ: {pose}");

            if (!_calibration.TryGet(path, out var affine, out string? error))
                return VisionToolResult.ConfigError(error!);

            // Tâm vật và góc vật trong hệ robot
            var (x, y) = affine.Apply(pose.X, pose.Y);
            double rz = affine.TransformAngle(pose.AngleDeg);

            // Độ lệch điểm gắp đo ở góc 0, nên quay theo phần vật đã xoay so với góc 0
            double turn = Pose2D.NormalizeAngle(rz - affine.TransformAngle(0.0)) * Math.PI / 180.0;
            double ox = input.Get<double>("GraspOffsetX");
            double oy = input.Get<double>("GraspOffsetY");

            var robot = new RobotPose(
                x + ox * Math.Cos(turn) - oy * Math.Sin(turn),
                y + ox * Math.Sin(turn) + oy * Math.Cos(turn),
                Pose2D.NormalizeAngle(rz + input.Get<double>("GraspOffsetRz")));

            var result = VisionToolResult.Ok($"Robot {robot}").Add("Robot", robot);
            result.Values["X"] = robot.X.ToString("F3", CultureInfo.InvariantCulture);
            result.Values["Y"] = robot.Y.ToString("F3", CultureInfo.InvariantCulture);
            result.Values["Rz"] = robot.Rz.ToString("F3", CultureInfo.InvariantCulture);
            return result;
        }
    }
}
