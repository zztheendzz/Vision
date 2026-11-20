using System.Text;
using Communication.Transport;

namespace Communication.Configuration
{
    /// <summary>
    /// Toàn bộ cấu hình thiết bị: cài đặt chung theo loại + danh sách thiết bị.
    /// Chứa luật thêm (tự đánh số), đổi tên, xóa, kiểm tra hợp lệ; không phụ thuộc giao diện nên kiểm thử được.
    /// </summary>
    public class DeviceCatalog
    {
        public const int CurrentVersion = 3;

        public int FormatVersion { get; set; } = CurrentVersion;

        /// <summary>Cài đặt chung: khóa ngoài là tên loại (DeviceType.ToString()), khóa trong là khóa trường.</summary>
        public Dictionary<string, Dictionary<string, string>> Groups { get; set; } = new();

        public List<DeviceConfig> Devices { get; set; } = new();

        // ---------- Cài đặt chung ----------

        public string GetGroupValue(DeviceType type, string key)
        {
            if (Groups.TryGetValue(type.ToString(), out var g) && g.TryGetValue(key, out var v)) return v;
            return DeviceSchemas.For(type).FindGroup(key)?.Default ?? "";
        }

        /// <summary>Như GetGroupValue, nhưng ô đang để trống thì dùng giá trị mặc định (file cấu hình cũ hoặc ô bị xóa làm mẫu lệnh / kết quả rỗng).</summary>
        public string GetGroupValueOrDefault(DeviceType type, string key)
        {
            string v = GetGroupValue(type, key);
            return v.Length > 0 ? v : DeviceSchemas.For(type).FindGroup(key)?.Default ?? "";
        }

        public void SetGroupValue(DeviceType type, string key, string value)
        {
            if (!Groups.TryGetValue(type.ToString(), out var g))
                Groups[type.ToString()] = g = new Dictionary<string, string>();
            g[key] = value;
        }

        // ---------- Thêm / đặt tên / xóa ----------

