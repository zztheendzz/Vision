using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Vision.Geometry;

namespace Vision.Calibration
{
    public static class CalibrationStore
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static CalibrationData Load(string path)
        {
            var data = JsonSerializer.Deserialize<CalibrationData>(File.ReadAllText(path), Options)
                       ?? throw new InvalidDataException("File hiệu chuẩn rỗng");
            data.ToAffine();                  // kiểm tra hợp lệ ngay khi đọc
            return data;
        }

        public static void Save(CalibrationData data, string path)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp";                    // ghi tạm rồi thay thế: mất điện không hỏng file cũ
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static CalibrationData Create(IReadOnlyList<CalibrationPoint> points)
        {
            var affine = Affine2D.Fit(points, out double rms, out double maxError);
            return new CalibrationData
            {
                Matrix = new[] { affine.A, affine.B, affine.C, affine.D, affine.E, affine.F },
                PointCount = points.Count,
                RmsMm = rms,
                MaxErrorMm = maxError,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Đọc CSV, mỗi dòng: pixelX, pixelY, robotX, robotY  (pixelX = cột, pixelY = hàng; robot theo mm).
        /// Phân cách bằng dấu phẩy, chấm phẩy hoặc tab. Số dùng dấu chấm thập phân. Dòng trống, dòng bắt đầu bằng # và dòng tiêu đề bị bỏ qua.
        /// </summary>
        public static List<CalibrationPoint> ReadCsv(string path)
        {
            var points = new List<CalibrationPoint>();
            int lineNo = 0;
            foreach (string raw in File.ReadLines(path))
            {
                lineNo++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                string[] parts = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 4)
                    throw new InvalidDataException($"Dòng {lineNo}: cần đúng 4 số (pixelX, pixelY, robotX, robotY)");

                var v = new double[4];
                bool allNumbers = true;
                for (int i = 0; i < 4; i++)
                    allNumbers &= double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);

                if (!allNumbers)
                {
                    if (points.Count == 0) continue;        // dòng tiêu đề ở đầu file
                    throw new InvalidDataException($"Dòng {lineNo}: có giá trị không phải số");
                }
                points.Add(new CalibrationPoint(v[0], v[1], v[2], v[3]));
            }
            return points;
        }
    }
}
