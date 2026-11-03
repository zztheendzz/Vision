using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Execution;
using Vision.Graph;

namespace UI.Models
{
    /// <summary>Một dòng ở tab "Kết quả": chỉ chứa chữ, an toàn để giữ lâu (không giữ object Halcon).</summary>
    public sealed class NodeResultRowViewModel
    {
        public string NodeName { get; }
        public string StatusText { get; }
        public string ElapsedText { get; }
        public string Details { get; }
        public bool IsOk { get; }

        public NodeResultRowViewModel(VisionNode node, NodeRun run)
        {
            NodeName = node.ToolName;
            ElapsedText = run.Status == NodeStatus.Skipped ? "-" : $"{run.ElapsedMs} ms";
            IsOk = run.Status == NodeStatus.Ok;

            StatusText = run.Status == NodeStatus.Skipped
                ? "Bỏ qua"
                : run.Result?.Status switch
                {
                    ResultStatus.Ok => "OK",
                    ResultStatus.NoResult => "Không có kết quả",
                    ResultStatus.ConfigError => "Lỗi cấu hình",
                    ResultStatus.SystemError => "Lỗi hệ thống",
                    _ => run.Status.ToString()
                };

            // Thông điệp + các giá trị tool ghi ra (vd "Count=3; Area=1520")
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(run.Message)) parts.Add(run.Message);
            if (run.Result is { Values.Count: > 0 } r)
                parts.Add(string.Join("; ", r.Values.Select(v => $"{v.Key}={v.Value}")));
            Details = string.Join("  |  ", parts);
        }
    }
}
