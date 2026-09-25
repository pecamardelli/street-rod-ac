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
        private readonly Action _back;
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
            GameSettingsService settingsService,
            Action back)
        {
            _navigationService = navigationService;
            _back = back;
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
            _back();
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
            var savedPath = Settings.AssettoCorsaPath;

            try
            {
                // The default is stored as no folder at all, so a later default follows along
                Settings.AssettoCorsaPath = folder.Length == 0 ||
                    string.Equals(folder, AppSettings.DefaultAssettoCorsaPath, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : folder;

                // The service logs why; a false here means nothing reached the disk
                if (!_settingsService.Save())
                {
                    // Not on disk, so not in use either: a later save of something else must not carry it along
                    Settings.AssettoCorsaPath = savedPath;
                    ShowNotSaved("The settings could not be saved. The details are in the log.");
                    return;
                }

                _logger.Information("Settings saved");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the settings");
                Settings.AssettoCorsaPath = savedPath;
                ShowNotSaved($"The settings could not be saved:\n\n{ex.Message}");
                return;
            }

            AssettoCorsaFolder = SavedAssettoCorsaFolder;
            ShowRestartNoticeIfFolderChanged();
        }

        /// <summary>
        /// The running game keeps the folder it started with: once a saved folder differs from it, the player
        /// hears that it takes a restart
        /// </summary>
        private void ShowRestartNoticeIfFolderChanged()
        {
            if (string.Equals(AssettoCorsaFolder, AppSettings.Instance.AssettoCorsaPath, StringComparison.OrdinalIgnoreCase))
                return;

            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                $"The game will use the Assetto Corsa folder\n{AssettoCorsaFolder}\nthe next time it starts. Restart the game for the change to take effect.",
                "Restart Needed"));
        }

        private void ShowNotSaved(string message)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, "Settings Not Saved"));
        }

        private void OnReset()
        {
            try
            {
                if (!_settingsService.ResetToDefaults())
                {
                    ShowNotSaved("The settings could not be reset. The details are in the log.");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not reset the settings");
                ShowNotSaved($"The settings could not be reset:\n\n{ex.Message}");
                return;
            }

            OnPropertyChanged(nameof(Settings));
            AssettoCorsaFolder = SavedAssettoCorsaFolder;
            _logger.Information("Settings reset to defaults");

            // The defaults name the default folder: a player whose install is elsewhere needs to know
            ShowRestartNoticeIfFolderChanged();
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
