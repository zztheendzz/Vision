using Communication.Plc;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using UI.ViewModels.Device;

namespace UI.Views
{
    /// <summary>Danh sách giá trị cho các cột chọn (Kiểu, Cách kích) trong lưới.</summary>
    public static class MapEnums
    {
        public static SignalKind[] Kinds { get; } = (SignalKind[])Enum.GetValues(typeof(SignalKind));
        public static TriggerMode[] Modes { get; } = (TriggerMode[])Enum.GetValues(typeof(TriggerMode));
    }

    public partial class PlcMapWindow : Window
    {
        public PlcMapWindow()
        {
            InitializeComponent();

            // Đóng khi còn sửa chưa lưu: hỏi Lưu / Không lưu / Hủy
            Closing += (_, e) =>
            {
                if (DataContext is not PlcMapViewModel vm || !vm.IsDirty) return;

                var answer = MessageBox.Show(this, "Bản đồ tín hiệu có thay đổi chưa lưu. Lưu lại?", "Xác nhận",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel) e.Cancel = true;
                else if (answer == MessageBoxResult.Yes)
                {
                    vm.SaveCommand.Execute(null);
                    if (vm.IsDirty) e.Cancel = true;          // lưu không được (còn lỗi): giữ cửa sổ để sửa
                }
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Sửa xong một ô: đánh dấu chưa lưu.</summary>
        private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit && DataContext is PlcMapViewModel vm) vm.MarkDirty();
        }

        /// <summary>Bảng theo chỉ số có số cột khác nhau nên tạo cột bằng code: cột đầu là nhãn hàng, các cột sau gắn chỉ mục [i].</summary>
        private void TableGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not DataGrid grid || grid.DataContext is not MapTableVm table) return;
            if (grid.Columns.Count > 0) return;                // đã tạo rồi (Loaded có thể chạy nhiều lần)

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Hàng",
                Binding = new Binding(nameof(MapRowVm.Label)) { Mode = BindingMode.OneWay },
                IsReadOnly = true,
                Width = 120
            });

            for (int i = 0; i < table.Table.Columns.Count; i++)
            {
                var col = table.Table.Columns[i];
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = $"{col.Title} ({KindText(col.Kind)})",
                    Binding = new Binding($"[{i}]")
                    {
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
                    },
                    Width = 120
                });
            }
        }

        private static string KindText(SignalKind kind) => kind switch
        {
            SignalKind.Bit => "bit",
            SignalKind.Word => "16 bit",
            _ => "32 bit"
        };

        /// <summary>Lưới nằm trong khung cuộn: bánh xe chuột trên lưới phải cuộn khung ngoài, không bị lưới nuốt.</summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.OriginalSource is not DependencyObject source) return;

            var grid = FindAncestor<DataGrid>(source);
            if (grid == null) return;

            var outer = FindAncestor<ScrollViewer>(VisualTreeHelper.GetParent(grid));
            if (outer == null) return;

            outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null)
            {
                if (node is T match) return match;
                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return null;
        }
    }
}
