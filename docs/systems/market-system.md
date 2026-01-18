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
Based on `DealerPrecedence`:
- Roll random for each car definition
- If `random < precedence`: spawn 1-3 instances
- Higher precedence = more instances

| Precedence | Type | Instances |
|------------|------|-----------|
| 0.7-1.0 | Common | 1-3 |
| 0.4-0.6 | Performance | 0-2 |
| 0.1-0.3 | Exotic | 0-1 |

## Daily Refresh
1. Remove sold listings older than 7 days
2. Remove unsold listings older than 14 days
3. Spawn new listings to target size (30-50)

## Pricing
- Start with `CarProfile.BasePrice`
- Multiply by condition factor (0.3 = 50%, 1.0 = 110%)
- Add random variation ±20%
- Round to nearest $100

## Dealer Locations
Five default dealers with regions:
- Downtown Motors, Eastside Garage, Suburban Autos, Riverside Cars, Industrial Motors

## Purchase Flow
1. Validate funds and availability
2. Create `CarInstance` from listing
3. Add to `GameState.Player.Cars`
4. Deduct money
5. Mark listing `IsSold = true`
6. Save game state

## Key Service
`UsedCarMarketService` (`IUsedCarMarketService`):
- `SpawnListings(dealers, date)` - Initial spawn
- `RefreshMarket(listings, dealers, date)` - Daily refresh
- `GetAvailableListings(listings)` - Filter unsold

## Files
- `Services/Market/UsedCarMarketService.cs`
- `Services/Market/IUsedCarMarketService.cs`
- `Models/GameState/UsedCarListing.cs`
- `Models/GameState/DealerLocation.cs`
