using System;
using System.Collections.Generic;
using System.Text;
using Vision.Graph;
using Vision.Tools.Abstractions;
using Vision.Tools.Locate;

namespace UI.ViewModels
{
    /// <summary>
    /// Hình học ROI dạng dữ liệu thuần (pixel, góc theo độ), dùng để trao đổi giữa khung ảnh và tham số của node.
    /// Luôn dùng bản đã Normalized: làm tròn như nhau ở mọi nơi để so sánh bằng nhau được.
    /// </summary>
    public sealed record RoiGeometry(
        RoiShape Shape, double Row, double Column, double Phi, double Length1, double Length2, double Radius)
    {
        public static RoiGeometry FromNode(VisionNode node) => new RoiGeometry(
            node.GetParameter<RoiShape>(RoiParameters.Shape),
            node.GetParameter<double>(RoiParameters.Row),
            node.GetParameter<double>(RoiParameters.Column),
            node.GetParameter<double>(RoiParameters.Phi),
            node.GetParameter<double>(RoiParameters.Length1),
            node.GetParameter<double>(RoiParameters.Length2),
            node.GetParameter<double>(RoiParameters.Radius)).Normalized();

        /// <summary>Làm tròn 0.1 pixel, đưa góc về [-180, 180), kích thước tối thiểu 1 pixel.</summary>
        public RoiGeometry Normalized() => this with
        {
            Row = Math.Round(Row, 1),
            Column = Math.Round(Column, 1),
            Phi = Math.Round(((Phi + 180.0) % 360.0 + 360.0) % 360.0 - 180.0, 1),
            Length1 = Math.Max(1.0, Math.Round(Length1, 1)),
            Length2 = Math.Max(1.0, Math.Round(Length2, 1)),
            Radius = Math.Max(1.0, Math.Round(Radius, 1))
        };

        /// <summary>Ghi vào các tham số của node (chỉ tham số mà hình đó dùng). Sai thì ném ArgumentException.</summary>
        public void ApplyTo(VisionNode node)
        {
            var g = Normalized();
            node.SetParameter(RoiParameters.Row, g.Row);
            node.SetParameter(RoiParameters.Column, g.Column);

            if (g.Shape == RoiShape.Circle)
            {
                node.SetParameter(RoiParameters.Radius, g.Radius);
                return;
            }

            node.SetParameter(RoiParameters.Length1, g.Length1);
            node.SetParameter(RoiParameters.Length2, g.Length2);
            if (g.Shape == RoiShape.RotatedRectangle)
                node.SetParameter(RoiParameters.Phi, g.Phi);
        }
    }
}
