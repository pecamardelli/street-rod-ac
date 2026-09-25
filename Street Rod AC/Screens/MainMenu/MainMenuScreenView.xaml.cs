using System.ComponentModel;
using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Street_Rod_AC.Screens.MainMenu
{
    /// <summary>
    /// Plays the main screen's changes: the menu and the cards take turns in the same place, one fading out before the
    /// other drops in. The screen starts black and the showroom fades up in it; the old picture only comes up when
    /// there is no showroom to show. Esc closes the open card.
    /// </summary>
    public partial class MainMenuScreenView : UserControl
    {
        private static readonly Duration OutDuration = new(TimeSpan.FromMilliseconds(180));
        private static readonly Duration InDuration = new(TimeSpan.FromMilliseconds(380));
        private static readonly Duration FallbackFade = new(TimeSpan.FromMilliseconds(900));
        // How far a panel travels as it comes and goes: it drops in from above and lifts away
        private const double Travel = 24;

        private MainMenuScreenViewModel? _viewModel;
        private Window? _window;

        /// <summary>Counts switches, so a slow fade-out does not finish a switch that a later one replaced</summary>
        private int _switch;

        public MainMenuScreenView()
        {
            InitializeComponent();

            DataContextChanged += (_, e) =>
            {
                if (IsLoaded) Attach(e.NewValue as MainMenuScreenViewModel);
            };
            Loaded += (_, _) =>
            {
                Attach(DataContext as MainMenuScreenViewModel);
                ShowPanel(_viewModel?.CurrentCard);

                // Esc from the window: once a menu badge is clicked it is hidden, and the focus leaves this screen
                _window = Window.GetWindow(this);
                if (_window != null)
                {
                    _window.PreviewKeyDown -= OnWindowKeyDown;
                    _window.PreviewKeyDown += OnWindowKeyDown;
                }
            };
            Unloaded += (_, _) =>
            {
                Attach(null);
                if (_window != null) _window.PreviewKeyDown -= OnWindowKeyDown;
                _window = null;
            };

            // No showroom to show at all: the old picture rather than a black screen
            Showcase.Failed += (_, _) => ShowFallback();
        }

        private void Attach(MainMenuScreenViewModel? viewModel)
        {
            if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = viewModel;
            if (_viewModel == null) return;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Found to have nothing to show before this view was attached
            if (_viewModel.NothingToShow) ShowFallback();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || !IsLoaded || _viewModel?.CloseCardCommand is not { } close) return;
            if (!close.CanExecute(null)) return;

            close.Execute(null);
            e.Handled = true;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainMenuScreenViewModel.CurrentCard)) SwitchTo(_viewModel?.CurrentCard);
            if (e.PropertyName == nameof(MainMenuScreenViewModel.NothingToShow) && _viewModel?.NothingToShow == true) ShowFallback();
        }

        private void ShowFallback() => Fallback.BeginAnimation(OpacityProperty, new DoubleAnimation(1, FallbackFade));

        private void SwitchTo(object? card)
        {
            var id = ++_switch;
            var outgoing = CardPanel.Visibility == Visibility.Visible ? (FrameworkElement)CardPanel : MenuPanel;

            // No second click lands while the first is still playing out
            outgoing.IsHitTestVisible = false;

            var fade = new DoubleAnimation(0, OutDuration);
            fade.Completed += (_, _) =>
            {
                if (id != _switch) return;
                outgoing.Visibility = Visibility.Collapsed;
                ShowPanel(card);
            };
            outgoing.BeginAnimation(OpacityProperty, fade);
            Translate(outgoing).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-Travel / 2, OutDuration));
        }

        /// <summary>Brings up the card, or the menu when there is none</summary>
        private void ShowPanel(object? card)
        {
            CardHost.Content = card;

            FrameworkElement incoming = card != null ? CardPanel : MenuPanel;
            FrameworkElement other = card != null ? MenuPanel : CardPanel;

            other.BeginAnimation(OpacityProperty, null);
            other.Opacity = 0;
            other.Visibility = Visibility.Collapsed;

            incoming.Visibility = Visibility.Visible;
            incoming.IsHitTestVisible = true;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, InDuration) { EasingFunction = ease });
            Translate(incoming).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-Travel, 0, InDuration) { EasingFunction = ease });
        }

        private static TranslateTransform Translate(UIElement element)
        {
            if (element.RenderTransform is TranslateTransform { IsFrozen: false } translate) return translate;

            translate = new TranslateTransform();
            element.RenderTransform = translate;
            return translate;
        }
    }
}
