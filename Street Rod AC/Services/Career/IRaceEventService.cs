using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for managing race events/invitations
    /// </summary>
    public interface IRaceEventService
    {
        /// <summary>
        /// Get all event definitions
        /// </summary>
        IEnumerable<RaceEventDefinition> GetAllEventDefinitions();

        /// <summary>
        /// Get an event definition by ID
        /// </summary>
        RaceEventDefinition? GetEventDefinition(string eventId);

        /// <summary>
        /// Get events the player is eligible for based on milestones and reputation
        /// </summary>
        IEnumerable<RaceEventDefinition> GetEligibleEvents(CareerState career);

        /// <summary>
        /// Get currently active event instances
        /// </summary>
        IEnumerable<RaceEventInstance> GetActiveEvents(CareerState career, DateTime currentTime);

        /// <summary>
        /// Generate new events for the current time period (called by scheduler)
        /// </summary>
        List<RaceEventInstance> GenerateEvents(CareerState career, DateTime currentTime);

        /// <summary>
        /// Check if the player can enter a specific event with a specific car
        /// </summary>
        bool CanEnterEvent(string eventId, string carDefinitionId, Car? carInstance, CareerState career);

        /// <summary>
        /// Complete an event and apply rewards
        /// </summary>
        EventReward? CompleteEvent(Guid eventInstanceId, bool playerWon, CareerState career, DateTime completedAt);

        /// <summary>
        /// Remove expired events from active events list
        /// </summary>
        int CleanupExpiredEvents(CareerState career, DateTime currentTime);
    }
}
