namespace Street_Rod_AC.Models.GameState
{
    public class NewspaperAds
    {
        public List<CarAd> Cars { get; set; }
        public List<PartAd> Parts { get; set; }

        /// <summary>The player's own cars up for sale; see <see cref="Services.Market.CarSaleService"/></summary>
        public List<CarSaleAd> PlayerCars { get; set; }

        public NewspaperAds()
        {
            Cars = [];
            Parts = [];
            PlayerCars = [];
        }
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

    public class CarAd
    {
        public Guid AdId { get; set; }
        public Car Car { get; set; }
        public decimal AskingPrice { get; set; }
        public string SellerName { get; set; }
        /// <summary>Game time; set by whoever places the ad</summary>
        public DateTime PostedDate { get; set; }
        public int DaysActive { get; set; }

        public CarAd()
        {
            AdId = Guid.NewGuid();
            Car = new Car();
            AskingPrice = 0m;
            SellerName = "Unknown";
            DaysActive = 0;
        }

        public CarAd(Car car, decimal askingPrice, string sellerName) : this()
        {
            Car = car;
            AskingPrice = askingPrice;
            SellerName = sellerName;
        }
    }

    public class PartAd
    {
        public Guid AdId { get; set; }
        public PartInstance Part { get; set; }
        public decimal AskingPrice { get; set; }
        public string SellerName { get; set; }
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
