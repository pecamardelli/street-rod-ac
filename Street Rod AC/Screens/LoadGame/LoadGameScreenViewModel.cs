using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;

namespace Street_Rod_AC.Screens.LoadGame
{
    public class LoadGameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly Action _back;
        private readonly DialogService _dialogService;
        private readonly IGameStateRepository _repository;
        private readonly ICarPartsService _partsService;
        private readonly Action<GameState?> _setCurrentGame;
        private readonly IAppLogger _logger;
        private string _errorMessage = string.Empty;
        private bool _hasSaves;

        public ObservableCollection<SaveGameInfo> SavedGames { get; }

        public bool HasSaves
        {
            get => _hasSaves;
            set => SetProperty(ref _hasSaves, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public RelayCommand<SaveGameInfo> LoadSaveCommand { get; }
        public RelayCommand<SaveGameInfo> DeleteSaveCommand { get; }
        public RelayCommand BackCommand { get; }

        /// <param name="setCurrentGame">Makes the loaded game the one the app saves on exit and races with</param>
        /// <param name="back">Closes the card, back to the main screen's menu</param>
        public LoadGameScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IGameStateRepository repository,
            ICarPartsService partsService,
            Action<GameState?> setCurrentGame,
            Action back)
        {
            _navigationService = navigationService;
            _back = back;
            _dialogService = dialogService;
            _repository = repository;
            _partsService = partsService;
            _setCurrentGame = setCurrentGame;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

            SavedGames = new ObservableCollection<SaveGameInfo>();

            LoadSaveCommand = new RelayCommand<SaveGameInfo>(OnLoadSave);
            DeleteSaveCommand = new RelayCommand<SaveGameInfo>(OnDeleteSave);
            BackCommand = new RelayCommand(OnBack);

            // The saves are listed in Enter, once: the screen is always entered right after it is made
        }

        private void LoadSavedGames()
        {
            try
            {
                SavedGames.Clear();

                // Headers only: listing no longer loads every whole game, and never closes the save in use.
                // A save that cannot be read is logged and left off by the repository.
                foreach (var header in _repository.ListSaveHeaders())
                {
                    SavedGames.Add(new SaveGameInfo
                    {
                        SaveName = header.SaveName,
                        PlayerName = header.PlayerName,
                        LastPlayedDate = header.LastPlayedDate,
                        LastPlayedText = FormatLastPlayed(header.LastPlayedDate)
                    });
                }

                HasSaves = SavedGames.Count > 0;

                if (!HasSaves)
                {
                    ErrorMessage = string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the saved games");
                ErrorMessage = $"Failed to load saved games: {ex.Message}";
                HasSaves = false;
            }
        }

        private string FormatLastPlayed(DateTime lastPlayed)
        {
            var timeSpan = DateTime.Now - lastPlayed;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes != 1 ? "s" : "")} ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours != 1 ? "s" : "")} ago";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays != 1 ? "s" : "")} ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)} week{((int)(timeSpan.TotalDays / 7) != 1 ? "s" : "")} ago";

            return lastPlayed.ToString("MMM d, yyyy");
        }

        private void OnLoadSave(SaveGameInfo? saveInfo)
        {
            if (saveInfo == null)
                return;

            try
            {
                var gameState = _repository.Load(saveInfo.SaveName);

                if (gameState == null)
                {
                    ErrorMessage = "Failed to load save game";
                    return;
                }

                // Parts saved under ids of a pack that has been replaced since
                if (_partsService.BringUpToDate(gameState)) _repository.Save(gameState, saveInfo.SaveName);

                // Store current game state in App for saving on exit
                _setCurrentGame(gameState);

                // Navigate to game screen with loaded state
                _navigationService.NavigateToGarage(gameState);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not load save {SaveName}", saveInfo.SaveName);
                ErrorMessage = $"Failed to load game: {ex.Message}";
            }
        }

        private void OnDeleteSave(SaveGameInfo? saveInfo)
        {
            if (saveInfo == null)
                return;

            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                $"Are you sure you want to delete the save '{saveInfo.PlayerName}'? This action cannot be undone.",
                "Delete Save",
                confirmed =>
                {
                    if (confirmed)
                    {
                        try
                        {
                            _repository.Delete(saveInfo.SaveName);
                            SavedGames.Remove(saveInfo);
                            HasSaves = SavedGames.Count > 0;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Could not delete save {SaveName}", saveInfo.SaveName);
                            ErrorMessage = $"Failed to delete save: {ex.Message}";
                        }
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        private void OnBack()
        {
            _back();
        }

        public override void Enter()
        {
            base.Enter();
            // Refresh the list when entering the screen
            LoadSavedGames();
        }

        public override void Exit()
        {
            base.Exit();
        }
    }

    public class SaveGameInfo
    {
        public string SaveName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public DateTime LastPlayedDate { get; set; }
        public string LastPlayedText { get; set; } = string.Empty;
    }
}
