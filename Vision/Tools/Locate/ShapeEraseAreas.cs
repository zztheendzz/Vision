using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Vision.Core;

namespace Vision.Tools.Locate
{
    /// <summary>
    /// Danh sách vùng "xóa viền" khi học mẫu: những chỗ trong ROI mà mẫu KHÔNG được lấy đường viền (nhiễu, bóng, chữ in, vết xước...).
    /// Lưu thành chữ trong tham số EraseAreas, mỗi vùng cách nhau dấu ';':
    ///   R row col length1 length2 phiDeg     chữ nhật xoay (length1, length2 là nửa chiều; phi theo độ, ngược chiều kim đồng hồ trên ảnh)
    ///   C row col radius                     hình tròn
    /// Tọa độ là pixel của ảnh. Ví dụ:  R 240.5 320 20 10 30; C 100 100 15
    /// Mẫu được tạo từ vùng (ROI trừ các vùng xóa), nên đường viền nằm trong vùng xóa không vào mẫu.
    /// </summary>
    public static class ShapeEraseAreas
    {
        public readonly record struct Area(bool IsCircle, double Row, double Column, double A, double B, double PhiDeg);

        /// <summary>Đọc chữ thành danh sách vùng. Sai cú pháp thì trả null và ghi lý do vào error.</summary>
        public static List<Area>? Parse(string? text, out string? error)
        {
            error = null;
            var list = new List<Area>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            foreach (string raw in text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string item = raw.Trim();
                if (item.Length == 0) continue;

                var parts = item.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                bool circle = parts[0].Equals("C", StringComparison.OrdinalIgnoreCase);
                bool rect = parts[0].Equals("R", StringComparison.OrdinalIgnoreCase);
                int need = circle ? 3 : 5;
                if ((!circle && !rect) || parts.Length != need + 1)
                {
                    error = $"Vùng xóa '{item}' sai cú pháp. Dùng 'R row col length1 length2 phi' hoặc 'C row col radius', cách nhau dấu ';'.";
                    return null;
                }

                var v = new double[need];
                for (int i = 0; i < need; i++)
                {
                    if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !double.IsFinite(v[i]))
                    {
                        error = $"Vùng xóa '{item}': '{parts[i + 1]}' không phải số.";
                        return null;
                    }
                }

                if (circle)
                {
                    if (v[2] <= 0) { error = $"Vùng xóa '{item}': bán kính phải lớn hơn 0."; return null; }
                    list.Add(new Area(true, v[0], v[1], v[2], 0, 0));
                }
                else
                {
                    if (v[2] <= 0 || v[3] <= 0) { error = $"Vùng xóa '{item}': length1 và length2 phải lớn hơn 0."; return null; }
                    list.Add(new Area(false, v[0], v[1], v[2], v[3], v[4]));
                }
            }
            return list;
        }

        public static string Format(IEnumerable<Area> areas)
        {
            var sb = new StringBuilder();
            foreach (var a in areas)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(a.IsCircle
                    ? string.Create(CultureInfo.InvariantCulture, $"C {a.Row:0.#} {a.Column:0.#} {a.A:0.#}")
                    : string.Create(CultureInfo.InvariantCulture, $"R {a.Row:0.#} {a.Column:0.#} {a.A:0.#} {a.B:0.#} {a.PhiDeg:0.#}"));
            }
            return sb.ToString();
        }

        /// <summary>Thêm một vùng vào chữ hiện có (dùng cho nút "Thêm vùng xóa" trên khung ảnh).</summary>
        public static string Append(string? existing, Area area)
        {
            var list = Parse(existing, out _) ?? new List<Area>();
            list.Add(area);
            return Format(list);
        }

        /// <summary>Vùng học mẫu = ROI trừ các vùng xóa. Người gọi Dispose kết quả.</summary>
        public static HRegion BuildTrainRegion(VisionToolInput input, HRegion? external = null)
        {
            // Có Region nối vào: dùng Region đó làm vùng học mẫu; không thì dùng ROI kéo trên ảnh
            var roi = external != null ? external.CopyObj(1, -1) : RoiRegionFactory.Create(input);
            var areas = Parse(input.Get<string>("EraseAreas"), out _);
            if (areas == null || areas.Count == 0) return roi;

            HRegion? erase = null;
            try
            {
                foreach (var a in areas)
                {
                    using var one = new HRegion();
                    if (a.IsCircle) one.GenCircle(a.Row, a.Column, a.A);
                    else one.GenRectangle2(a.Row, a.Column, a.PhiDeg * Math.PI / 180.0, a.A, a.B);
                    var merged = erase == null ? one.CopyObj(1, -1) : erase.Union2(one);
                    erase?.Dispose();
                    erase = merged;
                }
                var result = roi.Difference(erase!);
                roi.Dispose();
                return result;
            }
            finally
            {
                erase?.Dispose();
            }
        }
    }
}
