namespace Street_Rod_AC.Models.GameState
{
    public class NewspaperAds
    {
        public List<CarAd> Cars { get; set; }
        public List<PartAd> Parts { get; set; }

        public NewspaperAds()
        {
            Cars = new List<CarAd>();
            Parts = new List<PartAd>();
        }
    }

    public class CarAd
    {
        public Guid AdId { get; set; }
        public Car Car { get; set; }
        public decimal AskingPrice { get; set; }
        public string SellerName { get; set; }
        public DateTime PostedDate { get; set; }
        public int DaysActive { get; set; }

        public CarAd()
        {
            AdId = Guid.NewGuid();
            Car = new Car();
            AskingPrice = 0m;
            SellerName = "Unknown";
            PostedDate = DateTime.Now;
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
        public Part Part { get; set; }
        public decimal AskingPrice { get; set; }
        public string SellerName { get; set; }
        public DateTime PostedDate { get; set; }
        public int DaysActive { get; set; }

        public PartAd()
        {
            AdId = Guid.NewGuid();
            Part = new Part();
            AskingPrice = 0m;
            SellerName = "Unknown";
            PostedDate = DateTime.Now;
            DaysActive = 0;
        }

        public PartAd(Part part, decimal askingPrice, string sellerName) : this()
        {
            Part = part;
            AskingPrice = askingPrice;
            SellerName = sellerName;
        }
    }
}
