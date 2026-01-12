using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for handling opponent challenge logic (acceptance/decline decisions)
    /// </summary>
    public interface IOpponentChallengeService
    {
        /// <summary>
        /// Evaluate whether an opponent accepts a challenge
        /// </summary>
        /// <param name="opponent">The opponent being challenged</param>
        /// <param name="player">The player issuing the challenge</param>
        /// <param name="playerCar">The player's car</param>
        /// <param name="opponentCar">The opponent's car</param>
        /// <param name="isPinkSlip">Whether this is a pink slip race</param>
        /// <param name="cashWager">Cash wager amount (if not pink slip)</param>
        /// <returns>Challenge response with acceptance decision and reason</returns>
        ChallengeResponse EvaluateChallenge(
            Opponent opponent,
            Player player,
            Car playerCar,
            Car opponentCar,
            bool isPinkSlip,
            decimal cashWager = 0);
    }

    /// <summary>
    /// Response from an opponent to a race challenge
    /// </summary>
    public class ChallengeResponse
    {
        public bool Accepted { get; set; }
        public string Message { get; set; } = string.Empty;
        public ChallengeDeclineReason? DeclineReason { get; set; }
    }

    /// <summary>
    /// Reasons why an opponent might decline a challenge
    /// </summary>
    public enum ChallengeDeclineReason
    {
        ReputationTooLow,
        ReputationTooHigh,
        BetTooHigh,
        InsufficientFunds,
        CarValueMismatch,
        RecentlyLost,
        TooRisky,
        NotInterested,
        CarNotAvailable
    }
}
