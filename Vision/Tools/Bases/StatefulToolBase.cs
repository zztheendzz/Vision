using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Abstractions;

namespace Vision.Tools.Bases
{
    /// <summary>
    /// Tool giữ tài nguyên nạp một lần (model, hiệu chuẩn).
    /// Lớp cơ sở lo: gọi nạp, giải phóng bản cũ trước khi nạp lại, chặn Run khi chưa nạp, giải phóng khi Dispose.
    /// Tool con chỉ viết OnInitialize, OnRelease, Execute.
    /// </summary>
    public abstract class StatefulToolBase : VisionToolBase
    {
        private bool _initialized;

        public sealed override ToolKind Kind => ToolKind.Stateful;

        /// <summary>Đã nạp tài nguyên và sẵn sàng chạy.</summary>
        public bool IsInitialized => _initialized;

        public sealed override void Initialize(ToolContext context)
        {
            Release();                                    // nạp lại thì bỏ bản cũ trước
            OnInitialize(context);                        // lỗi (thiếu file...) thì ném exception, _initialized vẫn false
            _initialized = true;
        }

        /// <summary>Gọi khi tham số liên quan đến tài nguyên đổi (đường dẫn model...). Run sẽ báo cần nạp lại.</summary>
        public void Invalidate() => Release();

        protected abstract void OnInitialize(ToolContext context);

        /// <summary>Giải phóng tài nguyên đã nạp. Chỉ được gọi khi đã nạp thành công.</summary>
        protected abstract void OnRelease();

        protected abstract VisionToolResult Execute(VisionToolInput input, ToolContext context);

        private protected sealed override VisionToolResult RunCore(VisionToolInput input, ToolContext context)
        {
            if (!_initialized)
                return VisionToolResult.ConfigError($"[{GetType().Name}] Chưa nạp tài nguyên (Initialize)");
            return Execute(input, context);
        }

        protected sealed override void OnDispose() => Release();

        private void Release()
        {
            if (!_initialized) return;
            _initialized = false;
            OnRelease();
        }
    }
}
