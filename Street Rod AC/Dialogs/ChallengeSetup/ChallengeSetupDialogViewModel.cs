using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs.ChallengeSetup
{
    /// <summary>
    /// View model for setting up a race challenge
    /// </summary>
    public class ChallengeSetupDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
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

        // Track selection (for now just one track)
        public string SelectedTrack => "Drag Strip";

        public ChallengeSetupDialogViewModel(
            DialogService dialogService,
            Player player,
            Opponent opponent,
            Car playerCar,
            Car opponentCar,
            CarDefinition playerCarDef,
            CarDefinition opponentCarDef,
            Action<ChallengeSetup?> callback)
        {
            _dialogService = dialogService;
            _player = player;
            _opponent = opponent;
            _playerCar = playerCar;
            _opponentCar = opponentCar;
            _playerCarDef = playerCarDef;
            _opponentCarDef = opponentCarDef;
            _callback = callback;

            ConfirmCommand = new RelayCommand(OnConfirm);
            CancelCommand = new RelayCommand(OnCancel);

            // Initialize wager settings
            UpdateMinMaxWager();
        }

        private void UpdateMinMaxWager()
        {
            if (IsCashBet)
            {
                // Cash bet: minimum $100, max is lesser of player bankroll or opponent money
                MinWager = 100m;
                MaxWager = Math.Min(_player.Money, _opponent.Money);

                // Set default to 10% of max or $500, whichever is higher
                var defaultWager = Math.Max(500m, MaxWager * 0.1m);
                WagerAmount = Math.Min(defaultWager, MaxWager);
            }
            else
            {
                // Pink slip: no cash wager
                MinWager = 0m;
                MaxWager = 0m;
                WagerAmount = 0m;
            }
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
                TrackId = "drag_strip" // TODO: Make this selectable
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
        public string TrackId { get; set; } = "drag_strip";
    }
}
