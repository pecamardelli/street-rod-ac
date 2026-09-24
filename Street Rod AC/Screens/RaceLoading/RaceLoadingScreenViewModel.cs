using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;
using System.Windows.Input;

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
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IAppLogger _logger;

        // The "Stop Race" question while it is up: withdrawn when the race ends on its own, so it never stays over
        // the diner with the race's messages queued behind it
        private ConfirmationDialogViewModel? _stopDialog;

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
            set
            {
                if (SetProperty(ref _isLoading, value)) CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>
        /// The way out of a race that hangs (AC stuck loading a broken car or track): stops AC, and the launcher
        /// puts the install back as it does after any race
        /// </summary>
        public ICommand StopRaceCommand { get; }

        public RaceLoadingScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            IAssettoCorsaLauncher launcher,
            DragRaceLaunchIntent launchIntent,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _launcher = launcher;
            _launchIntent = launchIntent;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _logger = AppLoggerFactory.CreateLogger("RaceLoading");
            StopRaceCommand = new RelayCommand(ConfirmStopRace, () => IsLoading);
        }

        private void ConfirmStopRace()
        {
            // Before AC has started, stopping calls the launch off and costs nothing
            var atStake = Context is { } context && (context.IsPinkSlip || context.CashWager > 0);
            var message = !_launcher.IsAssettoCorsaRunning
                ? "Call the race off?\n\nAssetto Corsa has not started yet, so nothing is lost."
                : atStake
                    ? "Stop Assetto Corsa now?\n\nStopping before the finish counts as a loss: what was at stake is lost."
                    : "Stop Assetto Corsa now?\n\nThe race will not count.";

            _stopDialog = new ConfirmationDialogViewModel(_dialogService, message, "Stop Race", confirmed =>
            {
                _stopDialog = null;
                if (!confirmed || !IsLoading) return;

                _logger.Warning("The player stopped the race");
                StatusMessage = "Stopping the race...";
                _launcher.CancelRace();
            });
            _dialogService.ShowDialog(_stopDialog);
        }

        /// <summary>The race as it was set up, carried by the intent; null for a launch without one</summary>
        private RaceContext? Context =>
            _launchIntent.Metadata.TryGetValue("RaceContext", out var value) ? value as RaceContext : null;

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered race loading screen");

            // Nothing may escape from here: this runs as a fire-and-forget call, and the player has to get back
            // to the game whatever happened to the race
            var messages = new List<PlayerMessage>();
            try
            {
                messages = await LaunchAndWaitForRace();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The race could not be seen through");
            }

            IsLoading = false;

            // The race is over: a Stop question still up has nothing left to stop
            if (_stopDialog is { } stopDialog)
            {
                _stopDialog = null;
                try
                {
                    _dialogService.Withdraw(stopDialog);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not withdraw the Stop Race question");
                }
            }

            ReturnFromRace(towed: messages.Any(m => m.TowedToGarage));

            // One at a time, over the screen the player came back to: the dialog service queues them
            foreach (var message in messages)
            {
                if (message.Timeslip is { } slip)
                    _dialogService.ShowDialog(new Dialogs.Timeslip.TimeslipDialogViewModel(_dialogService, slip));
                else
                    _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message.Text, message.Title));
            }
        }

        private async Task<List<PlayerMessage>> LaunchAndWaitForRace()
        {
            var messages = new List<PlayerMessage>();
            try
            {
                // The launcher puts the race on record in the save (pending) before AC starts, after settling an
                // earlier race still waiting for its result, and takes it off again if AC never starts
                StatusMessage = "Racing...";
                _logger.Information("Launching race with intent: Player={PlayerCar}, Opponent={OpponentCar}",
                    _launchIntent.PlayerCarId, _launchIntent.OpponentCarId);

                var result = await _launcher.LaunchRaceAsync(_launchIntent);
                messages.AddRange(result.PlayerMessages);
                _logger.Information("Race over: {Outcome} (success: {Success})", result.Outcome, result.Success);

                // Time passes for a race that was run, whatever came of it; not for one that never started
                var raced = result.Outcome is RaceOutcome.Processed or RaceOutcome.NoResult
                    or RaceOutcome.Quarantined or RaceOutcome.ResultPending;
                if (raced)
                {
                    StatusMessage = result.Outcome == RaceOutcome.ResultPending
                        ? "The result is not in yet..."
                        : "Processing results...";

                    await SpendRaceTime();

                    // Small delay to show the processing message
                    await Task.Delay(500);
                }
                else if (result.Outcome == RaceOutcome.Cancelled)
                {
                    StatusMessage = "Race called off";
                    await Task.Delay(1000);
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

            return messages;
        }

        /// <summary>The time the race took, and a save for it: late in the day that is the next morning</summary>
        private async Task SpendRaceTime()
        {
            try
            {
                var action = (Context?.RaceType ?? _launchIntent.RaceType) == RaceType.DragRace
                    ? GameAction.DragRace
                    : GameAction.RoadRace;
                await _timeService.SpendTimeAsync(_gameState, action);

                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend and save the time for the race");
            }
        }

        /// <summary>
        /// Back to the diner, or to the garage when the diner cannot be opened. A player whose car was towed home
        /// after a crash goes to the garage, where the car is.
        /// </summary>
        private void ReturnFromRace(bool towed)
        {
            if (towed)
            {
                _logger.Information("The car was towed home after a crash: navigating to the garage");
                _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
                return;
            }

            _logger.Information("Navigating back to diner");
            if (_navigationService.NavigateToDiner(_gameState)) return;

            _logger.Warning("The diner could not be opened after the race: going to the garage");
            _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
        }
    }
}
