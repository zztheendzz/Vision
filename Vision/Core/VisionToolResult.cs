using HalconDotNet;

namespace Vision.Core
{
    /// <summary>
    /// Kết quả một lần chạy của một tool. SỞ HỮU mọi IDisposable bên trong (Dispose cùng nhau).
    /// QUY ƯỚC: tool chỉ Add object do chính nó tạo ra. Muốn đưa object lấy từ input ra output
    /// thì phải Clone/CopyObj trước.
    /// </summary>
    public class VisionToolResult : IDisposable
    {
        private readonly Dictionary<string, object?> _data = new();
        private bool _disposed;

        public ResultStatus Status { get; }
        public string Message { get; }

        /// <summary>Chỉ Ok mới cho các node phía sau chạy tiếp.</summary>
        public bool Success => Status == ResultStatus.Ok;

        /// <summary>True nếu kết quả do chế độ chạy thử tạo ra, không phải tác động thật.</summary>
        public bool IsSimulated { get; private set; }

        public Dictionary<string, string> Values { get; } = new();
        public List<HObject> DisplayObjects { get; } = new();
        public IReadOnlyDictionary<string, object?> All => _data;

        private VisionToolResult(ResultStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        public static VisionToolResult Ok(string message = "") => new(ResultStatus.Ok, message);
        public static VisionToolResult NoResult(string message = "") => new(ResultStatus.NoResult, message);
        public static VisionToolResult ConfigError(string message) => new(ResultStatus.ConfigError, message);
        public static VisionToolResult SystemError(string message) => new(ResultStatus.SystemError, message);

        public VisionToolResult MarkSimulated() { IsSimulated = true; return this; }

        public VisionToolResult Add<T>(string key, T value) { _data[key] = value; return this; }
        public VisionToolResult AddDisplay(HObject obj) { DisplayObjects.Add(obj); return this; }

        public bool TryGet<T>(string key, out T value)
        {
            if (_data.TryGetValue(key, out var obj) && obj is T t) { value = t; return true; }
            value = default!;
            return false;
        }

        public T Get<T>(string key)
        {
            if (!_data.TryGetValue(key, out var obj))
                throw new KeyNotFoundException($"Không có data '{key}'.");
            if (obj is not T t)
                throw new InvalidCastException(
                    $"Data '{key}' là {obj?.GetType().Name ?? "null"}, không phải {typeof(T).Name}.");
            return t;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var o in DisplayObjects) o?.Dispose();
            DisplayObjects.Clear();
            foreach (var v in _data.Values)
                if (v is IDisposable d) d.Dispose();
            _data.Clear();
        }
    }
}
