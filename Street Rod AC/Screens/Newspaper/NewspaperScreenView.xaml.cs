using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Street_Rod_AC.Screens.Newspaper
{
    public partial class NewspaperScreenView : System.Windows.Controls.UserControl
    {
        public NewspaperScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewspaperScreenViewModel viewModel)
            {
                if (viewModel.SkipEnterAnimation)
                {
                    // Skip animation - set everything to full opacity immediately
                    RootGrid.Opacity = 1;
                    BackButton.Opacity = 1;
                    ContentPanel.Opacity = 1;
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

                    var contentFade = new DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(0.5),
                        BeginTime = TimeSpan.FromSeconds(0.3)
                    };
                    ContentPanel.BeginAnimation(OpacityProperty, contentFade);
                }
            }
        }
    }
}
