using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Abstractions;

namespace Vision.Tools.Bases
{
    /// <summary>
    /// Tool có tác dụng phụ ra ngoài (gửi robot, ghi file).
    /// Lớp cơ sở quyết định: CHỈ RunMode.Live mới gọi ExecuteLive. DryRun và Teach gọi ExecuteSimulated.
    /// Tool con không có chỗ nào để quên kiểm tra chế độ.
    /// </summary>
    public abstract class ActionToolBase : VisionToolBase
    {
        public sealed override ToolKind Kind => ToolKind.Action;

        private protected sealed override VisionToolResult RunCore(VisionToolInput input, ToolContext context)
        {
            if (context.Mode == RunMode.Live)
            {
                // Chốt chặn cuối: đã hủy thì không được tác động ra ngoài
                context.CancellationToken.ThrowIfCancellationRequested();
                return ExecuteLive(input, context);
            }
            return ExecuteSimulated(input, context);
        }

        /// <summary>Thực hiện tác động thật. Chỉ được gọi khi Mode = Live.</summary>
        protected abstract VisionToolResult ExecuteLive(VisionToolInput input, ToolContext context);

        /// <summary>
        /// Phiên bản chạy thử: KHÔNG được tác động ra ngoài.
        /// Mặc định trả Ok kèm cờ IsSimulated, không có output. Tool nào có node phía sau cần output
        /// (ví dụ "Sent = false") thì override và trả output giả.
        /// </summary>
        protected virtual VisionToolResult ExecuteSimulated(VisionToolInput input, ToolContext context)
            => VisionToolResult.Ok($"[{context.Mode}] Bỏ qua tác dụng phụ").MarkSimulated();
    }
}
