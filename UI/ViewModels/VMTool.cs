using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Graph;
using Vision.Tools.Locate;

namespace UI.ViewModels
{
    public partial class VMTool : ObservableObject
    {


        [ObservableProperty] public List<VisionNode> nodes = new List<VisionNode>();

        [RelayCommand]
        public void AddNode(string nameTool)
        {
            VisionNode visionNode = new VisionNode(nameTool);
        }

        [RelayCommand]
        public void AddNode1(string nameTool)
        {
            VisionNode visionNode = new VisionNode(nameTool);
        }





    }
}
