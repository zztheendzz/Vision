using System;
using System.Collections.Generic;
using System.Text;

namespace UI.Services
{
    /// <summary>
    /// Một lần chụp + đo, để hiện thành hàng trong bảng: tọa độ robot ứng với tọa độ pixel của vật trong ảnh.
    /// Robot* : vị trí robot đọc lại sau khi dừng (hiệu chuẩn) hoặc vị trí vật đã quy ra hệ robot (chụp cố định).
    /// Pixel* : kết quả FindShape trong ảnh. Null = không đo được lần này.
    /// </summary>
    public sealed record CaptureSample(
        string Phase, int Index,
        double? RobotX, double? RobotY, double? RobotRz,
        double? PixelRow, double? PixelColumn, double? PixelAngleDeg,
        string Note = "");
}
