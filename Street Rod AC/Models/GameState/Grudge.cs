namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// A rival who lost a pink slip to the player wants a rematch: they wait at the diner, say so, and take on a
    /// pink-slip race with the player whatever the usual odds, until <see cref="Until"/>. Racing them for pink slips
    /// settles it, win or lose; the daily review lets it lapse (<see cref="Services.Opponents.Grudges"/>).
    /// </summary>
    public class Grudge
    {
        /// <summary>The day they lost the car, in game time</summary>
        public DateTime Since { get; set; }

        /// <summary>The last day the offer stands, in game time</summary>
        public DateTime Until { get; set; }

        /// <summary>The car they lost</summary>
        public Guid CarInstanceId { get; set; }

        /// <summary>Its model, to name it when the player no longer has it</summary>
        public string CarDefinitionId { get; set; } = string.Empty;
    }
}
