using HalconDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Vision.Core;
using Vision.Geometry;
using Vision.Tools.Abstractions;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Locate
{
    /// <summary>Cách so cực tính (sáng/tối) giữa mẫu và vật khi tìm.</summary>
    public enum ShapeMetric
    {
        /// <summary>Vật phải có cùng chiều sáng/tối như mẫu.</summary>
        UsePolarity,
        /// <summary>Chấp nhận vật bị đảo toàn bộ sáng/tối so với mẫu.</summary>
        IgnoreGlobalPolarity,
        /// <summary>Chấp nhận đảo cực tính cục bộ (nền thay đổi sáng tối từng chỗ). Chậm hơn.</summary>
        IgnoreLocalPolarity
    }

    /// <summary>
    /// Tìm vật theo mẫu hình dạng (Halcon shape-based matching) và trả tư thế so với ảnh mẫu.
    ///
    /// HỌC MẪU (RunMode.Teach): đặt vật đúng vị trí mẫu, kéo ROI bao quanh vật trên ảnh gốc rồi bấm "Học mẫu".
    ///   Tool tạo mẫu từ vùng ROI của chính nó, tự tìm lại mẫu trên ảnh đó để lấy tư thế tham chiếu,
    ///   rồi ghi ModelPath (mẫu) và ModelPath + ".ref.json" (tư thế tham chiếu).
    /// TÌM (DryRun/Live): tìm mẫu trong TOÀN BỘ miền ảnh nhận vào (muốn giới hạn vùng tìm thì đặt SelectByRoi phía trước).
    ///   Pose.AngleDeg chính là góc xoay so với ảnh mẫu (0 = đúng hướng mẫu).
    ///
    /// Ghi chú thiết kế: Initialize không đọc được tham số của node, nên mẫu được nạp "lười" ở lần chạy đầu và
    /// nạp lại khi đổi ModelPath hoặc file bị ghi lại. Ghi chú: chu kỳ đầu tiên sau khi mở chương trình sẽ chậm hơn một chút.
    /// Không thấy vật (điểm thấp hơn MinScore) là NoResult, tức NG, không phải lỗi.
    /// </summary>
    [VisionTool("FindShape", "Find Shape", Group = "Định vị",
                Description = "Tìm vật theo mẫu, trả vị trí và góc xoay so với ảnh mẫu")]
    public class FindShapeTool : StatefulToolBase, IRoiEditable
    {
        private sealed record ReferenceFile(double Row, double Column, double AngleDeg);

        // Mỗi FOV một file mẫu: giữ sẵn trong bộ nhớ để đổi FOV giữa các lượt không phải nạp lại từ đĩa
        private sealed class Loaded
        {
            public HShapeModel Model = null!;
            public DateTime Stamp;
            public Pose2D? Reference;
        }

        private readonly Dictionary<string, Loaded> _loaded = new(StringComparer.OrdinalIgnoreCase);

        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Image",  typeof(HImage)),
            // Tùy chọn: nối Region (ví dụ từ SelectByRoi / Blob) thì chỉ học và chỉ tìm trong vùng đó; không nối thì học bằng ROI của tool, tìm toàn ảnh
            new PortDef("Region", typeof(HRegion), Required: false)
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Pose",      typeof(Pose2D)),
            new PortDef("Reference", typeof(Pose2D)),
            new PortDef("Score",     typeof(double))
        };

        // ROI chuẩn (vùng học mẫu) + tham số riêng của tool
        public override IReadOnlyList<ParamDef> Parameters { get; } = RoiRegionFactory.ParamDefs()
            .Concat(new[]
            {
                new ParamDef("ModelPath",   typeof(string), "model.shm", Description: "File mẫu (ghi khi Học mẫu, đọc khi tìm). Nhiều vị trí chụp: dùng {FOV}, vd models/fov{FOV}.shm; FOV 1 dùng fov1.shm, FOV 2 dùng fov2.shm..."),
                new ParamDef("MinScore",    typeof(double), 0.7, Min: 0.05, Max: 1.0, Description: "Điểm khớp tối thiểu để coi là thấy vật"),
                new ParamDef("AngleStart",  typeof(double), -180.0, Min: -360, Max: 360, Description: "Góc xoay nhỏ nhất cần tìm (độ)"),
                new ParamDef("AngleExtent", typeof(double), 360.0,  Min: 1, Max: 360, Description: "Bề rộng khoảng góc cần tìm (độ)"),
                new ParamDef("Greediness",  typeof(double), 0.9, Min: 0, Max: 1, Description: "0 = kỹ nhưng chậm, 1 = nhanh nhưng có thể sót"),

                // ----- Tinh chỉnh mẫu khi Học mẫu (xem trước bằng nút "Xem mẫu" rồi mới lưu) -----
                new ParamDef("EraseAreas",  typeof(string), "", Description: "Vùng xóa viền: những chỗ không lấy đường viền vào mẫu. Thêm bằng nút 'Thêm vùng xóa' trên ảnh gốc, hoặc gõ: R row col length1 length2 phi; C row col radius"),
                new ParamDef("ContrastLow",  typeof(double), 0.0, Min: 0, Max: 255, Description: "Ngưỡng tương phản thấp của viền (0 = tự động). Cùng với ContrastHigh: viền yếu hơn ngưỡng thấp bị bỏ"),
                new ParamDef("ContrastHigh", typeof(double), 0.0, Min: 0, Max: 255, Description: "Ngưỡng tương phản cao (0 = tự động). Viền phải có chỗ mạnh hơn ngưỡng này mới được giữ. Tăng lên để bỏ viền nhiễu"),
                new ParamDef("MinContourLength", typeof(double), 0.0, Min: 0, Description: "Chiều dài tối thiểu của một đường viền (pixel). Chỉ dùng khi đặt ContrastHigh. Tăng lên để bỏ các đoạn viền ngắn"),
                new ParamDef("MinContrast", typeof(double), 0.0, Min: 0, Max: 255, Description: "Tương phản tối thiểu để một điểm ảnh được coi là viền khi tìm (0 = tự động). Đặt cao hơn nhiễu của ảnh. Đổi tham số này phải Học mẫu lại"),
                new ParamDef("NumLevels",   typeof(int), 0, Min: 0, Max: 10, Description: "Số tầng kim tự tháp (0 = tự động). Vật nhỏ thì giảm; tìm chậm thì tăng"),
                new ParamDef("Metric",      typeof(ShapeMetric), ShapeMetric.UsePolarity, Description: "Cách so cực tính sáng/tối: UsePolarity = vật phải cùng sáng/tối như mẫu, Ignore... = bỏ qua đảo cực tính"),
                new ParamDef("OriginRow",    typeof(double), -1.0, Min: -1, Description: "Hàng (pixel, trên ảnh học) của điểm gốc mẫu = tâm thật của vật. -1 = để mặc định (trọng tâm vùng học = ROI trừ vùng xóa). Đặt cùng OriginColumn"),
                new ParamDef("OriginColumn", typeof(double), -1.0, Min: -1, Description: "Cột (pixel, trên ảnh học) của điểm gốc mẫu. -1 = trọng tâm vùng học. Pose trả về chính là vị trí điểm gốc này"),
                new ParamDef("PreviewOnly", typeof(bool), false, Description: "Chỉ xem trước mẫu khi Học mẫu: tạo mẫu và vẽ viền nhưng KHÔNG ghi file. Nút 'Xem mẫu' tự bật tham số này")
            })
            .ToArray();

        protected override void ValidateRules(VisionToolInput p, List<string> errors)
        {
            RoiRegionFactory.Validate(p, errors);

            if (ShapeEraseAreas.Parse(p.Get<string>("EraseAreas"), out string? eraseError) == null)
                errors.Add(eraseError!);

            double low = p.Get<double>("ContrastLow"), high = p.Get<double>("ContrastHigh");
            if (high > 0 && (low <= 0 || low > high))
                errors.Add("ContrastLow phải lớn hơn 0 và không lớn hơn ContrastHigh (hoặc để cả hai bằng 0 cho tự động)");
            if (high <= 0 && low > 0)
                errors.Add("Đã đặt ContrastLow thì phải đặt cả ContrastHigh");
        }

        // Mẫu được nạp lười trong Execute (xem ghi chú ở trên), nên Initialize không làm gì
        protected override void OnInitialize(ToolContext context) { }

        protected override void OnRelease() => ReleaseCache();

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            string template = input.Get<string>("ModelPath");
            if (string.IsNullOrWhiteSpace(template))
                return VisionToolResult.ConfigError("Chưa đặt ModelPath");
            if (!context.TryResolvePath(template, out string path, out string? pathError))
                return VisionToolResult.ConfigError(pathError!);

            var image = input.Get<HImage>("Image");                   // chỉ mượn
            HRegion? region = input.TryGet<HRegion>("Region", out var r) && r != null && r.IsInitialized() ? r : null;

            // Mẫu hình dạng chỉ làm việc với ảnh 1 kênh. Ảnh màu phải qua RgbToGray trước
            int channels = image.CountChannels();
            if (channels != 1)
                return VisionToolResult.ConfigError(
                    $"FindShape cần ảnh 1 kênh (xám), ảnh nhận vào có {channels} kênh. Thêm tool RgbToGray phía trước.");

            double startRad = input.Get<double>("AngleStart") * Math.PI / 180.0;
            double extentRad = input.Get<double>("AngleExtent") * Math.PI / 180.0;
            double greediness = input.Get<double>("Greediness");

            return context.Mode == RunMode.Teach
                ? Teach(input, image, region, path, startRad, extentRad, greediness, context)
                : Find(input, image, region, path, startRad, extentRad, greediness, context);
        }

        // ================= HỌC MẪU =================

        private VisionToolResult Teach(
            VisionToolInput input, HImage image, HRegion? region, string path,
            double startRad, double extentRad, double greediness, ToolContext context)
        {
            using var roi = ShapeEraseAreas.BuildTrainRegion(input, region);   // ROI trừ các vùng xóa viền
            using var reduced = image.ReduceDomain(roi);

            // ROI mặc định (hàng 240, cột 320) có thể không nằm trên vật của bạn: báo rõ thay vì để Halcon báo lỗi khó hiểu
            using (var domain = reduced.GetDomain())
            {
                double area = domain.CountObj() == 0 ? 0 : domain.AreaCenter(out double _, out double _);
                if (area < 100)
                    return VisionToolResult.ConfigError(
                        $"ROI học mẫu nằm ngoài ảnh hoặc quá nhỏ (diện tích {area:F0} pixel). " +
                        "Chọn node FindShape, kéo ROI lên vùng vật trên 'Ảnh gốc' rồi học mẫu lại. (Nếu đã thêm vùng xóa: vùng xóa có thể đã phủ hết ROI.)");
            }

            double cLow = input.Get<double>("ContrastLow"), cHigh = input.Get<double>("ContrastHigh");
            HTuple contrast = cHigh > 0
                ? new HTuple(new[] { cLow, cHigh, Math.Max(1.0, input.Get<double>("MinContourLength")) })
                : new HTuple("auto");
            double minContrast = input.Get<double>("MinContrast");
            int numLevels = input.Get<int>("NumLevels");
            string metric = input.Get<ShapeMetric>("Metric") switch
            {
                ShapeMetric.IgnoreGlobalPolarity => "ignore_global_polarity",
                ShapeMetric.IgnoreLocalPolarity => "ignore_local_polarity",
                _ => "use_polarity"
            };

            HShapeModel model = reduced.CreateShapeModel(
                numLevels > 0 ? new HTuple(numLevels) : new HTuple("auto"),
                startRad, extentRad, "auto", "auto", metric, contrast,
                minContrast > 0 ? new HTuple(minContrast) : new HTuple("auto"));

            // Điểm gốc mẫu mặc định là TRỌNG TÂM vùng học. Người dùng có thể đặt lại về tâm thật của vật
            double originRow = input.Get<double>("OriginRow"), originCol = input.Get<double>("OriginColumn");
            string originNote = "";
            if (originRow >= 0 && originCol >= 0)
            {
                roi.AreaCenter(out double centroidRow, out double centroidCol);
                model.SetShapeModelOrigin(originRow - centroidRow, originCol - centroidCol);
                originNote = $" Gốc mẫu đặt tại ({originRow:F1}, {originCol:F1}), trọng tâm vùng học ({centroidRow:F1}, {centroidCol:F1}).";
            }
            else if (originRow >= 0 || originCol >= 0)
            {
                model.Dispose();
                return VisionToolResult.ConfigError("Phải đặt cả OriginRow và OriginColumn (hoặc cùng để -1).");
            }

            Pose2D reference;
            double score;
            try
            {
                // Tìm lại mẫu trên chính ảnh mẫu: lấy tư thế tham chiếu (tâm mẫu, góc ~0)
                var found = FindOne(model, image, startRad, extentRad, 0.5, greediness, out score);
                if (found == null)
                {
                    model.Dispose();
                    return VisionToolResult.ConfigError(
                        "Học mẫu xong nhưng không tìm lại được mẫu trên ảnh này. ROI cần chứa nhiều cạnh/đặc trưng rõ hơn.");
                }
                reference = found.Value;

                // Chỉ xem trước: vẽ viền, KHÔNG ghi file và KHÔNG đổi mẫu đang dùng
                if (input.Get<bool>("PreviewOnly"))
                {
                    int contours;
                    using (var xld = model.GetShapeModelContours(1)) contours = xld.CountObj();

                    var preview = VisionToolResult.Ok(
                            $"XEM TRƯỚC (chưa lưu): {contours} đường viền, tìm lại được với điểm {score:F2}. Ưng thì bấm Học mẫu để lưu." +
                            $" Gốc mẫu (điểm Pose trả về) ở hàng {reference.Row:F1}, cột {reference.Column:F1}.{originNote}")
                        .Add("Pose", reference)
                        .Add("Reference", reference)
                        .Add("Score", score);
                    preview.Values["Contours"] = contours.ToString();
                    if (context.NeedDisplay) preview.AddDisplay(ContourAt(model, reference));
                    model.Dispose();
                    return preview;
                }

                string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                model.WriteShapeModel(path);
                WriteReference(path, reference);
            }
            catch
            {
                model.Dispose();                                      // chưa giao cho cache thì tự giải phóng
                throw;
            }

            // Thay mẫu trong bộ nhớ bằng mẫu vừa học (chỉ mẫu của file này, các FOV khác giữ nguyên)
            Drop(path);
            _loaded[path] = new Loaded { Model = model, Stamp = File.GetLastWriteTimeUtc(path), Reference = reference };

            var result = VisionToolResult.Ok(context.Fov >= 0 ? $"Đã học mẫu FOV {context.Fov}, lưu vào {path}" : $"Đã học mẫu, lưu vào {path}")
                .Add("Pose", reference)
                .Add("Reference", reference)
                .Add("Score", score);
            result.Values["Reference"] = reference.ToString();
            if (context.NeedDisplay) result.AddDisplay(ContourAt(model, reference));
            return result;
        }

        // ================= TÌM =================

        private VisionToolResult Find(
            VisionToolInput input, HImage image, HRegion? region, string path,
            double startRad, double extentRad, double greediness, ToolContext context)
        {
            if (!EnsureLoaded(path, out Loaded? loaded, out string? error))
                return VisionToolResult.ConfigError(error!);

            double minScore = input.Get<double>("MinScore");

            // Có Region nối vào: chỉ tìm trong vùng đó
            HImage? limited = null;
            try
            {
                var searchImage = image;
                if (region != null)
                {
                    if (region.CountObj() == 0 || region.AreaCenter(out double _, out double _) < 1)
                        return VisionToolResult.NoResult("Region đầu vào rỗng, không có vùng để tìm");
                    limited = image.ReduceDomain(region);
                    searchImage = limited;
                }

                var pose = FindOne(loaded!.Model, searchImage, startRad, extentRad, minScore, greediness, out double score);
                if (pose == null)
                    return VisionToolResult.NoResult(DescribeMiss(loaded!.Model, searchImage, startRad, extentRad, greediness, minScore));
                return BuildFound(loaded!, pose.Value, score, context);
            }
            finally { limited?.Dispose(); }
        }

        private static VisionToolResult BuildFound(Loaded loaded, Pose2D poseValue, double score, ToolContext context)
        {
            var pose = (Pose2D?)poseValue;

            var result = VisionToolResult.Ok($"Điểm {score:F2}, góc {pose.Value.AngleDeg:F2}°")
                .Add("Pose", pose.Value)
                .Add("Score", score);
            if (loaded.Reference is { } reference) result.Add("Reference", reference);

            result.Values["Row"] = pose.Value.Row.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            result.Values["Column"] = pose.Value.Column.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            result.Values["Angle"] = pose.Value.AngleDeg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            result.Values["Score"] = score.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

            if (context.NeedDisplay) result.AddDisplay(ContourAt(loaded.Model, pose.Value));
            return result;
        }

        /// <summary>
        /// Giải thích vì sao không thấy vật: thử lại với ngưỡng rất thấp để biết điểm cao nhất là bao nhiêu.
        /// Điểm gần ngưỡng thì chỉ cần hạ MinScore; không tìm được gì thì ảnh/ROI khác mẫu hoặc góc nằm ngoài khoảng tìm.
        /// </summary>
        private static string DescribeMiss(
            HShapeModel model, HImage image, double startRad, double extentRad, double greediness, double minScore)
        {
            try
            {
                var best = FindOne(model, image, startRad, extentRad, 0.1, greediness, out double bestScore);
                if (best != null)
                    return $"Không thấy vật: điểm cao nhất {bestScore:F2} (tại hàng {best.Value.Row:F0}, cột {best.Value.Column:F0}, " +
                           $"góc {best.Value.AngleDeg:F1}°) thấp hơn MinScore {minScore:F2}";
            }
            catch (HalconException)
            {
                // Bỏ qua: chỉ để chẩn đoán
            }
            return $"Không thấy vật dù hạ ngưỡng xuống 0.10. Ảnh khác mẫu, mẫu nằm ngoài miền ảnh, " +
                   $"hoặc góc nằm ngoài khoảng [{startRad * 180 / Math.PI:F0}°, {(startRad + extentRad) * 180 / Math.PI:F0}°]";
        }

        /// <summary>Tìm một kết quả tốt nhất. null nếu không có kết quả nào đạt điểm tối thiểu.</summary>
        private static Pose2D? FindOne(
            HShapeModel model, HImage image, double startRad, double extentRad,
            double minScore, double greediness, out double score)
        {
            model.FindShapeModel(image, startRad, extentRad, minScore, 1, 0.5, "least_squares", 0, greediness,
                out HTuple rows, out HTuple cols, out HTuple angles, out HTuple scores);

            if (rows.Length == 0)
            {
                score = 0;
                return null;
            }

            score = scores[0].D;
            return new Pose2D(rows[0].D, cols[0].D, Pose2D.NormalizeAngle(angles[0].D * 180.0 / Math.PI));
        }

        /// <summary>Đường viền mẫu đặt đúng tại tư thế tìm được, để vẽ chồng lên ảnh. Người gọi giao cho result.</summary>
        private static HObject ContourAt(HShapeModel model, Pose2D pose)
        {
            using var contours = model.GetShapeModelContours(1);       // tọa độ quanh gốc (0, 0), góc 0
            var transform = new HHomMat2D();
            transform.VectorAngleToRigid(0.0, 0.0, 0.0, pose.Row, pose.Column, pose.AngleDeg * Math.PI / 180.0);
            return transform.AffineTransContourXld(contours);
        }

        // ================= NẠP / LƯU MẪU =================

        private bool EnsureLoaded(string path, out Loaded? loaded, out string? error)
        {
            error = null;
            loaded = null;
            if (!File.Exists(path))
            {
                error = $"Chưa học mẫu: không thấy file '{path}'. Hãy bấm 'Học mẫu' trước" +
                        (path.IndexOf("fov", StringComparison.OrdinalIgnoreCase) >= 0 ? " (đúng FOV này)." : ".");
                return false;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_loaded.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            {
                loaded = cached;
                return true;
            }

            Drop(path);                                               // file mới được ghi: nạp lại
            try
            {
                loaded = new Loaded { Model = new HShapeModel(path), Stamp = stamp, Reference = ReadReference(path) };
            }
            catch (HalconException ex)
            {
                loaded = null;
                error = $"Không đọc được file mẫu '{path}': {ex.Message}";
                return false;
            }

            _loaded[path] = loaded;
            return true;
        }

        private void Drop(string path)
        {
            if (_loaded.Remove(path, out var old)) old.Model.Dispose();
        }

        private void ReleaseCache()
        {
            foreach (var l in _loaded.Values) l.Model.Dispose();
            _loaded.Clear();
        }

        private static string ReferencePath(string modelPath) => modelPath + ".ref.json";

        private static void WriteReference(string modelPath, Pose2D pose)
        {
            string target = ReferencePath(modelPath);
            string temp = target + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new ReferenceFile(pose.Row, pose.Column, pose.AngleDeg)));
            if (File.Exists(target)) File.Replace(temp, target, null);
            else File.Move(temp, target);
        }

        private static Pose2D? ReadReference(string modelPath)
        {
            try
            {
                string file = ReferencePath(modelPath);
                if (!File.Exists(file)) return null;
                var r = JsonSerializer.Deserialize<ReferenceFile>(File.ReadAllText(file));
                if (r == null) return null;
                var pose = new Pose2D(r.Row, r.Column, r.AngleDeg);
                return pose.IsFinite ? pose : null;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return null;                                          // thiếu tư thế tham chiếu: vẫn tìm được, chỉ không có Reference
            }
        }
    }
}
