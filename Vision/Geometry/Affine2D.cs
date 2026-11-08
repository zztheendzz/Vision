using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Geometry
{

    /// <summary>
    /// Phép biến đổi affine 2D từ pixel sang mm của robot:
    ///   X = A*x + B*y + C
    ///   Y = D*x + E*y + F
    /// với x = Column, y = Row của ảnh. Affine đủ cho camera đặt cố định nhìn thẳng xuống mặt phẳng làm việc
    /// (gồm tỉ lệ, xoay, lệch trục, kể cả đảo chiều). Camera nghiêng nhiều hoặc méo nặng thì cần phép biến đổi phức tạp hơn.
    /// Thuần C#, không phụ thuộc Halcon.
    /// </summary>
    public sealed class Affine2D
    {
        public double A { get; }
        public double B { get; }
        public double C { get; }
        public double D { get; }
        public double E { get; }
        public double F { get; }

        public Affine2D(double a, double b, double c, double d, double e, double f)
        {
            A = a; B = b; C = c; D = d; E = e; F = f;
            if (!new[] { a, b, c, d, e, f }.All(double.IsFinite))
                throw new ArgumentException("Ma trận có giá trị không hợp lệ (NaN/vô cực)");
        }

        /// <summary>Định thức phần tuyến tính. Âm nghĩa là hệ pixel và hệ robot ngược chiều nhau (bình thường với ảnh có trục y xuống).</summary>
        public double Determinant => A * E - B * D;

        /// <summary>Số mm trên mỗi pixel (trung bình).</summary>
        public double MillimetersPerPixel => Math.Sqrt(Math.Abs(Determinant));

        public (double X, double Y) Apply(double pixelX, double pixelY)
            => (A * pixelX + B * pixelY + C, D * pixelX + E * pixelY + F);

        /// <summary>
        /// Đổi góc của vật trong ảnh (Pose2D.AngleDeg) sang góc trong hệ robot (Rz).
        /// Làm bằng cách biến đổi vector hướng nên tự đúng dấu dù hệ ảnh và hệ robot ngược chiều nhau.
        /// </summary>
        public double TransformAngle(double imageAngleDeg)
        {
            double a = imageAngleDeg * Math.PI / 180.0;
            double dx = Math.Cos(a);
            double dy = -Math.Sin(a);                       // trục y của ảnh đi xuống
            double rx = A * dx + B * dy;                    // chỉ phần tuyến tính, không cộng C, F
            double ry = D * dx + E * dy;
            return Pose2D.NormalizeAngle(Math.Atan2(ry, rx) * 180.0 / Math.PI);
        }

        /// <summary>
        /// Ước lượng phép biến đổi từ các cặp điểm bằng bình phương tối thiểu.
        /// Cần ít nhất 3 điểm không thẳng hàng; nên dùng 6 đến 12 điểm rải khắp vùng làm việc.
        /// rms, maxError: sai số còn lại (mm) khi áp ngược lên chính các điểm hiệu chuẩn.
        /// </summary>
        public static Affine2D Fit(IReadOnlyList<CalibrationPoint> points, out double rms, out double maxError)
        {
            if (points.Count < 3)
                throw new ArgumentException($"Cần ít nhất 3 điểm hiệu chuẩn (đang có {points.Count})");

            // Dời gốc pixel về trọng tâm để ma trận bình phương tối thiểu ổn định số học
            double mx = points.Average(p => p.PixelX);
            double my = points.Average(p => p.PixelY);

            // Chuẩn bị hệ N * [a b c]^T = v, giải riêng cho X' và Y'
            var n = new double[3, 3];
            var vx = new double[3];
            var vy = new double[3];
            foreach (var p in points)
            {
                double[] row = { p.PixelX - mx, p.PixelY - my, 1.0 };
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++) n[i, j] += row[i] * row[j];
                    vx[i] += row[i] * p.RobotX;
                    vy[i] += row[i] * p.RobotY;
                }
            }

            double[] sx = Solve3(n, vx);
            double[] sy = Solve3(n, vy);

            // Đổi về gốc pixel ban đầu: X = a*(x-mx) + b*(y-my) + c
            var result = new Affine2D(
                sx[0], sx[1], sx[2] - sx[0] * mx - sx[1] * my,
                sy[0], sy[1], sy[2] - sy[0] * mx - sy[1] * my);

            double sumSq = 0;
            maxError = 0;
            foreach (var p in points)
            {
                var (x, y) = result.Apply(p.PixelX, p.PixelY);
                double err = Math.Sqrt((x - p.RobotX) * (x - p.RobotX) + (y - p.RobotY) * (y - p.RobotY));
                sumSq += err * err;
                if (err > maxError) maxError = err;
            }
            rms = Math.Sqrt(sumSq / points.Count);
            return result;
        }

        /// <summary>Khử Gauss có chọn trụ. Ném lỗi nếu các điểm thẳng hàng (ma trận suy biến).</summary>
        private static double[] Solve3(double[,] matrix, double[] vector)
        {
            var m = new double[3, 4];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) m[i, j] = matrix[i, j];
                m[i, 3] = vector[i];
            }

            double scale = 0;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++) scale = Math.Max(scale, Math.Abs(m[i, j]));
            double tolerance = Math.Max(scale, 1.0) * 1e-9;

            for (int col = 0; col < 3; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < 3; r++)
                    if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;

                if (Math.Abs(m[pivot, col]) < tolerance)
                    throw new InvalidOperationException(
                        "Các điểm hiệu chuẩn thẳng hàng hoặc trùng nhau. Hãy chọn điểm rải rộng trên vùng làm việc.");

                if (pivot != col)
                    for (int k = 0; k < 4; k++) (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);

                for (int r = col + 1; r < 3; r++)
                {
                    double f = m[r, col] / m[col, col];
                    for (int k = col; k < 4; k++) m[r, k] -= f * m[col, k];
                }
            }

            var x = new double[3];
            for (int i = 2; i >= 0; i--)
            {
                double s = m[i, 3];
                for (int j = i + 1; j < 3; j++) s -= m[i, j] * x[j];
                x[i] = s / m[i, i];
            }
            return x;
        }

    }
}
