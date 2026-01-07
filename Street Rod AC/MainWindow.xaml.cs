using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Navigation;

namespace Street_Rod_AC
{
    public partial class MainWindow : System.Windows.Window
    {
        public NavigationService NavigationService { get; }
        public DialogService DialogService { get; }

        public MainWindow()
        {
            InitializeComponent();

            var app = (App)System.Windows.Application.Current;
            NavigationService = app.NavigationService;
            DialogService = app.DialogService;

            DataContext = this;
        }
    }
}