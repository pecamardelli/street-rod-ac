using Street_Rod_AC.Navigation;

namespace Street_Rod_AC
{
    public partial class MainWindow : System.Windows.Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var app = (App)System.Windows.Application.Current;
            DataContext = app.NavigationService;
        }
    }
}