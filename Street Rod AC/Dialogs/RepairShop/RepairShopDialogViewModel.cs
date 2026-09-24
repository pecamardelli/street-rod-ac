using System.Collections.ObjectModel;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs.RepairShop
{
    /// <summary>
    /// The garage's repair bay for one car: what is wrong with it, what each job costs and takes, and doing them one at
    /// a time. The money and the clock are the garage's (<paramref name="perform"/>); a job the player cannot pay for
    /// cannot be started.
    /// </summary>
    public class RepairShopDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Car _car;
        private readonly PartsCatalog? _catalog;
        private readonly Func<decimal> _money;
        private readonly Func<RepairJob, Task> _perform;
        private bool _busy;

        /// <param name="catalog">Null when there are no parts: only the body can be done</param>
        /// <param name="perform">Takes the money, does the job, runs the clock and saves</param>
        public RepairShopDialogViewModel(DialogService dialogService, Car car, string carName, PartsCatalog? catalog,
            Func<decimal> money, Func<RepairJob, Task> perform)
        {
            _dialogService = dialogService;
            _car = car;
            _catalog = catalog;
            _money = money;
            _perform = perform;
            CarName = carName;
            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog(), () => !_busy);
            Refresh();
        }

        public string CarName { get; }

        public ObservableCollection<RepairJobRow> Jobs { get; } = new();

        public bool HasJobs => Jobs.Count > 0;

        public string MoneyDisplay => $"${_money():N0}";

        private string _status = string.Empty;

        /// <summary>Whether the car can race as it is, in a line</summary>
        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public RelayCommand CloseCommand { get; }

        private void Refresh()
        {
            Jobs.Clear();
            foreach (var job in Parts.Cars.RepairShop.Jobs(_car, _catalog))
            {
                Jobs.Add(new RepairJobRow(job, new AsyncRelayCommand(() => Do(job), () => !_busy && _money() >= job.Cost)));
            }

            var problems = CarCondition.WhyCannotRace(_car, _catalog == null ? null : CarCondition.Groups(_catalog));
            Status = problems.Count > 0
                ? "Not fit to race: " + string.Join("; ", problems) + "."
                : Jobs.Count > 0 ? "It races as it is, with its damage." : "Nothing to fix: the car is in good shape.";

            OnPropertyChanged(nameof(HasJobs));
            OnPropertyChanged(nameof(MoneyDisplay));
        }

        private async Task Do(RepairJob job)
        {
            if (_busy || _money() < job.Cost) return;

            _busy = true;
            RaiseAll();
            try
            {
                await _perform(job);
            }
            finally
            {
                _busy = false;
                Refresh();
                RaiseAll();
            }
        }

        private void RaiseAll()
        {
            RelayCommand.RaiseCanExecuteChanged();
            foreach (var row in Jobs) row.Command.RaiseCanExecuteChanged();
        }
    }

    public sealed class RepairJobRow
    {
        public RepairJobRow(RepairJob job, AsyncRelayCommand command)
        {
            Name = job.Name;
            Detail = job.Detail;
            CostDisplay = $"${job.Cost:N0}";
            TimeDisplay = job.Time.GetTimeDescription();
            Command = command;
        }

        public string Name { get; }
        public string Detail { get; }
        public string CostDisplay { get; }
        public string TimeDisplay { get; }
        public AsyncRelayCommand Command { get; }
    }
}
