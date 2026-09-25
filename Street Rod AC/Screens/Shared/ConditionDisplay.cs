using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Market;

namespace Street_Rod_AC.Screens.Shared
{
    /// <summary>A car's condition as a screen shows it: a percentage and a word</summary>
    public sealed record ConditionText(string Percent, string Label)
    {
        /// <summary>"Good condition (82%)", or just what is wrong with a car that will not run</summary>
        public string Sentence => Percent == Label ? Label : $"{Label} condition ({Percent})";
    }

    /// <summary>
    /// One way to show a car's condition on every screen, on the market's rule: what is worst about the car when
    /// it will not run (totaled, a blown engine), otherwise its one overall condition (<see cref="CarValuation.ConditionOf"/>).
    /// A used-car listing's condition is that same figure, so an ad and the car bought from it read the same.
    /// </summary>
    public static class ConditionDisplay
    {
        public static ConditionText Of(Car car)
        {
            if (CarCondition.IsTotaled(car)) return new ConditionText("Totaled", "Totaled");
            if (car.EngineHealth <= 0) return new ConditionText("Blown engine", "Blown engine");
            return Of(CarValuation.ConditionOf(car));
        }

        /// <summary>An overall condition, 0 to 1</summary>
        public static ConditionText Of(double condition)
        {
            var clamped = double.IsFinite(condition) ? Math.Clamp(condition, 0, 1) : 0;
            return new ConditionText($"{(int)(clamped * 100)}%", LabelFor(clamped));
        }

        public static string LabelFor(double condition) => condition switch
        {
            >= 0.9 => "Excellent",
            >= 0.75 => "Good",
            >= 0.6 => "Fair",
            >= 0.4 => "Poor",
            _ => "Very Poor"
        };
    }
}
