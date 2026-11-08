using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Tools.Abstractions;

namespace Vision.Tools.Bases
{
    /// <summary>
    /// Lớp gốc chung. Run là TEMPLATE: kiểm tra hủy, input bắt buộc, tham số, luật tham số,
    /// rồi mới giao cho lớp cơ sở theo loại (RunCore). Exception đổi thành SystemError.
    /// Tool cụ thể KHÔNG kế thừa trực tiếp lớp này mà kế thừa một trong 4 lớp theo loại.
    /// </summary>
    public abstract class VisionToolBase : IVisionTool
    {
        private bool _disposed;

        // Chỉ các lớp trong assembly này (4 lớp theo loại) mới kế thừa trực tiếp được
        private protected VisionToolBase() { }

        public abstract ToolKind Kind { get; }
        public virtual IReadOnlyList<PortDef> Inputs { get; } = Array.Empty<PortDef>();
        public virtual IReadOnlyList<PortDef> Outputs { get; } = Array.Empty<PortDef>();
        public virtual IReadOnlyList<ParamDef> Parameters { get; } = Array.Empty<ParamDef>();

        public virtual void Initialize(ToolContext context) { }

        public IReadOnlyList<string> Validate(VisionToolInput parameters)
        {
            var copy = new VisionToolInput();                    // không làm thay đổi input của người gọi
            foreach (var kv in parameters.All) copy.Add<object?>(kv.Key, kv.Value);

            var errors = new List<string>();
            ApplyParameters(copy, errors);
            if (errors.Count == 0) ValidateRules(copy, errors);
            return errors;
        }

        public VisionToolResult Run(VisionToolInput input, ToolContext context)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            try
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                foreach (var p in Inputs)
                    if (p.Required && !input.Has(p.Name))
                        return VisionToolResult.ConfigError($"[{GetType().Name}] Thiếu input '{p.Name}'");

                // Điền default + ép kiểu (ghi ngược vào input)
                var errors = new List<string>();
                ApplyParameters(input, errors);
                if (errors.Count == 0) ValidateRules(input, errors);
                if (errors.Count > 0)
                    return VisionToolResult.ConfigError($"[{GetType().Name}] {string.Join("; ", errors)}");

                return RunCore(input, context);
            }
            catch (OperationCanceledException)
            {
                throw;                                           // hủy không phải lỗi, để executor xử lý
            }
            catch (Exception ex)
            {
                return VisionToolResult.SystemError($"[{GetType().Name}] {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Luật liên quan nhiều tham số (ví dụ Min không lớn hơn Max). Đã được ép kiểu sẵn.</summary>
        protected virtual void ValidateRules(VisionToolInput parameters, List<string> errors) { }

        /// <summary>Chỉ 4 lớp theo loại được override (và đều sealed), nên tool cụ thể không thể bỏ qua template.</summary>
        private protected abstract VisionToolResult RunCore(VisionToolInput input, ToolContext context);

        private void ApplyParameters(VisionToolInput target, List<string> errors)
        {
            foreach (var p in Parameters)
            {
                try
                {
                    object? raw = target.Has(p.Name) ? target.All[p.Name] : p.Default;
                    target.Add(p.Name, p.Coerce(raw));
                }
                catch (ArgumentException ex)
                {
                    errors.Add(ex.Message);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnDispose();
        }

        protected virtual void OnDispose() { }
    }
}
