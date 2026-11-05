using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UI.ViewModels.RunAndDisplay;

namespace UI.Views
{
    /// <summary>Hiển thị một danh sách FieldViewModel thành các ô nhập, gom theo Section.</summary>
    public partial class FieldsEditor : UserControl
    {
        public static readonly DependencyProperty FieldsProperty =
            DependencyProperty.Register(nameof(Fields), typeof(IEnumerable), typeof(FieldsEditor),
                new PropertyMetadata(null, (d, e) => ((FieldsEditor)d).Rebuild()));

        public IEnumerable? Fields
        {
            get => (IEnumerable?)GetValue(FieldsProperty);
            set => SetValue(FieldsProperty, value);
        }

        public FieldsEditor()
        {
            InitializeComponent();
        }

        private readonly List<FieldViewModel> _watched = new();
        private ListCollectionView? _view;

        private void Rebuild()
        {
            foreach (var f in _watched) f.PropertyChanged -= OnFieldChanged;
            _watched.Clear();

            if (Fields == null)
            {
                _view = null;
                Items.ItemsSource = null;
                return;
            }

            var list = Fields.Cast<FieldViewModel>().ToList();
            foreach (var f in list)
            {
                f.PropertyChanged += OnFieldChanged;
                _watched.Add(f);
            }

            // Ô ẩn bị lọc ra; nhóm không còn ô nào thì tiêu đề nhóm cũng mất
            _view = new ListCollectionView(list) { Filter = o => ((FieldViewModel)o).IsVisible };
            _view.GroupDescriptions.Add(new PropertyGroupDescription("Section"));
            Items.ItemsSource = _view;
        }

        private void OnFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FieldViewModel.IsVisible)) _view?.Refresh();
        }
    }
}
