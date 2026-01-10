using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;

namespace Street_Rod_AC.Screens.LoadGame
{
    public class LoadGameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
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

        public LoadGameScreenViewModel(NavigationService navigationService, DialogService dialogService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;

            SavedGames = new ObservableCollection<SaveGameInfo>();

            LoadSaveCommand = new RelayCommand<SaveGameInfo>(OnLoadSave);
            DeleteSaveCommand = new RelayCommand<SaveGameInfo>(OnDeleteSave);
            BackCommand = new RelayCommand(OnBack);

            LoadSavedGames();
        }

        private void LoadSavedGames()
        {
            try
            {
                var app = (App)System.Windows.Application.Current;
                var repository = app.GameStateRepository;

                SavedGames.Clear();
                var saves = repository.ListSaves();

                foreach (var saveName in saves)
                {
                    try
                    {
                        var gameState = repository.Load(saveName);
                        if (gameState != null)
                        {
                            SavedGames.Add(new SaveGameInfo
                            {
                                SaveName = saveName,
                                PlayerName = gameState.Player.Name,
                                LastPlayedDate = gameState.LastPlayedDate,
                                LastPlayedText = FormatLastPlayed(gameState.LastPlayedDate)
                            });
                        }
                    }
                    catch
                    {
                        // Skip corrupted saves
                        continue;
                    }
                }

                HasSaves = SavedGames.Count > 0;

                if (!HasSaves)
                {
                    ErrorMessage = string.Empty;
                }
            }
            catch (Exception ex)
            {
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
                var app = (App)System.Windows.Application.Current;
                var repository = app.GameStateRepository;

                var gameState = repository.Load(saveInfo.SaveName);

                if (gameState == null)
                {
                    ErrorMessage = "Failed to load save game";
                    return;
                }

                // Navigate to game screen with loaded state
                var gameViewModel = new Game.GameScreenViewModel(
                    _navigationService,
                    _dialogService,
                    gameState,
                    app.CatalogRepository);
                _navigationService.NavigateTo(gameViewModel);
            }
            catch (Exception ex)
            {
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
                            var app = (App)System.Windows.Application.Current;
                            var repository = app.GameStateRepository;

                            repository.Delete(saveInfo.SaveName);
                            SavedGames.Remove(saveInfo);
                            HasSaves = SavedGames.Count > 0;
                        }
                        catch (Exception ex)
                        {
                            ErrorMessage = $"Failed to delete save: {ex.Message}";
                        }
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        private void OnBack()
        {
            var mainMenuViewModel = new MainMenu.MainMenuScreenViewModel(_navigationService, _dialogService);
            _navigationService.NavigateTo(mainMenuViewModel);
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
