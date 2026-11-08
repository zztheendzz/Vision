using System.Diagnostics;
using Vision.Core;
using Vision.Graph;
using Vision.Services;


namespace Vision.Execution
{
    public class GraphExecutor
    {
        private readonly IVisionLogger _logger;

        public GraphExecutor(IVisionLogger? logger = null)
            => _logger = logger ?? NullVisionLogger.Instance;

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

            // Một ToolContext chung cho cả lượt: Mode / NeedDisplay lấy từ RunOptions
            var context = new ToolContext(options.Mode, ct, _logger, options.NeedDisplay);

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
                        VisionToolResult result;
                        try
                        {
                            result = node.Tool.Run(input, context);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;                       // hủy: để catch ngoài dọn kết quả
                        }
                        catch (Exception ex)
                        {
                            // Tool đúng quy ước thì không ném; đây là lưới an toàn
                            result = VisionToolResult.SystemError($"[{node.ToolName}] {ex.Message}");
                        }
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