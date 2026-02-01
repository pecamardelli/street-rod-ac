# Career System Architecture

This document describes the polymorphic career system implementation for Street Rod AC. It provides context for understanding how victory conditions, milestones, events, and car filters work together.

## Overview

The career system provides multiple paths to victory, milestone-based progression that unlocks content, and a dynamic event system with car entry requirements. The system is designed to be extensible - new victory types, milestones, and events can be added without modifying existing code.

## Directory Structure

```
Models/Career/
├── Filters/
│   ├── ICarFilter.cs           # Base interface
│   ├── BrandFilter.cs          # Match by manufacturer
│   ├── DecadeFilter.cs         # Match by year range
│   ├── PowerFilter.cs          # Match by horsepower
│   ├── OriginFilter.cs         # Match by country/region
│   ├── ValueFilter.cs          # Match by purchase price
│   └── CompositeCarFilter.cs   # Combine filters (AND/OR)
├── Victory/
│   ├── IVictoryCondition.cs    # Base interface
│   ├── VictoryProgress.cs      # Progress tracking DTO
│   ├── ReputationVictory.cs    # Reach 100 reputation
│   ├── KingVictory.cs          # Defeat The King
│   ├── PinkSlipCollectorVictory.cs  # Win 10 cars via pink slip
│   ├── SeasonChampionVictory.cs     # Most wins by day 30
│   └── DominationVictory.cs    # Defeat every opponent
├── Milestones/
│   ├── MilestoneTrigger.cs     # Enum of trigger types
│   ├── MilestoneDefinition.cs  # Milestone data model
│   └── MilestoneProgress.cs    # Progress tracking DTO
└── Events/
    ├── RaceEventDefinition.cs  # Event template
    ├── RaceEventInstance.cs    # Active event instance
    └── EventReward.cs          # Reward structure

Models/GameState/
└── CareerState.cs              # Persisted career progress

Services/Career/
├── ICarFilterService.cs / CarFilterService.cs
├── IVictoryConditionService.cs / VictoryConditionService.cs
├── IMilestoneService.cs / MilestoneService.cs
└── IRaceEventService.cs / RaceEventService.cs
```

## Core Concepts

### CareerState (Models/GameState/CareerState.cs)

Central state object that tracks all career progression. Stored as part of `GameState` and persisted with save files.

```csharp
public class CareerState
{
    // Victory tracking
    public string? ActiveVictoryType { get; set; }
    public Dictionary<string, bool> UnlockedVictories { get; set; }
    public bool HasWonGame { get; set; }
    public string? WinningVictoryType { get; set; }

    // Milestone tracking
    public HashSet<string> CompletedMilestones { get; set; }
    public Dictionary<MilestoneTrigger, int> MilestoneCounters { get; set; }

    // Event tracking
    public List<RaceEventState> ActiveEvents { get; set; }
    public HashSet<string> CompletedEventIds { get; set; }

    // Opponent tracking
    public HashSet<string> DefeatedOpponentIds { get; set; }
}
```

**Key methods:**
- `IncrementCounter(trigger, amount)` - Add to a cumulative counter (wins, money earned)
- `SetCounter(trigger, value)` - Set a "current state" counter (cars owned, reputation)
- `GetCounter(trigger)` - Read counter value
- `RecordDefeatedOpponent(name)` - Track unique opponent defeats

### MilestoneTrigger Enum

Defines what game events can trigger milestone progress:

| Trigger | Type | Description |
|---------|------|-------------|
| `TotalWins` | Cumulative | Any race win |
| `DragWins` | Cumulative | Drag race wins |
| `RoadWins` | Cumulative | Circuit/sprint wins |
| `PinkSlipWins` | Cumulative | Pink slip race wins |
| `ReputationReached` | Current | Player's reputation score |
| `MoneyEarned` | Cumulative | Race earnings |
| `CarsOwned` | Current | Cars in garage |
| `DaysPlayed` | Cumulative | Game days elapsed |
| `OpponentsDefeated` | Current | Unique opponents beaten |

## Car Filters

Filters determine car eligibility for events. All filters implement `ICarFilter`:

```csharp
public interface ICarFilter
{
    string FilterType { get; }           // For serialization
    string DisplayDescription { get; }   // Human-readable
    bool Matches(CarDefinition car, Car? instance = null);
}
```

### Filter Types

| Filter | Parameters | Notes |
|--------|------------|-------|
| `BrandFilter` | `Brand` | Case-insensitive match on `CarDefinition.Brand` |
| `DecadeFilter` | `StartYear`, `EndYear` | Both nullable; requires `CarDefinition.Year` |
| `PowerFilter` | `MinHP`, `MaxHP` | Parses `CarDefinition.Specs.Bhp` string |
| `OriginFilter` | `Origin` | Checks `CarDefinition.Country`, falls back to brand-based detection |
| `ValueFilter` | `MinValue`, `MaxValue` | Requires `Car` instance (checks `PurchasePrice`) |
| `CompositeCarFilter` | `Filters`, `RequireAll` | Combines filters with AND/OR logic |

