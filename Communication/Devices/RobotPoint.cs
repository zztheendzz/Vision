using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Communication.Devices
{
    /// <summary>Cách gửi kết quả đo cho robot.</summary>
    public enum RobotSendMode
    {
        /// <summary>Tính ra độ lệch (dX, dY, dRz) rồi ghi vào một biến điểm trung gian; chương trình robot tự cộng vào điểm.</summary>
        OffsetViaPoint,
        /// <summary>App tự tính ra điểm mới (điểm đã chọn + độ lệch) rồi ghi đè lên điểm đó trong robot.</summary>
        OverwritePoint
    }

    /// <summary>Cấu hình điểm của robot (lấy từ ô cấu hình thiết bị).</summary>
    public sealed record RobotPointSettings(RobotSendMode Mode, string TargetPoint, string OffsetPoint, char RzAxis);

    /// <summary>Điểm vị trí kiểu MELFA: (X,Y,Z,A,B,C)(cờ1,cờ2). X,Y,Z theo mm; A,B,C theo độ.</summary>
    public readonly record struct RobotPoint(double X, double Y, double Z, double A, double B, double C, int Flag1, int Flag2)
    {
        private static readonly Regex Number = new(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        public double GetAxis(char axis) => char.ToUpperInvariant(axis) switch { 'A' => A, 'B' => B, _ => C };

        public RobotPoint WithAxis(char axis, double value) => char.ToUpperInvariant(axis) switch
        {
            'A' => this with { A = value },
            'B' => this with { B = value },
            _ => this with { C = value }
        };

        /// <summary>Dạng MELFA BASIC: (x,y,z,a,b,c)(f1,f2), luôn dấu chấm thập phân.</summary>
        public string ToMelfa()
        {
            var i = CultureInfo.InvariantCulture;
            return string.Create(i, $"({X:0.###},{Y:0.###},{Z:0.###},{A:0.###},{B:0.###},{C:0.###})({Flag1},{Flag2})");
        }

        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"X {X:0.###}, Y {Y:0.###}, Z {Z:0.###}, A {A:0.###}, B {B:0.###}, C {C:0.###} (cờ {Flag1},{Flag2})");

        /// <summary>Đọc "(1,2,3,4,5,6)(7,0)" hoặc "P1=(1,2,3,4,5,6)(7,0)"; cần ít nhất 6 số.</summary>
        public static bool TryParse(string? text, out RobotPoint point)
        {
            point = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int open = text.IndexOf('(');
            string body = open >= 0 ? text[open..] : text;      // bỏ phần tên "P1=" để số trong tên không lẫn vào

            var values = new List<double>();
            foreach (Match m in Number.Matches(body))
            {
                if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v))
                    return false;
                values.Add(v);
            }
            if (values.Count < 6) return false;

            point = new RobotPoint(values[0], values[1], values[2], values[3], values[4], values[5],
                values.Count > 6 ? (int)values[6] : 0, values.Count > 7 ? (int)values[7] : 0);
            return true;
        }

        /// <summary>Tên biến điểm hợp lệ (chữ, số, gạch dưới; bắt đầu bằng chữ), để không chèn lệnh lạ vào robot.</summary>
        public static bool IsValidName(string? name) => name != null && Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_]{0,15}$");
    }

    public sealed record RobotPointReply(bool Ok, RobotPoint Point, string? Error = null)
    {
        public static RobotPointReply Success(RobotPoint p) => new(true, p);
        public static RobotPointReply Fail(string error) => new(false, default, error);
    }
}
