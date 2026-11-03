using Communication.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Vision.Graph;

namespace UI.ViewModels
{
    /// <summary>Gom các ViewModel và nối chúng với nhau.</summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly VisionGraph _graph;

        public GraphEditorViewModel Editor { get; }
        public ToolboxViewModel Toolbox { get; }
        public PropertyPanelViewModel Properties { get; }

        /// <summary>Chạy thử đồ thị, hiển thị ảnh và kết quả.</summary>
        public RunViewModel Run { get; }

        /// <summary>Danh sách thiết bị (robot, PLC, PC, scanner...) và trạng thái kết nối.</summary>
        public DeviceConfigViewModel Devices { get; }

        public MainViewModel(IDialogService dialogs)
        {
            _graph = BuildDemoGraph();
            Editor = new GraphEditorViewModel(_graph, dialogs);
            Toolbox = new ToolboxViewModel();
            Properties = new PropertyPanelViewModel();
            Run = new RunViewModel(Editor, dialogs);
            Devices = new DeviceConfigViewModel(new DeviceConfigStore(), dialogs);

            // Chọn node nào trên đồ thị thì bảng thuộc tính hiển thị tham số của node đó
            Editor.SelectedNodeChanged += (_, _) => Properties.Node = Editor.SelectedNode;
        }

        private static VisionGraph BuildDemoGraph()
        {
            var g = new VisionGraph();
            return g;
        }

        public void Dispose()
        {
            Run.Dispose();         // dừng lần chạy dở, giải phóng ảnh và kết quả
            Devices.Dispose();     // ngắt mọi kết nối
            _graph.Dispose();
        }
    }
}
