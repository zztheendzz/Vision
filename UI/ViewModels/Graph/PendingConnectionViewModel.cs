using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Text;
using UI.ViewModels.Graph;

namespace UI.ViewModels.Graph
{

    /// <summary>Dây đang kéo dở. Thả lên cổng khác thì nhờ GraphEditorViewModel nối thật.</summary>
    public partial class PendingConnectionViewModel : ViewModelBase
    {
        private readonly GraphEditorViewModel _editor;

        [ObservableProperty]
        private ConnectorViewModel? _source;

        /// <summary>Nodify ghi đối tượng đang rê chuột lên (cổng hoặc rỗng).</summary>
        [ObservableProperty]
        private object? _target;

        [ObservableProperty]
        private bool _isVisible;

        public PendingConnectionViewModel(GraphEditorViewModel editor) => _editor = editor;

        [RelayCommand]
        private void Start(ConnectorViewModel? source) => Source = source;

        [RelayCommand]
        private void Complete(object? target)
        {
            // Thả ra khoảng trống (target rỗng) thì bỏ qua, không báo lỗi
            if (Source != null && target is ConnectorViewModel connector && connector != Source)
                _editor.TryConnect(Source, connector);

            Source = null;
        }
    }
}
