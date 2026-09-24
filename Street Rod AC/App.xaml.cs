using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.Init;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Configuration;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Scheduler;
using Street_Rod_AC.Services.Scheduler.Tasks;
using Street_Rod_AC.Services.Settings;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.Services.Talk;
using Street_Rod_AC.Services.Career;

namespace Street_Rod_AC
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public IAssettoCorsaContentService ContentService { get; private set; }
        public IAssettoCorsaLauncher Launcher { get; private set; }
        public NavigationService NavigationService { get; private set; }
        public DialogService DialogService { get; private set; }
        public IGameStateRepository GameStateRepository { get; private set; }
        public IContentCatalogRepository CatalogRepository { get; private set; }
        public ICarImportService CarImportService { get; private set; }
        public ICarProfileRepository ProfileRepository { get; private set; }
        public ICarProfileService ProfileService { get; private set; }
        public IUsedCarMarketService MarketService { get; private set; }
        public Services.Dealers.IDealerCatalog DealerCatalog { get; private set; }
        public ICarPurchaseService PurchaseService { get; private set; }
        public Services.Parts.ICarPartsService CarPartsService { get; private set; }
        public Services.Parts.IPartsShopService PartsShopService { get; private set; }
        public IIniModificationService IniModificationService { get; private set; }
        public Services.Race.IRaceResultIngestionService RaceResultIngestionService { get; private set; }
        public CarDataOverlay CarDataOverlay { get; private set; }
        public Services.Race.RaceCarDataService RaceCarDataService { get; private set; }
        public IOpponentRepository OpponentRepository { get; private set; }
        public IOpponentInitializationService OpponentInitializationService { get; private set; }
        public IOpponentChallengeService OpponentChallengeService { get; private set; }
        public IGameTimeScheduler Scheduler { get; private set; }
        public IGameTimeService GameTimeService { get; private set; }
        public GameSettingsService GameSettingsService { get; private set; }

        // Career services
        public ICarFilterService CarFilterService { get; private set; }
        public IMilestoneService MilestoneService { get; private set; }
        public IVictoryConditionService VictoryConditionService { get; private set; }
        public IRaceEventService RaceEventService { get; private set; }
        public IEventOpponentService EventOpponentService { get; private set; }
        public ICareerProgressService CareerProgressService { get; private set; }
        public ITalkService TalkService { get; private set; }

        private Models.GameState.GameState? _currentGameState;

        /// <summary>
        /// Current game state (set when a game is loaded or created). A newly set game gets the results of a race it
        /// ran but never heard back from (the app closed during it): only now is there a save to apply them to.
        /// </summary>
        public Models.GameState.GameState? CurrentGameState
        {
            get => _currentGameState;
            set
            {
                var loaded = value != null && !ReferenceEquals(value, _currentGameState);
                _currentGameState = value;
                if (loaded) _ = ProcessOrphanedResultsAsync();
            }
        }

        // The crash path runs once, whichever handler gets there first
        private int _fatalPathRan;

        /// <summary>How long the crash and exit paths wait for the UI thread or the install's gate before going without</summary>
        private static readonly TimeSpan FatalPathWait = TimeSpan.FromSeconds(5);

        // A wait for AC to exit (to put the install back and settle a pending race) is under way
        private int _waitingForAcExit;

        // Recoverable UI exceptions lately: a handler failing over and over (a render callback) is not recoverable
        private readonly Queue<DateTime> _recentUiErrors = new();
        private bool _closeAfterRace;

        /// <summary>
        /// Spends game time for an action. Handles day transitions and scheduled tasks.
        /// </summary>
        public async Task<TimeSpendResult> SpendTimeAsync(GameAction action)
        {
            if (CurrentGameState == null)
                return new TimeSpendResult { NewDayStarted = false, DaysPassed = 0 };

            return await GameTimeService.SpendTimeAsync(CurrentGameState, action);
        }

        /// <summary>
        /// Spends a custom amount of game time in minutes.
        /// </summary>
        public async Task<TimeSpendResult> SpendTimeAsync(int minutes)
        {
            if (CurrentGameState == null)
                return new TimeSpendResult { NewDayStarted = false, DaysPassed = 0 };

            return await GameTimeService.SpendTimeAsync(CurrentGameState, minutes);
        }

        /// <summary>
        /// Ends the current game day and advances to next morning.
        /// </summary>
        public async Task<TimeSpendResult> EndDayAsync()
        {
            if (CurrentGameState == null)
                return new TimeSpendResult { NewDayStarted = false, DaysPassed = 0 };

            return await GameTimeService.EndDayAsync(CurrentGameState);
        }

        public App()
        {
            // Force English culture for the entire application
            var englishCulture = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentCulture = englishCulture;
            CultureInfo.DefaultThreadCurrentUICulture = englishCulture;
            Thread.CurrentThread.CurrentCulture = englishCulture;
            Thread.CurrentThread.CurrentUICulture = englishCulture;

            // Set WPF language for XAML bindings (dates, numbers, etc.)
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(englishCulture.IetfLanguageTag)));

            // Initialize logging FIRST
            AppLoggerFactory.Initialize();

            // Then the last line of defence: an exception nobody catches (14 of the async void handlers let them
            // through) must not end the app with the AC install still changed for a race and the game unsaved
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            InitializeComponent();

            // The settings before anything that reads the install: they name the AC folder
            GameSettingsService = new GameSettingsService();

            ContentService = new AssettoCorsaContentService();
            IniModificationService = new IniModificationService();
            DialogService = new DialogService();
            CatalogRepository = new ContentCatalogRepository();
            CarImportService = new CarImportService(CatalogRepository);
            ProfileRepository = new CarProfileRepository();
            ProfileService = new CarProfileService(CatalogRepository, ProfileRepository);
            CarPartsService = new Services.Parts.CarPartsService(CatalogRepository, ProfileRepository);
            PartsShopService = new Services.Parts.PartsShopService(CarPartsService);
            DealerCatalog = new Services.Dealers.DealerCatalog();
            MarketService = new UsedCarMarketService(CatalogRepository, ProfileRepository, CarPartsService, DealerCatalog);

            // Scheduler and Time Service
            Scheduler = new GameTimeScheduler();
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.RaceSimulatorTask(new Services.Simulation.RaceSimulatorService(MarketService)));
            // Note: EventGenerationTask, and after it the market and parts ads tasks, are registered once RaceEventService is created
            GameTimeService = new GameTimeService(Scheduler);

            // Opponent services (must be initialized before GameStateRepository)
            OpponentRepository = new OpponentRepository();
            OpponentInitializationService = new OpponentInitializationService(OpponentRepository, CatalogRepository, ProfileRepository);
            OpponentChallengeService = new OpponentChallengeService(CatalogRepository, ProfileRepository, CarPartsService);

            // Game state repository (depends on opponent initialization service). The save in use stays open in
            // one place, shared with the race sessions, so a race's result and the state it changes are one write
            var saveDatabase = new Services.Storage.SaveDatabase();
            GameStateRepository = new GameStateRepository(OpponentInitializationService, saveDatabase);
            PurchaseService = new CarPurchaseService(GameStateRepository, CarPartsService, GameTimeService);

            // Career services
            CarFilterService = new CarFilterService(car => MarketService.ValueOf(car));
            MilestoneService = new MilestoneService();
            VictoryConditionService = new VictoryConditionService(
                getTotalOpponents: gs => gs.Racers.TotalCount,
                playerHasMostWins: gs => IsPlayerSeasonChampion(gs)
            );
            RaceEventService = new RaceEventService(
                CarFilterService,
                carDefId => CatalogRepository.GetCar(carDefId)
            );
            EventOpponentService = new EventOpponentService(CarFilterService, CatalogRepository);
            CareerProgressService = new CareerProgressService(MilestoneService, VictoryConditionService);

            // Talk service for opponent dialogue
            TalkService = new StaticTalkService();

            // Register event generation task (must be after RaceEventService is created)
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.EventGenerationTask(RaceEventService));

            // Last: these two put engines together on a worker thread and hand the day back before they are
            // done. What comes before them is finished by the time whoever spent the time carries on.
            Scheduler.RegisterTask(new MarketRefreshTask(MarketService));
            Scheduler.RegisterTask(new PartsAdsRefreshTask(PartsShopService));

            // Race result services
            var raceResultValidator = new Services.Race.Validation.RaceResultValidator();
            var sessionRepository = new Services.Race.RaceSessionRepository(saveDatabase);
            var sessionDeduplicator = new Services.Race.Validation.SessionDeduplicator(sessionRepository);
            var raceResultProcessor = new Services.Race.RaceResultProcessor(GameStateRepository, sessionRepository, CareerProgressService, RaceEventService, CarPartsService);
            RaceResultIngestionService = new Services.Race.RaceResultIngestionService(
                raceResultValidator,
                sessionDeduplicator,
                raceResultProcessor,
                sessionRepository,
                () => CurrentGameState);

            // The cars' data for a race: what their parts make of them, put into the install and taken out again
            CarDataOverlay = new CarDataOverlay();
            RaceCarDataService = new Services.Race.RaceCarDataService(CarPartsService, CarDataOverlay);

            // Pass race result service to launcher
            Launcher = new AssettoCorsaLauncher(IniModificationService, RaceResultIngestionService, CarDataOverlay);

            // A game that outlived its launch (or the app) has closed: its race can be settled now
            Launcher.AssettoCorsaExited += OnAssettoCorsaExited;

            // NavigationService (must be created after all its dependencies)
            NavigationService = new NavigationService(
                DialogService, CatalogRepository, ProfileRepository, Launcher, ContentService, OpponentChallengeService,
                GameStateRepository, MarketService, GameSettingsService, ProfileService, TalkService, DealerCatalog,
                PurchaseService,
                GameTimeService, CarPartsService, PartsShopService, RaceCarDataService, RaceEventService, CarFilterService,
                EventOpponentService, VictoryConditionService, MilestoneService,
                game => CurrentGameState = game);
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
            logger.Information("Application starting");

            // async void: whatever throws in here would otherwise end the app with nothing in the log
            try
            {
                await StartAsync(logger);
            }
            catch (Exception ex)
            {
                logger.Critical(ex, "Start-up failed");
                try
                {
                    System.Windows.MessageBox.Show($"Street Rod AC could not start:\n{ex.Message}\n\nThe details are in the log.",
                        "Street Rod AC", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception)
                {
                    // No window to show it in either; the log has it
                }

                Shutdown();
            }
        }

        private async Task StartAsync(IAppLogger logger)
        {
            // Validate AC installation
            if (!AppSettings.Instance.IsValidInstallation())
            {
                logger.Error("Assetto Corsa installation not found at path: {ACPath}",
                    AppSettings.Instance.AssettoCorsaPath);

                var errorDialog = new Dialogs.Information.InformationDialogViewModel(
                    DialogService,
                    $"Assetto Corsa installation not found at:\n{AppSettings.Instance.AssettoCorsaPath}\n\n" +
                    "Please verify the installation path in the configuration.",
                    "Installation Not Found");
                DialogService.ShowDialog(errorDialog);
                Shutdown();
                return;
            }

            // A race that ended badly (a crash, the power) may have left cars with changed data and race.ini
            // rewritten. Not while the game still runs, though (the app was restarted during a race): it is reading
            // that data, so it goes back once the game has closed.
            var acRunning = AcProcesses.AnyRunning();
            var acStillRunning = acRunning && HasLeftoversToRestore(logger);
            if (acStillRunning)
            {
                logger.Warning("Assetto Corsa is running: what an earlier race changed in it goes back once it closes");
                StartWaitForAcExit(logger);
            }
            else if (acRunning)
            {
                logger.Information("Assetto Corsa is running, and nothing of an earlier race is waiting to go back");
            }
            else
            {
                RestoreInstall(logger);
            }

            logger.Information("Assetto Corsa installation validated at {ACPath}",
                AppSettings.Instance.AssettoCorsaPath);

            // Import AC content into catalog at startup
            try
            {
                logger.Information("Starting car import");

                var result = await CarImportService.ImportCarsAsync();

                logger.Information("Car import completed. TotalFound: {TotalFound}, Imported: {Imported}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}, Duration: {Duration}s",
                    result.TotalFound, result.Imported, result.Updated, result.Skipped, result.Failed, result.Duration.TotalSeconds);

                if (result.Errors.Any())
                {
                    logger.Warning("Car import completed with {ErrorCount} errors", result.Errors.Count);
                    foreach (var error in result.Errors.Take(10))
                    {
                        logger.Warning("Import error: {Error}", error);
                    }
                }

                // Generate car profiles for all imported cars
                logger.Information("Ensuring car profiles exist");
                await ProfileService.EnsureProfilesExistAsync();
                logger.Information("Car profiles ready. Total profiles: {ProfileCount}",
                    ProfileRepository.GetProfileCount());

                // Parts catalog and factory engines load in the background; nothing needs them before the garage
                _ = CarPartsService.WarmUpAsync();

                // Load track content
                logger.Information("Loading track content");
                var tracks = await ContentService.LoadTracksAsync();
                logger.Information("Loaded {TrackCount} tracks with {ConfigCount} total configurations",
                    tracks.Count,
                    tracks.Sum(t => t.Configurations.Count));
            }
            catch (Exception ex)
            {
                logger.Critical(ex, "Critical error during content import");

                var errorDialog = new Dialogs.Information.InformationDialogViewModel(
                    DialogService,
                    $"Error importing Assetto Corsa content:\n{ex.Message}",
                    "Import Error");
                DialogService.ShowDialog(errorDialog);
                Shutdown();
                return;
            }

            // Orphaned race results are processed once a game is loaded (see CurrentGameState): a result can only
            // be applied to the save whose race it is

            // Navigate to initial screen
            logger.Information("Navigating to initial screen");
            NavigationService.NavigateToInit();

            // Closing the window during a race asks first: the race is stopped and the install put back before the app goes
            if (MainWindow != null) MainWindow.Closing += OnMainWindowClosing;

            if (acStillRunning)
            {
                DialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    DialogService,
                    "Assetto Corsa is still running from an earlier session.\n\n" +
                    "The cars' data and race settings it was started with go back once it closes; races can start after that.",
                    "Assetto Corsa Is Running"));
            }
        }

        // ===== THE AC INSTALL: PUT BACK =====

        /// <summary>
        /// Whether an earlier race left anything in the install to put back. A probe that fails (the restore folder
        /// unreadable) counts as yes: the restore is then deferred until AC has closed, never skipped, and start-up
        /// goes on.
        /// </summary>
        private bool HasLeftoversToRestore(IAppLogger logger)
        {
            try
            {
                return CarDataOverlay.Applied.Count > 0 || IniModificationService.HasPendingRestore;
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Could not look for what an earlier race left changed; it is put back once Assetto Corsa closes");
                return true;
            }
        }

        /// <summary>
        /// Car data, race copies and cfg INI files back as they were before a race. Each part on its own (one that
        /// fails does not stop the other), logged, never throws. Only call it when no AC process runs.
        /// </summary>
        private void RestoreInstall(IAppLogger? logger)
        {
            try
            {
                var restored = CarDataOverlay?.RestoreAll() ?? 0;
                if (restored > 0) logger?.Warning("Data of {Count} car(s) put back from an earlier race", restored);
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Could not put the cars' data back");
            }

            try
            {
                var restored = IniModificationService?.RestoreAll() ?? 0;
                if (restored > 0) logger?.Warning("{Count} AC cfg file(s) put back from an earlier race", restored);
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Could not put the AC cfg files back");
            }
        }

        /// <summary>
        /// Starts the wait for AC to exit (the install goes back after it, and a pending race is settled on
        /// <see cref="IAssettoCorsaLauncher.AssettoCorsaExited"/>), unless one is already under way
        /// </summary>
        private void StartWaitForAcExit(IAppLogger logger)
        {
            if (Interlocked.Exchange(ref _waitingForAcExit, 1) == 1) return;
            _ = RestoreWhenAcExitsAsync(logger);
        }

        /// <summary>The restore of an earlier race, once a game that was still running has closed; logged, never throws</summary>
        private async Task RestoreWhenAcExitsAsync(IAppLogger logger)
        {
            try
            {
                var restored = await Launcher.RestoreWhenAcExitsAsync();
                logger.Information("Assetto Corsa closed: {Count} car(s) and file(s) put back from an earlier race", restored);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not put back what an earlier race changed once Assetto Corsa closed");
            }
            finally
            {
                Interlocked.Exchange(ref _waitingForAcExit, 0);
            }
        }

        /// <summary>
        /// AC has closed after a wait for it (a race that outlived its launch, or one the app was restarted during):
        /// the loaded game's pending race can be settled now. On the UI thread, which owns the game state.
        /// </summary>
        private void OnAssettoCorsaExited(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_currentGameState?.PendingRace != null) _ = ProcessOrphanedResultsAsync();
                });
            }
            catch (Exception ex)
            {
                AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion).Warning(ex, "Could not settle the pending race after Assetto Corsa closed");
            }
        }

        /// <summary>True when the race or showroom runs, the one the launcher started or any other; never throws</summary>
        private bool IsAssettoCorsaRunning()
        {
            try
            {
                if (Launcher?.IsAssettoCorsaRunning == true) return true;
            }
            catch (Exception)
            {
                return true;
            }

            return AcProcesses.AnyRunning();
        }

        /// <summary>
        /// Orphaned race results for the game just loaded (or once AC has closed); logged, never throws. What came of
        /// it is told to the player through the dialog queue, over whatever screen they are on, and a garage that was
        /// opened before the pass changed the game is opened again on the new state. A pending race whose game is
        /// still running is settled once it closes.
        /// </summary>
        private async Task ProcessOrphanedResultsAsync()
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
            try
            {
                var state = _currentGameState;
                logger.Information("Processing orphaned race result files for {SaveName}", state?.SaveName);
                var orphanedResult = await RaceResultIngestionService.ProcessOrphanedResultsAsync();
                logger.Information("Orphaned results processed: {Processed} files, {Deferred} left for later, forfeit {Forfeit}, {Errors} errors",
                    orphanedResult.FilesProcessed, orphanedResult.FilesDeferred, orphanedResult.ForfeitApplied, orphanedResult.Errors.Count);

                if (orphanedResult.WaitingForAssettoCorsa)
                    StartWaitForAcExit(logger);

                // The game may have been switched meanwhile: messages about another save are not shown over it
                if (state == null || !ReferenceEquals(state, _currentGameState)) return;

                var changed = orphanedResult.FilesProcessed > 0 || orphanedResult.ForfeitApplied;
                if (changed && NavigationService.CurrentScreen is Screens.Garage.GarageScreenViewModel)
                    NavigationService.NavigateToGarage(state, skipAnimation: true);

                foreach (var message in orphanedResult.PlayerMessages)
                {
                    DialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(DialogService, message.Text, message.Title));
                }
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to process orphaned race results");
            }
        }

        /// <summary>
        /// The window closes during a race only after asking: yes stops the race (the launcher kills AC, puts the
        /// install back in its finally) and the app closes once that is done
        /// </summary>
        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Only a launch under way asks. A wait for an earlier game to exit is not one: the app just goes, the game
            // is left running, and the next start puts the install back and settles the race.
            if (_closeAfterRace || Launcher == null || !Launcher.IsLaunchInProgress) return;

            e.Cancel = true;
            DialogService.ShowDialog(new Dialogs.Confirmation.ConfirmationDialogViewModel(
                DialogService,
                "Assetto Corsa is still running.\n\nStop it and quit? A race stopped before the finish counts as a loss when something is at stake.",
                "Quit During a Race",
                stop =>
                {
                    if (!stop) return;
                    _closeAfterRace = true;
                    _ = StopRaceAndCloseAsync();
                }), jumpQueue: true);
        }

        private async Task StopRaceAndCloseAsync()
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.App);
            try
            {
                Launcher.CancelRace();

                // The launcher's finally puts the install back; the app waits for it (it gives up on a game that
                // will not die after a while, and the next start puts things back then)
                var waited = TimeSpan.Zero;
                while (Launcher.IsLaunchInProgress && waited < TimeSpan.FromSeconds(30))
                {
                    await Task.Delay(250);
                    waited += TimeSpan.FromMilliseconds(250);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Stopping the race on the way out failed");
            }

            Shutdown();
        }

        // ===== EXCEPTIONS NOBODY CAUGHT =====

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.App);
            logger.Critical(e.Exception, "Unhandled exception on the UI thread");

            if (IsRecoverable(e.Exception))
            {
                e.Handled = true;
                try
                {
                    // One dialog at a time: an open one belongs to a flow that is waiting on it, and the log has this
                    if (DialogService != null && !DialogService.IsDialogOpen)
                    {
                        DialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                            DialogService,
                            $"Something went wrong:\n{e.Exception.Message}\n\nThe game carries on; the details are in the log.",
                            "Error"));
                    }
                }
                catch (Exception dialogError)
                {
                    logger.Error(dialogError, "Could not show the error dialog");
                }

                return;
            }

            // Not recoverable: the app ends. What must happen before it does happens now.
            RunFatalPath(e.Exception);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                AppLoggerFactory.CreateLogger(LogCategory.App)
                    .Critical(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
            }
            catch (Exception)
            {
                // Logging is not there (a crash before it started); the fatal path still runs
            }

            RunFatalPath(e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                AppLoggerFactory.CreateLogger(LogCategory.App).Error(e.Exception, "A background task failed and nobody awaited it");
            }
            catch (Exception)
            {
                // Nothing more to do about it
            }

            e.SetObserved();
        }

        /// <summary>
        /// Whether the app can carry on after an exception on the UI thread. Not after the runtime is in trouble
        /// (memory, corrupt state, a type that failed to load), and not when handlers fail over and over (the same
        /// error from a render or timer callback: the dialog would come back forever).
        /// </summary>
        private bool IsRecoverable(Exception exception)
        {
            var root = exception;
            while (root is System.Reflection.TargetInvocationException { InnerException: { } inner }) root = inner;

            if (root is OutOfMemoryException or InsufficientExecutionStackException or AccessViolationException
                or System.Runtime.InteropServices.SEHException or InvalidProgramException or BadImageFormatException
                or TypeInitializationException or TypeLoadException)
                return false;

            var now = DateTime.UtcNow;
            _recentUiErrors.Enqueue(now);
            while (_recentUiErrors.Count > 0 && now - _recentUiErrors.Peek() > TimeSpan.FromSeconds(30)) _recentUiErrors.Dequeue();
            return _recentUiErrors.Count <= 10;
        }

        /// <summary>
        /// What must happen before the process dies: the game saved, the AC install put back (only when no game
        /// runs), FMOD released, the save's database closed, the log flushed. Once, whichever handler gets here
        /// first; every step on its own, and it never throws.
        /// </summary>
        private void RunFatalPath(Exception? exception)
        {
            if (Interlocked.Exchange(ref _fatalPathRan, 1) == 1) return;

            IAppLogger? logger = null;
            try
            {
                logger = AppLoggerFactory.CreateLogger(LogCategory.App);
                logger.Critical(exception ?? new Exception("(no exception object)"), "Fatal error: saving the game and putting Assetto Corsa back before the app ends");
            }
            catch (Exception)
            {
                // Logging never started: the steps still run
            }

            ShutDownCleanly(logger);
        }

        /// <summary>
        /// The way out, normal or not: save, restore, FMOD, the database, the log. Every step guarded.
        /// </summary>
        private void ShutDownCleanly(IAppLogger? logger)
        {
            // Save current game state if one is loaded. On the UI thread, which owns the state: the crash path can
            // run on any thread (a pool thread, an FMOD callback) while the UI thread is changing the game. A UI
            // thread that does not get to it in time (stuck, or it is what crashed) means no save rather than a hang.
            var state = _currentGameState;
            if (state != null && !string.IsNullOrEmpty(state.SaveName) && GameStateRepository != null)
            {
                try
                {
                    logger?.Information("Saving game state on exit: {SaveName}", state.SaveName);
                    if (OnUiThread(() => GameStateRepository.Save(state, state.SaveName), logger))
                        logger?.Information("Game state saved successfully");
                    else
                        logger?.Error("The game could not be saved on exit: the UI thread did not answer within {Seconds}s", FatalPathWait.TotalSeconds);
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, "Failed to save game state on exit");
                }
            }

            // The install goes back unless the game still runs on it; then the next start does it. Under the install's
            // gate, so it never falls in the middle of a launch preparing its changes; a gate that is not free in
            // time leaves the restore to the next start rather than hang the way out.
            try
            {
                if (IsAssettoCorsaRunning())
                {
                    logger?.Warning("Assetto Corsa is still running: what the race changed in it goes back at the next start");
                }
                else if (!AcInstallGate.TryRun(FatalPathWait, () => RestoreInstall(logger)))
                {
                    logger?.Warning("A launch was changing the install: what it changed goes back at the next start");
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Could not put Assetto Corsa back on exit");
            }

            // FMOD's threads call back into this assembly: they stop before the runtime goes
            try
            {
                Audio.FmodLifetime.Shutdown();
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "FMOD did not shut down cleanly");
            }

            // After the final save: the save's database closes
            try
            {
                (GameStateRepository as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "The save's database did not close cleanly");
            }

            AppLoggerFactory.Shutdown();
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread: directly when already on it (or when the dispatcher is
        /// gone), else through the dispatcher with a time limit. False when it did not run in time. Exceptions of
        /// the action come through.
        /// </summary>
        private bool OnUiThread(Action action, IAppLogger? logger)
        {
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess() || dispatcher.HasShutdownStarted)
            {
                action();
                return true;
            }

            var ran = false;
            try
            {
                dispatcher.Invoke(() => { action(); ran = true; }, DispatcherPriority.Send, CancellationToken.None, FatalPathWait);
            }
            catch (TimeoutException)
            {
                logger?.Warning("The UI thread did not answer within {Seconds}s", FatalPathWait.TotalSeconds);
            }

            return ran;
        }

        /// <summary>
        /// Check if the player has the most wins for Season Champion victory
        /// </summary>
        private static bool IsPlayerSeasonChampion(Models.GameState.GameState gameState)
        {
            var playerWins = gameState.Player.Stats.Wins;

            // Check all opponents
            foreach (var racer in gameState.Racers.ReadyToRace.Values)
            {
                if (racer.Stats.Wins > playerWins)
                    return false;
            }

            foreach (var racer in gameState.Racers.Inactive.Values)
            {
                if (racer.Stats.Wins > playerWins)
                    return false;
            }

            foreach (var racer in gameState.Racers.Retired.Values)
            {
                if (racer.Stats.Wins > playerWins)
                    return false;
            }

            return true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IAppLogger? logger = null;
            try
            {
                logger = AppLoggerFactory.CreateLogger(LogCategory.App);
                logger.Information("Application shutting down");
            }
            catch (Exception)
            {
                // The way out goes ahead without a log
            }

            // The crash path did all of this already if it ran
            if (Interlocked.Exchange(ref _fatalPathRan, 1) == 0) ShutDownCleanly(logger);

            base.OnExit(e);
        }
    }

}
