
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

using System.Windows.Input;

using UI.ViewModels.Device;
using UI.ViewModels.RunAndDisplay;

namespace UI.Views
{
    /// <summary>Tab "Khuôn test": cài đặt chung gọn, bảng tất cả khuôn (sửa tại chỗ), trạng thái từng slot, nhật ký, bảng lệnh và bảng kết quả slot.</summary>
    public partial class FixtureConfigView : UserControl
    {
        private INotifyCollectionChanged? _log;

        public FixtureConfigView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => HookLog();
            Unloaded += (_, _) => Unhook();
        }

        // Nhật ký tự cuộn xuống dòng mới nhất
        private void HookLog()
        {
            Unhook();
            if (DataContext is DeviceTypeViewModel vm)
            {
                _log = vm.Log;
                _log.CollectionChanged += OnLogChanged;
                _raw = vm.RawLog;
                _raw.CollectionChanged += OnRawChanged;
            }
        }

        private void Unhook()
        {
            if (_log != null) _log.CollectionChanged -= OnLogChanged;
            _log = null;
            if (_raw != null) _raw.CollectionChanged -= OnRawChanged;
            _raw = null;
        }

        private System.Collections.ObjectModel.ObservableCollection<LogLine>? _raw;

        private void OnRawChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (RawLogList.Items.Count > 0) RawLogList.ScrollIntoView(RawLogList.Items[RawLogList.Items.Count - 1]);
                }
                catch (InvalidOperationException) { }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            // Chạy sau khi danh sách đã cập nhật xong (gọi ngay thì ItemsControl chưa kịp đồng bộ → InvalidOperationException)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                }
                catch (InvalidOperationException) { }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Bảng nằm trong khung cuộn của cả trang: chuyển con lăn chuột cho khung ngoài để cuộn được
        private void ForwardWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not Control control || control.Parent is not UIElement parent) return;
            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            });
        }
    }
}
