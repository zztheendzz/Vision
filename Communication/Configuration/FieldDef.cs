using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Communication.Configuration
{
    public enum FieldKind
    {
        Text,
        Integer,
        Number,
        Bool,
        Choice,
        /// <summary>Địa chỉ bit/thanh ghi PLC, ví dụ M641, L851, D600. Để trống được.</summary>
        Address,
        IpAddress,
        /// <summary>Ký tự kết thúc gói tin, gõ với mã thoát (\r\n, \x03...).</summary>
        Terminator
    }

    /// <summary>
    /// Mô tả MỘT thuộc tính của thiết bị: khóa lưu file, nhãn, kiểu, mặc định, giới hạn.
    /// Giao diện sinh ô nhập từ mô tả này. Ô có thể chỉ hiện khi một ô khác (ShowWhenKey) có đúng giá trị ShowWhenValue
    /// (ví dụ IP chỉ hiện khi Chế độ = Mitsubishi); ô ẩn không bị kiểm tra.
    /// </summary>
    public sealed record FieldDef(
        string Key,
        string Label,
        FieldKind Kind,
        string Default = "",
        string Section = "",
        IReadOnlyList<string>? Choices = null,
        double Min = double.MinValue,
        double Max = double.MaxValue,
        string Hint = "",
        string? ShowWhenKey = null,
        string? ShowWhenValue = null)
    {
        private static readonly Regex AddressPattern = new(@"^[A-Za-z]{1,2}\d+$", RegexOptions.Compiled);

        public bool IsVisible(Func<string, string> get)
            => ShowWhenKey == null || get(ShowWhenKey) == ShowWhenValue;

        /// <summary>Kiểm tra một giá trị chữ. Trả về nội dung lỗi, hoặc null nếu hợp lệ.</summary>
        public string? Validate(string? value)
        {
            value ??= "";
            switch (Kind)
            {
                case FieldKind.Bool:
                    return value is "true" or "false" ? null : $"{Label}: giá trị bật/tắt không hợp lệ.";

                case FieldKind.Choice:
                    return Choices != null && Choices.Contains(value) ? null : $"{Label}: chưa chọn giá trị hợp lệ.";

                case FieldKind.Integer:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                        return $"{Label}: phải là số nguyên.";
                    return i < Min || i > Max ? $"{Label}: phải từ {Min:0.##} đến {Max:0.##}." : null;

                case FieldKind.Number:
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || !double.IsFinite(d))
                        return $"{Label}: phải là số.";
                    return d < Min || d > Max ? $"{Label}: phải từ {Min:0.##} đến {Max:0.##}." : null;

                case FieldKind.Address:
                    // Cho phép danh sách cách dấu phẩy (khuôn test nhiều slot: D1001, D1002, ...)
                    return value.Length == 0 ||
                           value.Split(',', StringSplitOptions.TrimEntries).All(part => AddressPattern.IsMatch(part))
                        ? null
                        : $"{Label}: địa chỉ phải gồm chữ rồi số, ví dụ M641 (nhiều địa chỉ thì cách nhau bằng dấu phẩy).";

                case FieldKind.IpAddress:
                    if (value.Length == 0) return null;
                    return value.Split('.').Length == 4 && IPAddress.TryParse(value, out _)
                        ? null
                        : $"{Label}: địa chỉ IP không hợp lệ.";

                case FieldKind.Terminator:
                    foreach (char c in Escapes.Unescape(value))
                        if (c > 255) return $"{Label}: chỉ nhận ký tự mã 0–255.";
                    return null;

                default:
                    return null;
            }
        }
    }
}
