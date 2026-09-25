using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Storage
{
    /// <summary>
    /// A save from before cars kept their history (<see cref="CarHistory"/>) and the paper wrote about the races: every
    /// car gets an empty history with whoever has it now as its owner, so the next change of hands counts from there.
    /// </summary>
    public static class HistoryUpgrade
    {
        public static void BringUpToDate(GameState state)
        {
            state.News ??= [];

            foreach (var car in state.Player.Cars ?? []) Own(car, state.Player.Name);

            var racers = state.Racers.ReadyToRace.Values.Concat(state.Racers.Retired.Values).Concat(state.Racers.Inactive.Values);
            foreach (var racer in racers)
                foreach (var car in racer.Cars ?? []) Own(car, racer.Name);

            foreach (var listing in state.UsedCarMarket ?? []) listing.History ??= new CarHistory();
        }

        private static void Own(Car car, string owner)
        {
            car.History ??= new CarHistory();
            car.History.Owners ??= [];
            if (car.History.Owners.Count == 0) car.History.ChangeHands(owner, car.PurchaseDate, CarAcquisition.Unknown);
        }
    }
}
