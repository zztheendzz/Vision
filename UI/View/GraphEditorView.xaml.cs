
using System.Windows;
using System.Windows.Controls;
using UI.ViewModels;

namespace UI.View
{
    public partial class GraphEditorView : UserControl
    {
        public GraphEditorView()
        {
            InitializeComponent();
        }

        /// <summary>Đang kéo một thứ gì đó qua đồ thị: chỉ cho phép thả nếu là tool từ toolbox.</summary>
        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(ToolboxPanel.DragFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>Thả tool lên đồ thị: tạo node mới đúng vị trí thả.</summary>
        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(ToolboxPanel.DragFormat)) return;
            if (e.Data.GetData(ToolboxPanel.DragFormat) is not string toolId) return;
            if (DataContext is not MainViewModel main) return;

            // Vị trí chuột tính theo khung đồ thị, đổi sang tọa độ trong đồ thị
            // (đồ thị có thể đang được kéo dịch hoặc phóng to nên phải tính cả hai)
            Point mouse = e.GetPosition(GraphCanvas);
            var location = new Point(
                GraphCanvas.ViewportLocation.X + mouse.X / GraphCanvas.ViewportZoom,
                GraphCanvas.ViewportLocation.Y + mouse.Y / GraphCanvas.ViewportZoom);

            main.Editor.AddNode(toolId, location);
            e.Handled = true;
        }
    }
}
