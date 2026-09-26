using LiteDB;
using Newtonsoft.Json;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.News;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Police;
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
        private readonly ICarPartsService? _parts;
        private readonly IOpponentEvolutionService _evolution;
        private readonly IContentCatalogRepository? _catalog;
        private readonly IAppLogger _logger;
        private readonly Random _random = new();

        // Car health degradation constants (per race), for result files without the car's condition (before schema 1.2)
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
            IRaceEventService raceEventService,
            ICarPartsService? parts = null,
            IOpponentEvolutionService? evolution = null,
            IContentCatalogRepository? catalog = null)
        {
            _gameStateRepository = gameStateRepository;
            _sessionRepository = sessionRepository;
            _careerProgressService = careerProgressService;
            _raceEventService = raceEventService;
            _parts = parts;
            _evolution = evolution ?? new OpponentEvolutionService();
            _catalog = catalog;
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

            // A test-and-tune is no race: the player's car and its passes, nothing else
            if (context.IsTestAndTune)
                return ProcessTestAndTune(result, context, gameState, saveName, startedAt);

            var outcome = DetermineOutcome(result, context);
            _logger.Information("Race outcome: {WinCondition} - Winner: {Winner}",
                outcome.WinCondition, outcome.Winner?.DriverName ?? "None");

            var processedSession = CreateProcessedSession(result, outcome, context, startedAt, gameState.Date);

            // 2. Apply the race to the game, once
            // (wear first: a pink slip may move the car to its new owner)
            // 3. The state and the record that the race was applied, together or not at all
            var messages = new List<PlayerMessage>();
            var newBest = false;
            ApplyAndSave(gameState, saveName, processedSession, () =>
            {
                (var damage, newBest) = ApplyCarUpdates(gameState, outcome, context);
                ApplyRace(gameState, outcome, context, messages);

                // A busted player's car went to the impound instead of home, and the police message says so
                var towed = outcome.Player?.Crash.Crashed == true && outcome.Pursuit?.PlayerBusted != true;
                if (CarReport(damage, towed) is { } report) messages.Add(report);
            });

            // A drag race hands out timeslips, whatever came of it: first, before what the race did
            if (outcome.Player?.Timeslip != null || outcome.Opponent?.Timeslip != null)
            {
                var note = context.IsBracket ? BracketNote(outcome, context) : null;
                if (newBest) note = Join(note, $"A new best for your car: {BracketRules.Show(outcome.Player?.Timeslip?.QuarterMileSeconds)}.");
                messages.Insert(0, new PlayerMessage("Timeslip", string.Empty)
                {
                    Timeslip = new TimeslipCard(
                        [
                            new TimeslipLane(context.PlayerName, outcome.Player?.Timeslip, context.PlayerDialIn),
                            new TimeslipLane(context.OpponentName, outcome.Opponent?.Timeslip, context.OpponentDialIn)
                        ], note)
                });
            }

            _logger.Information("Race result processed successfully");
            return messages;
        }

        /// <summary>
        /// A test-and-tune: what the passes did to the car (wear, damage, the odometer), its best quarter, and the passes'
        /// slips for the player. Nothing else changes: no stats, no reputation, no money.
        /// </summary>
        private List<PlayerMessage> ProcessTestAndTune(RaceResultJson result, RaceContext context, GameState gameState, string saveName, DateTime startedAt)
        {
            var participant = result.Participants.FirstOrDefault(p => p.IsPlayer == true) ?? result.Participants.FirstOrDefault();
            var outcome = new RaceDecision { Player = participant, WinCondition = WinCondition.TestAndTune };
            var processedSession = CreateProcessedSession(result, outcome, context, startedAt, gameState.Date);
            var passes = participant?.Passes ?? [];
            _logger.Information("Test-and-tune: {Passes} pass(es), best {Best}", passes.Count, participant?.Timeslip?.QuarterMileSeconds);

            var messages = new List<PlayerMessage>();
            var newBest = false;
            ApplyAndSave(gameState, saveName, processedSession, () =>
            {
                var car = gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId);
                if (car != null && participant != null)
                {
                    var damage = ApplyCarDegradation(car, participant, PartGroups(), gameState.Rules.CarWearMultiplier);
                    car.OdometerKM += RaceDistance(participant);
                    foreach (var pass in passes)
                        newBest |= car.History.RecordQuarter(pass.QuarterMileSeconds, pass.QuarterMileMph, gameState.Date);

                    if (CarReport(damage, towed: participant.Crash.Crashed) is { } report) messages.Add(report);
                }

                if (gameState.PendingRace?.ContextId == context.ContextId) gameState.PendingRace = null;
            });

            if (passes.Count > 0)
            {
                var best = passes.Where(p => p.QuarterMileSeconds != null).MinBy(p => p.QuarterMileSeconds);
                string? note = best == null ? null : $"Best of the day: pass {best.Pass ?? passes.IndexOf(best) + 1}, {BracketRules.Show(best.QuarterMileSeconds)}.";
                if (newBest) note = Join(note, "A new best for your car.");
                messages.Insert(0, new PlayerMessage("Timeslip", string.Empty)
                {
                    Timeslip = new TimeslipCard(
                        passes.Select((p, i) => new TimeslipLane($"Pass {p.Pass ?? i + 1}", p)).ToList(), note, "Test and Tune")
                });
            }
            else
            {
                messages.Insert(0, new PlayerMessage("Test and Tune", "You never made a full pass: there's no timeslip to take home."));
            }

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
            // Its own mapper (made like the one the saves use), used under a lock: LiteDB builds a type's
            // mapping on first use and a second thread can see it half-built, which would make a snapshot that
            // silently drops members. The shared global mapper is also used by the catalog and the saves on
            // other threads.
            private static readonly BsonMapper Mapper = SaveMapper.Create();

            private BsonDocument _player = null!;
            private BsonDocument _racers = null!;
            private BsonDocument _career = null!;
            private RaceContext? _pendingRace;
            private DateTime _lastPlayed;

            // Never changed once written: the lists are copied, the items shared
            private List<StreetTalkItem> _streetTalk = null!;
            private List<NewsArticle> _news = null!;

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
                        _lastPlayed = gameState.LastPlayedDate,
                        _streetTalk = [.. gameState.StreetTalk ?? []],
                        _news = [.. gameState.News ?? []]
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
                gameState.StreetTalk = [.. _streetTalk];
                gameState.News = [.. _news];
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
            Describe(processedSession, context, gameState.Date);

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
            // How things stood before the race, for the paper
            var before = new StandingBefore(
                gameState.Player.Stats.Reputation,
                FindRacer(gameState, context.OpponentName)?.Stats.Reputation ?? 0,
                gameState.Player.Stats.Wins == 0);

            // Apply stat updates
            ApplyStatUpdates(gameState, outcome, context);
            RecordCarHistory(gameState, outcome, context);

            // Handle wager/pink slip transfer
            var moved = new StakesMoved(false, false);
            if (outcome.WinCondition is not (WinCondition.BothCrashed or WinCondition.BothOut))
            {
                moved = ApplyWagerTransfer(gameState, outcome, context);
            }

            // A rival remembers a pink slip lost to the player; a rematch settles it
            var grudge = ApplyGrudge(gameState, outcome, context, moved);

            // The police: fines, the impound, a name for getting away. After the stakes, which may have moved the cars.
            if (outcome.Pursuit is { Started: true } pursuit)
                ApplyPursuit(gameState, outcome, context, pursuit, messages);

            // Update reputations based on race outcome
            ApplyReputationUpdates(gameState, outcome, context);

            if (outcome.WinCondition == WinCondition.FalseStart)
                messages.Add(ApplyFalseStart(gameState, context));
            else if (outcome.WinCondition == WinCondition.PlayerAbandoned)
                messages.Add(new PlayerMessage("Out of the Race",
                    $"You left the race before the finish, and that counts as a loss to {context.OpponentName}."));
            else if (outcome.WinCondition == WinCondition.PlayerDisqualified)
                messages.Add(new PlayerMessage("Disqualified",
                    $"You hit {context.OpponentName} in his own lane. That's a DQ, and the race is his."));
            else if (outcome.WinCondition == WinCondition.OpponentDisqualified)
                messages.Add(new PlayerMessage("Rival Disqualified",
                    $"{context.OpponentName} hit you in your own lane. That's a DQ, and the race is yours."));
            else if (outcome.WinCondition == WinCondition.PlayerBrokeDown)
                messages.Add(new PlayerMessage("Broke Down",
                    $"Your {Breakdowns.Describe(outcome.Player?.Breakdown)} gave out before the line. The race is {context.OpponentName}'s."));
            else if (outcome.WinCondition == WinCondition.OpponentBrokeDown)
                messages.Add(new PlayerMessage("Rival Broke Down",
                    $"{context.OpponentName}'s {Breakdowns.Describe(outcome.Opponent?.Breakdown)} gave out before the line. The race is yours."));
            else if (outcome.WinCondition == WinCondition.BothOut)
                messages.Add(new PlayerMessage("Nobody Finished",
                    "Neither car made it to the line. It's a draw, and nothing changes hands."));
            else if (outcome.WinCondition == WinCondition.PlayerBrokeOut)
                messages.Add(new PlayerMessage("Breakout",
                    $"You ran quicker than your {BracketRules.Show(context.PlayerDialIn)} dial-in. That's a breakout, and the race is {context.OpponentName}'s."));
            else if (outcome.WinCondition == WinCondition.OpponentBrokeOut)
            {
                var theirs = FindRacer(gameState, context.OpponentName) is { } rival ? OpponentRules.Possessive(rival) : "their";
                messages.Add(new PlayerMessage("Rival Broke Out",
                    $"{context.OpponentName} ran quicker than {theirs} {BracketRules.Show(context.OpponentDialIn)} dial-in. The race is yours."));
            }

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

            // The paper writes it up, when there is a story in it
            WriteUp(gameState, outcome, context, moved, grudge, before);

            // The race is settled: it is no longer waiting for a result
            if (gameState.PendingRace == null || gameState.PendingRace.ContextId == context.ContextId)
                gameState.PendingRace = null;

            return moved;
        }

        /// <summary>
        /// A false start: no contest, nothing changes hands, but the player's name takes the hit
        /// (<see cref="RacerStats.FalseStarts"/>)
        /// </summary>
        private PlayerMessage ApplyFalseStart(GameState gameState, RaceContext context)
        {
            var stats = gameState.Player.Stats;
            var before = stats.Reputation;
            stats.FalseStarts++;
            stats.Reputation = stats.CalculateReputation();
            gameState.Career.SetCounter(MilestoneTrigger.ReputationReached, stats.Reputation);

            _logger.Information("False start against {Opponent}: no contest, reputation {Before} -> {After}",
                context.OpponentName, before, stats.Reputation);

            var stakes = context.IsPinkSlip || context.CashWager > 0
                ? " Nothing changes hands."
                : string.Empty;
            var reputation = stats.Reputation < before
                ? $"\n\nReputation: {before} → {stats.Reputation}"
                : string.Empty;
            return new PlayerMessage("False Start",
                $"You jumped the gun against {context.OpponentName}. No contest.{stakes}{reputation}");
        }

        /// <summary>
        /// What the police chase did: a busted racer pays a fine and their car goes to the impound (if it is still
        /// theirs after the stakes), and a player who got away makes a name for it (<see cref="RacerStats.PoliceEscapes"/>).
        /// A fine the racer can't pay goes onto the impound's bill.
        /// </summary>
        private void ApplyPursuit(GameState gameState, RaceDecision outcome, RaceContext context, PursuitResult pursuit, List<PlayerMessage> messages)
        {
            var now = gameState.Date;
            var player = gameState.Player;
            var lines = new List<string>();

            if (pursuit.PlayerBusted)
            {
                var car = player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId);
                var (fine, unpaid, impounded) = Bust(player, car, now);
                _logger.Information("The police caught the player: fined ${Fine}, car {Car} impounded until {Until}",
                    fine, car?.DefinitionId ?? "(not theirs any more)", car?.ImpoundedUntil);

                lines.Add(outcome.WinCondition == WinCondition.BothBusted
                    ? $"The police caught you and {context.OpponentName}. No contest, and nothing changes hands."
                    : $"The police caught you. The race goes to {context.OpponentName}.");
                lines.Add($"Fine: ${fine:N0}");
                if (impounded && car != null)
                {
                    lines.Add($"Your car is in the police impound until {car.ImpoundedUntil:dddd d MMMM}. Collecting it costs ${car.ImpoundFee:N0}.");
                    if (unpaid > 0)
                        lines.Add($"You couldn't pay ${unpaid:N0} of the fine: it's on the impound's bill.");
                }
                else if (context.IsPinkSlip)
                {
                    lines.Add($"The pink slip was signed: {context.OpponentName} collects your car from the police.");
                }
            }

            if (pursuit.RivalBusted && FindRacer(gameState, context.OpponentName) is { } rival)
            {
                var car = rival.Cars.FirstOrDefault(c => c.InstanceId == context.OpponentCarInstanceId);
                var (fine, _, impounded) = Bust(rival, car, now);
                _logger.Information("The police caught {Rival}: fined ${Fine}, car impounded {Impounded}", rival.Name, fine, impounded);

                if (outcome.WinCondition == WinCondition.OpponentBusted)
                    lines.Add($"The police caught {rival.Name}. The race is yours.");
                if (impounded) lines.Add($"{rival.Name}'s car is in the impound for a while.");
            }

            if (pursuit.PlayerEscaped)
            {
                var before = player.Stats.Reputation;
                player.Stats.PoliceEscapes++;
                player.Stats.Reputation = player.Stats.CalculateReputation();
                gameState.Career.SetCounter(MilestoneTrigger.ReputationReached, player.Stats.Reputation);
                _logger.Information("The player got away from the police ({Escapes} times now)", player.Stats.PoliceEscapes);

                lines.Add("You lost the cops. The street will hear about it.");
                if (player.Stats.Reputation > before)
                    lines.Add($"Reputation: {before} → {player.Stats.Reputation}");
            }

            if (lines.Count == 0) return;
            messages.Add(new PlayerMessage(pursuit.PlayerBusted ? "Busted" : pursuit.PlayerEscaped ? "Got Away" : "The Police", string.Join("\n\n", lines))
            {
                // Home on foot: the garage is where the player finds out what's left
                TowedToGarage = pursuit.PlayerBusted
            });
        }

        /// <summary>
        /// A racer caught by the police: the fine off their money, as far as it goes, and <paramref name="car"/> (null
        /// when it isn't theirs any more) into the impound with what's left of the fine on its bill. Returns the fine,
        /// what of it the racer couldn't pay, and whether the car was impounded.
        /// </summary>
        private static (decimal Fine, decimal Unpaid, bool Impounded) Bust(Racer racer, Car? car, DateTime now)
        {
            var earlier = racer.Stats.PoliceBusts;
            var fine = PoliceRules.Fine(earlier);
            var paid = Math.Min(fine, Math.Max(0m, racer.Money));
            racer.Money -= paid;
            racer.Stats.TotalLosses += paid;
            racer.Stats.PoliceBusts++;

            if (car == null) return (fine, 0m, false);

            PoliceRules.Impound(car, now, earlier);
            car.ImpoundFee += fine - paid;
            return (fine, fine - paid, true);
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
            outcome.Pursuit = result.Pursuit;

            // Jumping the start calls the race off before anything else counts: no contest
            if (playerParticipant.FalseStart == true || result.Session.EndReason == EndReasons.FalseStart)
            {
                outcome.WinCondition = WinCondition.FalseStart;
                outcome.PlayerWon = false;
                return outcome;
            }

            // The police: a racer they caught is out of the race, whatever else happened in it (a wreck, the finish
            // line). Both caught is no contest. The patrol only turns up after the green, so a false start comes first.
            if (result.Pursuit is { Started: true } pursuit && (pursuit.PlayerBusted || pursuit.RivalBusted))
            {
                outcome.WinCondition = pursuit.PlayerBusted && pursuit.RivalBusted ? WinCondition.BothBusted
                    : pursuit.PlayerBusted ? WinCondition.PlayerBusted
                    : WinCondition.OpponentBusted;
                outcome.PlayerWon = outcome.WinCondition == WinCondition.OpponentBusted;
                return outcome;
            }

            // Put back in the middle of the race (the pits, a lane violation): the player is out, and loses
            if (result.Session.EndReason == EndReasons.Abandoned)
            {
                outcome.WinCondition = WinCondition.PlayerAbandoned;
                outcome.PlayerWon = false;
                return outcome;
            }

            // A drag race's contact: whoever hit the other out of their own lane is out, whatever the crash did
            if (playerParticipant.Disqualified == true || result.Session.EndReason == EndReasons.Disqualified)
            {
                outcome.WinCondition = WinCondition.PlayerDisqualified;
                outcome.PlayerWon = false;
                return outcome;
            }

            if (opponentParticipant.Disqualified == true)
            {
                outcome.WinCondition = WinCondition.OpponentDisqualified;
                outcome.PlayerWon = true;
                return outcome;
            }

            // Check crash and breakdown scenarios: a car that crashed or broke down is out of the race
            bool playerCrashed = playerParticipant.Crash.Crashed;
            bool opponentCrashed = opponentParticipant.Crash.Crashed;
            bool playerBroke = playerParticipant.BrokeDown == true || (result.Session.EndReason == EndReasons.BrokeDown && !playerCrashed);
            bool opponentBroke = opponentParticipant.BrokeDown == true;

            if (playerCrashed && opponentCrashed)
            {
                // Both crashed - draw
                outcome.WinCondition = WinCondition.BothCrashed;
                outcome.PlayerWon = false;
                return outcome;
            }

            if ((playerCrashed || playerBroke) && (opponentCrashed || opponentBroke))
            {
                // Neither car made it: a draw, whatever put each one out
                outcome.WinCondition = WinCondition.BothOut;
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

            if (playerBroke)
            {
                outcome.WinCondition = WinCondition.PlayerBrokeDown;
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

            if (opponentBroke)
            {
                outcome.WinCondition = WinCondition.OpponentBrokeDown;
                outcome.PlayerWon = true;
                return outcome;
            }

            // A bracket race is won at the quarter by its own rules: the dial-ins, then who got there first
            if (context.IsBracket && BracketRules.Decide(playerParticipant.Timeslip, context.PlayerDialIn!.Value,
                    opponentParticipant.Timeslip, context.OpponentDialIn!.Value) is { } bracket)
            {
                outcome.PlayerWon = bracket.PlayerWon;
                outcome.WinCondition = bracket.PlayerBrokeOut && !bracket.PlayerWon ? WinCondition.PlayerBrokeOut
                    : bracket.OpponentBrokeOut && bracket.PlayerWon ? WinCondition.OpponentBrokeOut
                    : WinCondition.BracketFinish;
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

                    // A race against the player shapes the rival: a win makes them bolder, a wreck careful
                    if (opponent is Opponent rival)
                    {
                        if (outcome.WinCondition == WinCondition.OpponentCrashed) _evolution.ApplyCrashEvolution(rival);
                        else if (outcome.PlayerWon) _evolution.ApplyLossEvolution(rival);
                        else _evolution.ApplyWinEvolution(rival);
                    }
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

        /// <summary>The bracket race in a line under the slip: who won and why</summary>
        private static string? BracketNote(RaceDecision outcome, RaceContext context) => outcome.WinCondition switch
        {
            WinCondition.BracketFinish => outcome.PlayerWon ? "On your dial-in and first to the quarter: the race is yours."
                : $"{context.OpponentName} got to the quarter first on the dial-in.",
            WinCondition.PlayerBrokeOut => "You broke out: quicker than your dial-in.",
            WinCondition.OpponentBrokeOut => $"{context.OpponentName} broke out: quicker than the dial-in.",
            _ => null
        };

        private static string? Join(string? first, string second) => first == null ? second : first + "\n" + second;

        /// <summary>
        /// What the player hears about their car after the session: towed home when it crashed (the player goes with
        /// it to see what the crash did), else the damage report; null when there is nothing to tell
        /// </summary>
        private static PlayerMessage? CarReport(IReadOnlyList<string> damage, bool towed)
        {
            if (towed)
            {
                return new PlayerMessage("Towed Home", damage.Count == 0
                    ? "Your car was towed back to the garage."
                    : "Your car was towed back to the garage. Here's what the crash did:\n\n" + string.Join("\n", damage))
                {
                    TowedToGarage = true
                };
            }

            return damage.Count > 0 ? new PlayerMessage("Damage Report", string.Join("\n", damage)) : null;
        }

        /// <summary>
        /// What the race did to both cars, their odometers and their best quarters. Returns the player's car's damage
        /// report lines (none when nothing worth telling happened to it), and whether it ran its best quarter.
        /// </summary>
        private (List<string> Damage, bool NewBest) ApplyCarUpdates(GameState gameState, RaceDecision outcome, RaceContext context)
        {
            if (outcome.Player == null || outcome.Opponent == null)
            {
                _logger.Warning("Participants not identified - skipping car updates");
                return ([], false);
            }

            var groupOf = PartGroups();
            var damage = new List<string>();
            var newBest = false;

            // Update player car
            var playerCar = gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId);
            if (playerCar != null)
            {
                damage = ApplyCarDegradation(playerCar, outcome.Player, groupOf, gameState.Rules.CarWearMultiplier);
                playerCar.OdometerKM += RaceDistance(outcome.Player);
                newBest = playerCar.History.RecordQuarter(outcome.Player.Timeslip?.QuarterMileSeconds, outcome.Player.Timeslip?.QuarterMileMph, gameState.Date);
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
                ApplyCarDegradation(opponentCar, outcome.Opponent, groupOf, gameState.Rules.CarWearMultiplier);
                opponentCar.OdometerKM += RaceDistance(outcome.Opponent);
                opponentCar.History.RecordQuarter(outcome.Opponent.Timeslip?.QuarterMileSeconds, outcome.Opponent.Timeslip?.QuarterMileMph, gameState.Date);
                _logger.Debug("Opponent car updated: Odometer={Odometer}km",
                    opponentCar.OdometerKM);
            }

            return (damage, newBest);
        }

        /// <summary>The parts' groups off the catalog; null when there are no parts (the cars' own figures take the race)</summary>
        private Func<string, string?>? PartGroups()
        {
            try
            {
                return _parts is { IsAvailable: true } parts ? CarCondition.Groups(parts.Catalog) : null;
            }
            catch (Exception ex)
            {
                _logger.Warning("The parts catalog could not be read: the race's damage goes on the cars' own figures ({Error})", ex.Message);
                return null;
            }
        }

        /// <summary>The distance to put on the odometer: the validator rejected negative and non-finite ones</summary>
        private static double RaceDistance(RaceParticipant participant) =>
            Math.Clamp(participant.Performance.DistanceKm, 0.0, MAX_RACE_DISTANCE_KM);

        /// <summary>
        /// What the race did to one car. AC's report of the car (schema 1.2 on) goes onto its parts; an older file,
        /// which has none, takes the flat wear of before. Returns the report's lines for the player.
        /// </summary>
        private List<string> ApplyCarDegradation(Car car, RaceParticipant participant, Func<string, string?>? groupOf, double wearMultiplier)
        {
            if (participant.Condition is { } condition)
            {
                var report = CarCondition.ApplyRace(car, condition, RaceDistance(participant), groupOf, wearMultiplier);
                _logger.Information("{Car}: {Report}", car.DefinitionId, report.Count == 0 ? "no damage" : string.Join(" ", report));
                return report;
            }

            // A file from before the race mode reported the car's condition
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

            // Apply degradation, as fast as the game's difficulty wears cars, kept in [0.0, 1.0]
            var rate = GameRules.Sane(wearMultiplier);
            engineDeg *= rate;
            transDeg *= rate;
            tireDeg *= rate;
            bodyDeg *= rate;
            car.EngineHealth = Math.Clamp(car.EngineHealth - engineDeg, 0.0, 1.0);
            car.TransmissionHealth = Math.Clamp(car.TransmissionHealth - transDeg, 0.0, 1.0);
            car.TireCondition = Math.Clamp(car.TireCondition - tireDeg, 0.0, 1.0);
            car.BodyCondition = Math.Clamp(car.BodyCondition - bodyDeg, 0.0, 1.0);
            return new List<string>();
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
                        opponentCar.History.ChangeHands(gameState.Player.Name, gameState.Date, CarAcquisition.PinkSlip);
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
                        playerCar.History.ChangeHands(opponent.Name, gameState.Date, CarAcquisition.PinkSlip);
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
            DateTime startedAt,
            DateTime gameDate)
        {
            return Describe(new ProcessedRaceSession
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
            }, context, gameDate);
        }

        /// <summary>Who raced whom in what, when in the game, and what for: the race as the game's history keeps it</summary>
        private static ProcessedRaceSession Describe(ProcessedRaceSession session, RaceContext context, DateTime gameDate)
        {
            session.GameDate = gameDate;
            session.PlayerName = context.PlayerName;
            session.OpponentName = context.OpponentName;
            session.PlayerCarInstanceId = context.PlayerCarInstanceId;
            session.OpponentCarInstanceId = context.OpponentCarInstanceId;
            session.RaceType = context.RaceType.ToString();
            session.EventId = context.EventId;
            return session;
        }

        /// <summary>The player's and the rival's standing before the race: the paper tells an upset or a first win by it</summary>
        private readonly record struct StandingBefore(int PlayerReputation, int RivalReputation, bool NoWinsYet);

        /// <summary>What the race did about a rival's grudge: a rematch was raced, the rival wants the car back now</summary>
        private readonly record struct GrudgeOutcome(bool Rematch, bool Started);

        /// <summary>A settled race goes into both cars' histories, whoever owns them after it</summary>
        private void RecordCarHistory(GameState gameState, RaceDecision outcome, RaceContext context)
        {
            if (!outcome.IsDecided) return;

            gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId)
                ?.History.RecordRace(outcome.PlayerWon, context.IsPinkSlip);

            if (!context.IsEventOnlyOpponent)
            {
                FindRacer(gameState, context.OpponentName)?.Cars.FirstOrDefault(c => c.InstanceId == context.OpponentCarInstanceId)
                    ?.History.RecordRace(!outcome.PlayerWon, context.IsPinkSlip);
            }
        }

        /// <summary>
        /// A pink-slip race with a rival who wanted a rematch settles it, win or lose; a rival who has just lost their
        /// car to the player wants a rematch (<see cref="Grudges"/>). The street hears about both.
        /// </summary>
        private GrudgeOutcome ApplyGrudge(GameState gameState, RaceDecision outcome, RaceContext context, StakesMoved moved)
        {
            if (!outcome.IsDecided || !context.IsPinkSlip || context.IsEventOnlyOpponent) return default;
            if (FindRacer(gameState, context.OpponentName) is not Opponent rival) return default;

            var talk = new List<string>();
            var rematch = Grudges.WantsRematch(rival);
            if (Grudges.Settle(rival, rivalWon: !outcome.PlayerWon, gameState.Player.Name) is { } settled) talk.Add(settled);

            var started = false;
            if (outcome.PlayerWon && moved.Car
                && gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.OpponentCarInstanceId) is { } taken)
            {
                talk.Add(Grudges.Start(rival, taken, gameState.Date, gameState.Player.Name, CarName(taken.DefinitionId)));
                started = true;
                _logger.Information("{Rival} wants a rematch for the {Car} until {Until}", rival.Name, taken.DefinitionId, rival.Grudge!.Until);
            }

            OpponentLifeService.AddTalk(gameState, gameState.Date, talk);
            return new GrudgeOutcome(rematch, started);
        }

        /// <summary>The piece the paper writes about the race, when there is a story in it</summary>
        private void WriteUp(GameState gameState, RaceDecision outcome, RaceContext context, StakesMoved moved, GrudgeOutcome grudge, StandingBefore before)
        {
            var rival = FindRacer(gameState, context.OpponentName);
            var facts = new PlayerRaceFacts
            {
                Date = gameState.Date,
                PlayerName = gameState.Player.Name,
                RivalName = context.OpponentName,
                RivalPossessive = rival == null ? "their" : OpponentRules.Possessive(rival),
                RivalIsKing = rival is Opponent { IsKing: true },
                PlayerReputation = before.PlayerReputation,
                RivalReputation = before.RivalReputation,
                PlayerCar = CarName(CarDefinitionOf(gameState, context.PlayerCarInstanceId)),
                RivalCar = CarName(CarDefinitionOf(gameState, context.OpponentCarInstanceId) is { Length: > 0 } rivalCar
                    ? rivalCar : context.OpponentCarDefinitionId),
                IsDrag = context.RaceType == RaceType.DragRace,
                Decided = outcome.IsDecided,
                PlayerWon = outcome.PlayerWon,
                PinkSlip = context.IsPinkSlip && moved.Car,
                CashWager = moved.Cash ? context.CashWager : 0m,
                PlayerCrashed = outcome.WinCondition == WinCondition.PlayerCrashed,
                RivalCrashed = outcome.WinCondition == WinCondition.OpponentCrashed,
                PlayerBusted = outcome.Pursuit?.PlayerBusted == true,
                RivalBusted = outcome.Pursuit?.RivalBusted == true,
                PlayerEscaped = outcome.Pursuit?.PlayerEscaped == true,
                EventName = EventName(context.EventId),
                Rematch = grudge.Rematch,
                RivalSwearsRevenge = grudge.Started,
                FirstWin = before.NoWinsYet
            };

            if (NewsWriter.PlayerRace(facts, _random) is { } article)
            {
                NewsWriter.Add(gameState, gameState.Date, [article]);
                _logger.Information("The paper writes it up: {Headline}", article.Headline);
            }
        }

        /// <summary>The model of a car in the race, whoever has it now: after a pink slip it has changed garages</summary>
        private static string CarDefinitionOf(GameState gameState, Guid carInstanceId)
        {
            var racers = gameState.Racers.All;
            var car = gameState.Player.Cars.Concat(racers.SelectMany(r => r.Cars)).FirstOrDefault(c => c.InstanceId == carInstanceId);
            return car?.DefinitionId ?? string.Empty;
        }

        private string CarName(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId)) return "car";
            return _catalog == null ? definitionId : CarNames.Of(_catalog, definitionId);
        }

        private string? EventName(string? eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            try
            {
                return _raceEventService.GetEventDefinition(eventId)?.Name;
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not name event {EventId} for the paper: {Error}", eventId, ex.Message);
                return null;
            }
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

            // Cars owned and reputation as they stand now (not cumulative)
            career.SyncStanding(gameState.Player);

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
                // What this game's difficulty makes of the prize
                reward = reward.ScaledBy(gameState.Rules.RacePrizeMultiplier);

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

                // The special item: a part for the car that won it, on the shelf. The reward is this race's copy,
                // so it can say what the player got.
                if (!string.IsNullOrEmpty(reward.SpecialItem))
                {
                    reward.SpecialItemDescription = GiveSpecialItem(gameState, context, reward.SpecialItem);
                }

                // The screen that ran the race tells the player
                var rewardMessage = EventRewardMessage(reward);
                if (rewardMessage != null)
                    messages.Add(rewardMessage);
            }
        }

        /// <summary>What a prize camshaft is worth in cash when no better one fits the car that won it</summary>
        public const decimal CamshaftPrizeCash = 500m;

        /// <summary>
        /// Gives an event's special item and says what it was. "rare_camshaft" is the dearest camshaft that fits
        /// the engine of the car that won (<see cref="PrizeParts.BestCamshaft"/>), one for each camshaft the engine
        /// takes, on the shelf; when none fits or better, its worth in cash.
        /// </summary>
        private string GiveSpecialItem(GameState gameState, RaceContext context, string item)
        {
            if (item != "rare_camshaft")
            {
                _logger.Warning("Event reward: special item '{Item}' is nothing the game knows; not given", item);
                return string.Empty;
            }

            var car = gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId);
            try
            {
                if (_parts is { IsAvailable: true } parts && PrizeParts.BestCamshaft(parts.Catalog, car?.Engine) is var (cam, count))
                {
                    for (var i = 0; i < count; i++) gameState.Player.Parts.Add(new PartInstance(cam.Id));
                    var name = cam.DisplayName ?? cam.Name;
                    _logger.Information("Event reward: {Count} x {Cam} on the shelf", count, cam.Id);
                    return count > 1 ? $"{count} x {name} (on your shelf)" : $"{name} (on your shelf)";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not pick the prize camshaft: its worth is paid instead");
            }

            var cash = Math.Round(GameRules.Scale(CamshaftPrizeCash, gameState.Rules.PartPriceMultiplier) / 5) * 5;
            gameState.Player.Money += cash;
            gameState.Player.Stats.TotalEarnings += cash;
            _logger.Information("Event reward: no better camshaft fits {Car}; ${Cash} instead", car?.DefinitionId ?? "(no car)", cash);
            return $"${cash:N0} for a camshaft (none better fits your engine)";
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
        /// If opponent lost their only car in a pink slip race, they sit it out (retired) until the daily review
        /// (<see cref="OpponentLifeService"/>) finds them another
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
            if (opponent.Cars.Count == 0 && opponent.Status != RacerStatus.Retired)
            {
                // Opponent lost their only car: off the street until they buy another
                gameState.Racers.MoveRacer(opponent.Name, RacerStatus.Retired);

                _logger.Information("Opponent {Name} is sitting it out (no cars remaining)", opponent.Name);
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

        /// <summary>The police chase, when the police were sent; null otherwise</summary>
        public PursuitResult? Pursuit { get; set; }

        /// <summary>True when the race has a winner and a loser (stats, money and reputation change)</summary>
        public bool IsDecided => WinCondition is not (WinCondition.Inconclusive or WinCondition.BothCrashed or WinCondition.BothOut
            or WinCondition.FalseStart or WinCondition.BothBusted or WinCondition.TestAndTune);

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
        Forfeit,

        /// <summary>
        /// The player jumped the start: no contest, nothing changes hands, reputation drops
        /// </summary>
        FalseStart,

        /// <summary>
        /// The player was put back in the middle of the race (the pits, a lane violation): out, and loses
        /// </summary>
        PlayerAbandoned,

        /// <summary>
        /// Drag race: the player hit the rival out of their own lane and is disqualified: loses
        /// </summary>
        PlayerDisqualified,

        /// <summary>
        /// Drag race: the rival hit the player out of their own lane and is disqualified: the player wins
        /// </summary>
        OpponentDisqualified,

        /// <summary>
        /// The player's car broke down before the line (engine, gearbox, a corner, a tyre): out, and loses
        /// </summary>
        PlayerBrokeDown,

        /// <summary>
        /// The rival's car broke down before the line: the player wins
        /// </summary>
        OpponentBrokeDown,

        /// <summary>
        /// Neither car made it to the line, one broke down and the other crashed or broke down too: a draw
        /// </summary>
        BothOut,

        /// <summary>
        /// The police caught the player, before or after the line: the race is lost, a fine, the car impounded
        /// </summary>
        PlayerBusted,

        /// <summary>
        /// The police caught the rival and not the player: the race is the player's
        /// </summary>
        OpponentBusted,

        /// <summary>
        /// The police caught both: no contest, nothing changes hands, both fined and impounded
        /// </summary>
        BothBusted,

        /// <summary>
        /// A bracket race with neither car under its dial-in: the first to the quarter won
        /// (<see cref="BracketRules"/>)
        /// </summary>
        BracketFinish,

        /// <summary>
        /// A bracket race: the player ran quicker than their dial-in and loses (unless the rival broke out by more,
        /// which is <see cref="OpponentBrokeOut"/>)
        /// </summary>
        PlayerBrokeOut,

        /// <summary>
        /// A bracket race: the rival ran quicker than its dial-in (by more than the player, if both did): the player wins
        /// </summary>
        OpponentBrokeOut,

        /// <summary>A test-and-tune: no race, nobody wins</summary>
        TestAndTune
    }
}
