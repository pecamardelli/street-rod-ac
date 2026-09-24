using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Parts;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>
    /// What a car is worth: the one formula for it. The market prices its cars with it, an opponent's car is
    /// bought for it, a pink-slipped car goes back on a lot at it, and an opponent weighs a pink-slip challenge
    /// with it. Before, each of those had its own sum, so a car at condition 0.5 was worth 0.8 of its base price
    /// on a lot, 0.5 in an opponent's hands, 0.4 when relisted and 1.0 to the challenge logic.
    ///
    /// Worth is the profile's base price (the car in perfect shape with its factory engine) times how straight
    /// the car is, plus part of what has been put into the engine: money put into an engine never comes back in
    /// full.
    /// </summary>
    public static class CarValuation
    {
        /// <summary>Share of the money put into an engine beyond the factory one that shows in the car's worth</summary>
        public const double ModificationsShare = 0.5;

        /// <summary>
        /// How much of its base price a car is worth in this condition: 0.5 at condition 0, 1.1 at condition 1
        /// (a car in better shape than the average one the base price stands for)
        /// </summary>
        public static decimal ConditionFactor(double condition) =>
            0.5m + (decimal)Math.Clamp(double.IsFinite(condition) ? condition : 0, 0, 1) * 0.6m;

        /// <summary>One condition for the whole car: the average of engine, gearbox, body and tyres</summary>
        public static double ConditionOf(Car car) =>
            (car.EngineHealth + car.TransmissionHealth + car.BodyCondition + car.TireCondition) / 4.0;

        /// <summary>What an engine worth <paramref name="engineWorth"/> adds over the factory one worth <paramref name="stockWorth"/></summary>
        public static decimal ModificationsValue(double engineWorth, double stockWorth)
        {
            var extra = (engineWorth - stockWorth) * ModificationsShare;
            return double.IsFinite(extra) && extra > 0 ? (decimal)Math.Min(extra, 1e9) : 0m;
        }

        /// <summary>A car of this base price in this condition, with this much put into it, to the nearest hundred</summary>
        public static decimal Value(decimal basePrice, double condition, decimal modifications = 0m) =>
            RoundToHundred(Math.Max(0m, basePrice) * ConditionFactor(condition) + modifications);

        public static decimal RoundToHundred(decimal price) => Math.Round(price / 100) * 100;

        /// <summary>
        /// What this very car is worth: its condition and, when the parts catalog is there, what has been done to
        /// its engine. The engine is weighed at new prices against the factory build's parts, scaled by the car's
        /// condition; putting the factory engine together to weigh it worn would take a dyno run.
        /// </summary>
        /// <param name="basePrice">The profile's base price of the car's model</param>
        /// <param name="parts">Null, or no catalog: the engine is taken to be the factory one</param>
        /// <param name="definition">The car's catalog entry, to find its factory engine; null for the same</param>
        public static decimal ValueOf(Car car, decimal basePrice, ICarPartsService? parts = null, CarDefinition? definition = null)
        {
            var condition = ConditionOf(car);
            return Value(basePrice, condition, ModificationsOf(car, condition, parts, definition));
        }

        private static decimal ModificationsOf(Car car, double condition, ICarPartsService? parts, CarDefinition? definition)
        {
            if (parts is not { IsAvailable: true } || definition == null || car.Engine is not { } engine) return 0m;
            if (parts.GetStockBuild(definition) is not { } stock) return 0m;

            var catalog = parts.Catalog;
            var engineNew = engine.SelfAndDescendants().Sum(p => catalog.Get(p.DefinitionId) is { } d ? PartPricing.NewPrice(d) : 0);
            var stockNew = stock.Build.Parts.Sum(p => p.Part != null && catalog.Get(p.Part) is { } d ? PartPricing.NewPrice(d) : 0);

            var shape = Math.Clamp(double.IsFinite(condition) ? condition : 0, 0, 1);
            return ModificationsValue(engineNew * shape, stockNew * shape);
        }
    }
}
