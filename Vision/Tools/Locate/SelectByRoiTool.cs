using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Abstractions;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Locate
{

    public enum RoiShape
    {
        /// <summary>Chữ nhật song song trục ảnh (bỏ qua Phi).</summary>
        Rectangle,
        /// <summary>Chữ nhật xoay một góc Phi.</summary>
        RotatedRectangle,
        Circle
    }

    /// <summary>
    /// Chọn vùng trên ảnh bằng ROI (vùng quan tâm), không làm gì khác. ROI mô tả hoàn toàn bằng tham số (hình, tâm, kích thước, góc)
    /// nên lưu được vào recipe; kéo vẽ trên khung ảnh chỉ ghi lại các tham số này.
    ///
    ///  - Input:  Image (bắt buộc).
    ///  - Output Image:  chính ảnh gốc, giữ nguyên (không cắt, không thu hẹp miền).
    ///  - Output Region: vùng ROI được chọn, tọa độ theo ảnh gốc. Tool sau (FindShape...) nối Image + Region để chỉ làm việc trong vùng này.
    /// ROI nằm hoàn toàn ngoài ảnh (rỗng) → NoResult.
    /// Tọa độ tính theo pixel, gốc ở góc trên-trái ảnh: Row (hàng) xuống dưới, Column (cột) sang phải.
    /// </summary>
    [VisionTool("SelectByRoi", "Select By ROI", Group = "Định vị",
                Description = "Chỉ giữ phần vùng/ảnh nằm trong ROI (chữ nhật, chữ nhật xoay, tròn)")]
    public class SelectByRoiTool : PureToolBase, IRoiEditable
    {
        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Image", typeof(HImage))
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Region", typeof(HRegion)),
            new PortDef("Image",  typeof(HImage))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = RoiRegionFactory.ParamDefs();

        protected override void ValidateRules(VisionToolInput p, List<string> errors)
            => RoiRegionFactory.Validate(p, errors);

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            var image = input.Get<HImage>("Image");                  // chỉ mượn
            HRegion roi = RoiRegionFactory.Create(input);
            HImage? passthrough = null;
            try
            {
                // Chỉ giữ phần ROI nằm trong ảnh
                image.GetImageSize(out int w, out int h);
                using var frame = new HRegion();
                frame.GenRectangle1(0.0, 0.0, h - 1, w - 1);
                var inside = roi.Intersection(frame);
                roi.Dispose();
                roi = inside;

                double area = roi.CountObj() == 0 ? 0 : roi.AreaCenter(out double _, out double _);
                if (area <= 0)
                {
                    roi.Dispose();
                    return VisionToolResult.NoResult("ROI nằm ngoài ảnh");
                }

                passthrough = image.CopyImage();                     // ảnh gốc nguyên vẹn, result sở hữu
                var result = VisionToolResult.Ok($"ROI diện tích {area:F0}")
                    .Add("Region", roi)                              // result sở hữu và Dispose
                    .Add("Image", passthrough);
                result.Values["RoiArea"] = area.ToString("F0");

                if (context.NeedDisplay)
                {
                    result.AddDisplay(image.CopyImage());            // toàn ảnh làm nền để kéo / đổi cỡ vùng chọn
                    result.AddDisplay(roi.CopyObj(1, -1));
                }
                return result;
            }
            catch
            {
                passthrough?.Dispose();
                roi.Dispose();
                throw;
            }
        }
    }
}
