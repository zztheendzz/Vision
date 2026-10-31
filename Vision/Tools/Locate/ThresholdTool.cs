using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Bases;

namespace Vision.Tools.Locate
{
    public class ThresholdTool : PureToolBase
    {
        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Image", typeof(HImage))
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
            HRegion region = image.Threshold(input.Get<double>("Min"), input.Get<double>("Max"));

            var result = VisionToolResult.Ok().Add("Region", region);
            if (context.NeedDisplay) result.AddDisplay(region.CopyObj(1, -1));
            return result;
        }
    }
}