### OriginFilter Brand Mapping

When a car lacks explicit country data, `OriginFilter` uses hardcoded brand-to-country mappings:

- **USA**: Ford, Chevrolet, Dodge, Plymouth, Pontiac, Buick, Cadillac, etc.
- **Japan**: Toyota, Honda, Nissan, Mazda, Mitsubishi, Subaru, etc.
- **Germany**: BMW, Mercedes, Audi, Porsche, Volkswagen, etc.
- **UK**: Jaguar, Aston Martin, Bentley, McLaren, Lotus, etc.
- **Italy**: Ferrari, Lamborghini, Maserati, Alfa Romeo, Fiat, etc.

Supports region aliases: "American" → "USA", "European" → checks all European countries.

### CompositeCarFilter Examples

```csharp
// American muscle: USA origin AND 300+ HP
CompositeCarFilter.And(
    new OriginFilter("USA"),
    new PowerFilter(300, null)
);

// Classic or budget: 1960s cars OR under $5000
CompositeCarFilter.Or(
    DecadeFilter.ForDecade(1960),
    new ValueFilter(null, 5000)
);
```

## Victory Conditions

Victory conditions define win states. All implement `IVictoryCondition`:

```csharp
public interface IVictoryCondition
{
    string VictoryType { get; }
    string Name { get; }
    string Description { get; }
    bool IsUnlocked(CareerState career);
    VictoryProgress GetProgress(CareerState career);
    bool IsAchieved(CareerState career);
}
```

### Victory Types

| Type | Unlock Condition | Win Condition |
|------|------------------|---------------|
| `Reputation` | Always available | Reach 100 reputation |
| `King` | 10 wins + 50 rep | Defeat "The King" in pink slip race |
| `PinkSlipCollector` | 5 pink slip wins | Win 10 cars via pink slip |
| `SeasonChampion` | Reach day 30 | Have most wins at season end |
| `Domination` | 75 reputation | Defeat every opponent at least once |

### Dynamic Victory Conditions

Some victories need external data:

- **SeasonChampion**: Requires comparison with all opponent win counts
- **Domination**: Requires total opponent count

These are configured via callbacks when creating `VictoryConditionService`:

```csharp
VictoryConditionService = new VictoryConditionService(
    getTotalOpponents: gs => gs.Racers.TotalCount,
    playerHasMostWins: gs => IsPlayerSeasonChampion(gs)
);
```

## Milestones

Milestones are achievements that unlock content. Defined in `MilestoneService.CreateDefaultMilestones()`.

### Default Milestones

| ID | Trigger | Target | Unlocks |
|----|---------|--------|---------|
| `first_blood` | TotalWins | 1 | - |
| `veteran` | TotalWins | 10 | King challenge |
| `legend` | TotalWins | 50 | - |
| `street_cred` | ReputationReached | 25 | Muscle car events |
| `respected` | ReputationReached | 50 | - |
| `feared` | ReputationReached | 75 | Domination victory |
| `pink_slip_hunter` | PinkSlipWins | 5 | PinkSlipCollector victory |
| `title_collector` | PinkSlipWins | 10 | - |
| `garage_full` | CarsOwned | 5 | Car shows |
| `car_dealer` | CarsOwned | 10 | - |
| `rival_crusher` | OpponentsDefeated | 5 | - |
| `no_mercy` | OpponentsDefeated | 10 | - |
| `big_winner` | MoneyEarned | 10000 | - |
| `high_roller` | MoneyEarned | 50000 | - |

### Adding New Milestones

Add to `MilestoneService.CreateDefaultMilestones()`:

```csharp
AddMilestone(milestones, new MilestoneDefinition
{
    Id = "unique_id",
    Name = "Display Name",
    Description = "What the player must do",
    Trigger = MilestoneTrigger.TotalWins,
    TargetValue = 25,
    Category = "Racing",
    Unlocks = ["event:some_event", "victory:SomeVictory"]
});
```

## Race Events

Events are special races with entry requirements and rewards.

### Event Definition

```csharp
public class RaceEventDefinition
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public RaceType RaceType { get; set; }
    public ICarFilter? EntryRequirements { get; set; }
    public int MinReputation { get; set; }
    public List<string> RequiredMilestones { get; set; }
    public EventReward Reward { get; set; }
    public EventSchedule Schedule { get; set; }  // OneTime, Daily, Weekly, Permanent
    public bool IsPinkSlip { get; set; }
}
```

### Default Events

