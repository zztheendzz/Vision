using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Geometry
{
    /// <summary>
    /// Tư thế của vật trong ẢNH (đơn vị pixel).
    ///  - Row (hàng, đi xuống) và Column (cột, đi sang phải) là vị trí điểm tham chiếu của vật.
    ///  - AngleDeg: góc xoay so với mẫu đã học, theo độ, ngược chiều kim đồng hồ NHƯ NHÌN TRÊN ẢNH (quy ước của Halcon).
    ///    0 = đúng hướng như ảnh mẫu. Luôn nằm trong (-180, 180].
    /// Trong hệ (x = Column, y = Row), hướng của vật có vector đơn vị (cos a, -sin a) vì trục y của ảnh đi xuống.
    /// </summary>
    public readonly record struct Pose2D(double Row, double Column, double AngleDeg)
    {
        public double X => Column;
        public double Y => Row;

        public bool IsFinite => double.IsFinite(Row) && double.IsFinite(Column) && double.IsFinite(AngleDeg);

        public override string ToString() => $"(row {Row:F2}, col {Column:F2}, góc {AngleDeg:F2}°)";

        /// <summary>Đưa góc về (-180, 180].</summary>
        public static double NormalizeAngle(double degrees)
        {
            double a = degrees % 360.0;
            if (a > 180.0) a -= 360.0;
            else if (a <= -180.0) a += 360.0;
            return a;
        }
    }

    /// <summary>Tư thế của vật trong hệ tọa độ ROBOT: X, Y theo mm, Rz theo độ (ngược chiều kim đồng hồ, trục Z hướng lên).</summary>
    public readonly record struct RobotPose(double X, double Y, double Rz)
    {
        public override string ToString() => $"(X {X:F3}, Y {Y:F3}, Rz {Rz:F3}°)";
    }

    /// <summary>Độ lệch của vật so với vị trí mẫu, trong hệ tọa độ robot (mm, độ). Dx, Dy = vị trí hiện tại trừ vị trí mẫu.</summary>
    public readonly record struct RobotDelta(double Dx, double Dy, double Drz)
    {
        public override string ToString() => $"(dX {Dx:F3}, dY {Dy:F3}, dRz {Drz:F3}°)";
    }


    /// <summary>
    /// Độ bù để robot gắp đúng khi vật lệch so với lúc dạy (đơn vị mm, độ), trong hệ tọa độ robot.
    /// Ý nghĩa phụ thuộc quy ước (xem PickCompensationTool):
    ///  - Quanh gốc hệ robot : điểm bất kỳ P được bù thành  P' = R(Drz)·P + (Dx, Dy).
    ///  - Quanh điểm mẫu     : (Dx, Dy) = tâm vật hiện tại trừ tâm vật lúc dạy; điểm P được bù thành  P' = O_now + R(Drz)·(P - O_ref).
    /// R(a) là phép xoay ngược chiều kim đồng hồ góc a.
    /// </summary>
    public readonly record struct RobotOffset(double Dx, double Dy, double Drz)
    {
        public override string ToString() => $"(dX {Dx:F3}, dY {Dy:F3}, dRz {Drz:F3}°)";
    }
}
