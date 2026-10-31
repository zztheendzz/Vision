namespace Vision.Graph
{
    public class VisionGraph : IDisposable
    {
        private readonly List<VisionNode> _nodes = new();
        private readonly List<Connection> _connections = new();

        public IReadOnlyList<VisionNode> Nodes => _nodes;
        public IReadOnlyList<Connection> Connections => _connections;

        public VisionNode AddNode(string toolName, double x = 0, double y = 0, Guid? id = null)
        {
            var node = new VisionNode(toolName, id) { X = x, Y = y };
            _nodes.Add(node);
            return node;
        }

        public void RemoveNode(VisionNode node)
        {
            foreach (var c in _connections.Where(c => c.From.Owner == node || c.To.Owner == node).ToList())
                Disconnect(c);
            _nodes.Remove(node);
            node.Dispose();
        }

        public Connection Connect(OutputPort from, InputPort to)
        {
            if (from.Owner == to.Owner)
                throw new InvalidOperationException("Không nối vào chính nó");
            if (!to.Type.IsAssignableFrom(from.Type))
                throw new InvalidOperationException($"Sai kiểu: {from.Type.Name} -> {to.Type.Name}");
            if (to.Connection != null)
                throw new InvalidOperationException($"Input '{to.Name}' đã có nguồn");
            if (Reaches(to.Owner, from.Owner))
                throw new InvalidOperationException("Kết nối này tạo vòng lặp");

            var c = new Connection(from, to);
            from.Connections.Add(c);
            to.Connection = c;
            _connections.Add(c);
            return c;
        }

        public void Disconnect(Connection c)
        {
            c.From.Connections.Remove(c);
            c.To.Connection = null;
            _connections.Remove(c);
        }

        /// <summary>Thứ tự chạy (Kahn). Ném lỗi nếu có vòng (không xảy ra nếu chỉ nối qua Connect).</summary>
        public List<VisionNode> GetTopologicalOrder()
        {
            var indeg = _nodes.ToDictionary(n => n, n => n.Inputs.Count(p => p.Connection != null));
            var queue = new Queue<VisionNode>(_nodes.Where(n => indeg[n] == 0));
            var order = new List<VisionNode>(_nodes.Count);

            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                order.Add(n);
                foreach (var o in n.Outputs)
                    foreach (var c in o.Connections)
                        if (--indeg[c.To.Owner] == 0)
                            queue.Enqueue(c.To.Owner);
            }

            if (order.Count != _nodes.Count)
                throw new InvalidOperationException("Đồ thị có vòng lặp");
            return order;
        }

        // Có đường đi từ a tới b (theo chiều output) không?
        private static bool Reaches(VisionNode a, VisionNode b)
        {
            var seen = new HashSet<VisionNode>();
            var stack = new Stack<VisionNode>();
            stack.Push(a);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n == b) return true;
                if (!seen.Add(n)) continue;
                foreach (var o in n.Outputs)
                    foreach (var c in o.Connections)
                        stack.Push(c.To.Owner);
            }
            return false;
        }

        public void Dispose()
        {
            foreach (var n in _nodes) n.Dispose();
            _nodes.Clear();
            _connections.Clear();
        }
    }
}
