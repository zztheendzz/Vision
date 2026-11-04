using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Abstractions;

namespace Vision.Tools.Locate
{
    /// <summary>
    /// Khai báo tham số ROI chuẩn và sinh vùng ROI từ tham số. Dùng chung cho mọi tool có ROI (IRoiEditable),
    /// để tên tham số luôn khớp với giao diện kéo ROI trên ảnh.
    /// </summary>
    public static class RoiRegionFactory
    {
        /// <summary>Bảy tham số ROI chuẩn (xem RoiParameters).</summary>
        public static ParamDef[] ParamDefs() => new[]
        {
            new ParamDef(RoiParameters.Shape,   typeof(RoiShape), RoiShape.Rectangle, Description: "Hình dạng ROI"),
            new ParamDef(RoiParameters.Row,     typeof(double), 240.0, Description: "Hàng của tâm (pixel)"),
            new ParamDef(RoiParameters.Column,  typeof(double), 320.0, Description: "Cột của tâm (pixel)"),
            new ParamDef(RoiParameters.Phi,     typeof(double), 0.0,   Min: -360, Max: 360, Description: "Góc xoay (độ), chỉ dùng cho chữ nhật xoay"),
            new ParamDef(RoiParameters.Length1, typeof(double), 100.0, Min: 0, Description: "Nửa chiều rộng (chữ nhật)"),
            new ParamDef(RoiParameters.Length2, typeof(double), 80.0,  Min: 0, Description: "Nửa chiều cao (chữ nhật)"),
            new ParamDef(RoiParameters.Radius,  typeof(double), 80.0,  Min: 0, Description: "Bán kính (hình tròn)")
        };

        public static void Validate(VisionToolInput p, List<string> errors)
        {
            if (p.Get<RoiShape>(RoiParameters.Shape) == RoiShape.Circle)
            {
                if (p.Get<double>(RoiParameters.Radius) <= 0) errors.Add("Radius phải lớn hơn 0");
            }
            else
            {
                if (p.Get<double>(RoiParameters.Length1) <= 0) errors.Add("Length1 phải lớn hơn 0");
                if (p.Get<double>(RoiParameters.Length2) <= 0) errors.Add("Length2 phải lớn hơn 0");
            }
        }

        /// <summary>Sinh vùng ROI từ tham số. Trả về vùng MỚI, người gọi chịu trách nhiệm Dispose.</summary>
        public static HRegion Create(VisionToolInput p)
        {
            double row = p.Get<double>(RoiParameters.Row);
            double col = p.Get<double>(RoiParameters.Column);

            var roi = new HRegion();
            try
            {
                switch (p.Get<RoiShape>(RoiParameters.Shape))
                {
                    case RoiShape.Circle:
                        roi.GenCircle(row, col, p.Get<double>(RoiParameters.Radius));
                        break;

                    case RoiShape.RotatedRectangle:
                        double phi = p.Get<double>(RoiParameters.Phi) * Math.PI / 180.0;   // Halcon dùng radian
                        roi.GenRectangle2(row, col, phi,
                            p.Get<double>(RoiParameters.Length1), p.Get<double>(RoiParameters.Length2));
                        break;

                    default:                                                                // Rectangle: không xoay
                        roi.GenRectangle2(row, col, 0.0,
                            p.Get<double>(RoiParameters.Length1), p.Get<double>(RoiParameters.Length2));
                        break;
                }
                return roi;
            }
            catch
            {
                roi.Dispose();
                throw;
            }
        }
    }
}
