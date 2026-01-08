using LiteDB;

namespace Street_Rod_AC.Models.GameState
{
    public class GameState
    {
        [BsonId]
        public int Id { get; set; } = 1; // Single record per save file

        // Time System (matching GameMaker)
        public DateTime Date { get; set; }
        public int LastHour { get; set; }
        public int LastDay { get; set; }
        public int LastWeek { get; set; }

        // Player
        public Player Player { get; set; }

        // AI Racers (categorized like GameMaker)
        public RacerCollection Racers { get; set; }

        // Markets
        public List<Car> UsedCars { get; set; }
        public List<Part> UsedParts { get; set; }
        public NewspaperAds NewspaperAds { get; set; }

        // Metadata
        public int CatalogVersion { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastPlayedDate { get; set; }

        public GameState()
        {
            var now = DateTime.Now;
            Date = now;
            LastHour = now.Hour;
            LastDay = now.DayOfYear;
            LastWeek = GetWeekOfYear(now);

            Player = new Player("Player");
            Racers = new RacerCollection();
            UsedCars = new List<Car>();
            UsedParts = new List<Part>();
            NewspaperAds = new NewspaperAds();

            CatalogVersion = 1;
            CreatedDate = now;
            LastPlayedDate = now;
        }

        public static GameState CreateNew(string playerName)
        {
            var state = new GameState();
            state.Player = new Player(playerName);

            // TODO: Initialize used car market
            // TODO: Initialize used parts market
            // TODO: Initialize AI racers
            // TODO: Initialize newspaper ads

            return state;
        }

        private static int GetWeekOfYear(DateTime date)
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var calendar = culture.Calendar;
            var weekRule = culture.DateTimeFormat.CalendarWeekRule;
            var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
            return calendar.GetWeekOfYear(date, weekRule, firstDayOfWeek);
        }

        public void UpdateTime(DateTime newDate)
        {
            Date = newDate;
            LastHour = newDate.Hour;
            LastDay = newDate.DayOfYear;
            LastWeek = GetWeekOfYear(newDate);
            LastPlayedDate = DateTime.Now;
        }
    }
}
