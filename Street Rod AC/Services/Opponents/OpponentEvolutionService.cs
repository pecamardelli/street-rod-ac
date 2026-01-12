using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for evolving opponent traits based on race outcomes.
    /// Implements small, incremental changes (±1-2 for skill) based on events.
    /// Changes are applied immediately to the opponent object - caller is responsible for persistence.
    /// </summary>
    public class OpponentEvolutionService : IOpponentEvolutionService
    {
        private readonly Random _random;
        private readonly IAppLogger _logger;

        public OpponentEvolutionService()
        {
            _random = new Random();
            _logger = AppLoggerFactory.CreateLogger("OpponentEvolution");
        }

        /// <summary>
        /// Apply evolution after a race victory
        /// Win increases skill slightly and may increase aggression (especially for dominant wins)
        /// </summary>
        public void ApplyWinEvolution(Opponent opponent, bool isDominantWin = false)
        {
            var oldSkill = opponent.Skill;
            var oldAggression = opponent.Aggression;

            // Skill increases slightly from experience (±1-2)
            var skillChange = _random.Next(1, 3);
            opponent.AdjustSkill(skillChange);

            // Aggression may increase (winning builds confidence)
            int aggressionChange;
            if (isDominantWin)
            {
                // Dominant win: larger aggression boost (3-6)
                aggressionChange = _random.Next(3, 7);
            }
            else
            {
                // Regular win: small aggression boost (0-3)
                aggressionChange = _random.Next(0, 4);
            }

            opponent.AdjustAggression(aggressionChange);

            _logger.Information("Win evolution for {Name}: Skill {OldSkill}->{NewSkill}, Aggression {OldAggression}->{NewAggression} (Dominant: {IsDominant})",
                opponent.Name, oldSkill, opponent.Skill, oldAggression, opponent.Aggression, isDominantWin);
        }

        /// <summary>
        /// Apply evolution after a race loss
        /// Loss may increase or maintain skill (learning from defeat) but tends to decrease aggression
        /// </summary>
        public void ApplyLossEvolution(Opponent opponent, bool isBadlyBeaten = false)
        {
            var oldSkill = opponent.Skill;
            var oldAggression = opponent.Aggression;

            // Skill may still increase slightly (learning from the loss) or stay the same (±0-2)
            var skillChange = _random.Next(0, 3);
            opponent.AdjustSkill(skillChange);

            // Aggression tends to decrease (becomes more cautious)
            int aggressionChange;
            if (isBadlyBeaten)
            {
                // Badly beaten: significant aggression drop (-8 to -3)
                aggressionChange = _random.Next(-8, -2);
            }
            else
            {
                // Close loss: small aggression drop (-4 to 0)
                aggressionChange = _random.Next(-4, 1);
            }

            opponent.AdjustAggression(aggressionChange);

            _logger.Information("Loss evolution for {Name}: Skill {OldSkill}->{NewSkill}, Aggression {OldAggression}->{NewAggression} (Badly Beaten: {IsBadlyBeaten})",
                opponent.Name, oldSkill, opponent.Skill, oldAggression, opponent.Aggression, isBadlyBeaten);
        }

        /// <summary>
        /// Apply evolution after a crash or DNF
        /// Crashes significantly reduce aggression (driver becomes more cautious)
        /// Skill may slightly decrease (loss of confidence)
        /// </summary>
        public void ApplyCrashEvolution(Opponent opponent)
        {
            var oldSkill = opponent.Skill;
            var oldAggression = opponent.Aggression;

            // Skill may decrease slightly from loss of confidence (-2 to 0)
            var skillChange = _random.Next(-2, 1);
            opponent.AdjustSkill(skillChange);

            // Aggression drops significantly (becomes much more cautious) (-10 to -5)
            var aggressionChange = _random.Next(-10, -4);
            opponent.AdjustAggression(aggressionChange);

            _logger.Information("Crash evolution for {Name}: Skill {OldSkill}->{NewSkill}, Aggression {OldAggression}->{NewAggression}",
                opponent.Name, oldSkill, opponent.Skill, oldAggression, opponent.Aggression);
        }

        /// <summary>
        /// Apply evolution after a close race (regardless of outcome)
        /// Close races increase skill more than normal (intense competition improves ability)
        /// </summary>
        public void ApplyCloseRaceEvolution(Opponent opponent)
        {
            var oldSkill = opponent.Skill;

            // Close races are intense learning experiences (skill increases 1-2)
            var skillChange = _random.Next(1, 3);
            opponent.AdjustSkill(skillChange);

            _logger.Information("Close race evolution for {Name}: Skill {OldSkill}->{NewSkill}",
                opponent.Name, oldSkill, opponent.Skill);
        }
    }
}
