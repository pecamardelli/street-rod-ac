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
using Street_Rod_AC.Services.Storage;

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

        public App()
        {
            // Initialize logging FIRST
            AppLoggerFactory.Initialize();

            InitializeComponent();

            ContentService = new AssettoCorsaContentService();
            Launcher = new AssettoCorsaLauncher();
            NavigationService = new NavigationService();
            DialogService = new DialogService();
            GameStateRepository = new GameStateRepository();
            CatalogRepository = new ContentCatalogRepository();
            CarImportService = new CarImportService(CatalogRepository);
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

                System.Windows.MessageBox.Show(
                    $"Assetto Corsa installation not found at:\n{AppSettings.Instance.AssettoCorsaPath}\n\n" +
                    "Please verify the installation path in the configuration.",
                    "Installation Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            }
            catch (Exception ex)
            {
                logger.Critical(ex, "Critical error during car import");

                System.Windows.MessageBox.Show(
                    $"Error importing Assetto Corsa content:\n{ex.Message}",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // Navigate to initial screen
            logger.Information("Navigating to initial screen");
            var initScreen = new InitScreenViewModel(NavigationService, DialogService);
            NavigationService.NavigateTo(initScreen);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.App);
            logger.Information("Application shutting down");
            AppLoggerFactory.Shutdown();
            base.OnExit(e);
        }
    }

}
