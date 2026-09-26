namespace Street_Rod_AC.Models.GameState
{
    public class NewspaperAds
    {
        public List<PartAd> Parts { get; set; }

        /// <summary>The player's own cars up for sale; see <see cref="Services.Market.CarSaleService"/></summary>
        public List<CarSaleAd> PlayerCars { get; set; }

        /// <summary>The rivals' spare cars up for sale; see <see cref="Services.Opponents.RivalCarAds"/></summary>
        public List<RivalCarAd> RivalCars { get; set; }

        public NewspaperAds()
        {
            Parts = [];
            PlayerCars = [];
            RivalCars = [];
        }
    }

    /// <summary>
    /// A rival's car in the classifieds. As with the player's own ads the car stays in the rival's garage until
    /// somebody buys it; the ad names the rival and the car.
    /// </summary>
    public class RivalCarAd
    {
        public Guid AdId { get; set; } = Guid.NewGuid();
        public string RivalName { get; set; } = string.Empty;
        public Guid CarInstanceId { get; set; }
        public decimal AskingPrice { get; set; }

        /// <summary>Game time</summary>
        public DateTime PostedDate { get; set; }

        /// <summary>The price came down once already</summary>
        public bool Reduced { get; set; }

        /// <summary>What the seller says about the engine, as a lot's listing does; null when they had nothing to say</summary>
        public string? EngineSummary { get; set; }

        /// <summary>Somebody has been at the engine</summary>
        public bool IsModified { get; set; }
    }

    /// <summary>
    /// One of the player's cars in the classifieds. The car stays in the garage (and can still race) until a
    /// buyer's offer is taken; the ad names it by its instance id.
    /// </summary>
    public class CarSaleAd
    {
        public Guid AdId { get; set; } = Guid.NewGuid();
        public Guid CarInstanceId { get; set; }
        public decimal AskingPrice { get; set; }

        /// <summary>Game time</summary>
        public DateTime PostedDate { get; set; }

        /// <summary>The buyer on the phone, if there is one; a later caller takes the place of one who gave up</summary>
        public CarOffer? Offer { get; set; }
    }

    /// <summary>What a buyer who answered an ad would pay, and until when</summary>
    public class CarOffer
    {
        public string BuyerName { get; set; } = string.Empty;

        /// <summary>The rival who called, when the buyer is one of the racers: the car then races under them</summary>
        public string? RivalName { get; set; }
        public decimal Amount { get; set; }

        /// <summary>Game time; after it the buyer has found another car</summary>
        public DateTime Expires { get; set; }
    }

    public class PartAd
    {
        public Guid AdId { get; set; }
        public PartInstance Part { get; set; }
        public decimal AskingPrice { get; set; }
        public string SellerName { get; set; }

        /// <summary>
        /// The rival selling it, when the part came off one of their cars: they are paid when it sells, and the shop's
        /// trade-in when nobody wants it. Null for the paper's other sellers.
        /// </summary>
        public string? SellerRival { get; set; }

        /// <summary>Game time; set by whoever places the ad</summary>
        public DateTime PostedDate { get; set; }
        public int DaysActive { get; set; }

        public PartAd()
        {
            AdId = Guid.NewGuid();
            Part = new PartInstance();
            AskingPrice = 0m;
            SellerName = "Unknown";
            DaysActive = 0;
        }

        public PartAd(PartInstance part, decimal askingPrice, string sellerName) : this()
        {
            Part = part;
            AskingPrice = askingPrice;
            SellerName = sellerName;
        }
    }
}
