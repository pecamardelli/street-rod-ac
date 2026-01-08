using System.Windows;

namespace Street_Rod_AC.Screens.NewGame
{
    public partial class NewGameScreenView : System.Windows.Controls.UserControl
    {
        public NewGameScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Auto-focus the player name TextBox
            PlayerNameTextBox.Focus();
            PlayerNameTextBox.SelectAll();
        }
    }
}
