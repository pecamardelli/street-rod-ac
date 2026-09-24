using System.Windows.Input;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.ViewModels;

/// <summary>
/// A simple implementation of ICommand for async operations in ViewModels.
///
/// WPF runs a command as a fire-and-forget call, so an exception out of the task would reach the dispatcher
/// and end the game. Execute catches it: it is logged, and handed to <c>onError</c> when the owner wants to
/// tell the player.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isExecuting;

    /// <param name="onError">Called on the UI thread with whatever the command threw, after it is logged</param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute == null || _canExecute());
    }

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.UI);
            logger.Error(ex, "A command failed: {Command}", _execute.Method.Name);

            try
            {
                _onError?.Invoke(ex);
            }
            catch (Exception inner)
            {
                logger.Error(inner, "The error handler of {Command} failed as well", _execute.Method.Name);
            }
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}
