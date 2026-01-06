using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
            var app = (App)System.Windows.Application.Current;
            _viewModel = new MainWindowViewModel(app.ContentService, app.Launcher);
            DataContext = _viewModel;

            // Load content when window loads
            Loaded += async (s, e) => await _viewModel.LoadContentAsync();
        }

        private void LaunchShowroom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var overlay = new ShowroomOverlay();
                overlay.Show();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to launch showroom overlay:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SplashScreen_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var splashScreen = sender as Grid;
            if (splashScreen == null) return;

            // Create fade out animation
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.5)
            };

            // When animation completes, hide the splash screen
            fadeOut.Completed += (s, args) =>
            {
                splashScreen.Visibility = Visibility.Collapsed;
            };

            splashScreen.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}