using Communication.Fixtures;
using System;
using System.Collections.Generic;
using System.Text;

namespace UI.Views
{
    /// <summary>Danh sách kết quả chọn được cho từng slot (Pass, Fail, Repair...), dùng trong XAML qua x:Static.</summary>
    public static class SlotChoicesHolder
    {
        public static IReadOnlyList<SlotState> Items => UI.ViewModels.Device.SlotViewModel.Choices;
    }
}
