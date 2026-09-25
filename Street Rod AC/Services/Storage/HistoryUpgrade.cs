using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Storage
{
    /// <summary>
    /// A save from before cars kept their history (<see cref="CarHistory"/>) and the paper wrote about the races: every
    /// car gets a history with whoever has it now as its owner, so the next change of hands counts from there, and the
    /// owners nobody knows that its miles tell of (<see cref="EarlierOwnersFor"/>). A used car on a lot is not a
    /// first-owner car either.
    /// </summary>
    public static class HistoryUpgrade
    {
        public static void BringUpToDate(GameState state)
        {
            state.News ??= [];

            foreach (var car in state.Player.Cars ?? []) Own(car, state.Player.Name);

            var racers = state.Racers.All;
            foreach (var racer in racers)
                foreach (var car in racer.Cars ?? []) Own(car, racer.Name);

            foreach (var listing in state.UsedCarMarket ?? [])
            {
                listing.History ??= new CarHistory();
                listing.History.Owners ??= [];
                if (listing.History.IsBlank) listing.History.EarlierOwners = EarlierOwnersFor(listing.Mileage);
            }
        }

        private static void Own(Car car, string owner)
        {
            car.History ??= new CarHistory();
            car.History.Owners ??= [];
            if (car.History.IsBlank) car.History.EarlierOwners = EarlierOwnersFor(car.OdometerKM);
            if (car.History.Owners.Count == 0) car.History.ChangeHands(owner, car.PurchaseDate, CarAcquisition.Unknown);
        }

        /// <summary>Owners before anybody the game knows, told by the miles: none on a new car, one more every 80,000 km or so</summary>
        public static int EarlierOwnersFor(double km) =>
            !double.IsFinite(km) || km < NewCarKm ? 0 : 1 + (int)Math.Min(km / KmPerOwner, 5);

        private const double NewCarKm = 1_000;
        private const double KmPerOwner = 80_000;
    }
}
