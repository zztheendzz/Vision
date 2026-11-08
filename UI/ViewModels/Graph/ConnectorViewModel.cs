using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using Vision.Graph;

namespace UI.ViewModels.Graph
{
    public partial class ConnectorViewModel:ViewModelBase
    {
        public NodeViewModel Node { get; }
        public Port Model { get; }

        public bool IsInput => Model is InputPort;
        public string Name => Model.Name;
        public string TypeName => Model.Type.Name;

        /// <summary>Nodify tự ghi vị trí điểm nối vào đây (bind OneWayToSource). VM chỉ lưu.</summary>
        [ObservableProperty]
        private Point _anchor;

        [ObservableProperty]
        private bool _isConnected;

        public ConnectorViewModel(NodeViewModel node, Port model)
        {
            Node = node;
            Model = model;
            RefreshConnected();
        }

        /// <summary>Gọi sau khi nối hoặc ngắt dây có liên quan tới cổng này.</summary>
        public void RefreshConnected()
        {
            IsConnected = Model switch
            {
                InputPort i => i.Connection != null,
                OutputPort o => o.Connections.Count > 0,
                _ => false
            };
        }



    }
}
