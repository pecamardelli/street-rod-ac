using System.Windows;
using System.Windows.Input;

namespace Street_Rod_AC.Screens.Cruise
{
    /// <summary>
    /// The street from the driver's seat. The Rev button and the space bar hold the player's throttle down for as
    /// long as they are held, which a command cannot say.
    /// </summary>
    public partial class CruiseScreenView : System.Windows.Controls.UserControl
    {
        private Window? _window;

        public CruiseScreenView()
        {
            InitializeComponent();

            // The window's keys, as the garage takes them: a button that was clicked keeps the focus otherwise
            Loaded += (_, _) =>
            {
                _window = Window.GetWindow(this);
                if (_window == null) return;
                _window.PreviewKeyDown += OnWindowKeyDown;
                _window.PreviewKeyUp += OnWindowKeyUp;
            };
            Unloaded += (_, _) =>
            {
                if (_window == null) return;
                _window.PreviewKeyDown -= OnWindowKeyDown;
                _window.PreviewKeyUp -= OnWindowKeyUp;
                _window = null;
            };
        }

        private CruiseScreenViewModel? ViewModel => DataContext as CruiseScreenViewModel;

        private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Space || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
            ViewModel?.Rev(true);
            e.Handled = true;
        }

        private void OnWindowKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Space) return;
            ViewModel?.Rev(false);
            e.Handled = true;
        }

        private void RevButton_Down(object sender, MouseButtonEventArgs e) => ViewModel?.Rev(true);

        private void RevButton_Up(object sender, MouseButtonEventArgs e) => ViewModel?.Rev(false);

        private void RevButton_Leave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) ViewModel?.Rev(false);
        }
    }
}
