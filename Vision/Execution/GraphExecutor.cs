using System.Diagnostics;
using Vision.Core;
using Vision.Graph;
using Vision.Services;


namespace Vision.Execution
{

    public enum NodeStatus { Pending, Ok, Failed, Skipped }

    public class NodeRun
    {
        public NodeStatus Status { get; set; } = NodeStatus.Pending;
        public string Message { get; set; } = "";
        public long ElapsedMs { get; set; }

        /// <summary>true: node nằm ở nhánh của camera khác (theo FOV), bị bỏ qua có chủ ý, không phải lỗi.</summary>
        public bool OffBranch { get; set; }
        public VisionToolResult? Result { get; set; }
    }

    /// <summary>Kết quả một lượt chạy cả graph. Dispose = giải phóng toàn bộ kết quả các node.</summary>
    public class GraphRunResult : IDisposable
    {
        public Dictionary<Guid, NodeRun> Nodes { get; } = new();
        public NodeRun this[VisionNode node] => Nodes[node.Id];

        public void Dispose()
        {
            foreach (var r in Nodes.Values) r.Result?.Dispose();
            Nodes.Clear();
        }
    }

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
            var context = new ToolContext(options.Mode, ct, _logger, options.NeedDisplay, options.Fov, options.ModelPath, options.CalibrationPath);

            // Chụp tham số hiệu lực ngay đầu lượt: UI sửa node giữa chừng không ảnh hưởng lượt đang chạy
            var effective = BuildEffectiveParameters(graph, options);

            var offBranch = ComputeOffBranch(order, effective, options);

            var run = new GraphRunResult();
            try
            {
                foreach (var node in order)
                {
                    ct.ThrowIfCancellationRequested();

                    var nr = new NodeRun();
                    run.Nodes[node.Id] = nr;

                    if (offBranch.Contains(node.Id))
                    {
                        nr.Status = NodeStatus.Skipped;
                        nr.OffBranch = true;
                        nr.Message = $"Nhánh của camera khác (FOV {options.Fov} dùng camera '{options.ActiveCamera}')";
                        NodeFinished?.Invoke(node, nr);
                        continue;
                    }

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

        /// <summary>
        /// Các node nằm ở nhánh của camera KHÁC camera đang dùng (theo FOV). Node thuộc nhánh khi có node Camera Image ở phía trước
        /// (kể cả chính nó); nhánh bị bỏ qua khi không camera nào trong số đó là camera đang dùng. Node không có Camera Image phía trước thì luôn chạy.
        /// Camera Image để trống tham số Camera = camera đầu tiên.
        /// </summary>
        private static HashSet<Guid> ComputeOffBranch(
            IReadOnlyList<VisionNode> order, Dictionary<Guid, Dictionary<string, object?>> effective, RunOptions options)
        {
            var off = new HashSet<Guid>();
            if (string.IsNullOrWhiteSpace(options.ActiveCamera)) return off;

            string active = options.ActiveCamera!.Trim();
            var cameras = new Dictionary<Guid, HashSet<string>>();
            foreach (var node in order)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var port in node.Inputs)
                    if (port.Connection is { } c && cameras.TryGetValue(c.From.Owner.Id, out var up))
                        set.UnionWith(up);

                if (node.ToolName == "CameraImage")
                {
                    effective[node.Id].TryGetValue("Camera", out var cam);
                    string name = (cam as string ?? "").Trim();
                    if (name.Length == 0) name = options.FirstCamera?.Trim() ?? "";
                    set.Add(name);
                }

                cameras[node.Id] = set;
                if (set.Count > 0 && !set.Contains(active)) off.Add(node.Id);
            }
            return off;
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