using System;
using System.Collections.Generic;
using System.Text;
using Vision.Graph;

namespace Vision.Execution
{
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
}
