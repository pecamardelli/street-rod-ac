using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;

namespace Street_Rod_AC.Dialogs.ChallengeSetup
{
    /// <summary>
    /// View model for setting up a race challenge
    /// </summary>
    public class ChallengeSetupDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly Player _player;
        private readonly Opponent _opponent;
        private readonly Car _playerCar;
        private readonly Car _opponentCar;
        private readonly CarDefinition _playerCarDef;
        private readonly CarDefinition _opponentCarDef;
        private readonly Action<ChallengeSetup?> _callback;

        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelCommand { get; }

        // Display properties
        public string OpponentName => _opponent.Name;
        public string OpponentNickname => _opponent.Nickname;
        public string OpponentCarDisplay => $"{_opponentCarDef.Brand} {_opponentCarDef.Name}";
        public string PlayerCarDisplay => $"{_playerCarDef.Brand} {_playerCarDef.Name}";
        public decimal PlayerBankroll => _player.Money;
        public decimal OpponentEstimatedMoney => _opponent.Money;

        // Bet type selection
        private bool _isCashBet = true;
        public bool IsCashBet
        {
            get => _isCashBet;
            set
            {
                _isCashBet = value;
                OnPropertyChanged(nameof(IsCashBet));
                OnPropertyChanged(nameof(IsPinkSlipBet));
                OnPropertyChanged(nameof(IsCashBetEnabled));
                UpdateMinMaxWager();
            }
        }

        public bool IsPinkSlipBet
        {
            get => !_isCashBet;
            set => IsCashBet = !value;
        }

        public bool IsCashBetEnabled => IsCashBet;

        // Wager amount
        private decimal _wagerAmount;
        public decimal WagerAmount
        {
            get => _wagerAmount;
            set
            {
                _wagerAmount = value;
                OnPropertyChanged(nameof(WagerAmount));
                OnPropertyChanged(nameof(WagerAmountDisplay));
            }
        }

        public string WagerAmountDisplay => $"${WagerAmount:N0}";

        // Min/Max wager
        private decimal _minWager;
        public decimal MinWager
        {
            get => _minWager;
            set
            {
                _minWager = value;
                OnPropertyChanged(nameof(MinWager));
            }
        }

        private decimal _maxWager;
        public decimal MaxWager
        {
            get => _maxWager;
            set
            {
                _maxWager = value;
                OnPropertyChanged(nameof(MaxWager));
            }
        }

        // Race type selection
        private bool _isDragRace = true;
        public bool IsDragRace
        {
            get => _isDragRace;
            set
            {
                if (_isDragRace != value)
                {
                    _isDragRace = value;
                    OnPropertyChanged(nameof(IsDragRace));
                    OnPropertyChanged(nameof(IsRoadRace));
                    OnPropertyChanged(nameof(SelectedRaceType));
                    UpdateAvailableTracks();
                    UpdateMinMaxWager();
                }
            }
        }

        public bool IsRoadRace
        {
            get => !_isDragRace;
            set => IsDragRace = !value;
        }

        public RaceType SelectedRaceType => IsDragRace ? RaceType.DragRace : RaceType.Circuit;

        // Track selection
        private ObservableCollection<TrackDisplayItem> _availableTracks = new();
        public ObservableCollection<TrackDisplayItem> AvailableTracks
        {
            get => _availableTracks;
            set
            {
                _availableTracks = value;
                OnPropertyChanged(nameof(AvailableTracks));
            }
        }

