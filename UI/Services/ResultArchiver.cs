using HalconDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Vision.Execution;
using Vision.Graph;
using Vision.Runtime;

namespace UI.Services
{
    /// <summary>
    /// Lưu ảnh và kết quả của mỗi lần chạy vào ổ đĩa (mặc định D:\VisionData). Mỗi ngày một thư mục:
    ///   D:\VisionData\2026-11-05\091530_123_OK_7_Camera 1_original.png   ảnh camera đưa vào
    ///   D:\VisionData\2026-11-05\091530_123_OK_7_processed.png            ảnh của node cuối có ảnh
    ///   D:\VisionData\2026-11-05\091530_123_OK_7_result.png               ảnh trên có vẽ viền / vùng kết quả và nhãn OK / NG
    ///   D:\VisionData\2026-11-05\091530_123_OK_7.json                     kết luận, giá trị, dòng từng node
    ///   D:\VisionData\2026-11-05\results.csv                              mỗi lần chạy một dòng
    /// Gọi từ thread nền được (chỉ đọc ảnh, ghi file). Lỗi (ví dụ ổ D: không có) được ném ra để nơi gọi báo người dùng.
    /// </summary>
    public sealed class ResultArchiver
    {
        private static readonly object FileLock = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // giữ tiếng Việt đọc được
        };

        public string BaseFolder { get; }

        public ResultArchiver(string baseFolder) => BaseFolder = baseFolder;

        /// <summary>Trả về thư mục đã lưu vào.</summary>
        public string Save(CycleRecord record, VisionGraph graph, GraphRunResult run,
                           IReadOnlyDictionary<string, HImage> sources)
        {
            if (string.IsNullOrWhiteSpace(BaseFolder)) throw new InvalidOperationException("Chưa đặt thư mục lưu.");

            string day = Path.Combine(BaseFolder, record.StartedAt.ToString("yyyy-MM-dd"));
            string stem = $"{record.StartedAt:HHmmss_fff}_{record.Verdict.ToString().ToUpperInvariant()}_{record.Id}";
            var files = new List<string>();

            lock (FileLock)
            {
                Directory.CreateDirectory(day);

                // Ảnh camera đưa vào
                foreach (var (name, image) in sources)
                {
                    if (!image.IsInitialized()) continue;
                    string file = $"{stem}_{Safe(name)}_original.png";
                    image.WriteImage("png", 0, Path.Combine(day, file));
                    files.Add(file);
                }

                // Ảnh của node cuối cùng (theo thứ tự chạy) thành công và có ảnh
                HImage? processed = null;
                foreach (var node in graph.GetTopologicalOrder())
                {
                    if (run.Nodes.TryGetValue(node.Id, out var nr) && nr.Result is { Success: true } r)
                        processed = r.All.Values.OfType<HImage>().FirstOrDefault() ?? processed;
                }
                if (processed != null && processed.IsInitialized())
                {
                    string file = $"{stem}_processed.png";
                    processed.WriteImage("png", 0, Path.Combine(day, file));
                    files.Add(file);
                }

                // Ảnh có vẽ chồng kết quả (vùng, viền, đường nét của các node) và nhãn kết luận
                HImage? background = processed ?? sources.Values.FirstOrDefault();
                if (background != null && background.IsInitialized())
                {
                    try
                    {
                        using HImage overlay = RenderOverlay(graph, run, background, record.Verdict);
                        string file = $"{stem}_result.png";
                        overlay.WriteImage("png", 0, Path.Combine(day, file));
                        files.Add(file);
                    }
                    catch (HalconException)
                    {
                        // Không vẽ được (ví dụ máy không tạo được cửa sổ đệm): bỏ qua ảnh này, vẫn lưu phần còn lại
                    }
                }

                // Kết quả dạng JSON
                string jsonName = stem + ".json";
                var doc = new
                {
                    time = record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    id = record.Id,
                    mode = record.Mode.ToString(),
                    verdict = record.Verdict.ToString(),
                    message = record.Message,
                    durationMs = record.DurationMs,
                    productCode = record.ProductCode,
                    values = record.Values,
                    nodes = record.NodeLines,
                    files
                };
                File.WriteAllText(Path.Combine(day, jsonName), JsonSerializer.Serialize(doc, JsonOptions), new UTF8Encoding(true));

                // Một dòng tóm tắt vào results.csv của ngày
                string csv = Path.Combine(day, "results.csv");
                bool isNew = !File.Exists(csv);
                using var w = new StreamWriter(csv, append: true, new UTF8Encoding(true));
                if (isNew) w.WriteLine("Time,Id,Mode,Verdict,DurationMs,Message,Values,Files");
                string values = string.Join("; ", record.Values.Select(kv => $"{kv.Key}={kv.Value}"));
                w.WriteLine(string.Join(",",
                    Csv(record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss.fff")), record.Id, record.Mode,
                    record.Verdict, record.DurationMs, Csv(record.Message), Csv(values), Csv(string.Join("; ", files.Append(jsonName)))));
            }
            return day;
        }

        private static readonly string[] OverlayColors = { "green", "red", "yellow", "cyan", "magenta", "orange" };

        /// <summary>Vẽ ảnh nền + đối tượng hiển thị của các node thành công, qua cửa sổ Halcon dạng đệm (không hiện lên màn hình).</summary>
        private static HImage RenderOverlay(VisionGraph graph, GraphRunResult run, HImage background, RunVerdict verdict)
        {
            background.GetImageSize(out int width, out int height);
            using var window = new HWindow(0, 0, width, height, new HTuple(0), "buffer", "");
            window.SetPart(0, 0, height - 1, width - 1);
            window.DispObj(background);

            window.SetDraw("margin");                    // vùng chỉ vẽ viền để thấy ảnh bên dưới
            window.SetLineWidth(2);
            int i = 0;
            foreach (var node in graph.GetTopologicalOrder())
            {
                if (!run.Nodes.TryGetValue(node.Id, out var nr) || nr.Result is not { Success: true } r) continue;
                foreach (var obj in r.DisplayObjects)
                {
                    if (obj is HImage || !obj.IsInitialized()) continue;
                    window.SetColor(OverlayColors[i++ % OverlayColors.Length]);
                    window.DispObj(obj);
                }
            }

            string color = verdict switch { RunVerdict.Ok => "green", RunVerdict.Ng => "red", _ => "orange" };
            window.DispText(verdict.ToString().ToUpperInvariant(), "window", 12, 12, color,
                            new HTuple("box"), new HTuple("true"));
            return window.DumpWindowImage();
        }

        private static string Csv(string text) => "\"" + (text ?? "").Replace("\"", "\"\"") + "\"";

        private static string Safe(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 40 ? name[..40] : name;
        }
    }
}
