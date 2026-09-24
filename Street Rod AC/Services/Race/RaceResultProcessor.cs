using LiteDB;
using Newtonsoft.Json;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.Services.Race.Validation;
using Street_Rod_AC.Services.Storage;
using System.IO;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Processes race results and applies game logic
    /// Determines outcomes, updates stats, car health, and handles wagers
    ///
    /// Works in three steps: everything that can fail on the file's data (the timestamp, who is who, the
    /// session record) is worked out first without touching the game; then the changes are applied to the
    /// live game state in one go; then the state and the session record are saved in one transaction. The
    /// parts of the state a race changes (the player, the racers, the career, the pending race) are
    /// snapshotted before step 2: if anything in step 2 or 3 throws (a career check, the save itself), they
    /// are put back from the snapshot before the exception goes on, so a race is never left half-applied in
    /// memory for the next ordinary save to write without its session record.
    /// </summary>
    public class RaceResultProcessor : IRaceResultProcessor
    {
        private readonly IGameStateRepository _gameStateRepository;
        private readonly IRaceSessionRepository _sessionRepository;
        private readonly ICareerProgressService _careerProgressService;
        private readonly IRaceEventService _raceEventService;
        private readonly IAppLogger _logger;

        // Car health degradation constants (per race)
        private const double BASE_ENGINE_DEGRADATION = 0.01;        // -1%
        private const double BASE_TRANSMISSION_DEGRADATION = 0.005; // -0.5%
        private const double BASE_TIRE_DEGRADATION = 0.02;          // -2%
        private const double BASE_BODY_DEGRADATION = 0.001;         // -0.1%

        // Crash penalties (additional degradation)
        private const double CRASH_ENGINE_PENALTY = 0.05;           // -5%
        private const double CRASH_TRANSMISSION_PENALTY = 0.03;     // -3%
        private const double CRASH_BODY_PENALTY = 0.10;             // -10%
        private const double CRASH_TIRE_PENALTY = 0.05;             // -5%

        /// <summary>
        /// The most one race can add to an odometer. A drag race is 0.4 km and a long circuit race a few tens;
        /// anything past this is a broken file, not driving.
        /// </summary>
        private const double MAX_RACE_DISTANCE_KM = 500.0;

        public RaceResultProcessor(
            IGameStateRepository gameStateRepository,
            IRaceSessionRepository sessionRepository,
            ICareerProgressService careerProgressService,
            IRaceEventService raceEventService)
        {
            _gameStateRepository = gameStateRepository;
            _sessionRepository = sessionRepository;
            _careerProgressService = careerProgressService;
            _raceEventService = raceEventService;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        public List<PlayerMessage> ProcessRaceResult(RaceResultJson result, RaceContext context, GameState gameState)
        {
            _logger.Information("Processing race result for session {SessionId}", result.Session.SessionId);

            var saveName = gameState.SaveName;
            if (string.IsNullOrEmpty(saveName))
                throw new InvalidOperationException("The game state has no save name - cannot record the race");

            // 1. Everything that can fail on the file's data, before the game changes
            if (!RaceResultValidator.TryParseTimestamp(result.Session.StartTimestamp, out var startedAt))
                throw new InvalidDataException($"session.start_timestamp is not a date: '{result.Session.StartTimestamp}'");

            var outcome = DetermineOutcome(result, context);
            _logger.Information("Race outcome: {WinCondition} - Winner: {Winner}",
                outcome.WinCondition, outcome.Winner?.DriverName ?? "None");

            var processedSession = CreateProcessedSession(result, outcome, context, startedAt);

            // 2. Apply the race to the game, once
            // (wear first: a pink slip may move the car to its new owner)
            // 3. The state and the record that the race was applied, together or not at all
            var messages = new List<PlayerMessage>();
            ApplyAndSave(gameState, saveName, processedSession, () =>
            {
                ApplyCarUpdates(gameState, outcome, context);
                ApplyRace(gameState, outcome, context, messages);
            });

            _logger.Information("Race result processed successfully");
            return messages;
        }

        /// <summary>
        /// Runs <paramref name="apply"/> on the live state and saves it with the session record. When either
        /// throws, what a race can change is put back as it was before and the exception goes on; a failed save
        /// as <see cref="RaceNotSavedException"/>, since the race itself was fine and can be tried again.
        /// </summary>
        private void ApplyAndSave(GameState gameState, string saveName, ProcessedRaceSession processedSession, Action apply)
        {
            var snapshot = RaceStateSnapshot.Take(gameState);
            try
            {
                apply();
            }
            catch (Exception ex)
            {
                PutBack(snapshot, gameState, processedSession, ex);
                throw;
            }

            try
            {
                _gameStateRepository.Save(gameState, saveName, db => _sessionRepository.Save(db, processedSession));
            }
            catch (Exception ex)
            {
                PutBack(snapshot, gameState, processedSession, ex);
                throw new RaceNotSavedException($"The race could not be saved: {ex.Message}", ex);
            }
        }

        private void PutBack(RaceStateSnapshot snapshot, GameState gameState, ProcessedRaceSession processedSession, Exception cause)
        {
            try
            {
                snapshot.Restore(gameState);
                _logger.Warning("Race {ContextId} could not be applied ({Error}): the game is as it was before it", processedSession.RaceContextId, cause.Message);
            }
            catch (Exception restoreError)
            {
                // Nothing more can be done in memory; the save on disk still holds the state before the race
                _logger.Critical(restoreError, "Race {ContextId} failed half-way and the state before it could not be put back", processedSession.RaceContextId);
            }
        }

        /// <summary>
        /// The parts of the game state a race changes, as the save would store them. Restoring replaces them with
        /// fresh objects read back from that copy: the same round trip a save and a load make.
        /// </summary>
        private sealed class RaceStateSnapshot
        {
            // Its own mapper (the same defaults as the one the saves use), used under a lock: LiteDB builds a type's
            // mapping on first use and a second thread can see it half-built, which would make a snapshot that
            // silently drops members. The shared global mapper is also used by the catalog and the saves on
            // other threads.
            private static readonly BsonMapper Mapper = new();

            private BsonDocument _player = null!;
            private BsonDocument _racers = null!;
            private BsonDocument _career = null!;
            private RaceContext? _pendingRace;
            private DateTime _lastPlayed;

            public static RaceStateSnapshot Take(GameState gameState)
            {
                lock (Mapper)
                {
                    return new RaceStateSnapshot
                    {
                        _player = Mapper.ToDocument(gameState.Player),
                        _racers = Mapper.ToDocument(gameState.Racers),
                        _career = Mapper.ToDocument(gameState.Career),
                        _pendingRace = gameState.PendingRace,
                        _lastPlayed = gameState.LastPlayedDate
                    };
                }
            }

            public void Restore(GameState gameState)
            {
                // All three are read back before any is replaced: a restore that fails leaves the state as it is
                Player player;
                RacerCollection racers;
                Models.GameState.CareerState career;
                lock (Mapper)
                {
                    player = Mapper.ToObject<Player>(_player);
                    racers = Mapper.ToObject<RacerCollection>(_racers);
                    career = Mapper.ToObject<Models.GameState.CareerState>(_career);
                }

                gameState.Player = player;
                gameState.Racers = racers;
                gameState.Career = career;
                gameState.PendingRace = _pendingRace;
                gameState.LastPlayedDate = _lastPlayed;
            }
        }

        public List<PlayerMessage> ApplyForfeit(RaceContext context, GameState gameState)
        {
            _logger.Information("Race {ContextId} brought back no result - the player forfeits", context.ContextId);

            var saveName = gameState.SaveName;
            if (string.IsNullOrEmpty(saveName))
                throw new InvalidOperationException("The game state has no save name - cannot record the forfeit");

            var outcome = new RaceDecision { WinCondition = WinCondition.Forfeit, PlayerWon = false };
            var processedSession = new ProcessedRaceSession
            {
                // Not a Lua session: its own id space, one per race context
                SessionId = $"forfeit-{context.ContextId:D}",
                ProcessedAt = DateTime.Now,
                RaceContextId = context.ContextId,
                WinnerName = context.OpponentName,
                LoserName = context.PlayerName,
                PlayerWon = false,
                CashWager = context.CashWager,
                WasPinkSlip = context.IsPinkSlip,
                TrackId = context.TrackId,
                SessionStartTime = context.CreatedAt,
                WinCondition = outcome.WinCondition.ToString()
            };

            var messages = new List<PlayerMessage>();
            ApplyAndSave(gameState, saveName, processedSession, () =>
            {
                var applied = ApplyRace(gameState, outcome, context, messages);
                messages.Insert(0, ForfeitMessage(context, applied));
            });
            return messages;
        }

        public void MarkRacePending(RaceContext context, GameState gameState)
        {
            gameState.PendingRace = context;
            if (!string.IsNullOrEmpty(gameState.SaveName))
                _gameStateRepository.Save(gameState, gameState.SaveName);
            _logger.Information("Race {ContextId} is pending in save {SaveName} until its result is settled", context.ContextId, gameState.SaveName);
        }

        public void ReleasePendingRace(RaceContext context, GameState gameState)
        {
            if (gameState.PendingRace?.ContextId != context.ContextId || string.IsNullOrEmpty(gameState.SaveName))
                return;

            gameState.PendingRace = null;
            _gameStateRepository.Save(gameState, gameState.SaveName);
            _logger.Information("Race {ContextId} brought back no result and had no stakes - no longer pending", context.ContextId);
        }

        /// <summary>
        /// Everything a settled race changes except the cars' wear, which needs the race's data. Returns what of
        /// the stakes actually changed hands.
        /// </summary>
        private StakesMoved ApplyRace(GameState gameState, RaceDecision outcome, RaceContext context, List<PlayerMessage> messages)
        {
            // Apply stat updates
            ApplyStatUpdates(gameState, outcome, context);

            // Handle wager/pink slip transfer
            var moved = new StakesMoved(false, false);
            if (outcome.WinCondition != WinCondition.BothCrashed)
            {
                moved = ApplyWagerTransfer(gameState, outcome, context);
            }

            // Update reputations based on race outcome
            ApplyReputationUpdates(gameState, outcome, context);

            // Update career milestone counters
            UpdateMilestoneCounters(gameState, outcome, context);

            // Complete event and apply rewards (if this is an event race)
            if (context.EventId != null)
            {
                ProcessEventCompletion(gameState, outcome, context, messages);
            }

            // Check for career progress (milestones, victory unlocks, game victory)
            var progress = _careerProgressService.CheckProgressAfterRace(gameState);
            messages.AddRange(progress.PlayerMessages);

            // Handle opponent status changes (e.g., if they lost their only car)
            UpdateOpponentStatus(gameState, context);

            // The race is settled: it is no longer waiting for a result
            if (gameState.PendingRace == null || gameState.PendingRace.ContextId == context.ContextId)
                gameState.PendingRace = null;

            return moved;
        }

        /// <summary>What of a race's stakes changed hands: the cash wager, the pink slip's car</summary>
        private readonly record struct StakesMoved(bool Cash, bool Car);

        /// <summary>
        /// Determine the race outcome (who won and how)
        /// </summary>
        private RaceDecision DetermineOutcome(RaceResultJson result, RaceContext context)
        {
            var outcome = new RaceDecision();

            // Get participants
            var participants = result.Participants;
            if (participants.Count != 2)
            {
                _logger.Warning("Expected 2 participants, found {Count}", participants.Count);
                outcome.WinCondition = WinCondition.Inconclusive;
                return outcome;
            }

            var (playerParticipant, opponentParticipant) = IdentifyPlayer(participants, context);
            outcome.Player = playerParticipant;
            outcome.Opponent = opponentParticipant;

            // Check crash scenarios
            bool playerCrashed = playerParticipant.Crash.Crashed;
            bool opponentCrashed = opponentParticipant.Crash.Crashed;

            if (playerCrashed && opponentCrashed)
            {
                // Both crashed - draw
                outcome.WinCondition = WinCondition.BothCrashed;
                outcome.PlayerWon = false;
                return outcome;
            }

            if (playerCrashed)
            {
                // Player crashed, opponent wins
                outcome.WinCondition = WinCondition.PlayerCrashed;
                outcome.PlayerWon = false;
                return outcome;
            }

            if (opponentCrashed)
            {
                // Opponent crashed, player wins
                outcome.WinCondition = WinCondition.OpponentCrashed;
                outcome.PlayerWon = true;
                return outcome;
            }

            // No crashes - determine by final position
            var playerPosition = playerParticipant.Performance.FinalPosition;
            var opponentPosition = opponentParticipant.Performance.FinalPosition;

            if (playerPosition.HasValue && opponentPosition.HasValue)
            {
                // Lower number = better
                outcome.WinCondition = WinCondition.FinishPosition;
                outcome.PlayerWon = playerPosition.Value < opponentPosition.Value;
                return outcome;
            }

            // No positions available - inconclusive
            _logger.Warning("No final positions available - outcome is inconclusive");
            outcome.WinCondition = WinCondition.Inconclusive;
            return outcome;
        }

        /// <summary>
        /// Which of the two participants is the player. The Lua app marks the player's car (is_player, car
        /// index 0); older files only have names, and when one name matches the other participant is the
        /// opponent. The file's order says nothing: the Lua app used to write the cars in hash order.
        /// </summary>
        private (RaceParticipant Player, RaceParticipant Opponent) IdentifyPlayer(List<RaceParticipant> participants, RaceContext context)
        {
            RaceParticipant Other(RaceParticipant p) => ReferenceEquals(p, participants[0]) ? participants[1] : participants[0];

            var marked = participants.FirstOrDefault(p => p.IsPlayer == true)
                ?? participants.FirstOrDefault(p => p.IsPlayer == null && p.CarIndex == 0);
            if (marked != null)
                return (marked, Other(marked));

            var byName = participants.FirstOrDefault(p => p.DriverName == context.PlayerName);
            if (byName != null)
                return (byName, Other(byName));

            var opponentByName = participants.FirstOrDefault(p => p.DriverName == context.OpponentName);
            if (opponentByName != null)
                return (Other(opponentByName), opponentByName);

            _logger.Warning("Could not match participants to race context - assuming first participant is player");
            return (participants[0], participants[1]);
        }

        /// <summary>
        /// Apply stat updates to racers
        /// </summary>
        private void ApplyStatUpdates(GameState gameState, RaceDecision outcome, RaceContext context)
        {
            // For inconclusive or both crashed, no stats updates
            if (!outcome.IsDecided)
            {
                _logger.Information("No stat updates (inconclusive or both crashed)");
                return;
            }

            // Update player stats
            var playerStats = gameState.Player.Stats;

            if (outcome.PlayerWon)
            {
                playerStats.Wins++;
                _logger.Debug("Player wins incremented: {Wins}", playerStats.Wins);

                if (context.IsPinkSlip)
                {
                    playerStats.PinkSlipsWon++;
                    _logger.Debug("Player pink slips won: {PinkSlipsWon}", playerStats.PinkSlipsWon);
                }
            }
            else
            {
                playerStats.Losses++;
                _logger.Debug("Player losses incremented: {Losses}", playerStats.Losses);

                if (context.IsPinkSlip)
                {
                    playerStats.PinkSlipsLost++;
                    _logger.Debug("Player pink slips lost: {PinkSlipsLost}", playerStats.PinkSlipsLost);
                }
            }

            playerStats.Races++;
            _logger.Debug("Player total races: {Races}", playerStats.Races);

            // Update opponent stats (if we can find them in game state)
            // Skip for event-only opponents as they're not tracked in game state
            if (!context.IsEventOnlyOpponent)
            {
                var opponent = FindRacer(gameState, context.OpponentName);
                if (opponent != null)
                {
                    if (outcome.PlayerWon)
                    {
                        opponent.Stats.Losses++;
                        if (context.IsPinkSlip)
                            opponent.Stats.PinkSlipsLost++;
                    }
                    else
                    {
                        opponent.Stats.Wins++;
                        if (context.IsPinkSlip)
                            opponent.Stats.PinkSlipsWon++;
                    }

                    opponent.Stats.Races++;
                    _logger.Debug("Opponent stats updated: W{Wins}/L{Losses}",
                        opponent.Stats.Wins, opponent.Stats.Losses);
                }
                else
                {
                    _logger.Warning("Could not find opponent {OpponentName} in game state", context.OpponentName);
                }
            }
            else
            {
                _logger.Debug("Skipping opponent stats update for event-only opponent {OpponentName}", context.OpponentName);
            }
        }

        /// <summary>
        /// Apply car health degradation and odometer updates
        /// </summary>
        private void ApplyCarUpdates(GameState gameState, RaceDecision outcome, RaceContext context)
        {
            if (outcome.Player == null || outcome.Opponent == null)
            {
                _logger.Warning("Participants not identified - skipping car updates");
                return;
            }

            // Update player car
            var playerCar = gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId);
            if (playerCar != null)
            {
                ApplyCarDegradation(playerCar, outcome.Player);
                playerCar.OdometerKM += RaceDistance(outcome.Player);
                _logger.Debug("Player car updated: Odometer={Odometer}km, Engine={Engine}%, Transmission={Trans}%",
                    playerCar.OdometerKM, playerCar.EngineHealth * 100, playerCar.TransmissionHealth * 100);
            }
            else
            {
                _logger.Warning("Could not find player car {CarId}", context.PlayerCarInstanceId);
            }

            // Update opponent car
            var opponent = FindRacer(gameState, context.OpponentName);
            var opponentCar = opponent?.Cars.FirstOrDefault(c => c.InstanceId == context.OpponentCarInstanceId);
            if (opponentCar != null)
            {
                ApplyCarDegradation(opponentCar, outcome.Opponent);
                opponentCar.OdometerKM += RaceDistance(outcome.Opponent);
                _logger.Debug("Opponent car updated: Odometer={Odometer}km",
                    opponentCar.OdometerKM);
            }
        }

        /// <summary>The distance to put on the odometer: the validator rejected negative and non-finite ones</summary>
        private static double RaceDistance(RaceParticipant participant) =>
            Math.Clamp(participant.Performance.DistanceKm, 0.0, MAX_RACE_DISTANCE_KM);

        /// <summary>
        /// Apply degradation to a single car
        /// </summary>
        private void ApplyCarDegradation(Car car, RaceParticipant participant)
        {
            double engineDeg = BASE_ENGINE_DEGRADATION;
            double transDeg = BASE_TRANSMISSION_DEGRADATION;
            double tireDeg = BASE_TIRE_DEGRADATION;
            double bodyDeg = BASE_BODY_DEGRADATION;

            // Add crash penalties
            if (participant.Crash.Crashed)
            {
                engineDeg += CRASH_ENGINE_PENALTY;
                transDeg += CRASH_TRANSMISSION_PENALTY;
                tireDeg += CRASH_TIRE_PENALTY;
                bodyDeg += CRASH_BODY_PENALTY;

                _logger.Debug("Applying crash penalties to car (crash intensity: {Intensity}G)",
                    participant.Crash.MaxCrashIntensityG);
            }

            // Apply degradation, kept in [0.0, 1.0]
            car.EngineHealth = Math.Clamp(car.EngineHealth - engineDeg, 0.0, 1.0);
            car.TransmissionHealth = Math.Clamp(car.TransmissionHealth - transDeg, 0.0, 1.0);
            car.TireCondition = Math.Clamp(car.TireCondition - tireDeg, 0.0, 1.0);
            car.BodyCondition = Math.Clamp(car.BodyCondition - bodyDeg, 0.0, 1.0);
        }

        /// <summary>
        /// Apply wager transfer and pink slip car transfer
        /// </summary>
        private StakesMoved ApplyWagerTransfer(GameState gameState, RaceDecision outcome, RaceContext context)
        {
            var cashMoved = false;
            var carMoved = false;
            if (!outcome.IsDecided)
                return new StakesMoved(false, false);

            // Find opponent racer
            var opponent = FindRacer(gameState, context.OpponentName);
            if (opponent == null)
            {
                _logger.Warning("Cannot apply wager transfer: opponent {OpponentName} not found", context.OpponentName);
                return new StakesMoved(false, false);
            }

            // Cash wager transfer
            if (context.CashWager > 0)
            {
                if (outcome.PlayerWon)
                {
                    gameState.Player.Money += context.CashWager;
                    opponent.Money -= context.CashWager;
                    gameState.Player.Stats.TotalEarnings += context.CashWager;
                    opponent.Stats.TotalLosses += context.CashWager;

                    _logger.Information("Player won ${Wager} from {Opponent}",
                        context.CashWager, context.OpponentName);
                }
                else
                {
                    gameState.Player.Money -= context.CashWager;
                    opponent.Money += context.CashWager;
                    gameState.Player.Stats.TotalLosses += context.CashWager;
                    opponent.Stats.TotalEarnings += context.CashWager;

                    _logger.Information("Player lost ${Wager} to {Opponent}",
                        context.CashWager, context.OpponentName);
                }

                cashMoved = true;
            }

            // Pink slip car transfer
            if (context.IsPinkSlip)
            {
                if (outcome.PlayerWon)
                {
                    // Player wins opponent's car
                    var opponentCar = opponent.Cars.FirstOrDefault(c => c.InstanceId == context.OpponentCarInstanceId);
                    if (opponentCar != null)
                    {
                        opponent.Cars.Remove(opponentCar);
                        gameState.Player.Cars.Add(opponentCar);
                        gameState.Player.Stats.CarsOwned++;
                        carMoved = true;

                        _logger.Information("Player won {OpponentName}'s {CarDef} in pink slip race",
                            context.OpponentName, opponentCar.DefinitionId);
                    }
                    else
                    {
                        _logger.Error("Pink slip race: could not find opponent car {CarId}",
                            context.OpponentCarInstanceId);
                    }
                }
                else
                {
                    // Player loses their car
                    var playerCar = gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId);
                    if (playerCar != null)
                    {
                        gameState.Player.Cars.Remove(playerCar);
                        opponent.Cars.Add(playerCar);
                        carMoved = true;

                        // Clear selected car if lost
                        if (gameState.Player.SelectedCarInstanceId == playerCar.InstanceId)
                        {
                            gameState.Player.SelectedCarInstanceId = null;
                            _logger.Warning("Player lost their selected car in pink slip race");
                        }

                        _logger.Information("Player lost their {CarDef} to {OpponentName} in pink slip race",
                            playerCar.DefinitionId, context.OpponentName);
                    }
                    else
                    {
                        _logger.Error("Pink slip race: could not find player car {CarId}",
                            context.PlayerCarInstanceId);
                    }
                }
            }

            return new StakesMoved(cashMoved, carMoved);
        }

        /// <summary>
        /// Create a processed session record for storage
        /// </summary>
        private static ProcessedRaceSession CreateProcessedSession(
            RaceResultJson result,
            RaceDecision outcome,
            RaceContext context,
            DateTime startedAt)
        {
            return new ProcessedRaceSession
            {
                SessionId = result.Session.SessionId,
                ProcessedAt = DateTime.Now,
                RaceContextId = context.ContextId,
                WinnerName = outcome.Winner?.DriverName ?? "None",
                LoserName = outcome.Loser?.DriverName ?? "None",
                PlayerWon = outcome.PlayerWon,
                CashWager = context.CashWager,
                WasPinkSlip = context.IsPinkSlip,
                TrackId = result.Session.TrackId,
                RawResultJson = JsonConvert.SerializeObject(result),
                SessionStartTime = startedAt,
                DurationSeconds = result.Session.DurationSeconds,
                WinCondition = outcome.WinCondition.ToString()
            };
        }

        /// <summary>
        /// Find a racer in the game state by name
        /// </summary>
        private Racer? FindRacer(GameState gameState, string racerName)
        {
            // Check all racer collections
            foreach (var racer in gameState.Racers.Inactive.Values)
                if (racer.Name == racerName) return racer;

            foreach (var racer in gameState.Racers.Retired.Values)
                if (racer.Name == racerName) return racer;

            foreach (var racer in gameState.Racers.ReadyToRace.Values)
                if (racer.Name == racerName) return racer;

            return null;
        }

        /// <summary>
        /// Update reputation for both racers based on race outcome
        /// Winners gain reputation, losers lose reputation
        /// </summary>
        private void ApplyReputationUpdates(GameState gameState, RaceDecision outcome, RaceContext context)
        {
            // Skip reputation updates for inconclusive or both crashed
            if (!outcome.IsDecided)
            {
                _logger.Information("No reputation updates (inconclusive or both crashed)");
                return;
            }

            // Calculate player reputation
            var oldPlayerRep = gameState.Player.Stats.Reputation;
            gameState.Player.Stats.Reputation = gameState.Player.Stats.CalculateReputation();
            var playerRepChange = gameState.Player.Stats.Reputation - oldPlayerRep;

            _logger.Information("Player reputation: {OldRep} -> {NewRep} ({Change:+#;-#;0})",
                oldPlayerRep, gameState.Player.Stats.Reputation, playerRepChange);

            // Calculate opponent reputation (if available)
            var opponent = FindRacer(gameState, context.OpponentName);
            if (opponent != null)
            {
                var oldOpponentRep = opponent.Stats.Reputation;
                opponent.Stats.Reputation = opponent.Stats.CalculateReputation();
                var opponentRepChange = opponent.Stats.Reputation - oldOpponentRep;

                _logger.Information("Opponent {Name} reputation: {OldRep} -> {NewRep} ({Change:+#;-#;0})",
                    context.OpponentName, oldOpponentRep, opponent.Stats.Reputation, opponentRepChange);
            }
        }

        /// <summary>
        /// Update career milestone counters based on race outcome
        /// </summary>
        private void UpdateMilestoneCounters(GameState gameState, RaceDecision outcome, RaceContext context)
        {
            // Skip milestone updates for inconclusive or both crashed
            if (!outcome.IsDecided)
            {
                return;
            }

            var career = gameState.Career;

            if (outcome.PlayerWon)
            {
                // Update total wins
                career.IncrementCounter(MilestoneTrigger.TotalWins);

                // Update race-type specific wins
                switch (context.RaceType)
                {
                    case RaceType.DragRace:
                        career.IncrementCounter(MilestoneTrigger.DragWins);
                        break;
                    case RaceType.Circuit:
                    case RaceType.Sprint:
                        career.IncrementCounter(MilestoneTrigger.RoadWins);
                        break;
                }

                // Update pink slip wins
                if (context.IsPinkSlip)
                {
                    career.IncrementCounter(MilestoneTrigger.PinkSlipWins);
                }

                // Track defeated opponent
                if (!string.IsNullOrEmpty(context.OpponentName))
                {
                    career.RecordDefeatedOpponent(context.OpponentName);
                }

                // Update money earned (from wager)
                if (context.CashWager > 0)
                {
                    career.IncrementCounter(MilestoneTrigger.MoneyEarned, (int)context.CashWager);
                }
            }

            // Update current cars owned count (not cumulative)
            career.SetCounter(MilestoneTrigger.CarsOwned, gameState.Player.Cars.Count);

            // Update current reputation (not cumulative)
            career.SetCounter(MilestoneTrigger.ReputationReached, gameState.Player.Stats.Reputation);

            _logger.Debug("Milestone counters updated: TotalWins={Wins}, PinkSlipWins={PinkSlips}, CarsOwned={Cars}, Reputation={Rep}",
                career.GetCounter(MilestoneTrigger.TotalWins),
                career.GetCounter(MilestoneTrigger.PinkSlipWins),
                career.GetCounter(MilestoneTrigger.CarsOwned),
                career.GetCounter(MilestoneTrigger.ReputationReached));
        }

        /// <summary>
        /// Process event completion and apply rewards
        /// </summary>
        private void ProcessEventCompletion(GameState gameState, RaceDecision outcome, RaceContext context, List<PlayerMessage> messages)
        {
            if (string.IsNullOrEmpty(context.EventId))
                return;

            // Is there an open event of this definition at all?
            var eventState = gameState.Career.ActiveEvents.FirstOrDefault(e =>
                e.EventDefinitionId == context.EventId &&
                !e.IsCompleted);

            if (eventState == null)
            {
                _logger.Warning("Could not find active event {EventId} for completion", context.EventId);
                return;
            }

            // Complete the event and get reward. The instance tells a daily or weekly event from its
            // siblings; the event windows are game time, so it completes at the game's date, not the clock's.
            var reward = _raceEventService.CompleteEvent(
                context.EventInstanceId ?? Guid.Empty,
                outcome.PlayerWon,
                gameState.Career,
                gameState.Date);

            _logger.Information("Event {EventId} completed. Player won: {PlayerWon}",
                context.EventId, outcome.PlayerWon);

            // Apply rewards if player won
            if (reward != null && outcome.PlayerWon)
            {
                // Apply cash reward
                if (reward.Cash > 0)
                {
                    gameState.Player.Money += reward.Cash;
                    gameState.Player.Stats.TotalEarnings += reward.Cash;
                    _logger.Information("Event reward: ${Cash} added to player", reward.Cash);
                }

                // Apply reputation reward
                if (reward.Reputation > 0)
                {
                    // Reputation is calculated from stats, so we need to add a bonus
                    // For now, we log it - reputation will be recalculated from stats
                    _logger.Information("Event reward: +{Rep} reputation bonus", reward.Reputation);

                    // Add reputation directly to stats (temporary bonus tracking)
                    gameState.Player.Stats.EventReputationBonus += reward.Reputation;
                }

                // Handle special item reward (log for now - parts system integration later)
                if (!string.IsNullOrEmpty(reward.SpecialItem))
                {
                    _logger.Information("Event reward: Special item '{Item}' - {Description}",
                        reward.SpecialItem, reward.SpecialItemDescription ?? "No description");
                    // TODO: Add to player's parts inventory when parts system is implemented
                }

                // The screen that ran the race tells the player
                var rewardMessage = EventRewardMessage(reward);
                if (rewardMessage != null)
                    messages.Add(rewardMessage);
            }
        }

        /// <summary>
        /// The message telling the player what an event paid; null when it paid nothing to speak of
        /// </summary>
        private static PlayerMessage? EventRewardMessage(Models.Career.Events.EventReward reward)
        {
            var rewardParts = new List<string>();

            if (reward.Cash > 0)
                rewardParts.Add($"${reward.Cash:N0}");

            if (reward.Reputation > 0)
                rewardParts.Add($"+{reward.Reputation} reputation");

            if (!string.IsNullOrEmpty(reward.SpecialItemDescription))
                rewardParts.Add(reward.SpecialItemDescription);
            else if (!string.IsNullOrEmpty(reward.SpecialItem))
                rewardParts.Add($"Special item: {reward.SpecialItem}");

            if (rewardParts.Count == 0)
                return null; // No rewards to show

            var message = "You won the event!\n\nRewards:\n" + string.Join("\n", rewardParts.Select(r => $"  {r}"));
            return new PlayerMessage("Event Complete!", message);
        }

        /// <summary>
        /// What the player is told when a race with stakes came back without a result: only what actually changed
        /// hands (an event-only opponent has no purse or garage to take it)
        /// </summary>
        private static PlayerMessage ForfeitMessage(RaceContext context, StakesMoved moved)
        {
            var stakes = new List<string>();
            if (moved.Cash)
                stakes.Add($"the ${context.CashWager:N0} wager goes to {context.OpponentName}");
            if (moved.Car)
                stakes.Add($"{context.OpponentName} takes your car's pink slip");

            if (stakes.Count == 0)
                return new PlayerMessage("Race Forfeited",
                    "The race never came back with a result, and walking away from a race with something on it counts as a loss.");

            return new PlayerMessage(
                "Race Forfeited",
                "The race never came back with a result, and walking away from a race with something on it counts as a loss:\n\n"
                + string.Join("\n", stakes.Select(s => $"  {char.ToUpper(s[0])}{s[1..]}.")));
        }

        /// <summary>
        /// Update opponent status based on race outcome
        /// If opponent lost their only car in a pink slip race, move them to Inactive
        /// </summary>
        private void UpdateOpponentStatus(GameState gameState, RaceContext context)
        {
            var opponent = FindRacer(gameState, context.OpponentName) as Opponent;
            if (opponent == null)
            {
                _logger.Warning("Cannot update opponent status: opponent {OpponentName} not found", context.OpponentName);
                return;
            }

            // Check if opponent has no cars left
            if (opponent.Cars.Count == 0)
            {
                // Opponent lost their only car - move to Inactive
                gameState.Racers.ReadyToRace.Remove(opponent.Name);
                gameState.Racers.Inactive[opponent.Name] = opponent;
                opponent.Status = RacerStatus.Inactive;

                _logger.Information("Opponent {Name} moved to Inactive (no cars remaining)", opponent.Name);
            }
            else if (opponent.Status == RacerStatus.Inactive && opponent.Cars.Count > 0)
            {
                // Opponent gained a car (won pink slip) and is now active again
                gameState.Racers.Inactive.Remove(opponent.Name);
                gameState.Racers.ReadyToRace[opponent.Name] = opponent;
                opponent.Status = RacerStatus.ReadyToRace;

                _logger.Information("Opponent {Name} moved to ReadyToRace (now has a car)", opponent.Name);
            }
        }
    }

    /// <summary>
    /// Result of race outcome determination. (Named apart from the launcher's RaceOutcome, which says how
    /// a launch ended, so the two can be used side by side.)
    /// </summary>
    public class RaceDecision
    {
        /// <summary>The player's entry in the result file; null for a forfeit or an inconclusive file</summary>
        public RaceParticipant? Player { get; set; }

        /// <summary>The opponent's entry in the result file; null for a forfeit or an inconclusive file</summary>
        public RaceParticipant? Opponent { get; set; }

        public bool PlayerWon { get; set; }
        public WinCondition WinCondition { get; set; }

        /// <summary>True when the race has a winner and a loser (stats, money and reputation change)</summary>
        public bool IsDecided => WinCondition is not (WinCondition.Inconclusive or WinCondition.BothCrashed);

        public RaceParticipant? Winner => !IsDecided ? null : PlayerWon ? Player : Opponent;
        public RaceParticipant? Loser => !IsDecided ? null : PlayerWon ? Opponent : Player;
    }

    /// <summary>
    /// How the race was decided
    /// </summary>
    public enum WinCondition
    {
        /// <summary>
        /// Normal finish - determined by final position
        /// </summary>
        FinishPosition,

        /// <summary>
        /// Opponent hard crashed - player wins by default
        /// </summary>
        OpponentCrashed,

        /// <summary>
        /// Player hard crashed - opponent wins by default
        /// </summary>
        PlayerCrashed,

        /// <summary>
        /// Both drivers crashed - race is a draw
        /// </summary>
        BothCrashed,

        /// <summary>
        /// Cannot determine outcome (missing data)
        /// </summary>
        Inconclusive,

        /// <summary>
        /// A race with stakes brought back no result: the player walked away and loses
        /// </summary>
        Forfeit
    }
}
