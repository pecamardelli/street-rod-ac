using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Race.Validation;
using System.IO;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Main orchestrator for race result ingestion pipeline
    /// Implements the ingestion pipeline: filter, validate, dedup, match the race, apply and save, archive
    ///
    /// A result file is tied to its race by the context id the launcher wrote into race.ini and the Lua app
    /// wrote back (K1). It is applied only with that race's context: the race just run, or the save's pending
    /// race for a file left over when the app closed during a race. A file for another race is never applied
    /// here, so a leftover never pays out the current race's wager: it stays in the inbox for its own save (another
    /// save's race), or is quarantined with the reason when this save has settled that race already or the file has
    /// waited a month. Files that name no race are judged by their start time.
    ///
    /// A file is applied and saved while it is still in the inbox, and archived after: the app killed at any point
    /// leaves either a race still to apply or one the next pass recognises as done, never a race without its file.
    ///
    /// A file that is there but cannot be dealt with right now (locked by a virus scanner just after the Lua
    /// write, the dedup or settled check failing, the save failing) is never taken for "no result":
    /// it stays in the inbox, the race stays pending, and a later pass settles it. A race is forfeited only
    /// when AC ran, has exited, and no file of that race exists at all.
    /// </summary>
    public class RaceResultIngestionService : IRaceResultIngestionService
    {
        /// <summary>
        /// How much earlier than its context a file without a context id may say it started: the Lua app's
        /// clock has whole seconds. A file that started before its race was even set up is from another race.
        /// </summary>
        private static readonly TimeSpan ContextClockSlack = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How long a file of a race no loaded save is waiting on stays in the inbox for its own save. Older than
        /// this (by the time it was written) it is taken for a leftover of a save that is gone, and quarantined.
        /// </summary>
        private static readonly TimeSpan LeftoverAge = TimeSpan.FromDays(30);

        private readonly IRaceResultValidator _validator;
        private readonly SessionDeduplicator _deduplicator;
        private readonly IRaceResultProcessor _processor;
        private readonly IRaceSessionRepository _sessionRepository;
        private readonly Func<GameState?> _currentGameState;
        private readonly IAppLogger _logger;

        // One ingestion at a time: the orphan pass on loading a save must not race the launcher's pass
        // over the same inbox
        private readonly SemaphoreSlim _gate = new(1, 1);

        // The race whose launch is under way (from BeginRaceAsync to EndRace). The orphan pass leaves the inbox
        // and the pending race alone meanwhile: the launcher's own pass settles that race, and a race that is
        // being prepared (AC not started yet) must never be taken for one that was walked away from.
        private volatile RaceContext? _raceInFlight;

        private readonly string _inboxPath;
        private readonly string _quarantinePath;
        private readonly string _archiveRoot;
        private readonly Func<bool?> _isAcRunning;

        /// <param name="currentGameState">The loaded game (App.CurrentGameState); null when none is loaded</param>
        public RaceResultIngestionService(
            IRaceResultValidator validator,
            SessionDeduplicator deduplicator,
            IRaceResultProcessor processor,
            IRaceSessionRepository sessionRepository,
            Func<GameState?> currentGameState)
            : this(validator, deduplicator, processor, sessionRepository, currentGameState,
                DefaultInboxPath, DefaultQuarantinePath, DefaultArchiveRoot)
        {
        }

        /// <param name="currentGameState">The loaded game (App.CurrentGameState); null when none is loaded</param>
        /// <param name="inboxPath">Where the Lua app writes its results</param>
        /// <param name="quarantinePath">Where files that are not applied go, with a sidecar saying why</param>
        /// <param name="archiveRoot">Where applied files go, one folder per player</param>
        /// <param name="isAcRunning">Whether AC runs (null: cannot tell); the real process check when null</param>
        public RaceResultIngestionService(
            IRaceResultValidator validator,
            SessionDeduplicator deduplicator,
            IRaceResultProcessor processor,
            IRaceSessionRepository sessionRepository,
            Func<GameState?> currentGameState,
            string inboxPath,
            string quarantinePath,
            string archiveRoot,
            Func<bool?>? isAcRunning = null)
        {
            _validator = validator;
            _deduplicator = deduplicator;
            _processor = processor;
            _sessionRepository = sessionRepository;
            _currentGameState = currentGameState;
            _inboxPath = inboxPath;
            _quarantinePath = quarantinePath;
            _archiveRoot = archiveRoot;
            _isAcRunning = isAcRunning ?? AcProcesses.QueryAnyRunning;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        /// <summary>
        /// The Lua app's output folder. It writes under CSP's ACDocuments folder, which is this same shell folder
        /// (it follows a Documents folder moved to OneDrive).
        /// </summary>
        public static string DefaultInboxPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa", "out", "sr_race_manager");

        /// <summary>Next to the inbox</summary>
        public static string DefaultQuarantinePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa", "out", "sr_race_manager_quarantine");

        /// <summary>%AppData%\StreetRodAC\RaceResults</summary>
        public static string DefaultArchiveRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetRodAC", "RaceResults");

        public async Task<IngestionResult> IngestResultsAsync(
            RaceContext? raceContext = null,
            IProgress<string>? progress = null)
        {
            await _gate.WaitAsync();
            try
            {
                return await IngestCoreAsync(raceContext, progress);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<IngestionResult> ProcessOrphanedResultsAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_raceInFlight is { } inFlight)
                {
                    _logger.Information("Race {ContextId} is being run: the orphan pass leaves it to the launcher", inFlight.ContextId);
                    return new IngestionResult();
                }

                _logger.Information("Processing orphaned race result files");
                var gameState = _currentGameState();
                var pendingBefore = gameState?.PendingRace;
                var result = await IngestCoreAsync(raceContext: null, progress: null);

                if (result.FilesProcessed > 0 && pendingBefore != null && gameState?.PendingRace == null)
                {
                    result.PlayerMessages.Insert(0, new PlayerMessage("Race Result",
                        "The result of the race you left running while the game was closed has come in, and it counts."));
                }

                if (gameState != null && pendingBefore != null)
                    await ResolveUnansweredPendingRaceAsync(gameState, result);

                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> BeginRaceAsync(RaceContext context, List<PlayerMessage> messages)
        {
            await _gate.WaitAsync();
            try
            {
                var gameState = _currentGameState();
                if (gameState == null || string.IsNullOrEmpty(gameState.SaveName))
                {
                    _logger.Warning("No game loaded - race {ContextId} runs without being marked pending", context.ContextId);
                    _raceInFlight = context;
                    return true;
                }

                // An earlier race of this save still waiting for its result is settled first: its file (if any) is
                // applied now, before this race's pass would quarantine it as another race's
                if (gameState.PendingRace is { } earlier && earlier.ContextId != context.ContextId)
                {
                    _logger.Information("Race {Earlier} is still pending: settling it before race {ContextId}", earlier.ContextId, context.ContextId);
                    var result = await IngestCoreAsync(raceContext: null, progress: null);
                    await ResolveUnansweredPendingRaceAsync(gameState, result);
                    messages.AddRange(result.PlayerMessages);

                    if (gameState.PendingRace?.ContextId == earlier.ContextId)
                    {
                        _logger.Warning("Race {Earlier} could not be settled yet: race {ContextId} does not start", earlier.ContextId, context.ContextId);
                        messages.Add(new PlayerMessage("Last Race Not Settled",
                            "The result of your last race has not been read yet (Assetto Corsa is still running, or its result file is busy).\n\n"
                            + "Try again in a moment: it has to count before the next race can start."));
                        return false;
                    }
                }

                _raceInFlight = context;
                try
                {
                    _processor.MarkRacePending(context, gameState);
                }
                catch (Exception ex)
                {
                    // The race still goes ahead: the pending race is in memory and is saved with the next save
                    _logger.Error(ex, "Could not save the pending race {ContextId} before the launch", context.ContextId);
                }

                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public void MarkRaceLaunched(RaceContext context)
        {
            context.LaunchedAt = DateTime.Now;
            var gameState = _currentGameState();
            if (gameState?.PendingRace?.ContextId != context.ContextId || string.IsNullOrEmpty(gameState.SaveName))
                return;

            // The pending race's copy in the save learns that AC did start: only a race that ran can be forfeited
            gameState.PendingRace.LaunchedAt = context.LaunchedAt;
            try
            {
                _processor.MarkRacePending(gameState.PendingRace, gameState);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save that race {ContextId} started", context.ContextId);
            }
        }

        public async Task ReleasePendingRaceAsync(RaceContext context)
        {
            await _gate.WaitAsync();
            try
            {
                var gameState = _currentGameState();
                if (gameState != null)
                    _processor.ReleasePendingRace(context, gameState);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void EndRace(RaceContext context)
        {
            if (ReferenceEquals(_raceInFlight, context) || _raceInFlight?.ContextId == context.ContextId)
                _raceInFlight = null;
        }

        public async Task<bool> ApplyNoResultForfeitAsync(RaceContext context, List<PlayerMessage>? messages = null)
        {
            await _gate.WaitAsync();
            try
            {
                var gameState = _currentGameState();
                if (gameState == null || string.IsNullOrEmpty(gameState.SaveName))
                {
                    _logger.Warning("No game loaded - the race without a result cannot be settled");
                    return false;
                }

                return await ForfeitCoreAsync(context, gameState, messages);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Settles a race that brought back nothing; under the gate. True when a forfeit was applied.</summary>
        private async Task<bool> ForfeitCoreAsync(RaceContext context, GameState gameState, List<PlayerMessage>? messages)
        {
            // The race may have been settled after all (its result applied, or a forfeit already recorded)
            if (await _sessionRepository.IsContextSettledAsync(gameState.SaveName, context.ContextId))
            {
                _logger.Information("Race {ContextId} is already settled - no forfeit", context.ContextId);
                _processor.ReleasePendingRace(context, gameState);
                return false;
            }

            if (context.CashWager <= 0 && !context.IsPinkSlip)
            {
                // Nothing at stake: the race simply did not happen, and is no longer waiting for a result
                _processor.ReleasePendingRace(context, gameState);
                return false;
            }

            var forfeitMessages = _processor.ApplyForfeit(context, gameState);
            messages?.AddRange(forfeitMessages);
            return true;
        }

        /// <summary>
        /// After a pass over the inbox for the save's pending race: when that race is still pending and nothing of it
        /// is left to try again, it is settled. A file of it that was quarantined makes it void (nothing lost); AC
        /// still running leaves it for after AC exits; a race that never started (the app died before AC did) is
        /// just released; one that ran and brought back nothing at all is forfeited (the app was killed during the
        /// race and AC was then quit). Under the gate. What the player is told goes to the result's
        /// <see cref="IngestionResult.PlayerMessages"/>.
        /// </summary>
        private async Task ResolveUnansweredPendingRaceAsync(GameState gameState, IngestionResult result)
        {
            var pending = gameState.PendingRace;
            if (pending == null || string.IsNullOrEmpty(gameState.SaveName)) return;

            if (result.RetryLater)
            {
                _logger.Warning("Race {ContextId}: a result file could not be read yet - it stays pending for the next pass", pending.ContextId);
                return;
            }

            if (result.FilesQuarantinedForContext > 0)
            {
                _logger.Warning("Race {ContextId}: its result could not be used and was quarantined - the race does not count", pending.ContextId);
                _processor.ReleasePendingRace(pending, gameState);
                result.PlayerMessages.Add(new PlayerMessage("Last Race",
                    "Your last race wrote a result the game could not use, so it does not count and nothing was lost. The file is kept for a look; the details are in the log."));
                return;
            }

            var acState = _isAcRunning();
            if (acState != false)
            {
                _logger.Information("Race {ContextId} is pending and Assetto Corsa {State}: it is settled once the game has closed",
                    pending.ContextId, acState == true ? "is running" : "may be running");
                result.WaitingForAssettoCorsa = true;
                return;
            }

            if (pending.LaunchedAt == null)
            {
                _logger.Warning("Race {ContextId} is pending but Assetto Corsa never started for it: it is released", pending.ContextId);
                _processor.ReleasePendingRace(pending, gameState);
                return;
            }

            _logger.Warning("Race {ContextId} ran ({LaunchedAt}) but no result of it exists, and Assetto Corsa is closed: settled as a race without a result",
                pending.ContextId, pending.LaunchedAt);
            if (await ForfeitCoreAsync(pending, gameState, result.PlayerMessages))
                result.ForfeitApplied = true;
        }

        private async Task<IngestionResult> IngestCoreAsync(RaceContext? raceContext, IProgress<string>? progress)
        {
            var result = new IngestionResult();
            var startTime = DateTime.Now;

            _logger.Information("=== RACE RESULT INGESTION STARTED ===");

            try
            {
                // Results belong to a save: with none loaded they wait in the inbox for one
                var gameState = _currentGameState();
                if (gameState == null || string.IsNullOrEmpty(gameState.SaveName))
                {
                    _logger.Warning("No game loaded - race results stay in the inbox until one is");
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }

                // The race just run, or else the one this save is waiting on
                var context = raceContext ?? gameState.PendingRace;

                // Step 1: Folder enumeration
                var inboxPath = GetInboxPath();
                if (!Directory.Exists(inboxPath))
                {
                    _logger.Information("Inbox directory does not exist: {Path}", inboxPath);
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }

                var files = Directory.GetFiles(inboxPath);
                result.FilesScanned = files.Length;
                _logger.Information("Scanned {Count} files in inbox: {Path}", files.Length, inboxPath);

                if (files.Length == 0)
                {
                    _logger.Information("No files to process");
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }

                // Step 2: First-pass filtering
                var candidates = files
                    .Select(f => new FileInfo(f))
                    .Where(f => _validator.IsValidFilename(f.Name))
                    .ToList();

                result.FilesIgnored = files.Length - candidates.Count;
                await QuarantineOrphanedTempFilesAsync(files, context, result);
                _logger.Information("{Count} candidate files after first-pass filtering ({Ignored} ignored)",
                    candidates.Count, result.FilesIgnored);

                // Steps 3-8: Process each candidate
                foreach (var file in candidates)
                {
                    progress?.Report($"Processing {file.Name}...");
                    await ProcessSingleFileAsync(file, context, gameState, result);
                }

                if (result.FilesDeferred > 0)
                    _logger.Warning("{Count} result file(s) could not be dealt with now and stay in the inbox for the next pass", result.FilesDeferred);

                result.Duration = DateTime.Now - startTime;
                _logger.Information("=== RACE RESULT INGESTION COMPLETED ===");
                _logger.Information("Summary: Scanned={Scanned}, Processed={Processed}, Duplicates={Duplicates}, Quarantined={Quarantined}, Ignored={Ignored}, Duration={Duration}s",
                    result.FilesScanned, result.FilesProcessed, result.FilesDuplicate,
                    result.FilesQuarantined, result.FilesIgnored, result.Duration.TotalSeconds);

                return result;
            }
            catch (Exception ex)
            {
                // The inbox could not be read: a result may well be in it, so this is never "no result"
                _logger.Error(ex, "Race result ingestion failed with exception");
                result.Errors.Add($"Critical error: {ex.Message}");
                result.InboxUnreadable = true;
                result.Duration = DateTime.Now - startTime;
                return result;
            }
        }

        /// <summary>
        /// Process a single candidate file through the pipeline
        /// </summary>
        private async Task ProcessSingleFileAsync(FileInfo file, RaceContext? context, GameState gameState, IngestionResult result)
        {
            try
            {
                _logger.Debug("Processing file: {FileName}", file.Name);

                // Step 3: Content validation
                var validation = await _validator.ValidateFileAsync(file.FullName);

                if (!validation.IsValid)
                {
                    await HandleValidationFailureAsync(file, validation, context, result);
                    return;
                }

                var raceResult = validation.ParsedResult!;
                _logger.Debug("File {FileName} validated successfully", file.Name);

                // Step 4: Deduplication check. A read failure leaves the file for the next pass rather than risk
                // applying it twice.
                bool alreadyProcessed;
                try
                {
                    alreadyProcessed = await _deduplicator.IsProcessedAsync(gameState.SaveName, raceResult.Session.SessionId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not check whether {FileName} was applied before - it stays for the next pass", file.Name);
                    result.Errors.Add($"Dedup check failed for {file.Name}: {ex.Message}");
                    result.FilesDeferred++;
                    return;
                }

                if (alreadyProcessed)
                {
                    _logger.Information("Session {SessionId} already processed - deleting duplicate file",
                        raceResult.Session.SessionId);

                    // Safe cleanup - delete duplicate
                    try
                    {
                        File.Delete(file.FullName);
                        result.FilesDuplicate++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Failed to delete duplicate file {FileName}: {Error}",
                            file.Name, ex.Message);
                    }

                    return;
                }

                // Step 4a: A file of another race that is still open somewhere (another save's, or this save's once
                // it is loaded again) is not this pass's to judge: it stays in the inbox for its own save
                if (await IsLeftForItsOwnSaveAsync(file, raceResult, context, gameState, result))
                    return;

                // Step 4b: Does the file belong to the race in hand, and is that race still open? (A settled-check
                // that cannot be read leaves the file for the next pass, as the dedup check does.)
                var mismatch = ContextMismatch(raceResult, context);
                bool settled;
                try
                {
                    settled = mismatch == null && await _sessionRepository.IsContextSettledAsync(gameState.SaveName, context!.ContextId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not check whether race {ContextId} was settled - {FileName} stays for the next pass", context?.ContextId, file.Name);
                    result.Errors.Add($"Settled check failed for {file.Name}: {ex.Message}");
                    result.FilesDeferred++;
                    return;
                }

                if (settled)
                    mismatch = $"race {context!.ContextId} already has its result";

                if (mismatch != null)
                {
                    _logger.Warning("File {FileName} is not applied: {Reason} - quarantining", file.Name, mismatch);
                    await QuarantineFileAsync(file, "Not applied", new[] { mismatch });
                    result.FilesQuarantined++;
                    return;
                }

                // The race's own file, with the wrong racers for it: it cannot settle the race, and is quarantined as
                // one that fails validation, for this race
                if (ParticipantMismatch(raceResult, context!) is { } wrongRacers)
                {
                    _logger.Warning("File {FileName} is not applied: {Reason} - quarantining", file.Name, wrongRacers);
                    await QuarantineFileAsync(file, "Wrong participants", new[] { wrongRacers });
                    result.FilesQuarantined++;
                    result.FilesQuarantinedForContext++;
                    return;
                }

                WarnOnRaceTypeMismatch(raceResult, context!, file);

                // Step 5: Processing, with the file still in the inbox. On the thread that owns the game state; the
                // processor changes nothing before everything it needs from the file has been worked out, and saves
                // the race with its session record in one transaction. Until that commits the file is where the next
                // pass looks for it: the app killed at any point before leaves the race to be applied again, never
                // forfeited for want of a file.
                try
                {
                    var messages = _processor.ProcessRaceResult(raceResult, context!, gameState);
                    result.PlayerMessages.AddRange(messages);
                    _logger.Information("Successfully processed session {SessionId}", raceResult.Session.SessionId);
                    result.FilesProcessed++;
                }
                catch (RaceNotSavedException ex)
                {
                    // The race is fine, the save was not: the game is as it was, and the file stays in the inbox
                    // for the next pass
                    _logger.Error(ex, "Session {SessionId} could not be saved - it is tried again on the next pass", raceResult.Session.SessionId);
                    result.Errors.Add($"Saving failed for {file.Name}: {ex.Message}");
                    result.FilesDeferred++;
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to process session {SessionId}", raceResult.Session.SessionId);
                    result.Errors.Add($"Processing failed for {file.Name}: {ex.Message}");

                    // Quarantine the file for investigation
                    await QuarantineFileAsync(file, "Processing failed", new[] { ex.Message });
                    result.FilesQuarantined++;
                    result.FilesQuarantinedForContext++;
                    return;
                }

                // Step 6: Into the archive, now that the save has it. A file that cannot move stays in the inbox,
                // where the next pass finds its session in the save and drops it as a duplicate.
                var archivePath = GetArchivePath(raceResult.Session.SessionId, gameState);
                try
                {
                    var archiveDir = Path.GetDirectoryName(archivePath);
                    if (!string.IsNullOrEmpty(archiveDir))
                        Directory.CreateDirectory(archiveDir);

                    File.Move(file.FullName, archivePath, overwrite: false);
                    _logger.Debug("Moved {FileName} to archive: {ArchivePath}", file.Name, archivePath);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Session {SessionId} is applied but {FileName} could not go to the archive ({Error}) - the next pass drops it as a duplicate",
                        raceResult.Session.SessionId, file.Name, ex.Message);
                }
            }
            catch (Exception ex)
            {
                // Wherever it failed, the file was not applied: it is taken as still waiting, never as "no result"
                _logger.Error(ex, "Unexpected error processing file {FileName}", file.Name);
                result.Errors.Add($"Unexpected error for {file.Name}: {ex.Message}");
                result.FilesDeferred++;
            }
        }

        /// <summary>
        /// True when the file has been dealt with here because it names a race other than the one in hand: left in
        /// the inbox for its own save (counted as ignored), or quarantined when this save has already settled that
        /// race or the file has waited longer than <see cref="LeftoverAge"/>. False for a file of the race in hand
        /// and one without a readable context id, which the usual checks judge.
        /// </summary>
        private async Task<bool> IsLeftForItsOwnSaveAsync(FileInfo file, RaceResultJson raceResult, RaceContext? context, GameState gameState, IngestionResult result)
        {
            if (!Guid.TryParse(raceResult.Session.ContextId, out var fileContextId) || fileContextId == context?.ContextId)
                return false;

            bool settledHere;
            try
            {
                settledHere = await _sessionRepository.IsContextSettledAsync(gameState.SaveName, fileContextId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not check whether race {ContextId} was settled - {FileName} stays for the next pass", fileContextId, file.Name);
                result.Errors.Add($"Settled check failed for {file.Name}: {ex.Message}");
                result.FilesDeferred++;
                return true;
            }

            string reason;
            if (settledHere)
            {
                reason = $"it belongs to race {fileContextId}, which this save has already settled";
            }
            else if (OlderThan(file, LeftoverAge))
            {
                reason = $"it belongs to race {fileContextId}, and no save has taken it in {LeftoverAge.TotalDays:0} days";
            }
            else
            {
                // Another save's race (or a race of a save that is not loaded): quarantining it would forfeit that
                // race when its save is loaded again
                _logger.Information("File {FileName} belongs to race {FileContextId}, not a race of this save - left in the inbox for its own save",
                    file.Name, fileContextId);
                result.FilesIgnored++;
                return true;
            }

            _logger.Warning("File {FileName} is not applied: {Reason} - quarantining", file.Name, reason);
            await QuarantineFileAsync(file, "Not applied", new[] { reason });
            result.FilesQuarantined++;
            return true;
        }

        /// <summary>Whether the file was last written longer ago than <paramref name="age"/>; false when that cannot be read</summary>
        private static bool OlderThan(FileInfo file, TimeSpan age)
        {
            try
            {
                file.Refresh();
                return file.LastWriteTimeUtc < DateTime.UtcNow - age;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The race mode writes <c>{session}.json.tmp</c> and renames it at once: one still there was cut short (AC
        /// killed in between, or the rename failed). It is quarantined with a warning, and counts as the race in
        /// hand's when it was written after that race was set up. Only once AC has exited: while it may run, the
        /// file may be being written right now.
        /// </summary>
        private async Task QuarantineOrphanedTempFilesAsync(string[] files, RaceContext? context, IngestionResult result)
        {
            var temps = files.Where(f => f.EndsWith(".json.tmp", StringComparison.OrdinalIgnoreCase)).ToList();
            if (temps.Count == 0)
                return;

            if (_isAcRunning() != false)
            {
                _logger.Information("{Count} unfinished result file(s) in the inbox, and Assetto Corsa may be running: left alone", temps.Count);
                return;
            }

            foreach (var path in temps)
            {
                var file = new FileInfo(path);
                _logger.Warning("File {FileName} is a race result the race mode never finished writing - quarantining", file.Name);
                var writtenForContext = context != null && WrittenSince(file, context);
                await QuarantineFileAsync(file, "Unfinished result",
                    new[] { "The race mode wrote this file but never renamed it to .json: Assetto Corsa was killed in between, or the rename failed" });
                result.FilesIgnored--;
                result.FilesQuarantined++;
                if (writtenForContext) result.FilesQuarantinedForContext++;
            }
        }

        /// <summary>
        /// The file's race_type is the race the mode ran; the context's is the one the career set up. They should
        /// agree. The context decides, so a mismatch is only logged.
        /// </summary>
        private void WarnOnRaceTypeMismatch(RaceResultJson raceResult, RaceContext context, FileInfo file)
        {
            var fileType = raceResult.Session.RaceType;
            if (string.IsNullOrWhiteSpace(fileType))
                return;

            var expected = context.IsTestAndTune ? RaceTypes.TestAndTune : context.RaceType == RaceType.DragRace ? RaceTypes.Drag : RaceTypes.Road;
            if (!string.Equals(fileType, expected, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning("File {FileName} says the race was {FileRaceType}, but race {ContextId} is a {RaceType} ({Expected}) - applied as the race was set up",
                    file.Name, fileType, context.ContextId, context.RaceType, expected);
            }
        }

        /// <summary>
        /// Why the file does not fit the race the career set up, by its racers; null when it does. The validator only
        /// holds the file to its own race_type (a TUNE file has the player alone): a race's context takes the player
        /// and one rival, whatever the file says it ran.
        /// </summary>
        private static string? ParticipantMismatch(RaceResultJson raceResult, RaceContext context)
        {
            var expected = context.IsTestAndTune ? 1 : 2;
            var count = raceResult.Participants.Count;
            return count == expected
                ? null
                : $"race {context.ContextId} is {(context.IsTestAndTune ? "a test-and-tune, the player alone" : "a race of two")}, but the file has {count} participant(s)";
        }

        /// <summary>
        /// Why the file cannot be applied with <paramref name="context"/>; null when it can. A file names its
        /// race by context_id. One without (an older Lua app, or a race.ini without the id) is taken for the
        /// race in hand unless it started before that race was set up.
        /// </summary>
        private static string? ContextMismatch(RaceResultJson raceResult, RaceContext? context)
        {
            if (context == null)
                return "no race is waiting for a result in this save";

            var fileContext = raceResult.Session.ContextId;
            if (!string.IsNullOrWhiteSpace(fileContext))
            {
                if (!Guid.TryParse(fileContext, out var fileContextId))
                    return $"its context_id '{fileContext}' is not a GUID";

                return fileContextId == context.ContextId
                    ? null
                    : $"it belongs to race {fileContextId}, not to race {context.ContextId}";
            }

            if (RaceResultValidator.TryParseTimestamp(raceResult.Session.StartTimestamp, out var startedAt)
                && startedAt.ToUniversalTime() < context.CreatedAt.ToUniversalTime() - ContextClockSlack)
            {
                return $"it has no context_id and started ({startedAt:u}) before race {context.ContextId} was set up";
            }

            return null;
        }

        /// <summary>Whether the file was last written after the race was set up (with the Lua clock's slack)</summary>
        private static bool WrittenSince(FileInfo file, RaceContext context)
        {
            try
            {
                file.Refresh();
                return file.LastWriteTimeUtc >= context.CreatedAt.ToUniversalTime() - ContextClockSlack;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Handle validation failure according to failure reason
        /// </summary>
        private async Task HandleValidationFailureAsync(FileInfo file, ValidationResult validation, RaceContext? context, IngestionResult result)
        {
            var reason = validation.FailureReason;

            _logger.Warning("File {FileName} failed validation: {Reason} - {Errors}",
                file.Name, reason, string.Join(", ", validation.Errors));

            // Determine action based on failure reason
            switch (reason)
            {
                case ValidationFailureReason.InvalidFileName:
                case ValidationFailureReason.ZeroSize:
                case ValidationFailureReason.WrongSource:
                    // Completely ignore - not our file
                    _logger.Debug("Ignoring file {FileName} (not a Street Rod result file)", file.Name);
                    result.FilesIgnored++;
                    break;

                case ValidationFailureReason.FileUnreadable:
                    // Skip and retry later (file may be locked): a result is there, it is just not readable yet
                    _logger.Warning("File {FileName} is unreadable - leaving for retry", file.Name);
                    result.FilesDeferred++;
                    break;

                case ValidationFailureReason.InvalidJson:
                case ValidationFailureReason.MissingSchemaVersion:
                case ValidationFailureReason.MissingSessionId:
                case ValidationFailureReason.MissingStartTimestamp:
                case ValidationFailureReason.InvalidTimestamp:
                case ValidationFailureReason.InvalidParticipantCount:
                case ValidationFailureReason.InvalidParticipantData:
                case ValidationFailureReason.TooLarge:
                    // Quarantine - looks like our file but invalid. Which race it was cannot be read from it; one
                    // written after the race in hand was set up is taken to be that race's.
                    _logger.Warning("File {FileName} failed content validation - quarantining", file.Name);
                    var writtenForContext = context != null && WrittenSince(file, context);
                    await QuarantineFileAsync(file, validation);
                    result.FilesQuarantined++;
                    if (writtenForContext) result.FilesQuarantinedForContext++;
                    break;

                default:
                    // Unknown reason - ignore
                    _logger.Warning("File {FileName} failed validation with unknown reason - ignoring", file.Name);
                    result.FilesIgnored++;
                    break;
            }
        }

        /// <summary>
        /// Move a file to quarantine with error sidecar
        /// </summary>
        private Task QuarantineFileAsync(FileInfo file, ValidationResult validation) =>
            QuarantineFileAsync(file, $"Validation failed: {validation.FailureReason}", validation.Errors);

        /// <summary>
        /// Move a file to quarantine with an error sidecar saying why
        /// </summary>
        private async Task QuarantineFileAsync(FileInfo file, string headline, IEnumerable<string> errors)
        {
            try
            {
                var quarantinePath = GetQuarantinePath();
                Directory.CreateDirectory(quarantinePath);

                var quarantineFilePath = Path.Combine(quarantinePath, file.Name);
                var errorFilePath = quarantineFilePath + ".error.txt";

                // Move file to quarantine
                if (file.Exists)
                {
                    File.Move(file.FullName, quarantineFilePath, overwrite: true);
                }

                // Create error sidecar file
                var errorMessage = $"{headline}\n" +
                                   $"Errors:\n{string.Join("\n", errors)}\n" +
                                   $"Quarantined at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                await File.WriteAllTextAsync(errorFilePath, errorMessage);

                _logger.Debug("Quarantined file {FileName} with error sidecar", file.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to quarantine file {FileName}", file.Name);
            }
        }

        /// <summary>The inbox path (Lua app output folder)</summary>
        private string GetInboxPath() => _inboxPath;

        /// <summary>
        /// Get the archive path for a processed session
        /// Organized by player name: %AppData%\StreetRodAC\RaceResults\{PlayerName}\{session_id}.json
        /// </summary>
        private string GetArchivePath(string sessionId, GameState gameState)
        {
            // The same sanitizing as every other name that becomes a folder
            var safePlayerName = PathNames.Sanitize(gameState.Player?.Name, fallback: "Unknown");

            return Path.Combine(_archiveRoot, safePlayerName, $"{sessionId}.json");
        }

        /// <summary>
        /// Get the quarantine folder path
        /// </summary>
        private string GetQuarantinePath() => _quarantinePath;
    }
}
