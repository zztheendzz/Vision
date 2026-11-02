using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Vision.Graph;

namespace UI.ViewModels
{
    public  class MainViewModel : ViewModelBase, IDisposable
    {



        private readonly VisionGraph _graph;

        public GraphEditorViewModel Editor { get; }

        public MainViewModel(IDialogService dialogs)
        {
            _graph = BuildDemoGraph();
            Editor = new GraphEditorViewModel(_graph, dialogs);
        }

        private static VisionGraph BuildDemoGraph()
        {
            var g = new VisionGraph();
            var load = g.AddNode("LoadImage", 40, 80);
         
            var thr = g.AddNode("Threshold", 300, 80);
            var thr2 = g.AddNode("Threshold", 301, 80);
            var blobA = g.AddNode("Blob", 560, 20);
            var blobB = g.AddNode("Blob", 560, 200);

            var thr21 = g.AddNode("Threshold", 301, 80);
            var blobA1 = g.AddNode("Blob", 560, 20);
            var blobB1 = g.AddNode("Blob", 560, 200);

            g.Connect(load.Output("Image"), thr.Input("Image"));
            g.Connect(thr.Output("Region"), blobA.Input("Region"));
            g.Connect(thr.Output("Region"), blobB.Input("Region"));   // rẽ nhánh
            return g;
        }

        public void Dispose() => _graph.Dispose();
    }
}
