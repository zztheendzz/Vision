using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Execution;
using Vision.Graph;

namespace Vision.Runtime
{
    /// <summary>
    /// Đổi kết quả chạy đồ thị thành CycleRecord (kết luận Ok / Ng / Error + giá trị + dòng từng node).
    /// Dùng chung cho Runner và nơi chạy trên giao diện, để cùng một luật kết luận.
    /// </summary>
    public static class CycleRecordBuilder
    {
        public static CycleRecord Build(
            VisionGraph graph, GraphRunResult run, long id, DateTime startedAt, long durationMs,
            RunMode mode, string? productCode = null, string? extraMessage = null)
        {
            var (verdict, message) = Evaluate(graph, run);
            if (!string.IsNullOrEmpty(extraMessage)) message = $"{message} | {extraMessage}";

            return new CycleRecord(id, startedAt, durationMs, verdict, message, productCode, mode,
                                   CollectValues(graph, run), BuildNodeLines(graph, run));
        }

        public static (RunVerdict Verdict, string Message) Evaluate(VisionGraph graph, GraphRunResult run)
        {
            string? noResultMessage = null;

            foreach (var node in graph.Nodes)
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

        /// <summary>Gom các giá trị mà node ghi vào Values, thêm tên tool phía trước để không trùng nhau.</summary>
        public static IReadOnlyDictionary<string, string> CollectValues(VisionGraph graph, GraphRunResult? run)
        {
            var values = new Dictionary<string, string>();
            if (run == null) return values;

            foreach (var node in graph.GetTopologicalOrder())
            {
                var result = run[node].Result;
                if (result == null) continue;

                foreach (var pair in result.Values)
                    values[$"{node.ToolName}.{pair.Key}"] = pair.Value;
            }
            return values;
        }

        public static IReadOnlyList<string> BuildNodeLines(VisionGraph graph, GraphRunResult? run)
        {
            if (run == null) return Array.Empty<string>();

            return graph.Nodes
                .Select(node =>
                {
                    var nodeRun = run[node];
                    string text = $"{node.ToolName}: {nodeRun.Status} ({nodeRun.ElapsedMs} ms)";
                    return string.IsNullOrEmpty(nodeRun.Message) ? text : $"{text} - {nodeRun.Message}";
                })
                .ToList();
        }
    }
}
