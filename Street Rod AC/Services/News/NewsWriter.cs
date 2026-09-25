using Street_Rod_AC.Helpers;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.News
{
    /// <summary>What happened in one of the player's races, as the paper needs it</summary>
    public sealed record PlayerRaceFacts
    {
        public DateTime Date { get; init; }
        public string PlayerName { get; init; } = string.Empty;
        public string RivalName { get; init; } = string.Empty;

        /// <summary>"his", "her", "their" for the rival (<see cref="Opponents.OpponentRules.Possessive"/>)</summary>
        public string RivalPossessive { get; init; } = "their";
        public bool RivalIsKing { get; init; }
        public int PlayerReputation { get; init; }
        public int RivalReputation { get; init; }
        public string PlayerCar { get; init; } = string.Empty;
        public string RivalCar { get; init; } = string.Empty;
        public bool IsDrag { get; init; }

        /// <summary>The race had a winner</summary>
        public bool Decided { get; init; }
        public bool PlayerWon { get; init; }
        public bool PinkSlip { get; init; }
        public decimal CashWager { get; init; }

        /// <summary>The loser wrecked their car (<see cref="Race.WinCondition.PlayerCrashed"/>, <see cref="Race.WinCondition.OpponentCrashed"/>)</summary>
        public bool PlayerCrashed { get; init; }
        public bool RivalCrashed { get; init; }
        public bool PlayerBusted { get; init; }
        public bool RivalBusted { get; init; }
        public bool PlayerEscaped { get; init; }

        /// <summary>The event the race was for; null for a race at the diner</summary>
        public string? EventName { get; init; }

        /// <summary>The rival wanted this rematch (<see cref="Grudge"/>)</summary>
        public bool Rematch { get; init; }

        /// <summary>The rival wants the car back now</summary>
        public bool RivalSwearsRevenge { get; init; }

        /// <summary>The player's first win</summary>
        public bool FirstWin { get; init; }
    }

    /// <summary>
    /// The newspaper's race pages (<see cref="GameState.News"/>): a piece is written when a race is settled, and only
    /// when there is a story in it. The player's pink slips, the King, the police and wrecks make the paper; a
    /// run-of-the-mill cash race does not. From the rivals' own races only pink slips and wrecks do. The paper prints
    /// the last <see cref="DaysPrinted"/> days, newest and weightiest first.
    /// </summary>
    public static class NewsWriter
    {
        /// <summary>How far back the paper goes</summary>
        public const int DaysPrinted = 3;

        /// <summary>Pieces on the page</summary>
        public const int OnThePage = 5;

        /// <summary>How long a piece is kept at all</summary>
        public const int DaysKept = 14;

        public const int MaxKept = 40;

        /// <summary>A cash race worth writing about, at the least</summary>
        public const decimal BigWager = 1000m;

        /// <summary>How much higher the rival's name has to be for the player's win to be an upset</summary>
        public const int UpsetReputation = 20;

        /// <summary>The piece about one of the player's races; null when there is no story in it</summary>
        public static NewsArticle? PlayerRace(PlayerRaceFacts f, Random random)
        {
            var (headline, body, weight) = Story(f, random);
            if (headline == null) return null;

            return new NewsArticle { Date = f.Date, Headline = headline, Body = body!, Weight = weight };
        }

        private static (string? Headline, string? Body, int Weight) Story(PlayerRaceFacts f, Random random)
        {
            var p = f.PlayerName;
            var r = f.RivalName;
            var where = f.IsDrag ? "at the strip" : "on the road course";

            // The police first: a bust is the story, whatever else happened
            if (f.PlayerBusted)
            {
                return (Pick(random, $"{p} Nabbed After Street Race", $"Police Break Up Race; {p} Arrested"),
                    $"Officers broke up a race between {p} and {r} and took {p} in. The {f.PlayerCar} went to the police impound."
                    + (f.RivalBusted ? $" {r} didn't get away either." : $" {r} got away."), 50);
            }

            if (f.PlayerEscaped)
            {
                return (Pick(random, $"{p} Gives Police the Slip", "Racer Outruns Patrol Cars"),
                    $"Patrol cars chased {p}'s {f.PlayerCar} after a race against {r}, and came back empty-handed."
                    + (f.RivalBusted ? $" {r} was not so lucky." : ""), 50);
            }

            if (!f.Decided) return (null, null, 0);

            if (f.RivalIsKing)
            {
                return f.PlayerWon
                    ? (Pick(random, "The King Is Dead", $"{p} Dethrones the King", "A New King on the Street"),
                        $"{r} lost for the first time in anybody's memory. {p} beat him {where} and drove off with his {f.RivalCar}.", 100)
                    : (Pick(random, "The King Keeps His Crown", $"{r} Sends {p} Home on Foot"),
                        $"{p} took a shot at {r} and lost, and the {f.PlayerCar} went with it. The King's record grows.", 80);
            }

            if (f.Rematch && f.PinkSlip)
            {
                return f.PlayerWon
                    ? (Pick(random, $"{r} Loses Again", $"No Revenge for {r}"),
                        $"{r} wanted a rematch with {p} and got one. It went the same way: {p} won {where} and took the {f.RivalCar} too."
                        + Revenge(f), 70)
                    : (Pick(random, $"{r} Gets Even", "Sweet Revenge on the Street", $"{r} Settles the Score"),
                        $"{r} had been waiting for a rematch since {p} took {f.RivalPossessive} car. {r} won it {where}, and now drives off in {PossessiveOf(p)} {f.PlayerCar}.", 70);
            }

            if (f.PinkSlip)
            {
                return f.PlayerWon
                    ? (Pick(random, $"{p} Takes {PossessiveOf(r)} Pink Slip", $"{r} Walks Home", "Pink Slips Change Hands"),
                        $"{p} raced {r} for pink slips {where} and won. {r} signed over the {f.RivalCar}." + Crash(f) + Revenge(f), 60)
                    : (Pick(random, $"{p} Loses {Short(f.PlayerCar)} to {r}", $"{r} Takes {PossessiveOf(p)} Car"),
                        $"{p} put the {f.PlayerCar} up against {r} for pink slips {where}, and lost. The car is {PossessiveOf(r)} now." + Crash(f), 60);
            }

            // An event won is the story even when the rival wrecked on the way
            if (f.EventName != null && f.PlayerWon)
            {
                return (Pick(random, $"{p} Wins the {f.EventName}", $"{f.EventName} Goes to {p}"),
                    $"{p} beat {r} {where} in the {f.PlayerCar} to win the {f.EventName}."
                    + (f.RivalCrashed ? $" {r} finished it off the road." : ""), 45);
            }

            if (f.PlayerCrashed || f.RivalCrashed)
            {
                var who = f.PlayerCrashed ? p : r;
                var car = f.PlayerCrashed ? f.PlayerCar : f.RivalCar;

                // The player's own car is "the" car in a headline: the paper does not know the player as him or her
                var whose = f.PlayerCrashed ? "the" : Capitalized(f.RivalPossessive);
                return (Pick(random, $"{who} Wrecks {whose} {Short(car)}", "Race Ends in a Wreck"),
                    $"A race between {p} and {r} ended with {PossessiveOf(who)} {car} off the road. {(f.PlayerWon ? p : r)} took the win.", 40);
            }

            if (f.PlayerWon && f.RivalReputation - f.PlayerReputation >= UpsetReputation)
            {
                return (Pick(random, $"Upset: {p} Beats {r}", $"Nobody Saw {p} Coming"),
                    $"{r} was supposed to win this one. {p}'s {f.PlayerCar} had other ideas {where}.", 35);
            }

            if (f.CashWager >= BigWager)
            {
                var winner = f.PlayerWon ? p : r;
                return (Pick(random, $"${f.CashWager:N0} Race Goes to {winner}", "Big Money on the Street"),
                    $"{p} and {r} raced {where} for ${f.CashWager:N0}. {winner} collected.", 30);
            }

            if (f.FirstWin && f.PlayerWon)
            {
                return (Pick(random, "New Face on the Street", $"{p} Gets a First Win"),
                    $"A newcomer, {p}, beat {r} {where} in a {f.PlayerCar}. Keep an eye on this one.", 25);
            }

            return (null, null, 0);
        }

        private static string Crash(PlayerRaceFacts f) =>
            f.PlayerCrashed ? $" The {f.PlayerCar} left in pieces."
            : f.RivalCrashed ? $" What's left of the {f.RivalCar}, that is."
            : "";

        private static string Revenge(PlayerRaceFacts f) =>
            f.RivalSwearsRevenge ? $" {f.RivalName} was heard saying this isn't over." : "";

        /// <summary>The piece about a race between two rivals; null when there is no story in it</summary>
        /// <param name="loserPossessive">"his", "her", "their" for the loser (<see cref="Opponents.OpponentRules.Possessive"/>)</param>
        public static NewsArticle? RivalRace(DateTime date, string winner, string loser, string loserPossessive, string loserCar, bool isDrag,
            bool pinkSlip, bool loserCrashed, int carWins, Random random)
        {
            var whose = Capitalized(loserPossessive);
            var where = isDrag ? "at the strip" : "on the road";
            var record = carWins >= 5 ? $" The car had {carWins} wins to its name." : "";

            if (pinkSlip)
            {
                return new NewsArticle
                {
                    Date = date,
                    Weight = 30,
                    Headline = loserCrashed
                        ? Pick(random, $"{winner} Tows Away {PossessiveOf(loser)} Wreck", "Pink-Slip Race Ends in a Wreck")
                        : Pick(random, $"{winner} Takes {PossessiveOf(loser)} {Short(loserCar)}", $"{loser} Loses {whose} Ride"),
                    Body = loserCrashed
                        ? $"{loser} wrecked the {loserCar} racing {winner} for pink slips {where}. {winner} towed away what was left.{record}"
                        : $"{winner} beat {loser} for pink slips {where}, and drives the {loserCar} home.{record}"
                };
            }

            if (loserCrashed)
            {
                return new NewsArticle
                {
                    Date = date,
                    Weight = 15,
                    Headline = Pick(random, $"{loser} Wrecks {whose} {Short(loserCar)}", "Another Wreck on the Street"),
                    Body = $"{loser} put the {loserCar} into something hard racing {winner} {where}."
                };
            }

            return null;
        }

        /// <summary>The new pieces after what was written before; old ones go</summary>
        public static void Add(GameState gameState, DateTime date, IEnumerable<NewsArticle> articles)
        {
            gameState.News ??= [];
            DatedLog.Append(gameState.News, articles, a => a.Date, date, DaysKept, MaxKept);
        }

        /// <summary>What the paper prints on <paramref name="today"/>: the last few days, newest day first, the weightiest first within a day</summary>
        public static List<NewsArticle> FrontPage(IEnumerable<NewsArticle>? news, DateTime today) =>
            (news ?? [])
                .Where(a => a.Date.Date <= today.Date && (today.Date - a.Date.Date).TotalDays < DaysPrinted)
                .Select((a, i) => (Article: a, Order: i))
                .OrderByDescending(x => x.Article.Date.Date)
                .ThenByDescending(x => x.Article.Weight)
                .ThenByDescending(x => x.Order)
                .Select(x => x.Article)
                .Take(OnThePage)
                .ToList();

        private static string Pick(Random random, params string[] choices) => choices[random.Next(choices.Length)];

        private static string PossessiveOf(string name) => name.EndsWith('s') ? name + "'" : name + "'s";

        private static string Capitalized(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

        /// <summary>A car as a headline says it: the model without the year</summary>
        private static string Short(string car)
        {
            var space = car.IndexOf(' ');
            return space > 0 && int.TryParse(car[..space], out _) ? car[(space + 1)..] : car;
        }
    }
}
