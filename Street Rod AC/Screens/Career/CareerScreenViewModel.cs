using System.Collections.ObjectModel;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Career
{
    public class CareerScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly IVictoryConditionService _victoryService;
        private readonly IMilestoneService _milestoneService;
        private readonly Models.GameState.GameState _gameState;

        // Commands
        public RelayCommand BackCommand { get; }
        public RelayCommand<string> SetActiveVictoryCommand { get; }

        // Victory conditions
        public ObservableCollection<VictoryDisplayViewModel> Victories { get; } = [];

        private VictoryDisplayViewModel? _activeVictory;
        public VictoryDisplayViewModel? ActiveVictory
        {
            get => _activeVictory;
            private set
            {
                _activeVictory = value;
                OnPropertyChanged(nameof(ActiveVictory));
                OnPropertyChanged(nameof(HasActiveVictory));
            }
        }

        public bool HasActiveVictory => ActiveVictory != null;

        // Milestones
        public ObservableCollection<MilestoneDisplayViewModel> Milestones { get; } = [];

        public int CompletedMilestoneCount => Milestones.Count(m => m.IsCompleted);
        public int TotalMilestoneCount => Milestones.Count;
        public string MilestoneProgressText => $"{CompletedMilestoneCount}/{TotalMilestoneCount} completed";

        // Stats
        public int TotalWins => _gameState.Player.Stats.Wins;
        public int TotalLosses => _gameState.Player.Stats.Losses;
        public int TotalRaces => _gameState.Player.Stats.Races;
        public string WinRate => TotalRaces > 0
            ? $"{(_gameState.Player.Stats.WinRate * 100):F0}%"
            : "N/A";

        public int Reputation => _gameState.Player.Stats.Reputation;
        public string ReputationTier => _gameState.Player.Stats.GetReputationTier();

        public int CarsOwned => _gameState.Player.Cars.Count;
        public int PinkSlipsWon => _gameState.Player.Stats.PinkSlipsWon;
        public int PinkSlipsLost => _gameState.Player.Stats.PinkSlipsLost;

        public int OpponentsDefeated => _gameState.Career.DefeatedOpponentIds.Count;

        public string TotalEarnings => $"${_gameState.Player.Stats.TotalEarnings:N0}";
        public string TotalLossesAmount => $"${_gameState.Player.Stats.TotalLosses:N0}";
        public string NetEarnings => $"${_gameState.Player.Stats.NetEarnings:N0}";

        // Bottom bar
        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        // Game won state
        public bool HasWonGame => _gameState.Career.HasWonGame;
        public string? WinningVictoryName => HasWonGame && _gameState.Career.WinningVictoryType != null
            ? _victoryService.GetVictoryCondition(_gameState.Career.WinningVictoryType)?.Name
            : null;

        public CareerScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IVictoryConditionService victoryService,
            IMilestoneService milestoneService,
            Models.GameState.GameState gameState)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _victoryService = victoryService;
            _milestoneService = milestoneService;
            _gameState = gameState;

            BackCommand = new RelayCommand(OnBack);
            SetActiveVictoryCommand = new RelayCommand<string>(OnSetActiveVictory);

            LoadVictories();
            LoadMilestones();
        }

        private void LoadVictories()
        {
            Victories.Clear();

            foreach (var condition in _victoryService.GetAllVictoryConditions())
            {
                var vm = VictoryDisplayViewModel.FromCondition(condition, _gameState.Career);
                Victories.Add(vm);

                if (vm.IsActive)
                {
                    ActiveVictory = vm;
                }
            }

            // Sort: Active first, then achieved, then unlocked, then locked
            var sorted = Victories
                .OrderByDescending(v => v.IsActive)
                .ThenByDescending(v => v.IsAchieved)
                .ThenByDescending(v => v.IsUnlocked)
                .ThenBy(v => v.Name)
                .ToList();

            Victories.Clear();
            foreach (var v in sorted)
            {
                Victories.Add(v);
            }
        }

        private void LoadMilestones()
        {
            Milestones.Clear();

            foreach (var milestone in _milestoneService.GetAllMilestones())
            {
                var vm = MilestoneDisplayViewModel.FromMilestone(milestone, _gameState.Career);
                Milestones.Add(vm);
            }

            // Sort: Completed first, then by progress percentage descending
            var sorted = Milestones
                .OrderByDescending(m => m.IsCompleted)
                .ThenByDescending(m => m.ProgressPercentage)
                .ThenBy(m => m.Name)
                .ToList();

            Milestones.Clear();
            foreach (var m in sorted)
            {
                Milestones.Add(m);
            }

            OnPropertyChanged(nameof(CompletedMilestoneCount));
            OnPropertyChanged(nameof(MilestoneProgressText));
        }

        private void OnSetActiveVictory(string? victoryType)
        {
            if (string.IsNullOrEmpty(victoryType))
                return;

            // Find the victory
            var victory = Victories.FirstOrDefault(v => v.VictoryType == victoryType);
            if (victory == null || !victory.CanSetActive)
                return;

            // Update game state
            _gameState.Career.ActiveVictoryType = victoryType;

            // Refresh display
            LoadVictories();
        }

        private void OnBack()
        {
            _navigationService.NavigateToGame(_gameState);
        }

        public override void Enter()
        {
            base.Enter();

            // Refresh data in case it changed
            LoadVictories();
            LoadMilestones();
        }

        public override void Exit()
        {
            base.Exit();
        }
    }
}
