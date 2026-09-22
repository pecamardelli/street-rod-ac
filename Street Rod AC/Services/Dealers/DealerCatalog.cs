using System.IO;
using System.Text.Json;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Dealers
{
    /// <summary>
    /// Reads Assets/Dealers/dealers.json once and hands the definitions out.
    ///
    /// A missing or broken file is not fatal: the game falls back to the five dealers the market service has
    /// always made, without map positions. The map screen then has nothing to show and says so, but the
    /// listing screen and everything else carries on.
    /// </summary>
    public class DealerCatalog : IDealerCatalog
    {
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("DealerCatalog");
        private readonly List<DealerDefinition> _dealers = [];
        private readonly Dictionary<string, DealerDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public DealerCatalog()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Dealers", "dealers.json");

            try
            {
                if (!File.Exists(path))
                {
                    _logger.Warning("Dealer definitions not found: {Path}", path);
                    return;
                }

                var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!document.RootElement.TryGetProperty("dealers", out var array))
                {
                    _logger.Error("No 'dealers' array in {Path}", path);
                    return;
                }

                foreach (var element in array.EnumerateArray())
                {
                    var dealer = element.Deserialize<DealerDefinition>(Options);
                    if (dealer == null || string.IsNullOrWhiteSpace(dealer.Id))
                    {
                        _logger.Warning("Skipped a dealer with no id");
                        continue;
                    }

                    if (!_byId.TryAdd(dealer.Id, dealer))
                    {
                        _logger.Warning("Two dealers share the id {Id}; keeping the first", dealer.Id);
                        continue;
                    }

                    _dealers.Add(dealer);
                }

                _logger.Information("Loaded {Count} dealers", _dealers.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read dealer definitions from {Path}", path);
            }
        }

        public IReadOnlyList<DealerDefinition> All => _dealers;

        public DealerDefinition? Get(string id) =>
            id != null && _byId.TryGetValue(id, out var dealer) ? dealer : null;

        public List<DealerLocation> ToLocations() => _dealers.Select(d => d.ToLocation()).ToList();
    }
}
