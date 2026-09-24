using System.Globalization;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs.Timeslip
{
    /// <summary>A drag race's timeslip: both lanes, mark by mark, as the strip hands it out</summary>
    public class TimeslipDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;

        public TimeslipDialogViewModel(DialogService dialogService, TimeslipCard card)
        {
            _dialogService = dialogService;
            PlayerName = card.PlayerName;
            OpponentName = card.OpponentName;
            Rows = BuildRows(card.Player, card.Opponent);
            OkCommand = new RelayCommand(() => _dialogService.CloseDialog());
        }

        public string PlayerName { get; }
        public string OpponentName { get; }

        public IReadOnlyList<TimeslipRow> Rows { get; }

        public RelayCommand OkCommand { get; }

        public static IReadOnlyList<TimeslipRow> BuildRows(Models.Race.Timeslip? player, Models.Race.Timeslip? opponent)
        {
            TimeslipRow Row(string label, Func<Models.Race.Timeslip, double?> value, string format) =>
                new(label, Show(player, value, format), Show(opponent, value, format));

            return new[]
            {
                Row("R/T", s => s.ReactionSeconds, "0.000"),
                Row("60'", s => s.SixtyFeetSeconds, "0.000"),
                Row("330'", s => s.ThreeThirtyFeetSeconds, "0.000"),
                Row("1/8 ET", s => s.EighthMileSeconds, "0.000"),
                Row("1/8 MPH", s => s.EighthMileMph, "0.00"),
                Row("1000'", s => s.ThousandFeetSeconds, "0.000"),
                Row("1/4 ET", s => s.QuarterMileSeconds, "0.000"),
                Row("1/4 MPH", s => s.QuarterMileMph, "0.00")
            };
        }

        /// <summary>A mark the car never reached is a dash, as on a real slip</summary>
        private static string Show(Models.Race.Timeslip? slip, Func<Models.Race.Timeslip, double?> value, string format) =>
            slip != null && value(slip) is { } number && double.IsFinite(number) ? number.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    public sealed record TimeslipRow(string Label, string Player, string Opponent);
}
