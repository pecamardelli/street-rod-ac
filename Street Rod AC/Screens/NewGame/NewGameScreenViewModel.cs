using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.NewGame
{
    public class NewGameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly IGameStateRepository _repository;
        private readonly IRaceEventService _raceEventService;
        private readonly Action<GameState?> _setCurrentGame;
        private readonly IAppLogger _logger;
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

        /// <summary>The difficulty the career starts on</summary>
        public GameRulesEditor Rules { get; } = new();

        public RelayCommand StartGameCommand { get; }
        public RelayCommand BackCommand { get; }

        /// <summary>A save name the catalog uses for itself; a player with that name gets a different file</summary>
        private const string ReservedSaveName = "catalog";

        /// <param name="setCurrentGame">Makes the new game the one the app saves on exit and races with</param>
        public NewGameScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IGameStateRepository repository,
            IRaceEventService raceEventService,
            Action<GameState?> setCurrentGame)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _repository = repository;
            _raceEventService = raceEventService;
            _setCurrentGame = setCurrentGame;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

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
            // Use player name as save file name (sanitize for filename)
            var saveName = SaveNameFor(playerName);

            bool exists;
            try
            {
                exists = _repository.Exists(saveName);
            }
            catch (Exception ex)
            {
                ReportFailure(ex, saveName);
                return;
            }

            if (!exists)
            {
                StartGame(saveName, playerName);
                return;
            }

            // Show confirmation dialog
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                $"A save file with the name '{saveName}' already exists. Do you want to overwrite it?",
                "Overwrite Save?",
                confirmed =>
                {
                    // Create new save, overwriting the existing one
                    if (confirmed) StartGame(saveName, playerName);
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        /// <summary>
        /// Creates the save, its first race events, and goes to the garage. The save is a file write: a locked
        /// or read-only saves folder is told to the player, and they stay on this screen.
        /// </summary>
        private void StartGame(string saveName, string playerName)
        {
            GameState gameState;
            try
            {
                gameState = _repository.CreateNew(saveName, playerName, Rules.Build());

                // Generate initial race events
                _raceEventService.GenerateEvents(gameState.Career, gameState.Date, gameState.Rules.PinkSlipFactor);
            }
            catch (Exception ex)
            {
                ReportFailure(ex, saveName);
                return;
            }

            // Store current game state in App for saving on exit
            _setCurrentGame(gameState);

            // Navigate to game screen
            _navigationService.NavigateToGarage(gameState);
        }

        private void ReportFailure(Exception ex, string saveName)
        {
            _logger.Error(ex, "Could not create save {SaveName}", saveName);
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                $"The new game could not be set up:\n\n{ex.Message}",
                "New Game Failed"));
        }

        /// <summary>
        /// The player's name as a save file name: one safe file name (<see cref="PathNames.Sanitize"/>), never the
        /// name the catalog database uses.
        /// </summary>
        public static string SaveNameFor(string playerName)
        {
            var saveName = PathNames.Sanitize(playerName, maxLength: 50, fallback: "player");
            return string.Equals(saveName, ReservedSaveName, StringComparison.OrdinalIgnoreCase)
                ? saveName + "_player"
                : saveName;
        }

        private void OnBack()
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
