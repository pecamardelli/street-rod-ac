using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
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
    /// the car is and a little for its past (<see cref="HistoryFactor"/>), plus part of what has been put into the
    /// engine: money put into an engine never comes back in full.
    /// </summary>
    public static class CarValuation
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Market");

        /// <summary>Share of the money put into an engine beyond the factory one that shows in the car's worth</summary>
        public const double ModificationsShare = 0.5;

        /// <summary>
        /// How much of its base price a car is worth in this condition: 0.5 at condition 0, 1.1 at condition 1
        /// (a car in better shape than the average one the base price stands for)
        /// </summary>
        public static decimal ConditionFactor(double condition) =>
            0.5m + (decimal)Unit.Clamp01(condition, ifNotFinite: 0) * 0.6m;

        /// <summary>One condition for the whole car: the average of engine, gearbox, body and tyres</summary>
        public static double ConditionOf(Car car) =>
            (car.EngineHealth + car.TransmissionHealth + car.BodyCondition + car.TireCondition) / 4.0;

        /// <summary>What an engine worth <paramref name="engineWorth"/> adds over the factory one worth <paramref name="stockWorth"/></summary>
        public static decimal ModificationsValue(double engineWorth, double stockWorth)
        {
            var extra = (engineWorth - stockWorth) * ModificationsShare;
            return double.IsFinite(extra) && extra > 0 ? (decimal)Math.Min(extra, 1e9) : 0m;
        }

        /// <summary>
        /// A car of this base price in this condition, with this much put into it, to the nearest hundred.
        /// <paramref name="history"/> is <see cref="HistoryFactor"/>: it moves the car's worth, not what was put into the engine.
        /// </summary>
        public static decimal Value(decimal basePrice, double condition, decimal modifications = 0m, decimal history = 1m) =>
            RoundToHundred(Math.Max(0m, basePrice) * ConditionFactor(condition) * history + modifications);

        /// <summary>What wins add to a car's worth: a point each, two for a car won with it, at most <see cref="MaxHistoryShare"/></summary>
        public const decimal WinShare = 0.01m;
        public const decimal PinkSlipWinShare = 0.02m;

        /// <summary>What an owner beyond the first <see cref="OwnersWithoutPenalty"/> takes off, at most <see cref="MaxOwnersPenalty"/></summary>
        public const decimal OwnerPenalty = 0.02m;
        public const int OwnersWithoutPenalty = 2;
        public const decimal MaxOwnersPenalty = 0.08m;

        /// <summary>A point off for every <see cref="MileageStepKm"/> past <see cref="MileageWithoutPenaltyKm"/>, at most <see cref="MaxMileagePenalty"/></summary>
        public const double MileageWithoutPenaltyKm = 80_000;
        public const double MileageStepKm = 10_000;
        public const decimal MileagePenalty = 0.01m;
        public const decimal MaxMileagePenalty = 0.08m;

        /// <summary>The most a car's history moves its worth, either way</summary>
        public const decimal MaxHistoryShare = 0.15m;

        /// <summary>
        /// What a car's past does to its worth, as a factor on it: wins (a car that took pink slips most) put up to 15%
        /// on, a car that has been through many hands or has a lot of miles loses up to 15%. The car's condition still
        /// says far more (<see cref="ConditionFactor"/>). A car nobody knows anything about (null, an older save) is 1.
        /// </summary>
        public static decimal HistoryFactor(CarHistory? history, double odometerKm)
        {
            var bonus = 0m;
            var penalty = 0m;

            if (history != null)
            {
                bonus = Math.Min(MaxHistoryShare, Math.Max(0, history.Wins) * WinShare + Math.Max(0, history.PinkSlipsWon) * PinkSlipWinShare);
                penalty += Math.Min(MaxOwnersPenalty, Math.Max(0, history.OwnerCount - OwnersWithoutPenalty) * OwnerPenalty);
            }

            if (double.IsFinite(odometerKm) && odometerKm > MileageWithoutPenaltyKm)
            {
                var steps = Math.Floor((odometerKm - MileageWithoutPenaltyKm) / MileageStepKm);
                penalty += Math.Min(MaxMileagePenalty, (decimal)Math.Min(steps, 1000) * MileagePenalty);
            }

            return 1m + Math.Clamp(bonus - penalty, -MaxHistoryShare, MaxHistoryShare);
        }

        /// <summary>The factor of this very car's history on its worth</summary>
        public static decimal HistoryFactor(Car car) => HistoryFactor(car.History, car.OdometerKM);

        public static decimal RoundToHundred(decimal price) => Math.Round(price / 100) * 100;

        /// <summary>
        /// What this very car is worth, the one way every part of the game asks: its model's base price (see
        /// <see cref="ValueOf"/>), or, for a model the catalog has no price for, what was paid for the car.
        /// </summary>
        /// <param name="profileOf">The profile of a car definition id</param>
        /// <param name="definitionOf">The catalog entry of a car definition id; only asked for a priced model</param>
        /// <param name="stockBuildOf">The factory build of a catalog entry; by default the parts service's</param>
        public static decimal WorthOf(Car car, Func<string, CarProfile?> profileOf, Func<string, CarDefinition?> definitionOf,
            ICarPartsService? parts, Func<CarDefinition, RatedBuild?>? stockBuildOf = null)
        {
            var profile = profileOf(car.DefinitionId);
            if (profile == null || profile.BasePrice <= 0) return PaidFor(car);

            return ValueOf(car, profile.BasePrice, parts, definitionOf(car.DefinitionId), stockBuildOf);
        }

        /// <summary>A car nobody can price is worth what was paid for it: the best guess there is</summary>
        public static decimal PaidFor(Car car) => RoundToHundred(car.PurchasePrice);

        /// <summary>
        /// What this very car is worth: its condition and, when the parts catalog is there, what has been done to
        /// its engine. The engine is weighed at new prices against the factory build's parts, scaled by the car's
        /// condition; putting the factory engine together to weigh it worn would take a dyno run.
        /// </summary>
        /// <param name="basePrice">The profile's base price of the car's model</param>
        /// <param name="parts">Null, or no catalog: the engine is taken to be the factory one</param>
        /// <param name="definition">The car's catalog entry, to find its factory engine; null for the same</param>
        /// <param name="stockBuildOf">The factory build of a catalog entry; by default the parts service's</param>
        public static decimal ValueOf(Car car, decimal basePrice, ICarPartsService? parts = null, CarDefinition? definition = null,
            Func<CarDefinition, RatedBuild?>? stockBuildOf = null)
        {
            var condition = ConditionOf(car);
            return Value(basePrice, condition, ModificationsOf(car, condition, parts, definition, stockBuildOf), HistoryFactor(car));
        }

        /// <summary>Worked on: the engine is not made of the factory build's parts</summary>
        public static bool IsModified(PartInstance engine, RatedBuild stock)
        {
            var factory = stock.Build.Parts.Where(p => p.Part != null).Select(p => p.Part!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return !engine.SelfAndDescendants().All(p => factory.Contains(p.DefinitionId));
        }

        /// <summary>
        /// What has been put into <paramref name="engine"/> beyond the factory build that shows in the car's worth:
        /// its parts' new prices against the factory build's, scaled by the car's condition. The one rule for a
        /// worked-on engine, on a lot and in anybody's hands.
        /// </summary>
        public static decimal EngineModifications(PartsCatalog catalog, PartInstance engine, RatedBuild stock, double condition)
        {
            var engineNew = engine.SelfAndDescendants().Sum(p => catalog.Get(p.DefinitionId) is { } d ? PartPricing.NewPrice(d) : 0);
            var stockNew = stock.Build.Parts.Sum(p => p.Part != null && catalog.Get(p.Part) is { } d ? PartPricing.NewPrice(d) : 0);

            var shape = Unit.Clamp01(condition, ifNotFinite: 0);
            return ModificationsValue(engineNew * shape, stockNew * shape);
        }

        /// <remarks>
        /// Parts work (the factory build, the parts catalog) can fail on a bad part. Worth is asked for in places
        /// that must not fail over it - a pink-slip challenge in the diner, a race nobody watches - so a failure
        /// counts the engine as the factory one, the way a listing whose engine could not be described still sells.
        /// </remarks>
        private static decimal ModificationsOf(Car car, double condition, ICarPartsService? parts, CarDefinition? definition,
            Func<CarDefinition, RatedBuild?>? stockBuildOf)
        {
            try
            {
                if (parts is not { IsAvailable: true } || definition == null || car.Engine is not { } engine) return 0m;
                if ((stockBuildOf ?? parts.GetStockBuild)(definition) is not { } stock) return 0m;

                return EngineModifications(parts.Catalog, engine, stock, condition);
            }
            catch (Exception ex)
            {
                Logger.Warning("Could not weigh the engine of a {CarId}; valued as the factory one: {Error}", car.DefinitionId, ex.Message);
                return 0m;
            }
        }
    }
}
