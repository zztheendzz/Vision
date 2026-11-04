using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

using Vision.Tools.Abstractions;
using Vision.Tools.Registry;

namespace UI.ViewModels.ToolboxAndParam
{
    /// <summary>
    /// Danh sách tool để kéo thả. Tự lấy từ ToolRegistry, thêm tool mới vào registry thì toolbox tự có.
    /// Nhóm theo hành vi của tool (ToolKind). Có ô tìm kiếm.
    /// </summary>
    public partial class ToolboxViewModel : ViewModelBase
    {
        private readonly List<ToolboxItemViewModel> _allItems = new();

        public ObservableCollection<ToolboxGroupViewModel> Groups { get; } = new();

        [ObservableProperty]
        private string _searchText = "";

        public ToolboxViewModel()
        {
            foreach (var d in ToolRegistry.All.OrderBy(d => d.DisplayName))
            {
                string inputs = string.Join(", ", d.Inputs.Select(p => p.Name));
                string outputs = string.Join(", ", d.Outputs.Select(p => p.Name));
                string io = $"Vào: {(inputs == "" ? "-" : inputs)}   Ra: {(outputs == "" ? "-" : outputs)}";
                string text = d.Description == "" ? io : $"{d.Description}\n{io}";

                _allItems.Add(new ToolboxItemViewModel(d.Id, d.DisplayName, d.Kind, text));
            }

            Refresh();
        }

        // Tự chạy mỗi khi người dùng gõ vào ô tìm kiếm
        partial void OnSearchTextChanged(string value) => Refresh();

        private void Refresh()
        {
            Groups.Clear();
            string filter = SearchText.Trim();

            foreach (var kind in new[] { ToolKind.Source, ToolKind.Pure, ToolKind.Stateful, ToolKind.Action })
            {
                var items = _allItems
                    .Where(i => i.Kind == kind &&
                                (filter.Length == 0 ||
                                 i.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (items.Count > 0)
                    Groups.Add(new ToolboxGroupViewModel(GroupTitle(kind), items));
            }
        }

        private static string GroupTitle(ToolKind kind) => kind switch
        {
            ToolKind.Source => "Nguồn dữ liệu",
            ToolKind.Pure => "Xử lý ảnh",
            ToolKind.Stateful => "Xử lý có mô hình",
            ToolKind.Action => "Hành động (tác động ra ngoài)",
            _ => "Khác"
        };
    }
}
