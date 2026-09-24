using System.IO;
using System.Text;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Services.Configuration.Models;

namespace Street_Rod_AC.Services.Configuration
{
    /// <summary>
    /// Central service for all Assetto Corsa INI file modifications.
    /// Implements the Read -> Intent -> Apply model, with one way to write: the file's original is kept first
    /// (<see cref="AcConfigBackup"/>, under %AppData%\StreetRodAC\AcRestore), then the new text is written through
    /// <see cref="SafeFile"/> (temp file, flushed, renamed over the old one). An edited file keeps its encoding and
    /// every line but the ones changed (<see cref="IniText"/>); race.ini, which a race rewrites whole, is written as
    /// UTF-8 without a BOM. Everything written goes back with <see cref="RestoreAll"/> once AC has exited.
    /// </summary>
    public class IniModificationService : IIniModificationService
    {
        /// <summary>The CSP mode a race runs in: race.ini selects it by its folder name under extension\lua\new-modes</summary>
        public const string RaceModeId = "sr_race";

        /// <summary>AC's assists file, where the damage and tyre wear rates are</summary>
        public const string AssistsFile = "assists.ini";

        /// <summary>Mechanical and body damage in a race, in percent (AC's full rate)</summary>
        public const int RaceDamage = 100;

        /// <summary>Tyre wear in a race: 1 is AC's normal rate</summary>
        public const int RaceTyreWear = 1;

        private readonly string _cfgDirectory;
        private readonly AcConfigBackup _backup;
        private readonly IAppLogger _logger;

        public IniModificationService() : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa", "cfg"),
            AppSettings.AcRestorePath)
        {
        }

        /// <param name="cfgDirectory">AC's Documents\Assetto Corsa\cfg</param>
        /// <param name="restoreRoot">Where the originals wait (%AppData%\StreetRodAC\AcRestore)</param>
        public IniModificationService(string cfgDirectory, string restoreRoot)
        {
            _cfgDirectory = cfgDirectory;
            _backup = new AcConfigBackup(cfgDirectory, restoreRoot);
            _logger = AppLoggerFactory.CreateLogger("IniModification");

            // Ensure cfg directory exists
            if (!Directory.Exists(_cfgDirectory))
            {
                _logger.Warning("AC cfg directory not found: {CfgDirectory}", _cfgDirectory);
            }
            else
            {
                _logger.Information("INI Modification Service initialized. CFG directory: {CfgDirectory}", _cfgDirectory);
            }
        }

