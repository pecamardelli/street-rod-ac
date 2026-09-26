using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Market;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// The rivals' cars in the classifieds (<see cref="NewspaperAds.RivalCars"/>). A rival advertises a spare
    /// (<see cref="OpponentLifeService"/>); the player buys it off the paper's used car pages as they would a car off a
    /// lot, and other rivals buy it as they would one off a lot. The car stays in the seller's garage until then, so
    /// an ad is only good while the seller still has the car and it is not the one they drive.
    /// </summary>
    public static class RivalCarAds
    {
        /// <summary>A live ad with the rival and the car it is for</summary>
        public sealed record Offer(RivalCarAd Ad, Opponent Seller, Car Car);

        /// <summary>The ads that still stand: the seller is on the scene and has the car, and it is not in the impound</summary>
        public static List<Offer> Live(GameState gameState)
        {
            var offers = new List<Offer>();
            foreach (var ad in gameState.NewspaperAds.RivalCars ?? [])
            {
                if (gameState.Racers.Find(ad.RivalName) is Opponent seller && Stands(ad, seller) is { } offer) offers.Add(offer);
            }

            return offers;
        }

        /// <summary>The ads of one seller that still stand, without looking up everybody else's</summary>
        public static List<Offer> LiveOf(GameState gameState, Opponent seller) =>
        [
            .. (gameState.NewspaperAds.RivalCars ?? [])
                .Where(a => string.Equals(a.RivalName, seller.Name, StringComparison.Ordinal))
                .Select(a => Stands(a, seller))
                .OfType<Offer>()
        ];

        private static Offer? Stands(RivalCarAd ad, Opponent seller)
        {
            var car = seller.Cars.FirstOrDefault(c => c.InstanceId == ad.CarInstanceId);
            return car == null || car.IsImpounded || ReferenceEquals(car, seller.Cars[0]) ? null : new Offer(ad, seller, car);
        }

        /// <summary>The cars of theirs in the paper, by instance id, whether the ad stands or not</summary>
        public static HashSet<Guid> AdvertisedBy(GameState gameState, Opponent seller) =>
        [
            .. (gameState.NewspaperAds.RivalCars ?? [])
                .Where(a => string.Equals(a.RivalName, seller.Name, StringComparison.Ordinal))
                .Select(a => a.CarInstanceId)
        ];

        /// <summary>The ad for this car of theirs, if there is one</summary>
        public static RivalCarAd? AdFor(GameState gameState, Car car) =>
            gameState.NewspaperAds.RivalCars?.FirstOrDefault(a => a.CarInstanceId == car.InstanceId);

        /// <param name="engineSummary">What the seller says about the engine, worked out once here: a spare in the
        /// paper is not driven or worked on, so it holds for as long as the ad runs</param>
        public static RivalCarAd Post(GameState gameState, Opponent seller, Car car, decimal askingPrice, DateTime date,
            string? engineSummary = null, bool isModified = false)
        {
            gameState.NewspaperAds.RivalCars ??= [];
            var ad = new RivalCarAd
            {
                RivalName = seller.Name,
                CarInstanceId = car.InstanceId,
                AskingPrice = Math.Max(1m, askingPrice),
                PostedDate = date,
                EngineSummary = engineSummary,
                IsModified = isModified
            };
            gameState.NewspaperAds.RivalCars.Add(ad);
            return ad;
        }

        /// <summary>Ads whose seller or car is gone, or whose car is back in use, come down</summary>
        public static int Tidy(GameState gameState)
        {
            var live = Live(gameState).Select(o => o.Ad).ToHashSet();
            return gameState.NewspaperAds.RivalCars?.RemoveAll(a => !live.Contains(a)) ?? 0;
        }

        /// <summary>
        /// The car changes hands at the asking price: out of the seller's garage, the money to the seller, the ad down.
        /// The buyer's money and garage are the caller's to see to. Null when the ad no longer stands.
        /// </summary>
        public static Car? HandOver(GameState gameState, RivalCarAd ad, string buyerName, DateTime date)
        {
            var offer = Live(gameState).FirstOrDefault(o => o.Ad.AdId == ad.AdId);
            if (offer == null) return null;

            var (_, seller, car) = offer;
            seller.Cars.Remove(car);
            seller.Money += ad.AskingPrice;
            seller.Stats.CarsSold++;
            gameState.NewspaperAds.RivalCars.Remove(ad);

            car.PurchasePrice = ad.AskingPrice;
            car.PurchaseDate = date;
            car.History ??= new CarHistory();
            car.History.ChangeHands(buyerName, date, CarAcquisition.PrivateSale);
            return car;
        }

        /// <summary>
        /// The ad as the paper's used car pages show it: a listing made up from the car, for the page only and never
        /// kept in the market (it shares the car's parts). Its id is the ad's. The car's own figures and what the
        /// ad says about the engine: no dyno run for it.
        /// </summary>
        public static UsedCarListing AsListing(Offer offer, IUsedCarMarketService market)
        {
            var listing = market.ListCar(offer.Car, offer.Ad.AskingPrice, string.Empty, offer.Ad.PostedDate, describeEngine: false);
            listing.Id = offer.Ad.AdId.ToString();
            listing.Price = offer.Ad.AskingPrice;
            listing.PowerHp = offer.Car.PowerHp;
            listing.EngineSummary = offer.Ad.EngineSummary;
            listing.IsModified = offer.Ad.IsModified;
            return listing;
        }
    }
}
