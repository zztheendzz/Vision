using System;
using System.Collections.Generic;
using System.Text;
using Vision.Services;

namespace Vision.Core
{
    /// <summary>Ngữ cảnh một lần chạy, bất biến. Mặc định là DryRun (an toàn).</summary>
    public sealed class ToolContext
    {
        public RunMode Mode { get; }
        public CancellationToken CancellationToken { get; }
        public IVisionLogger Logger { get; }

        /// <summary>false khi chạy sản xuất: tool bỏ qua việc tạo bản copy để hiển thị.</summary>
        public bool NeedDisplay { get; }

        public ToolContext(
            RunMode mode = RunMode.DryRun,
            CancellationToken cancellationToken = default,
            IVisionLogger? logger = null,
            bool needDisplay = true)
        {
            Mode = mode;
            CancellationToken = cancellationToken;
            Logger = logger ?? NullVisionLogger.Instance;
            NeedDisplay = needDisplay;
        }

        public ToolContext WithLogger(IVisionLogger logger)
            => new(Mode, CancellationToken, logger, NeedDisplay);
    }
}
