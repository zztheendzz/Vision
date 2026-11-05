namespace Communication.Configuration
{
    /// <summary>
    /// Cấu hình của MỘT thiết bị. Thuộc tính riêng của từng loại nằm trong Values (khóa → chữ);
    /// khóa và kiểu được mô tả ở DeviceSchemas. Khóa chưa có thì dùng giá trị mặc định của schema.
    /// </summary>
    public class DeviceConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DeviceType Type { get; set; }
        public string Name { get; set; } = "";
        public bool Enabled { get; set; }

        /// <summary>PLC mà thiết bị này lấy tín hiệu (null: chưa chọn / loại này không dùng).</summary>
        public Guid? SourcePlcId { get; set; }

        public Dictionary<string, string> Values { get; set; } = new();

        public string GetString(string key)
        {
            if (Values.TryGetValue(key, out var v)) return v;
            return DeviceSchemas.For(Type).FindDevice(key)?.Default ?? "";
        }

        public int GetInt(string key, int fallback = 0)
            => int.TryParse(GetString(key), System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : fallback;

        public double GetDouble(string key, double fallback = 0)
            => double.TryParse(GetString(key), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

        public bool GetBool(string key) => GetString(key) == "true";

        public void Set(string key, string value) => Values[key] = value;
    }
}
