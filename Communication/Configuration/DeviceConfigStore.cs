using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Communication.Configuration
{
    /// <summary>Đọc và lưu danh sách thiết bị vào file JSON (mặc định trong thư mục AppData của người dùng).</summary>
    public class DeviceConfigStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string FilePath { get; }

        public DeviceConfigStore(string? filePath = null)
        {
            FilePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VisionGraph", "devices.json");
        }

        public List<DeviceConfig> Load()
        {
            if (!File.Exists(FilePath)) return CreateDefaults();

            try
            {
                var list = JsonSerializer.Deserialize<List<DeviceConfig>>(File.ReadAllText(FilePath), Options);
                return list ?? CreateDefaults();
            }
            catch (JsonException)
            {
                // File hỏng: giữ lại bản sao để không mất, rồi dùng danh sách mặc định
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
                return CreateDefaults();
            }
        }

        public void Save(IEnumerable<DeviceConfig> devices)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Ghi ra file tạm rồi mới thay file thật, để mất điện giữa chừng không làm hỏng file cũ
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(devices, Options));
            File.Move(temp, FilePath, overwrite: true);
        }

        /// <summary>Danh sách ban đầu khi chưa có file. Địa chỉ và cổng chỉ là giá trị mẫu để sửa.</summary>
        private static List<DeviceConfig> CreateDefaults() => new()
        {
            new DeviceConfig { Name = "Robot",   Type = DeviceType.Robot,   IpAddress = "192.168.1.10", Port = 9001, Terminator = TerminatorKind.CRLF },
            new DeviceConfig { Name = "PLC",     Type = DeviceType.PLC,     IpAddress = "192.168.1.20", Port = 5000, Terminator = TerminatorKind.None },
            new DeviceConfig { Name = "PC",      Type = DeviceType.PC,      IpAddress = "192.168.1.30", Port = 8000, Terminator = TerminatorKind.CRLF },
            new DeviceConfig { Name = "Scanner", Type = DeviceType.Scanner, IpAddress = "192.168.1.50", Port = 23,   Terminator = TerminatorKind.CR }
        };
    }
}
