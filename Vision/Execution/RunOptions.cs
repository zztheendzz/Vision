using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Graph;
namespace Vision.Execution
{
    /// <summary>
    /// Thứ người ngoài truyền vào cho MỘT lần chạy. Không sửa node, không lưu lại.
    ///  - Mode: chạy thử hay chạy thật (tool có tác dụng phụ chỉ hoạt động khi Live).
    ///  - NeedDisplay: có tạo dữ liệu hiển thị cho từng node không.
    ///  - Seed: dữ liệu đầu vào chung (ví dụ ảnh từ camera, mã sản phẩm), mọi node đều thấy.
    ///  - Tham số ghi đè theo từng node (ví dụ đổi MinArea theo loại sản phẩm).
    /// Thứ tự ưu tiên: Seed &lt; tham số node &lt; tham số ghi đè &lt; dữ liệu từ dây nối.
    /// </summary>
    public class RunOptions
    {
        internal Dictionary<Guid, Dictionary<string, object?>> Overrides { get; } = new();

        /// <summary>Mặc định là chạy thử (an toàn). Muốn tác động thật phải đặt Live rõ ràng.</summary>
        public RunMode Mode { get; set; } = RunMode.DryRun;

        /// <summary>true: tool tạo bản copy để hiển thị (khi chỉnh/thử). false: bỏ qua cho nhanh (khi sản xuất).</summary>
        public bool NeedDisplay { get; set; } = true;

        /// <summary>Vị trí chụp (FOV) của lượt này. Âm = không dùng. Xem ToolContext.Fov.</summary>
        public int Fov { get; set; } = -1;

        /// <summary>
        /// Camera của FOV này. Có giá trị: chỉ chạy các node thuộc nhánh của camera đó (nhánh bắt đầu từ node Camera Image của camera khác bị bỏ qua).
        /// Null = chạy tất cả như bình thường.
        /// </summary>
        public string? ActiveCamera { get; set; }

        /// <summary>Tên camera đầu tiên: node Camera Image để trống tham số Camera lấy camera này.</summary>
        public string? FirstCamera { get; set; }

        /// <summary>
        /// Đường dẫn file mẫu / hiệu chuẩn theo TỪNG node (từ bảng FOV), khóa là Id node. Node có mặt ở đây dùng đường dẫn này
        /// thay cho đường dẫn đặt trong node; node khác không bị ảnh hưởng.
        /// </summary>
        public Dictionary<Guid, (string? ModelPath, string? CalibrationPath)> NodePaths { get; } = new();

        public VisionToolInput Seed { get; } = new();

        public RunOptions SetSeed<T>(string key, T value)
        {
            Seed.Add(key, value);
            return this;
        }

        /// <summary>Ghi đè tham số cho lần chạy này. Kiểm tra tên/kiểu/khoảng ngay (fail fast).</summary>
        public RunOptions SetParameter(VisionNode node, string name, object? value)
        {
            var def = node.Tool.Parameters.FirstOrDefault(p => p.Name == name)
                ?? throw new ArgumentException($"{node.ToolName} không có tham số '{name}'");

            if (!Overrides.TryGetValue(node.Id, out var dict))
                Overrides[node.Id] = dict = new();
            dict[name] = def.Coerce(value);
            return this;
        }
    }
}

