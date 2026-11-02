
using System.Windows.Controls;
using UI.ViewModels;

namespace UI.View
{
    /// <summary>
    /// Interaction logic for GraphEditorView.xaml
    /// </summary>
    public partial class GraphEditorView : UserControl
    {
        public GraphEditorView()
        {
            InitializeComponent();
            var vm = new MainViewModel(new DialogService());
            DataContext = vm;
            //Closed += (_, _) => vm.Dispose();
        }
    }
}
