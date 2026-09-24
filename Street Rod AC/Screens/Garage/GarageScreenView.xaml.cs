using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Street_Rod_AC.Screens.Garage
{
    /// <summary>
    /// Interaction logic for GarageScreenView.xaml
    /// </summary>
    public partial class GarageScreenView : System.Windows.Controls.UserControl
    {
        private static readonly TimeSpan FadeInDuration = TimeSpan.FromSeconds(0.6);
        private static readonly TimeSpan QuickFadeInDuration = TimeSpan.FromSeconds(0.25);
        private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(0.35);

        // Let the renderer draw a few frames (reflections, shadows) before showing it
        private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(200);

        // Never leave the player stuck on the loading layer if the renderer reports nothing
        private static readonly TimeSpan RevealTimeout = TimeSpan.FromSeconds(15);

        private DispatcherTimer? _revealTimer;
        private bool _revealed;

        public GarageScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += (_, _) => _revealTimer?.Stop();

            Viewport3D.Ready += (_, _) => RevealAfter(SettleDelay);
            Viewport3D.Failed += (_, _) => RevealAfter(TimeSpan.Zero);

            // Parts are picked in the 3D view; what a pick means is the workbench's business
            Viewport3D.PartClicked += part => (DataContext as GarageScreenViewModel)?.Workbench.OnPartClicked(part);
            Viewport3D.CandidateClicked += candidate => (DataContext as GarageScreenViewModel)?.Workbench.OnCandidateClicked(candidate);
            Viewport3D.PlacementChanged += text =>
            {
                if (DataContext is GarageScreenViewModel viewModel) viewModel.Workbench.PlacementDisplay = text ?? string.Empty;
            };

            // Placement mode is driven from the keyboard, wherever the focus is on the screen
            Unloaded += (_, _) => { if (_window != null) _window.PreviewKeyDown -= OnWindowKeyDown; };
        }

        private Window? _window;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is GarageScreenViewModel viewModel)
            {
                viewModel.ExitTransition = FadeOutAsync;
                viewModel.ExitCancelled = CancelFadeOut;
            }

            _window = Window.GetWindow(this);
            if (_window != null)
            {
                _window.PreviewKeyDown -= OnWindowKeyDown;
                _window.PreviewKeyDown += OnWindowKeyDown;
            }

            // Re-loaded with the garage already rendered (or failed): nothing to wait for
            if (Viewport3D.IsReady || Viewport3D.HasFailed)
            {
                RevealAfter(TimeSpan.Zero);
            }
            else
            {
                RevealAfter(RevealTimeout);
            }
        }

        /// <summary>
        /// Placement mode (a tool for fitting the converted parts, not gameplay): F5 on a selected part turns it on,
        /// then the arrows move the part across and fore-aft, PgUp/PgDn up and down, in the engine's axes; Ctrl
        /// steps a millimetre, Shift two centimetres, plain five; Tab moves the pad it sits on instead; R takes the
        /// slot back to where the packs put it; F5 or Esc ends it. Every step is written to the slot shifts.
        /// </summary>
        private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!IsLoaded) return;

            if (e.Key == System.Windows.Input.Key.F5)
            {
                Viewport3D.TogglePlacement();
                e.Handled = true;
                return;
            }

            if (!Viewport3D.IsPlacing) return;

            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            var step = modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) ? 0.001f
                : modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift) ? 0.02f
                : 0.005f;

            switch (e.Key)
            {
                case System.Windows.Input.Key.Left: Viewport3D.NudgePlacement(-step, 0, 0); break;
                case System.Windows.Input.Key.Right: Viewport3D.NudgePlacement(step, 0, 0); break;
                case System.Windows.Input.Key.Up: Viewport3D.NudgePlacement(0, 0, step); break;
                case System.Windows.Input.Key.Down: Viewport3D.NudgePlacement(0, 0, -step); break;
                case System.Windows.Input.Key.PageUp: Viewport3D.NudgePlacement(0, step, 0); break;
                case System.Windows.Input.Key.PageDown: Viewport3D.NudgePlacement(0, -step, 0); break;
                case System.Windows.Input.Key.Tab: Viewport3D.TogglePlacementTarget(); break;
                case System.Windows.Input.Key.R: Viewport3D.ResetPlacement(); break;
                case System.Windows.Input.Key.Escape: Viewport3D.TogglePlacement(); break;
                default: return;
            }

            e.Handled = true;
        }

        /// <summary>
        /// Schedules the reveal; a shorter delay replaces a pending longer one (timeout -> ready)
        /// </summary>
        private void RevealAfter(TimeSpan delay)
        {
            if (_revealed) return;

            _revealTimer?.Stop();

            if (delay <= TimeSpan.Zero)
            {
                Reveal();
                return;
            }

            _revealTimer = new DispatcherTimer { Interval = delay };
            _revealTimer.Tick += (_, _) => Reveal();
            _revealTimer.Start();
        }

        /// <summary>
        /// Swaps the loading layer for the finished screen: garage, car and overlay fade in together
        /// </summary>
        private void Reveal()
        {
            _revealTimer?.Stop();
            if (_revealed) return;
            _revealed = true;

            var skipAnimation = (DataContext as GarageScreenViewModel)?.SkipEnterAnimation == true;
            var duration = skipAnimation ? QuickFadeInDuration : FadeInDuration;

            // Invisible buttons must not be clickable while loading
            ContentLayer.IsHitTestVisible = true;

            ContentLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            var hideLoading = new DoubleAnimation(LoadingLayer.Opacity, 0.0, QuickFadeInDuration);
            hideLoading.Completed += (_, _) => LoadingLayer.Visibility = Visibility.Collapsed;
            LoadingLayer.BeginAnimation(OpacityProperty, hideLoading);
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

        /// <summary>The garage stays after all (the next screen would not open): back in full view and clickable</summary>
        private void CancelFadeOut()
        {
            RootGrid.BeginAnimation(OpacityProperty, null);
            RootGrid.Opacity = 1.0;
            RootGrid.IsHitTestVisible = true;
        }
    }
}
