using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vision.Geometry;

namespace Vision.Calibration
{
    /// <summary>
    /// Giữ phép biến đổi đã nạp cho một tool. Nạp lại khi đổi đường dẫn hoặc file được ghi lại.
    /// Không an toàn đa luồng: mỗi tool giữ một bản riêng (tool chỉ chạy trên một luồng tại một thời điểm).
    /// </summary>
    public sealed class CalibrationCache
    {
        private Affine2D? _affine;
        private string? _path;
        private DateTime _stamp;

        public void Clear()
        {
            _affine = null;
            _path = null;
        }

        public bool TryGet(string path, out Affine2D affine, out string? error)
        {
            error = null;
            affine = null!;

            if (!File.Exists(path))
            {
                error = $"Chưa hiệu chuẩn: không thấy file '{path}'";
                return false;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_affine == null || _path != path || _stamp != stamp)
            {
                try
                {
                    _affine = CalibrationStore.Load(path).ToAffine();
                    _path = path;
                    _stamp = stamp;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                                              or System.Text.Json.JsonException)
                {
                    _affine = null;
                    error = $"File hiệu chuẩn '{path}' không hợp lệ: {ex.Message}";
                    return false;
                }
            }

            affine = _affine;
            return true;
        }
    }
}
