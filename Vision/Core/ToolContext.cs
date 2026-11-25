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

        /// <summary>
        /// Vị trí chụp (FOV) của lượt này: PLC ghi vào thanh ghi FOV (vd D600), hoặc người dùng nhập khi chạy tay.
        /// Âm = chưa chọn. Đường dẫn có chữ {FOV} (mẫu, hiệu chuẩn) sẽ được thay bằng số này.
        /// </summary>
        public int Fov { get; }

        /// <summary>Mẫu / hiệu chuẩn của FOV (bảng FOV). Trống = dùng đường dẫn đặt trong node.</summary>
        public string? ModelPathOverride { get; }
        public string? CalibrationPathOverride { get; }

        /// <summary>Đường dẫn file mẫu: của bảng FOV nếu có, không thì từ node; rồi thay {FOV}.</summary>
        public bool TryResolveModelPath(string template, out string path, out string? error)
            => TryResolvePath(string.IsNullOrWhiteSpace(ModelPathOverride) ? template : ModelPathOverride!, out path, out error);

        /// <summary>Đường dẫn file hiệu chuẩn: của bảng FOV nếu có, không thì từ node; rồi thay {FOV}.</summary>
        public bool TryResolveCalibrationPath(string template, out string path, out string? error)
            => TryResolvePath(string.IsNullOrWhiteSpace(CalibrationPathOverride) ? template : CalibrationPathOverride!, out path, out error);

        /// <summary>Thay {FOV} trong đường dẫn bằng số FOV của lượt. Không có {FOV} thì giữ nguyên. Có {FOV} mà chưa chọn FOV thì báo lỗi.</summary>
        public bool TryResolvePath(string template, out string path, out string? error)
        {
            path = template;
            error = null;
            const string key = "{FOV}";
            if (template.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) return true;
            if (Fov < 0)
            {
                error = $"Đường dẫn '{template}' có {key} nhưng lượt chạy này chưa có số FOV. " +
                        "Chạy tay: nhập ô FOV trên thanh công cụ. Chạy tự động: kiểm tra 'Thanh ghi FOV' trong cài đặt PLC.";
                return false;
            }
            path = template.Replace(key, Fov.ToString(), StringComparison.OrdinalIgnoreCase);
            return true;
        }

        public ToolContext(
            RunMode mode = RunMode.DryRun,
            CancellationToken cancellationToken = default,
            IVisionLogger? logger = null,
            bool needDisplay = true,
            int fov = -1,
            string? modelPathOverride = null,
            string? calibrationPathOverride = null)
        {
            Mode = mode;
            CancellationToken = cancellationToken;
            Logger = logger ?? NullVisionLogger.Instance;
            NeedDisplay = needDisplay;
            Fov = fov;
            ModelPathOverride = modelPathOverride;
            CalibrationPathOverride = calibrationPathOverride;
        }

        /// <summary>Bản sao dành riêng cho một node: có đường dẫn mẫu / hiệu chuẩn của node đó (từ bảng FOV).</summary>
        public ToolContext WithPaths(string? modelPath, string? calibrationPath)
            => new(Mode, CancellationToken, Logger, NeedDisplay, Fov,
                   string.IsNullOrWhiteSpace(modelPath) ? ModelPathOverride : modelPath,
                   string.IsNullOrWhiteSpace(calibrationPath) ? CalibrationPathOverride : calibrationPath);

        public ToolContext WithLogger(IVisionLogger logger)
            => new(Mode, CancellationToken, logger, NeedDisplay, Fov, ModelPathOverride, CalibrationPathOverride);
    }
}
