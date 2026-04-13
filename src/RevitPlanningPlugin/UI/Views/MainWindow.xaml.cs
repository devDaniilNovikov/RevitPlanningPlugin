using System.Windows;
using Autodesk.Revit.UI;
using RevitPlanningPlugin.UI.ViewModels;

namespace RevitPlanningPlugin.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(ExternalCommandData commandData)
        {
            InitializeComponent();
            DataContext = new MainViewModel(commandData);
        }
    }
}