        public bool ApplyIntent(ModificationIntent intent)
        {
            _logger.Information("Applying intent: {Description}", intent.Description);

            try
            {
                return intent switch
                {
                    ShowroomIntent showroomIntent => ApplyShowroomIntent(showroomIntent),
                    DragRaceIntent dragRaceIntent => ApplyDragRaceIntent(dragRaceIntent),
                    RaceConfigIntent raceIntent => ApplyRaceConfigIntent(raceIntent),
                    FreeRunIntent freeRunIntent => ApplyFreeRunIntent(freeRunIntent),
                    _ => throw new NotSupportedException($"Intent type not supported: {intent.GetType().Name}")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to apply intent: {Description}", intent.Description);
                return false;
            }
        }

        public int RestoreAll() => _backup.RestoreAll();

        public bool HasPendingRestore => _backup.HasPending;

        public IniText? ReadIniFile(string fileName)
        {
            var filePath = GetIniFilePath(fileName);
            if (!File.Exists(filePath)) return null;

            return new IniText(Decode(File.ReadAllBytes(filePath), out _));
        }

        public bool FileExists(string fileName)
        {
            var filePath = GetIniFilePath(fileName);
            return File.Exists(filePath);
        }

        public string GetIniFilePath(string fileName)
        {
            // If already absolute path, return as-is
            if (Path.IsPathRooted(fileName))
            {
                return fileName;
            }

            // Otherwise, resolve relative to cfg directory
            return Path.Combine(_cfgDirectory, fileName);
        }

        // ===== THE ONE WAY AN INI FILE IS WRITTEN =====

        /// <summary>
        /// Changes some values of a cfg file and leaves the rest as it was: comments, order and the encoding (a BOM
        /// stays if there was one, none is added). Lines are written back with CRLF, as AC writes them.
        /// </summary>
        private void EditIni(string filePath, Action<IniText> edit)
        {
            Encoding encoding = SafeFile.Utf8NoBom;
            var hadBom = false;
            string? text = null;
            if (File.Exists(filePath))
            {
                var bytes = File.ReadAllBytes(filePath);
                hadBom = HasUtf8Bom(bytes);
                text = Decode(bytes, out encoding);
            }

            var ini = new IniText(text);
            edit(ini);

            // IniText marks every Set as a change, even one to the value already there
            var content = ini.ToString();
            if (text != null && content == new IniText(text).ToString()) return;

            WriteIni(filePath, content, encoding, hadBom);
        }

        /// <summary>The original is kept, then the file is replaced in one rename</summary>
        private void WriteIni(string filePath, string content, Encoding encoding, bool bom = false)
        {
            _backup.Keep(filePath);

            var bytes = encoding.GetBytes(content);
            if (bom) bytes = [.. Utf8Bom, .. bytes];
            if (File.Exists(filePath)) File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
            SafeFile.WriteAllBytes(filePath, bytes);
        }

        private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

        private static bool HasUtf8Bom(byte[] bytes) =>
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        /// <summary>
        /// The text of a cfg file and the encoding that writes it back byte for byte: UTF-8 when it is valid UTF-8
        /// (plain ASCII is), else Latin-1, which maps every byte to one character and back, so an ANSI file's
        /// accented names survive an edit of another key unchanged
        /// </summary>
        internal static string Decode(byte[] bytes, out Encoding encoding)
        {
            var offset = HasUtf8Bom(bytes) ? 3 : 0;
            try
            {
                var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, offset, bytes.Length - offset);
                encoding = SafeFile.Utf8NoBom;
                return text;
            }
            catch (DecoderFallbackException)
            {
                encoding = Encoding.Latin1;
                return Encoding.Latin1.GetString(bytes, offset, bytes.Length - offset);
            }
        }

        // ===== VALUES THAT GO INTO AN INI FILE =====

        /// <summary>
        /// An id (car, skin, track, layout) as it goes into a cfg file. Ids are folder names; one that could end its
        /// line, open a section or start a comment is refused rather than written, since it would change what the
        /// file says (a new [REMOTE] section, a key of ours overwritten). Null is an empty value.
        /// </summary>
        internal static string IniId(string? value, string what)
        {
            var id = value ?? string.Empty;
            if (id.Any(c => char.IsControl(c) || c is '[' or ']' or '=' or ';'))
                throw new ArgumentException($"The {what} id '{IniName(id)}' has characters that cannot go into an INI file");
            return id;
        }

        /// <summary>
        /// A name (a driver's) as it goes into a cfg file: free text, so what would break the line is replaced
        /// rather than refused. Line breaks and other control characters become spaces, [ ] become ( ), = becomes -,
        /// and ; (a comment to AC, which would cut the name) becomes ,
        /// </summary>
        internal static string IniName(string? value)
        {
            var name = new StringBuilder(value?.Length ?? 0);
            foreach (var c in value ?? string.Empty)
            {
                name.Append(c switch
                {
                    '[' => '(',
                    ']' => ')',
                    '=' => '-',
                    ';' => ',',
                    _ when char.IsControl(c) => ' ',
                    _ => c
                });
            }

            return name.ToString().Trim();
        }

        // ===== INTENT APPLICATION METHODS =====

        private bool ApplyShowroomIntent(ShowroomIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);
            var carId = IniId(intent.CarId, "car");
            var skinId = IniId(intent.SkinId, "skin");
            var track = IniId(intent.Track, "showroom");

            EditIni(filePath, ini =>
            {
                ini.Set("SHOWROOM", "CAR", carId);
                ini.Set("SHOWROOM", "SKIN", skinId);
                ini.Set("SHOWROOM", "SELECTED_SKIN", skinId);
                ini.Set("SHOWROOM", "TRACK", track);
            });

            _logger.Information("Applied showroom intent: Car={CarId}, Skin={SkinId}",
                intent.CarId, intent.SkinId);

