using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Locate
{
    [VisionTool("ThresholdTool", "Threshold", Group = "Tiền xử lý", Description = "Tách vùng theo ngưỡng xám (có thể giới hạn trong một Region)")]
    public class ThresholdTool : PureToolBase
    {
        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Image", typeof(HImage)),
            // Tùy chọn: nối Region (vd từ SelectByRoi) thì chỉ tách ngưỡng trong vùng đó; không nối thì xử lý toàn ảnh
            new PortDef("Region", typeof(HRegion), Required: false)
        };
        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Region", typeof(HRegion))
        };
        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("Min", typeof(double), 0.0,   Min: 0, Max: 255, Description: "Ngưỡng dưới"),
            new ParamDef("Max", typeof(double), 128.0, Min: 0, Max: 255, Description: "Ngưỡng trên")
        };
        protected override void ValidateRules(VisionToolInput p, List<string> errors)
        {
            if (p.Get<double>("Min") > p.Get<double>("Max"))
                errors.Add("Min lớn hơn Max");
        }

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            var image = input.Get<HImage>("Image");          // chỉ mượn
            double min = input.Get<double>("Min"), max = input.Get<double>("Max");

            // Có Region nối vào: chỉ xử lý trong vùng đó. Region ra vẫn theo tọa độ ảnh gốc
            HRegion region;
            if (input.TryGet<HRegion>("Region", out var area) && area != null && area.IsInitialized())
            {
                if (area.CountObj() == 0 || area.AreaCenter(out double _, out double _) < 1)
                    return VisionToolResult.NoResult("Region đầu vào rỗng, không có vùng để xử lý");
                using var limited = image.ReduceDomain(area);
                region = limited.Threshold(min, max);
            }
            else
            {
                region = image.Threshold(min, max);
            }

            var result = VisionToolResult.Ok().Add("Region", region);
            if (context.NeedDisplay) result.AddDisplay(region.CopyObj(1, -1));
            return result;
        }
    }
}
