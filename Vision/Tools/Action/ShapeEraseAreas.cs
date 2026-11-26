using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Vision.Core;
using Vision.Tools.Locate;

namespace Vision.Tools.Action
{
    /// <summary>
    /// Danh sách vùng "xóa viền" khi học mẫu: những chỗ trong ROI mà mẫu KHÔNG được lấy đường viền (nhiễu, bóng, chữ in, vết xước...).
    /// Lưu thành chữ trong tham số EraseAreas, mỗi vùng cách nhau dấu ';':
    ///   R row col length1 length2 phiDeg     chữ nhật xoay (length1, length2 là nửa chiều; phi theo độ, ngược chiều kim đồng hồ trên ảnh)
    ///   C row col radius                     hình tròn
    ///   P radius r1 c1 r2 c2 ...             nét tẩy: hợp các hình tròn bán kính 'radius' tâm tại từng điểm (tẩy theo con trỏ)
    /// Tọa độ là pixel của ảnh. Ví dụ:  R 240.5 320 20 10 30; C 100 100 15
    /// Mẫu được tạo từ vùng (ROI trừ các vùng xóa), nên đường viền nằm trong vùng xóa không vào mẫu.
    /// </summary>
    public static class ShapeEraseAreas
    {
        public readonly record struct Area(bool IsCircle, double Row, double Column, double A, double B, double PhiDeg, double[]? Points = null)
        {
            /// <summary>Nét tẩy theo con trỏ: A là bán kính, Points là r1,c1,r2,c2...</summary>
            public bool IsPath => Points != null;
        }

        /// <summary>Dựng vùng ảnh của một vùng xóa. Người gọi Dispose.</summary>
        public static HRegion MakeRegion(Area a)
        {
            if (a.Points is { Length: >= 2 } pts)
            {
                int n = pts.Length / 2;
                var rows = new double[n];
                var cols = new double[n];
                for (int i = 0; i < n; i++) { rows[i] = pts[2 * i]; cols[i] = pts[2 * i + 1]; }
                using var dots = new HRegion();
                dots.GenRegionPoints(new HTuple(rows), new HTuple(cols));
                return dots.DilationCircle(Math.Max(0.5, a.A));
            }
            var region = new HRegion();
            if (a.IsCircle) region.GenCircle(a.Row, a.Column, a.A);
            else region.GenRectangle2(a.Row, a.Column, a.PhiDeg * Math.PI / 180.0, a.A, a.B);
            return region;
        }

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

                if (parts[0].Equals("P", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 4 || (parts.Length - 2) % 2 != 0)
                    {
                        error = $"Nét tẩy '{Shorten(item)}' sai cú pháp. Dùng 'P radius r1 c1 r2 c2 ...'.";
                        return null;
                    }
                    var nums = new double[parts.Length - 1];
                    for (int i = 0; i < nums.Length; i++)
                    {
                        if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out nums[i]) || !double.IsFinite(nums[i]))
                        {
                            error = $"Nét tẩy '{Shorten(item)}': '{parts[i + 1]}' không phải số.";
                            return null;
                        }
                    }
                    if (nums[0] <= 0) { error = $"Nét tẩy '{Shorten(item)}': bán kính phải lớn hơn 0."; return null; }
                    list.Add(new Area(false, nums[1], nums[2], nums[0], 0, 0, nums.Skip(1).ToArray()));
                    continue;
                }

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

        private static string Shorten(string s) => s.Length <= 40 ? s : s.Substring(0, 40) + "...";

        public static string Format(IEnumerable<Area> areas)
        {
            var sb = new StringBuilder();
            foreach (var a in areas)
            {
                if (sb.Length > 0) sb.Append("; ");
                if (a.Points != null)
                {
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $"P {a.A:0.#}"));
                    foreach (double v in a.Points) sb.Append(' ').Append(v.ToString("0.#", CultureInfo.InvariantCulture));
                    continue;
                }
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
            // Hai nguồn vùng xóa độc lập: EraseAreas (nhập / thêm trên ảnh gốc) và SketchErase (vẽ tay lúc Xem mẫu)
            var areas = Parse(input.Get<string>("EraseAreas"), out _) ?? new List<Area>();
            if (input.Has("SketchErase"))
                areas.AddRange(Parse(input.Get<string>("SketchErase"), out _) ?? new List<Area>());
            if (areas.Count == 0) return roi;

            HRegion? erase = null;
            try
            {
                foreach (var a in areas)
                {
                    using var one = MakeRegion(a);
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
