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
        OverwritePoint,
        /// <summary>Gửi một gói tin chữ (mẫu do người dùng đặt) chứa độ lệch qua TCP tới chương trình robot.</summary>
        Packet,
        /// <summary>Độ bù được C# ghi vào các thanh ghi Calib X, Y, Z, R của PLC; PLC chuyển cho robot (robot không cần nối trực tiếp).</summary>
        ViaPlc
    }

    /// <summary>Gói tin độ lệch: đích TCP, mẫu gói tin, số lẻ, hệ số nhân.</summary>
    public sealed record RobotPacketSettings(
        string Host, int Port, string OffsetTemplate, string RejectTemplate, int Decimals, double Scale, int ConnectTimeoutMs = 3000);

    /// <summary>Cấu hình điểm của robot (lấy từ ô cấu hình thiết bị).</summary>
    public sealed record RobotPointSettings(RobotSendMode Mode, string TargetPoint, string OffsetPoint, char RzAxis,
                                            RobotPacketSettings? Packet = null);

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

        private static readonly Regex Group = new(@"\(([^)]*)\)", RegexOptions.Compiled);

        private static bool Numbers(string text, List<double> values)
        {
            foreach (Match m in Number.Matches(text))
            {
                if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v))
                    return false;
                values.Add(v);
            }
            return true;
        }

        /// <summary>
        /// Đọc "P1=(x,y,z,a,b,c)(f1,f2)" hoặc dạng 8 trục "(x,y,z,a,b,c,l1,l2)(f1,f2)" (trục thêm L1, L2 bị bỏ qua).
        /// Cần ít nhất 6 số tọa độ. Không có nhóm cờ thì cờ = 0.
        /// </summary>
        public static bool TryParse(string? text, out RobotPoint point)
        {
            point = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var coords = new List<double>();
            var flags = new List<double>();
            var groups = Group.Matches(text);
            if (groups.Count >= 1)
            {
                if (!Numbers(groups[0].Groups[1].Value, coords)) return false;
                if (groups.Count >= 2 && !Numbers(groups[1].Groups[1].Value, flags)) return false;
            }
            else if (!Numbers(text, coords)) return false;

            if (coords.Count < 6) return false;
            point = new RobotPoint(coords[0], coords[1], coords[2], coords[3], coords[4], coords[5],
                flags.Count > 0 ? (int)flags[0] : 0, flags.Count > 1 ? (int)flags[1] : 0);
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
