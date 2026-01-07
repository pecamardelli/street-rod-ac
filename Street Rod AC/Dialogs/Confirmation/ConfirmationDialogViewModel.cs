using Street_Rod_AC.ViewModels;
using System;

namespace Street_Rod_AC.Dialogs.Confirmation
{
    public class ConfirmationDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Action<bool> _resultCallback;
        private string _title;
        private string _message;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public RelayCommand YesCommand { get; }
        public RelayCommand NoCommand { get; }

        public ConfirmationDialogViewModel(
            DialogService dialogService,
            string message,
            string title = "Confirm",
            Action<bool>? resultCallback = null)
        {
            _dialogService = dialogService;
            _resultCallback = resultCallback ?? (_ => { });
            _title = title;
            _message = message;

            YesCommand = new RelayCommand(OnYes);
            NoCommand = new RelayCommand(OnNo);
        }

        private void OnYes()
        {
            _resultCallback(true);
            _dialogService.CloseDialog();
        }

        private void OnNo()
        {
            _resultCallback(false);
            _dialogService.CloseDialog();
        }
    }
}
