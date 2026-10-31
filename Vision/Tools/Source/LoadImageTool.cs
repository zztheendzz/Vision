using HalconDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vision.Core;
using Vision.Tools.Bases;

namespace Vision.Tools.Source
{
    public class LoadImageTool : SourceToolBase
    {
        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Image", typeof(HImage))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("Path", typeof(string), "", Description: "Đường dẫn file ảnh")
        };

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            string path = input.Get<string>("Path");
            if (string.IsNullOrWhiteSpace(path))
                return VisionToolResult.ConfigError("Chưa đặt Path");
            if (!File.Exists(path))
                return VisionToolResult.ConfigError($"Không thấy file: {path}");

            var image = new HImage(path);
            var result = VisionToolResult.Ok(path).Add("Image", image);
            if (context.NeedDisplay) result.AddDisplay(image.CopyImage());
            return result;
        }
    }
}
