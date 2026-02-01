using Newtonsoft.Json;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Storage;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Processes race results and applies game logic
    /// Determines outcomes, updates stats, car health, and handles wagers
    /// </summary>
    public class RaceResultProcessor : IRaceResultProcessor
    {
        private readonly IGameStateRepository _gameStateRepository;
        private readonly IRaceSessionRepository _sessionRepository;
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

        public RaceResultProcessor(
            IGameStateRepository gameStateRepository,
            IRaceSessionRepository sessionRepository)
        {
            _gameStateRepository = gameStateRepository;
            _sessionRepository = sessionRepository;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        public async Task ProcessRaceResultAsync(RaceResultJson result, RaceContext? context)
        {
            _logger.Information("Processing race result for session {SessionId}", result.Session.SessionId);

            // Get current game state
            var app = System.Windows.Application.Current as App;
            var gameState = app?.CurrentGameState;

            if (gameState == null)
            {
                _logger.Warning("No active game state - cannot process race results");
                throw new InvalidOperationException("No active game state");
            }

            var saveName = gameState.SaveName;

            try
            {
                // Determine race outcome
                var outcome = DetermineOutcome(result, context);
                _logger.Information("Race outcome: {WinCondition} - Winner: {Winner}",
                    outcome.WinCondition, outcome.Winner?.DriverName ?? "None");

                // Apply stat updates
                ApplyStatUpdates(gameState, outcome, context);

                // Apply car health and odometer updates
                ApplyCarUpdates(gameState, result, context);

                // Handle wager/pink slip transfer (if context available)
                if (context != null && outcome.WinCondition != WinCondition.BothCrashed)
                {
                    ApplyWagerTransfer(gameState, outcome, context);
                }

                // Update reputations based on race outcome
                ApplyReputationUpdates(gameState, outcome, context);

                // Update career milestone counters
                UpdateMilestoneCounters(gameState, outcome, context);

                // Handle opponent status changes (e.g., if they lost their only car)
                if (context != null)
                {
                    UpdateOpponentStatus(gameState, context);
                }

                // Create and persist processed session record
                var processedSession = CreateProcessedSession(result, outcome, context);
                await _sessionRepository.SaveAsync(processedSession);

                // Save updated game state
                _gameStateRepository.Save(gameState, saveName);

                _logger.Information("Race result processed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to process race result for session {SessionId}", result.Session.SessionId);
                throw;
            }
        }

        /// <summary>
        /// Determine the race outcome (who won and how)
        /// </summary>
        private RaceOutcome DetermineOutcome(RaceResultJson result, RaceContext? context)
        {
            var outcome = new RaceOutcome();

            // Get participants
            var participants = result.Participants;
            if (participants.Count != 2)
            {
                _logger.Warning("Expected 2 participants, found {Count}", participants.Count);
                outcome.WinCondition = WinCondition.Inconclusive;
                return outcome;
            }

            var participant1 = participants[0];
            var participant2 = participants[1];

            // Determine which participant is the player
            RaceParticipant? playerParticipant = null;
            RaceParticipant? opponentParticipant = null;

            if (context != null)
            {
                playerParticipant = participants.FirstOrDefault(p => p.DriverName == context.PlayerName);
                opponentParticipant = participants.FirstOrDefault(p => p.DriverName == context.OpponentName);
            }

            // If we couldn't match by context, assume first participant is player
            if (playerParticipant == null)
            {
                _logger.Warning("Could not match participants to race context - assuming first participant is player");
                playerParticipant = participant1;
                opponentParticipant = participant2;
            }

            // Check crash scenarios
            bool playerCrashed = playerParticipant.Crash.Crashed;
            bool opponentCrashed = opponentParticipant.Crash.Crashed;

            if (playerCrashed && opponentCrashed)
            {
                // Both crashed - draw
                outcome.WinCondition = WinCondition.BothCrashed;
                outcome.Winner = null;
                outcome.Loser = null;
                outcome.PlayerWon = false;
                return outcome;
            }

            if (playerCrashed)
            {
                // Player crashed, opponent wins
                outcome.WinCondition = WinCondition.PlayerCrashed;
                outcome.Winner = opponentParticipant;
                outcome.Loser = playerParticipant;
                outcome.PlayerWon = false;
                return outcome;
            }

            if (opponentCrashed)
            {
                // Opponent crashed, player wins
                outcome.WinCondition = WinCondition.OpponentCrashed;
                outcome.Winner = playerParticipant;
                outcome.Loser = opponentParticipant;
                outcome.PlayerWon = true;
                return outcome;
            }

            // No crashes - determine by final position
            var playerPosition = playerParticipant.Performance.FinalPosition;
            var opponentPosition = opponentParticipant.Performance.FinalPosition;

            if (playerPosition.HasValue && opponentPosition.HasValue)
            {
                if (playerPosition.Value < opponentPosition.Value)
                {
                    // Player finished in better position (lower number = better)
                    outcome.WinCondition = WinCondition.FinishPosition;
                    outcome.Winner = playerParticipant;
                    outcome.Loser = opponentParticipant;
                    outcome.PlayerWon = true;
                }
                else
                {
                    // Opponent finished in better position
                    outcome.WinCondition = WinCondition.FinishPosition;
                    outcome.Winner = opponentParticipant;
                    outcome.Loser = playerParticipant;
                    outcome.PlayerWon = false;
                }
                return outcome;
            }

            // No positions available - inconclusive
            _logger.Warning("No final positions available - outcome is inconclusive");
            outcome.WinCondition = WinCondition.Inconclusive;
            return outcome;
        }

        /// <summary>
        /// Apply stat updates to racers
        /// </summary>
        private void ApplyStatUpdates(GameState gameState, RaceOutcome outcome, RaceContext? context)
        {
            // For inconclusive or both crashed, no stats updates
            if (outcome.WinCondition == WinCondition.Inconclusive ||
                outcome.WinCondition == WinCondition.BothCrashed)
            {
                _logger.Information("No stat updates (inconclusive or both crashed)");
                return;
            }

            if (outcome.Winner == null || outcome.Loser == null)
            {
                _logger.Warning("Cannot update stats: winner or loser is null");
                return;
            }

            // Update player stats
            var playerStats = gameState.Player.Stats;

            if (outcome.PlayerWon)
            {
                playerStats.Wins++;
                _logger.Debug("Player wins incremented: {Wins}", playerStats.Wins);

                if (context?.IsPinkSlip == true)
                {
                    playerStats.PinkSlipsWon++;
                    _logger.Debug("Player pink slips won: {PinkSlipsWon}", playerStats.PinkSlipsWon);
                }
            }
            else
            {
                playerStats.Losses++;
                _logger.Debug("Player losses incremented: {Losses}", playerStats.Losses);

                if (context?.IsPinkSlip == true)
                {
                    playerStats.PinkSlipsLost++;
                    _logger.Debug("Player pink slips lost: {PinkSlipsLost}", playerStats.PinkSlipsLost);
                }
            }

            playerStats.Races++;
            _logger.Debug("Player total races: {Races}", playerStats.Races);

            // Update opponent stats (if we can find them in game state)
            if (context != null)
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
        }

        /// <summary>
        /// Apply car health degradation and odometer updates
        /// </summary>
        private void ApplyCarUpdates(GameState gameState, RaceResultJson result, RaceContext? context)
        {
            if (context == null)
            {
                _logger.Warning("No race context - skipping car updates");
                return;
            }

            // Update player car
            var playerCar = gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId);
            if (playerCar != null)
            {
                var playerParticipant = result.Participants.FirstOrDefault(p => p.DriverName == context.PlayerName);
                if (playerParticipant != null)
                {
                    ApplyCarDegradation(playerCar, playerParticipant);
                    playerCar.OdometerKM += playerParticipant.Performance.DistanceKm;
                    _logger.Debug("Player car updated: Odometer={Odometer}km, Engine={Engine}%, Transmission={Trans}%",
                        playerCar.OdometerKM, playerCar.EngineHealth * 100, playerCar.TransmissionHealth * 100);
                }
            }
            else
            {
                _logger.Warning("Could not find player car {CarId}", context.PlayerCarInstanceId);
            }

            // Update opponent car
            var opponent = FindRacer(gameState, context.OpponentName);
            if (opponent != null)
            {
                var opponentCar = opponent.Cars.FirstOrDefault(c => c.InstanceId == context.OpponentCarInstanceId);
                if (opponentCar != null)
                {
                    var opponentParticipant = result.Participants.FirstOrDefault(p => p.DriverName == context.OpponentName);
                    if (opponentParticipant != null)
                    {
                        ApplyCarDegradation(opponentCar, opponentParticipant);
                        opponentCar.OdometerKM += opponentParticipant.Performance.DistanceKm;
                        _logger.Debug("Opponent car updated: Odometer={Odometer}km",
                            opponentCar.OdometerKM);
                    }
                }
            }
        }

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

            // Apply degradation
            car.EngineHealth -= engineDeg;
            car.TransmissionHealth -= transDeg;
            car.TireCondition -= tireDeg;
            car.BodyCondition -= bodyDeg;

            // Clamp to [0.0, 1.0]
            car.EngineHealth = Math.Max(0.0, Math.Min(1.0, car.EngineHealth));
            car.TransmissionHealth = Math.Max(0.0, Math.Min(1.0, car.TransmissionHealth));
            car.TireCondition = Math.Max(0.0, Math.Min(1.0, car.TireCondition));
            car.BodyCondition = Math.Max(0.0, Math.Min(1.0, car.BodyCondition));
        }

        /// <summary>
        /// Apply wager transfer and pink slip car transfer
        /// </summary>
        private void ApplyWagerTransfer(GameState gameState, RaceOutcome outcome, RaceContext context)
        {
            if (outcome.Winner == null || outcome.Loser == null)
                return;

            // Find opponent racer
            var opponent = FindRacer(gameState, context.OpponentName);
            if (opponent == null)
            {
                _logger.Warning("Cannot apply wager transfer: opponent {OpponentName} not found", context.OpponentName);
                return;
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
        }

        /// <summary>
        /// Create a processed session record for storage
        /// </summary>
        private ProcessedRaceSession CreateProcessedSession(
            RaceResultJson result,
            RaceOutcome outcome,
            RaceContext? context)
        {
            var session = new ProcessedRaceSession
            {
                SessionId = result.Session.SessionId,
                ProcessedAt = DateTime.Now,
                RaceContextId = context?.ContextId,
                WinnerName = outcome.Winner?.DriverName ?? "None",
                LoserName = outcome.Loser?.DriverName ?? "None",
                PlayerWon = outcome.PlayerWon,
                CashWager = context?.CashWager ?? 0,
                WasPinkSlip = context?.IsPinkSlip ?? false,
                TrackId = result.Session.TrackId,
                RawResultJson = JsonConvert.SerializeObject(result),
                SessionStartTime = DateTime.Parse(result.Session.StartTimestamp),
                DurationSeconds = result.Session.DurationSeconds,
                WinCondition = outcome.WinCondition.ToString()
            };

            return session;
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
        private void ApplyReputationUpdates(GameState gameState, RaceOutcome outcome, RaceContext? context)
        {
            // Skip reputation updates for inconclusive or both crashed
            if (outcome.WinCondition == WinCondition.Inconclusive ||
                outcome.WinCondition == WinCondition.BothCrashed)
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
            if (context != null)
            {
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
        }

        /// <summary>
        /// Update career milestone counters based on race outcome
        /// </summary>
        private void UpdateMilestoneCounters(GameState gameState, RaceOutcome outcome, RaceContext? context)
        {
            // Skip milestone updates for inconclusive or both crashed
            if (outcome.WinCondition == WinCondition.Inconclusive ||
                outcome.WinCondition == WinCondition.BothCrashed)
            {
                return;
            }

            var career = gameState.Career;

            if (outcome.PlayerWon)
            {
                // Update total wins
                career.IncrementCounter(MilestoneTrigger.TotalWins);

                // Update race-type specific wins
                if (context != null)
                {
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
    /// Result of race outcome determination
    /// </summary>
    public class RaceOutcome
    {
        public RaceParticipant? Winner { get; set; }
        public RaceParticipant? Loser { get; set; }
        public bool PlayerWon { get; set; }
        public WinCondition WinCondition { get; set; }
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
        Inconclusive
    }
}
