using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Career.Victory;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Police;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>The rivals' day off the track: see <see cref="OpponentLifeService"/></summary>
    public interface IOpponentLifeService
    {
        /// <summary>Every rival looks after their car, their money and their place on the street; see the class</summary>
        Task ReviewDayAsync(GameState gameState, DateTime currentDate);
    }

    /// <summary>
    /// The rivals live like the player does. Every morning each racer (the GameMaker version's scr_review_racers):
    /// <list type="bullet">
    /// <item>drives the best car they own, and sells what they don't need: a wreck for scrap, a spare over
    /// <see cref="OpponentRules.MaxCars"/> through the paper (<see cref="RivalCarAds"/>), to a dealer when nobody calls;</item>
    /// <item>without a car, buys one off the same lots the player buys from, or out of another rival's ad;</item>
    /// <item>with money to spare, now and then trades up to a clearly stronger car and puts the old one in the paper;</item>
    /// <item>with a car that can't race, has it repaired if the whole bill is affordable, else sells it and buys
    /// another if that gets them racing, else sits it out (retired) until they can;</item>
    /// <item>broke, scrapes some money together (<see cref="OpponentRules.IsBankrupt"/>) and comes back;</item>
    /// <item>now and then gives up the scene for good (<see cref="OpponentRules.Leaves"/>): sells up and leaves town.</item>
    /// </list>
    /// Then more racers come out onto the street as the weeks go by (<see cref="OpponentRules.MinActive"/>), new faces
    /// first and, once there are none left, racers who left long ago; and a few with money to spare tune their engines
    /// (<see cref="EngineTuner"/>), buying used out of the paper when it asks less and selling what came off there.
    /// What the street notices goes into <see cref="GameState.StreetTalk"/>.
    ///
    /// The King lives the same way, but stays out of sight (inactive) until the player has earned a shot at him.
    ///
    /// Dyno runs and tuning happen off the UI thread, on copies of the engines; the save only changes on the
    /// thread that called.
    /// </summary>
    public class OpponentLifeService(
        ICarPartsService parts,
        IUsedCarMarketService market,
        IContentCatalogRepository catalogRepo,
        IOpponentInitializationService? initialization = null,
        Random? random = null) : IOpponentLifeService
    {
        /// <summary>At most this many racers tune their engines in a day (each is a few dozen dyno runs)</summary>
        public const int MaxTunersPerDay = 3;

        /// <summary>The chance a racer with money to spare works on the engine on a given day: now and then, not daily</summary>
        public const double TuneChance = 0.15;

        /// <summary>How long the street keeps talking about something</summary>
        public const int StreetTalkDays = 7;

        public const int MaxStreetTalk = 40;

        private readonly ICarPartsService _parts = parts;
        private readonly IUsedCarMarketService _market = market;
        private readonly IContentCatalogRepository _catalogRepo = catalogRepo;
        private readonly IOpponentInitializationService? _initialization = initialization;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("OpponentLife");
        private readonly Random _random = random ?? new();

        // The day's catalog lookups: each model valued and named from the catalog once per review, not per car and line
        private Func<Car, decimal>? _valueOf;
        private Func<string, string>? _carNames;

        public async Task ReviewDayAsync(GameState gameState, DateTime currentDate)
        {
            _initialization?.EnsureKing(gameState);
            try
            {
                _initialization?.EnsureNewcomers(gameState);
            }
            catch (Exception ex)
            {
                // The racers on the street still have their day; the newcomers are looked for again tomorrow
                _logger.Error(ex, "Could not bring in the racers defined since this game began");
            }
            _valueOf = _market.Valuer();
            _carNames = CarNames.Book(_catalogRepo);

            var talk = new List<string>();
            var groupOf = PartGroups();

            await GiveCarsTheirPartsAsync(gameState);
            await PutOnDynoAsync(gameState);

            // Those who sat out first: they may be back today. The King is reviewed wherever he is kept.
            var racers = gameState.Racers.Retired.Values
                .Concat(gameState.Racers.ReadyToRace.Values)
                .Concat(gameState.Racers.Inactive.Values.Where(r => r is Opponent { IsKing: true }))
                .OfType<Opponent>()
                .ToList();
            RivalCarAds.Tidy(gameState);
            foreach (var racer in racers) ReviewRacer(gameState, racer, currentDate, groupOf, talk);
            RivalCarAds.Tidy(gameState);

            Activate(gameState, currentDate, groupOf, talk);

            await TuneAsync(gameState, groupOf, talk);

            // A rematch nobody came for is off. Last, next to the talk it makes: a review that fails before this
            // leaves the grudges for tomorrow's rather than dropping them without a word.
            Grudges.Lapse(gameState.Racers.All.OfType<Opponent>(), currentDate, _carNames, talk);

            AddTalk(gameState, currentDate, talk);
            _logger.Information("Rivals reviewed: {Ready} racing, {Retired} sitting out, {Inactive} not on the street yet; {Talk} thing(s) to talk about",
                gameState.Racers.ReadyToRace.Count, gameState.Racers.Retired.Count, gameState.Racers.Inactive.Count, talk.Count);
        }

        // ----- one racer -----

        private void ReviewRacer(GameState gameState, Opponent racer, DateTime date, Func<string, string?>? groupOf, List<string> talk)
        {
            try
            {
                CollectFromImpound(racer, date, talk);
                PickBestCar(racer, groupOf);
                ReviewAds(gameState, racer, date, talk);
                SellSpares(gameState, racer, date, groupOf, talk);

                if (racer.Cars.Count == 0) TryBuy(gameState, racer, date, talk);

                // A car in the impound can be neither fixed nor sold: they wait for it
                if (racer.Cars.FirstOrDefault() is { IsImpounded: false } car && !CanRace(car, groupOf)
                    && !TryRepair(gameState, racer, car, groupOf, talk))
                {
                    SellAndBuyAnother(gameState, racer, car, date, groupOf, talk);
                }

                if (OpponentRules.IsBankrupt(IsReady(racer, groupOf), racer.Money, CheapestCar(gameState)))
                {
                    // Once per spell of being broke, not once per day of it
                    if (!racer.IsBroke) racer.TimesBroke++;
                    racer.IsBroke = true;
                    if (racer.TimesBroke >= OpponentRules.MaxTimesBroke && CanLeave(gameState, racer))
                    {
                        Leave(gameState, racer, date, $"{racer.Name} went broke one time too many and quit the scene.", talk);
                        return;
                    }

                    var cash = OpponentRules.CashInjection(CheapestCar(gameState), _random.NextDouble());
                    racer.Money += cash;
                    talk.Add($"{racer.Name} is broke and scraped ${cash:N0} together to get back on the street.");
                    _logger.Information("{Racer} is broke: ${Cash} injected, now ${Money}", racer.Name, cash, racer.Money);
                }
                else
                {
                    racer.IsBroke = false;

                    // Not on the day they bought one: a car is driven a while before it is judged
                    if (IsReady(racer, groupOf) && racer.Cars[0].PurchaseDate.Date != date.Date && _random.NextDouble() < OpponentRules.TradeUpChance)
                    {
                        TryTradeUp(gameState, racer, date, groupOf, talk);
                    }
                }

                MoveToWhereTheyBelong(gameState, racer, date, groupOf, talk);
                MaybeLeave(gameState, racer, date, talk);
            }
            catch (Exception ex)
            {
                // One racer's bad car is not everybody's day
                _logger.Error(ex, "Could not review {Racer}", racer.Name);
            }
        }

        private void MoveToWhereTheyBelong(GameState gameState, Opponent racer, DateTime date, Func<string, string?>? groupOf, List<string> talk)
        {
            var ready = IsReady(racer, groupOf);
            var target = racer.IsKing
                ? ready && new KingVictory().IsUnlocked(gameState.Career) ? RacerStatus.ReadyToRace : RacerStatus.Inactive
                : ready ? RacerStatus.ReadyToRace : RacerStatus.Retired;

            // How long they have been sitting out, for whether they give up (OpponentRules.Leaves)
            racer.SittingOutSince = target == RacerStatus.Retired ? racer.SittingOutSince ?? date : null;
            if (racer.Status == target) return;

            var from = racer.Status;
            gameState.Racers.MoveRacer(racer.Name, target);

            if (racer.IsKing)
            {
                if (target == RacerStatus.ReadyToRace) talk.Add($"{racer.Name} has heard about you. He's at the diner, and he only races for pink slips.");
            }
            else if (target == RacerStatus.ReadyToRace && from == RacerStatus.Retired)
            {
                talk.Add($"{racer.Name} is back on the street.");
            }
            else if (target == RacerStatus.Retired)
            {
                talk.Add(racer.Cars.Count == 0
                    ? $"{racer.Name} has no car and is sitting it out."
                    : racer.Cars[0].IsImpounded
                        ? $"The police have {racer.Name}'s {CarName(racer.Cars[0])} in the impound; {racer.Name} is sitting it out."
                        : $"{racer.Name}'s {CarName(racer.Cars[0])} is laid up; {racer.Name} is sitting it out.");
            }

            _logger.Information("{Racer}: {From} -> {To}", racer.Name, from, target);
        }

        // ----- leaving the scene -----

        /// <summary>Laid up for too long, or just moving on: see <see cref="OpponentRules.Leaves"/></summary>
        private void MaybeLeave(GameState gameState, Opponent racer, DateTime date, List<string> talk)
        {
            if (racer.Status is not (RacerStatus.ReadyToRace or RacerStatus.Retired)) return;

            var sittingOut = racer.SittingOutSince is { } since ? (int?)(date.Date - since.Date).Days : null;
            if (!OpponentRules.Leaves(racer.TimesBroke, sittingOut, _random.NextDouble()) || !CanLeave(gameState, racer)) return;

            var why = sittingOut >= OpponentRules.LaidUpDays && racer.Cars.FirstOrDefault() is { } car
                ? $"{racer.Name} gave up on {OpponentRules.Possessive(racer)} {CarName(car)} and quit the scene."
                : $"{racer.Name}{Nickname(racer)} sold up and left town.";
            Leave(gameState, racer, date, why, talk);
        }

        /// <summary>
        /// Not the King, and not with something open with the player: a race about to start, a rematch they want, an
        /// offer on the player's car
        /// </summary>
        private static bool CanLeave(GameState gameState, Opponent racer)
        {
            if (racer.IsKing || racer.Grudge != null) return false;
            if (string.Equals(gameState.PendingRace?.OpponentName, racer.Name, StringComparison.Ordinal)) return false;
            return !gameState.NewspaperAds.PlayerCars.Any(a => string.Equals(a.Offer?.RivalName, racer.Name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Gone for good: the cars go to a dealer (a wreck for scrap; one in the impound stays with the police), their
        /// ads come down, and they leave the street (<see cref="RacerStatus.Departed"/>). The parts they put in the
        /// paper stay there; nobody is paid for them any more.
        /// </summary>
        private void Leave(GameState gameState, Opponent racer, DateTime date, string why, List<string> talk)
        {
            foreach (var car in racer.Cars.ToList())
            {
                if (car.IsImpounded) racer.Cars.Remove(car);
                else Sell(gameState, racer, car, []);
            }

            gameState.NewspaperAds.RivalCars.RemoveAll(a => a.RivalName == racer.Name);
            foreach (var ad in gameState.NewspaperAds.Parts.Where(a => a.SellerRival == racer.Name)) ad.SellerRival = null;

            racer.Grudge = null;
            racer.LeftDate = date;
            racer.SittingOutSince = null;
            gameState.Racers.MoveRacer(racer.Name, RacerStatus.Departed);

            talk.Add(why);
            _logger.Information("{Racer} left the scene: {Why}", racer.Name, why);
        }

        /// <summary>
        /// Cars whose days in the impound are up come home, when the racer can pay for them. One left there too long
        /// (<see cref="PoliceRules.AuctionAfterDays"/>) is auctioned off by the police, and it is gone.
        /// </summary>
        private void CollectFromImpound(Opponent racer, DateTime date, List<string> talk)
        {
            foreach (var car in racer.Cars.Where(c => PoliceRules.CanCollect(c, date)).ToList())
            {
                if (racer.Money >= car.ImpoundFee)
                {
                    racer.Money -= car.ImpoundFee;
                    _logger.Information("{Racer} collected the {Car} from the impound for ${Fee}", racer.Name, car.DefinitionId, car.ImpoundFee);
                    PoliceRules.Release(car);
                    talk.Add($"{racer.Name} got {OpponentRules.Possessive(racer)} {CarName(car)} back from the police impound.");
                }
                else if (PoliceRules.IsForAuction(car, date))
                {
                    racer.Cars.Remove(car);
                    _logger.Information("{Racer} never paid to collect the {Car}: the police auctioned it off", racer.Name, car.DefinitionId);
                    talk.Add($"{racer.Name} never collected {OpponentRules.Possessive(racer)} {CarName(car)}: the police auctioned it off.");
                }
            }
        }

        /// <summary>The car the racer means to drive goes to the front of their cars, where every screen looks for it</summary>
        private void PickBestCar(Opponent racer, Func<string, string?>? groupOf)
        {
            if (racer.Cars.Count < 2) return;

            var best = racer.Cars.OrderByDescending(c => Score(c, groupOf)).First();
            if (ReferenceEquals(best, racer.Cars[0])) return;

            racer.Cars.Remove(best);
            racer.Cars.Insert(0, best);
            _logger.Information("{Racer} now drives the {Car}", racer.Name, best.DefinitionId);
        }

        private double Score(Car car, Func<string, string?>? groupOf) =>
            OpponentRules.CarScore(car.PowerHp ?? 0, CarValuation.ConditionOf(car), CanRace(car, groupOf), ValueOf(car));

        /// <summary>
        /// Wrecks they don't drive go for scrap, and cars beyond the spare into the paper (a dealer takes them when
        /// nobody calls, see <see cref="ReviewAds"/>)
        /// </summary>
        private void SellSpares(GameState gameState, Opponent racer, DateTime date, Func<string, string?>? groupOf, List<string> talk)
        {
            // Cars in the impound can't be sold: with nothing else to spare they wait
            foreach (var wreck in racer.Cars.Skip(1).Where(c => !c.IsImpounded && CarCondition.IsTotaled(c)).ToList())
            {
                Sell(gameState, racer, wreck, talk);
            }

            var over = racer.Cars.Count - OpponentRules.MaxCars;
            if (over <= 0) return;

            var advertised = RivalCarAds.AdvertisedBy(gameState, racer);
            var spares = racer.Cars.Skip(1)
                .Where(c => !c.IsImpounded && !advertised.Contains(c.InstanceId))
                .OrderBy(c => Score(c, groupOf))
                .Take(over - racer.Cars.Skip(1).Count(c => advertised.Contains(c.InstanceId)))
                .ToList();
            foreach (var spare in spares) Advertise(gameState, racer, spare, date, talk);
        }

        /// <summary>The car in the paper, asking less than a lot would and more than a dealer pays</summary>
        private void Advertise(GameState gameState, Opponent racer, Car car, DateTime date, List<string> talk)
        {
            var value = ValueOf(car);
            var asking = OpponentRules.AskingPrice(GameRules.Scale(value, gameState.Rules.CarPriceMultiplier),
                Math.Round(value * (decimal)CarSaleService.DealerShare), _random.NextDouble());
            if (asking <= 0)
            {
                Sell(gameState, racer, car, talk);
                return;
            }

            // What the seller says about the engine, as a lot would: the ad keeps it, the paper's page reads it from there
            var engine = _market.DescribeEngine(car);
            RivalCarAds.Post(gameState, racer, car, asking, date, engine?.Summary, engine?.IsModified ?? false);
            talk.Add($"{racer.Name} put {OpponentRules.Possessive(racer)} {CarName(car)} in the paper for ${asking:N0}.");
            _logger.Information("{Racer} advertised the {Car} for ${Asking}", racer.Name, car.DefinitionId, asking);
        }

        /// <summary>
        /// Their ads in the paper: one nobody answered for a week comes down in price, and one that ran its time goes
        /// to a dealer after all
        /// </summary>
        private void ReviewAds(GameState gameState, Opponent racer, DateTime date, List<string> talk)
        {
            foreach (var (ad, _, car) in RivalCarAds.LiveOf(gameState, racer))
            {
                // By the calendar, as the other day counts are: not by the hour the review happens to run
                var days = (date.Date - ad.PostedDate.Date).Days;
                if (days >= CarSaleService.AdDays)
                {
                    talk.Add($"Nobody called about {racer.Name}'s {CarName(car)}.");
                    Sell(gameState, racer, car, talk);
                }
                else if (days >= OpponentRules.AskReducedAfterDays && !ad.Reduced)
                {
                    var floor = Math.Round(ValueOf(car) * (decimal)CarSaleService.DealerShare);
                    ad.AskingPrice = Math.Max(floor, CarValuation.RoundToHundred(ad.AskingPrice * OpponentRules.AskReduction));
                    ad.Reduced = true;
                }
            }
        }

        /// <summary>A dealer buys the car at its share and puts it on the lot; a wreck goes for scrap</summary>
        private decimal Sell(GameState gameState, Opponent racer, Car car, List<string> talk)
        {
            var value = ValueOf(car);
            var totaled = CarCondition.IsTotaled(car);
            var paid = Math.Round(value * (decimal)(totaled ? CarSaleService.ScrapFactor(car.InstanceId) : CarSaleService.DealerShare));

            racer.Cars.Remove(car);
            racer.Money += paid;
            racer.Stats.CarsSold++;
            if (RivalCarAds.AdFor(gameState, car) is { } ad) gameState.NewspaperAds.RivalCars.Remove(ad);

            if (!totaled)
            {
                gameState.UsedCarMarket.Add(_market.ListCar(car, GameRules.Scale(value, gameState.Rules.CarPriceMultiplier),
                    _market.TradeInLocation(gameState.DealerLocations), gameState.Date));
            }

            talk.Add(totaled
                ? $"{racer.Name} sold the wreck of {OpponentRules.Possessive(racer)} {CarName(car)} for scrap."
                : $"{racer.Name} sold {OpponentRules.Possessive(racer)} {CarName(car)} to a dealer for ${paid:N0}.");
            _logger.Information("{Racer} sold the {Car} for ${Paid}{Scrap}", racer.Name, car.DefinitionId, paid, totaled ? " (scrap)" : "");
            return paid;
        }

        /// <summary>A car for sale as a buyer weighs it: off a lot, or out of a rival's ad</summary>
        private sealed record ForSale(decimal Price, double Hp, double Condition, UsedCarListing? Listing, RivalCarAds.Offer? Ad);

        /// <summary>Everything for sale a racer could buy with <paramref name="budget"/>: the lots and the other rivals' ads</summary>
        private List<ForSale> CarsForSale(GameState gameState, Opponent racer, decimal budget) =>
        [
            .. gameState.UsedCarMarket
                .Where(l => !l.IsSold && l.Price > 0 && l.Price <= budget)
                .Select(l => new ForSale(l.Price, l.PowerHp ?? 0, l.Condition, l, null)),
            .. RivalCarAds.Live(gameState)
                .Where(o => !ReferenceEquals(o.Seller, racer) && o.Ad.AskingPrice > 0 && o.Ad.AskingPrice <= budget)
                .Select(o => new ForSale(o.Ad.AskingPrice, o.Car.PowerHp ?? 0, CarValuation.ConditionOf(o.Car), null, o))
        ];

        /// <summary>The best car on the lots and in the paper for the racer's money, as <see cref="OpponentRules.PurchaseScore"/> weighs them</summary>
        private bool TryBuy(GameState gameState, Opponent racer, DateTime date, List<string> talk)
        {
            var budget = racer.Money;
            if (budget <= 0) return false;

            var pick = CarsForSale(gameState, racer, budget)
                .OrderByDescending(c => OpponentRules.PurchaseScore(c.Hp, c.Condition, c.Hp > 0, c.Price, budget))
                .FirstOrDefault();
            return pick != null && Buy(gameState, racer, pick, date, talk);
        }

        /// <summary>
        /// Now and then a racer with money to spare buys a clearly stronger car (<see cref="OpponentRules.WorthTradingUp"/>)
        /// and drives it; the old one goes into the paper
        /// </summary>
        private void TryTradeUp(GameState gameState, Opponent racer, DateTime date, Func<string, string?>? groupOf, List<string> talk)
        {
            if (racer.IsKing || racer.Cars.FirstOrDefault() is not { } current) return;

            var currentHp = current.PowerHp ?? 0;
            var pick = CarsForSale(gameState, racer, racer.Money)
                .Where(c => c.Hp > 0 && OpponentRules.WorthTradingUp(currentHp, c.Hp, c.Price, racer.Money))
                // A rival's car is there to look at: one that can't race is no step up
                .Where(c => c.Ad == null || CanRace(c.Ad.Car, groupOf))
                .OrderByDescending(c => OpponentRules.PurchaseScore(c.Hp, c.Condition, true, c.Price, racer.Money))
                .FirstOrDefault();
            if (pick == null || !Buy(gameState, racer, pick, date, talk)) return;

            // A car off a lot that turns out not fit to race: they keep driving the old one, and it stays out of the paper
            PickBestCar(racer, groupOf);
            if (ReferenceEquals(racer.Cars[0], current)) return;

            if (RivalCarAds.AdFor(gameState, current) == null && !current.IsImpounded) Advertise(gameState, racer, current, date, talk);
        }

        private bool Buy(GameState gameState, Opponent racer, ForSale pick, DateTime date, List<string> talk)
        {
            if (pick.Ad is { } offer)
            {
                var seller = offer.Seller;
                var bought = RivalCarAds.HandOver(gameState, offer.Ad, racer.Name, date);
                if (bought == null) return false;

                racer.Money -= offer.Ad.AskingPrice;
                racer.Cars.Insert(0, bought);
                racer.Stats.CarsOwned++;
                Grudges.CarBack(racer, bought);

                talk.Add($"{racer.Name} bought {seller.Name}'s {CarName(bought)} out of the paper for ${offer.Ad.AskingPrice:N0}.");
                _logger.Information("{Racer} bought {Seller}'s {Car} for ${Price} ({Hp:0} hp), ${Money} left",
                    racer.Name, seller.Name, bought.DefinitionId, offer.Ad.AskingPrice, bought.PowerHp ?? 0, racer.Money);
                return true;
            }

            var listing = pick.Listing!;

            // Off the lot, exactly as the player would have had it
            listing.IsSold = true;
            listing.SoldDate = date;
            racer.Money -= listing.Price;

            var car = CarPurchaseService.CarFrom(listing, date, racer.Name);
            listing.Parts = [];
            try
            {
                _parts.EnsureParts(car);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not give {Racer}'s new {Car} its factory parts: {Error}", racer.Name, car.DefinitionId, ex.Message);
            }

            racer.Cars.Insert(0, car);
            racer.Stats.CarsOwned++;
            Grudges.CarBack(racer, car);

            var dealer = gameState.DealerLocations?.FirstOrDefault(d => d.Id == listing.DealerLocation)?.Name;
            talk.Add($"{racer.Name} bought a {CarName(car)}{(dealer == null ? "" : $" off {dealer}'s lot")} for ${listing.Price:N0}.");
            _logger.Information("{Racer} bought the {Car} for ${Price} ({Hp:0} hp), ${Money} left", racer.Name, car.DefinitionId, listing.Price, car.PowerHp ?? 0, racer.Money);
            return true;
        }

        /// <summary>The whole bill or nothing: half a repair doesn't get a car racing</summary>
        private bool TryRepair(GameState gameState, Opponent racer, Car car, Func<string, string?>? groupOf, List<string> talk)
        {
            var jobs = RepairShop.Jobs(car, _parts.IsAvailable ? _parts.Catalog : null, gameState.Rules.PartPriceMultiplier);
            var bill = jobs.Sum(j => j.Cost);
            if (jobs.Count == 0 || bill > racer.Money) return false;

            foreach (var job in jobs) job.Apply();
            racer.Money -= bill;

            talk.Add($"{racer.Name} had {OpponentRules.Possessive(racer)} {CarName(car)} fixed up for ${bill:N0}.");
            _logger.Information("{Racer} repaired the {Car}: {Jobs} for ${Bill}", racer.Name, car.DefinitionId, string.Join(", ", jobs.Select(j => j.Name)), bill);
            return CanRace(car, groupOf);
        }

        /// <summary>A car too dear to fix is sold, when what it fetches buys one that runs</summary>
        private void SellAndBuyAnother(GameState gameState, Opponent racer, Car car, DateTime date, Func<string, string?>? groupOf, List<string> talk)
        {
            var cheapestRunner = gameState.UsedCarMarket.Where(l => !l.IsSold && (l.PowerHp ?? 0) > 0).Select(l => (decimal?)l.Price).Min();
            if (cheapestRunner == null) return;

            var value = ValueOf(car);
            var fetches = value * (decimal)(CarCondition.IsTotaled(car) ? CarSaleService.ScrapFactor(car.InstanceId) : CarSaleService.DealerShare);
            if (racer.Money + fetches < cheapestRunner) return;

            Sell(gameState, racer, car, talk);
            if (racer.Cars.Count == 0 || !CanRace(racer.Cars[0], groupOf)) TryBuy(gameState, racer, date, talk);
        }

        // ----- the street -----

        /// <summary>More racers come out as the weeks go by; one whose car can't race yet comes out to fix it first</summary>
        private void Activate(GameState gameState, DateTime date, Func<string, string?>? groupOf, List<string> talk)
        {
            var racers = gameState.Racers;
            // Those who left count: once the new faces run out, they are who comes back
            var street = racers.All.Concat(racers.Departed.Values)
                .Count(r => r is not Opponent { IsKing: true });
            var ready = racers.ReadyToRace.Values.Count(r => r is not Opponent { IsKing: true });
            var wanted = OpponentRules.MinActive(date, street) - ready;
            if (wanted <= 0) return;

            var newcomers = racers.Inactive.Values.OfType<Opponent>().Where(o => !o.IsKing).OrderBy(_ => _random.Next()).Take(wanted).ToList();
            var comebacks = ComeBacks(gameState, date, wanted - newcomers.Count);
            foreach (var back in comebacks)
            {
                racers.MoveRacer(back.Name, RacerStatus.Inactive);
                talk.Add($"{back.Name}{Nickname(back)} is back in town.");
            }

            foreach (var racer in newcomers.Concat(comebacks))
            {
                racers.MoveRacer(racer.Name, RacerStatus.Retired);
                ReviewRacer(gameState, racer, date, groupOf, talk);
                talk.RemoveAll(t => t == $"{racer.Name} is back on the street.");

                // One who came back was announced as that: not a new face
                if (racer.Status == RacerStatus.ReadyToRace && !comebacks.Contains(racer))
                {
                    talk.Add(racer.Cars.FirstOrDefault() is { } car
                        ? $"A new face at the diner: {racer.Name}{Nickname(racer)}, in a {CarName(car)}."
                        : $"A new face at the diner: {racer.Name}{Nickname(racer)}.");
                }
            }
        }

        /// <summary>
        /// Racers who left at least <see cref="OpponentRules.ComeBackAfterDays"/> ago, the longest gone first, with a
        /// fresh start: new money, nothing owed, broke no more
        /// </summary>
        private List<Opponent> ComeBacks(GameState gameState, DateTime date, int count)
        {
            if (count <= 0) return [];

            var back = gameState.Racers.Departed.Values.OfType<Opponent>()
                .Where(o => !o.IsKing && o.LeftDate is { } left && (date.Date - left.Date).Days >= OpponentRules.ComeBackAfterDays)
                .OrderBy(o => o.LeftDate)
                .Take(count)
                .ToList();
            foreach (var racer in back)
            {
                racer.Money = _initialization?.StartingMoney(racer) ?? OpponentRules.CashInjection(CheapestCar(gameState), _random.NextDouble());
                racer.TimesBroke = 0;
                racer.IsBroke = false;
                racer.LeftDate = null;
                _logger.Information("{Racer} came back to town with ${Money}", racer.Name, racer.Money);
            }

            return back;
        }

        /// <summary>A few racers with money to spare put it into their engines; the parts are tried on the dyno off the UI thread</summary>
        private async Task TuneAsync(GameState gameState, Func<string, string?>? groupOf, List<string> talk)
        {
            if (!_parts.IsAvailable) return;

            // Whether a part is affordable is the tuner's to find out, part by part at the shop's prices
            var jobs = gameState.Racers.ReadyToRace.Values
                .Concat(gameState.Racers.Inactive.Values.Where(r => r is Opponent { IsKing: true }))
                .OfType<Opponent>()
                .Where(r => r.Cars.FirstOrDefault() is { Engine: not null } car && CanRace(car, groupOf) && _random.NextDouble() < TuneChance)
                .Select(r => (Racer: r, Budget: OpponentRules.TuningBudget(r.Money, ValueOf(r.Cars[0]))))
                .Where(r => r.Budget > 0)
                .OrderBy(_ => _random.Next())
                .Take(MaxTunersPerDay)
                .Select(r => new TuneJob(r.Racer, r.Racer.Cars[0], r.Racer.Cars[0].Engine!.InstanceId, PartTrees.Clone(r.Racer.Cars[0].Engine!, keepIds: true),
                    r.Budget, _random.Next(),
                    // The paper as it is this morning, copied: the tuner reads it off this thread
                    RivalPartAds.OffersFor(gameState, r.Racer).Select(o => o with { Part = PartTrees.Clone(o.Part, keepIds: true) }).ToList()))
                .ToList();
            if (jobs.Count == 0) return;

            var catalog = _parts.Catalog;
            var builds = _parts.Builds;
            var prices = gameState.Rules.PartPriceMultiplier;
            var results = await Task.Run(() => jobs.Select(job =>
            {
                try
                {
                    // One thing at a time: a racer works on the car over the weeks, not in a day
                    return EngineTuner.TuneUp(catalog, builds, job.Engine, job.Budget, prices, new Random(job.Seed), maxUpgrades: 1, used: job.Used);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Tuning {Racer}'s engine failed: {Error}", job.Racer.Name, ex.Message);
                    return null;
                }
            }).ToList());

            for (var i = 0; i < jobs.Count; i++)
            {
                var (racer, car, engineId, _, _, _, _) = jobs[i];
                var result = results[i];
                if (result == null) continue;

                // Still their car, still that engine, still the money, still the part in the paper: the day may have
                // moved on meanwhile (another racer bought it this morning)
                var index = car.Parts.FindIndex(p => p.InstanceId == engineId);
                if (!racer.Cars.Contains(car) || index < 0 || racer.Money < result.Cost) continue;
                if (result.Upgrades.Any(u => u.UsedAdId is { } id && gameState.NewspaperAds.Parts.All(a => a.AdId != id))) continue;

                car.Parts[index] = result.Engine;
                racer.Money -= result.Cost;
                foreach (var upgrade in result.Upgrades)
                {
                    if (upgrade.UsedAdId is { } adId) RivalPartAds.Take(gameState, adId);

                    // What came off goes into the paper under their name, paid for when it sells (or at a shop's
                    // trade-in when the ad runs out); a shop takes at once what is worth nothing there
                    foreach (var removed in upgrade.Removed)
                    {
                        if (RivalPartAds.Post(gameState, catalog, racer, removed, gameState.Date, _random.NextDouble()) is { } ad)
                            _logger.Information("{Racer} put the {Part} in the paper for ${Price}", racer.Name, removed.DefinitionId, ad.AskingPrice);
                        else
                            racer.Money += PartPricing.TradeIn(catalog, removed);
                    }
                }

                car.PowerHp = MarketPower(result.Report);
                if (groupOf != null) CarCondition.RefreshFigures(car, groupOf);

                foreach (var upgrade in result.Upgrades)
                {
                    talk.Add(upgrade.Group == EngineTuner.BlockGroup
                        ? $"{racer.Name} dropped {upgrade.Change} into {OpponentRules.Possessive(racer)} {CarName(car)} ({upgrade.PowerBefore:0} → {upgrade.PowerAfter:0} hp)."
                        : $"{racer.Name} put a {upgrade.Change} on {OpponentRules.Possessive(racer)} {CarName(car)} ({upgrade.PowerBefore:0} → {upgrade.PowerAfter:0} hp).");
                }

                _logger.Information("{Racer} tuned the {Car}: {Upgrades}; ${Cost} spent, ${Money} left",
                    racer.Name, car.DefinitionId, string.Join(", ", result.Upgrades.Select(u => u.Change)), result.Cost, racer.Money);
            }
        }

        private sealed record TuneJob(Opponent Racer, Car Car, Guid EngineId, PartInstance Engine, decimal Budget, int Seed, List<UsedPartOffer> Used);

        // ----- the parts and the dyno -----

        /// <summary>Cars from before rivals had parts get their factory ones, put together off the UI thread</summary>
        private async Task GiveCarsTheirPartsAsync(GameState gameState)
        {
            if (!_parts.IsAvailable) return;

            foreach (var car in AllRivalCars(gameState).Where(c => !c.HasPartsAssigned || !c.HasRunningGearAssigned).ToList())
            {
                try
                {
                    await _parts.EnsurePartsAsync(car);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not give the {Car} its parts: {Error}", car.DefinitionId, ex.Message);
                }
            }
        }

        /// <summary>
        /// Every rival car and every car for sale without a figure goes on the dyno: the rivals' because wear and
        /// repairs change it, the lots' once. On copies, off the UI thread.
        /// </summary>
        private async Task PutOnDynoAsync(GameState gameState)
        {
            if (!_parts.IsAvailable) return;

            var cars = AllRivalCars(gameState).Select(c => (Car: c, Engine: c.Engine is { } e ? PartTrees.Clone(e) : null)).ToList();
            var listings = gameState.UsedCarMarket
                .Where(l => !l.IsSold && l.PowerHp == null)
                .Select(l => (Listing: l, Engine: l.Parts.FirstOrDefault(p => p.ParentSlot == PartInstance.CarEngineSlot) is { } e ? PartTrees.Clone(e) : null))
                .ToList();

            var catalog = _parts.Catalog;
            double Power(PartInstance? engine)
            {
                if (engine == null) return 0;
                try
                {
                    return MarketPower(EngineFactory.Evaluate(catalog, engine));
                }
                catch (Exception ex)
                {
                    _logger.Warning("An engine could not go on the dyno: {Error}", ex.Message);
                    return 0;
                }
            }

            var (carPower, listingPower) = await Task.Run(() =>
                (cars.Select(c => Power(c.Engine)).ToList(), listings.Select(l => Power(l.Engine)).ToList()));

            // A listing without an engine sells with the factory one: it runs, but nobody knows how well
            for (var i = 0; i < cars.Count; i++) cars[i].Car.PowerHp = carPower[i];
            for (var i = 0; i < listings.Count; i++) listings[i].Listing.PowerHp = listings[i].Engine == null ? null : listingPower[i];
        }

        private static IEnumerable<Car> AllRivalCars(GameState gameState) =>
            gameState.Racers.All
                .Where(r => r.Type == RacerType.AI)
                .SelectMany(r => r.Cars);

        private static double MarketPower(Street_Rod_AC.Parts.Logic.EngineReport? report) => UsedCarMarketService.PowerOf(report);

        private Func<string, string?>? PartGroups()
        {
            try
            {
                return _parts.IsAvailable ? CarCondition.Groups(_parts.Catalog) : null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The parts catalog could not be read: the rivals' cars are judged on their own figures");
                return null;
            }
        }

        private static bool CanRace(Car car, Func<string, string?>? groupOf) => CarCondition.WhyCannotRace(car, groupOf).Count == 0;

        private static bool IsReady(Racer racer, Func<string, string?>? groupOf) =>
            racer.Cars.FirstOrDefault() is { } car && CanRace(car, groupOf);

        private static decimal? CheapestCar(GameState gameState) =>
            gameState.UsedCarMarket.Where(l => !l.IsSold && l.Price > 0).Select(l => (decimal?)l.Price).Min();

        private decimal ValueOf(Car car)
        {
            try
            {
                return (_valueOf ?? _market.ValueOf)(car);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not value the {Car}; going by what was paid for it: {Error}", car.DefinitionId, ex.Message);
                return CarValuation.PaidFor(car);
            }
        }

        private string CarName(Car car) => _carNames?.Invoke(car.DefinitionId) ?? CarNames.Of(_catalogRepo, car.DefinitionId);

        private static string Nickname(Opponent racer) => string.IsNullOrWhiteSpace(racer.Nickname) ? "" : $" \"{racer.Nickname}\"";

        /// <summary>The day's talk after what was said before; a week later nobody talks about it any more</summary>
        public static void AddTalk(GameState gameState, DateTime date, IEnumerable<string> lines)
        {
            gameState.StreetTalk ??= [];
            DatedLog.Append(gameState.StreetTalk, lines.Select(line => new StreetTalkItem(date, line)), item => item.Date, date, StreetTalkDays, MaxStreetTalk);
        }
    }
}
