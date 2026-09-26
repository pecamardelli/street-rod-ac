using System.Globalization;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs.Timeslip
{
    /// <summary>
    /// Timeslips, mark by mark, as the strip hands them out: a column for each lane of a drag race, or for each pass of
    /// a test-and-tune. A bracket race's slip starts with the dial-ins.
    /// </summary>
    public class TimeslipDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;

        public TimeslipDialogViewModel(DialogService dialogService, TimeslipCard card)
        {
            _dialogService = dialogService;
            Title = card.Title;
            Names = card.Lanes.Select(l => l.Name).ToList();
            Rows = BuildRows(card.Lanes);
            Note = card.Note ?? string.Empty;
            OkCommand = new RelayCommand(() => _dialogService.CloseDialog());
        }

        public string Title { get; }

        /// <summary>Whose each column is</summary>
        public IReadOnlyList<string> Names { get; }

        public int Columns => Math.Max(1, Names.Count);

        public IReadOnlyList<TimeslipRow> Rows { get; }

        public string Note { get; }

        public bool HasNote => Note.Length > 0;

        public RelayCommand OkCommand { get; }

        public static IReadOnlyList<TimeslipRow> BuildRows(IReadOnlyList<TimeslipLane> lanes)
        {
            TimeslipRow Row(string label, Func<Models.Race.Timeslip, double?> value, string format) =>
                new(label, lanes.Select(l => Show(l.Slip, value, format)).ToList());

            var rows = new List<TimeslipRow>();
            if (lanes.Any(l => l.DialIn != null))
                rows.Add(new TimeslipRow("DIAL", lanes.Select(l => l.DialIn is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) : "-").ToList()));

            rows.Add(new TimeslipRow("R/T", lanes.Select(l => Reaction(l.Slip)).ToList()));
            rows.Add(Row("60'", s => s.SixtyFeetSeconds, "0.000"));
            rows.Add(Row("330'", s => s.ThreeThirtyFeetSeconds, "0.000"));
            rows.Add(Row("1/8 ET", s => s.EighthMileSeconds, "0.000"));
            rows.Add(Row("1/8 MPH", s => s.EighthMileMph, "0.00"));
            rows.Add(Row("1000'", s => s.ThousandFeetSeconds, "0.000"));
            rows.Add(Row("1/4 ET", s => s.QuarterMileSeconds, "0.000"));
            rows.Add(Row("1/4 MPH", s => s.QuarterMileMph, "0.00"));
            return rows;
        }

        /// <summary>The reaction time, a red light marked as the strips mark it: "-0.052 RL"</summary>
        private static string Reaction(Models.Race.Timeslip? slip)
        {
            var shown = Show(slip, s => s.ReactionSeconds, "0.000");
            return slip?.RedLight == true ? $"{shown} RL" : shown;
        }

        /// <summary>A mark the car never reached is a dash, as on a real slip</summary>
        private static string Show(Models.Race.Timeslip? slip, Func<Models.Race.Timeslip, double?> value, string format) =>
            slip != null && value(slip) is { } number && double.IsFinite(number) ? number.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>One mark of the slip: its label and each column's figure</summary>
    public sealed record TimeslipRow(string Label, IReadOnlyList<string> Values);
}
