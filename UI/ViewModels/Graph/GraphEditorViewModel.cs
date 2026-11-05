using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using UI.Models;
using UI.Services;
using UI.ViewModels.Graph;
using Vision.Execution;
using Vision.Graph;

namespace UI.ViewModels.Graph
{
    /// <summary>
    /// Bọc VisionGraph. Đây là NƠI DUY NHẤT sửa graph (thêm/xóa node, nối/ngắt dây),
    /// nên model không cần bắn sự kiện, và sau này gắn undo/redo vào đúng một chỗ.
    /// </summary>
    public partial class GraphEditorViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;
        private readonly Dictionary<VisionNode, NodeViewModel> _nodeMap = new();
        private readonly Dictionary<Port, ConnectorViewModel> _connectorMap = new();

        public VisionGraph Model { get; private set; }

        public ObservableCollection<NodeViewModel> Nodes { get; } = new();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

        /// <summary>Nodify cập nhật danh sách này khi người dùng chọn node.</summary>
        public ObservableCollection<NodeViewModel> SelectedNodes { get; } = new();

        public PendingConnectionViewModel PendingConnection { get; }

        public NodeViewModel? SelectedNode => SelectedNodes.FirstOrDefault();
        public event EventHandler? SelectedNodeChanged;

        /// <summary>RunViewModel đặt true khi đang chạy: không cho sửa graph.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
        private bool _isLocked;

        public GraphEditorViewModel(VisionGraph model, IDialogService dialogs)
        {
            Model = model;
            _dialogs = dialogs;
            PendingConnection = new PendingConnectionViewModel(this);

            SelectedNodes.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(SelectedNode));
                SelectedNodeChanged?.Invoke(this, EventArgs.Empty);
            };

            Rebuild();
        }

        /// <summary>Thay graph khác (mở file). Người gọi tự Dispose graph cũ.</summary>
        public void Load(VisionGraph newModel)
        {
            Model = newModel;
            OnPropertyChanged(nameof(Model));
            Rebuild();
        }

        private void Rebuild()
        {
            SelectedNodes.Clear();
            Connections.Clear();
            Nodes.Clear();
            _nodeMap.Clear();
            _connectorMap.Clear();

            foreach (var node in Model.Nodes) AddNodeViewModel(node);

            foreach (var c in Model.Connections)
                Connections.Add(new ConnectionViewModel(c, _connectorMap[c.From], _connectorMap[c.To]));
        }

        private NodeViewModel AddNodeViewModel(VisionNode node)
        {
            var vm = new NodeViewModel(node);
            foreach (var c in vm.Inputs.Concat(vm.Outputs)) _connectorMap[c.Model] = c;
            _nodeMap[node] = vm;
            Nodes.Add(vm);
            return vm;
        }

        // ---------- Thêm node (kéo thả từ toolbox) ----------

        public NodeViewModel? AddNode(string toolId, Point location)
        {
            if (IsLocked) return null;
            try
            {
                var node = Model.AddNode(toolId, location.X, location.Y);
                return AddNodeViewModel(node);
            }
            catch (Exception ex)
            {
                _dialogs.ShowError($"Không tạo được node '{toolId}': {ex.Message}");
                return null;
            }
        }

        // ---------- Nối / ngắt dây ----------

        /// <summary>Nối hai cổng bất kể hướng kéo. Kiểm tra kiểu và vòng lặp do VisionGraph.Connect làm.</summary>
        public bool TryConnect(ConnectorViewModel a, ConnectorViewModel b)
        {
            if (IsLocked) return false;

            if (a.IsInput == b.IsInput)
            {
                _dialogs.ShowError("Phải nối một cổng ra với một cổng vào.");
                return false;
            }

            var output = a.IsInput ? b : a;
            var input = a.IsInput ? a : b;

            try
            {
                var c = Model.Connect((OutputPort)output.Model, (InputPort)input.Model);
                Connections.Add(new ConnectionViewModel(c, output, input));
                output.RefreshConnected();
                input.RefreshConnected();
                return true;
            }
            catch (InvalidOperationException ex)
            {
                _dialogs.ShowError(ex.Message);   // sai kiểu, input đã có nguồn, tạo vòng lặp
                return false;
            }
        }

        public void Disconnect(ConnectorViewModel connector)
        {
            if (IsLocked) return;
            foreach (var c in Connections.Where(c => c.Source == connector || c.Target == connector).ToList())
                RemoveConnection(c);
        }

        /// <summary>Nodify gọi lệnh này khi người dùng ngắt dây ở một cổng.</summary>
        [RelayCommand]
        private void DisconnectConnector(ConnectorViewModel? connector)
        {
            if (connector != null) Disconnect(connector);
        }

        private void RemoveConnection(ConnectionViewModel c)
        {
            Model.Disconnect(c.Model);
            Connections.Remove(c);
            c.Source.RefreshConnected();
            c.Target.RefreshConnected();
        }

        // ---------- Xóa node ----------

        [RelayCommand(CanExecute = nameof(CanEdit))]
        private void DeleteSelected()
        {
            foreach (var node in SelectedNodes.ToList()) RemoveNode(node);
        }

        private bool CanEdit() => !IsLocked;

        private void RemoveNode(NodeViewModel node)
        {
            // Ngắt dây trước: cập nhật luôn trạng thái cổng ở đầu bên kia
            foreach (var c in Connections
                         .Where(c => c.Source.Node == node || c.Target.Node == node).ToList())
                RemoveConnection(c);

            foreach (var c in node.Inputs.Concat(node.Outputs)) _connectorMap.Remove(c.Model);
            _nodeMap.Remove(node.Model);
            SelectedNodes.Remove(node);
            Nodes.Remove(node);

            Model.RemoveNode(node.Model);          // giải phóng tool bên trong node
        }

        // ---------- Hiển thị trạng thái chạy (gọi trên UI thread) ----------

        public void ResetRunState()
        {
            foreach (var n in Nodes) n.ResetRun();
        }

        public void UpdateNodeRun(VisionNode node, NodeRun run)
        {
            if (_nodeMap.TryGetValue(node, out var vm)) vm.ApplyRun(run);
        }
    }
}
