using Vision.Core;

namespace Vision.Graph
{
    public abstract class Port
    {
        public VisionNode Owner { get; }
        public PortDef Def { get; }
        public string Name => Def.Name;
        public Type Type => Def.Type;

        protected Port(VisionNode owner, PortDef def) { 
            Owner = owner; Def = def; }
    }

    /// <summary>1 output nối được nhiều dây (rẽ nhánh).</summary>
    public class OutputPort : Port
    {
        public List<Connection> Connections { get; } = new();
        public OutputPort(VisionNode owner, PortDef def) : base(owner, def) { }
    }

    /// <summary>1 input chỉ nhận 1 dây.</summary>
    public class InputPort : Port
    {
        public Connection? Connection { get; internal set; }
        public bool Required => Def.Required;
        public InputPort(VisionNode owner, PortDef def) : base(owner, def) { }
    }
}
