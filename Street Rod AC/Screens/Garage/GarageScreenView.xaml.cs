using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Street_Rod_AC.Screens.Garage
{
    /// <summary>
    /// Interaction logic for GarageScreenView.xaml
    /// </summary>
    public partial class GarageScreenView : System.Windows.Controls.UserControl
    {
        public GarageScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is GarageScreenViewModel viewModel)
            {
                if (viewModel.SkipEnterAnimation)
                {
                    // Skip animation - set everything to full opacity immediately
                    RootGrid.Opacity = 1;
                    BackButton.Opacity = 1;
                    ExitButton.Opacity = 1;
                    CarDisplayButton.Opacity = 1;
                    PreviewContainer.Opacity = 1;
                    CalendarButton.Opacity = 1;
                }
                else
                {
                    // Play the fade-in animations
                    var backgroundFade = new DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(0.5)
                    };
                    RootGrid.BeginAnimation(OpacityProperty, backgroundFade);

                    var buttonFade = new DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(0.5),
                        BeginTime = TimeSpan.FromSeconds(0.3)
                    };
                    BackButton.BeginAnimation(OpacityProperty, buttonFade);
                    ExitButton.BeginAnimation(OpacityProperty, buttonFade);
                    CarDisplayButton.BeginAnimation(OpacityProperty, buttonFade);
                    CalendarButton.BeginAnimation(OpacityProperty, buttonFade);

                    var contentFade = new DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(0.5),
                        BeginTime = TimeSpan.FromSeconds(0.4)
                    };
                    PreviewContainer.BeginAnimation(OpacityProperty, contentFade);
                }
            }
        }
    }
}
