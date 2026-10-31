using System.Diagnostics;
using Vision.Core;
using Vision.Graph;


namespace Vision.Execution
{

        /// <summary>Kết quả 1 lượt chạy cả graph. Dispose = giải phóng toàn bộ kết quả các node.</summary>
        public class GraphExecutor
        {
            /// <summary>Bắn sau khi mỗi node xong. Có thể đến từ thread nền: UI phải qua Dispatcher.</summary>
            public event Action<VisionNode, NodeRun>? NodeFinished;

            public Task<GraphRunResult> RunAsync(
                VisionGraph graph, RunOptions? options = null, CancellationToken ct = default)
                => Task.Run(() => Run(graph, options, ct), ct);

            public GraphRunResult Run(
                VisionGraph graph, RunOptions? options = null, CancellationToken ct = default)
            {
                options ??= new RunOptions();
                var order = graph.GetTopologicalOrder();

                // Chụp tham số hiệu lực ngay đầu lượt: UI sửa node giữa chừng không ảnh hưởng lượt đang chạy
                var effective = BuildEffectiveParameters(graph, options);

                var run = new GraphRunResult();
                try
                {
                    foreach (var node in order)
                    {
                        ct.ThrowIfCancellationRequested();

                        var nr = new NodeRun();
                        run.Nodes[node.Id] = nr;

                        // Seed < tham số (đã gộp ghi đè) < dây nối
                        var input = new VisionToolInput();
                        foreach (var kv in options.Seed.All) input.Add<object?>(kv.Key, kv.Value);
                        foreach (var kv in effective[node.Id]) input.Add<object?>(kv.Key, kv.Value);

                        string? skip = null;
                        foreach (var port in node.Inputs)
                        {
                            if (port.Connection is not { } c) continue;   // chưa nối: tool tự báo thiếu nếu Required

                            var up = run.Nodes[c.From.Owner.Id];
                            if (up.Status != NodeStatus.Ok || up.Result == null)
                            { skip = $"Node '{c.From.Owner.ToolName}' phía trước không thành công"; break; }

                            if (!up.Result.All.TryGetValue(c.From.Name, out var val))
                            { skip = $"Node '{c.From.Owner.ToolName}' không trả '{c.From.Name}'"; break; }

                            input.Add<object?>(port.Name, val);
                        }

                        if (skip != null)
                        {
                            nr.Status = NodeStatus.Skipped;
                            nr.Message = skip;
                        }
                        else
                        {
                            var sw = Stopwatch.StartNew();

                        // đang cần xem xét toolcontext: có thể cần truyền logger, mode, cancellation token, v.v. vào tool.Run
                            var result = node.Tool.Run(input,new ToolContext());

                            sw.Stop();

                            nr.Result = result;
                            nr.ElapsedMs = sw.ElapsedMilliseconds;
                            nr.Status = result.Success ? NodeStatus.Ok : NodeStatus.Failed;
                            nr.Message = result.Message;
                        }

                        NodeFinished?.Invoke(node, nr);
                    }
                }
                catch
                {
                    run.Dispose();      // bị hủy hoặc lỗi bất ngờ: không để rò rỉ kết quả đã có
                    throw;
                }
                return run;
            }

            private static Dictionary<Guid, Dictionary<string, object?>> BuildEffectiveParameters(
                VisionGraph graph, RunOptions options)
            {
                var eff = new Dictionary<Guid, Dictionary<string, object?>>();
                foreach (var n in graph.Nodes)
                    eff[n.Id] = new Dictionary<string, object?>(n.Parameters);

                foreach (var (nodeId, overrides) in options.Overrides)
                {
                    if (!eff.TryGetValue(nodeId, out var dict))
                        throw new ArgumentException($"RunOptions tham chiếu node {nodeId} không thuộc graph này");
                    foreach (var (name, value) in overrides)
                        dict[name] = value;
                }
                return eff;
            }
        }
    
}
