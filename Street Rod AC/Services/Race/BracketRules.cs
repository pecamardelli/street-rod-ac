using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>What came of a bracket race, by its own rules</summary>
    public readonly record struct BracketOutcome(bool PlayerWon, bool PlayerBrokeOut, bool OpponentBrokeOut);

    /// <summary>
    /// Bracket racing (docs/roadmap.md, step 11): each racer dials in the quarter-mile time they expect to run. The
    /// slower dial-in gets the green first, by the difference, so two cars on their dial-ins get to the quarter
    /// together, and the sharper leaver wins. Running quicker than your dial-in is a breakout, which loses, unless the
    /// other car broke out by more. The race mode runs the trees and judges the race the same way
    /// (mode.lua, bracketPlayerWins); the career decides from the slips.
    ///
    /// The player picks a dial-in, suggested from the car's best quarter. A rival dials in from its car: its best
    /// quarter when it has run one, else an estimate from its power and weight; the better the driver, the sharper
    /// it leaves and the closer to its dial-in it runs (it takes the stripe, as racers do).
    /// </summary>
    public static class BracketRules
    {
        /// <summary>A strip must run at least this far for a bracket race, in metres: the race is to the quarter mile</summary>
        public const double MinStripMetres = 400;

        /// <summary>Dial-ins go in steps of this, in seconds, and between these</summary>
        public const double DialInStep = 0.01, MinDialIn = 6, MaxDialIn = 30;

        /// <summary>What a car with no time and no figures dials in</summary>
        public const double UnknownCarDialIn = 15;

        /// <summary>A driver of the weight of a person in the car, in kg, for the estimate</summary>
        public const double DriverKg = 80;

        /// <summary>
        /// Hale's quarter-mile formula (ET = 5.825 × (lb/hp)^⅓) is for a car on slicks; a street car on street
        /// tyres, as AC's AI launches it, runs about 8% slower. Not yet checked against AC's AI (the user's to play).
        /// </summary>
        public const double StreetEtFactor = 6.29;

        /// <summary>
        /// The bracket race decided from the two slips; null when the player never got to the quarter (then the race
        /// is decided like any other, by what stopped the car). The mode decides on the same numbers, the slips'
        /// (rounded to the millisecond), added up in the same order, so both come to the same verdict.
        /// </summary>
        public static BracketOutcome? Decide(Timeslip? player, double playerDialIn, Timeslip? opponent, double opponentDialIn)
        {
            if (FinishOf(player) is not { } mine) return null;
            var playerOut = mine.Et < playerDialIn;

            if (FinishOf(opponent) is not { } theirs)
                return new BracketOutcome(!playerOut, playerOut, false);

            var opponentOut = theirs.Et < opponentDialIn;
            bool won;
            if (playerOut && opponentOut) won = playerDialIn - mine.Et < opponentDialIn - theirs.Et;
            else if (playerOut != opponentOut) won = opponentOut;
            else won = mine.At < theirs.At;
            return new BracketOutcome(won, playerOut, opponentOut);
        }

        /// <summary>When the car crossed the quarter, in seconds from AC's start, and its elapsed time; null when it didn't</summary>
        public static (double At, double Et)? FinishOf(Timeslip? slip)
        {
            if (slip?.QuarterMileSeconds is not { } et || !double.IsFinite(et)) return null;
            var green = slip.GreenSeconds is { } g && double.IsFinite(g) ? g : 0;
            var reaction = slip.ReactionSeconds is { } r && double.IsFinite(r) ? r : 0;
            return (green + reaction + et, et);
        }

        /// <summary>The dial-in the player is offered for a car: its best quarter, rounded up to the step; null when it has none</summary>
        public static double? SuggestDialIn(Car car) =>
            car.History.BestQuarterSeconds is { } best ? Clamp(RoundUp(best)) : null;

        /// <summary>
        /// A quarter mile the car should run, from its power and weight (kg, without a driver): the dial-in for a car
        /// that has never run one. Null without both figures.
        /// </summary>
        public static double? EstimateEt(double? powerHp, double? weightKg)
        {
            if (powerHp is not { } hp || weightKg is not { } kg || !double.IsFinite(hp) || !double.IsFinite(kg) || hp <= 0 || kg <= 0)
                return null;
            var pounds = (kg + DriverKg) * 2.20462;
            return Clamp(RoundUp(StreetEtFactor * Math.Cbrt(pounds / hp)));
        }

        /// <summary>
        /// The estimate for a car from what is known of it: the dyno figure of its parts (<see cref="Car.PowerHp"/>) or
        /// its spec sheet's power, and the spec sheet's weight; <see cref="UnknownCarDialIn"/> without them
        /// </summary>
        public static double EstimateFor(Car car, Models.Catalog.CarDefinition? definition)
        {
            var power = car.PowerHp is { } hp && hp > 0 && double.IsFinite(hp) ? hp : Street_Rod_AC.Parts.Cars.AcSpecs.ParsePower(definition?.Specs?.Bhp);
            return EstimateEt(power, Street_Rod_AC.Parts.Cars.AcSpecs.ParseWeight(definition?.Specs?.Weight)) ?? UnknownCarDialIn;
        }

        /// <summary>A rival's dial-in: its car's best quarter when it has run one, else the estimate (<see cref="EstimateFor"/>)</summary>
        public static double RivalDialInFor(Car car, Models.Catalog.CarDefinition? definition) =>
            SuggestDialIn(car) ?? EstimateFor(car, definition);

        /// <summary>The bracket race as the race mode runs it: both dial-ins, and how the rival drives by its AI level</summary>
        public static BracketSetup Setup(double playerDialIn, double opponentDialIn, int opponentAiLevel)
        {
            var (reaction, margin) = RivalDriving(opponentAiLevel);
            return new BracketSetup(Clamp(playerDialIn), Clamp(opponentDialIn), reaction, margin);
        }

        /// <summary>
        /// How a rival of this AI level (<see cref="Opponents.OpponentAIAdapter"/>, 85 to 100) leaves and takes the
        /// stripe: seconds from its green to leaving, 0.10 for the best to 0.40; and how far over its dial-in it aims,
        /// 0.02 s to 0.14.
        /// </summary>
        public static (double Reaction, double Margin) RivalDriving(int aiLevel)
        {
            var below = Math.Clamp(100 - aiLevel, 0, 15);
            return (0.10 + below * 0.02, 0.02 + below * 0.008);
        }

        /// <summary>A dial-in as the game keeps it: in steps of <see cref="DialInStep"/>, within the bounds</summary>
        public static double Clamp(double dialIn) =>
            double.IsFinite(dialIn) ? Math.Round(Math.Clamp(dialIn, MinDialIn, MaxDialIn), 2) : UnknownCarDialIn;

        /// <summary>A layout's length from its ui_track.json, in metres: the layout's own, else the track's; null when neither is one</summary>
        public static double? LengthMetres(Models.AC.TrackInfo track, Models.AC.TrackConfiguration? layout) =>
            Street_Rod_AC.Parts.Cars.AcSpecs.ParseLength(string.IsNullOrWhiteSpace(layout?.Length) ? track.Length : layout.Length);

        /// <summary>A layout that runs the quarter mile, for a bracket race: as long as <see cref="MinStripMetres"/></summary>
        public static bool RunsTheQuarter(Models.AC.TrackInfo track, Models.AC.TrackConfiguration? layout) =>
            LengthMetres(track, layout) >= MinStripMetres;

        /// <summary>A time as the strip shows it: "12.45", whatever the culture</summary>
        public static string Show(double? seconds) =>
            seconds is { } s && double.IsFinite(s) ? s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "-";

        private static double RoundUp(double seconds) => Math.Ceiling(seconds * 20 - 1e-9) / 20;
    }
}
