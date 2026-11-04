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

        private HShapeModel? _model;
        private string? _loadedPath;
        private DateTime _loadedStamp;
        private Pose2D? _reference;

        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Image", typeof(HImage))
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
                new ParamDef("ModelPath",   typeof(string), "model.shm", Description: "File mẫu (ghi khi Học mẫu, đọc khi tìm)"),
                new ParamDef("MinScore",    typeof(double), 0.7, Min: 0.05, Max: 1.0, Description: "Điểm khớp tối thiểu để coi là thấy vật"),
                new ParamDef("AngleStart",  typeof(double), -180.0, Min: -360, Max: 360, Description: "Góc xoay nhỏ nhất cần tìm (độ)"),
                new ParamDef("AngleExtent", typeof(double), 360.0,  Min: 1, Max: 360, Description: "Bề rộng khoảng góc cần tìm (độ)"),
                new ParamDef("Greediness",  typeof(double), 0.9, Min: 0, Max: 1, Description: "0 = kỹ nhưng chậm, 1 = nhanh nhưng có thể sót")
            })
            .ToArray();

        protected override void ValidateRules(VisionToolInput p, List<string> errors)
            => RoiRegionFactory.Validate(p, errors);

        // Mẫu được nạp lười trong Execute (xem ghi chú ở trên), nên Initialize không làm gì
        protected override void OnInitialize(ToolContext context) { }

        protected override void OnRelease() => ReleaseCache();

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            string path = input.Get<string>("ModelPath");
            if (string.IsNullOrWhiteSpace(path))
                return VisionToolResult.ConfigError("Chưa đặt ModelPath");

            var image = input.Get<HImage>("Image");                   // chỉ mượn

            // Mẫu hình dạng chỉ làm việc với ảnh 1 kênh. Ảnh màu phải qua RgbToGray trước
            int channels = image.CountChannels();
            if (channels != 1)
                return VisionToolResult.ConfigError(
                    $"FindShape cần ảnh 1 kênh (xám), ảnh nhận vào có {channels} kênh. Thêm tool RgbToGray phía trước.");

            double startRad = input.Get<double>("AngleStart") * Math.PI / 180.0;
            double extentRad = input.Get<double>("AngleExtent") * Math.PI / 180.0;
            double greediness = input.Get<double>("Greediness");

            return context.Mode == RunMode.Teach
                ? Teach(input, image, path, startRad, extentRad, greediness, context)
                : Find(input, image, path, startRad, extentRad, greediness, context);
        }

        // ================= HỌC MẪU =================

        private VisionToolResult Teach(
            VisionToolInput input, HImage image, string path,
            double startRad, double extentRad, double greediness, ToolContext context)
        {
            using var roi = RoiRegionFactory.Create(input);
            using var reduced = image.ReduceDomain(roi);

            // ROI mặc định (hàng 240, cột 320) có thể không nằm trên vật của bạn: báo rõ thay vì để Halcon báo lỗi khó hiểu
            using (var domain = reduced.GetDomain())
            {
                double area = domain.CountObj() == 0 ? 0 : domain.AreaCenter(out double _, out double _);
                if (area < 100)
                    return VisionToolResult.ConfigError(
                        $"ROI học mẫu nằm ngoài ảnh hoặc quá nhỏ (diện tích {area:F0} pixel). " +
                        "Chọn node FindShape, kéo ROI lên vùng vật trên 'Ảnh gốc' rồi học mẫu lại.");
            }

            HShapeModel model = reduced.CreateShapeModel(
                "auto", startRad, extentRad, "auto", "auto", "use_polarity", "auto", "auto");

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

            // Thay mẫu trong bộ nhớ bằng mẫu vừa học
            ReleaseCache();
            _model = model;
            _loadedPath = path;
            _loadedStamp = File.GetLastWriteTimeUtc(path);
            _reference = reference;

            var result = VisionToolResult.Ok($"Đã học mẫu, lưu vào {path}")
                .Add("Pose", reference)
                .Add("Reference", reference)
                .Add("Score", score);
            result.Values["Reference"] = reference.ToString();
            if (context.NeedDisplay) result.AddDisplay(ContourAt(model, reference));
            return result;
        }

        // ================= TÌM =================

        private VisionToolResult Find(
            VisionToolInput input, HImage image, string path,
            double startRad, double extentRad, double greediness, ToolContext context)
        {
            if (!EnsureLoaded(path, out string? error))
                return VisionToolResult.ConfigError(error!);

            double minScore = input.Get<double>("MinScore");
            var pose = FindOne(_model!, image, startRad, extentRad, minScore, greediness, out double score);
            if (pose == null)
                return VisionToolResult.NoResult(DescribeMiss(_model!, image, startRad, extentRad, greediness, minScore));

            var result = VisionToolResult.Ok($"Điểm {score:F2}, góc {pose.Value.AngleDeg:F2}°")
                .Add("Pose", pose.Value)
                .Add("Score", score);
            if (_reference is { } reference) result.Add("Reference", reference);

            result.Values["Row"] = pose.Value.Row.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            result.Values["Column"] = pose.Value.Column.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            result.Values["Angle"] = pose.Value.AngleDeg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            result.Values["Score"] = score.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

            if (context.NeedDisplay) result.AddDisplay(ContourAt(_model!, pose.Value));
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

        private bool EnsureLoaded(string path, out string? error)
        {
            error = null;
            if (!File.Exists(path))
            {
                error = $"Chưa học mẫu: không thấy file '{path}'. Hãy bấm 'Học mẫu' trước.";
                return false;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_model != null && _loadedPath == path && _loadedStamp == stamp) return true;

            ReleaseCache();                                           // đổi file hoặc file mới được ghi: nạp lại
            try
            {
                _model = new HShapeModel(path);
            }
            catch (HalconException ex)
            {
                error = $"Không đọc được file mẫu '{path}': {ex.Message}";
                return false;
            }

            _loadedPath = path;
            _loadedStamp = stamp;
            _reference = ReadReference(path);
            return true;
        }

        private void ReleaseCache()
        {
            _model?.Dispose();
            _model = null;
            _loadedPath = null;
            _reference = null;
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
