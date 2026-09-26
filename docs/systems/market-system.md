# Market System

## Purpose
Dynamic used car market with dealer locations, pricing based on condition, and daily refresh.

## Data Layers

| Layer | Model | Purpose |
|-------|-------|---------|
| 1 | CarDefinition | Immutable AC identity |
| 2 | CarProfile | BasePrice, DealerPrecedence |
| 3 | UsedCarListing | Car for sale |
| 4 | CarInstance | Player-owned car |

## UsedCarListing Properties
- `CarDefinitionId` - References catalog
- `Price` - Varies from BasePrice by condition
- `Condition` - 0.0 (broken) to 1.0 (perfect)
- `Mileage` - Odometer reading
- `SkinId` - Visual variant
- `DealerLocation` - Which dealer
- `ListedDate` - When listed
- `IsSold` - Purchase status

## Spawning Logic
Dealer by dealer, each filled to its own `stockLow`..`stockHigh`:
- Build the pool of cars that have a profile and a price, each with its rank (the share of installed cars
  it is dearer than)
- For each dealer, take the cars whose rank falls in its price band
- Draw from those weighted by `DealerPrecedence`, at most twice per model, until the lot is full

A dealer whose band matches nothing installed takes the nearest cars instead of standing empty.

Do **not** go back to spawning a whole market and cutting it to size — `.Take(n)` over a list built in
catalog order starves whole dealers.

## Daily Refresh
1. Remove sold listings older than 7 days
2. Remove unsold listings older than 14 days
3. Top each dealer back up to its own target

The save's rules can turn the daily refresh off (`GameRules.MarketRefreshEnabled`): the lots then keep what they have.

## Pricing
What a car is worth has one formula, `CarValuation` (`Services/Market/CarValuation.cs`), used by the market, an
opponent's car, a pink-slipped car going back on a lot and the pink-slip challenge logic:
- `CarProfile.BasePrice` × condition factor `0.5 + 0.6 × condition` (condition 0 = 50%, 1.0 = 110%)
- × its history (`HistoryFactor`, step 10): +1% a win and +2% a car won on a pink slip; −2% an owner beyond the
  second and −1% every 10,000 km past 80,000 (each at most −8%); all together within ±15%. A car with no history
  (an older save) is ×1.
- plus half of what the car's engine parts cost new beyond its factory build's, scaled by condition (`ValueOf`, with the
  parts catalog; without it the engine counts as the factory one)
- rounded to the nearest $100

A listing's price is that times the dealer's random variation of ±20%, then times the save's
`GameRules.CarPriceMultiplier` (the difficulty: 0.85 Easy, 1 Normal, 1.15 Hard). The multiplier is on what sellers
ask, never on what a car is worth: a car the player sells fetches a share of its worth whatever the difficulty.

## Dealer Locations
Ten dealers, defined in `Assets/Dealers/dealers.json` and read by `DealerCatalog`: Downtown Motors,
Sunset Motors, Colorado Motors, Suburban Autos, Ocean Park Autos, Riverside Cars, Vermont Auto Sales,
Industrial Motors, Harbor Auto and Eastside Garage. `GetDefaultDealers()` still hands out the original five
when the file cannot be read.

The save holds only `DealerLocation` (Id, Name, Region). Map position, showroom, parking bays and stock
character live in the JSON and are merged over the save on load, so new fields reach old saves. See
`docs/screens/dealer-lot.md`.

## Which Dealer Gets a Car
Not random. Each dealer claims a slice of the market's price range (`priceBandLow`/`priceBandHigh`); a car
goes to one of the dealers whose slice covers where its base price sits, or to the nearest slice if none
does. `conditionCenter` then shifts the condition roll by how far that dealer's standard sits from the middle
of the range (the roll still leads, and one car in twelve is not shifted at all), so a cheap lot has rough
cars without making a good find impossible.

## Purchase Flow
`CarPurchaseService` owns this, shared by the listings screen and the dealer lot (`Screens/Shared/PurchaseFlow.cs`
holds the confirmation and result dialogs).

1. Validate funds and availability
2. Claim the listing (`IsSold = true`) before anything is awaited, so a second confirm cannot buy it twice; it is
   released again if making the car fails
3. Create `CarInstance` from listing (a part pack that cannot be put together is logged; the car sells without it and
   `EnsurePartsAsync` fills it in later)
4. Add to `GameState.Player.Cars`
5. Deduct money
6. Spend `GameAction.BuyCar` time
7. Save game state

A car bought counts in `Player.Stats.CarsOwned`.

**A rival's car out of the paper** (step 12): the Used Cars page lists the rivals' ads after the lots, as listings made
up from the car for the page only (`RivalCarAds.AsListing`: the car's own figures and the engine as the ad described it
when posted, no dyno run). The row carries the ad (`UsedCarListingViewModel.RivalAd`), and buying it
(`CarPurchaseService.PurchaseFromRivalAsync`, called with the ad) pays the ad's price as it stands now to the rival and hands over that very
car from their garage (`RivalCarAds.HandOver`, `CarAcquisition.PrivateSale`); nothing is put together. See
`opponent-system.md`, "A bigger pool".

