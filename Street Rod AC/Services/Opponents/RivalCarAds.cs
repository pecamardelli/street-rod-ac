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
                if (gameState.Racers.Find(ad.RivalName) is not Opponent seller) continue;
                var car = seller.Cars.FirstOrDefault(c => c.InstanceId == ad.CarInstanceId);
                if (car == null || car.IsImpounded || ReferenceEquals(car, seller.Cars[0])) continue;
                offers.Add(new Offer(ad, seller, car));
            }

            return offers;
        }

        /// <summary>The ad for this car of theirs, if there is one</summary>
        public static RivalCarAd? AdFor(GameState gameState, Car car) =>
            gameState.NewspaperAds.RivalCars?.FirstOrDefault(a => a.CarInstanceId == car.InstanceId);

        public static RivalCarAd Post(GameState gameState, Opponent seller, Car car, decimal askingPrice, DateTime date)
        {
            gameState.NewspaperAds.RivalCars ??= [];
            var ad = new RivalCarAd
            {
                RivalName = seller.Name,
                CarInstanceId = car.InstanceId,
                AskingPrice = Math.Max(1m, askingPrice),
                PostedDate = date
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
        /// The ad as the paper's used car pages show it: a listing made up from the car, never kept in the market.
        /// Its id is the ad's, its seller the rival.
        /// </summary>
        public static UsedCarListing AsListing(Offer offer, IUsedCarMarketService market)
        {
            var listing = market.ListCar(offer.Car, offer.Ad.AskingPrice, string.Empty, offer.Ad.PostedDate);
            listing.Id = offer.Ad.AdId.ToString();
            listing.Price = offer.Ad.AskingPrice;
            listing.PowerHp ??= offer.Car.PowerHp;
            listing.PrivateSeller = offer.Seller.Name;
            return listing;
        }

        /// <summary>The ad a made-up listing stands for (<see cref="AsListing"/>)</summary>
        public static RivalCarAd? AdOf(GameState gameState, UsedCarListing listing) =>
            listing.PrivateSeller == null || !Guid.TryParse(listing.Id, out var id)
                ? null
                : gameState.NewspaperAds.RivalCars?.FirstOrDefault(a => a.AdId == id);
    }
}
