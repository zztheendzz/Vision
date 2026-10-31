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
using UI.ViewModels;


namespace UI.View
{
    /// <summary>
    /// Interaction logic for view.xaml
    /// </summary>
    public partial class view : Window
    {
        public view()
        {
            InitializeComponent();
            var vm = new MainViewModel(new DialogService());
            DataContext = vm;
            Closed += (_, _) => vm.Dispose();      // giải phóng graph và các tool bên trong
        }
    }
}
