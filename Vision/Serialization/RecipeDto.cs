using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Vision.Tools;

namespace Vision.Serialization
{
    // DTO = bản "chỉ dữ liệu" của graph để ghi/đọc file. Không chứa object runtime (tool, port, ảnh...).
    // Đổi tên thuộc tính ở đây là đổi định dạng file: đổi thì phải tăng FormatVersion.

    public sealed class RecipeDto
    {
        public int FormatVersion { get; set; } = GraphSerializer.CurrentFormatVersion;
        public string Name { get; set; } = "";
        public List<NodeDto> Nodes { get; set; } = new();
        public List<ConnectionDto> Connections { get; set; } = new();
    }

    public sealed class NodeDto
    {
        public Guid Id { get; set; }

        /// <summary>Id của tool (ToolDescriptor.Id), không phải tên lớp C#.</summary>
        public string Tool { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Chú thích của người dùng (có thể trống).</summary>
        public string Description { get; set; } = "";

        /// <summary>Giá trị tham số. Khi đọc là JsonElement, tool ép kiểu bằng ParamDef.Coerce.</summary>
        public Dictionary<string, JsonElement> Parameters { get; set; } = new();
    }

    public sealed class ConnectionDto
    {
        public EndpointDto From { get; set; } = new();
        public EndpointDto To { get; set; } = new();
    }

    public sealed class EndpointDto
    {
        public Guid Node { get; set; }
        public string Port { get; set; } = "";
    }
}
