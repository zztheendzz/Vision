using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Communication.Fov
{
    /// <summary>Một vùng chụp (FOV): PLC ghi số này vào thanh ghi FOV (vd D600), mỗi số gắn với camera, mẫu và hiệu chuẩn riêng.</summary>
    public class FovEntry
    {
        public bool Enabled { get; set; } = true;
        public int Fov { get; set; }

        /// <summary>Tên camera (đúng tên thiết bị). Chỉ camera này chụp và chỉ nhánh của nó chạy.</summary>
        public string Camera { get; set; } = "";

        /// <summary>File mẫu FindShape dùng cho FOV này. Trống = dùng ModelPath đặt trong node.</summary>
        public string ModelPath { get; set; } = "";

        /// <summary>File hiệu chuẩn camera - robot dùng cho FOV này. Trống = dùng CalibrationPath đặt trong node.</summary>
        public string CalibrationPath { get; set; } = "";
    }

    /// <summary>Bảng FOV lưu thành file JSON riêng (fov.json cạnh devices.json), không nằm trong recipe.</summary>
    public class FovTable
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public string FilePath { get; }
        public List<FovEntry> Entries { get; private set; } = new();

        /// <summary>Lỗi của lần Load gần nhất (file hỏng...); null nếu bình thường.</summary>
        public string? LoadError { get; private set; }

        public FovTable(string? filePath = null)
        {
            FilePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VisionGraph", "fov.json");
        }

        public void Load()
        {
            LoadError = null;
            Entries = new List<FovEntry>();
            if (!File.Exists(FilePath)) return;
            try
            {
                Entries = JsonSerializer.Deserialize<List<FovEntry>>(File.ReadAllText(FilePath)) ?? new List<FovEntry>();
            }
            catch (Exception ex)
            {
                LoadError = $"Không đọc được bảng FOV '{FilePath}': {ex.Message}";
            }
        }

        public void Save(IEnumerable<FovEntry> entries)
        {
            Entries = entries.Select(e => new FovEntry
            {
                Enabled = e.Enabled,
                Fov = e.Fov,
                Camera = (e.Camera ?? "").Trim(),
                ModelPath = (e.ModelPath ?? "").Trim(),
                CalibrationPath = (e.CalibrationPath ?? "").Trim()
            }).ToList();

            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Entries, Options));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }

        /// <summary>Lỗi (chữ) nếu bảng không dùng được: trùng số FOV, thiếu camera. null nếu ổn.</summary>
        public static string? Validate(IEnumerable<FovEntry> entries)
        {
            var seen = new HashSet<int>();
            foreach (var e in entries)
            {
                if (e.Fov < 0) return $"FOV {e.Fov}: số FOV không được âm";
                if (!seen.Add(e.Fov)) return $"FOV {e.Fov} bị khai báo hai lần";
                if (e.Enabled && string.IsNullOrWhiteSpace(e.Camera)) return $"FOV {e.Fov}: chưa chọn camera";
            }
            return null;
        }

        public FovEntry? Find(int fov) => Entries.FirstOrDefault(e => e.Fov == fov);
    }
}
