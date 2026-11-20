using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Source
{
    /// <summary>
    /// Nguồn ảnh từ camera. Tool KHÔNG tự điều khiển camera: nơi chạy (giao diện, vòng điều khiển) chụp ảnh rồi đưa vào
    /// lượt chạy qua Seed:  "Image@&lt;tên camera&gt;" cho từng camera, và "Image" cho camera đầu tiên.
    /// Tham số Camera để trống = lấy "Image"; có tên = lấy đúng ảnh của camera đó (dùng khi có nhiều camera).
    /// </summary>
    [VisionTool("CameraImage", "Camera Image", Group = "Nguồn ảnh",
                Description = "Lấy ảnh do camera chụp (chạy bằng nút 'Chạy từ camera')")]
    public class CameraImageTool : SourceToolBase
    {
        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Image", typeof(HImage))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("Camera", typeof(string), "",
                         Description: "Chọn camera theo tên đã đặt ở Cấu hình kết nối. Để trống = camera đầu tiên",
                         ChoicesKey: ParamChoices.Cameras)
        };

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            string camera = (input.Get<string>("Camera") ?? "").Trim();
            string key = camera.Length == 0 ? "Image" : "Image@" + camera;

            if (!input.TryGet<HImage>(key, out var source) || source == null || !source.IsInitialized())
            {
                var available = input.All.Keys.Where(k => k == "Image" || k.StartsWith("Image@", StringComparison.Ordinal)).ToList();
                string have = available.Count == 0
                    ? "chưa có ảnh nào được đưa vào lượt chạy này (camera chưa kết nối hoặc không chụp được)"
                    : "ảnh đang có: " + string.Join(", ", available.Select(k => k == "Image" ? "(mặc định)" : k["Image@".Length..]));

                return VisionToolResult.ConfigError(camera.Length == 0
                    ? $"Không có ảnh camera: {have}"
                    : $"Không có ảnh của camera '{camera}': {have}. Tên ở tham số Camera phải đúng tên thiết bị, hoặc để trống");
            }

            // Ảnh seed là của nơi chạy; kết quả của tool phải là bản do tool tự tạo ra
            var image = source.CopyImage();
            var result = VisionToolResult.Ok(camera.Length == 0 ? "camera" : camera).Add("Image", image);
            if (context.NeedDisplay) result.AddDisplay(image.CopyImage());
            return result;
        }
    }
}