        /// <summary>Tên cho thiết bị mới: "Tiền tố n" với n là số nhỏ nhất ≥ 1 chưa ai dùng (không phân biệt hoa thường).</summary>
        public string NextName(DeviceType type)
        {
            string prefix = DeviceSchemas.For(type).NamePrefix;
            var used = new HashSet<string>(
                Devices.Where(d => d.Type == type).Select(d => d.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);

            int n = 1;
            while (used.Contains($"{prefix} {n}")) n++;
            return $"{prefix} {n}";
        }

        public DeviceConfig AddDevice(DeviceType type)
        {
            var schema = DeviceSchemas.For(type);
            var device = new DeviceConfig
            {
                Type = type,
                Name = NextName(type),
                Enabled = schema.DefaultEnabled,
                SourcePlcId = schema.HasPlcSource ? Devices.FirstOrDefault(d => d.Type == DeviceType.PLC)?.Id : null
            };
            if (type == DeviceType.Fixture)
            {
                // Số khuôn nhỏ nhất chưa ai dùng
                var used = Devices.Where(d => d.Type == DeviceType.Fixture).Select(d => d.GetInt("FixtureNo", 1)).ToHashSet();
                int ss = 1;
                while (used.Contains(ss)) ss++;
                device.Set("FixtureNo", ss.ToString());
            }
            Devices.Add(device);
            return device;
        }

        /// <summary>Kiểm tra tên mới cho thiết bị. Trả về lỗi, hoặc null nếu dùng được.</summary>
        public string? CheckName(DeviceConfig device, string? newName)
        {
            string name = (newName ?? "").Trim();
            if (name.Length == 0) return "Tên thiết bị không được để trống.";
            if (name.Length > 40) return "Tên thiết bị tối đa 40 ký tự.";

            bool duplicate = Devices.Any(d => d != device && d.Type == device.Type &&
                                              string.Equals(d.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            return duplicate ? $"Đã có {DeviceSchemas.For(device.Type).DisplayName} tên '{name}'." : null;
        }

        /// <summary>Xóa thiết bị. Nếu là PLC thì thiết bị đang chọn PLC đó bị bỏ chọn; trả về danh sách bị ảnh hưởng.</summary>
        public List<DeviceConfig> Remove(DeviceConfig device)
        {
            Devices.Remove(device);
            var affected = new List<DeviceConfig>();
            if (device.Type != DeviceType.PLC) return affected;

            foreach (var d in Devices.Where(d => d.SourcePlcId == device.Id))
            {
                d.SourcePlcId = null;
                affected.Add(d);
            }
            return affected;
        }

        public List<DeviceConfig> UsersOfPlc(DeviceConfig plc)
            => Devices.Where(d => d.SourcePlcId == plc.Id).ToList();

        // ---------- Kết nối ----------

        /// <summary>
        /// Thiết bị này có nối thật không (không phải mô phỏng).
        /// PLC: Chế độ Mitsubishi (hoặc MX Component khi bật MX_COMPONENT). Robot: Mitsubishi. Khuôn test: khác Simulated. PC: TCP Client. Camera: giao thức GigE Vision. Máy quét: chưa nối.
        /// </summary>
        public bool IsMeantToConnect(DeviceConfig d) => d.Type switch
        {
#if MX_COMPONENT
            DeviceType.PLC => d.GetString(DeviceSchemas.KeyMode) is DeviceSchemas.Mitsubishi or DeviceSchemas.MxComponent,
#else
            DeviceType.PLC => d.GetString(DeviceSchemas.KeyMode) == DeviceSchemas.Mitsubishi,
#endif
            DeviceType.Robot => d.GetString(DeviceSchemas.KeyMode) == DeviceSchemas.Mitsubishi,
            DeviceType.Fixture => GetGroupValue(d.Type, DeviceSchemas.KeyMode) != DeviceSchemas.Simulated,
            DeviceType.PC => GetGroupValue(d.Type, DeviceSchemas.KeyMode) == "TCP Client",
            DeviceType.Camera => d.GetString("Protocol") == DeviceSchemas.GigE,
            _ => false
        };

        /// <summary>
        /// Thông số TCP thô cho Khuôn test và PC (PLC / Robot tự nối bằng thư viện của Mitsubishi nên không dùng).
        /// Trả về null nếu thiết bị tắt, chạy mô phỏng, loại không dùng TCP thô, hoặc IP / cổng chưa hợp lệ.
        /// </summary>
        public TcpOptions? GetTcpOptions(DeviceConfig d)
        {
            if (!d.Enabled || d.Type is not (DeviceType.Fixture or DeviceType.PC) || !IsMeantToConnect(d))
                return null;

            string host = d.GetString(DeviceSchemas.KeyIp);
            int port = d.GetInt(DeviceSchemas.KeyPort);
            if (host.Length == 0 || DeviceSchemas.For(d.Type).FindDevice(DeviceSchemas.KeyIp)!.Validate(host) != null) return null;
            if (port < 1 || port > 65535) return null;

            byte[] tx = Escapes.ToBytes(GetGroupValue(d.Type, DeviceSchemas.KeyTx));
            byte[] rx = Escapes.ToBytes(GetGroupValue(d.Type, DeviceSchemas.KeyRx));
            int timeout = d.Type == DeviceType.PC ? d.GetInt(DeviceSchemas.KeyTimeoutMs, 2000) : 3000;

            return new TcpOptions(host, port, timeout, tx, rx, Encoding.ASCII);
        }

        // ---------- Kiểm tra toàn bộ ----------

        /// <summary>Trả về TẤT CẢ lỗi (rỗng nếu hợp lệ). Dùng trước khi lưu. Ô đang ẩn không bị kiểm tra.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            foreach (var schema in DeviceSchemas.All)
            {
                foreach (var f in schema.GroupFields.Where(fd => fd.IsVisible(k => GetGroupValue(schema.Type, k))))
                {
                    string? e = f.Validate(GetGroupValue(schema.Type, f.Key));
                    if (e != null) errors.Add($"{schema.DisplayName} (chung) – {e}");
                }
            }

            foreach (var d in Devices)
            {
                var schema = DeviceSchemas.For(d.Type);

                string? nameError = CheckName(d, d.Name);
                if (nameError != null) errors.Add(nameError);

                foreach (var f in schema.DeviceFields.Where(fd => fd.IsVisible(d.GetString)))
                {
                    string? e = f.Validate(d.GetString(f.Key));
                    if (e != null) errors.Add($"{d.Name} – {e}");
                }

                if (d.SourcePlcId is Guid plc && !Devices.Any(x => x.Id == plc && x.Type == DeviceType.PLC))
                    errors.Add($"{d.Name} – PLC nguồn không còn tồn tại.");

                // Đã bật và nối thật thì phải có IP (và cổng) để nối
                var ipField = schema.FindDevice(DeviceSchemas.KeyIp);
                if (d.Enabled && IsMeantToConnect(d) && ipField != null && ipField.IsVisible(d.GetString) &&
                    d.GetString(DeviceSchemas.KeyIp).Length == 0)
                    errors.Add($"{d.Name} – chưa nhập địa chỉ IP.");
            }

            foreach (var dup in Devices.Where(d => d.Type == DeviceType.Fixture && d.Values.ContainsKey("FixtureNo"))   // khuôn cũ chưa có số khuôn thì bỏ qua
                                       .GroupBy(d => d.GetInt("FixtureNo", 1)).Where(g => g.Count() > 1))
                errors.Add($"Khuôn test trùng số khuôn {dup.Key:00}: {string.Join(", ", dup.Select(d => d.Name))}.");

            return errors;
        }

        // ---------- Khởi tạo / chỉnh sau khi đọc file ----------

        /// <summary>Danh sách ban đầu: mỗi loại một thiết bị, đánh số 1 (PLC trước để thiết bị khác chọn PLC 1 làm nguồn).</summary>
        public static DeviceCatalog CreateDefault()
        {
            var c = new DeviceCatalog();
            foreach (var type in new[] { DeviceType.PLC, DeviceType.Camera, DeviceType.Scanner,
                                         DeviceType.Robot, DeviceType.Fixture, DeviceType.PC })
                c.AddDevice(type);
            return c;
        }

        /// <summary>
        /// Sửa các sai lệch nhẹ sau khi đọc file (tên trống/trùng, PLC nguồn mất) thay vì từ chối cả file.
        /// Trả về danh sách điều đã sửa để báo người dùng.
        /// </summary>
        public List<string> Repair()
        {
            var notes = new List<string>();
            var seen = new HashSet<Guid>();

            foreach (var d in Devices.ToList())
            {
                if (!seen.Add(d.Id)) d.Id = Guid.NewGuid();
                d.Values ??= new Dictionary<string, string>();

                if (string.IsNullOrWhiteSpace(d.Name) || CheckName(d, d.Name) != null)
                {
                    string old = d.Name;
                    d.Name = "";
                    d.Name = NextName(d.Type);
                    notes.Add($"Đã đổi tên '{old}' thành '{d.Name}' (trống hoặc trùng).");
                }

#if !MX_COMPONENT
                // MX Component đang tắt mà file cấu hình còn chế độ đó: chuyển về giả lập, không để ô chọn trống
                if (d.Type == DeviceType.PLC && d.Values.TryGetValue(DeviceSchemas.KeyMode, out var mode)
                    && mode == DeviceSchemas.MxComponent)
                {
                    d.Values[DeviceSchemas.KeyMode] = DeviceSchemas.Simulated;
                    d.Values.Remove("StationNo");
                    notes.Add($"{d.Name}: chế độ MX Component đang tắt, đã chuyển sang Simulated. Chọn lại Mitsubishi và nhập IP của PLC để nối thật.");
                }
#endif

                if (d.SourcePlcId is Guid plc && !Devices.Any(x => x.Id == plc && x.Type == DeviceType.PLC))
                {
                    d.SourcePlcId = null;
                    notes.Add($"{d.Name}: PLC nguồn cũ không còn, đã bỏ chọn.");
                }
            }
            return notes;
        }
    }
}
