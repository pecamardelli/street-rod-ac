using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Rivals remember: one who lost a pink slip to the player wants a rematch (<see cref="Grudge"/>). They wait at the
    /// diner saying so, and take a pink-slip race with the player whatever the usual odds, for
    /// <see cref="RematchDays"/> days. What each step says goes into the street talk.
    /// </summary>
    public static class Grudges
    {
        /// <summary>How long a rival waits for the rematch</summary>
        public const int RematchDays = 14;

        /// <summary>The rival wants a rematch with the player</summary>
        public static bool WantsRematch(Opponent rival) => rival.Grudge != null;

        /// <summary>
        /// The player took <paramref name="car"/> off <paramref name="rival"/> for pink slips on <paramref name="date"/>:
        /// the rival wants it back. Returns the street's line about it.
        /// </summary>
        public static string Start(Opponent rival, Car car, DateTime date, string playerName, string carName)
        {
            rival.Grudge = new Grudge
            {
                Until = date.Date.AddDays(RematchDays).AddHours(GameState.DayEndHour),
                CarInstanceId = car.InstanceId,
                CarDefinitionId = car.DefinitionId
            };
            return $"{rival.Name} wants {OpponentRules.Possessive(rival)} {carName} back from {playerName}, and tells anyone who'll listen.";
        }

        /// <summary>
        /// A pink-slip race between the player and a rival who wanted the rematch settled it. Returns the street's line
        /// when the rival got even; null when the rival wanted nothing, or lost again (that is a new grudge, <see cref="Start"/>).
        /// </summary>
        public static string? Settle(Opponent rival, bool rivalWon, string playerName)
        {
            if (rival.Grudge == null) return null;

            rival.Grudge = null;
            return rivalWon ? $"{rival.Name} got even with {playerName} in a pink-slip rematch." : null;
        }

        /// <summary>
        /// <paramref name="racer"/> has just come by <paramref name="car"/> other than in the rematch (bought it, won it
        /// off somebody else): when it is the car they wanted back, there is nothing left to settle and the rematch is off.
        /// </summary>
        public static void CarBack(Racer racer, Car car)
        {
            if (racer is Opponent { Grudge: { } grudge } rival && grudge.CarInstanceId == car.InstanceId) rival.Grudge = null;
        }

        /// <summary>
        /// The daily review: a rematch nobody came for is off, and the street hears the rival has let it go. A rival
        /// out of the game (retired, no car) keeps waiting until the offer runs out like anybody else.
        /// </summary>
        public static void Lapse(IEnumerable<Opponent> rivals, DateTime date, Func<string, string> carName, List<string> talk)
        {
            foreach (var rival in rivals)
            {
                if (rival.Grudge is not { } grudge) continue;

                // Theirs again by some way the game did not see to: nothing to talk about
                if (rival.Cars.Any(c => c.InstanceId == grudge.CarInstanceId))
                {
                    rival.Grudge = null;
                    continue;
                }

                if (grudge.Until >= date) continue;

                rival.Grudge = null;
                talk.Add($"{rival.Name} has stopped talking about getting {OpponentRules.Possessive(rival)} {carName(grudge.CarDefinitionId)} back.");
            }
        }
    }
}
