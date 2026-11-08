using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Vision.Tools.Abstractions;
using Vision.Tools.Locate;
using Vision.Tools.Source;
namespace Vision.Tools.Registry
{
    /// <summary>
    /// Danh sách các loại tool. Tự quét assembly tìm lớp có [VisionTool]; thêm tool mới = viết lớp + gắn attribute.
    /// Tool sai quy ước bị kiểm tra và bỏ ngay lúc đăng ký (xem Errors), không đợi đến lúc người dùng kéo ra.
    /// </summary>
    public static class ToolRegistry
    {
        private static readonly Dictionary<string, ToolDescriptor> _map = new(StringComparer.Ordinal);
        private static readonly List<string> _errors = new();
        private static readonly object _lock = new();

        static ToolRegistry() => Scan(typeof(ToolRegistry).Assembly);

        public static IReadOnlyCollection<ToolDescriptor> All { get { lock (_lock) return _map.Values.ToList(); } }
        public static IEnumerable<string> Names => All.Select(d => d.Id);

        /// <summary>Các tool bị từ chối khi đăng ký và lý do. Nên ghi log lúc khởi động.</summary>
        public static IReadOnlyList<string> Errors { get { lock (_lock) return _errors.ToList(); } }

        public static ToolDescriptor Get(string id)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(id, out var d)) return d;

                // Báo đủ thông tin để biết ngay nguyên nhân: sai Id, thiếu attribute, hay bị từ chối lúc đăng ký
                string registered = _map.Count == 0 ? "(không có tool nào)" : string.Join(", ", _map.Keys);
                string rejected = _errors.Count == 0 ? "" : "\nTool bị từ chối lúc đăng ký:\n - " + string.Join("\n - ", _errors);
                throw new KeyNotFoundException(
                    $"Chưa đăng ký tool '{id}'. Đã đăng ký: {registered}.{rejected}");
            }
        }

        public static bool TryGet(string id, out ToolDescriptor descriptor)
        {
            lock (_lock) return _map.TryGetValue(id, out descriptor!);
        }

        public static IVisionTool Create(string id) => Get(id).Create();

        /// <summary>Quét thêm assembly khác (plugin, project tool riêng).</summary>
        public static void Scan(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<VisionToolAttribute>(inherit: false);
                if (attr == null) continue;
                Register(type, attr);
            }
        }

        private static void Register(Type type, VisionToolAttribute attr)
        {
            try
            {
                var descriptor = Build(type, attr);
                lock (_lock)
                {
                    if (_map.ContainsKey(descriptor.Id))
                        throw new InvalidOperationException($"Id '{descriptor.Id}' bị trùng");
                    _map[descriptor.Id] = descriptor;
                }
            }
            catch (Exception ex)
            {
                lock (_lock) _errors.Add($"{type.Name}: {ex.Message}");
            }
        }

        private static ToolDescriptor Build(Type type, VisionToolAttribute attr)
        {
            if (string.IsNullOrWhiteSpace(attr.Id)) throw new InvalidOperationException("Id rỗng");
            if (string.IsNullOrWhiteSpace(attr.DisplayName)) throw new InvalidOperationException("DisplayName rỗng");
            if (!typeof(IVisionTool).IsAssignableFrom(type) || type.IsAbstract)
                throw new InvalidOperationException("Phải là lớp cụ thể kế thừa IVisionTool");
            if (type.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException("Cần constructor không tham số");

            // Tạo một bản mẫu để đọc khai báo rồi bỏ ngay (tool phải nhẹ khi tạo)
            using var sample = (IVisionTool)Activator.CreateInstance(type)!;

            if (sample.Kind == ToolKind.Unknown)
                throw new InvalidOperationException("Kind chưa được gán (hãy kế thừa lớp cơ sở Source/Pure/Stateful/Action)");

            Unique("input", sample.Inputs.Select(p => p.Name));
            Unique("output", sample.Outputs.Select(p => p.Name));
            Unique("tham số", sample.Parameters.Select(p => p.Name));

            var portNames = sample.Inputs.Select(p => p.Name).ToHashSet();
            foreach (var p in sample.Parameters)
            {
                if (portNames.Contains(p.Name))
                    throw new InvalidOperationException($"Tham số '{p.Name}' trùng tên input port");
                p.Coerce(p.Default);        // default sai kiểu/ngoài khoảng sẽ lộ ngay ở đây
            }

            return new ToolDescriptor(
                attr.Id, attr.DisplayName, attr.Group, attr.Description, sample.Kind, type,
                sample.Inputs.ToList(), sample.Outputs.ToList(), sample.Parameters.ToList());
        }

        private static void Unique(string what, IEnumerable<string> names)
        {
            var dup = names.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) throw new InvalidOperationException($"Tên {what} '{dup.Key}' bị trùng");
        }
    }
}
