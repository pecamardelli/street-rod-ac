using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs
{
    /// <summary>
    /// The one modal overlay of the main window. One dialog shows at a time; a dialog asked for while another
    /// is up waits its turn and shows when the one in front is closed, so no message is ever lost by being
    /// replaced before the player could read it (a "Save Warning" followed at once by "Purchase Successful").
    /// </summary>
    public class DialogService : ObservableObject
    {
        private readonly Queue<IDialog> _waiting = new();
        private IDialog? _currentDialog;

        public IDialog? CurrentDialog
        {
            get => _currentDialog;
            private set
            {
                if (_currentDialog != value)
                {
                    _currentDialog = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDialogOpen));
                }
            }
        }

        public bool IsDialogOpen => CurrentDialog != null;

        /// <summary>Shows the dialog now, or after the ones already waiting when a dialog is up</summary>
        public void ShowDialog(IDialog dialog)
        {
            if (CurrentDialog != null)
            {
                _waiting.Enqueue(dialog);
                return;
            }

            CurrentDialog = dialog;
            dialog.OnOpened();
        }

        /// <summary>Closes the dialog in front; the next one waiting, if any, shows in its place</summary>
        public void CloseDialog()
        {
            if (CurrentDialog == null)
                return;

            var closing = CurrentDialog;
            CurrentDialog = null;
            closing.OnClosed();

            if (_waiting.Count > 0)
            {
                ShowDialog(_waiting.Dequeue());
            }
        }
    }
}
