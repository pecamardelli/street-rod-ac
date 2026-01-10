using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.NewGame
{
    public class NewGameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private string _playerName = string.Empty;
        private string _errorMessage = string.Empty;

        public string PlayerName
        {
            get => _playerName;
            set
            {
                if (SetProperty(ref _playerName, value))
                {
                    ErrorMessage = string.Empty;
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public RelayCommand StartGameCommand { get; }
        public RelayCommand BackCommand { get; }

        public NewGameScreenViewModel(NavigationService navigationService, DialogService dialogService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;

            StartGameCommand = new RelayCommand(OnStartGame);
            BackCommand = new RelayCommand(OnBack);
        }

        private void OnStartGame()
        {
            var trimmedName = PlayerName.Trim();

            // Validate player name
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                ErrorMessage = "Please enter your name";
                return;
            }

            if (trimmedName.Length > 50)
            {
                ErrorMessage = "Name is too long (max 50 characters)";
                return;
            }

            CreateNewGame(trimmedName);
        }

        private void CreateNewGame(string playerName)
        {
            var app = (App)System.Windows.Application.Current;
            var repository = app.GameStateRepository;

            // Use player name as save file name (sanitize for filename)
            var saveName = SanitizeSaveName(playerName);

            // Check if save already exists
            if (repository.Exists(saveName))
            {
                // Show confirmation dialog
                var confirmDialog = new ConfirmationDialogViewModel(
                    _dialogService,
                    $"A save file with the name '{saveName}' already exists. Do you want to overwrite it?",
                    "Overwrite Save?",
                    confirmed =>
                    {
                        if (confirmed)
                        {
                            // Create new save, overwriting the existing one
                            var gameState = repository.CreateNew(saveName, playerName);
                            // Navigate to game screen
                            var theApp = (App)System.Windows.Application.Current;
                            var gameViewModel = new Game.GameScreenViewModel(
                                _navigationService,
                                _dialogService,
                                gameState,
                                theApp.CatalogRepository);
                            _navigationService.NavigateTo(gameViewModel);
                        }
                    });

                _dialogService.ShowDialog(confirmDialog);
            }
            else
            {
                // Create new save
                var gameState = repository.CreateNew(saveName, playerName);
                // Navigate to game screen
                var gameViewModel = new Game.GameScreenViewModel(
                    _navigationService,
                    _dialogService,
                    gameState,
                    app.CatalogRepository);
                _navigationService.NavigateTo(gameViewModel);
            }
        }

        private string SanitizeSaveName(string playerName)
        {
            // Remove invalid filename characters
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", playerName.Split(invalidChars));

            // Limit length and trim
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);

            return sanitized.Trim();
        }

        private void OnBack()
        {
            var mainMenuViewModel = new MainMenu.MainMenuScreenViewModel(_navigationService, _dialogService);
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
