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

        // Player
        public Player Player { get; set; }

        // AI Racers (categorized like GameMaker)
        public RacerCollection Racers { get; set; }

        // Markets
        public List<Car> UsedCars { get; set; }
        public List<PartInstance> UsedParts { get; set; }
        public NewspaperAds NewspaperAds { get; set; }

        // Used Car Market System
        public List<UsedCarListing> UsedCarMarket { get; set; }
        public List<DealerLocation> DealerLocations { get; set; }

        // Scheduled Tasks
        public List<ScheduledTaskState> ScheduledTasks { get; set; }

        /// <summary>What the racers have been up to, newest last: the diner's "word on the street"</summary>
        public List<StreetTalkItem> StreetTalk { get; set; }

        // Career Progression
        public CareerState Career { get; set; }

        /// <summary>The difficulty the career was started on; fixed for the life of the save</summary>
        public GameRules Rules { get; set; }

        /// <summary>
        /// The race the player went off to and has not come back from yet: set just before AC starts and saved
        /// with the game, cleared once its result (or the lack of one) has been dealt with. A result file left
        /// over from a crash is only ever applied to the save that holds its race.
        /// </summary>
        public Models.Race.RaceContext? PendingRace { get; set; }

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

            Player = new Player("Player");
            Racers = new RacerCollection();
            UsedCars = [];
            UsedParts = [];
            NewspaperAds = new NewspaperAds();

            UsedCarMarket = [];
            DealerLocations = [];
            ScheduledTasks = [];
            StreetTalk = [];
            Career = CareerState.CreateNew();
            Rules = new GameRules();

            CatalogVersion = 1;
            // Real-world timestamps for save file metadata
            CreatedDate = now;
            LastPlayedDate = now;
        }

        public static GameState CreateNew(string playerName, GameRules? rules = null)
        {
            var state = new GameState
            {
                Player = new Player(playerName),
                Date = GetStartingDateTime(),
                Rules = rules ?? new GameRules()
            };

            // TODO: Initialize used car market
            // TODO: Initialize used parts market
            // TODO: Initialize AI racers
            // TODO: Initialize newspaper ads

            return state;
        }

        public void UpdateTime(DateTime newDate)
        {
            Date = newDate;
            LastPlayedDate = DateTime.Now;
        }
    }
}
