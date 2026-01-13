using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Init
{
    public class InitScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;

        public RelayCommand ProceedCommand { get; }

        public InitScreenViewModel(NavigationService navigationService, DialogService dialogService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            ProceedCommand = new RelayCommand(OnProceed);
        }

        private void OnProceed()
        {
            _navigationService.NavigateToMainMenu();
        }

        public override void Enter()
        {
            base.Enter();
        }

        public override void Exit()
        {
            base.Exit();
        }
    }
}
