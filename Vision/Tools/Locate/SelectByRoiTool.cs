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
    /// Chọn vùng bằng ROI (vùng quan tâm). ROI mô tả hoàn toàn bằng tham số (hình, tâm, kích thước, góc) nên lưu được
    /// vào recipe; sau này kéo vẽ trên khung ảnh sẽ chỉ ghi lại các tham số này.
    ///
    ///  - Có input Region: Region ra = giao của Region với ROI (chỉ giữ phần nằm trong ROI).
    ///  - Không có Region: Region ra = chính ROI.
    ///  - Có input Image: Image ra = ảnh đã thu hẹp miền xử lý về Region ra (các tool sau chỉ xử lý trong ROI).
    /// Phải có ít nhất một trong hai input. Output nào không có input tương ứng thì không được trả.
    /// ROI nằm ngoài vùng/ảnh (giao rỗng) → NoResult.
    /// Tọa độ tính theo pixel, gốc ở góc trên-trái ảnh: Row (hàng) xuống dưới, Column (cột) sang phải.
    /// </summary>
    [VisionTool("SelectByRoi", "Select By ROI", Group = "Định vị",
                Description = "Chỉ giữ phần vùng/ảnh nằm trong ROI (chữ nhật, chữ nhật xoay, tròn)")]
    public class SelectByRoiTool : PureToolBase, IRoiEditable
    {
        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Image",  typeof(HImage),  Required: false),
            new PortDef("Region", typeof(HRegion), Required: false)
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
            bool hasImage = input.TryGet<HImage>("Image", out var image);
            bool hasRegion = input.TryGet<HRegion>("Region", out var region);
            if (!hasImage && !hasRegion)
                return VisionToolResult.ConfigError("Cần nối ít nhất một input: Image hoặc Region");

            HRegion roi = RoiRegionFactory.Create(input);
            HRegion final;
            try
            {
                if (hasRegion)
                {
                    final = region.Intersection(roi);           // giao: chỉ giữ phần trong ROI
                    roi.Dispose();                              // ROI chỉ là tạm
                }
                else
                {
                    final = roi;                                // ROI chính là kết quả
                }
            }
            catch
            {
                roi.Dispose();
                throw;
            }

            HImage? reduced = null;
            try
            {
                double area = final.CountObj() == 0 ? 0 : final.AreaCenter(out double _, out double _);
                if (area <= 0)
                {
                    final.Dispose();
                    return VisionToolResult.NoResult("ROI không chứa vùng nào");
                }

                // Có ảnh thì thu hẹp miền xử lý; ROI ngoài ảnh sẽ được Halcon cắt bớt
                if (hasImage) reduced = image.ReduceDomain(final);

                var result = VisionToolResult.Ok($"ROI diện tích {area:F0}")
                    .Add("Region", final);                      // result sở hữu và Dispose
                if (reduced != null) result.Add("Image", reduced);
                result.Values["RoiArea"] = area.ToString("F0");

                if (context.NeedDisplay)
                {
                    // Hiển thị TOÀN ẢNH làm nền (Image ra đã thu hẹp miền nên chỉ còn phần trong ROI), vẽ ROI chồng lên:
                    // người dùng thấy cả ảnh để kéo / đổi cỡ vùng chọn
                    if (hasImage) result.AddDisplay(image.CopyImage());
                    result.AddDisplay(final.CopyObj(1, -1));
                }
                return result;
            }
            catch
            {
                // Chưa giao cho result thì tự giải phóng, không rò rỉ
                reduced?.Dispose();
                final.Dispose();
                throw;
            }
        }
    }
}
