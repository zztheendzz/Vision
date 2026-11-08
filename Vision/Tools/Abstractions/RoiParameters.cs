using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Tools.Abstractions
{
    /// <summary>
    /// Đánh dấu tool có ROI chỉnh được bằng cách kéo trên ảnh.
    /// Tool phải có đúng các tham số chuẩn (xem RoiParameters): Shape, Row, Column, Phi, Length1, Length2, Radius.
    /// Giao diện chỉ đọc/ghi các tham số này, không cần biết tool cụ thể là tool nào.
    /// </summary>
    public interface IRoiEditable
    {
    }

    /// <summary>Tên các tham số ROI chuẩn.</summary>
    public static class RoiParameters
    {
        public const string Shape = "Shape";
        public const string Row = "Row";
        public const string Column = "Column";
        public const string Phi = "Phi";
        public const string Length1 = "Length1";
        public const string Length2 = "Length2";
        public const string Radius = "Radius";
    }
}
