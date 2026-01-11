using Street_Rod_AC.Services.Configuration.Models;

namespace Street_Rod_AC.Services.Configuration.Models
{
    /// <summary>
    /// Base class for all Assetto Corsa launch intents.
    /// Launch intents represent high-level user actions that generate one or more configuration intents.
    /// </summary>
    public abstract class LaunchIntent
    {
        /// <summary>
        /// The executable to launch (acs.exe or acShowroom.exe)
        /// </summary>
        public abstract string Executable { get; }

        /// <summary>
        /// Description of what this launch intent will do
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Generate all required modification intents for this launch
        /// </summary>
        public abstract IEnumerable<ModificationIntent> GetConfigurationIntents();

        /// <summary>
        /// Optional metadata for tracking execution context
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Intent to launch Assetto Corsa showroom with a specific car and skin
    /// </summary>
    public class ShowroomLaunchIntent : LaunchIntent
    {
        public string CarId { get; set; } = string.Empty;
        public string SkinId { get; set; } = string.Empty;
        public string Track { get; set; } = "showroom";

        public override string Executable => "acShowroom.exe";

        public override string Description =>
            $"Launch showroom to preview car '{CarId}' with skin '{SkinId}'";

        public override IEnumerable<ModificationIntent> GetConfigurationIntents()
        {
            yield return new ShowroomIntent
            {
                CarId = this.CarId,
                SkinId = this.SkinId,
                Track = this.Track
            };
        }
    }

    /// <summary>
    /// Intent to launch a full race session with specified configuration
    /// </summary>
    public class RaceLaunchIntent : LaunchIntent
    {
        public string CarId { get; set; } = string.Empty;
        public string SkinId { get; set; } = string.Empty;
        public string TrackId { get; set; } = string.Empty;
        public string? TrackConfig { get; set; }

        public override string Executable => "acs.exe";

        public override string Description =>
            $"Launch race with car '{CarId}' on track '{TrackId}'";

        public override IEnumerable<ModificationIntent> GetConfigurationIntents()
        {
            yield return new RaceConfigIntent
            {
                CarId = this.CarId,
                SkinId = this.SkinId,
                TrackId = this.TrackId,
                TrackConfig = this.TrackConfig
            };

            // Future: Add DisableAssistsIntent, OpponentIntent, etc.
        }
    }

    /// <summary>
    /// Intent to launch a drag race (1v1 quarter mile)
    /// </summary>
    public class DragRaceLaunchIntent : LaunchIntent
    {
        public string PlayerCarId { get; set; } = string.Empty;
        public string PlayerSkin { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string OpponentCarId { get; set; } = string.Empty;
        public string OpponentSkin { get; set; } = string.Empty;
        public string OpponentName { get; set; } = string.Empty;

        /// <summary>
        /// Player's car instance ID (for result correlation)
        /// </summary>
        public Guid PlayerCarInstanceId { get; set; }

        /// <summary>
        /// Opponent's car instance ID (for result correlation)
        /// </summary>
        public Guid OpponentCarInstanceId { get; set; }

        /// <summary>
        /// Cash wager amount (0 if no wager)
        /// </summary>
        public decimal CashWager { get; set; }

        /// <summary>
        /// Whether this is a pink slip race (loser gives up their car)
        /// </summary>
        public bool IsPinkSlip { get; set; }

        public override string Executable => "acs.exe";

        public override string Description =>
            $"Launch drag race: {PlayerName} vs {OpponentName}";

        public override IEnumerable<ModificationIntent> GetConfigurationIntents()
        {
            yield return new DragRaceIntent
            {
                PlayerCarId = this.PlayerCarId,
                PlayerSkin = this.PlayerSkin,
                PlayerName = this.PlayerName,
                OpponentCarId = this.OpponentCarId,
                OpponentSkin = this.OpponentSkin,
                OpponentName = this.OpponentName
            };
        }
    }
}
