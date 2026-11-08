using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Geometry
{
    /// <summary>
    /// Phép biến đổi cứng 2D (tịnh tiến X, Y theo mm + xoay theo độ), dùng để ghép và đảo các tư thế.
    /// A.Compose(B) = A rồi B nằm trong hệ của A  (ma trận A·B). Tư thế của một vật cũng chính là phép biến đổi
    /// từ hệ gốc tới hệ của vật.
    /// </summary>
    public readonly record struct Rigid2D(double X, double Y, double ThetaDeg)
    {
        public static Rigid2D FromPose(RobotPose pose) => new(pose.X, pose.Y, pose.Rz);

        public RobotPose ToPose() => new(X, Y, Pose2D.NormalizeAngle(ThetaDeg));

        /// <summary>this · other.</summary>
        public Rigid2D Compose(Rigid2D other)
        {
            double rad = ThetaDeg * Math.PI / 180.0;
            double c = Math.Cos(rad), s = Math.Sin(rad);
            return new Rigid2D(
                X + c * other.X - s * other.Y,
                Y + s * other.X + c * other.Y,
                ThetaDeg + other.ThetaDeg);
        }

        /// <summary>Phép ngược: this.Compose(this.Inverse()) là phép đồng nhất.</summary>
        public Rigid2D Inverse()
        {
            double rad = -ThetaDeg * Math.PI / 180.0;
            double c = Math.Cos(rad), s = Math.Sin(rad);
            return new Rigid2D(-(c * X - s * Y), -(s * X + c * Y), -ThetaDeg);
        }
    }
}
