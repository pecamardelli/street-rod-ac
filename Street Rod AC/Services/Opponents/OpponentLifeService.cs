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
    /// <item>drives the best car they own, and sells what they don't need (more than <see cref="OpponentRules.MaxCars"/>, or a wreck);</item>
    /// <item>without a car, buys one off the same lots the player buys from;</item>
    /// <item>with a car that can't race, has it repaired if the whole bill is affordable, else sells it and buys
    /// another if that gets them racing, else sits it out (retired) until they can;</item>
    /// <item>broke, scrapes some money together (<see cref="OpponentRules.IsBankrupt"/>) and comes back.</item>
    /// </list>
    /// Then more racers come out onto the street as the weeks go by (<see cref="OpponentRules.MinActive"/>), and a
    /// few with money to spare tune their engines (<see cref="EngineTuner"/>). What the street notices goes into
    /// <see cref="GameState.StreetTalk"/>.
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
        IOpponentInitializationService? initialization = null) : IOpponentLifeService
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
        private readonly Random _random = new();

        // The day's catalog lookups: each model valued and named from the catalog once per review, not per car and line
        private Func<Car, decimal>? _valueOf;
        private Func<string, string>? _carNames;

        public async Task ReviewDayAsync(GameState gameState, DateTime currentDate)
        {
            _initialization?.EnsureKing(gameState);
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
            foreach (var racer in racers) ReviewRacer(gameState, racer, currentDate, groupOf, talk);

            Activate(gameState, currentDate, groupOf, talk);

            await TuneAsync(gameState, groupOf, talk);

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
                SellSpares(gameState, racer, groupOf, talk);

                if (racer.Cars.Count == 0) TryBuy(gameState, racer, date, talk);

                // A car in the impound can be neither fixed nor sold: they wait for it
                if (racer.Cars.FirstOrDefault() is { IsImpounded: false } car && !CanRace(car, groupOf)
                    && !TryRepair(gameState, racer, car, groupOf, talk))
                {
                    SellAndBuyAnother(gameState, racer, car, date, groupOf, talk);
                }

                if (OpponentRules.IsBankrupt(IsReady(racer, groupOf), racer.Money, CheapestCar(gameState)))
                {
                    var cash = OpponentRules.CashInjection(CheapestCar(gameState), _random.NextDouble());
                    racer.Money += cash;
                    talk.Add($"{racer.Name} is broke and scraped ${cash:N0} together to get back on the street.");
                    _logger.Information("{Racer} is broke: ${Cash} injected, now ${Money}", racer.Name, cash, racer.Money);
                }

                MoveToWhereTheyBelong(gameState, racer, groupOf, talk);
            }
            catch (Exception ex)
            {
                // One racer's bad car is not everybody's day
                _logger.Error(ex, "Could not review {Racer}", racer.Name);
            }
        }

        private void MoveToWhereTheyBelong(GameState gameState, Opponent racer, Func<string, string?>? groupOf, List<string> talk)
        {
            var ready = IsReady(racer, groupOf);
            var target = racer.IsKing
                ? ready && new KingVictory().IsUnlocked(gameState.Career) ? RacerStatus.ReadyToRace : RacerStatus.Inactive
                : ready ? RacerStatus.ReadyToRace : RacerStatus.Retired;
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

        /// <summary>Wrecks they don't drive go for scrap, and cars beyond the spare to a dealer</summary>
        private void SellSpares(GameState gameState, Opponent racer, Func<string, string?>? groupOf, List<string> talk)
        {
            while (racer.Cars.Count > 1)
            {
                // Cars in the impound can't be sold: with nothing else to spare they wait
                var spares = racer.Cars.Skip(1).Where(c => !c.IsImpounded).ToList();
                if (spares.Count == 0) return;
                var wreck = spares.FirstOrDefault(CarCondition.IsTotaled);
                var sell = wreck ?? (racer.Cars.Count > OpponentRules.MaxCars ? spares.OrderBy(c => Score(c, groupOf)).First() : null);
                if (sell == null) return;

                Sell(gameState, racer, sell, talk);
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

        /// <summary>The best car on the lots for the racer's money, as <see cref="OpponentRules.PurchaseScore"/> weighs them</summary>
        private bool TryBuy(GameState gameState, Opponent racer, DateTime date, List<string> talk)
        {
            var budget = racer.Money;
            if (budget <= 0) return false;

            var listing = gameState.UsedCarMarket
                .Where(l => !l.IsSold && l.Price > 0 && l.Price <= budget)
                .OrderByDescending(l => OpponentRules.PurchaseScore(l.PowerHp ?? 0, l.Condition, (l.PowerHp ?? 0) > 0, l.Price, budget))
                .FirstOrDefault();
            if (listing == null) return false;

            // Off the lot, exactly as the player would have had it
            listing.IsSold = true;
            listing.SoldDate = date;
            racer.Money -= listing.Price;

            var car = CarPurchaseService.CarFrom(listing, date);
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
            var street = racers.ReadyToRace.Values.Concat(racers.Retired.Values).Concat(racers.Inactive.Values)
                .Count(r => r is not Opponent { IsKing: true });
            var ready = racers.ReadyToRace.Values.Count(r => r is not Opponent { IsKing: true });
            var wanted = OpponentRules.MinActive(date, street) - ready;
            if (wanted <= 0) return;

            var newcomers = racers.Inactive.Values.OfType<Opponent>().Where(o => !o.IsKing).OrderBy(_ => _random.Next()).Take(wanted).ToList();
            foreach (var racer in newcomers)
            {
                racers.MoveRacer(racer.Name, RacerStatus.Retired);
                ReviewRacer(gameState, racer, date, groupOf, talk);
                if (racer.Status == RacerStatus.ReadyToRace)
                {
                    talk.RemoveAll(t => t == $"{racer.Name} is back on the street.");
                    talk.Add(racer.Cars.FirstOrDefault() is { } car
                        ? $"A new face at the diner: {racer.Name}{Nickname(racer)}, in a {CarName(car)}."
                        : $"A new face at the diner: {racer.Name}{Nickname(racer)}.");
                }
            }
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
                    r.Budget, _random.Next()))
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
                    return EngineTuner.TuneUp(catalog, builds, job.Engine, job.Budget, prices, new Random(job.Seed), maxUpgrades: 1);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Tuning {Racer}'s engine failed: {Error}", job.Racer.Name, ex.Message);
                    return null;
                }
            }).ToList());

            for (var i = 0; i < jobs.Count; i++)
            {
                var (racer, car, engineId, _, _, _) = jobs[i];
                var result = results[i];
                if (result == null) continue;

                // Still their car, still that engine, still the money: the day may have moved on meanwhile
                var index = car.Parts.FindIndex(p => p.InstanceId == engineId);
                if (!racer.Cars.Contains(car) || index < 0 || racer.Money < result.Cost) continue;

                car.Parts[index] = result.Engine;
                racer.Money += result.TradeIn - result.Cost;
                car.PowerHp = MarketPower(result.Report);
                if (groupOf != null) CarCondition.RefreshFigures(car, groupOf);

                foreach (var upgrade in result.Upgrades)
                {
                    talk.Add(upgrade.Group == EngineTuner.BlockGroup
                        ? $"{racer.Name} dropped {upgrade.Change} into {OpponentRules.Possessive(racer)} {CarName(car)} ({upgrade.PowerBefore:0} → {upgrade.PowerAfter:0} hp)."
                        : $"{racer.Name} put a {upgrade.Change} on {OpponentRules.Possessive(racer)} {CarName(car)} ({upgrade.PowerBefore:0} → {upgrade.PowerAfter:0} hp).");
                }

                _logger.Information("{Racer} tuned the {Car}: {Upgrades}; ${Cost} spent, ${TradeIn} back, ${Money} left",
                    racer.Name, car.DefinitionId, string.Join(", ", result.Upgrades.Select(u => u.Change)), result.Cost, result.TradeIn, racer.Money);
            }
        }

        private sealed record TuneJob(Opponent Racer, Car Car, Guid EngineId, PartInstance Engine, decimal Budget, int Seed);

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
            gameState.Racers.ReadyToRace.Values.Concat(gameState.Racers.Retired.Values).Concat(gameState.Racers.Inactive.Values)
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
            gameState.StreetTalk.AddRange(lines.Select(line => new StreetTalkItem(date, line)));
            gameState.StreetTalk.RemoveAll(item => (date - item.Date).TotalDays > StreetTalkDays);
            if (gameState.StreetTalk.Count > MaxStreetTalk) gameState.StreetTalk.RemoveRange(0, gameState.StreetTalk.Count - MaxStreetTalk);
        }
    }
}
