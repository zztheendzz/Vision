using System.Globalization;

namespace Vision.Core
{
    /// <summary>Túi dữ liệu có key đưa vào tool. Input chỉ MƯỢN object, không sở hữu.</summary>
    public class VisionToolInput
    {
        private readonly Dictionary<string, object?> _data = new();

        public IReadOnlyDictionary<string, object?> All => _data;

        public VisionToolInput Add<T>(string key, T value)
        {
            _data[key] = value;
            return this;
        }

        public bool Has(string key) => _data.ContainsKey(key);

        public bool TryGet<T>(string key, out T value)
        {
            if (_data.TryGetValue(key, out var obj) && TryConvert(obj, out value))
                return true;
            value = default!;
            return false;
        }

        public T Get<T>(string key)
        {
            if (!_data.TryGetValue(key, out var obj))
                throw new KeyNotFoundException($"Thiếu input '{key}'.");
            if (!TryConvert<T>(obj, out var v))
                throw new InvalidCastException(
                    $"Input '{key}' là {obj?.GetType().Name ?? "null"}, cần {typeof(T).Name}.");
            return v;
        }

        /// <summary>Dùng cho tham số: không có thì trả default của tool.</summary>
        public T GetOrDefault<T>(string key, T defaultValue)
            => TryGet<T>(key, out var v) ? v : defaultValue;

        // Cho phép int -> double, string -> double... (hữu ích khi tham số đọc từ UI/JSON)
        private static bool TryConvert<T>(object? obj, out T value)
        {
            if (obj is T t) { value = t; return true; }
            if (obj is IConvertible && typeof(IConvertible).IsAssignableFrom(typeof(T)))
            {
                try
                {
                    value = (T)Convert.ChangeType(obj, typeof(T), CultureInfo.InvariantCulture);
                    return true;
                }
                catch { }
            }
            value = default!;
            return false;
        }
    }
}

