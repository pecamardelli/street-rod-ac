using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Configuration.Models;

namespace Street_Rod_AC.Screens.RaceLoading
{
    /// <summary>
    /// Loading screen shown while AC is running.
    /// Waits for AC to exit, processes results, then navigates back.
    /// </summary>
    public class RaceLoadingScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly DragRaceLaunchIntent _launchIntent;
        private readonly IAppLogger _logger;

        private string _statusMessage = "Launching race...";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public RaceLoadingScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            IAssettoCorsaLauncher launcher,
            DragRaceLaunchIntent launchIntent)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _launcher = launcher;
            _launchIntent = launchIntent;
            _logger = AppLoggerFactory.CreateLogger("RaceLoading");
        }

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered race loading screen");

            await LaunchAndWaitForRace();
        }

        private async Task LaunchAndWaitForRace()
        {
            try
            {
                StatusMessage = "Racing...";
                _logger.Information("Launching race with intent: Player={PlayerCar}, Opponent={OpponentCar}",
                    _launchIntent.PlayerCarId, _launchIntent.OpponentCarId);

                var result = await _launcher.LaunchRaceAsync(_launchIntent);

                if (result.Success)
                {
                    _logger.Information("Race completed successfully");
                    StatusMessage = "Processing results...";

                    // Small delay to show the processing message
                    await Task.Delay(500);
                }
                else
                {
                    _logger.Warning("Race launch failed: {Error}", result.ErrorMessage);
                    StatusMessage = $"Race failed: {result.ErrorMessage}";
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during race");
                StatusMessage = $"Error: {ex.Message}";
                await Task.Delay(2000);
            }
            finally
            {
                IsLoading = false;

                // Navigate back to diner
                _logger.Information("Navigating back to diner");
                _navigationService.NavigateToDiner(_gameState);
            }
        }
    }
}
