using Communication.Fixtures;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels.Device
{
    /// <summary>Một khuôn tìm thấy khi quét, kèm ô chọn để thêm / cập nhật vào danh sách khuôn.</summary>
    public partial class ScanRow : ObservableObject
    {
        public ScannedFixture Fixture { get; }
        public string Ip => Fixture.Ip;
        public int Port => Fixture.Port;
        public string FixtureNo => Fixture.FixtureNo.ToString("00");
        public int SlotCount => Fixture.SlotCount;
        public string Summary => Fixture.Summary;

        /// <summary>Đã có khuôn cùng IP + cổng hoặc cùng số khuôn trong danh sách: thêm sẽ cập nhật khuôn đó.</summary>
        public string ExistingName { get; }

        /// <summary>"Quét" = tìm thấy khi bấm Quét khuôn; "Tự nhận" = khuôn tự nối vào cổng nhận và gửi dòng dữ liệu.</summary>
        public string Source { get; }
        public string Action => ExistingName.Length == 0 ? "Thêm mới" : $"Cập nhật '{ExistingName}'";

        [ObservableProperty] private bool _selected = true;

        public ScanRow(ScannedFixture fixture, string existingName, string source = "Quét")
        {
            Fixture = fixture;
            Source = source;
            ExistingName = existingName;
        }
    }
}
