namespace Street_Rod_AC.Services.Configuration.Models
{
    /// <summary>
    /// Base class for all modification intents.
    /// Intent declares WHAT to change, not HOW to change it.
    /// </summary>
    public abstract class ModificationIntent
    {
        /// <summary>
        /// Human-readable description of this intent
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Target INI file (relative to AC cfg directory or absolute path)
        /// </summary>
        public abstract string TargetFile { get; }
    }

    /// <summary>
    /// Intent to configure showroom for a specific car and skin
    /// </summary>
    public class ShowroomIntent : ModificationIntent
    {
        public string CarId { get; set; } = string.Empty;
        public string SkinId { get; set; } = string.Empty;
        public string Track { get; set; } = "showroom";

        public override string Description =>
            $"Configure showroom for car '{CarId}' with skin '{SkinId}'";

        public override string TargetFile => "showroom_start.ini";

        public ShowroomIntent()
        {
        }

        public ShowroomIntent(string carId, string skinId, string track = "showroom")
        {
            CarId = carId;
            SkinId = skinId;
            Track = track;
        }
    }

    /// <summary>
    /// Intent to disable all driving assists (for career mode realism)
    /// </summary>
    public class DisableAssistsIntent : ModificationIntent
    {
        public override string Description => "Disable all driving assists for career mode";
        public override string TargetFile => "assists.ini";
    }

    /// <summary>
    /// Intent to configure a race session
    /// </summary>
    public class RaceConfigIntent : ModificationIntent
    {
        public string CarId { get; set; } = string.Empty;
        public string SkinId { get; set; } = string.Empty;
        public string TrackId { get; set; } = string.Empty;
        public string? TrackConfig { get; set; }

        public override string Description =>
            $"Configure race: car '{CarId}', track '{TrackId}'";

        public override string TargetFile => "race.ini";
    }

    /// <summary>
    /// Intent to configure a drag race using the drag_race.ini template
    /// </summary>
    public class DragRaceIntent : ModificationIntent
    {
        public string PlayerCarId { get; set; } = string.Empty;
        public string PlayerSkin { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string OpponentCarId { get; set; } = string.Empty;
        public string OpponentSkin { get; set; } = string.Empty;
        public string OpponentName { get; set; } = string.Empty;

        /// <summary>
        /// Opponent AI skill level (80-100)
        /// </summary>
        public int OpponentAILevel { get; set; } = 90;

        /// <summary>
        /// Opponent AI aggression (0-100)
        /// </summary>
        public int OpponentAIAggression { get; set; } = 50;

        public override string Description =>
            $"Configure drag race: {PlayerName} ({PlayerCarId}) vs {OpponentName} ({OpponentCarId}) [AI: {OpponentAILevel}/{OpponentAIAggression}]";

        public override string TargetFile => "race.ini";
    }
}
