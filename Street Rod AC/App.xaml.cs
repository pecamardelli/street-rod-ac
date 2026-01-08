using System.Configuration;
using System.Data;
using System.Windows;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
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

            // Validate AC installation
            if (!AppSettings.Instance.IsValidInstallation())
            {
                System.Windows.MessageBox.Show(
                    $"Assetto Corsa installation not found at:\n{AppSettings.Instance.AssettoCorsaPath}\n\n" +
                    "Please verify the installation path in the configuration.",
                    "Installation Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // Import AC content into catalog at startup
            try
            {
                Console.WriteLine("Starting car import...");

                var progress = new Progress<ImportProgress>(p =>
                {
                    Console.WriteLine($"Importing cars: {p.Current}/{p.Total} - {p.CurrentCarName}");
                });

                var result = await CarImportService.ImportCarsAsync(progress);

                Console.WriteLine($"Import completed:");
                Console.WriteLine($"  Total found: {result.TotalFound}");
                Console.WriteLine($"  Imported: {result.Imported}");
                Console.WriteLine($"  Updated: {result.Updated}");
                Console.WriteLine($"  Skipped: {result.Skipped}");
                Console.WriteLine($"  Failed: {result.Failed}");
                Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F2}s");

                if (result.Errors.Any())
                {
                    Console.WriteLine($"Errors:");
                    foreach (var error in result.Errors.Take(10))
                    {
                        Console.WriteLine($"  - {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error importing Assetto Corsa content:\n{ex.Message}",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // Navigate to initial screen
            var initScreen = new InitScreenViewModel(NavigationService, DialogService);
            NavigationService.NavigateTo(initScreen);
        }
    }

}
