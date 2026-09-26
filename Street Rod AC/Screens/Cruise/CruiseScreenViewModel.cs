using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Street_Rod_AC.Audio;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Controls.Street;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.Diner;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Police;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Street;
using Street_Rod_AC.Services.Talk;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Cruise
{
    /// <summary>
    /// Cruising: the player sits in their car at the curb, engine running, while the clock goes round. Now and then a
    /// rival the street's rules pick (<see cref="StreetEncounters"/>) pulls up alongside with an offer: a race, where,
    /// and for what. The player takes it, changes the stakes (and the rival says whether they are still on), or waves
    /// them off and they pull away. The night ends at the day's end, and the player goes home.
    /// </summary>
    public class CruiseScreenViewModel : BaseScreenViewModel
    {
        // Real seconds a tick of the clock takes, and the game minutes it moves on
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1.5);
        private const int MinutesPerTick = 5;

        // The first rival is not due before the street is up on screen
        private const int FirstWaitAtLeast = 10;

        // A rival who pulled up blips the throttle at the player now and then
        private static readonly TimeSpan BlipEvery = TimeSpan.FromSeconds(4.5);

        // The rivals met tonight, whoever it was: a rival waved off does not come round again the same night
        private static DateTime _metOn;
        private static readonly HashSet<Guid> MetTonight = [];

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IOpponentChallengeService _challengeService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly ITalkService _talkService;
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly ICarPartsService _partsService;
        private readonly ChallengeLauncher _launcher;
        private readonly IAppLogger _logger;
        private readonly Random _random = new();
        private readonly DispatcherTimer _clock;
        private readonly DispatcherTimer _blips;

        private List<TrackInfo> _tracks = [];
        private DateTime _dueAt;
        private bool _ticking;
        private bool _leaving;
        private bool _spending;
        private int _encounterVersion;

        private Opponent? _rival;
        private Car? _rivalCar;
        private CarDefinition? _rivalDefinition;
        private StreetOffer? _offer;
        private TrackInfo? _track;
        private TrackConfiguration? _configuration;

        public CruiseScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            IContentCatalogRepository catalogRepository,
            IOpponentChallengeService challengeService,
            IAssettoCorsaContentService contentService,
            ITalkService talkService,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo,
            RaceSetupBuilder raceSetup,
            ICarPartsService partsService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _challengeService = challengeService;
            _contentService = contentService;
            _talkService = talkService;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _partsService = partsService;
            _logger = AppLoggerFactory.CreateLogger("Cruise");
            _launcher = new ChallengeLauncher(navigationService, dialogService, gameState, catalogRepository, challengeService,
                raceSetup, _logger);

            GarageCommand = new RelayCommand(() => Leave(toGarage: true));
            DinerCommand = new RelayCommand(() => Leave(toGarage: false));
            AcceptCommand = new AsyncRelayCommand(OnAccept, () => IsEncounterShown && !IsBusy && CanAffordBet);
            WaveOffCommand = new RelayCommand(WaveOff, () => IsEncounterShown && !IsBusy);
            CashBetCommand = new RelayCommand(() => IsPinkSlipBet = false, () => CanChangeBet);
            PinkSlipBetCommand = new RelayCommand(() => IsPinkSlipBet = true, () => CanChangeBet);

            _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
            _clock.Tick += OnClockTick;
            _blips = new DispatcherTimer(DispatcherPriority.Background) { Interval = BlipEvery };
            _blips.Tick += (_, _) => Blip();

            Stage.RivalAlongside += OnRivalAlongside;
            Stage.RivalGone += OnRivalGone;
        }

        #region Commands

        public RelayCommand GarageCommand { get; }
        public RelayCommand DinerCommand { get; }
        public AsyncRelayCommand AcceptCommand { get; }
        public RelayCommand WaveOffCommand { get; }
        public RelayCommand CashBetCommand { get; }
        public RelayCommand PinkSlipBetCommand { get; }

        #endregion

        #region The street

        /// <summary>What the rival does, which the street's viewport shows</summary>
        public StreetStage Stage { get; } = new();

        public StreetScene? Scene { get; private set; }

        private StreetLight _light;

        public StreetLight Light
        {
            get => _light;
            private set
            {
                if (_light == value) return;
                _light = value;
                OnPropertyChanged(nameof(Light));
                OnPropertyChanged(nameof(LightDisplay));
            }
        }

        /// <summary>The car the player sits in</summary>
        public StreetCar? PlayerCar { get; private set; }

        /// <summary>The player's own engine, idling under them; the Rev button blips it</summary>
        public EngineRunner PlayerEngine { get; } = new(EngineChannel.Main);

        /// <summary>The rival's engine, driven by the street while the car moves</summary>
        public EngineRunner RivalEngine { get; } = new(EngineChannel.Second);

        public string ClockDisplay => _gameState.Date.ToString("h:mm tt", System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        public string DateDisplay => _gameState.Date.ToString("dddd, MMMM d", System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        public string LightDisplay => Light switch
        {
            StreetLight.Day => "Daylight",
            StreetLight.Dusk => "Dusk",
            _ => "Night"
        } + (MatchupCalculator.IsNight(_gameState.Date) ? " · the stakes are up" : string.Empty);

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        private string _statusText = "You pull over to the curb and let the engine idle.";

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        #endregion

        #region The encounter

        private bool _isEncounterShown;

        /// <summary>A rival has stopped alongside and their offer is up</summary>
        public bool IsEncounterShown
        {
            get => _isEncounterShown;
            private set
            {
                if (SetProperty(ref _isEncounterShown, value)) RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _isBusy;

        /// <summary>The race is being set up</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value)) RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public string RivalName => _rival?.Name ?? string.Empty;
        public string RivalNickname => string.IsNullOrEmpty(_rival?.Nickname) ? string.Empty : $"\"{_rival.Nickname}\"";
        public string? RivalPortrait { get; private set; }
        public string RivalCarName => _rivalDefinition == null ? string.Empty : CarNames.Of(_rivalDefinition);
        public string RivalStanding => _rival == null ? string.Empty
            : $"Reputation {_rival.Stats.Reputation} · {_rival.Stats.Wins}W - {_rival.Stats.Losses}L";

        private string _rivalLine = string.Empty;

        /// <summary>What the rival says through the window</summary>
        public string RivalLine
        {
            get => _rivalLine;
            private set => SetProperty(ref _rivalLine, value);
        }

        /// <summary>"Drag race at Santa Pod" or "Road race at Laguna Seca"</summary>
        public string RaceDisplay => _offer == null || _track == null ? string.Empty
            : $"{(_offer.RaceType == RaceType.DragRace ? "Drag race" : "Road race")} at {_configuration?.Name ?? _track.Name}";

        public ObservableCollection<MatchupStatViewModel> MatchupStats { get; } = [];

        /// <summary>The King and a rival after a rematch race for pink slips and nothing else</summary>
        public bool CanChangeBet => IsEncounterShown && _rival is { IsKing: false, Grudge: null };

        private bool _isPinkSlipBet;

        public bool IsPinkSlipBet
        {
            get => _isPinkSlipBet;
            set
            {
                if (!SetProperty(ref _isPinkSlipBet, value)) return;
                OnPropertyChanged(nameof(IsCashBet));
                OnPropertyChanged(nameof(BetDisplay));
                OnBetChanged();
            }
        }

        public bool IsCashBet => !IsPinkSlipBet;

        private decimal _wager;

        public decimal Wager
        {
            get => _wager;
            set
            {
                var clamped = MatchupCalculator.ClampWager(Math.Round(value / 5m) * 5m, MinWager, MaxWager);
                if (!SetProperty(ref _wager, clamped)) return;
                OnPropertyChanged(nameof(BetDisplay));
                OnBetChanged();
            }
        }

        public decimal MinWager { get; private set; }
        public decimal MaxWager { get; private set; }

        public string BetDisplay => IsPinkSlipBet ? "PINK SLIPS" : $"${Wager:N0}";

        /// <summary>The player can cover a cash bet (pink slips need no cash)</summary>
        public bool CanAffordBet => IsPinkSlipBet || (MaxWager > 0m && _gameState.Player.Money >= Wager);

        /// <summary>The terms are not the rival's any more: they will say whether they are still on</summary>
        public bool TermsChanged => _offer != null && (_offer.PinkSlips != IsPinkSlipBet || (!IsPinkSlipBet && _offer.Wager != Wager));

        public string AcceptText => TermsChanged ? "HOW ABOUT THIS?" : "YOU'RE ON!";

        public string PoliceNote
        {
            get
            {
                if (_offer is not { RaceType: not RaceType.DragRace } || _rival == null || PoliceCars.Installed() == null) return string.Empty;
                var chance = PoliceRules.Chance(_gameState.Date, ChallengeLauncher.PoliceReputation(_gameState.Player, _rival),
                    IsPinkSlipBet, IsPinkSlipBet ? 0m : Wager);
                return chance > 0 ? $"Police: {PoliceRules.RiskLabel(chance)}" : string.Empty;
            }
        }

        public bool HasPoliceNote => PoliceNote.Length > 0;

        private void OnBetChanged()
        {
            OnPropertyChanged(nameof(TermsChanged));
            OnPropertyChanged(nameof(AcceptText));
            OnPropertyChanged(nameof(CanAffordBet));
            OnPropertyChanged(nameof(PoliceNote));
            OnPropertyChanged(nameof(HasPoliceNote));
            RelayCommand.RaiseCanExecuteChanged();
        }

        #endregion

        #region Coming and going

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered the street");

            if (_metOn != _gameState.Date.Date)
            {
                _metOn = _gameState.Date.Date;
                MetTonight.Clear();
            }

            var playerCar = SelectedCar();
            if (playerCar == null || _catalogRepository.GetCar(playerCar.DefinitionId) == null)
            {
                SendHome("You need a car to cruise in. Pick one in your garage first.", "No Car");
                return;
            }

            if (playerCar.IsImpounded || !_challengeService.CanRace(playerCar))
            {
                SendHome("Your car is in no shape to be out on the street. Sort it out in the garage first.", "Car Won't Run");
                return;
            }

            try
            {
                _tracks = _contentService.GetTracks().ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the tracks for the street");
                _tracks = [];
            }

            Scene = StreetScenes.LoadFirst(StreetScenes.StreetsFolder);
            Light = StreetLights.At(_gameState.Date);
            PlayerCar = new StreetCar(Path.Combine(AppSettings.Instance.CarsPath, playerCar.DefinitionId),
                string.IsNullOrEmpty(playerCar.SkinId) ? null : playerCar.SkinId);
            OnPropertyChanged(nameof(Scene));
            OnPropertyChanged(nameof(PlayerCar));
            RefreshClock();

            _ = StartOwnEngineAsync(playerCar);

            _dueAt = _gameState.Date.AddMinutes(Math.Max(FirstWaitAtLeast, StreetEncounters.MinutesToNext(_gameState.Date, _random)));
            _ticking = true;
            _clock.Start();
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Left the street");

            _ticking = false;
            _clock.Stop();
            _blips.Stop();
            _encounterVersion++;
            Stage.Clear();
            PlayerEngine.Detach();
            RivalEngine.Detach();

            // The rival's bank can be hundreds of MB: it goes with the screen
            _ = EngineAudio.Shared.ReleaseAsync(EngineChannel.Second);
        }

        private Car? SelectedCar() =>
            _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == _gameState.Player.SelectedCarInstanceId);

        private async Task StartOwnEngineAsync(Car car)
        {
            try
            {
                var spec = await EngineSpecs.ForAsync(_partsService, car, CarNames.Of(_catalogRepository, car.DefinitionId));
                if (!_ticking) return;
                await PlayerEngine.SetEngineAsync(spec);
                if (_ticking) PlayerEngine.StartRunning();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The player's engine could not be started on the street");
            }
        }

        /// <summary>Holds the throttle down (the Rev button); false lets it up</summary>
        public void Rev(bool down)
        {
            if (!PlayerEngine.IsRunning) PlayerEngine.StartRunning();
            PlayerEngine.SetKey(down);
        }

        private void SendHome(string message, string title)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
            // Not from inside Enter: the navigation that brought the player here is still going on
            _leaving = true;
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() => _navigationService.NavigateToGarage(_gameState));
        }

        private void Leave(bool toGarage)
        {
            if (_leaving) return;
            _leaving = true;
            Save();
            if (toGarage) _navigationService.NavigateToGarage(_gameState);
            else _navigationService.NavigateToDiner(_gameState);
        }

        private void Save()
        {
            try
            {
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save after cruising");
            }
        }

        #endregion

        #region The clock

        private async void OnClockTick(object? sender, EventArgs e)
        {
            // Nothing may escape an async void; one spend at a time, and none while a rival is about
            if (_spending || !_ticking || Stage.Phase != RivalPhase.None || IsEncounterShown) return;

            _spending = true;
            try
            {
                var result = await _timeService.SpendTimeAsync(_gameState, MinutesPerTick);
                if (!_ticking) return;

                RefreshClock();
                if (result.NewDayStarted)
                {
                    EndOfTheNight();
                    return;
                }

                // The light moves on while nobody is about; the street is loaded again for it
                Light = StreetLights.At(_gameState.Date);

                if (_gameState.Date >= _dueAt) await SomebodyPullsUpAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The street's clock failed");
            }
            finally
            {
                _spending = false;
            }
        }

        private void RefreshClock()
        {
            OnPropertyChanged(nameof(ClockDisplay));
            OnPropertyChanged(nameof(DateDisplay));
            OnPropertyChanged(nameof(LightDisplay));
            OnPropertyChanged(nameof(BankrollDisplay));
        }

        /// <summary>The day is over: the player drives home, and wakes up in the garage</summary>
        private void EndOfTheNight()
        {
            _ticking = false;
            _clock.Stop();
            _logger.Information("The night is over: home to the garage");
            Save();
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService,
                "The street's gone quiet. You head home and call it a night.", "Late Night"));
            _leaving = true;
            _navigationService.NavigateToGarage(_gameState);
        }

        private void ScheduleNext()
        {
            _dueAt = _gameState.Date.AddMinutes(StreetEncounters.MinutesToNext(_gameState.Date, _random));
        }

        #endregion

        #region A rival pulls up

        /// <summary>The racers out tonight: on the scene, with a car that can race and that the game can show</summary>
        private List<Opponent> RivalsOut() => _gameState.Racers.ReadyToRace.Values
            .OfType<Opponent>()
            .Where(o => o.Cars.FirstOrDefault() is { } car && _challengeService.CanRace(car)
                        && _catalogRepository.GetCar(car.DefinitionId) != null
                        && Directory.Exists(Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId)))
            .ToList();

        private async Task SomebodyPullsUpAsync()
        {
            var time = _gameState.Date;
            var rival = StreetEncounters.PickRival(RivalsOut(), _gameState.Player.Stats.Reputation, time, MetTonight, _random);
            ScheduleNext();

            if (rival == null)
            {
                StatusText = "The street's quiet. Nobody's out looking for a race.";
                return;
            }

            var dragTracks = _tracks.Where(t => t.Type == TrackType.Dragstrip).ToList();
            var circuits = _tracks.Where(t => t.Type != TrackType.Dragstrip).ToList();
            var raceType = StreetEncounters.PickRaceType(time, dragTracks.Count > 0, circuits.Count > 0, _random);
            if (raceType == null)
            {
                StatusText = "Nowhere to race: no tracks are installed.";
                return;
            }

            var offer = StreetEncounters.OfferFrom(rival, _gameState.Player.Money, raceType.Value, time,
                _gameState.Rules.PinkSlipFactor, _random);
            if (offer == null)
            {
                // Nobody has the money for a bet tonight: they cruise on by
                MetTonight.Add(rival.OpponentId);
                return;
            }

            var track = (raceType == RaceType.DragRace ? dragTracks : circuits)[_random.Next(raceType == RaceType.DragRace ? dragTracks.Count : circuits.Count)];
            var configuration = track.Configurations.Count > 0 ? track.Configurations[_random.Next(track.Configurations.Count)] : null;
            if (raceType == RaceType.DragRace && track.Configurations.Count > 0)
            {
                // A drag strip's layouts: the quarter mile if it has one, as the street runs it
                configuration = track.Configurations.FirstOrDefault(c => Services.Race.BracketRules.RunsTheQuarter(track, c)) ?? configuration;
            }

            var car = rival.Cars.First();
            var definition = _catalogRepository.GetCar(car.DefinitionId)!;
            var version = ++_encounterVersion;

            MetTonight.Add(rival.OpponentId);
            _rival = rival;
            _rivalCar = car;
            _rivalDefinition = definition;
            _offer = offer;
            _track = track;
            _configuration = configuration;
            _logger.Information("{Rival} pulls up in a {Car}: {Race} for {Stakes}", rival.Name, definition.Id,
                raceType, offer.PinkSlips ? "pink slips" : $"${offer.Wager}");

            StatusText = "Headlights in the mirror. Somebody's pulling up...";

            var spec = await EngineSpecs.ForAsync(_partsService, car, CarNames.Of(_catalogRepository, car.DefinitionId));
            if (version != _encounterVersion || !_ticking) return;
            await RivalEngine.SetEngineAsync(spec);
            if (version != _encounterVersion || !_ticking) return;

            Stage.Arrive(new StreetCar(Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId),
                string.IsNullOrEmpty(car.SkinId) ? null : car.SkinId), RivalEngine);
        }

        private void OnRivalAlongside()
        {
            if (_rival == null || _offer == null) return;

            StatusText = $"{_rival.Name} pulls up alongside and rolls the window down.";
            RivalPortrait = PortraitOf(_rival);

            _isPinkSlipBet = _offer.PinkSlips;
            (MinWager, MaxWager) = _offer.PinkSlips
                ? (0m, 0m)
                : MatchupCalculator.WagerLimits(_offer.RaceType, _gameState.Player.Money, _rival.Money, _rival.Stats.Reputation, _gameState.Date);
            _wager = _offer.PinkSlips ? 0m : _offer.Wager;

            MatchupStats.Clear();
            if (SelectedCar() is { } playerCar && _catalogRepository.GetCar(playerCar.DefinitionId) is { } playerDefinition)
            {
                foreach (var stat in MatchupCalculator.Compare(playerDefinition, _rivalDefinition!, playerCar.PowerHp, _rivalCar?.PowerHp))
                    MatchupStats.Add(stat);
            }

            IsEncounterShown = true;
            foreach (var name in new[]
                     {
                         nameof(RivalName), nameof(RivalNickname), nameof(RivalPortrait), nameof(RivalCarName), nameof(RivalStanding),
                         nameof(RaceDisplay), nameof(CanChangeBet), nameof(IsPinkSlipBet), nameof(IsCashBet), nameof(Wager),
                         nameof(MinWager), nameof(MaxWager), nameof(BetDisplay)
                     })
            {
                OnPropertyChanged(name);
            }

            OnBetChanged();
            _ = SayAsync(TalkTrigger.PulledUp);
            Blip();
            _blips.Start();
        }

        private async Task SayAsync(TalkTrigger trigger)
        {
            if (_rival == null) return;
            try
            {
                var playerCar = SelectedCar();
                RivalLine = await _talkService.GetMessageAsync(new TalkContext
                {
                    Opponent = _rival,
                    Player = _gameState.Player,
                    OpponentCar = _rivalDefinition,
                    PlayerCar = playerCar == null ? null : _catalogRepository.GetCar(playerCar.DefinitionId),
                    Trigger = trigger,
                    IsPinkSlipBet = IsPinkSlipBet,
                    SelectedTrackName = _configuration?.Name ?? _track?.Name,
                    Grudge = _rival.Grudge,
                    GrudgeCarName = _rival.Grudge == null ? null : CarNames.Of(_catalogRepository, _rival.Grudge.CarDefinitionId),
                    PlayerHasGrudgeCar = _rival.Grudge != null && _gameState.Player.Cars.Any(c => c.InstanceId == _rival.Grudge.CarInstanceId)
                });
            }
            catch (Exception ex)
            {
                _logger.Warning("The rival had nothing to say: {Error}", ex.Message);
                RivalLine = string.Empty;
            }
        }

        /// <summary>The rival blips the throttle: a quarter of a second, most of the way down</summary>
        private void Blip()
        {
            if (Stage.Phase != RivalPhase.Alongside || !RivalEngine.IsRunning) return;

            RivalEngine.SetPedal(0.55 + 0.35 * _random.NextDouble());
            var release = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(220 + _random.Next(120)) };
            release.Tick += (_, _) =>
            {
                release.Stop();
                RivalEngine.SetPedal(null);
            };
            release.Start();
        }

        private static string? PortraitOf(Opponent rival)
        {
            if (string.IsNullOrEmpty(rival.PortraitPath)) return null;
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rival.PortraitPath.TrimStart('/', '\\'));
            return File.Exists(path) ? path : null;
        }

        private void WaveOff()
        {
            if (_rival == null) return;
            _logger.Information("Waved {Rival} off", _rival.Name);
            StatusText = $"You wave {_rival.Name} off. The {(_rivalDefinition == null ? "car" : CarNames.Short(_rivalDefinition))} pulls away.";
            CloseEncounter();
            Stage.Leave();
        }

        private void CloseEncounter()
        {
            _blips.Stop();
            IsEncounterShown = false;
            OnPropertyChanged(nameof(CanChangeBet));
            RivalLine = string.Empty;
        }

        private void OnRivalGone()
        {
            RivalEngine.Detach();
            _rival = null;
            _rivalCar = null;
            _rivalDefinition = null;
            _offer = null;
            if (_ticking) ScheduleNext();
        }

        private async Task OnAccept()
        {
            if (_rival == null || _rivalCar == null || _rivalDefinition == null || _offer == null || _track == null) return;
            var playerCar = SelectedCar();
            if (playerCar == null) return;

            IsBusy = true;
            try
            {
                var outcome = await _launcher.RunAsync(new ChallengeRequest
                {
                    Opponent = _rival,
                    OpponentCar = _rivalCar,
                    OpponentDefinition = _rivalDefinition,
                    PlayerCar = playerCar,
                    Track = _track,
                    Configuration = _configuration,
                    RaceType = _offer.RaceType,
                    IsPinkSlip = IsPinkSlipBet,
                    CashWager = IsPinkSlipBet ? 0m : Wager,
                    AlreadyAgreed = !TermsChanged,
                    ReturnToCruise = true
                }, this);

                switch (outcome.Status)
                {
                    case ChallengeStatus.Declined:
                        // Not on those terms; the offer they made still stands
                        RivalLine = outcome.Message ?? "Nah. Not for that.";
                        break;
                    case ChallengeStatus.Launched:
                        CloseEncounter();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The street race could not be set up");
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService,
                    $"The race could not be set up:\n\n{ex.Message}", "Race Not Started"));
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion
    }
}
