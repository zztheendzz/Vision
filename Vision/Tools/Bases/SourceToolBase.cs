using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Abstractions;

namespace Vision.Tools.Bases
{
    /// <summary>Tool đưa dữ liệu từ ngoài vào (đọc ảnh, camera). Không có input bắt buộc.</summary>
    public abstract class SourceToolBase : VisionToolBase
    {
        public sealed override ToolKind Kind => ToolKind.Source;

        private protected sealed override VisionToolResult RunCore(VisionToolInput input, ToolContext context)
            => Execute(input, context);

        protected abstract VisionToolResult Execute(VisionToolInput input, ToolContext context);
    }
}
