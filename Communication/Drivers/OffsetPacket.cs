using Communication.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Communication.Drivers
{
    /// <summary>
    /// Dựng gói tin độ lệch gửi cho robot từ mẫu do người dùng đặt.
    /// Mẫu là chữ thường kèm các ô thay thế trong ngoặc nhọn và mã thoát (\r \n \t \xHH):
    ///   {id}   số thứ tự chu kỳ
    ///   {dx} {dy} {dz} {drz}   độ lệch (mm, độ), đã nhân hệ số, định dạng theo số lẻ đã chọn
    ///   Có thể chỉ định định dạng riêng: {dx:F2}, {dx:+0.000;-0.000}, {id:D6}
    /// Ví dụ: "X{dx},Y{dy},R{drz}\r\n"  →  "X1.250,Y-0.800,R0.500" + CRLF.
    /// </summary>
    public static class OffsetPacket
    {
        private static readonly Regex Placeholder = new(@"\{(\w+)(?::([^}]*))?\}", RegexOptions.Compiled);
        private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase) { "id", "dx", "dy", "dz", "drz" };

        /// <summary>Kiểm tra mẫu. Trả về lỗi (ví dụ ô thay thế lạ), hoặc null nếu hợp lệ.</summary>
        public static string? Validate(string template)
        {
            if (string.IsNullOrEmpty(template)) return "Mẫu gói tin đang trống.";
            foreach (Match m in Placeholder.Matches(template))
                if (!Names.Contains(m.Groups[1].Value))
                    return $"Ô thay thế {{{m.Groups[1].Value}}} không có. Dùng: {{id}}, {{dx}}, {{dy}}, {{dz}}, {{drz}}.";
            return null;
        }

        /// <summary>Dựng gói tin. Số luôn dùng dấu chấm thập phân. Mã thoát được đổi thành ký tự thật.</summary>
        public static string Build(string template, double dx, double dy, double dz, double drz,
                                   int cycleId, int decimals = 3, double scale = 1.0)
        {
            string? invalid = Validate(template);
            if (invalid != null) throw new ArgumentException(invalid, nameof(template));
            decimals = Math.Clamp(decimals, 0, 9);
            var inv = CultureInfo.InvariantCulture;

            string text = Placeholder.Replace(template, m =>
            {
                string name = m.Groups[1].Value.ToLowerInvariant();
                string? format = m.Groups[2].Success ? m.Groups[2].Value : null;

                if (name == "id") return cycleId.ToString(format ?? "D", inv);

                double value = name switch { "dx" => dx, "dy" => dy, "dz" => dz, _ => drz } * scale;
                return value.ToString(format ?? "F" + decimals.ToString(inv), inv);
            });

            return Escapes.Unescape(text);
        }

        /// <summary>Hiển thị gói tin để đọc: ký tự điều khiển viết thành \r, \n...</summary>
        public static string Visible(string packet)
        {
            var sb = new StringBuilder();
            foreach (char c in packet)
            {
                switch (c)
                {
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case < ' ': sb.Append("\\x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture)); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

    }
}
