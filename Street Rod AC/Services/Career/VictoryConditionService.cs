using Street_Rod_AC.Models.Career.Victory;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Implementation of victory condition management
    /// </summary>
    public class VictoryConditionService : IVictoryConditionService
    {
        private readonly Dictionary<string, IVictoryCondition> _victoryConditions;
        private readonly Func<GameState, int>? _getTotalOpponents;
        private readonly Func<GameState, bool>? _playerHasMostWins;

        public VictoryConditionService(
            Func<GameState, int>? getTotalOpponents = null,
            Func<GameState, bool>? playerHasMostWins = null)
        {
            _getTotalOpponents = getTotalOpponents;
            _playerHasMostWins = playerHasMostWins;

            // Register all victory conditions
            _victoryConditions = new Dictionary<string, IVictoryCondition>
            {
                ["Reputation"] = new ReputationVictory(),
                ["King"] = new KingVictory(),
                ["PinkSlipCollector"] = new PinkSlipCollectorVictory(),
                ["SeasonChampion"] = new SeasonChampionVictory(),
                ["Domination"] = new DominationVictory()
            };
        }

        public IEnumerable<IVictoryCondition> GetAllVictoryConditions()
        {
            return _victoryConditions.Values;
        }

        public IEnumerable<IVictoryCondition> GetUnlockedVictoryConditions(CareerState career)
        {
            return _victoryConditions.Values.Where(v => v.IsUnlocked(career));
        }

        public IVictoryCondition? GetVictoryCondition(string victoryType)
        {
            return _victoryConditions.TryGetValue(victoryType, out var condition) ? condition : null;
        }

        public IVictoryCondition? CheckForVictory(GameState gameState)
        {
            RefreshStanding(gameState);

            // The path the player set out on is the one that wins; with none picked (or one the game no longer
            // knows), whichever is reached first
            if (gameState.Career.ActiveVictoryType is { } active && _victoryConditions.TryGetValue(active, out var chosen))
            {
                return chosen.IsAchieved(gameState.Career) ? chosen : null;
            }

            return _victoryConditions.Values.FirstOrDefault(c => c.IsAchieved(gameState.Career));
        }

        public IVictoryCondition? ClaimVictory(GameState gameState)
        {
            if (gameState.Career.HasWonGame)
            {
                // Won already: the Career screen still shows where the player stands
                RefreshStanding(gameState);
                return null;
            }

            if (CheckForVictory(gameState) is not { } victory)
                return null;

            gameState.Career.HasWonGame = true;
            gameState.Career.WinningVictoryType = victory.VictoryType;
            return victory;
        }

        public VictoryProgress GetProgress(string victoryType, CareerState career)
        {
            if (_victoryConditions.TryGetValue(victoryType, out var condition))
            {
                return condition.GetProgress(career);
            }

            return VictoryProgress.NotStarted("Unknown victory type");
        }

        public Dictionary<string, VictoryProgress> GetAllProgress(CareerState career)
        {
            var progress = new Dictionary<string, VictoryProgress>();

            foreach (var (type, condition) in _victoryConditions)
            {
                progress[type] = condition.GetProgress(career);
            }

            return progress;
        }

        public List<IVictoryCondition> CheckForNewUnlocks(CareerState career)
        {
            var newlyUnlocked = new List<IVictoryCondition>();

            foreach (var condition in _victoryConditions.Values)
            {
                if (condition.IsUnlocked(career) &&
                    !career.UnlockedVictories.ContainsKey(condition.VictoryType))
                {
                    career.UnlockedVictories[condition.VictoryType] = true;
                    newlyUnlocked.Add(condition);
                }
            }

            return newlyUnlocked;
        }

        /// <summary>
        /// The figures the victories need from the whole game (how many racers, who has the most wins, the King's
        /// name), put on the career where every victory reads them
        /// </summary>
        public void RefreshStanding(GameState gameState)
        {
            var career = gameState.Career;
            if (_getTotalOpponents != null) career.TotalOpponents = _getTotalOpponents(gameState);
            if (_playerHasMostWins != null) career.PlayerHasMostWins = _playerHasMostWins(gameState);

            var racers = gameState.Racers;
            career.KingName = racers.All
                .OfType<Opponent>().FirstOrDefault(o => o.IsKing)?.Name ?? career.KingName;
        }
    }
}
