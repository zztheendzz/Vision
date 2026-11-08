using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels.ToolboxAndParam
{
    /// <summary>Một nhóm tool trong toolbox (ví dụ "Nguồn dữ liệu", "Xử lý ảnh").</summary>
    public sealed class ToolboxGroupViewModel
    {
        public string Title { get; }
        public IReadOnlyList<ToolboxItemViewModel> Items { get; }

        public ToolboxGroupViewModel(string title, IReadOnlyList<ToolboxItemViewModel> items)
        {
            Title = title;
            Items = items;
        }
    }
}
