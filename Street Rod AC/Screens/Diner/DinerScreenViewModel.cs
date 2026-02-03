using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.ChallengeSetup;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace Street_Rod_AC.Screens.Diner
{
    public class DinerScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IOpponentChallengeService _challengeService;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly IAppLogger _logger;

        public RelayCommand GarageCommand { get; }
        public RelayCommand<OpponentDisplayViewModel> SelectOpponentCommand { get; }
        public RelayCommand ChallengeCommand { get; }

        private ObservableCollection<OpponentDisplayViewModel> _opponents;
        public ObservableCollection<OpponentDisplayViewModel> Opponents
        {
            get => _opponents;
            set
            {
                _opponents = value;
                OnPropertyChanged(nameof(Opponents));
            }
        }

        private OpponentDisplayViewModel? _selectedOpponent;
        public OpponentDisplayViewModel? SelectedOpponent
        {
            get => _selectedOpponent;
            set
            {
                _selectedOpponent = value;
                OnPropertyChanged(nameof(SelectedOpponent));
                OnPropertyChanged(nameof(HasSelectedOpponent));
                OnPropertyChanged(nameof(SelectedOpponentDisplay));
            }
        }

        public bool HasSelectedOpponent => SelectedOpponent != null;

        public string SelectedOpponentDisplay => SelectedOpponent != null
            ? $"{SelectedOpponent.Name} \"{SelectedOpponent.Nickname}\""
            : "Select an opponent";

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public DinerScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepository,
            IOpponentChallengeService challengeService,
            IAssettoCorsaLauncher launcher,
            IAssettoCorsaContentService contentService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _challengeService = challengeService;
            _launcher = launcher;
            _contentService = contentService;
            _logger = AppLoggerFactory.CreateLogger("Diner");

            GarageCommand = new RelayCommand(OnGarage);
            SelectOpponentCommand = new RelayCommand<OpponentDisplayViewModel>(OnSelectOpponent);
            ChallengeCommand = new RelayCommand(OnChallenge, CanChallenge);

            _opponents = new ObservableCollection<OpponentDisplayViewModel>();

            LoadOpponents();
        }

        private void LoadOpponents()
        {
            Opponents.Clear();

            // Get opponents from the ReadyToRace collection
            var readyOpponents = _gameState.Racers.ReadyToRace.Values
                .OfType<Opponent>()
                .ToList();

            if (readyOpponents.Count == 0)
            {
                _logger.Information("No opponents available at diner");
                OnPropertyChanged(nameof(BankrollDisplay));
                return;
            }

            _logger.Information("Loading {Count} opponents", readyOpponents.Count);

            foreach (var opponent in readyOpponents)
            {
                // Get opponent's car
                var opponentCar = opponent.Cars.FirstOrDefault();
                if (opponentCar == null)
                {
                    _logger.Warning("Opponent {Name} has no cars", opponent.Name);
                    continue;
                }

                // Get car definition
                var carDef = _catalogRepository.GetCar(opponentCar.DefinitionId);
                if (carDef == null)
                {
                    _logger.Warning("Car definition not found for opponent {Name}", opponent.Name);
                    continue;
                }

                // Get portrait path and resolve to absolute path
                var portraitPath = opponent.PortraitPath;
                if (!string.IsNullOrEmpty(portraitPath))
                {
                    // Remove leading slash if present and make it relative to app directory
                    var relativePath = portraitPath.TrimStart('/', '\\');
                    portraitPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

                    if (!File.Exists(portraitPath))
                    {
                        _logger.Warning("Portrait not found for opponent {Name}: {Path}", opponent.Name, portraitPath);
                        portraitPath = null;
                    }
                }

                // Calculate difficulty relative to player
                var difficulty = CalculateDifficulty(opponent);

                var displayVm = new OpponentDisplayViewModel
                {
                    Opponent = opponent,
                    CarDefinition = carDef,
                    PortraitPath = portraitPath,
                    Difficulty = difficulty
                };

                Opponents.Add(displayVm);
            }

            _logger.Information("Loaded {Count} opponents into diner view", Opponents.Count);

            // Auto-select first opponent if available
            if (Opponents.Count > 0)
            {
                SelectedOpponent = Opponents[0];
            }

            // Update bankroll display
            OnPropertyChanged(nameof(BankrollDisplay));
        }

        private OpponentDifficulty CalculateDifficulty(Opponent opponent)
        {
            var reputationDiff = opponent.Stats.Reputation - _gameState.Player.Stats.Reputation;

            if (reputationDiff < -15)
                return OpponentDifficulty.Easy;
            else if (reputationDiff > 15)
                return OpponentDifficulty.Hard;
            else
                return OpponentDifficulty.Matched;
        }

        private void OnSelectOpponent(OpponentDisplayViewModel? opponentVm)
        {
            if (opponentVm != null)
            {
                SelectedOpponent = opponentVm;
                _logger.Information("Selected opponent: {Name}", opponentVm.Name);
            }
        }

        private bool CanChallenge()
        {
            return SelectedOpponent != null &&
                   _gameState.Player.Cars.Count > 0 &&
                   _gameState.Player.SelectedCarInstanceId != null;
        }

        private void OnChallenge()
        {
            if (SelectedOpponent == null)
            {
                _logger.Warning("Cannot challenge - no opponent selected");
                return;
            }

            // Get player's selected car
            var playerCar = _gameState.Player.Cars.FirstOrDefault(c =>
                c.InstanceId == _gameState.Player.SelectedCarInstanceId);

            if (playerCar == null)
            {
                var errorDialog = new InformationDialogViewModel(
                    _dialogService,
                    "You need to select a car in your garage first!",
                    "No Car Selected");
                _dialogService.ShowDialog(errorDialog);
                return;
            }

            // Get player car definition
            var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
            if (playerCarDef == null)
            {
                _logger.Error("Player car definition not found");
                return;
            }

            // Get opponent's car
            var opponentCar = SelectedOpponent.Opponent.Cars.FirstOrDefault();
            if (opponentCar == null)
            {
                _logger.Error("Opponent has no cars");
                return;
            }

            // Show challenge setup dialog
            var challengeDialog = new ChallengeSetupDialogViewModel(
                _dialogService,
                _contentService,
                _gameState.Player,
                SelectedOpponent.Opponent,
                playerCar,
                opponentCar,
                playerCarDef,
                SelectedOpponent.CarDefinition,
                OnChallengeSetupComplete);

            _dialogService.ShowDialog(challengeDialog);
        }

        private async void OnChallengeSetupComplete(ChallengeSetup? setup)
        {
            if (setup == null)
            {
                _logger.Information("Challenge setup cancelled");
                return;
            }

            _logger.Information("Challenge setup complete - evaluating opponent response");

            // Get player car definition
            var playerCarDef = _catalogRepository.GetCar(setup.PlayerCar.DefinitionId);
            if (playerCarDef == null)
            {
                _logger.Error("Player car definition not found");
                return;
            }

            // Evaluate whether opponent accepts
            var response = _challengeService.EvaluateChallenge(
                setup.Opponent,
                _gameState.Player,
                setup.PlayerCar,
                setup.OpponentCar,
                setup.IsPinkSlip,
                setup.CashWager);

            if (!response.Accepted)
            {
                // Show decline message
                var declineDialog = new InformationDialogViewModel(
                    _dialogService,
                    $"{setup.Opponent.Name} declined your challenge:\n\n\"{response.Message}\"",
                    "Challenge Declined");
                _dialogService.ShowDialog(declineDialog);
                return;
            }

            // Opponent accepted - launch race directly
            _logger.Information("Opponent accepted challenge - launching race directly");

            var opponentCarDef = _catalogRepository.GetCar(setup.OpponentCar.DefinitionId);
            if (opponentCarDef == null)
            {
                _logger.Error("Opponent car definition not found");
                return;
            }

            // Launch the race immediately
            await LaunchRace(setup, playerCarDef, opponentCarDef);
        }

        private Task LaunchRace(ChallengeSetup setup, CarDefinition playerCarDef, CarDefinition opponentCarDef)
        {
            _logger.Information("LaunchRace called - Player: {PlayerCar}, Opponent: {OpponentName} in {OpponentCar}, RaceType: {RaceType}",
                playerCarDef.Id, setup.Opponent.Name, opponentCarDef.Id, setup.RaceType);

            // Create drag race launch intent
            _logger.Debug("Creating DragRaceLaunchIntent with Track: {TrackId}, Config: {TrackConfig}",
                setup.TrackId, setup.TrackConfig ?? "(none)");
            var dragRaceIntent = new DragRaceLaunchIntent
            {
                PlayerCarId = playerCarDef.Id,
                PlayerSkin = setup.PlayerCar.SkinId,
                PlayerName = _gameState.Player.Name,
                OpponentCarId = opponentCarDef.Id,
                OpponentSkin = setup.OpponentCar.SkinId,
                OpponentName = setup.Opponent.Name,
                TrackId = setup.TrackId,
                TrackConfig = setup.TrackConfig,
                PlayerCarInstanceId = setup.PlayerCar.InstanceId,
                OpponentCarInstanceId = setup.OpponentCar.InstanceId,
                CashWager = setup.CashWager,
                IsPinkSlip = setup.IsPinkSlip,
                OpponentAILevel = setup.Opponent.Skill,
                OpponentAIAggression = setup.Opponent.Aggression
            };

            // Create race context and store in metadata
            var raceContext = new Street_Rod_AC.Models.Race.RaceContext
            {
                PlayerName = _gameState.Player.Name,
                OpponentName = setup.Opponent.Name,
                PlayerCarInstanceId = setup.PlayerCar.InstanceId,
                OpponentCarInstanceId = setup.OpponentCar.InstanceId,
                CashWager = setup.CashWager,
                IsPinkSlip = setup.IsPinkSlip,
                TrackId = setup.TrackId,
                TrackConfig = setup.TrackConfig,
                RaceType = setup.RaceType
            };

            dragRaceIntent.Metadata["RaceContext"] = raceContext;

            // Navigate to loading screen (it will launch the race and return when done)
            _logger.Information("Navigating to race loading screen");
            _navigationService.NavigateToRaceLoading(_gameState, dragRaceIntent);

            return Task.CompletedTask;
        }

        private void OnGarage()
        {
            _logger.Information("Navigating to garage");
            _navigationService.NavigateToGarage(_gameState);
        }

        private void OnRaceCompleted(object? sender, EventArgs e)
        {
            _logger.Information("Race completed - refreshing diner display");

            // Spend time for the drag race (30 min)
            // TODO: Support road races (1 hour) when implemented
            _ = ((App)System.Windows.Application.Current).SpendTimeAsync(GameAction.DragRace);

            LoadOpponents();
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered diner screen");

            // Spend time for visiting the diner (30 min)
            _ = ((App)System.Windows.Application.Current).SpendTimeAsync(GameAction.VisitDiner);

            // Subscribe to race completion events when screen becomes active
            _launcher.RaceCompleted += OnRaceCompleted;

            // Refresh the diner view (in case state changed while screen was inactive)
            LoadOpponents();
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited diner screen");

            // Unsubscribe from race completion events when screen becomes inactive
            _launcher.RaceCompleted -= OnRaceCompleted;
        }
    }

    /// <summary>
    /// View model for displaying an opponent in the list
    /// </summary>
    public class OpponentDisplayViewModel
    {
        public Opponent Opponent { get; set; } = new();
        public CarDefinition CarDefinition { get; set; } = new();
        public string? PortraitPath { get; set; }
        public OpponentDifficulty Difficulty { get; set; }

        public string Name => Opponent.Name;
        public string Nickname => Opponent.Nickname;
        public string CarDisplay => $"{CarDefinition.Brand} {CarDefinition.Name}";
        public int Reputation => Opponent.Stats.Reputation;
        public string ReputationTier => Opponent.Stats.GetReputationTier();
        public string RecordDisplay => $"{Opponent.Stats.Wins}W - {Opponent.Stats.Losses}L";
        public bool HasPortrait => !string.IsNullOrEmpty(PortraitPath);

        // Difficulty color
        public string DifficultyColor => Difficulty switch
        {
            OpponentDifficulty.Easy => "#90EE90",    // Light green
            OpponentDifficulty.Matched => "#FFD700", // Gold
            OpponentDifficulty.Hard => "#FF6B6B",    // Light red
            _ => "#FFFFFF"
        };

        public string DifficultyBorderColor => Difficulty switch
        {
            OpponentDifficulty.Easy => "#00AA00",    // Green
            OpponentDifficulty.Matched => "#FFA500", // Orange
            OpponentDifficulty.Hard => "#CC0000",    // Red
            _ => "#404040"
        };
    }

    public enum OpponentDifficulty
    {
        Easy,
        Matched,
        Hard
    }
}
