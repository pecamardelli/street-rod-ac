using System.Configuration;
using System.Data;
using System.Windows;
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
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;

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
        public IIniModificationService IniModificationService { get; private set; }
        public Services.Race.IRaceResultIngestionService RaceResultIngestionService { get; private set; }
        public IOpponentRepository OpponentRepository { get; private set; }
        public IOpponentInitializationService OpponentInitializationService { get; private set; }
        public IOpponentChallengeService OpponentChallengeService { get; private set; }
        public IGameTimeScheduler Scheduler { get; private set; }
        public IGameTimeService GameTimeService { get; private set; }

        // Current game state (set when a game is loaded or created)
        public Models.GameState.GameState? CurrentGameState { get; set; }

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
            // Initialize logging FIRST
            AppLoggerFactory.Initialize();

            InitializeComponent();

            ContentService = new AssettoCorsaContentService();
            IniModificationService = new IniModificationService();
            DialogService = new DialogService();
            CatalogRepository = new ContentCatalogRepository();
            CarImportService = new CarImportService(CatalogRepository);
            ProfileRepository = new CarProfileRepository();
            ProfileService = new CarProfileService(CatalogRepository, ProfileRepository);
            MarketService = new UsedCarMarketService(CatalogRepository, ProfileRepository);

            // Scheduler and Time Service
            Scheduler = new GameTimeScheduler();
            Scheduler.RegisterTask(new MarketRefreshTask(MarketService));
            GameTimeService = new GameTimeService(Scheduler);

            // Opponent services (must be initialized before GameStateRepository)
            OpponentRepository = new OpponentRepository();
            OpponentInitializationService = new OpponentInitializationService(OpponentRepository, CatalogRepository, ProfileRepository);
            OpponentChallengeService = new OpponentChallengeService(CatalogRepository, ProfileRepository);

            // Game state repository (depends on opponent initialization service)
            GameStateRepository = new GameStateRepository(OpponentInitializationService);

            // Race result services
            var raceResultValidator = new Services.Race.Validation.RaceResultValidator();
            var sessionRepository = new Services.Race.RaceSessionRepository();
            var sessionDeduplicator = new Services.Race.Validation.SessionDeduplicator(sessionRepository);
            var raceResultProcessor = new Services.Race.RaceResultProcessor(GameStateRepository, sessionRepository);
            RaceResultIngestionService = new Services.Race.RaceResultIngestionService(
                raceResultValidator,
                sessionDeduplicator,
                raceResultProcessor,
                sessionRepository);

            // Pass race result service to launcher
            Launcher = new AssettoCorsaLauncher(IniModificationService, RaceResultIngestionService);

            // NavigationService (must be created after all its dependencies)
            NavigationService = new NavigationService(
                DialogService,
                CatalogRepository,
                ProfileRepository,
                Launcher,
                OpponentChallengeService,
                GameStateRepository,
                MarketService);
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
            logger.Information("Application starting");

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
            }
            catch (Exception ex)
            {
                logger.Critical(ex, "Critical error during car import");

                var errorDialog = new Dialogs.Information.InformationDialogViewModel(
                    DialogService,
                    $"Error importing Assetto Corsa content:\n{ex.Message}",
                    "Import Error");
                DialogService.ShowDialog(errorDialog);
                Shutdown();
                return;
            }

            // Process orphaned race results
            try
            {
                logger.Information("Processing orphaned race result files");
                var orphanedResult = await RaceResultIngestionService.ProcessOrphanedResultsAsync();
                logger.Information("Orphaned results processed: {Processed} files, {Errors} errors",
                    orphanedResult.FilesProcessed, orphanedResult.Errors.Count);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to process orphaned race results - continuing startup");
            }

            // Navigate to initial screen
            logger.Information("Navigating to initial screen");
            NavigationService.NavigateToInit();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.App);
            logger.Information("Application shutting down");

            // Save current game state if one is loaded
            if (CurrentGameState != null && !string.IsNullOrEmpty(CurrentGameState.SaveName))
            {
                try
                {
                    logger.Information("Saving game state on exit: {SaveName}", CurrentGameState.SaveName);
                    GameStateRepository.Save(CurrentGameState, CurrentGameState.SaveName);
                    logger.Information("Game state saved successfully");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to save game state on exit");
                }
            }

            AppLoggerFactory.Shutdown();
            base.OnExit(e);
        }
    }

}
