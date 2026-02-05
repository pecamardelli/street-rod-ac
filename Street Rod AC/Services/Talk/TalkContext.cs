using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Talk
{
    /// <summary>
    /// Context for generating opponent dialogue
    /// </summary>
    public class TalkContext
    {
        /// <summary>
        /// The opponent speaking
        /// </summary>
        public required Opponent Opponent { get; set; }

        /// <summary>
        /// The player being addressed
        /// </summary>
        public required Player Player { get; set; }

        /// <summary>
        /// The opponent's car definition (for contextual dialogue)
        /// </summary>
        public CarDefinition? OpponentCar { get; set; }

        /// <summary>
        /// The player's selected car definition (for contextual dialogue)
        /// </summary>
        public CarDefinition? PlayerCar { get; set; }

        /// <summary>
        /// What triggered this dialogue
        /// </summary>
        public TalkTrigger Trigger { get; set; } = TalkTrigger.OpponentSelected;

        /// <summary>
        /// Whether a pink slip bet is being proposed
        /// </summary>
        public bool IsPinkSlipBet { get; set; }

        /// <summary>
        /// Selected track name (for track-specific dialogue)
        /// </summary>
        public string? SelectedTrackName { get; set; }
    }

    /// <summary>
    /// Events that can trigger opponent dialogue
    /// </summary>
    public enum TalkTrigger
    {
        /// <summary>Player clicked on opponent in the list</summary>
        OpponentSelected,

        /// <summary>Player selected a track for racing</summary>
        TrackSelected,

        /// <summary>Player hovering over challenge button</summary>
        ChallengeHover,

        /// <summary>Player changed bet type to pink slips</summary>
        BetTypeChanged,

        /// <summary>Opponent accepted the challenge</summary>
        ChallengeAccepted,

        /// <summary>Opponent rejected the challenge</summary>
        ChallengeRejected,

        /// <summary>Custom/AI-generated contextual response</summary>
        Custom
    }
}
