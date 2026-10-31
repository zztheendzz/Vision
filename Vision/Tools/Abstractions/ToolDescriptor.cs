using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Graph;
using Vision.Tools.Registry;
using Vision.Tools.Abstractions;

namespace Vision.Tools.Abstractions
{
    /*
     sealed record tạo ra một bản ghi (record) không cho phép bất kỳ class hay record nào khác kế thừa (inherit) từ nó.
     */
    public sealed record class  ToolDescriptor(
    string Id,                               // ổn định, nằm trong file lưu
    string DisplayName,                      // hiển thị, đổi tự do
    string Group,                            // nhóm trong toolbox
    ToolKind Kind,                           // Source / Pure / Stateful / Action
    string Description,
    IReadOnlyList<PortDef> Inputs,
    IReadOnlyList<PortDef> Outputs,
    IReadOnlyList<ParamDef> Parameters,      // ParamKind nằm trong ParamDef
    Func<IVisionTool> Create);
}
