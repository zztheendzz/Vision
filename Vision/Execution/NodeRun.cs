using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;

namespace Vision.Execution
{
    public class NodeRun
    {
        public NodeStatus Status { get; set; } = NodeStatus.Pending;
        public string Message { get; set; } = "";
        public long ElapsedMs { get; set; }
        public VisionToolResult? Result { get; set; }
    }
}
