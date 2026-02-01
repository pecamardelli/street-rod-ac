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
            // Update dynamic victory conditions before checking
            UpdateDynamicConditions(gameState);

            foreach (var condition in _victoryConditions.Values)
            {
                if (condition.IsAchieved(gameState.Career))
                {
                    return condition;
                }
            }

            return null;
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
        /// Update victory conditions that need external data (total opponents, season status, etc.)
        /// </summary>
        private void UpdateDynamicConditions(GameState gameState)
        {
            // Update Domination victory with total opponent count
            if (_victoryConditions.TryGetValue("Domination", out var domination) &&
                domination is DominationVictory dominationVictory &&
                _getTotalOpponents != null)
            {
                dominationVictory.TotalOpponents = _getTotalOpponents(gameState);
            }

            // Update Season Champion victory
            if (_victoryConditions.TryGetValue("SeasonChampion", out var season) &&
                season is SeasonChampionVictory seasonVictory &&
                _playerHasMostWins != null)
            {
                seasonVictory.PlayerHasMostWins = _playerHasMostWins(gameState);
            }
        }
    }
}
