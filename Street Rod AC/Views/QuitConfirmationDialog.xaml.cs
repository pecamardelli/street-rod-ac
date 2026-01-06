using System.ComponentModel;
using System.Windows;
using Street_Rod_AC.Controls;

namespace Street_Rod_AC.Views;

public partial class QuitConfirmationDialog : BaseWindow
{
    private bool? _dialogResultToSet = null;

    public QuitConfirmationDialog()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Set DialogResult before the fade-out animation if a button was clicked
        if (_dialogResultToSet.HasValue && DialogResult != _dialogResultToSet.Value)
        {
            DialogResult = _dialogResultToSet.Value;
        }

        base.OnClosing(e);
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        _dialogResultToSet = true;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        _dialogResultToSet = false;
        Close();
    }
}
