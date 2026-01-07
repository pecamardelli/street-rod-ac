using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Init
{
    public class InitScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;

        public RelayCommand ProceedCommand { get; }

        public InitScreenViewModel(NavigationService navigationService)
        {
            _navigationService = navigationService;
            ProceedCommand = new RelayCommand(OnProceed);
        }

        private void OnProceed()
        {
            var mainMenuViewModel = new MainMenu.MainMenuScreenViewModel(_navigationService);
            _navigationService.NavigateTo(mainMenuViewModel);
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
