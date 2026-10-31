using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Graph;
namespace Vision.Execution
{
        /// <summary>
        /// Thứ người ngoài truyền vào cho MỘT lần chạy. Không sửa node, không lưu lại.
        ///  - Seed: dữ liệu đầu vào chung (ví dụ ảnh từ camera, mã sản phẩm), mọi node đều thấy.
        ///  - Tham số ghi đè theo từng node (ví dụ đổi MinArea theo loại sản phẩm).
        /// Thứ tự ưu tiên: Seed &lt; tham số node &lt; tham số ghi đè &lt; dữ liệu từ dây nối.
        /// </summary>
        public class RunOptions
        {
            internal Dictionary<Guid, Dictionary<string, object?>> Overrides { get; } = new();

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

