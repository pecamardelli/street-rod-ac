using System.Windows.Input;

namespace Street_Rod_AC.ViewModels;

/// <summary>
/// A generic implementation of ICommand for use in ViewModels with command parameters
/// </summary>
public class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    private readonly Action<T?> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<T?, bool>? _canExecute = canExecute;

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute == null || _canExecute((T?)parameter);
    }

    public void Execute(object? parameter)
    {
        _execute((T?)parameter);
    }

    public static void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}
