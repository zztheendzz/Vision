using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Abstractions;

namespace Vision.Tools.Bases
{
    /// <summary>
    /// Tool thuần túy: cùng input cho cùng output, không giữ trạng thái giữa các lượt, không tác dụng phụ.
    /// (Compiler không ép được "không giữ trạng thái": người viết tool tự giữ quy ước này.)
    /// </summary>
    public abstract class PureToolBase : VisionToolBase
    {
        public sealed override ToolKind Kind => ToolKind.Pure;

        private protected sealed override VisionToolResult RunCore(VisionToolInput input, ToolContext context)
            => Execute(input, context);

        protected abstract VisionToolResult Execute(VisionToolInput input, ToolContext context);
    }
}
