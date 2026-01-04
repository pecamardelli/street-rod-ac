using System.Configuration;
using System.Data;
using System.Windows;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Services;

namespace Street_Rod_AC
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public IAssettoCorsaContentService ContentService { get; private set; }
        public IAssettoCorsaLauncher Launcher { get; private set; }

        public App()
        {
            ContentService = new AssettoCorsaContentService();
            Launcher = new AssettoCorsaLauncher();
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

            // Load AC content at startup
            try
            {
                await Task.Run(async () =>
                {
                    await ContentService.LoadCarsAsync();
                    await ContentService.LoadTracksAsync();
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error loading Assetto Corsa content:\n{ex.Message}",
                    "Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }
        }
    }

}
