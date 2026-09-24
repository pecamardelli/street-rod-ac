namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// Something the player should be told after a race (an event reward, a forfeit, a milestone). Services
    /// return these instead of opening dialogs; the screen that ran the race shows them, one at a time.
    /// </summary>
    public sealed record PlayerMessage(string Title, string Text)
    {
        /// <summary>A drag race's timeslips, shown as a timeslip rather than as text; null for any other message</summary>
        public TimeslipCard? Timeslip { get; init; }

        /// <summary>
        /// The player's car was towed home after a crash: the race hands the player back in the garage, where the car
        /// is, rather than on the streets
        /// </summary>
        public bool TowedToGarage { get; init; }
    }

    /// <summary>Both lanes' timeslips of a drag race, as the strip hands them out</summary>
    public sealed record TimeslipCard(string PlayerName, Timeslip? Player, string OpponentName, Timeslip? Opponent);
}
