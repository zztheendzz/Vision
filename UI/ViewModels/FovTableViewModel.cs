using Communication.Drivers;
using Communication.Fov;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Controls.Primitives;

namespace UI.ViewModels
{
    /// <summary>Cửa sổ "Bảng FOV": mỗi số FOV (thanh ghi PLC) gắn với camera, file mẫu, file hiệu chuẩn.</summary>
    public partial class FovTableViewModel : ViewModelBase
    {
        private readonly FovTable _table;

        public ObservableCollection<FovEntry> Rows { get; } = new();
        public IReadOnlyList<string> Cameras { get; }

        [ObservableProperty] private FovEntry? _selected;
        [ObservableProperty] private string _statusMessage = "";

        public FovTableViewModel(FovTable table, DeviceManager devices)
        {
            _table = table;
            Cameras = devices.CameraNames;
            foreach (var e in table.Entries)
                Rows.Add(new FovEntry { Enabled = e.Enabled, Fov = e.Fov, Camera = e.Camera, ModelPath = e.ModelPath, CalibrationPath = e.CalibrationPath });
            if (table.LoadError != null) StatusMessage = table.LoadError;
        }

        [RelayCommand]
        private void Add()
        {
            int next = Rows.Count == 0 ? 1 : Rows.Max(r => r.Fov) + 1;
            var row = new FovEntry
            {
                Fov = next,
                Camera = Cameras.FirstOrDefault() ?? "",
                ModelPath = $"models/fov{next}.shm",
                CalibrationPath = $"calib/fov{next}.json"
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
        private void Save()
        {
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
}
