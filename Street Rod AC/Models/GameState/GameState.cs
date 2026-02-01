using LiteDB;

namespace Street_Rod_AC.Models.GameState
{
    public class GameState
    {
        // Game time constants
        public const int StartingYear = 1970;
        public const int StartingMonth = 6;  // June
        public const int StartingDay = 1;
        public const int DayStartHour = 8;   // 8:00 AM
        public const int DayEndHour = 22;    // 10:00 PM

        /// <summary>
        /// Gets the default starting date/time for a new game
        /// </summary>
        public static DateTime GetStartingDateTime() =>
            new DateTime(StartingYear, StartingMonth, StartingDay, DayStartHour, 0, 0);

        [BsonId]
        public int Id { get; set; } = 1; // Single record per save file

        // Time System - Game world time (starts in 1963)
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

        // Used Car Market System
        public List<UsedCarListing> UsedCarMarket { get; set; }
        public List<DealerLocation> DealerLocations { get; set; }

        // Scheduled Tasks
        public List<ScheduledTaskState> ScheduledTasks { get; set; }

        // Career Progression
        public CareerState Career { get; set; }

        // Metadata
        public int CatalogVersion { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastPlayedDate { get; set; }

        // Save file name (not persisted in DB, set at runtime)
        [BsonIgnore]
        public string SaveName { get; set; } = string.Empty;

        public GameState()
        {
            var gameStart = GetStartingDateTime();
            var now = DateTime.Now;

            // Game world time (1963)
            Date = gameStart;
            LastHour = gameStart.Hour;
            LastDay = gameStart.DayOfYear;
            LastWeek = GetWeekOfYear(gameStart);

            Player = new Player("Player");
            Racers = new RacerCollection();
            UsedCars = [];
            UsedParts = [];
            NewspaperAds = new NewspaperAds();

            UsedCarMarket = [];
            DealerLocations = [];
            ScheduledTasks = [];
            Career = CareerState.CreateNew();

            CatalogVersion = 1;
            // Real-world timestamps for save file metadata
            CreatedDate = now;
            LastPlayedDate = now;
        }

        public static GameState CreateNew(string playerName)
        {
            var state = new GameState
            {
                Player = new Player(playerName),
                Date = GetStartingDateTime()
            };

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
