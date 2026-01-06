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
        private readonly double FadeDuration = 0.5;

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

            // Create splash fade out animation
            var splashFadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(FadeDuration)
            };

            // When splash fade completes, hide it
            splashFadeOut.Completed += (s, args) =>
            {
                splashScreen.Visibility = Visibility.Collapsed;
            };

            splashScreen.BeginAnimation(UIElement.OpacityProperty, splashFadeOut);

            // Simultaneously start main menu background fade-in (with slight delay for overlap)
            var mainMenuFadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                BeginTime = TimeSpan.FromSeconds(0.2),
                Duration = TimeSpan.FromSeconds(FadeDuration)
            };

            mainMenuFadeIn.Completed += (s, args) =>
            {
                // After background is visible, fade in and slide up the buttons
                AnimateButtons();
            };

            MainMenuGrid.BeginAnimation(UIElement.OpacityProperty, mainMenuFadeIn);
        }

        private void AnimateButtons()
        {
            // Exit button fade-in and slide-up
            var exitButtonFadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(FadeDuration)
            };

            var exitButtonSlideUp = new DoubleAnimation
            {
                From = 30,
                To = 0,
                Duration = TimeSpan.FromSeconds(FadeDuration)
            };

            ExitButton.BeginAnimation(UIElement.OpacityProperty, exitButtonFadeIn);
            var exitTransform = ExitButton.RenderTransform as TranslateTransform;
            exitTransform?.BeginAnimation(TranslateTransform.YProperty, exitButtonSlideUp);

            // Menu buttons fade-in and slide-up
            var menuButtonsFadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(FadeDuration)
            };

            var menuButtonsSlideUp = new DoubleAnimation
            {
                From = 30,
                To = 0,
                Duration = TimeSpan.FromSeconds(FadeDuration)
            };

            MenuButtonsContainer.BeginAnimation(UIElement.OpacityProperty, menuButtonsFadeIn);
            var menuTransform = MenuButtonsContainer.RenderTransform as TranslateTransform;
            menuTransform?.BeginAnimation(TranslateTransform.YProperty, menuButtonsSlideUp);
        }
    }
}