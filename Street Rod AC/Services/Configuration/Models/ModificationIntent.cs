using Street_Rod_AC.Models.Race;

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
        /// Track folder ID (e.g., "ks_drag")
        /// </summary>
        public string TrackId { get; set; } = "ks_drag";

        /// <summary>
        /// Track configuration/layout (e.g., "drag1000")
        /// </summary>
        public string? TrackConfig { get; set; } = "drag1000";

        /// <summary>
        /// Opponent AI skill level (90-100)
        /// </summary>
        public int OpponentAILevel { get; set; } = 90;

        /// <summary>
        /// Opponent AI aggression (0-100)
        /// </summary>
        public int OpponentAIAggression { get; set; } = 50;

        /// <summary>
        /// Type of race (drag or circuit)
        /// </summary>
        public RaceType RaceType { get; set; } = RaceType.DragRace;

        /// <summary>AC's damage for the race, in percent (the difficulty's; 100 is AC's full rate)</summary>
        public int DamagePercent { get; set; } = IniModificationService.RaceDamage;

        /// <summary>
        /// The race's <see cref="RaceContext.ContextId"/>, written to race.ini as [STREET_ROD] CONTEXT_ID so the Lua
        /// app can put it into the result; null for a launch without a context (nothing is written then)
        /// </summary>
        public Guid? ContextId { get; set; }

        /// <summary>The player's car's damage for the race mode to put into AC at the start ([STREET_ROD] CAR_0_*)</summary>
        public Street_Rod_AC.Models.Race.RaceStartState? PlayerStart { get; set; }

        /// <summary>The opponent's car's damage ([STREET_ROD] CAR_1_*)</summary>
        public Street_Rod_AC.Models.Race.RaceStartState? OpponentStart { get; set; }

        /// <summary>The police: [CAR_2] on, and [STREET_ROD] POLICE and POLICE_SPOT for the race mode; null for none</summary>
        public Street_Rod_AC.Models.Race.RacePolice? Police { get; set; }

        /// <summary>The time of day the race starts at, for AC's sun ([LIGHTING] SUN_ANGLE); null is noon</summary>
        public DateTime? RaceTime { get; set; }

        public override string Description =>
            $"Configure drag race: {PlayerName} ({PlayerCarId}) vs {OpponentName} ({OpponentCarId}) [AI: {OpponentAILevel}/{OpponentAIAggression}]";

        public override string TargetFile => "race.ini";
    }

    /// <summary>
    /// Intent to configure a free run: the player alone on a track, a practice session the player ends when
    /// they have had enough (no auto-start, no auto-quit, no results)
    /// </summary>
    public class FreeRunIntent : ModificationIntent
    {
        public string CarId { get; set; } = string.Empty;
        public string Skin { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string TrackId { get; set; } = string.Empty;
        public string? TrackConfig { get; set; }

        public override string Description => $"Configure free run: {CarId} on {TrackId}{(string.IsNullOrEmpty(TrackConfig) ? "" : "/" + TrackConfig)}";

        public override string TargetFile => "race.ini";
    }
}
