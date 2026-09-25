using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Storage;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>The save after buying or selling a car, the same way for both</summary>
    internal static class GameSaves
    {
        /// <summary>
        /// Saves the game; false when that failed (logged). A game without a save name (one being set up, a test)
        /// has nowhere to go and counts as saved.
        /// </summary>
        public static bool TrySave(IGameStateRepository repository, GameState gameState, IAppLogger logger, string after)
        {
            if (string.IsNullOrEmpty(gameState.SaveName)) return true;

            try
            {
                repository.Save(gameState, gameState.SaveName);
                logger.Information("Game saved after {After}", after);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not save the game after {After}", after);
                return false;
            }
        }
    }
}
