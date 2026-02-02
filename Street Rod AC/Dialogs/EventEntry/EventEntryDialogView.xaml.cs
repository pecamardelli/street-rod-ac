using System.Windows;
using System.Windows.Input;
using Street_Rod_AC.Screens.Newspaper;

namespace Street_Rod_AC.Dialogs.EventEntry
{
    /// <summary>
    /// Interaction logic for EventEntryDialogView.xaml
    /// </summary>
    public partial class EventEntryDialogView : System.Windows.Controls.UserControl
    {
        public EventEntryDialogView()
        {
            InitializeComponent();
        }

        private void OnCarSelected(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.DataContext is EligibleCarViewModel car &&
                DataContext is EventEntryDialogViewModel viewModel)
            {
                // Deselect all cars
                foreach (var c in viewModel.EligibleCars)
                {
                    c.IsSelected = false;
                }

                // Select the clicked car
                car.IsSelected = true;
                viewModel.SelectedCar = car;
            }
        }
    }
}
