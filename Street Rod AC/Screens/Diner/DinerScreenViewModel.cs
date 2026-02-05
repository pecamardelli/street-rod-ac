using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.ChallengeSetup; // For ChallengeSetup model
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Talk;
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
        private readonly ITalkService _talkService;
        private readonly IAppLogger _logger;

        public RelayCommand GarageCommand { get; }
        public RelayCommand<OpponentDisplayViewModel> SelectOpponentCommand { get; }
        public RelayCommand<TrackCardViewModel> SelectTrackCommand { get; }
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

                // Update matchup stats when opponent changes
                UpdateMatchupStats();
            }
        }

        public bool HasSelectedOpponent => SelectedOpponent != null;

        public string SelectedOpponentDisplay => SelectedOpponent != null
            ? $"{SelectedOpponent.Name} \"{SelectedOpponent.Nickname}\""
            : "Select an opponent";

        private string _opponentMessage = "";
        public string OpponentMessage
        {
            get => _opponentMessage;
            set
            {
                _opponentMessage = value;
                OnPropertyChanged(nameof(OpponentMessage));
                OnPropertyChanged(nameof(HasOpponentMessage));
            }
        }

        public bool HasOpponentMessage => !string.IsNullOrEmpty(OpponentMessage);

        // Track collections
        public ObservableCollection<TrackCardViewModel> DragTracks { get; } = new();
        public ObservableCollection<TrackCardViewModel> RoadTracks { get; } = new();

        public bool HasDragTracks => DragTracks.Count > 0;
        public bool HasRoadTracks => RoadTracks.Count > 0;
        public bool HasAnyTracks => HasDragTracks || HasRoadTracks;
        public bool HasOpponents => Opponents.Count > 0;

        private TrackCardViewModel? _selectedTrack;
        public TrackCardViewModel? SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                if (_selectedTrack != value)
                {
                    // Deselect previous track
                    if (_selectedTrack != null)
                        _selectedTrack.IsSelected = false;

                    _selectedTrack = value;

                    // Select new track
                    if (_selectedTrack != null)
                        _selectedTrack.IsSelected = true;

                    OnPropertyChanged(nameof(SelectedTrack));
                    OnPropertyChanged(nameof(HasSelectedTrack));
                    OnPropertyChanged(nameof(SelectedRaceType));

                    // Update matchup stats when track changes
                    UpdateMatchupStats();

                    // Update wager limits when track changes (different limits for drag vs road)
                    UpdateWagerLimits();

                    // Update talk message when track changes
                    if (SelectedOpponent != null && _selectedTrack != null)
                    {
                        _ = LoadOpponentMessageAsync(SelectedOpponent, TalkTrigger.TrackSelected);
                    }
                }
            }
        }

        public bool HasSelectedTrack => SelectedTrack != null;

        public RaceType SelectedRaceType => SelectedTrack?.RaceType ?? RaceType.DragRace;

        // Matchup stats
        public ObservableCollection<MatchupStatViewModel> MatchupStats { get; } = new();

        public bool HasMatchupStats => MatchupStats.Count > 0;

        // Bet options
        private bool _isCashBet = true;
        public bool IsCashBet
        {
            get => _isCashBet;
            set
            {
                if (_isCashBet != value)
                {
                    _isCashBet = value;
                    OnPropertyChanged(nameof(IsCashBet));
                    OnPropertyChanged(nameof(IsPinkSlipBet));
                    OnPropertyChanged(nameof(IsCashBetEnabled));
                    UpdateWagerLimits();

                    // Trigger talk message when bet type changes
                    if (SelectedOpponent != null)
                    {
                        _ = LoadOpponentMessageAsync(SelectedOpponent, TalkTrigger.BetTypeChanged);
                    }
                }
            }
        }

        public bool IsPinkSlipBet
        {
            get => !_isCashBet;
            set => IsCashBet = !value;
        }

        public bool IsCashBetEnabled => IsCashBet;

        private decimal _wagerAmount = 50;
        public decimal WagerAmount
        {
            get => _wagerAmount;
            set
            {
                if (_wagerAmount != value)
                {
                    _wagerAmount = value;
                    OnPropertyChanged(nameof(WagerAmount));
                    OnPropertyChanged(nameof(WagerAmountDisplay));
                }
            }
        }

        public string WagerAmountDisplay => $"${WagerAmount:N0}";

        public bool CanAffordMinBet
        {
            get
            {
                if (!IsCashBet) return true; // Pink slip doesn't require cash
                var isDrag = SelectedTrack?.RaceType == RaceType.DragRace;
                var baseMin = isDrag ? 10m : 25m;
                return _gameState.Player.Money >= baseMin;
            }
        }

        public string InsufficientFundsMessage
        {
            get
            {
                var isDrag = SelectedTrack?.RaceType == RaceType.DragRace;
                var baseMin = isDrag ? 10m : 25m;
                return $"You need at least ${baseMin:N0} to place a cash wager.";
            }
        }

        private decimal _minWager = 10;
        public decimal MinWager
        {
            get => _minWager;
            private set
            {
                if (_minWager != value)
                {
                    _minWager = value;
                    OnPropertyChanged(nameof(MinWager));
                }
            }
        }

        private decimal _maxWager = 100;
        public decimal MaxWager
        {
            get => _maxWager;
            private set
            {
                if (_maxWager != value)
                {
                    _maxWager = value;
                    OnPropertyChanged(nameof(MaxWager));
                }
            }
        }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public DinerScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepository,
            IOpponentChallengeService challengeService,
            IAssettoCorsaLauncher launcher,
            IAssettoCorsaContentService contentService,
            ITalkService talkService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _challengeService = challengeService;
            _launcher = launcher;
            _contentService = contentService;
            _talkService = talkService;
            _logger = AppLoggerFactory.CreateLogger("Diner");

            GarageCommand = new RelayCommand(OnGarage);
            SelectOpponentCommand = new RelayCommand<OpponentDisplayViewModel>(OnSelectOpponent);
            SelectTrackCommand = new RelayCommand<TrackCardViewModel>(OnSelectTrack);
            ChallengeCommand = new RelayCommand(OnChallenge, CanChallenge);

            _opponents = new ObservableCollection<OpponentDisplayViewModel>();

            LoadTracks();
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

            // Notify UI about opponents availability
            OnPropertyChanged(nameof(HasOpponents));

            // Auto-select first opponent if available
            if (Opponents.Count > 0)
            {
                OnSelectOpponent(Opponents[0]);
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

                // Update wager limits based on opponent's money
                UpdateWagerLimits();

                // Load opponent dialogue
                _ = LoadOpponentMessageAsync(opponentVm, TalkTrigger.OpponentSelected);
            }
        }

        private async Task LoadOpponentMessageAsync(OpponentDisplayViewModel opponentVm, TalkTrigger trigger)
        {
            try
            {
                // Get player's car for context
                CarDefinition? playerCarDef = null;
                if (_gameState.Player.SelectedCarInstanceId != null)
                {
                    var playerCar = _gameState.Player.Cars.FirstOrDefault(c =>
                        c.InstanceId == _gameState.Player.SelectedCarInstanceId);
                    if (playerCar != null)
                    {
                        playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
                    }
                }

                var context = new TalkContext
                {
                    Opponent = opponentVm.Opponent,
                    Player = _gameState.Player,
                    OpponentCar = opponentVm.CarDefinition,
                    PlayerCar = playerCarDef,
                    Trigger = trigger,
                    SelectedTrackName = SelectedTrack?.DisplayName
                };

                OpponentMessage = await _talkService.GetMessageAsync(context);
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to load opponent message: {Error}", ex.Message);
                OpponentMessage = "";
            }
        }

        private void LoadTracks()
        {
            DragTracks.Clear();
            RoadTracks.Clear();

            var tracks = _contentService.GetTracks();
            _logger.Information("Loading {Count} tracks for diner", tracks.Count);

            foreach (var track in tracks)
            {
                // Determine race type based on track type
                var raceType = track.Type == TrackType.Dragstrip
                    ? RaceType.DragRace
                    : RaceType.Circuit;

                // Get preview path
                var previewPath = _contentService.GetTrackPreviewPath(track.TrackId);

                // If track has configurations, create a card for each
                if (track.Configurations.Count > 0)
                {
                    foreach (var config in track.Configurations)
                    {
                        var trackVm = new TrackCardViewModel
                        {
                            Track = track,
                            Configuration = config,
                            PreviewPath = previewPath,
                            RaceType = raceType
                        };

                        if (raceType == RaceType.DragRace)
                            DragTracks.Add(trackVm);
                        else
                            RoadTracks.Add(trackVm);
                    }
                }
                else
                {
                    // No configurations - create single card for the track
                    var trackVm = new TrackCardViewModel
                    {
                        Track = track,
                        Configuration = null,
                        PreviewPath = previewPath,
                        RaceType = raceType
                    };

                    if (raceType == RaceType.DragRace)
                        DragTracks.Add(trackVm);
                    else
                        RoadTracks.Add(trackVm);
                }
            }

            _logger.Information("Loaded {DragCount} drag tracks and {RoadCount} road tracks",
                DragTracks.Count, RoadTracks.Count);

            // Notify UI
            OnPropertyChanged(nameof(HasDragTracks));
            OnPropertyChanged(nameof(HasRoadTracks));
            OnPropertyChanged(nameof(HasAnyTracks));

            // Auto-select first drag track if available
            if (DragTracks.Count > 0)
            {
                SelectedTrack = DragTracks[0];
            }
            else if (RoadTracks.Count > 0)
            {
                SelectedTrack = RoadTracks[0];
            }
        }

        private void OnSelectTrack(TrackCardViewModel? trackVm)
        {
            if (trackVm != null)
            {
                SelectedTrack = trackVm;
                _logger.Information("Selected track: {TrackName} ({TrackId})",
                    trackVm.DisplayName, trackVm.TrackId);
            }
        }

        private void UpdateMatchupStats()
        {
            MatchupStats.Clear();

            if (SelectedOpponent == null)
            {
                OnPropertyChanged(nameof(HasMatchupStats));
                return;
            }

            // Get player's selected car
            CarDefinition? playerCarDef = null;
            if (_gameState.Player.SelectedCarInstanceId != null)
            {
                var playerCar = _gameState.Player.Cars.FirstOrDefault(c =>
                    c.InstanceId == _gameState.Player.SelectedCarInstanceId);
                if (playerCar != null)
                {
                    playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
                }
            }

            if (playerCarDef == null)
            {
                OnPropertyChanged(nameof(HasMatchupStats));
                return;
            }

            var opponentCarDef = SelectedOpponent.CarDefinition;

            // Parse specs from strings (AC stores these as strings like "320bhp", "1250kg")
            var playerPower = ParseNumericValue(playerCarDef.Specs?.Bhp);
            var opponentPower = ParseNumericValue(opponentCarDef.Specs?.Bhp);
            var playerWeight = ParseNumericValue(playerCarDef.Specs?.Weight);
            var opponentWeight = ParseNumericValue(opponentCarDef.Specs?.Weight);

            // Horsepower - higher is better
            if (playerPower > 0 || opponentPower > 0)
            {
                AddMatchupStat("Horsepower",
                    $"{playerPower:N0} HP",
                    $"{opponentPower:N0} HP",
                    playerPower, opponentPower, higherIsBetter: true);
            }

            // Weight - lower is better
            if (playerWeight > 0 || opponentWeight > 0)
            {
                AddMatchupStat("Weight",
                    $"{playerWeight:N0} kg",
                    $"{opponentWeight:N0} kg",
                    playerWeight, opponentWeight, higherIsBetter: false);
            }

            // Power-to-weight ratio - higher is better
            if (playerWeight > 0 && opponentWeight > 0 && (playerPower > 0 || opponentPower > 0))
            {
                var playerPWR = playerWeight > 0 ? playerPower / (playerWeight / 1000.0) : 0;
                var opponentPWR = opponentWeight > 0 ? opponentPower / (opponentWeight / 1000.0) : 0;
                AddMatchupStat("HP/Ton",
                    $"{playerPWR:N1}",
                    $"{opponentPWR:N1}",
                    playerPWR, opponentPWR, higherIsBetter: true);
            }

            OnPropertyChanged(nameof(HasMatchupStats));
        }

        private void UpdateWagerLimits()
        {
            if (IsCashBet)
            {
                // Wager limits vary by race type
                // Drag: $10-$100, Road: $25-$250
                var isDrag = SelectedTrack?.RaceType == RaceType.DragRace;
                var baseMin = isDrag ? 10m : 25m;
                var baseMax = isDrag ? 100m : 250m;

                MinWager = baseMin;

                // Max is capped by both racers' money
                var playerMoney = _gameState.Player.Money;
                var opponentMoney = SelectedOpponent?.Opponent.Money ?? 0m;
                var maxAvailable = Math.Min(playerMoney, opponentMoney);
                MaxWager = Math.Min(baseMax, maxAvailable);

                // Ensure min doesn't exceed max
                if (MinWager > MaxWager)
                {
                    MinWager = MaxWager;
                }

                // Clamp wager amount to valid range
                if (WagerAmount < MinWager)
                    WagerAmount = MinWager;
                else if (WagerAmount > MaxWager)
                    WagerAmount = MaxWager;
            }
            else
            {
                // Pink slip: no cash wager
                MinWager = 0m;
                MaxWager = 0m;
                WagerAmount = 0m;
            }

            // Notify affordability status
            OnPropertyChanged(nameof(CanAffordMinBet));
            OnPropertyChanged(nameof(InsufficientFundsMessage));
        }

        private void AddMatchupStat(string label, string playerDisplay, string opponentDisplay,
            double playerValue, double opponentValue, bool higherIsBetter)
        {
            var diff = playerValue - opponentValue;
            var advantage = Math.Abs(diff) < 0.01 ? 0 : (diff > 0 ? 1 : -1);

            // If lower is better, flip the advantage
            if (!higherIsBetter)
                advantage = -advantage;

            MatchupStats.Add(new MatchupStatViewModel
            {
                Label = label,
                PlayerValue = playerDisplay,
                OpponentValue = opponentDisplay,
                Advantage = advantage
            });
        }

        /// <summary>
        /// Parse numeric value from AC spec strings like "320bhp", "1250kg", "320 bhp"
        /// </summary>
        private static double ParseNumericValue(string? specString)
        {
            if (string.IsNullOrWhiteSpace(specString))
                return 0;

            // Extract just the digits and decimal point
            var numericPart = new string(specString.Where(c => char.IsDigit(c) || c == '.').ToArray());

            if (double.TryParse(numericPart, out var result))
                return result;

            return 0;
        }

        private bool CanChallenge()
        {
            return SelectedOpponent != null &&
                   SelectedTrack != null &&
                   _gameState.Player.Cars.Count > 0 &&
                   _gameState.Player.SelectedCarInstanceId != null;
        }

        private async void OnChallenge()
        {
            if (SelectedOpponent == null)
            {
                _logger.Warning("Cannot challenge - no opponent selected");
                return;
            }

            if (SelectedTrack == null)
            {
                _logger.Warning("Cannot challenge - no track selected");
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

            // Build challenge setup directly from diner selections
            var setup = new ChallengeSetup
            {
                Opponent = SelectedOpponent.Opponent,
                PlayerCar = playerCar,
                OpponentCar = opponentCar,
                IsPinkSlip = IsPinkSlipBet,
                CashWager = IsCashBet ? WagerAmount : 0m,
                TrackId = SelectedTrack.TrackId,
                TrackConfig = SelectedTrack.ConfigurationId,
                RaceType = SelectedTrack.RaceType
            };

            _logger.Information("Challenge setup: Track={TrackId}, Config={Config}, IsPinkSlip={PinkSlip}, Wager={Wager}",
                setup.TrackId, setup.TrackConfig ?? "(none)", setup.IsPinkSlip, setup.CashWager);

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
                // Show rejection via talk container
                _logger.Information("Opponent declined: {Message}", response.Message);
                OpponentMessage = response.Message;
                return;
            }

            // Opponent accepted - launch race directly
            _logger.Information("Opponent accepted challenge - launching race");

            var opponentCarDef = SelectedOpponent.CarDefinition;

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
                OpponentAIAggression = setup.Opponent.Aggression,
                RaceType = setup.RaceType
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
        public string CarBrand => CarDefinition.Brand;
        public string CarName => CarDefinition.Name;
        public int Reputation => Opponent.Stats.Reputation;
        public string ReputationTier => Opponent.Stats.GetReputationTier();
        public string RecordDisplay => $"{Opponent.Stats.Wins}W - {Opponent.Stats.Losses}L";
        public bool HasPortrait => !string.IsNullOrEmpty(PortraitPath);

        // Additional record stats
        public string WinRateDisplay
        {
            get
            {
                var winRate = Opponent.Stats.Races > 0
                    ? (Opponent.Stats.Wins * 100.0 / Opponent.Stats.Races)
                    : 0.0;
                return $"{winRate:F0}%";
            }
        }

        public string PinkSlipsDisplay
        {
            get
            {
                var won = Opponent.Stats.PinkSlipsWon;
                var lost = Opponent.Stats.PinkSlipsLost;
                return $"{won}W - {lost}L";
            }
        }

        // Car stats
        public string CarYear => CarDefinition.Year.HasValue ? CarDefinition.Year.Value.ToString() : "";

        public string CarPower
        {
            get
            {
                var power = ParseNumericValue(CarDefinition.Specs?.Bhp);
                return power > 0 ? $"{power:N0} HP" : "";
            }
        }

        public string CarWeight
        {
            get
            {
                var weight = ParseNumericValue(CarDefinition.Specs?.Weight);
                return weight > 0 ? $"{weight:N0} kg" : "";
            }
        }

        public string CarDrivetrain => CarDefinition.Specs?.Drivetrain ?? "";

        /// <summary>
        /// Parse numeric value from AC spec strings like "320bhp", "1250kg", "320 bhp"
        /// </summary>
        private static double ParseNumericValue(string? specString)
        {
            if (string.IsNullOrWhiteSpace(specString))
                return 0;

            // Extract just the digits and decimal point
            var numericPart = new string(specString.Where(c => char.IsDigit(c) || c == '.').ToArray());

            if (double.TryParse(numericPart, out var result))
                return result;

            return 0;
        }

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
