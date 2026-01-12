using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using System.IO;
using System.Text.Json;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Repository for loading opponent definitions from JSON file
    /// </summary>
    public class OpponentRepository : IOpponentRepository
    {
        private readonly string _definitionsFilePath;
        private readonly IAppLogger _logger;

        public OpponentRepository()
        {
            // Definitions file is in Assets/Opponents folder
            _definitionsFilePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets",
                "Opponents",
                "opponent_definitions.json"
            );

            _logger = AppLoggerFactory.CreateLogger("OpponentRepository");
        }

        /// <summary>
        /// Load all opponent definitions from JSON file
        /// </summary>
        public List<Opponent> LoadAllOpponents()
        {
            if (!DefinitionsFileExists())
            {
                _logger.Warning("Opponent definitions file not found: {FilePath}", _definitionsFilePath);
                return new List<Opponent>();
            }

            try
            {
                var json = File.ReadAllText(_definitionsFilePath);
                var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (!root.TryGetProperty("opponents", out var opponentsArray))
                {
                    _logger.Error("No 'opponents' array found in definitions file");
                    return new List<Opponent>();
                }

                var opponents = new List<Opponent>();

                foreach (var opponentJson in opponentsArray.EnumerateArray())
                {
                    var opponent = ParseOpponent(opponentJson);
                    if (opponent != null)
                    {
                        opponents.Add(opponent);
                    }
                }

                _logger.Information("Loaded {Count} opponent definitions", opponents.Count);
                return opponents;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load opponent definitions from {FilePath}", _definitionsFilePath);
                return new List<Opponent>();
            }
        }

        /// <summary>
        /// Load a specific opponent by ID
        /// </summary>
        public Opponent? LoadOpponent(string opponentId)
        {
            var allOpponents = LoadAllOpponents();
            return allOpponents.FirstOrDefault(o => o.OpponentId.ToString() == opponentId);
        }

        /// <summary>
        /// Check if definitions file exists
        /// </summary>
        public bool DefinitionsFileExists()
        {
            return File.Exists(_definitionsFilePath);
        }

        /// <summary>
        /// Parse a single opponent from JSON element
        /// </summary>
        private Opponent? ParseOpponent(JsonElement json)
        {
            try
            {
                // Extract required fields
                var id = json.GetProperty("opponentId").GetString() ?? string.Empty;
                var name = json.GetProperty("name").GetString() ?? "Unknown";
                var age = json.GetProperty("age").GetInt32();
                var genderStr = json.GetProperty("gender").GetString() ?? "Male";
                var skill = json.GetProperty("skill").GetInt32();
                var aggression = json.GetProperty("aggression").GetInt32();

                // Parse gender enum
                if (!Enum.TryParse<Gender>(genderStr, out var gender))
                {
                    gender = Gender.Male;
                }

                // Create opponent
                var opponent = new Opponent(name, age, gender, skill, aggression)
                {
                    OpponentId = Guid.NewGuid() // Generate new GUID for persistence
                };

                // Extract optional fields
                if (json.TryGetProperty("nickname", out var nicknameElement))
                {
                    opponent.Nickname = nicknameElement.GetString() ?? string.Empty;
                }

                if (json.TryGetProperty("portraitPath", out var portraitElement))
                {
                    opponent.PortraitPath = portraitElement.GetString();
                }

                if (json.TryGetProperty("location", out var locationElement))
                {
                    opponent.Location = locationElement.GetString();
                }

                if (json.TryGetProperty("biography", out var biographyElement))
                {
                    opponent.Biography = biographyElement.GetString();
                }

                return opponent;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to parse opponent from JSON");
                return null;
            }
        }
    }
}
