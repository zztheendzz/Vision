using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UI.ViewModels.ToolboxAndParam;



namespace UI.Views
{
    public partial class ToolboxPanel : UserControl
    {
        /// <summary>Tên "loại dữ liệu" gắn vào thao tác kéo thả. Đồ thị nhận đúng tên này để biết là đang kéo tool.</summary>
        public const string DragFormat = "Vision.ToolId";

        // Vị trí chuột lúc bấm xuống, để biết người dùng đã kéo đủ xa chưa
        private Point _dragStart;

        public ToolboxPanel()
        {
            InitializeComponent();
        }

        private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
        }

        private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            // Chưa kéo đủ xa thì chưa coi là kéo (tránh bắt nhầm khi chỉ bấm chọn)
            Vector moved = _dragStart - e.GetPosition(null);
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            // Tìm mục tool đang bị kéo. Kéo tên nhóm thì bỏ qua.
            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            if (item?.DataContext is not ToolboxItemViewModel tool) return;

            var data = new DataObject(DragFormat, tool.ToolId);
            DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
        }

        /// <summary>Đi từ phần tử chuột đang trỏ lên cha cho đến khi gặp TreeViewItem.</summary>
        private static TreeViewItem? FindTreeViewItem(DependencyObject? element)
        {
            while (element != null && element is not TreeViewItem)
            {
                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }
            return element as TreeViewItem;
        }
    }
}
