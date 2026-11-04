using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vision.Geometry;

namespace Vision.Calibration
{
    /// <summary>Nội dung file hiệu chuẩn (JSON). Matrix = [A, B, C, D, E, F] của Affine2D.</summary>
    public sealed class CalibrationData
    {
        public int FormatVersion { get; set; } = 1;
        public double[] Matrix { get; set; } = Array.Empty<double>();
        public int PointCount { get; set; }
        public double RmsMm { get; set; }
        public double MaxErrorMm { get; set; }
        public string CreatedAt { get; set; } = "";

        public Affine2D ToAffine()
        {
            if (FormatVersion != 1) throw new InvalidDataException($"Không hỗ trợ FormatVersion {FormatVersion}");
            if (Matrix.Length != 6) throw new InvalidDataException("Matrix phải có đúng 6 số");
            return new Affine2D(Matrix[0], Matrix[1], Matrix[2], Matrix[3], Matrix[4], Matrix[5]);
        }
    }
}
