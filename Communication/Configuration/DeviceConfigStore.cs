using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Communication.Configuration
{
    /// <summary>Đọc và lưu DeviceCatalog vào file JSON (mặc định trong thư mục AppData của người dùng).</summary>
    public class DeviceConfigStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string FilePath { get; }

        /// <summary>Điều cần báo người dùng sau lần Load gần nhất (file cũ bị thay, mục được sửa...).</summary>
        public List<string> LoadNotes { get; } = new();

        public DeviceConfigStore(string? filePath = null)
        {
            FilePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VisionGraph", "devices.json");
        }

        public DeviceCatalog Load()
        {
            LoadNotes.Clear();
            if (!File.Exists(FilePath)) return DeviceCatalog.CreateDefault();

            try
            {
                string json = File.ReadAllText(FilePath);
                using (var doc = JsonDocument.Parse(json))
                {
                    // File phiên bản cũ (mảng phẳng, hoặc cấu trúc trước khi gộp Mitsubishi) không đọc được theo cấu trúc mới
                    if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                        !doc.RootElement.TryGetProperty("FormatVersion", out var ver) ||
                        ver.GetInt32() != DeviceCatalog.CurrentVersion)
                        return ReplaceUnreadable("File cấu hình có định dạng cũ");
                }

                var catalog = JsonSerializer.Deserialize<DeviceCatalog>(json, Options);
                if (catalog == null) return ReplaceUnreadable("File cấu hình rỗng");

                LoadNotes.AddRange(catalog.Repair());
                return catalog;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return ReplaceUnreadable("File cấu hình hỏng");
            }
        }

        private DeviceCatalog ReplaceUnreadable(string reason)
        {
            string backup = FilePath + ".bak";
            File.Copy(FilePath, backup, overwrite: true);
            LoadNotes.Add($"{reason}; đã giữ bản sao ở {backup} và dùng cấu hình mặc định.");
            return DeviceCatalog.CreateDefault();
        }

        public void Save(DeviceCatalog catalog)
        {
            catalog.FormatVersion = DeviceCatalog.CurrentVersion;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Ghi ra file tạm rồi mới thay file thật, để mất điện giữa chừng không làm hỏng file cũ
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(catalog, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
    }
}
