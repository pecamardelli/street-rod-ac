using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs
{
    /// <summary>
    /// The one modal overlay of the main window. One dialog shows at a time; a dialog asked for while another
    /// is up waits its turn and shows when the one in front is closed, so no message is ever lost by being
    /// replaced before the player could read it (a "Save Warning" followed at once by "Purchase Successful").
    ///
    /// The same question is never asked twice at once: a dialog whose <see cref="IDialog.DuplicateKey"/> matches
    /// the one in front or one already waiting is dropped (each click on the window's X during a race would
    /// otherwise stack another "Quit During a Race"). A question that must be answered now, over whatever is up,
    /// jumps the queue; the dialog it covers comes back when it is answered.
    /// </summary>
    public class DialogService : ObservableObject
    {
        // A list, not a queue: a dialog that is jumped goes back to the front, and one can be withdrawn
        private readonly List<IDialog> _waiting = new();
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
        public void ShowDialog(IDialog dialog) => ShowDialog(dialog, jumpQueue: false);

        /// <summary>
        /// Shows the dialog now, or after the ones already waiting when a dialog is up. With
        /// <paramref name="jumpQueue"/> it shows at once even then: the dialog in front steps back to the head
        /// of the queue (not closed, its callback still to come) and shows again once this one is answered.
        /// A dialog already up or waiting with the same <see cref="IDialog.DuplicateKey"/> is not shown twice.
        /// </summary>
        public void ShowDialog(IDialog dialog, bool jumpQueue)
        {
            if (IsAlreadyAsked(dialog))
                return;

            if (CurrentDialog == null)
            {
                Open(dialog);
                return;
            }

            if (!jumpQueue)
            {
                _waiting.Add(dialog);
                return;
            }

            _waiting.Insert(0, CurrentDialog);
            Open(dialog);
        }

        /// <summary>
        /// Takes a dialog away whether it is up or still waiting, e.g. a question the game has since answered
        /// itself ("Stop Race" once the race is over). Nothing happens for one that is already gone.
        /// </summary>
        public void Withdraw(IDialog dialog)
        {
            if (ReferenceEquals(CurrentDialog, dialog))
            {
                CloseDialog();
                return;
            }

            _waiting.Remove(dialog);
        }

        private void Open(IDialog dialog)
        {
            CurrentDialog = dialog;
            dialog.OnOpened();
        }

        private bool IsAlreadyAsked(IDialog dialog)
        {
            if (ReferenceEquals(CurrentDialog, dialog) || _waiting.Contains(dialog))
                return true;

            var key = dialog.DuplicateKey;
            if (key == null)
                return false;

            return IsSame(CurrentDialog) || _waiting.Any(IsSame);

            bool IsSame(IDialog? other) =>
                other != null && other.GetType() == dialog.GetType() && string.Equals(other.DuplicateKey, key, StringComparison.Ordinal);
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
                var next = _waiting[0];
                _waiting.RemoveAt(0);
                Open(next);
            }
        }
    }
}
