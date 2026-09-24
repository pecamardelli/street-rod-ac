using System.IO;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Settings;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Settings
{
    public class SettingsScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameSettingsService _settingsService;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand OpenCatalogEditorCommand { get; }
        public RelayCommand BrowseAssettoCorsaFolderCommand { get; }

        public GameSettings Settings => _settingsService.Current;

        private string _assettoCorsaFolder;

        /// <summary>
        /// The Assetto Corsa folder the game uses. Read once at start-up: a new one takes effect the next time
        /// the game starts, because the catalog and the car-data overlay are built on the folder it started with.
        /// </summary>
        public string AssettoCorsaFolder
        {
            get => _assettoCorsaFolder;
            set
            {
                if (SetProperty(ref _assettoCorsaFolder, value ?? string.Empty))
                    OnPropertyChanged(nameof(AssettoCorsaFolderHint));
            }
        }

        /// <summary>Whether the folder typed or picked looks like an Assetto Corsa install</summary>
        public string AssettoCorsaFolderHint => LooksLikeAssettoCorsa(AssettoCorsaFolder)
            ? string.Empty
            : "No Assetto Corsa installation found in this folder (it needs content\\cars and content\\tracks).";

        public SettingsScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameSettingsService settingsService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _settingsService = settingsService;
            _logger = AppLoggerFactory.CreateLogger("Settings");

            BackCommand = new RelayCommand(OnBack);
            SaveCommand = new RelayCommand(OnSave);
            ResetCommand = new RelayCommand(OnReset);
            OpenCatalogEditorCommand = new RelayCommand(OnOpenCatalogEditor);
            BrowseAssettoCorsaFolderCommand = new RelayCommand(OnBrowseAssettoCorsaFolder);

            _assettoCorsaFolder = SavedAssettoCorsaFolder;
        }

        /// <summary>The folder the settings name, or the default when they name none</summary>
        private string SavedAssettoCorsaFolder =>
            string.IsNullOrWhiteSpace(Settings.AssettoCorsaPath) ? AppSettings.DefaultAssettoCorsaPath : Settings.AssettoCorsaPath.Trim();

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered settings screen");
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to main menu");
            _navigationService.NavigateToMainMenu();
        }

        private void OnBrowseAssettoCorsaFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Assetto Corsa folder",
                InitialDirectory = Directory.Exists(AssettoCorsaFolder) ? AssettoCorsaFolder : string.Empty
            };

            if (dialog.ShowDialog() == true)
            {
                AssettoCorsaFolder = dialog.FolderName;
            }
        }

        private void OnSave()
        {
            var folder = AssettoCorsaFolder.Trim();

            try
            {
                // The default is stored as no folder at all, so a later default follows along
                Settings.AssettoCorsaPath = folder.Length == 0 ||
                    string.Equals(folder, AppSettings.DefaultAssettoCorsaPath, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : folder;

                _settingsService.Save();
                _logger.Information("Settings saved");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the settings");
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"The settings could not be saved:\n\n{ex.Message}",
                    "Settings Not Saved"));
                return;
            }

            // The running game keeps the folder it started with
            AssettoCorsaFolder = SavedAssettoCorsaFolder;
            if (!string.Equals(AssettoCorsaFolder, AppSettings.Instance.AssettoCorsaPath, StringComparison.OrdinalIgnoreCase))
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"The game will use the Assetto Corsa folder\n{AssettoCorsaFolder}\nthe next time it starts. Restart the game for the change to take effect.",
                    "Restart Needed"));
            }
        }

        private void OnReset()
        {
            try
            {
                _settingsService.ResetToDefaults();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not reset the settings");
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"The settings could not be reset:\n\n{ex.Message}",
                    "Settings Not Saved"));
                return;
            }

            OnPropertyChanged(nameof(Settings));
            AssettoCorsaFolder = SavedAssettoCorsaFolder;
            _logger.Information("Settings reset to defaults");
        }

        private void OnOpenCatalogEditor()
        {
            _logger.Information("Navigating to car catalog editor");
            _navigationService.NavigateToCarCatalogEditor();
        }

        /// <summary>The same test the game makes of the install at start-up: a cars and a tracks folder</summary>
        public static bool LooksLikeAssettoCorsa(string? folder) =>
            !string.IsNullOrWhiteSpace(folder)
            && Directory.Exists(Path.Combine(folder, "content", "cars"))
            && Directory.Exists(Path.Combine(folder, "content", "tracks"));
    }
}
