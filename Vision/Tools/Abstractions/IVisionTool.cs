using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;

namespace Vision.Tools.Abstractions
{
    public interface IVisionTool : IDisposable
    {
        ToolKind Kind { get; }
        IReadOnlyList<PortDef> Inputs { get; }
        IReadOnlyList<PortDef> Outputs { get; }
        IReadOnlyList<ParamDef> Parameters { get; }

        /// <summary>Nạp tài nguyên nặng. Tool không cần thì không làm gì. Được phép gọi lại để nạp lại.</summary>
        void Initialize(ToolContext context);

        /// <summary>Kiểm tra tham số (khoảng + quan hệ giữa các tham số). Trả danh sách lỗi, rỗng nếu hợp lệ.</summary>
        IReadOnlyList<string> Validate(VisionToolInput parameters);

        VisionToolResult Run(VisionToolInput input, ToolContext context);
    }
}
