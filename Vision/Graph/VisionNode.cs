using Vision.Tools;
using Vision.Tools.Bases;
using System;
using Vision.Tools.Abstractions;
using Vision.Tools.Registry;
namespace Vision.Graph
{
        public class VisionNode : IDisposable
        {
            private readonly Dictionary<string, object?> _parameters = new();

            public Guid Id { get; }
            public string ToolName { get; }
            public IVisionTool Tool { get; }
            public double X { get; set; }
            public double Y { get; set; }

            public IReadOnlyList<InputPort> Inputs { get; }
            public IReadOnlyList<OutputPort> Outputs { get; }

            /// <summary>Giá trị tham số hiện tại của node (luôn đủ, luôn hợp lệ). Sửa qua SetParameter.</summary>
            public IReadOnlyDictionary<string, object?> Parameters => _parameters;

            public VisionNode(string toolName, Guid? id = null)
            {
                Id = id ?? Guid.NewGuid();
                ToolName = toolName;
                Tool = ToolRegistry.Create(toolName);
                Inputs = Tool.Inputs.Select(d => new InputPort(this, d)).ToList();
                Outputs = Tool.Outputs.Select(d => new OutputPort(this, d)).ToList();

                // Tên tham số không được trùng tên port (cả hai cùng đi vào 1 input của tool)
                var portNames = Tool.Inputs.Select(p => p.Name).ToHashSet();
                foreach (var def in Tool.Parameters)
                {
                    if (portNames.Contains(def.Name))
                        throw new InvalidOperationException(
                            $"Tool '{toolName}': tham số '{def.Name}' trùng tên input port");
                }
                ResetParameters();
            }

            public InputPort Input(string name)
                => Inputs.FirstOrDefault(p => p.Name == name)
                   ?? throw new ArgumentException($"{ToolName} không có input '{name}'");

            public OutputPort Output(string name)
                => Outputs.FirstOrDefault(p => p.Name == name)
                   ?? throw new ArgumentException($"{ToolName} không có output '{name}'");

            /// <summary>Đặt tham số (có kiểm tra tên, ép kiểu, khoảng). Ném ArgumentException nếu sai.</summary>
            public void SetParameter(string name, object? value)
            {
                var def = Tool.Parameters.FirstOrDefault(p => p.Name == name)
                    ?? throw new ArgumentException($"{ToolName} không có tham số '{name}'");
                _parameters[name] = def.Coerce(value);
            }

            public T GetParameter<T>(string name)
                => _parameters.TryGetValue(name, out var v) && v is T t
                    ? t
                    : throw new ArgumentException($"{ToolName}: không có tham số '{name}' kiểu {typeof(T).Name}");

            public void ResetParameters()
            {
                _parameters.Clear();
                foreach (var def in Tool.Parameters)
                    _parameters[def.Name] = def.Coerce(def.Default);   // default sai sẽ lộ ngay lúc tạo node
            }

            public void Dispose() => Tool.Dispose();
        }
    
}
