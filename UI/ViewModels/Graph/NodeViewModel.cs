using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using Vision.Execution;
using Vision.Graph;

namespace UI.ViewModels.Graph
{
    /// <summary>Bọc một VisionNode. Model là nguồn sự thật: vị trí ghi ngược vào model.</summary>
    public partial class NodeViewModel : ViewModelBase
    {
        public VisionNode Model { get; }

        /// <summary>Tạm dùng ToolName. Khi có ToolDescriptor thì đổi sang DisplayName.</summary>
        public string Title => Model.ToolName;

        public ObservableCollection<ConnectorViewModel> Inputs { get; } = new();
        public ObservableCollection<ConnectorViewModel> Outputs { get; } = new();

        [ObservableProperty]
        private Point _location;

        [ObservableProperty]
        private NodeStatus _status = NodeStatus.Pending;

        [ObservableProperty]
        private string _message = "";

        [ObservableProperty]
        private long _elapsedMs;

        public NodeViewModel(VisionNode model)
        {
            Model = model;
            _location = new Point(model.X, model.Y);      // gán trường: không kích hoạt ghi ngược

            foreach (var p in model.Inputs) Inputs.Add(new ConnectorViewModel(this, p));
            foreach (var p in model.Outputs) Outputs.Add(new ConnectorViewModel(this, p));
        }

        partial void OnLocationChanged(Point value)
        {
            Model.X = value.X;
            Model.Y = value.Y;
        }

        /// <summary>Gọi trên UI thread.</summary>
        public void ApplyRun(NodeRun run)
        {
            Status = run.Status;
            Message = run.Message;
            ElapsedMs = run.ElapsedMs;
        }

        public void ResetRun()
        {
            Status = NodeStatus.Pending;
            Message = "";
            ElapsedMs = 0;
        }
    }
}