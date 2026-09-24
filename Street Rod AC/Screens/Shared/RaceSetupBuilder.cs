using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Race;

namespace Street_Rod_AC.Screens.Shared
{
    /// <summary>
    /// Everything a race needs to know before AC starts, whoever set it up: a challenge at the diner or an
    /// event from the newspaper.
    /// </summary>
    public sealed class RaceEntry
    {
        public string PlayerName { get; init; } = string.Empty;

        /// <summary>The player's car, as owned; its folder is its definition id</summary>
        public Car PlayerCar { get; init; } = null!;

        public string OpponentName { get; init; } = string.Empty;

        /// <summary>The folder of the opponent's car model</summary>
        public string OpponentCarId { get; init; } = string.Empty;

        public string? OpponentSkin { get; init; }

        /// <summary>The opponent's own car, when it has one on the books (a racer of the pool); null for a one-off event entrant</summary>
        public Car? OpponentCar { get; init; }

        /// <summary>How the opponent drives, from <see cref="OpponentAIAdapter"/>, the one place AC's AI values come from</summary>
        public AssettoCorsaAIParameters OpponentAI { get; init; } = new();

        public string TrackId { get; init; } = string.Empty;
        public string? TrackConfig { get; init; }
        public RaceType RaceType { get; init; }

        public decimal CashWager { get; init; }
        public bool IsPinkSlip { get; init; }

        /// <summary>AC's damage for the race, in percent: the save's <see cref="GameRules.RaceDamagePercent"/></summary>
        public int DamagePercent { get; init; } = 100;

        /// <summary>The event raced for, or null for a street race</summary>
        public string? EventId { get; init; }

        /// <summary>The invitation raced for, so exactly that one is completed afterwards</summary>
        public Guid? EventInstanceId { get; init; }

        /// <summary>An event entrant who is not one of the racers of the pool: their stats are not kept</summary>
        public bool IsEventOnlyOpponent { get; init; }
    }

    /// <summary>What came of putting a race together</summary>
    public sealed class RaceSetupResult
    {
        /// <summary>The race to launch; null when the player's car cannot go</summary>
        public DragRaceLaunchIntent? Intent { get; init; }

        /// <summary>Why the player's car will not run, for the player; empty when it will</summary>
        public string PlayerCarProblem { get; init; } = string.Empty;

        public bool CanRace => Intent != null;
    }

    /// <summary>
    /// Puts a race together the same way wherever it was set up: each car on what its parts make of it, an
    /// opponent in the player's model in a marked copy of the folder, the AI from <see cref="OpponentAIAdapter"/>,
    /// and a <see cref="RaceContext"/> that describes the race exactly as it is launched.
    ///
    /// The diner and the newspaper both go through here, so an event is raced on its own track with the
    /// same car data and clone handling as a challenge.
    /// </summary>
    public sealed class RaceSetupBuilder
    {
        private readonly ICarPartsService _parts;
        private readonly RaceCarDataService _raceCarData;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RaceSetup");

        public RaceSetupBuilder(ICarPartsService parts, RaceCarDataService raceCarData)
        {
            _parts = parts;
            _raceCarData = raceCarData;
        }

