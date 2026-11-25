using Communication.Drivers;
using Communication.Fov;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Controls.Primitives;
using Vision.Graph;

namespace UI.ViewModels
{
    /// <summary>Cửa sổ "Bảng FOV": mỗi số FOV (thanh ghi PLC) gắn với camera, file mẫu, file hiệu chuẩn.</summary>
    public partial class FovTableViewModel : ViewModelBase
    {
        private static readonly HashSet<string> PathTools = new() { "FindShape", "PickCompensation", "PlaceCompensation", "PixelToRobot" };

        private readonly FovTable _table;

        public ObservableCollection<FovEntry> Rows { get; } = new();
        public IReadOnlyList<string> Cameras { get; }

        /// <summary>Node trong recipe hiện tại có dùng file mẫu / hiệu chuẩn, để gán đường dẫn riêng cho từng FOV.</summary>
        public IReadOnlyList<NodeChoice> NodeChoices { get; }

        [ObservableProperty] private FovEntry? _selected;
        [ObservableProperty] private FovNodePath? _selectedNode;
        [ObservableProperty] private string _statusMessage = "";

        public FovTableViewModel(FovTable table, DeviceManager devices, IEnumerable<VisionNode> graphNodes)
        {
            _table = table;
            NodeChoices = graphNodes
                .Where(n => PathTools.Contains(n.ToolName))
                .OrderBy(n => n.ToolName).ThenBy(n => n.Y).ThenBy(n => n.X)
                .Select(n => new NodeChoice(n.Id, $"{n.ToolName} @ ({n.X:F0}, {n.Y:F0})"))
                .ToList();
            Cameras = devices.CameraNames;
            foreach (var e in table.Entries)
            {
                var row = new FovEntry { Enabled = e.Enabled, Fov = e.Fov, Camera = e.Camera };
                foreach (var n in e.Nodes)
                {
                    // Làm mới nhãn theo recipe hiện tại (nếu node còn)
                    string label = NodeChoices.FirstOrDefault(c => c.Id == n.NodeId)?.Label ?? n.Label + " (không còn trong recipe)";
                    row.Nodes.Add(new FovNodePath { NodeId = n.NodeId, Label = label, ModelPath = n.ModelPath, CalibrationPath = n.CalibrationPath });
                }
                Rows.Add(row);
            }
            if (table.LoadError != null) StatusMessage = table.LoadError;
            else if (Cameras.Count == 0) StatusMessage = "Chưa có camera nào trong Cấu hình thiết bị (tab Camera): thêm camera rồi mở lại bảng này.";
        }

        [RelayCommand]
        private void Add()
        {
            int next = Rows.Count == 0 ? 1 : Rows.Max(r => r.Fov) + 1;
            var row = new FovEntry
            {
                Fov = next,
                Camera = Cameras.FirstOrDefault() ?? ""
            };
            Rows.Add(row);
            Selected = row;
        }

        [RelayCommand]
        private void Remove()
        {
            if (Selected != null) Rows.Remove(Selected);
        }

        [RelayCommand]
        private void AddNode()
        {
            if (Selected == null) { StatusMessage = "Chọn một dòng FOV trước."; return; }
            var free = NodeChoices.FirstOrDefault(c => Selected.Nodes.All(n => n.NodeId != c.Id));
            if (free == null) { StatusMessage = "Recipe không còn node FindShape / Pick / Place / PixelToRobot nào chưa gán."; return; }
            var row = new FovNodePath { NodeId = free.Id, Label = free.Label };
            Selected.Nodes.Add(row);
            SelectedNode = row;
        }

        [RelayCommand]
        private void RemoveNode()
        {
            if (Selected != null && SelectedNode != null) Selected.Nodes.Remove(SelectedNode);
        }

        /// <summary>Đổi node của một dòng: cập nhật nhãn theo node mới.</summary>
        public void RefreshLabel(FovNodePath row)
        {
            var c = NodeChoices.FirstOrDefault(x => x.Id == row.NodeId);
            if (c != null) row.Label = c.Label;
        }

        [RelayCommand]
        private void Save()
        {
            foreach (var r in Rows) foreach (var n in r.Nodes) RefreshLabel(n);
            string? error = FovTable.Validate(Rows);
            if (error != null) { StatusMessage = "Chưa lưu: " + error; return; }
            try
            {
                _table.Save(Rows);
                StatusMessage = $"Đã lưu {Rows.Count} FOV ({_table.FilePath}). Có hiệu lực từ lượt chạy kế tiếp.";
            }
            catch (Exception ex) { StatusMessage = "Không lưu được: " + ex.Message; }
        }
    }

    /// <summary>Node để chọn trong bảng FOV.</summary>
    public sealed record NodeChoice(Guid Id, string Label);
}
