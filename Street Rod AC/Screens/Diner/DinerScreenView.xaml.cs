namespace Street_Rod_AC.Screens.Diner
{
    public partial class DinerScreenView : System.Windows.Controls.UserControl
    {
        public DinerScreenView()
        {
            InitializeComponent();
        }

        private void CashBetBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is DinerScreenViewModel vm)
            {
                vm.IsCashBet = true;
            }
        }

        private void PinkSlipBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is DinerScreenViewModel vm)
            {
                vm.IsPinkSlipBet = true;
            }
        }
    }
}
