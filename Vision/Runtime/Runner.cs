using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Vision.Core;
using Vision.Execution;
using Vision.Graph;
using Vision.Services;
using Vision.Tools;

namespace Vision.Runtime
{

    /// <summary>
    /// Chạy đồ thị cho từng ảnh và đưa ra kết luận (Ok / Ng / Error) trong một CycleRecord.
    ///
    /// Runner KHÔNG tự lấy ảnh, KHÔNG tự chờ tín hiệu, KHÔNG gửi gì cho robot.
    /// Nơi gọi (giao diện, vòng điều khiển) lấy ảnh rồi gọi RunCycleAsync mỗi khi có tín hiệu.
    ///
    /// Quy ước:
    ///  - Ảnh được đưa vào đồ thị với tên "Image" (mã sản phẩm, nếu có, với tên "ProductCode").
    ///    Đồ thị chạy thật không nên có node LoadImage.
    ///  - Một lúc chỉ chạy một chu kỳ. Không sửa đồ thị khi đang chạy.
    /// </summary>
    public class Runner : IDisposable
    {
        private readonly VisionGraph _graph;
        private readonly IVisionLogger _logger;
        private readonly GraphExecutor _executor;
        private readonly SemaphoreSlim _cycleLock = new(1, 1);

        private bool _ready;
        private long _cycleCounter;

        /// <summary>Mặc định là chạy thử: tool có tác dụng phụ (Action) sẽ không tác động ra ngoài.</summary>
        public RunMode Mode { get; set; } = RunMode.DryRun;

        /// <summary>Có tạo dữ liệu hiển thị cho từng node không. Chạy sản xuất nên tắt cho nhanh.</summary>
        public bool NeedDisplay { get; set; }

        /// <summary>
        /// Báo khi xong một chu kỳ (có thể đến từ thread nền: giao diện phải chuyển về UI thread).
        /// GraphRunResult chỉ dùng được TRONG LÚC xử lý sự kiện: ngay sau đó Runner giải phóng nó.
        /// Cần giữ lại gì thì sao chép ngay trong hàm xử lý. Có thể null nếu lỗi trước khi chạy đồ thị.
        /// </summary>
        public event Action<CycleRecord, GraphRunResult?>? CycleFinished;

        public Runner(VisionGraph graph, IVisionLogger? logger = null)
        {
            _graph = graph;
            _logger = logger ?? NullVisionLogger.Instance;
            _executor = new GraphExecutor(_logger);
        }

        /// <summary>
        /// Chạy đồ thị cho một ảnh. Người gọi giữ ảnh sống đến khi hàm này chạy xong (dùng await), rồi tự Dispose.
        /// Không ném lỗi ra ngoài (trừ khi bị hủy): lỗi được ghi vào CycleRecord với Verdict = Error.
        /// </summary>
        public async Task<CycleRecord> RunCycleAsync(
            HImage image, string? productCode = null, CancellationToken cancellationToken = default)
        {
            await _cycleLock.WaitAsync(cancellationToken);

            try
            {
                var startedAt = DateTime.Now;
                var stopwatch = Stopwatch.StartNew();
                long id = ++_cycleCounter;

                GraphRunResult? run = null;
                RunVerdict verdict = RunVerdict.Error;
                string message = "";

                try
                {
                    EnsureReady();

                    var options = new RunOptions { Mode = Mode, NeedDisplay = NeedDisplay };
                    options.SetSeed("Image", image);
                    if (productCode != null) options.SetSeed("ProductCode", productCode);

                    run = await Task.Run(() => _executor.Run(_graph, options, cancellationToken), cancellationToken);

                    (verdict, message) = Evaluate(run);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    run?.Dispose();
                    throw;                                   // bị hủy: không ghi nhận chu kỳ này
                }
                catch (Exception ex)
                {
                    verdict = RunVerdict.Error;
                    message = $"{ex.GetType().Name}: {ex.Message}";
                    _logger.Error($"Chu kỳ {id} bị lỗi", ex);
                }

                stopwatch.Stop();

                var record = new CycleRecord(
                    id, startedAt, stopwatch.ElapsedMilliseconds, verdict, message,
                    productCode, Mode, CollectValues(run), BuildNodeLines(run));

                _logger.Info($"#{record.Id} {record.Verdict} ({record.DurationMs} ms) {record.Message}");

                Publish(record, run);                        // gọi sự kiện rồi giải phóng run
                return record;
            }
            finally
            {
                _cycleLock.Release();
            }
        }