        /// <summary>
        /// Both cars' data and the launch intent. The player's car that will not run makes no race; an
        /// opponent's car that will not run races as its author made it.
        /// </summary>
        public async Task<RaceSetupResult> BuildAsync(RaceEntry entry)
        {
            // A car the last races left blown, wrecked or totaled goes nowhere until it is repaired. Its parts are
            // judged, so it gets them first if it never had any.
            await EnsurePartsAsync(entry.PlayerCar);
            if (entry.OpponentCar != null) await EnsurePartsAsync(entry.OpponentCar);
            var groupOf = await PartGroupsAsync();
            var damage = CarCondition.WhyCannotRace(entry.PlayerCar, groupOf);
            if (damage.Count > 0)
            {
                return new RaceSetupResult { PlayerCarProblem = string.Join("; ", damage) };
            }

            // Each car goes in with the damage it carries. An opponent's car races whatever shape it is in (they do
            // not look after their cars yet): it gets just enough to leave the line.
            var playerStart = CarCondition.StartState(entry.PlayerCar, groupOf);
            var opponentStart = entry.OpponentCar == null ? null : CarCondition.Runnable(CarCondition.StartState(entry.OpponentCar, groupOf));

            var playerData = await PrepareCarDataAsync(entry.PlayerCar, playerStart);
            if (playerData is { CanDrive: false })
            {
                return new RaceSetupResult { PlayerCarProblem = playerData.Problem };
            }

            RaceCarData? opponentData = null;
            if (entry.OpponentCar != null)
            {
                opponentData = await PrepareCarDataAsync(entry.OpponentCar, opponentStart);
                if (opponentData is { CanDrive: false })
                {
                    _logger.Warning("{Opponent}'s car would not run ({Problem}): it races as its author made it", entry.OpponentName, opponentData.Problem);
                    opponentData = null;
                }
            }

            var (opponentRacesAs, sharedData) = RacesAs(entry.PlayerCar.DefinitionId, entry.OpponentCarId, opponentData);
            if (!string.Equals(opponentRacesAs, entry.OpponentCarId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Information("{Opponent} drives the same model as the player: it races in a copy, {Clone}", entry.OpponentName, opponentRacesAs);
            }

            var carData = new List<RaceCarData>();
            if (playerData != null) carData.Add(playerData);
            if (sharedData != null) carData.Add(sharedData);

            var intent = BuildIntent(entry, opponentRacesAs, carData);
            intent.PlayerStart = playerStart;
            intent.OpponentStart = opponentStart;
            return new RaceSetupResult { Intent = intent };
        }

        /// <summary>
        /// The folder the opponent's car races in, and its data: an opponent in the player's model races in a
        /// copy of the folder under its own id, so each car has its own data and its own sound.
        /// </summary>
        public static (string RacesAs, RaceCarData? Data) RacesAs(string playerCarId, string opponentCarId, RaceCarData? opponentData)
        {
            if (!string.Equals(playerCarId, opponentCarId, StringComparison.OrdinalIgnoreCase))
                return (opponentCarId, opponentData);

            var cloneId = AcCarFolder.CloneIdFor(opponentCarId);
            var data = opponentData == null
                ? RaceCarData.AsAuthored(opponentCarId, cloneId)
                : new RaceCarData(opponentData.CarId, opponentData.Build) { CloneId = cloneId };
            return (cloneId, data);
        }

        /// <summary>The launch intent and the race context it carries, both describing the same race</summary>
        public static DragRaceLaunchIntent BuildIntent(RaceEntry entry, string opponentRacesAs, List<RaceCarData> carData)
        {
            var intent = new DragRaceLaunchIntent
            {
                PlayerCarId = entry.PlayerCar.DefinitionId,
                PlayerSkin = entry.PlayerCar.SkinId ?? string.Empty,
                PlayerName = entry.PlayerName,
                PlayerCarInstanceId = entry.PlayerCar.InstanceId,
                OpponentCarId = opponentRacesAs,
                OpponentSkin = entry.OpponentSkin ?? string.Empty,
                OpponentName = entry.OpponentName,
                OpponentCarInstanceId = entry.OpponentCar?.InstanceId ?? Guid.Empty,
                OpponentAILevel = entry.OpponentAI.AILevel,
                OpponentAIAggression = entry.OpponentAI.AIAggression,
                TrackId = entry.TrackId,
                TrackConfig = entry.TrackConfig,
                RaceType = entry.RaceType,
                CashWager = entry.CashWager,
                IsPinkSlip = entry.IsPinkSlip,
                DamagePercent = entry.DamagePercent,
                CarData = carData
            };

            intent.Metadata["RaceContext"] = new RaceContext
            {
                PlayerName = entry.PlayerName,
                OpponentName = entry.OpponentName,
                PlayerCarInstanceId = entry.PlayerCar.InstanceId,
                OpponentCarInstanceId = entry.OpponentCar?.InstanceId ?? Guid.Empty,
                CashWager = entry.CashWager,
                IsPinkSlip = entry.IsPinkSlip,
                TrackId = entry.TrackId,
                TrackConfig = entry.TrackConfig,
                RaceType = entry.RaceType,
                EventId = entry.EventId,
                EventInstanceId = entry.EventInstanceId,
                IsEventOnlyOpponent = entry.IsEventOnlyOpponent
            };

            return intent;
        }

        /// <summary>
        /// Where an event is raced: the track it names when that is installed, else one of the installed tracks
        /// that suits its race type (a dragstrip for a drag race, anything else for a road race). A track with
        /// layouts is raced on one of them. The drag strip AC ships (ks_drag, drag1000) is the drag default when
        /// it is there. <paramref name="seed"/> makes the pick the same every time for one invitation.
        /// Null when nothing installed suits the event.
        /// </summary>
        public static (string TrackId, string? TrackConfig)? PickTrack(
            IEnumerable<Models.AC.TrackInfo> tracks, RaceType raceType, string? wantedTrackId, int seed)
        {
            var installed = tracks.ToList();

            IEnumerable<(string TrackId, string? TrackConfig)> Layouts(Models.AC.TrackInfo track) =>
                track.Configurations.Count == 0
                    ? new[] { (track.TrackId, (string?)null) }
                    : track.Configurations.Select(c => (track.TrackId, (string?)c.FolderName));

            if (!string.IsNullOrWhiteSpace(wantedTrackId))
            {
                var wanted = installed.FirstOrDefault(t => string.Equals(t.TrackId, wantedTrackId, StringComparison.OrdinalIgnoreCase));
                if (wanted != null) return Layouts(wanted).First();
            }

            var isDrag = raceType == RaceType.DragRace;
            var candidates = installed
                .Where(t => (t.Type == Models.AC.TrackType.Dragstrip) == isDrag)
                .OrderBy(t => t.TrackId, StringComparer.OrdinalIgnoreCase)
                .SelectMany(Layouts)
                .ToList();
            if (candidates.Count == 0) return null;

            if (isDrag)
            {
                var defaults = new DragRaceLaunchIntent();
                var stock = candidates.FirstOrDefault(c =>
                    string.Equals(c.TrackId, defaults.TrackId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.TrackConfig, defaults.TrackConfig, StringComparison.OrdinalIgnoreCase));
                if (stock.TrackId != null) return stock;
            }

            return candidates[(int)((uint)seed % (uint)candidates.Count)];
        }

        private async Task EnsurePartsAsync(Car car)
        {
            try
            {
                await _parts.EnsurePartsAsync(car);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not give {Car} its parts: it races on its own figures", car.DefinitionId);
            }
        }

        /// <summary>The parts' groups off the catalog, loaded off the UI thread; null when there are no parts</summary>
        private async Task<Func<string, string?>?> PartGroupsAsync()
        {
            try
            {
                return await Task.Run(() => _parts.IsAvailable ? CarCondition.Groups(_parts.Catalog) : null);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The parts catalog could not be read: the cars race on their own figures");
                return null;
            }
        }

        /// <summary>The car's data as its parts make it, put together off the UI thread; null when it races as it is</summary>
        public async Task<RaceCarData?> PrepareCarDataAsync(Car car, RaceStartState? damage = null)
        {
            try
            {
                await _parts.EnsurePartsAsync(car);
                return await Task.Run(() => _raceCarData.Prepare(car, damage));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not prepare the data of {Car}: it races as it is", car.DefinitionId);
                return null;
            }
        }
    }
}
