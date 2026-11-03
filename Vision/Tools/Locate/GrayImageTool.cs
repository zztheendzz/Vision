using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Locate
{
    [VisionTool("RgbToGray", "RGB → Gray", Group = "Tiền xử lý", Description = "Chuyển ảnh màu sang ảnh xám")]
    public class RgbToGrayTool : PureToolBase
    {
        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Image", typeof(HImage))
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Image", typeof(HImage))
        };

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            var image = input.Get<HImage>("Image");            // chỉ mượn, không Dispose

            int channels = image.CountChannels();
            HImage gray = channels switch
            {
                3 => image.Rgb1ToGray(),
                1 => image.CopyImage(),                          // đã là ảnh xám: phải COPY, không trả lại chính input
                _ => null!
            };

            if (gray == null)
                return VisionToolResult.ConfigError($"Ảnh có {channels} kênh, chỉ hỗ trợ 1 hoặc 3 kênh");

            return VisionToolResult.Ok().Add("Image", gray);     // result sở hữu và Dispose
        }
    }
}
