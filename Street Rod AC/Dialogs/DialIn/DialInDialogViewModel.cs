using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs.DialIn
{
    /// <summary>
    /// A bracket race's dial-in: the quarter-mile time the player expects to run, suggested from the car's best (or,
    /// for a car that has never run one, from its power and weight), set in hundredths and tenths. Shows the rival's
    /// dial-in and who gets the green first. The callback gets the dial-in, or null when the player backs out.
    /// </summary>
    public class DialInDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Action<double?> _callback;
        private double _dialIn;

        public DialInDialogViewModel(DialogService dialogService, Car car, string carName, double estimate,
            string opponentName, double opponentDialIn, Action<double?> callback)
        {
            _dialogService = dialogService;
            _callback = callback;
            CarName = carName;
            OpponentName = opponentName;
            OpponentDialIn = opponentDialIn;

            var suggested = BracketRules.SuggestDialIn(car);
            _dialIn = suggested ?? BracketRules.Clamp(estimate);
            BestNote = CarHistoryDisplay.BestQuarter(car.History) is { Length: > 0 } best
                ? $"Your car's {best}."
                : "Your car has never run the quarter: this is a guess from its power and weight. A test-and-tune tells you better.";

            UpCommand = new RelayCommand(() => DialIn += 0.01);
            DownCommand = new RelayCommand(() => DialIn -= 0.01);
            UpTenthCommand = new RelayCommand(() => DialIn += 0.1);
            DownTenthCommand = new RelayCommand(() => DialIn -= 0.1);
            ConfirmCommand = new RelayCommand(OnConfirm);
            CancelCommand = new RelayCommand(OnCancel);
        }

        /// <summary>
        /// Asks the player for their dial-in against a rival's, the estimate for their car from its parts and spec
        /// sheet; the dial-in they pick, or null when they back out
        /// </summary>
        public static Task<double?> AskAsync(DialogService dialogService, Car car, Models.Catalog.CarDefinition? definition,
            string carName, string opponentName, double opponentDialIn)
        {
            var answer = new TaskCompletionSource<double?>();
            dialogService.ShowDialog(new DialInDialogViewModel(dialogService, car, carName, BracketRules.EstimateFor(car, definition),
                opponentName, opponentDialIn, dialIn => answer.TrySetResult(dialIn)));
            return answer.Task;
        }

        public string CarName { get; }
        public string OpponentName { get; }
        public double OpponentDialIn { get; }

        /// <summary>Where the suggestion came from</summary>
        public string BestNote { get; }

        public double DialIn
        {
            get => _dialIn;
            set
            {
                var clamped = BracketRules.Clamp(value);
                if (SetProperty(ref _dialIn, clamped))
                {
                    OnPropertyChanged(nameof(DialInDisplay));
                    OnPropertyChanged(nameof(StartNote));
                }
            }
        }

        public string DialInDisplay => BracketRules.Show(DialIn);

        public string OpponentDisplay => $"{OpponentName} dials {BracketRules.Show(OpponentDialIn)}";

        /// <summary>Who leaves first, and by how much</summary>
        public string StartNote
        {
            get
            {
                var gap = Math.Round(DialIn - OpponentDialIn, 2);
                if (Math.Abs(gap) < 0.005) return "You both get the green together.";
                var seconds = BracketRules.Show(Math.Abs(gap));
                return gap > 0
                    ? $"You get the green {seconds} s before {OpponentName}."
                    : $"{OpponentName} gets the green {seconds} s before you.";
            }
        }

        public RelayCommand UpCommand { get; }
        public RelayCommand DownCommand { get; }
        public RelayCommand UpTenthCommand { get; }
        public RelayCommand DownTenthCommand { get; }
        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelCommand { get; }

        // Closed before the callback: it goes on to the race, and anything it shows must not close with this
        private void OnConfirm()
        {
            _dialogService.CloseDialog();
            _callback(DialIn);
        }

        private void OnCancel()
        {
            _dialogService.CloseDialog();
            _callback(null);
        }
    }
}