## Selling Cars
`CarSaleService` (`ICarSaleService`), from the garage's Sell button (`Dialogs/SellCar`):
- **To a dealer**, on the spot: `DealerShare` (60%) of what the car is worth, one hour (`GameAction.SellCar`). The
  trade-in lot (`TradeInLocation`) puts it on sale at its worth times `CarPriceMultiplier`.
- **To the scrapyard**, the only buyer of a totaled car (`CarCondition.IsTotaled`): 15% of its worth, ±20% (the same
  price every time for the same car, rolled from its instance id). It leaves the game.
- **Through the paper**: an ad (`CarSaleAd` in `NewspaperAds.PlayerCars`) at the player's asking price ($100 up to
  three times the car's worth) costs $10 and half an hour (`GameAction.PlaceAd`) and runs 14 days. The car stays in the
  garage and can race. Each day (`CarAdsReviewTask`) a buyer may call: a 60% chance for a car asked at 90% of its worth
  or less, 12 points less for each tenth more, nobody at 140%. A buyer pays the asking price up to the car's worth; over
  it they haggle up to 15% off, never below the worth. An offer holds two days; the player takes it or turns it down in
  the Sell dialog or under "Your Ads" in the newspaper. A buyer drives the car away: it leaves the game.

A sale refuses a car that is out racing (`GameState.PendingRace`), moves the selected car on to another, and counts in
`Player.Stats.CarsSold`.

## Car History
Every car keeps its history (`Car.History`, a `CarHistory`): the owners the game has seen, each with the date and how
they got it (`CarAcquisition`: Dealer, PinkSlip, PrivateSale, Unknown), the owners before anybody in the game had it
(`EarlierOwners`), and its races, wins and pink slips won, whoever drove it. The odometer is `Car.OdometerKM`.
- **Its best quarter mile** (step 11): `BestQuarterSeconds`, `BestQuarterMph` and `BestQuarterDate`, from any drag
  race's timeslip or test-and-tune pass, whoever drove it (`CarHistory.RecordQuarter`; a time under 5 s or over 60 s
  is a bad file, not a run). It shows with the history ("best 13.52 @ 104 mph") and is the dial-in a bracket race
  suggests. It does not move the price.
- It goes with the car everywhere: a pink slip (the player's races and the rivals'), a sale out of the paper to a rival,
  and through a dealer's lot (`ListCar` copies it onto `UsedCarListing.History`, `CarPurchaseService.CarFrom` copies it
  back and adds the buyer). A relisted car keeps its id (`UsedCarListing.CarInstanceId`): whoever buys it gets that
  very car back.
- Somebody who had the car twice (won it back, bought it back) is one owner (`OwnerCount`), for the price and the words.
- New stock comes with 1-3 earlier owners by its mileage; a rival's first car with a few more; the King's car with his
  record (it tops the ±15%).
- Older saves: `HistoryUpgrade` gives every car its present owner at load, and a car or listing nothing was known about
  the earlier owners its miles tell of (`EarlierOwnersFor`: none under 1,000 km, one more every 80,000 km).
- Shown on the dealer lot, the used car ads, the purchase question, the Sell dialog and the garage (the mileage's
  tooltip), always through `CarHistoryDisplay`.

## Cars Going Back on a Lot
`ListCar(car, price, location, listedDate)` turns a car into a listing with its parts (engine, running gear), the
seller's engine summary and whether it has been worked on. A pink-slipped car goes back this way at its `ValueOf` (times `CarPriceMultiplier`), to
`TradeInLocation(dealers)`: the roughest lot (lowest `conditionCenter`).

## Key Service
`UsedCarMarketService` (`IUsedCarMarketService`):
- `SpawnListingsAsync(dealers, date, priceMultiplier)` - Initial spawn
- `RefreshMarketAsync(listings, dealers, date, priceMultiplier)` - Daily refresh. Engines are put together only for the listings that
  make it into the market, on a worker thread; the listings are looked at and handed back on the calling thread
- `GetAvailableListings(listings)` - Filter unsold
- `ListCar(car, price, location, listedDate)` - A car (with its parts) as a listing
- `ValueOf(car)` - What the car is worth (`CarValuation`)
- `TradeInLocation(dealers)` - The dealer that takes in traded cars

## Files
- `Services/Market/UsedCarMarketService.cs`
- `Services/Market/CarPurchaseService.cs`
- `Services/Market/CarSaleService.cs`
- `Services/Scheduler/Tasks/CarAdsReviewTask.cs`
- `Dialogs/SellCar/SellCarDialogViewModel.cs`
- `Services/Market/CarValuation.cs`
- `Services/Dealers/DealerCatalog.cs`
- `Assets/Dealers/dealers.json`
- `Services/Market/IUsedCarMarketService.cs`
- `Models/GameState/UsedCarListing.cs`
- `Models/GameState/DealerLocation.cs`
