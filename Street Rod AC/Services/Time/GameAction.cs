namespace Street_Rod_AC.Services.Time
{
    /// <summary>
    /// Actions that consume game time
    /// </summary>
    public enum GameAction
    {
        /// <summary>Visit the newspaper to check ads (30 min)</summary>
        VisitNewspaper,

        /// <summary>Buy a car from the market (2 hours)</summary>
        BuyCar,

        /// <summary>Visit the diner to meet opponents (30 min)</summary>
        VisitDiner,

        /// <summary>Compete in a drag race (30 min)</summary>
        DragRace,

        /// <summary>Compete in a road race (1 hour)</summary>
        RoadRace,

        /// <summary>Visit the used parts shop (30 min)</summary>
        VisitUsedParts,

        /// <summary>Buy and install a part (1 hour)</summary>
        BuyPart,

        /// <summary>View a car in the showroom (15 min)</summary>
        ViewShowroom,

        /// <summary>Perform garage work - minor (30 min)</summary>
        GarageWorkMinor,

        /// <summary>Perform garage work - major (2 hours)</summary>
        GarageWorkMajor,

        /// <summary>Sell a car (1 hour)</summary>
        SellCar,

        /// <summary>Switch to a different car in the garage (15 min)</summary>
        SwitchCar
    }

    /// <summary>
    /// Extension methods for GameAction time costs
    /// </summary>
    public static class GameActionExtensions
    {
        /// <summary>
        /// Gets the time cost in minutes for this action
        /// </summary>
        public static int GetTimeInMinutes(this GameAction action)
        {
            return action switch
            {
                GameAction.VisitNewspaper => 30,
                GameAction.BuyCar => 120,
                GameAction.VisitDiner => 30,
                GameAction.DragRace => 30,
                GameAction.RoadRace => 60,
                GameAction.VisitUsedParts => 30,
                GameAction.BuyPart => 60,
                GameAction.ViewShowroom => 15,
                GameAction.GarageWorkMinor => 30,
                GameAction.GarageWorkMajor => 120,
                GameAction.SellCar => 60,
                GameAction.SwitchCar => 15,
                _ => 30 // Default fallback
            };
        }

        /// <summary>
        /// Gets a human-readable description of the time cost
        /// </summary>
        public static string GetTimeDescription(this GameAction action)
        {
            var minutes = action.GetTimeInMinutes();
            if (minutes >= 60)
            {
                var hours = minutes / 60;
                var remainingMinutes = minutes % 60;
                if (remainingMinutes == 0)
                    return hours == 1 ? "1 hour" : $"{hours} hours";
                return $"{hours}h {remainingMinutes}min";
            }
            return $"{minutes} min";
        }
    }
}
