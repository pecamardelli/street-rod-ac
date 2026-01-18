using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Settings;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Settings
{
    public class SettingsScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameSettingsService _settingsService;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand ResetCommand { get; }

        public GameSettings Settings => _settingsService.Current;

        public SettingsScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameSettingsService settingsService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _settingsService = settingsService;
            _logger = AppLoggerFactory.CreateLogger("Settings");

            BackCommand = new RelayCommand(OnBack);
            SaveCommand = new RelayCommand(OnSave);
            ResetCommand = new RelayCommand(OnReset);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered settings screen");
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to main menu");
            _navigationService.NavigateToMainMenu();
        }

        private void OnSave()
        {
            _settingsService.Save();
            _logger.Information("Settings saved");
        }

        private void OnReset()
        {
            _settingsService.ResetToDefaults();
            OnPropertyChanged(nameof(Settings));
            _logger.Information("Settings reset to defaults");
        }
    }
}
