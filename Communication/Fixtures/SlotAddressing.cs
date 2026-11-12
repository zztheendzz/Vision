using Communication.Plc;
using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Fixtures
{
    /// <summary>
    /// Địa chỉ PLC của từng slot. PLC coi mỗi slot như một khuôn riêng, nên mỗi slot có thanh ghi (và bit) riêng.
    /// Ô cấu hình nhận danh sách cách dấu phẩy, thứ tự = số slot (slot 1 là địa chỉ đầu).
    /// Chỉ ghi MỘT địa chỉ mà khuôn có nhiều slot thì các slot lấy địa chỉ liên tiếp (D1001, D1002, D1003...).
    /// </summary>
    public static class SlotAddressing
    {
        /// <summary>Trả về mảng đúng <paramref name="slots"/> phần tử; slot không có địa chỉ thì null. <paramref name="warning"/> báo thiếu / dư.</summary>
        public static string?[] Resolve(string? text, int slots, out string? warning)
        {
            warning = null;
            var result = new string?[Math.Max(0, slots)];
            var items = (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (items.Length == 0 || result.Length == 0) return result;

            if (items.Length == 1 && result.Length > 1)
            {
                if (!PlcAddress.TryParse(items[0], out var first, out _)) return result;
                for (int i = 0; i < result.Length; i++)
                    result[i] = first.TryOffset(i, out var a) ? a.ToString() : null;
                return result;
            }

            for (int i = 0; i < result.Length && i < items.Length; i++)
                result[i] = PlcAddress.TryParse(items[i], out var addr, out _) ? addr.ToString() : null;

            if (items.Length < result.Length)
                warning = $"mới có {items.Length} địa chỉ cho {result.Length} slot (slot {items.Length + 1} trở đi không được ghi)";
            else if (items.Length > result.Length)
                warning = $"có {items.Length} địa chỉ nhưng chỉ có {result.Length} slot (bỏ phần dư)";
            return result;
        }
    }
}
