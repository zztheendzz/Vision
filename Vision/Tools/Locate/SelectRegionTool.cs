using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Locate
{
    /// <summary>Đặc trưng dùng để lọc vùng. Tên (viết thường) trùng tên đặc trưng của Halcon.</summary>
    public enum RegionFeature
    {
        Area,
        Width,
        Height,
        Circularity,
        Rectangularity,
        Convexity,
        Compactness
    }

    /// <summary>
    /// Lọc các vùng theo một đặc trưng nằm trong khoảng [Min, Max] (Halcon select_shape).
    /// Khác Blob: Blob chỉ lọc theo diện tích và luôn Ok; tool này chọn được đặc trưng khác,
    /// và mặc định coi "không có vùng nào thỏa" là NoResult (không thấy vật) để chu kỳ kết luận NG.
    /// </summary>
    [VisionTool("SelectRegion", "Select Region", Group = "Định vị",
                Description = "Lọc vùng theo diện tích, kích thước hoặc hình dạng")]
    public class SelectRegionTool : PureToolBase
    {
        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Region", typeof(HRegion))
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Region", typeof(HRegion)),
            new PortDef("Count",  typeof(int))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("Feature", typeof(RegionFeature), RegionFeature.Area, Description: "Đặc trưng dùng để lọc"),
            new ParamDef("Min", typeof(double), 500.0, Min: 0, Description: "Giá trị nhỏ nhất"),
            new ParamDef("Max", typeof(double), 99999999.0, Min: 0, Description: "Giá trị lớn nhất"),
            new ParamDef("Connect", typeof(bool), true, Description: "Tách vùng thành các phần liên thông trước khi lọc"),
            new ParamDef("FailIfEmpty", typeof(bool), true, Description: "Không có vùng nào thỏa thì báo không có kết quả (NG)")
        };

        protected override void ValidateRules(VisionToolInput p, List<string> errors)
        {
            if (p.Get<double>("Min") > p.Get<double>("Max"))
                errors.Add("Min lớn hơn Max");
        }

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            var region = input.Get<HRegion>("Region");                  // chỉ mượn, không Dispose
            string feature = input.Get<RegionFeature>("Feature").ToString().ToLowerInvariant();
            double min = input.Get<double>("Min");
            double max = input.Get<double>("Max");
            bool connect = input.Get<bool>("Connect");
            bool failIfEmpty = input.Get<bool>("FailIfEmpty");

            // Tách phần liên thông (nếu cần). Bản tách là object tạm do tool tạo ra nên tự Dispose.
            HRegion? parts = connect ? region.Connection() : null;
            HRegion source = parts ?? region;

            HRegion selected;
            try
            {
                selected = source.CountObj() == 0
                    ? new HRegion()                                     // vùng rỗng: không có gì để lọc
                    : source.SelectShape(feature, "and", min, max);
            }
            finally
            {
                parts?.Dispose();
            }

            int count = selected.CountObj();
            if (count == 0)
            {
                selected.Dispose();                                     // không đưa vào result thì phải tự giải phóng
                string message = $"Không có vùng nào có {feature} trong [{min}, {max}]";
                return failIfEmpty ? VisionToolResult.NoResult(message)
                                   : VisionToolResult.Ok(message).Add("Count", 0);
            }

            var result = VisionToolResult.Ok($"Count = {count}")
                .Add("Region", selected)                                // result sở hữu và Dispose
                .Add("Count", count);
            result.Values["Count"] = count.ToString();
            if (context.NeedDisplay) result.AddDisplay(selected.CopyObj(1, -1));
            return result;
        }
    }
}
