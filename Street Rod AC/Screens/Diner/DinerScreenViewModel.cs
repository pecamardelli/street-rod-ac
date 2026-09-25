using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Police;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Talk;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace Street_Rod_AC.Screens.Diner
{
    /// <summary>One line of the street talk, with when it happened ("Today", "Yesterday", "Jun 3")</summary>
    public sealed record StreetTalkLine(string When, string Text);

    public class DinerScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IOpponentChallengeService _challengeService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly ITalkService _talkService;
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly IAppLogger _logger;

        // Back from a race: the player never left, so the visit is not paid for again
        private readonly bool _returning;

        public RelayCommand GarageCommand { get; }
        public RelayCommand<OpponentDisplayViewModel> SelectOpponentCommand { get; }
        public RelayCommand<TrackCardViewModel> SelectTrackCommand { get; }
        public AsyncRelayCommand ChallengeCommand { get; }

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

        /// <summary>What the rivals have been up to, newest first (<see cref="Models.GameState.GameState.StreetTalk"/>)</summary>
        public ObservableCollection<StreetTalkLine> StreetTalk { get; } = new();

        public bool HasStreetTalk => StreetTalk.Count > 0;

        /// <summary>How many lines of talk the diner shows</summary>
        private const int StreetTalkShown = 15;

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
                    OnPropertyChanged(nameof(PoliceNote));
                    OnPropertyChanged(nameof(HasPoliceNote));
                }
            }
        }

        public string WagerAmountDisplay => $"${WagerAmount:N0}";

        /// <summary>Why the stakes are higher than usual: the hour, the rival's name; empty when they aren't</summary>
        public string StakesNote
        {
            get
            {
                var reasons = new List<string>();
                if (MatchupCalculator.IsNight(_gameState.Date)) reasons.Add("night");
                if ((SelectedOpponent?.Opponent.Stats.Reputation ?? 0) > MatchupCalculator.StakesReputation) reasons.Add("a big name");
                return reasons.Count == 0 ? string.Empty : $"Higher stakes: {string.Join(", ", reasons)}";
            }
        }

        public bool HasStakesNote => IsCashBet && StakesNote.Length > 0;

        /// <summary>The reputation the police go by: the better known of the two racers</summary>
        private int PoliceReputation => Math.Max(_gameState.Player.Stats.Reputation, SelectedOpponent?.Opponent.Stats.Reputation ?? 0);

        /// <summary>The chance of the police on a road race with the selected rival, now; 0 for a drag race or with no police car installed</summary>
        private double PoliceChance =>
            SelectedTrack is { RaceType: not RaceType.DragRace } && PoliceCars.Installed() != null
                ? PoliceRules.Chance(_gameState.Date, PoliceReputation, IsPinkSlipBet, IsCashBet ? WagerAmount : 0m)
                : 0;

        /// <summary>The word on the police for the race as it is set up: "Police: Moderate"; empty when none could come</summary>
        public string PoliceNote => PoliceChance > 0
            ? $"Police: {PoliceRules.RiskLabel(PoliceChance)}{(PoliceRules.IsNight(_gameState.Date) ? " (night patrols)" : string.Empty)}"
            : string.Empty;

        public bool HasPoliceNote => PoliceNote.Length > 0;

        public bool CanAffordMinBet
        {
            get
            {
                if (!IsCashBet) return true; // Pink slip doesn't require cash
                return _gameState.Player.Money >= MatchupCalculator.MinimumWager(SelectedTrack?.RaceType);
            }
        }

        public string InsufficientFundsMessage
        {
            get
            {
                var baseMin = MatchupCalculator.MinimumWager(SelectedTrack?.RaceType);
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
            IAssettoCorsaContentService contentService,
            ITalkService talkService,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo,
            RaceSetupBuilder raceSetup,
            bool returning = false)
        {
            _returning = returning;
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _challengeService = challengeService;
            _contentService = contentService;
            _talkService = talkService;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _raceSetup = raceSetup;
            _logger = AppLoggerFactory.CreateLogger("Diner");

            GarageCommand = new RelayCommand(OnGarage);
            SelectOpponentCommand = new RelayCommand<OpponentDisplayViewModel>(OnSelectOpponent);
            SelectTrackCommand = new RelayCommand<TrackCardViewModel>(OnSelectTrack);
            // Async: preparing the cars' data takes a moment, and a second click meanwhile must not start a second race
            ChallengeCommand = new AsyncRelayCommand(OnChallenge, CanChallenge);

            _opponents = new ObservableCollection<OpponentDisplayViewModel>();

            // Tracks and opponents are loaded in Enter, once: the screen is always entered right after it is made
        }

        private void LoadOpponents()
        {
            // A new day can bring other racers to the tables: whoever was picked before stays picked only if
            // they are still here, so the player can never challenge someone who has left
            var previous = SelectedOpponent?.Opponent;
            Opponents.Clear();

            // Get opponents from the ReadyToRace collection
            var readyOpponents = _gameState.Racers.ReadyToRace.Values
                .OfType<Opponent>()
                .ToList();

            if (readyOpponents.Count == 0)
                _logger.Information("No opponents available at diner");
            else
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

                // A racer whose car came back from a race today unable to go is at the garage, not the diner
                if (!_challengeService.CanRace(opponentCar))
                {
                    _logger.Information("{Name}'s car can't race today: not at the diner", opponent.Name);
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

            // Keep the one picked before if they are still here, else the first; with nobody here, nobody
            var keep = Opponents.FirstOrDefault(o => ReferenceEquals(o.Opponent, previous));
            if (keep != null || Opponents.Count > 0)
            {
                OnSelectOpponent(keep ?? Opponents[0]);
            }
            else
            {
                SelectedOpponent = null;
                OpponentMessage = "";
                UpdateWagerLimits();
            }
            RelayCommand.RaiseCanExecuteChanged();

            // Update bankroll display
            OnPropertyChanged(nameof(BankrollDisplay));

            LoadStreetTalk();
        }

        private void LoadStreetTalk()
        {
            StreetTalk.Clear();
            var today = _gameState.Date.Date;
            foreach (var item in (_gameState.StreetTalk ?? []).AsEnumerable().Reverse().Take(StreetTalkShown))
            {
                var days = (today - item.Date.Date).Days;
                var when = days <= 0 ? "Today" : days == 1 ? "Yesterday" : item.Date.ToString("MMM d");
                StreetTalk.Add(new StreetTalkLine(when, item.Text));
            }

            OnPropertyChanged(nameof(HasStreetTalk));
        }

        private OpponentDifficulty CalculateDifficulty(Opponent opponent) =>
            MatchupCalculator.DifficultyOf(opponent.Stats.Reputation, _gameState.Player.Stats.Reputation);

        private void OnSelectOpponent(OpponentDisplayViewModel? opponentVm)
        {
            if (opponentVm != null)
            {
                SelectedOpponent = opponentVm;
                _logger.Information("Selected opponent: {Name}", opponentVm.Name);

                // The King races for pink slips and nothing else, and a rival who wants a rematch wants the pink slips
                if (opponentVm.Opponent.IsKing || opponentVm.WantsRematch) IsPinkSlipBet = true;

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

                var grudge = opponentVm.Opponent.Grudge;
                var context = new TalkContext
                {
                    Opponent = opponentVm.Opponent,
                    Player = _gameState.Player,
                    OpponentCar = opponentVm.CarDefinition,
                    PlayerCar = playerCarDef,
                    Trigger = trigger,
                    IsPinkSlipBet = IsPinkSlipBet,
                    SelectedTrackName = SelectedTrack?.DisplayName,
                    Grudge = grudge,
                    GrudgeCarName = grudge == null ? null : CarNames.Of(_catalogRepository, grudge.CarDefinitionId),
                    PlayerHasGrudgeCar = grudge != null && _gameState.Player.Cars.Any(c => c.InstanceId == grudge.CarInstanceId)
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
            Models.GameState.Car? playerCar = null;
            if (_gameState.Player.SelectedCarInstanceId != null)
            {
                playerCar = _gameState.Player.Cars.FirstOrDefault(c =>
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

            foreach (var stat in MatchupCalculator.Compare(playerCarDef, SelectedOpponent.CarDefinition,
                         playerCar?.PowerHp, SelectedOpponent.Opponent.Cars.FirstOrDefault()?.PowerHp))
            {
                MatchupStats.Add(stat);
            }

            OnPropertyChanged(nameof(HasMatchupStats));
        }

        private void UpdateWagerLimits()
        {
            if (IsCashBet)
            {
                // Wager limits vary by race type (drag $10-$100, road $25-$250), go up at night and against a
                // rival with a name, and are capped by both racers' money
                var (min, max) = MatchupCalculator.WagerLimits(
                    SelectedTrack?.RaceType,
                    _gameState.Player.Money,
                    SelectedOpponent?.Opponent.Money ?? 0m,
                    SelectedOpponent?.Opponent.Stats.Reputation ?? MatchupCalculator.StakesReputation,
                    _gameState.Date);

                MinWager = min;
                MaxWager = max;
                WagerAmount = MatchupCalculator.ClampWager(WagerAmount, MinWager, MaxWager);
            }
            else
            {
                // Pink slip: no cash wager
                MinWager = 0m;
                MaxWager = 0m;
                WagerAmount = 0m;
            }

            // Notify affordability status
            OnPropertyChanged(nameof(StakesNote));
            OnPropertyChanged(nameof(HasStakesNote));
            OnPropertyChanged(nameof(PoliceNote));
            OnPropertyChanged(nameof(HasPoliceNote));
            OnPropertyChanged(nameof(CanAffordMinBet));
            OnPropertyChanged(nameof(InsufficientFundsMessage));
        }

        private bool CanChallenge()
        {
            return SelectedOpponent != null &&
                   SelectedTrack != null &&
                   _gameState.Player.Cars.Count > 0 &&
                   _gameState.Player.SelectedCarInstanceId != null;
        }

        /// <summary>Everything in here reads the catalog and the save: whatever throws is the player's to hear about, not the end of the game</summary>
        private async Task OnChallenge()
        {
            try
            {
                await ChallengeAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The challenge could not be set up");
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"The race could not be set up:\n\n{ex.Message}",
                    "Race Not Started"));
            }
        }

        private async Task ChallengeAsync()
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

            if (_catalogRepository.GetCar(playerCar.DefinitionId) == null)
            {
                _logger.Error("Player car definition not found");
                return;
            }

            // Get opponent's car
            var opponent = SelectedOpponent.Opponent;
            var opponentCar = opponent.Cars.FirstOrDefault();
            if (opponentCar == null)
            {
                _logger.Error("Opponent has no cars");
                return;
            }

            var isPinkSlip = IsPinkSlipBet;
            var cashWager = IsCashBet ? WagerAmount : 0m;
            var track = SelectedTrack;

            _logger.Information("Challenge setup: Track={TrackId}, Config={Config}, IsPinkSlip={PinkSlip}, Wager={Wager}",
                track.TrackId, track.ConfigurationId ?? "(none)", isPinkSlip, cashWager);

            // Whether the opponent takes it on, before any work goes into the cars
            var response = _challengeService.EvaluateChallenge(
                opponent,
                _gameState.Player,
                playerCar,
                opponentCar,
                isPinkSlip,
                cashWager,
                _gameState.Rules.PinkSlipFactor);

            if (!response.Accepted)
            {
                // Show rejection via talk container
                _logger.Information("Opponent declined: {Message}", response.Message);
                OpponentMessage = response.Message;
                return;
            }

            _logger.Information("Opponent accepted challenge - launching race");

            // Whether the police turn up, rolled once the race is on
            var police = PoliceCars.Patrol(PoliceCars.Installed(), track.RaceType != RaceType.DragRace, _gameState.Date,
                PoliceReputation, isPinkSlip, cashWager, track.Configuration?.Pitboxes ?? track.Track.Pitboxes,
                [playerCar.DefinitionId, SelectedOpponent.CarDefinition.Id], Random.Shared);
            if (police != null)
                _logger.Information("The police will show up: {Count} car(s), {Share:P0} into the race", police.Count, police.SpotShare);

            // Both cars race on what their parts make of them, the same way an event does; a player's car that
            // will not go stays home
            var setup = await _raceSetup.BuildAsync(new RaceEntry
            {
                PlayerName = _gameState.Player.Name,
                PlayerCar = playerCar,
                OpponentName = opponent.Name,
                OpponentCarId = SelectedOpponent.CarDefinition.Id,
                OpponentSkin = opponentCar.SkinId,
                OpponentCar = opponentCar,
                OpponentAI = OpponentAIAdapter.ToAssettoCorsaAI(opponent, _gameState.Rules),
                TrackId = track.TrackId,
                TrackConfig = track.ConfigurationId,
                RaceType = track.RaceType,
                CashWager = cashWager,
                IsPinkSlip = isPinkSlip,
                DamagePercent = _gameState.Rules.RaceDamagePercent,
                Police = police,
                RaceTime = _gameState.Date
            });

            // Setting the cars up takes a moment: a player who walked out meanwhile has called it off
            if (!ReferenceEquals(_navigationService.CurrentScreen, this))
            {
                _logger.Information("The player left the diner while the challenge was set up; no race");
                return;
            }

            if (setup.Intent == null)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"Your car is not going anywhere: {setup.PlayerCarProblem}.\n\nSort it out in the garage first.",
                    "Car Won't Run"));
                return;
            }

            // The loading screen launches the race, spends its time and comes back when AC is closed
            _logger.Information("Navigating to race loading screen");
            _navigationService.NavigateToRaceLoading(_gameState, setup.Intent);
        }

        private void OnGarage()
        {
            _logger.Information("Navigating to garage");
            _navigationService.NavigateToGarage(_gameState);
        }

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered diner screen");

            // Each on its own: a track folder that can't be read still leaves the racers at their tables
            try
            {
                LoadTracks();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the tracks at the diner");
            }

            try
            {
                LoadOpponents();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the racers at the diner");
            }

            if (_returning) return;

            try
            {
                // Spend time for visiting the diner (30 min). Late in the evening that is the next morning, when
                // the racers at the tables have changed: the room is shown again, and the new day is saved.
                var spent = await _timeService.SpendTimeAsync(_gameState, GameAction.VisitDiner);
                if (spent.NewDayStarted) LoadOpponents();
                OnPropertyChanged(nameof(BankrollDisplay));

                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend and save the time for the diner");
            }
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited diner screen");
        }
    }
}
