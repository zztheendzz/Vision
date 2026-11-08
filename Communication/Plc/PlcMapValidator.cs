using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Plc
{
    public enum IssueLevel { Error, Warning }

    public sealed record MapIssue(IssueLevel Level, string Where, string Message)
    {
        public override string ToString() => $"{(Level == IssueLevel.Error ? "[Lỗi]" : "[Cảnh báo]")} {Where}: {Message}";
    }

    /// <summary>
    /// Kiểm tra bản đồ tín hiệu:
    ///  - Lỗi (không cho lưu): địa chỉ sai cú pháp, bit đặt vào thanh ghi hoặc ngược lại, khóa trống hoặc trùng, hệ số 0.
    ///  - Cảnh báo (cho lưu): hai nơi dùng cùng địa chỉ, hoặc số 32 bit và thanh ghi khác chồng lên nhau.
    ///    Có thể là cố ý (nhiều trục dùng chung một thanh ghi tốc độ), nên chỉ cảnh báo.
    /// Địa chỉ trống nghĩa là "máy này không dùng", không bị báo.
    /// </summary>
    public static class PlcMapValidator
    {
        private const int MaxIssues = 200;

        private sealed record Entry(string Where, PlcAddress Address, int Length);

        public static IReadOnlyList<MapIssue> Validate(PlcMap map)
        {
            var issues = new List<MapIssue>();
            var entries = new List<Entry>();

            void Check(string where, string address, SignalKind kind)
            {
                if (string.IsNullOrWhiteSpace(address)) return;
                if (!PlcAddress.TryParse(address, out var a, out string? error))
                {
                    issues.Add(new(IssueLevel.Error, where, $"địa chỉ '{address}': {error}"));
                    return;
                }
                if (kind == SignalKind.Bit && a.Class != DeviceClass.Bit)
                    issues.Add(new(IssueLevel.Error, where, $"{a} là thanh ghi, nhưng tín hiệu này là bit (dùng M, L, S, X, Y...)"));
                else if (kind != SignalKind.Bit && a.Class != DeviceClass.Word)
                    issues.Add(new(IssueLevel.Error, where, $"{a} là thiết bị bit, nhưng tín hiệu này là thanh ghi (dùng D, W, R...)"));
                else
                    entries.Add(new Entry(where, a, kind == SignalKind.DWord ? 2 : 1));
            }

            // ---- Tín hiệu đơn ----
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in map.Items)
            {
                string where = string.IsNullOrWhiteSpace(item.Key) ? "(tín hiệu không có khóa)" : item.Key;
                if (string.IsNullOrWhiteSpace(item.Key)) issues.Add(new(IssueLevel.Error, where, "thiếu khóa"));
                else if (!keys.Add(item.Key)) issues.Add(new(IssueLevel.Error, where, "khóa bị trùng"));
                if (!double.IsFinite(item.Scale) || item.Scale == 0)
                    issues.Add(new(IssueLevel.Error, where, "hệ số phải khác 0"));
                Check(where, item.Address, item.Kind);
            }

            // ---- Bảng ----
            var tableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in map.Tables)
            {
                string tName = string.IsNullOrWhiteSpace(table.Title) ? table.Key : table.Title;
                if (string.IsNullOrWhiteSpace(table.Key)) issues.Add(new(IssueLevel.Error, tName, "bảng thiếu khóa"));
                else if (!tableKeys.Add(table.Key)) issues.Add(new(IssueLevel.Error, tName, "khóa bảng bị trùng"));

                var colKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var col in table.Columns)
                {
                    if (string.IsNullOrWhiteSpace(col.Key)) issues.Add(new(IssueLevel.Error, tName, $"cột '{col.Title}' thiếu khóa"));
                    else if (!colKeys.Add(col.Key)) issues.Add(new(IssueLevel.Error, tName, $"khóa cột '{col.Key}' bị trùng"));
                    if (!double.IsFinite(col.Scale) || col.Scale == 0)
                        issues.Add(new(IssueLevel.Error, $"{tName} / {col.Title}", "hệ số phải khác 0"));
                }

                Check($"{tName} / thanh ghi chọn chỉ số", table.IndexAddress, SignalKind.Word);

                foreach (var row in table.Rows)
                    for (int c = 0; c < table.Columns.Count && c < row.Addresses.Count; c++)
                        Check($"{tName} / {row.Label} / {table.Columns[c].Title}", row.Addresses[c], table.Columns[c].Kind);
            }

            // ---- Trùng và chồng địa chỉ ----
            foreach (var device in entries.GroupBy(e => e.Address.Device))
            {
                var ranges = device
                    .GroupBy(e => (e.Address.Number, e.Length))
                    .Select(g => new
                    {
                        Start = g.Key.Number,
                        End = g.Key.Number + g.Key.Length - 1,
                        Address = g.First().Address,
                        Names = g.Select(x => x.Where).ToList()
                    })
                    .OrderBy(r => r.Start).ThenBy(r => r.End)
                    .ToList();

                foreach (var r in ranges.Where(r => r.Names.Count > 1))
                    issues.Add(new(IssueLevel.Warning, "Trùng địa chỉ",
                        $"{Range(r.Address, r.End - r.Start + 1)} được dùng cho {Join(r.Names)}. " +
                        "Nếu cố ý dùng chung thì bỏ qua."));

                for (int i = 0; i < ranges.Count; i++)
                    for (int j = i + 1; j < ranges.Count && ranges[j].Start <= ranges[i].End; j++)
                        issues.Add(new(IssueLevel.Warning, "Chồng địa chỉ",
                            $"{Range(ranges[i].Address, ranges[i].End - ranges[i].Start + 1)} ({Join(ranges[i].Names)}) " +
                            $"chồng lên {Range(ranges[j].Address, ranges[j].End - ranges[j].Start + 1)} ({Join(ranges[j].Names)})"));
            }

            if (issues.Count > MaxIssues)
            {
                int extra = issues.Count - MaxIssues;
                issues.RemoveRange(MaxIssues, extra);
                issues.Add(new(IssueLevel.Warning, "Danh sách", $"còn {extra} mục nữa chưa hiển thị. Sửa các mục trên rồi kiểm tra lại."));
            }
            return issues;
        }

        private static string Range(PlcAddress start, int length)
            => length <= 1 || !start.TryOffset(length - 1, out var end) ? start.ToString() : $"{start}-{end}";

        private static string Join(List<string> names)
            => names.Count <= 3 ? string.Join("; ", names) : string.Join("; ", names.Take(3)) + $"; ... (+{names.Count - 3})";
    }
}
