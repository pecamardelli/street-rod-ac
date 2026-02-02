using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for selecting opponents for race events
    /// </summary>
    public interface IEventOpponentService
    {
        /// <summary>
        /// Get an opponent for an event.
        /// Returns pool opponent if available, otherwise special opponent.
        /// </summary>
        EventOpponentResult? GetOpponentForEvent(
            RaceEventDefinition eventDef,
            GameState gameState);
    }

    /// <summary>
    /// Result of opponent selection for an event
    /// </summary>
    public class EventOpponentResult
    {
        /// <summary>
        /// Whether this is a pool opponent (tracked in game state) or event-only
        /// </summary>
        public bool IsPoolOpponent { get; set; }

        /// <summary>
        /// The pool opponent (if IsPoolOpponent is true)
        /// </summary>
        public Opponent? PoolOpponent { get; set; }

        /// <summary>
        /// The pool opponent's car (if IsPoolOpponent is true)
        /// </summary>
        public Car? PoolOpponentCar { get; set; }

        /// <summary>
        /// The special opponent (if IsPoolOpponent is false)
        /// </summary>
        public EventOpponent? SpecialOpponent { get; set; }

        // Common properties for easy access
        public string OpponentName { get; set; } = string.Empty;
        public string? OpponentNickname { get; set; }
        public string CarDefinitionId { get; set; } = string.Empty;
        public string CarSkin { get; set; } = "default";
        public int Skill { get; set; }
        public int Aggression { get; set; }
    }
}
