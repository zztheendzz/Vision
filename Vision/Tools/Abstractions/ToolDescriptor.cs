using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Graph;
using Vision.Tools.Registry;
using Vision.Tools.Abstractions;

namespace Vision.Tools.Abstractions
{
    /// <summary>
    /// Mô tả bất biến của một loại tool: đọc ra một lần lúc đăng ký, UI/toolbox dùng mà không cần tạo tool.
    /// </summary>
    public sealed record ToolDescriptor(
        string Id,
        string DisplayName,
        string Group,
        string Description,
        ToolKind Kind,
        Type ToolType,
        IReadOnlyList<PortDef> Inputs,
        IReadOnlyList<PortDef> Outputs,
        IReadOnlyList<ParamDef> Parameters)
    {
        public IVisionTool Create() => (IVisionTool)Activator.CreateInstance(ToolType)!;
    }
}
