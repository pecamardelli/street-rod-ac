using Street_Rod_AC.ViewModels;
using System;

namespace Street_Rod_AC.Dialogs.Information
{
    public class InformationDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Action? _okCallback;
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

        public RelayCommand OkCommand { get; }

        public InformationDialogViewModel(
            DialogService dialogService,
            string message,
            string title = "Information",
            Action? okCallback = null)
        {
            _dialogService = dialogService;
            _okCallback = okCallback;
            _title = title;
            _message = message;

            OkCommand = new RelayCommand(OnOk);
        }

        private void OnOk()
        {
            _okCallback?.Invoke();
            _dialogService.CloseDialog();
        }
    }
}
