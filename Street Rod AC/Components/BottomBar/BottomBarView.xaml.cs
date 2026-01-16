using System.Windows;

namespace Street_Rod_AC.Components.BottomBar
{
    /// <summary>
    /// Reusable bottom bar component that displays bankroll information
    /// </summary>
    public partial class BottomBarView : System.Windows.Controls.UserControl
    {
        public static readonly DependencyProperty BankrollDisplayProperty =
            DependencyProperty.Register(
                nameof(BankrollDisplay),
                typeof(string),
                typeof(BottomBarView),
                new PropertyMetadata(string.Empty));

        /// <summary>
        /// The formatted bankroll text to display (e.g., "$1,000")
        /// </summary>
        public string BankrollDisplay
        {
            get => (string)GetValue(BankrollDisplayProperty);
            set => SetValue(BankrollDisplayProperty, value);
        }

        public BottomBarView()
        {
            InitializeComponent();
        }
    }
}
