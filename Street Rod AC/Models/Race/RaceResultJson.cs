using Newtonsoft.Json;

namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// Root schema for race result JSON files from sr_race_manager (Lua app)
    /// Mirrors the Lua app output structure for deserialization
    /// </summary>
    public class RaceResultJson
    {
        [JsonProperty("metadata")]
        public RaceMetadata Metadata { get; set; } = new();

        [JsonProperty("session")]
        public RaceSession Session { get; set; } = new();

        [JsonProperty("participants")]
        public List<RaceParticipant> Participants { get; set; } = new();

        /// <summary>
        /// The police chase, when the race had police sent to it (schema 1.5); null otherwise and in older files. The
        /// police cars are never among the <see cref="Participants"/>.
        /// </summary>
        [JsonProperty("pursuit")]
        public PursuitResult? Pursuit { get; set; }
    }

    /// <summary>How the police chase went</summary>
    public class PursuitResult
    {
        /// <summary>How many police cars were sent</summary>
        [JsonProperty("police")]
        public int Police { get; set; }

        /// <summary>The patrol showed up; false when the race was over before it got there</summary>
        [JsonProperty("started")]
        public bool Started { get; set; }

        /// <summary>Seconds into the session the patrol showed up</summary>
        [JsonProperty("started_at_s")]
        public double? StartedAtSeconds { get; set; }

        [JsonProperty("duration_s")]
        public double? DurationSeconds { get; set; }

        /// <summary>The player's chase, one of <see cref="PursuitOutcomes"/>; null when there was none</summary>
        [JsonProperty("player")]
        public string? Player { get; set; }

        /// <summary>The rival's chase, one of <see cref="PursuitOutcomes"/>; null when there was none</summary>
        [JsonProperty("rival")]
        public string? Rival { get; set; }

        public bool PlayerBusted => Started && Player == PursuitOutcomes.Busted;
        public bool RivalBusted => Started && Rival == PursuitOutcomes.Busted;
        public bool PlayerEscaped => Started && Player == PursuitOutcomes.Escaped;
    }

    /// <summary>How a racer's police chase ended</summary>
    public static class PursuitOutcomes
    {
        public const string Escaped = "ESCAPED";
        public const string Busted = "BUSTED";
    }

    /// <summary>
    /// Metadata about the race result file
    /// Used for validation and version tracking
    /// </summary>
    public class RaceMetadata
    {
        [JsonProperty("schema_version")]
        public string SchemaVersion { get; set; } = string.Empty;

        [JsonProperty("script_version")]
        public string ScriptVersion { get; set; } = string.Empty;

        [JsonProperty("source")]
        public string Source { get; set; } = string.Empty;  // Must be "sr_race_manager"

        [JsonProperty("generated_at")]
        public string GeneratedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Race session information
    /// Contains timing, track, and session identification data
    /// </summary>
    public class RaceSession
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; } = string.Empty;  // Primary deduplication key (UUID)

        [JsonProperty("start_timestamp")]
        public string StartTimestamp { get; set; } = string.Empty;

        [JsonProperty("end_timestamp")]
        public string EndTimestamp { get; set; } = string.Empty;

        [JsonProperty("duration_seconds")]
        public double DurationSeconds { get; set; }

        [JsonProperty("track_id")]
        public string TrackId { get; set; } = string.Empty;

        [JsonProperty("track_layout")]
        public string? TrackLayout { get; set; }

        /// <summary>
        /// The <see cref="RaceContext.ContextId"/> the launcher wrote into race.ini ([STREET_ROD] CONTEXT_ID),
        /// echoed back by the Lua app. Ties the file to the race it came from; absent in files written before
        /// schema 1.1 or when race.ini had no id.
        /// </summary>
        [JsonProperty("context_id")]
        public string? ContextId { get; set; }

        /// <summary>
        /// How the race mode ended the race (schema 1.2): <see cref="EndReasons"/>. Null in older files, whose
        /// outcome comes from the positions and crashes alone.
        /// </summary>
        [JsonProperty("end_reason")]
        public string? EndReason { get; set; }

        /// <summary>
        /// The kind of race the mode ran, as race.ini told it (schema 1.3): <c>DRAG</c> or <c>ROAD</c>, or
        /// <see cref="RaceTypes.TestAndTune"/> (schema 1.6). The race's context decides; this is only checked against
        /// it. Null in older files.
        /// </summary>
        [JsonProperty("race_type")]
        public string? RaceType { get; set; }
    }

    /// <summary>The <see cref="RaceSession.RaceType"/> values the race mode writes</summary>
    public static class RaceTypes
    {
        public const string Drag = "DRAG";
        public const string Road = "ROAD";

        /// <summary>Test-and-tune: the player alone on the strip, pass after pass (schema 1.6)</summary>
        public const string TestAndTune = "TUNE";
    }

    /// <summary>The <see cref="RaceSession.EndReason"/> values the race mode writes</summary>
    public static class EndReasons
    {
        /// <summary>The player crossed the line</summary>
        public const string Finished = "FINISHED";

        /// <summary>The player crashed</summary>
        public const string Crash = "CRASH";

        /// <summary>The player's car moved before the green, or AC put it back before it got anywhere</summary>
        public const string FalseStart = "FALSE_START";

        /// <summary>AC put the player's car back in the middle of the race: the pits, a lane violation</summary>
        public const string Abandoned = "ABANDONED";

        /// <summary>The player hit the rival out of their own lane in a drag race</summary>
        public const string Disqualified = "DISQUALIFIED";

        /// <summary>The player's car broke down: the engine blew, the gearbox or a corner gave out, a tyre blew (schema 1.4)</summary>
        public const string BrokeDown = "BROKE_DOWN";

        /// <summary>The police caught the player before the finish (schema 1.5)</summary>
        public const string Busted = "BUSTED";
    }

    /// <summary>What gave out in a <see cref="RaceParticipant.Breakdown"/></summary>
    public static class Breakdowns
    {
        public const string Engine = "ENGINE";
        public const string Gearbox = "GEARBOX";
        public const string Suspension = "SUSPENSION";
        public const string Tyre = "TYRE";

        /// <summary>The engine boiled over: its heat took the last of its life (schema 1.7)</summary>
        public const string Overheat = "OVERHEAT";

        /// <summary>The sump ran dry and took the last of the engine's life (schema 1.7)</summary>
        public const string Oil = "OIL";

        /// <summary>The tank ran dry (schema 1.7)</summary>
        public const string Fuel = "FUEL";

        /// <summary>The breakdown in the player's words: "engine", "gearbox"...</summary>
        public static string Describe(string? breakdown) => breakdown switch
        {
            Engine or Overheat or Oil => "engine",
            Gearbox => "gearbox",
            Suspension => "suspension",
            Tyre => "tyre",
            _ => "car"
        };

        /// <summary>What happened, for a sentence that starts with whose car it was: "engine gave out", "car ran out of gas"</summary>
        public static string WhatHappened(string? breakdown) => breakdown switch
        {
            Overheat => "engine boiled over",
            Oil => "engine lost its oil pressure",
            Fuel => "car ran out of gas",
            _ => Describe(breakdown) + " gave out"
        };
    }

    /// <summary>
    /// Individual participant (driver) data in the race
    /// Contains performance metrics and crash data
    /// </summary>
    public class RaceParticipant
    {
        [JsonProperty("driver_name")]
        public string DriverName { get; set; } = string.Empty;

        [JsonProperty("car_name")]
        public string CarName { get; set; } = string.Empty;

        /// <summary>AC's index of the car (0 is the player's); null in files written before schema 1.1</summary>
        [JsonProperty("car_index")]
        public int? CarIndex { get; set; }

        /// <summary>True for the player's car; null in files written before schema 1.1</summary>
        [JsonProperty("is_player")]
        public bool? IsPlayer { get; set; }

        /// <summary>This car jumped the start (schema 1.2); null in older files</summary>
        [JsonProperty("false_start")]
        public bool? FalseStart { get; set; }

        /// <summary>
        /// This car hit the other one out of its own lane in a drag race and was disqualified (schema 1.3); null in
        /// older files
        /// </summary>
        [JsonProperty("disqualified")]
        public bool? Disqualified { get; set; }

        /// <summary>This car broke down during the race and was out of it (schema 1.4); null in older files</summary>
        [JsonProperty("broke_down")]
        public bool? BrokeDown { get; set; }

        /// <summary>What gave out, one of <see cref="Breakdowns"/>; null when nothing did</summary>
        [JsonProperty("breakdown")]
        public string? Breakdown { get; set; }

        /// <summary>What the race left of the car, as AC tracks it (schema 1.2); null in older files</summary>
        [JsonProperty("condition")]
        public RaceCarCondition? Condition { get; set; }

        /// <summary>
        /// The car's run down the strip, for a drag race (schema 1.4); on a test-and-tune the best of its passes. Null
        /// for a road race and older files.
        /// </summary>
        [JsonProperty("timeslip")]
        public Timeslip? Timeslip { get; set; }

        /// <summary>Every pass of a test-and-tune, in order (schema 1.6); null in any other race</summary>
        [JsonProperty("passes")]
        public List<Timeslip>? Passes { get; set; }

        /// <summary>The car's dial-in in a bracket race, in seconds over the quarter (schema 1.6); null in any other race</summary>
        [JsonProperty("dial_in_s")]
        public double? DialInSeconds { get; set; }

        /// <summary>The car ran quicker than its dial-in in a bracket race (schema 1.6); null in any other race</summary>
        [JsonProperty("breakout")]
        public bool? Breakout { get; set; }

        [JsonProperty("performance")]
        public ParticipantPerformance Performance { get; set; } = new();

        [JsonProperty("crash")]
        public CrashData Crash { get; set; } = new();
    }

    /// <summary>
    /// Performance metrics for a participant
    /// Includes lap times, speed and distance (the fuel left is in the car's condition)
    /// </summary>
    public class ParticipantPerformance
    {
        [JsonProperty("final_position")]
        public int? FinalPosition { get; set; }

        [JsonProperty("laps_completed")]
        public int LapsCompleted { get; set; }

        [JsonProperty("best_lap_time_ms")]
        public double? BestLapTimeMs { get; set; }

        [JsonProperty("total_race_time_ms")]
        public double TotalRaceTimeMs { get; set; }

        [JsonProperty("max_speed_kmh")]
        public double MaxSpeedKmh { get; set; }

        [JsonProperty("distance_km")]
        public double DistanceKm { get; set; }
    }

    /// <summary>
    /// Crash detection data for a participant
    /// Tracks crash status, intensity, and timing
    /// </summary>
    public class CrashData
    {
        [JsonProperty("crashed")]
        public bool Crashed { get; set; }

        [JsonProperty("crash_intensities_g")]
        public List<double> CrashIntensitiesG { get; set; } = new();

        [JsonProperty("max_crash_intensity_g")]
        public double MaxCrashIntensityG { get; set; }

        [JsonProperty("crash_timestamp")]
        public string? CrashTimestamp { get; set; }
    }

    /// <summary>
    /// A car as AC left it at the end of the race. Every field is read on its own in the race mode, so any may be
    /// missing; a missing one changes nothing.
    /// </summary>
    public class RaceCarCondition
    {
        /// <summary>Body damage by zone (front, rear, left, right), the collision speed in km/h</summary>
        [JsonProperty("body_damage_kmh")]
        public List<double>? BodyDamageKmh { get; set; }

        /// <summary>1000 for a new engine, 0 or less for a blown one</summary>
        [JsonProperty("engine_life")]
        public double? EngineLife { get; set; }

        /// <summary>What this race did to the gearbox: 0 nothing, 1 non-functional. AC starts every race at 0.</summary>
        [JsonProperty("gearbox_damage")]
        public double? GearboxDamage { get; set; }

        [JsonProperty("water_temperature_c")]
        public double? WaterTemperatureC { get; set; }

        [JsonProperty("oil_temperature_c")]
        public double? OilTemperatureC { get; set; }

        [JsonProperty("oil_pressure")]
        public double? OilPressure { get; set; }

        [JsonProperty("fuel_litres")]
        public double? FuelLitres { get; set; }

        /// <summary>What the tank holds (schema 1.7)</summary>
        [JsonProperty("max_fuel_litres")]
        public double? MaxFuelLitres { get; set; }

        /// <summary>The body's dirt, 0 clean to 1 filthy, as the race left it: it went in as the car carried it (schema 1.7)</summary>
        [JsonProperty("dirt")]
        public double? Dirt { get; set; }

        /// <summary>How the engine's heat and oil went, as the race mode runs them (schema 1.7)</summary>
        [JsonProperty("heat")]
        public RaceHeatReport? Heat { get; set; }

        /// <summary>Front left, front right, rear left, rear right</summary>
        [JsonProperty("wheels")]
        public List<WheelCondition>? Wheels { get; set; }
    }

    /// <summary>
    /// The engine's heat and oil over a race, as the race mode runs them (its engineHeat). The engine life they took is
    /// already in <see cref="RaceCarCondition.EngineLife"/>; these say what took it.
    /// </summary>
    public class RaceHeatReport
    {
        /// <summary>The hottest the water got, °C</summary>
        [JsonProperty("peak_water_c")]
        public double? PeakWaterC { get; set; }

        /// <summary>How long the engine ran hot enough to go flat, seconds</summary>
        [JsonProperty("overheated_s")]
        public double? OverheatedSeconds { get; set; }

        /// <summary>The most power the heat took, as AC's restrictor (0 none, 250 at the boil)</summary>
        [JsonProperty("peak_fade")]
        public double? PeakFade { get; set; }

        /// <summary>Engine life the heat took, as AC counts it (1000 a new engine)</summary>
        [JsonProperty("heat_life_lost")]
        public double? HeatLifeLost { get; set; }

        /// <summary>How long the sump ran short of oil at speed, seconds</summary>
        [JsonProperty("oil_starved_s")]
        public double? OilStarvedSeconds { get; set; }

        /// <summary>Engine life the lack of oil took</summary>
        [JsonProperty("oil_life_lost")]
        public double? OilLifeLost { get; set; }

        /// <summary>The least oil the pickup had, 1 always to 0 dry</summary>
        [JsonProperty("lowest_oil_supply")]
        public double? LowestOilSupply { get; set; }
    }

    public class WheelCondition
    {
        /// <summary>Which corner, 0 to 3 in AC's order (script 3.5); null in older files</summary>
        [JsonProperty("wheel")]
        public int? Wheel { get; set; }

        /// <summary>How much of the tread this race took, from 0; AC's tyres start every race new</summary>
        [JsonProperty("tyre_wear")]
        public double? TyreWear { get; set; }

        [JsonProperty("tyre_virtual_km")]
        public double? TyreVirtualKm { get; set; }

        [JsonProperty("tyre_blown")]
        public bool? TyreBlown { get; set; }

        /// <summary>How far this race bent the steering rod, in metres (AC's MAX_DAMAGE, 0.05 on nearly every car, is as far as it goes)</summary>
        [JsonProperty("suspension_damage")]
        public double? SuspensionDamage { get; set; }
    }

    /// <summary>
    /// A drag strip timeslip. The elapsed times run from the moment the car leaves the line; the reaction time is
    /// from the car's green to that moment: AC's, or since schema 1.6 the car's own tree in a bracket race and on a
    /// test-and-tune pass. A mark the car never reached is null.
    /// </summary>
    public class Timeslip
    {
        [JsonProperty("reaction_s")]
        public double? ReactionSeconds { get; set; }

        /// <summary>When the car got its own green, in seconds from AC's start (schema 1.6); null when that was AC's</summary>
        [JsonProperty("green_s")]
        public double? GreenSeconds { get; set; }

        /// <summary>The car left before its green (schema 1.6); null when it didn't</summary>
        [JsonProperty("red_light")]
        public bool? RedLight { get; set; }

        /// <summary>The pass's number on a test-and-tune, from 1 (schema 1.6); null in a race</summary>
        [JsonProperty("pass")]
        public int? Pass { get; set; }

        [JsonProperty("sixty_ft_s")]
        public double? SixtyFeetSeconds { get; set; }

        [JsonProperty("three_thirty_ft_s")]
        public double? ThreeThirtyFeetSeconds { get; set; }

        [JsonProperty("eighth_mile_s")]
        public double? EighthMileSeconds { get; set; }

        [JsonProperty("eighth_mile_mph")]
        public double? EighthMileMph { get; set; }

        [JsonProperty("thousand_ft_s")]
        public double? ThousandFeetSeconds { get; set; }

        [JsonProperty("quarter_mile_s")]
        public double? QuarterMileSeconds { get; set; }

        [JsonProperty("quarter_mile_mph")]
        public double? QuarterMileMph { get; set; }
    }
}
