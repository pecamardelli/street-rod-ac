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

## Pricing
- Start with `CarProfile.BasePrice`
- Multiply by condition factor (0.3 = 50%, 1.0 = 110%)
- Add random variation ±20%
- Round to nearest $100

## Dealer Locations
Ten dealers, defined in `Assets/Dealers/dealers.json` and read by `DealerCatalog`:
- Downtown Motors, Eastside Garage, Suburban Autos, Riverside Cars, Industrial Motors

The save holds only `DealerLocation` (Id, Name, Region). Map position, showroom, parking bays and stock
character live in the JSON and are merged over the save on load, so new fields reach old saves. See
`docs/screens/dealer-lot.md`.

## Which Dealer Gets a Car
Not random. Each dealer claims a slice of the market's price range (`priceBandLow`/`priceBandHigh`); a car
goes to one of the dealers whose slice covers where its base price sits, or to the nearest slice if none
does. `conditionCenter` then pulls the condition roll toward that dealer's standard (two parts dealer, one
part roll), so a cheap lot has rough cars without making a good find impossible.

## Purchase Flow
`CarPurchaseService` owns this, shared by the listings screen and the dealer lot.

1. Validate funds and availability
2. Create `CarInstance` from listing
3. Add to `GameState.Player.Cars`
4. Deduct money
5. Mark listing `IsSold = true`
6. Save game state

## Key Service
`UsedCarMarketService` (`IUsedCarMarketService`):
- `SpawnListingsAsync(dealers, date)` - Initial spawn
- `RefreshMarketAsync(listings, dealers, date)` - Daily refresh. Engines are put together only for the listings that
  make it into the market, on a worker thread; the listings are looked at and handed back on the calling thread
- `GetAvailableListings(listings)` - Filter unsold

## Files
- `Services/Market/UsedCarMarketService.cs`
- `Services/Market/CarPurchaseService.cs`
- `Services/Dealers/DealerCatalog.cs`
- `Assets/Dealers/dealers.json`
- `Services/Market/IUsedCarMarketService.cs`
- `Models/GameState/UsedCarListing.cs`
- `Models/GameState/DealerLocation.cs`
