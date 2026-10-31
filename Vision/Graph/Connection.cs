namespace Vision.Graph
{
    /// <summary>Sợi dây nối OutputPort -> InputPort.</summary>
    public class Connection
    {
        public OutputPort From { get; }
        public InputPort To { get; }
        public Connection(OutputPort from, InputPort to) { From = from; To = to; }
    }
}
