using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Bases;

namespace Vision.Tools.Locate
{
    /// <summary>Đếm vùng liên thông. Count = 0 vẫn là Ok (đây là tool đếm, 0 là một kết quả hợp lệ).</summary>
    public class BlobTool : PureToolBase
    {
        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Region", typeof(HRegion))
        };
        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Region", typeof(HRegion)),
            new PortDef("Count",  typeof(int)),
            new PortDef("Areas",  typeof(HTuple))
        };
        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("MinArea", typeof(double), 500.0,      Min: 0, Description: "Diện tích nhỏ nhất"),
            new ParamDef("MaxArea", typeof(double), 99999999.0, Min: 0, Description: "Diện tích lớn nhất")
        };

        protected override void ValidateRules(VisionToolInput p, List<string> errors)
        {
            if (p.Get<double>("MinArea") > p.Get<double>("MaxArea"))
                errors.Add("MinArea lớn hơn MaxArea");
        }

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            var region = input.Get<HRegion>("Region");

            using var connected = region.Connection();
            HRegion selected = connected.SelectShape(
                "area", "and", input.Get<double>("MinArea"), input.Get<double>("MaxArea"));

            int count = selected.CountObj();
            HTuple areas = selected.RegionFeatures("area");

            var result = VisionToolResult.Ok($"Count = {count}")
                .Add("Region", selected)
                .Add("Count", count)
                .Add("Areas", areas);
            result.Values["Count"] = count.ToString();
            if (context.NeedDisplay) result.AddDisplay(selected.CopyObj(1, -1));
            return result;
        }
    }
}
