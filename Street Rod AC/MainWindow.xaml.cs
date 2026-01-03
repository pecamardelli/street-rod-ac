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
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            // Get services from the App instance
            var app = (App)Application.Current;
            _viewModel = new MainWindowViewModel(app.ContentService, app.Launcher);
            DataContext = _viewModel;

            // Load content when window loads
            Loaded += async (s, e) => await _viewModel.LoadContentAsync();
        }
    }
}