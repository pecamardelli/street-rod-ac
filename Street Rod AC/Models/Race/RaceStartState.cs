using System.Globalization;

namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// The shape a car goes into a race in, as the race mode puts it into AC at the start: what the last races left
    /// of it. Written into race.ini under [STREET_ROD] as <c>CAR_n_*</c> keys, n being AC's index of the car.
    /// </summary>
    public sealed class RaceStartState
    {
        /// <summary>How far AC bends a steering rod at most, in metres: MAX_DAMAGE in suspensions.ini, 0.05 on every car the game has</summary>
        public const double MaxSuspensionBendMetres = 0.05;

        /// <summary>Front, rear, left, right: collision speed in km/h, as AC keeps body damage</summary>
        public double[] BodyKmh { get; init; } = new double[4];

        /// <summary>1000 for a new engine, as AC counts it</summary>
        public double EngineLife { get; init; } = 1000;

        /// <summary>0 for a gearbox in good shape, 1 for one that no longer works. AC has no setter: the mode adds it to what the race does.</summary>
        public double GearboxWear { get; init; }

        /// <summary>
        /// How bent each corner already is (front left, front right, rear left, rear right), 0 straight to 1 as far as
        /// AC bends a steering rod. AC has no setter either: the car's data carries the toe it gives, and the mode adds it
        /// to what the race does.
        /// </summary>
        public double[] SuspensionBend { get; init; } = new double[4];

        /// <summary>Nothing wrong with the car</summary>
        public bool IsPristine =>
            BodyKmh.All(z => z <= 0) && EngineLife >= 1000 && GearboxWear <= 0 && SuspensionBend.All(b => b <= 0);

        /// <summary>The race.ini keys for the car at <paramref name="carIndex"/>, in AC's invariant number format</summary>
        public IEnumerable<(string Key, string Value)> IniKeys(int carIndex)
        {
            var prefix = $"CAR_{carIndex}_";
            yield return (prefix + "BODY", List(BodyKmh, "0.0"));
            yield return (prefix + "ENGINE_LIFE", EngineLife.ToString("0.0", CultureInfo.InvariantCulture));
            yield return (prefix + "GEARBOX", GearboxWear.ToString("0.000", CultureInfo.InvariantCulture));
            yield return (prefix + "SUSPENSION", List(SuspensionBend, "0.000"));
        }

        private static string List(double[] values, string format) =>
            string.Join(",", values.Select(v => (double.IsFinite(v) ? v : 0).ToString(format, CultureInfo.InvariantCulture)));
    }
}
