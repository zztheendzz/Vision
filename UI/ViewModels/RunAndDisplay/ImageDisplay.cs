using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels.RunAndDisplay
{
    /// <summary>
    /// Thứ cần vẽ lên một khung xem ảnh: ảnh nền + các đối tượng vẽ chồng (vùng, đường viền...).
    /// KHÔNG sở hữu gì: ảnh và đối tượng thuộc về kết quả chạy (GraphRunResult) do RunViewModel giữ.
    /// Chỉ vẽ trong lúc kết quả đó còn sống; trước khi RunViewModel giải phóng kết quả, nó đặt lại về Empty.
    /// </summary>
    public sealed record ImageDisplay(HImage? Image, IReadOnlyList<HObject> Overlays, string Caption)
    {
        public static readonly ImageDisplay Empty = new(null, Array.Empty<HObject>(), "");
    }
}
