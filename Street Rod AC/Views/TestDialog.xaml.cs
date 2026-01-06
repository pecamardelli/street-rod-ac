using System.Windows;
using Street_Rod_AC.Controls;

namespace Street_Rod_AC.Views;

public partial class TestDialog : BaseWindow
{
    public TestDialog()
    {
        InitializeComponent();
    }

    private void CloseDialogButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