            return true;
        }

        private bool ApplyRaceConfigIntent(RaceConfigIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);
            var carId = IniId(intent.CarId, "car");
            var skinId = IniId(intent.SkinId, "skin");
            var trackId = IniId(intent.TrackId, "track");
            var trackConfig = IniId(intent.TrackConfig, "track layout");

            EditIni(filePath, ini =>
            {
                // Configure [RACE] section
                ini.Set("RACE", "MODEL", carId);
                ini.Set("RACE", "SKIN", skinId);
                ini.Set("RACE", "TRACK", trackId);
                ini.Set("RACE", "CONFIG_TRACK", trackConfig);

                // Configure [SESSION_0] section
                ini.Set("SESSION_0", "NAME", "Quick Race");
            });

            _logger.Information("Applied race config intent: Car={CarId}, Skin={SkinId}, Track={TrackId}, Config={TrackConfig}",
                intent.CarId, intent.SkinId, intent.TrackId, intent.TrackConfig ?? "(none)");

            return true;
        }

        private bool ApplyDragRaceIntent(DragRaceIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);

            _logger.Information("Applying drag race intent (generating INI from code)");

            // Build the race.ini content from scratch
            var content = BuildDragRaceIni(intent);

            // Write to race.ini; the user's own is kept until AC exits
            WriteIni(filePath, content, SafeFile.Utf8NoBom);

            ApplyRaceDamage();

            _logger.Information("Applied drag race intent: Player={PlayerName} ({PlayerCarId}), Opponent={OpponentName} ({OpponentCarId}), AI={AILevel}/{AIAggression}, Context={ContextId}",
                intent.PlayerName, intent.PlayerCarId, intent.OpponentName, intent.OpponentCarId, intent.OpponentAILevel, intent.OpponentAIAggression,
                intent.ContextId?.ToString("D") ?? "(none)");

            return true;
        }

        /// <summary>
        /// Builds the complete drag race INI file content with all required sections
        /// </summary>
        private string BuildDragRaceIni(DragRaceIntent intent)
        {
            var sb = new System.Text.StringBuilder();
            AppendCommonSections(sb);

            // [RACE] - Player car info and track
            sb.AppendLine("[RACE]");
            sb.AppendLine("AI_LEVEL=100");
            sb.AppendLine("CARS=2");
            sb.AppendLine($"CONFIG_TRACK={IniId(intent.TrackConfig, "track layout")}");
            sb.AppendLine("DRIFT_MODE=0");
            sb.AppendLine("FIXED_SETUP=0");
            sb.AppendLine("JUMP_START_PENALTY=0");  // the race mode judges false starts; AC's penalty teleports the car
            sb.AppendLine($"MODEL={IniId(intent.PlayerCarId, "car")}");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine("PENALTIES=0");
            sb.AppendLine("RACE_LAPS=1");
            sb.AppendLine($"SKIN={IniId(intent.PlayerSkin, "skin")}");
            sb.AppendLine($"TRACK={IniId(intent.TrackId, "track")}");
            sb.AppendLine($"__CM_CUSTOM_MODE={RaceModeId}");  // the CSP mode that runs the race (see SrRaceMode)
            sb.AppendLine();

            AppendTailSections(sb);

            // [SESSION_0] - Race session. A drag race is a one-lap race too, on the strip: AC's own drag session
            // (TYPE=7) disqualifies for lanes, resets jump starts and runs matches, all by teleporting the cars,
            // and the race mode owns those calls. On ks_drag both cars start side by side, one per lane, and the
            // lap ends at the strip's finish line (checked in the game 2026-09-24).
            var isDrag = intent.RaceType == RaceType.DragRace;
            sb.AppendLine("[SESSION_0]");
            sb.AppendLine("STARTING_POSITION=1");
            sb.AppendLine(isDrag ? "NAME=Drag Race" : "NAME=Quick Race");
            sb.AppendLine("TYPE=3");
            sb.AppendLine("LAPS=1");
            sb.AppendLine("DURATION_MINUTES=0");
            sb.AppendLine("SPAWN_SET=START");
            sb.AppendLine();

            // [CAR_0] - Player
            sb.AppendLine("[CAR_0]");
            sb.AppendLine("MODEL=-");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine($"SKIN={IniId(intent.PlayerSkin, "skin")}");
            sb.AppendLine($"DRIVER_NAME={IniName(intent.PlayerName)}");
            sb.AppendLine("NATIONALITY=");
            sb.AppendLine("NATION_CODE=");
            sb.AppendLine();

            // [CAR_1] - Opponent (AI)
            sb.AppendLine("[CAR_1]");
            sb.AppendLine($"MODEL={IniId(intent.OpponentCarId, "car")}");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine($"AI_LEVEL={intent.OpponentAILevel}");
            sb.AppendLine($"AI_AGGRESSION={intent.OpponentAIAggression}");
            sb.AppendLine($"SKIN={IniId(intent.OpponentSkin, "skin")}");
            sb.AppendLine($"DRIVER_NAME={IniName(intent.OpponentName)}");
            sb.AppendLine("NATIONALITY=");
            sb.AppendLine("NATION_CODE=");

            // [STREET_ROD] - What the race mode needs to know: the kind of race (a drag race has lanes and a
            // flagger), and which race this is, which it writes into the result so a result is only ever applied
            // to the race it came from
            sb.AppendLine();
            sb.AppendLine("[STREET_ROD]");
            sb.AppendLine(isDrag ? "RACE_TYPE=DRAG" : "RACE_TYPE=ROAD");
            if (intent.ContextId is { } contextId)
            {
                sb.AppendLine($"CONTEXT_ID={contextId:D}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// A race is run with Assetto Corsa's damage and tyre wear on, whatever the player last picked in AC: the
        /// engine can blow and the body takes what it hits. The rest of the assists (and visual damage) stay the
        /// player's. Kept and put back like race.ini.
        /// </summary>
        private void ApplyRaceDamage()
        {
            EditIni(GetIniFilePath(AssistsFile), ini =>
            {
                ini.Set("ASSISTS", "DAMAGE", RaceDamage.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ini.Set("ASSISTS", "TYRE_WEAR", RaceTyreWear.ToString(System.Globalization.CultureInfo.InvariantCulture));
            });
            _logger.Information("Race assists: DAMAGE={Damage}, TYRE_WEAR={TyreWear}", RaceDamage, RaceTyreWear);
        }

        private bool ApplyFreeRunIntent(FreeRunIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);
            WriteIni(filePath, BuildFreeRunIni(intent), SafeFile.Utf8NoBom);

            _logger.Information("Applied free run intent: Car={CarId}, Track={TrackId}, Config={TrackConfig}",
                intent.CarId, intent.TrackId, intent.TrackConfig ?? "(none)");
            return true;
        }

        /// <summary>
        /// The player alone on a track: a practice session without the sr_race mode, so nothing starts the
        /// clock or closes the game; the player leaves when they are done, and no results are read
        /// </summary>
        private string BuildFreeRunIni(FreeRunIntent intent)
        {
            var sb = new System.Text.StringBuilder();
            AppendCommonSections(sb);

            sb.AppendLine("[RACE]");
            sb.AppendLine("AI_LEVEL=100");
            sb.AppendLine("CARS=1");
            sb.AppendLine($"CONFIG_TRACK={IniId(intent.TrackConfig, "track layout")}");
            sb.AppendLine("DRIFT_MODE=0");
            sb.AppendLine("FIXED_SETUP=0");
            sb.AppendLine("JUMP_START_PENALTY=0");
            sb.AppendLine($"MODEL={IniId(intent.CarId, "car")}");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine("PENALTIES=0");
            sb.AppendLine("RACE_LAPS=0");
            sb.AppendLine($"SKIN={IniId(intent.Skin, "skin")}");
            sb.AppendLine($"TRACK={IniId(intent.TrackId, "track")}");
            sb.AppendLine();

            AppendTailSections(sb);

            sb.AppendLine("[SESSION_0]");
            sb.AppendLine("NAME=Free Run");
            sb.AppendLine("TYPE=1");
            sb.AppendLine("DURATION_MINUTES=0");
            sb.AppendLine("SPAWN_SET=PIT");
            sb.AppendLine();

            sb.AppendLine("[CAR_0]");
            sb.AppendLine("MODEL=-");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine($"SKIN={IniId(intent.Skin, "skin")}");
            sb.AppendLine($"DRIVER_NAME={IniName(intent.PlayerName)}");
            sb.AppendLine("NATIONALITY=");
            sb.AppendLine("NATION_CODE=");

            return sb.ToString();
        }

        /// <summary>Everything before [RACE] that every launch shares</summary>
        private static void AppendCommonSections(System.Text.StringBuilder sb)
        {

            // [BENCHMARK]
            sb.AppendLine("[BENCHMARK]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

            // [DYNAMIC_TRACK]
            sb.AppendLine("[DYNAMIC_TRACK]");
            sb.AppendLine("LAP_GAIN=1");
            sb.AppendLine("RANDOMNESS=0");
            sb.AppendLine("SESSION_START=100");
            sb.AppendLine("SESSION_TRANSFER=100");
            sb.AppendLine("PRESET=5");
            sb.AppendLine();

            // [GHOST_CAR]
            sb.AppendLine("[GHOST_CAR]");
            sb.AppendLine("ENABLED=0");
            sb.AppendLine("FILE=");
            sb.AppendLine("LOAD=0");
            sb.AppendLine("PLAYING=0");
            sb.AppendLine("RECORDING=0");
            sb.AppendLine("SECONDS_ADVANTAGE=0");
            sb.AppendLine();

            // [GROOVE]
            sb.AppendLine("[GROOVE]");
            sb.AppendLine("VIRTUAL_LAPS=10");
            sb.AppendLine("MAX_LAPS=30");
            sb.AppendLine("STARTING_LAPS=0");
            sb.AppendLine();

            // [HEADER]
            sb.AppendLine("[HEADER]");
            sb.AppendLine("VERSION=1");
            sb.AppendLine("__CM_FEATURE_SET=2");
            sb.AppendLine();

            // [LAP_INVALIDATOR]
            sb.AppendLine("[LAP_INVALIDATOR]");
            sb.AppendLine("ALLOWED_TYRES_OUT=-1");
            sb.AppendLine();

            // [LIGHTING]
            sb.AppendLine("[LIGHTING]");
            sb.AppendLine("CLOUD_SPEED=0.200");
            sb.AppendLine("SUN_ANGLE=-16.00");
            sb.AppendLine("TIME_MULT=1.0");
            sb.AppendLine("__CM_WEATHER_TYPE=19");
            sb.AppendLine("__TRACK_GEOTAG_LONG=0.231944444444444");
            sb.AppendLine("__TRACK_TIMEZONE_BASE_OFFSET=3600");
            sb.AppendLine("__TRACK_TIMEZONE_DTS=0");
            sb.AppendLine("__TRACK_TIMEZONE_OFFSET=3600");
            sb.AppendLine("__TRACK_GEOTAG_LAT=47.9602777777778");
            sb.AppendLine("__CM_WEATHER_CONTROLLER=base");
            sb.AppendLine();

            // [OPTIONS]
            sb.AppendLine("[OPTIONS]");
            sb.AppendLine("USE_MPH=0");
            sb.AppendLine();

        }

        /// <summary>The sections between [RACE] and the sessions that every launch shares</summary>
        private static void AppendTailSections(System.Text.StringBuilder sb)
        {
            // [REMOTE]
            sb.AppendLine("[REMOTE]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine("GUID=");
            sb.AppendLine("NAME=");
            sb.AppendLine("PASSWORD=");
            sb.AppendLine("REQUESTED_CAR=");
            sb.AppendLine("SERVER_IP=");
            sb.AppendLine("SERVER_PORT=");
            sb.AppendLine("TEAM=");
            sb.AppendLine();

            // [REPLAY]
            sb.AppendLine("[REPLAY]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine("FILENAME=");
            sb.AppendLine();

            // [RESTART]
            sb.AppendLine("[RESTART]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

            // [TEMPERATURE]
            sb.AppendLine("[TEMPERATURE]");
            sb.AppendLine("AMBIENT=12");
            sb.AppendLine("ROAD=13");
            sb.AppendLine();

            // [WEATHER]
            sb.AppendLine("[WEATHER]");
            sb.AppendLine("NAME=4_mid_clear");
            sb.AppendLine();

            // [WIND]
            sb.AppendLine("[WIND]");
            sb.AppendLine("DIRECTION_DEG=-1");
            sb.AppendLine("SPEED_KMH_MAX=40");
            sb.AppendLine("SPEED_KMH_MIN=2");
            sb.AppendLine();

            // [__PREVIEW_GENERATION]
            sb.AppendLine("[__PREVIEW_GENERATION]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

        }
    }
}