        private TrackDisplayItem? _selectedTrack;
        public TrackDisplayItem? SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                _selectedTrack = value;
                OnPropertyChanged(nameof(SelectedTrack));
                OnPropertyChanged(nameof(SelectedTrackDisplay));
            }
        }

        public string SelectedTrackDisplay => SelectedTrack?.DisplayName ?? "No tracks available";

        public ChallengeSetupDialogViewModel(
            DialogService dialogService,
            IAssettoCorsaContentService contentService,
            Player player,
            Opponent opponent,
            Car playerCar,
            Car opponentCar,
            CarDefinition playerCarDef,
            CarDefinition opponentCarDef,
            Action<ChallengeSetup?> callback)
        {
            _dialogService = dialogService;
            _contentService = contentService;
            _player = player;
            _opponent = opponent;
            _playerCar = playerCar;
            _opponentCar = opponentCar;
            _playerCarDef = playerCarDef;
            _opponentCarDef = opponentCarDef;
            _callback = callback;

            ConfirmCommand = new RelayCommand(OnConfirm);
            CancelCommand = new RelayCommand(OnCancel);

            // Initialize track selection and wager settings
            UpdateAvailableTracks();
            UpdateMinMaxWager();
        }

        private void UpdateMinMaxWager()
        {
            if (IsCashBet)
            {
                // Wager limits vary by race type
                // Drag: $10-$100, Road: $25-$250
                var baseMin = IsDragRace ? 10m : 25m;
                var baseMax = IsDragRace ? 100m : 250m;

                MinWager = baseMin;
                // Max is capped by both racers' money
                var maxAvailable = Math.Min(_player.Money, _opponent.Money);
                MaxWager = Math.Min(baseMax, maxAvailable);

                // Ensure min doesn't exceed max
                if (MinWager > MaxWager)
                {
                    MinWager = MaxWager;
                }

                // Set default wager (middle of range)
                var defaultWager = (MinWager + MaxWager) / 2m;
                WagerAmount = Math.Max(MinWager, Math.Min(defaultWager, MaxWager));
            }
            else
            {
                // Pink slip: no cash wager
                MinWager = 0m;
                MaxWager = 0m;
                WagerAmount = 0m;
            }
        }

        private void UpdateAvailableTracks()
        {
            var trackType = IsDragRace ? TrackType.Dragstrip : TrackType.Circuit;
            var tracks = _contentService.GetTracks()
                .Where(t => t.Type == trackType)
                .ToList();

            AvailableTracks.Clear();

            foreach (var track in tracks)
            {
                if (track.Configurations.Count > 0)
                {
                    // Add each configuration as a separate selectable item
                    foreach (var config in track.Configurations)
                    {
                        AvailableTracks.Add(new TrackDisplayItem
                        {
                            TrackInfo = track,
                            Configuration = config,
                            // Use just the configuration name since it's more descriptive
                            DisplayName = config.Name
                        });
                    }
                }
                else
                {
                    // Track has no configurations, add as-is
                    AvailableTracks.Add(new TrackDisplayItem
                    {
                        TrackInfo = track,
                        Configuration = null,
                        DisplayName = track.Name
                    });
                }
            }

            // Select the first track by default
            SelectedTrack = AvailableTracks.FirstOrDefault();
        }

        private void OnConfirm()
        {
            var setup = new ChallengeSetup
            {
                Opponent = _opponent,
                PlayerCar = _playerCar,
                OpponentCar = _opponentCar,
                IsPinkSlip = IsPinkSlipBet,
                CashWager = IsCashBet ? WagerAmount : 0m,
                TrackId = SelectedTrack?.TrackInfo.TrackId ?? "ks_drag",
                TrackConfig = SelectedTrack?.Configuration?.FolderName,
                RaceType = SelectedRaceType
            };

            _callback?.Invoke(setup);
            _dialogService.CloseDialog();
        }

        private void OnCancel()
        {
            _callback?.Invoke(null);
            _dialogService.CloseDialog();
        }
    }

    /// <summary>
    /// Setup data for a race challenge
    /// </summary>
    public class ChallengeSetup
    {
        public Opponent Opponent { get; set; } = null!;
        public Car PlayerCar { get; set; } = null!;
        public Car OpponentCar { get; set; } = null!;
        public bool IsPinkSlip { get; set; }
        public decimal CashWager { get; set; }
        public string TrackId { get; set; } = "ks_drag";
        public string? TrackConfig { get; set; } = "drag1000";
        public RaceType RaceType { get; set; } = RaceType.DragRace;
    }

    /// <summary>
    /// Display item for track selection combining track and configuration
    /// </summary>
    public class TrackDisplayItem
    {
        public TrackInfo TrackInfo { get; set; } = null!;
        public TrackConfiguration? Configuration { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
