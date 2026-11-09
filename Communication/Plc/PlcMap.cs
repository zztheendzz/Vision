using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Communication.Plc
{

    public enum SignalKind
    {
        /// <summary>Một bit (M, L, S, X, Y...).</summary>
        Bit,
        /// <summary>Một thanh ghi 16 bit.</summary>
        Word,
        /// <summary>Hai thanh ghi liên tiếp ghép thành số 32 bit.</summary>
        DWord
    }

    public enum TriggerMode
    {
        /// <summary>Giữ trạng thái (bật = có, tắt = không).</summary>
        Level,
        /// <summary>Bật rồi tắt ngay (xung).</summary>
        Pulse,
        /// <summary>Giữ nút thì bật, thả thì tắt (jog).</summary>
        Hold
    }

    /// <summary>Một tín hiệu đơn lẻ: tên chức năng cố định (Key) gắn với một địa chỉ PLC có thể sửa.</summary>
    public sealed class SignalItem
    {
        public string Key { get; set; } = "";          // tên cố định mà code dùng, ví dụ "Cmd.Start"
        public string Title { get; set; } = "";        // tên hiển thị
        public string Group { get; set; } = "";        // nhóm (tab) hiển thị
        public SignalKind Kind { get; set; }
        public TriggerMode Mode { get; set; }
        public string Address { get; set; } = "";      // để trống = máy này không dùng
        public double Scale { get; set; } = 1;         // giá trị thật = giá trị PLC * Scale
        public bool Signed { get; set; }
        public string Note { get; set; } = "";
    }

    /// <summary>Một cột của bảng tín hiệu (ví dụ Pass, Fail, Dx).</summary>
    public sealed class SignalColumn
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public SignalKind Kind { get; set; }
        public TriggerMode Mode { get; set; }
        public double Scale { get; set; } = 1;
        public bool Signed { get; set; }
    }

    /// <summary>Một hàng của bảng: nhãn và địa chỉ theo thứ tự các cột.</summary>
    public sealed class SignalRow
    {
        public string Label { get; set; } = "";
        public List<string> Addresses { get; set; } = new();
    }

    /// <summary>
    /// Bảng tín hiệu theo chỉ số (FOV 0..8, tay hút 0..8, camera 1..8...).
    /// IndexAddress là thanh ghi PLC báo "đang xử lý hàng số mấy" (ví dụ D600). Để trống nếu không có.
    /// Mỗi ô lưu địa chỉ riêng nên bảng không cần đều đặn; có thể tạo dãy tự động từ địa chỉ đầu + bước.
    /// </summary>
    public sealed class SignalTable
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Group { get; set; } = "";
        public string IndexAddress { get; set; } = "";
        public string Note { get; set; } = "";
        public List<SignalColumn> Columns { get; set; } = new();
        public List<SignalRow> Rows { get; set; } = new();
    }

    /// <summary>
    /// Bản đồ tín hiệu PLC của MỘT máy. Code chỉ dùng tên chức năng (Key); địa chỉ lấy từ đây nên đổi máy chỉ cần đổi file.
    /// </summary>
    public sealed class PlcMap
    {
        public const int CurrentVersion = 1;

        public int FormatVersion { get; set; } = CurrentVersion;
        public string Machine { get; set; } = "";
        public List<SignalItem> Items { get; set; } = new();
        public List<SignalTable> Tables { get; set; } = new();

        /// <summary>Các nhóm theo thứ tự xuất hiện (tín hiệu đơn trước, rồi bảng).</summary>
        [JsonIgnore]
        public IReadOnlyList<string> GroupNames
            => Items.Select(i => i.Group).Concat(Tables.Select(t => t.Group))
                    .Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();

        public SignalItem? FindItem(string key)
            => Items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));

        public SignalTable? FindTable(string key)
            => Tables.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Địa chỉ của một tín hiệu đơn; null nếu không có khóa này hoặc máy không dùng (địa chỉ trống).</summary>
        public string? Address(string key)
        {
            string? a = FindItem(key)?.Address;
            return string.IsNullOrWhiteSpace(a) ? null : a;
        }

        /// <summary>Địa chỉ một ô của bảng (hàng đếm từ 0); null nếu không có hoặc trống.</summary>
        public string? Address(string tableKey, int rowIndex, string columnKey)
        {
            var table = FindTable(tableKey);
            if (table == null || rowIndex < 0 || rowIndex >= table.Rows.Count) return null;
            int col = table.Columns.FindIndex(c => string.Equals(c.Key, columnKey, StringComparison.OrdinalIgnoreCase));
            if (col < 0) return null;
            var cells = table.Rows[rowIndex].Addresses;
            return col < cells.Count && !string.IsNullOrWhiteSpace(cells[col]) ? cells[col] : null;
        }

        /// <summary>Cắt khoảng trắng, viết hoa địa chỉ, bù ô thiếu để mỗi hàng đủ số cột.</summary>
        public void Normalize()
        {
            foreach (var item in Items) item.Address = Clean(item.Address);

            foreach (var table in Tables)
            {
                table.IndexAddress = Clean(table.IndexAddress);
                foreach (var row in table.Rows)
                {
                    row.Addresses ??= new List<string>();
                    while (row.Addresses.Count < table.Columns.Count) row.Addresses.Add("");
                    if (row.Addresses.Count > table.Columns.Count)
                        row.Addresses.RemoveRange(table.Columns.Count, row.Addresses.Count - table.Columns.Count);
                    for (int i = 0; i < row.Addresses.Count; i++) row.Addresses[i] = Clean(row.Addresses[i]);
                }
            }
        }

        private static string Clean(string? s) => (s ?? "").Trim().ToUpperInvariant();
    }

    /// <summary>Đọc/ghi file bản đồ tín hiệu (JSON).</summary>
    public static class PlcMapStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>File mặc định, cạnh file cấu hình thiết bị (AppData\VisionGraph).</summary>
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VisionGraph", "plc_map.json");

        public static PlcMap Load(string path)
        {
            var map = JsonSerializer.Deserialize<PlcMap>(File.ReadAllText(path), Options)
                      ?? throw new InvalidDataException("File bản đồ tín hiệu rỗng");
            if (map.FormatVersion != PlcMap.CurrentVersion)
                throw new InvalidDataException($"Không hỗ trợ FormatVersion {map.FormatVersion}");
            map.Normalize();
            PlcMapDefaults.EnsurePickOffset(map);        // file cũ chưa có nhóm Calib gắp: thêm vào (địa chỉ trống)
            return map;
        }

        public static void Save(PlcMap map, string path)
        {
            map.FormatVersion = PlcMap.CurrentVersion;
            map.Normalize();

            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp";                    // ghi tạm rồi thay: mất điện không hỏng file cũ
            File.WriteAllText(temp, JsonSerializer.Serialize(map, Options));
            File.Move(temp, path, overwrite: true);
        }
    }
}
