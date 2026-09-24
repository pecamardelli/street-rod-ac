namespace Street_Rod_AC.Services.Settings
{
    /// <summary>
    /// Game-wide settings stored in settings.json
    /// These settings persist across all save games. The rules of a career (prices, prizes, wear, how hard the
    /// rivals drive) are not here: they belong to its save (<see cref="Models.GameState.GameRules"/>). Keys an older
    /// settings.json still has for them are ignored.
    /// </summary>
    public class GameSettings
    {
        // Where the last free run went: "track" or "track/configuration"
        public string FreeRunTrack { get; set; } = string.Empty;

        /// <summary>
        /// The Assetto Corsa folder; null for the default (<see cref="Street_Rod_AC.Configuration.AppSettings.DefaultAssettoCorsaPath"/>).
        /// Read once, when the settings load at start-up: a change takes effect at the next start.
        /// </summary>
        public string? AssettoCorsaPath { get; set; }
    }
}
