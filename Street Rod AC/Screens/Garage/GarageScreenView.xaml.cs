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
        private static readonly TimeSpan FadeInDuration = TimeSpan.FromSeconds(0.5);
        private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(0.35);

        public GarageScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>Overlay elements that fade in after the background, in order of appearance</summary>
        private FrameworkElement[] OverlayElements => new FrameworkElement[]
        {
            BackButton, ExitButton, CarDisplayButton, RightButtons,
            HitTheStreetsButton, NewspaperButton, PreviewContainer, CarInfoBar
        };

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GarageScreenViewModel viewModel)
                return;

            viewModel.ExitTransition = FadeOutAsync;

            if (viewModel.SkipEnterAnimation)
            {
                // Skip animation - set everything to full opacity immediately
                RootGrid.Opacity = 1;
                foreach (var element in OverlayElements)
                {
                    element.Opacity = 1;
                }
                return;
            }

            // Background (and the 3D garage) first, then the overlay staggered on top of it
            RootGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, FadeInDuration));

            var delay = TimeSpan.FromSeconds(0.3);
            foreach (var element in OverlayElements)
            {
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, FadeInDuration)
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                delay += TimeSpan.FromSeconds(0.04);
            }
        }

        /// <summary>
        /// Fades the whole screen out. Awaited by the view model before it navigates away.
        /// </summary>
        private Task FadeOutAsync()
        {
            var completion = new TaskCompletionSource();

            // Block clicks while leaving
            RootGrid.IsHitTestVisible = false;

            var fadeOut = new DoubleAnimation(RootGrid.Opacity, 0.0, FadeOutDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) => completion.TrySetResult();
            RootGrid.BeginAnimation(OpacityProperty, fadeOut);

            return completion.Task;
        }
    }
}
