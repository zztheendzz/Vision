using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Communication.Plc
{
    public enum DeviceClass { Bit, Word }

    /// <summary>
    /// Địa chỉ thiết bị kiểu Mitsubishi: chữ + số, ví dụ M601, D600, L850, X1F.
    /// X, Y, B, W (và SB, SW, DX, DY) đánh số thập lục phân; các loại còn lại đánh số thập phân.
    /// Thuần C#, không phụ thuộc thư viện PLC.
    /// </summary>
    public readonly record struct PlcAddress(string Device, int Number)
    {
        private static readonly HashSet<string> BitDevices = new(StringComparer.Ordinal)
            { "X", "Y", "M", "L", "F", "V", "B", "S", "SM", "SB", "DX", "DY" };
        private static readonly HashSet<string> WordDevices = new(StringComparer.Ordinal)
            { "D", "W", "R", "ZR", "SD", "SW" };
        private static readonly HashSet<string> HexDevices = new(StringComparer.Ordinal)
            { "X", "Y", "B", "W", "SB", "SW", "DX", "DY" };

        /// <summary>Thiết bị bit (M, L, X...) hay thiết bị thanh ghi (D, W, R...).</summary>
        public DeviceClass Class => BitDevices.Contains(Device) ? DeviceClass.Bit : DeviceClass.Word;

        public bool IsHex => HexDevices.Contains(Device);

        public static bool TryParse(string? text, out PlcAddress address, out string? error)
        {
            address = default;
            error = null;

            string s = (text ?? "").Trim().ToUpperInvariant();
            if (s.Length == 0) { error = "địa chỉ trống"; return false; }

            int i = 0;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            if (i == 0 || i == s.Length)
            {
                error = $"'{text}' không phải địa chỉ (ví dụ M100, D600)";
                return false;
            }

            string device = s[..i];
            string number = s[i..];
            if (!BitDevices.Contains(device) && !WordDevices.Contains(device))
            {
                error = $"không hỗ trợ thiết bị '{device}' (dùng X Y M L F V B S SM SB D W R ZR SD SW)";
                return false;
            }

            var style = HexDevices.Contains(device) ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
            if (!int.TryParse(number, style, CultureInfo.InvariantCulture, out int n) || n < 0)
            {
                error = $"số thứ tự '{number}' không hợp lệ cho thiết bị {device}";
                return false;
            }

            address = new PlcAddress(device, n);
            return true;
        }

        /// <summary>Địa chỉ cách địa chỉ này delta vị trí (cùng loại thiết bị). False nếu ra ngoài giới hạn.</summary>
        public bool TryOffset(int delta, out PlcAddress result)
        {
            long n = (long)Number + delta;
            if (n < 0 || n > int.MaxValue) { result = default; return false; }
            result = this with { Number = (int)n };
            return true;
        }

        public override string ToString()
            => Device + (IsHex ? Number.ToString("X", CultureInfo.InvariantCulture)
                               : Number.ToString(CultureInfo.InvariantCulture));
    }
}