| Event | Filter | Min Rep | Reward |
|-------|--------|---------|--------|
| Ford Fanatics | BrandFilter("Ford") | 0 | $500 + 5 rep |
| Budget Brawl | ValueFilter(0, 5000) | 0 | $1000 + 3 rep |
| Muscle Madness | USA + 300+ HP | 25 | $750 + 10 rep + rare part |
| Classic Showdown | Pre-1970 | 15 | $750 + 10 rep |
| Fifties Fever | 1950s | 30 | $1000 + 8 rep |
| Chevy Challenge | BrandFilter("Chevrolet") | 10 | $600 + 5 rep |
| High Stakes | None (pink slip) | 50 | 15 rep |
| Underdog Challenge | Under 200 HP | 0 | $400 + 5 rep |
| European Invasion | OriginFilter("European") | 20 | $800 + 7 rep |
| Sixties Showdown | 1960s | 20 | $850 + 8 rep |

### Event Scheduling

- **OneTime**: Appears once, tracked in `CompletedEventIds`
- **Daily**: Can reappear 24 hours after completion
- **Weekly**: Can reappear 7 days after completion
- **Permanent**: Always available

Events are generated by `RaceEventService.GenerateEvents()` which should be called by the game scheduler.

## Integration Points

### Race Result Processing

`RaceResultProcessor.UpdateMilestoneCounters()` is called after every race:

```csharp
private void UpdateMilestoneCounters(GameState gameState, RaceOutcome outcome, RaceContext? context)
{
    if (outcome.PlayerWon)
    {
        career.IncrementCounter(MilestoneTrigger.TotalWins);

        // Race type specific
        if (context.RaceType == RaceType.DragRace)
            career.IncrementCounter(MilestoneTrigger.DragWins);

        // Pink slip
        if (context.IsPinkSlip)
            career.IncrementCounter(MilestoneTrigger.PinkSlipWins);

        // Track opponent
        career.RecordDefeatedOpponent(context.OpponentName);

        // Money
        if (context.CashWager > 0)
            career.IncrementCounter(MilestoneTrigger.MoneyEarned, (int)context.CashWager);
    }

    // Current state counters (always update)
    career.SetCounter(MilestoneTrigger.CarsOwned, gameState.Player.Cars.Count);
    career.SetCounter(MilestoneTrigger.ReputationReached, gameState.Player.Stats.Reputation);
}
```

### Service Registration (App.xaml.cs)

```csharp
CarFilterService = new CarFilterService();
MilestoneService = new MilestoneService();
VictoryConditionService = new VictoryConditionService(
    getTotalOpponents: gs => gs.Racers.TotalCount,
    playerHasMostWins: gs => IsPlayerSeasonChampion(gs)
);
RaceEventService = new RaceEventService(
    CarFilterService,
    carDefId => CatalogRepository.GetCar(carDefId)
);
```

### Typical Usage Flow

1. **After race completes**: `RaceResultProcessor` updates milestone counters
2. **Check milestones**: Call `MilestoneService.CheckForCompletedMilestones(career)` to get newly completed milestones
3. **Check victory unlocks**: Call `VictoryConditionService.CheckForNewUnlocks(career)`
4. **Check for victory**: Call `VictoryConditionService.CheckForVictory(gameState)` to see if player won
5. **Generate events**: Call `RaceEventService.GenerateEvents(career, currentTime)` on day change

## Extending the System

### Adding a New Filter Type

1. Create class implementing `ICarFilter` in `Models/Career/Filters/`
2. Add case to `CarFilterService.CreateFilter()` for deserialization support

### Adding a New Victory Condition

1. Create class implementing `IVictoryCondition` in `Models/Career/Victory/`
2. Register in `VictoryConditionService` constructor's `_victoryConditions` dictionary
3. If it needs external data, add callback parameter like `getTotalOpponents`

### Adding New Events

Add to `RaceEventService.CreateDefaultEvents()`:

```csharp
AddEvent(events, new RaceEventDefinition
{
    Id = "unique_id",
    Name = "Event Name",
    Description = "Flavor text",
    RaceType = RaceType.DragRace,
    EntryRequirements = new BrandFilter("Pontiac"),
    MinReputation = 20,
    RequiredMilestones = ["street_cred"],
    Reward = new EventReward(1000, 10),
    Schedule = EventSchedule.Weekly
});
```

## Data Persistence

- `CareerState` is a property of `GameState` and persists automatically with save files
- `MilestoneDefinition` and `RaceEventDefinition` are defined in code, not persisted
- `RaceEventState` instances in `ActiveEvents` persist the state of generated events

## Not Yet Implemented

The following UI/integration work remains:

1. **UI for victory progress display** - Show progress in Game screen
2. **UI for milestone notifications** - Popup when milestones complete
3. **Newspaper integration** - Show active events in newspaper
4. **Event generation scheduler task** - Call `GenerateEvents()` on day transitions
5. **Event completion flow** - Connect event races to `RaceEventService.CompleteEvent()`
6. **Days played counter** - Increment `MilestoneTrigger.DaysPlayed` on day transitions
