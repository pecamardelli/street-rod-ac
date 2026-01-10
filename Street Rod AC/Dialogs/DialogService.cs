using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Street_Rod_AC.Dialogs
{
    public class DialogService : INotifyPropertyChanged
    {
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ShowDialog(IDialog dialog)
        {
            if (CurrentDialog != null)
            {
                // Close current dialog before opening new one
                CloseDialog();
            }

            CurrentDialog = dialog;
            CurrentDialog?.OnOpened();
        }

        public void CloseDialog()
        {
            if (CurrentDialog != null)
            {
                CurrentDialog.OnClosed();
                CurrentDialog = null;
            }
        }
    }
}