        /// <summary>Gọi khi đồ thị đã bị sửa (thêm node...) để lần chạy sau khởi tạo lại các tool.</summary>
        public void InvalidateGraph() => _ready = false;

        // ================= KẾT LUẬN =================

        private (RunVerdict Verdict, string Message) Evaluate(GraphRunResult run)
        {
            string? noResultMessage = null;

            foreach (var node in _graph.Nodes)
            {
                var nodeRun = run[node];
                var status = nodeRun.Result?.Status;

                // Lỗi cấu hình hoặc lỗi hệ thống ở bất kỳ node nào thì kết luận ngay là Error
                if (status == ResultStatus.ConfigError || status == ResultStatus.SystemError)
                    return (RunVerdict.Error, $"{node.ToolName}: {nodeRun.Message}");

                // Node chạy tốt nhưng không có kết quả (không thấy vật...): nhớ node đầu tiên
                if (status == ResultStatus.NoResult && noResultMessage == null)
                    noResultMessage = $"{node.ToolName}: {nodeRun.Message}";
            }

            return noResultMessage != null
                ? (RunVerdict.Ng, noResultMessage)
                : (RunVerdict.Ok, "Hoàn tất");
        }

        // ================= HỖ TRỢ =================

        /// <summary>Khởi tạo các tool cần nạp tài nguyên (shape model, hiệu chuẩn...) một lần trước khi chạy.</summary>
        private void EnsureReady()
        {
            if (_ready) return;

            var context = new ToolContext(Mode, CancellationToken.None, _logger, NeedDisplay);
            foreach (var node in _graph.Nodes)
            {
                try
                {
                    node.Tool.Initialize(context);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Node '{node.ToolName}' không khởi tạo được: {ex.Message}", ex);
                }
            }
            _ready = true;
        }

        /// <summary>Gom các giá trị mà node ghi vào Values, thêm tên tool phía trước để không trùng nhau.</summary>
        private IReadOnlyDictionary<string, string> CollectValues(GraphRunResult? run)
        {
            var values = new Dictionary<string, string>();
            if (run == null) return values;

            foreach (var node in _graph.GetTopologicalOrder())
            {
                var result = run[node].Result;
                if (result == null) continue;

                foreach (var pair in result.Values)
                    values[$"{node.ToolName}.{pair.Key}"] = pair.Value;
            }
            return values;
        }

        private IReadOnlyList<string> BuildNodeLines(GraphRunResult? run)
        {
            if (run == null) return Array.Empty<string>();

            return _graph.Nodes
                .Select(node =>
                {
                    var nodeRun = run[node];
                    string text = $"{node.ToolName}: {nodeRun.Status} ({nodeRun.ElapsedMs} ms)";
                    return string.IsNullOrEmpty(nodeRun.Message) ? text : $"{text} - {nodeRun.Message}";
                })
                .ToList();
        }

        private void Publish(CycleRecord record, GraphRunResult? run)
        {
            try
            {
                CycleFinished?.Invoke(record, run);
            }
            catch (Exception ex)
            {
                // Lỗi ở nơi nhận sự kiện không được làm hỏng Runner
                _logger.Error("Lỗi trong xử lý sự kiện CycleFinished", ex);
            }
            finally
            {
                run?.Dispose();
            }
        }

        public void Dispose() => _cycleLock.Dispose();
    }

}
