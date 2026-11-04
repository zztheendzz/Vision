using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Geometry
{
    /// <summary>Một cặp điểm hiệu chuẩn: điểm trên ảnh (pixel) và vị trí robot (mm) tại đúng điểm đó.</summary>
    public readonly record struct CalibrationPoint(double PixelX, double PixelY, double RobotX, double RobotY);

}
