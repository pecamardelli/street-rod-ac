# Data Storage

## Dual-Database Strategy

### Catalog Database (`Data/Catalog.db`)
**Purpose:** Static game rules and content definitions
**Access:** Read-only during gameplay

| Collection | Model | Purpose |
|------------|-------|---------|
| cars | CarDefinition | Imported AC car identity |
| carprofiles | CarProfile | Gameplay properties (price, precedence) |
| tracks | TrackDefinition | Imported AC track identity |

### Save Database (`Saves/{name}.db`)
**Purpose:** Dynamic player progress
**Access:** Read/Write

| Collection | Model | Purpose |
|------------|-------|---------|
| gamestate | GameState | Root save object |
| - | UsedCarMarket | List of cars for sale |
| - | Player.Cars | Owned car instances |
| - | Opponents | AI racers |
| - | ScheduledTasks | Task execution state |

## Key Models

### CarDefinition (Immutable)
- `Id` - AC folder name (stable identifier)
- `Name`, `Brand`, `Year` - Display info
- `Source` - Kunos/DLC/Mod
- `Status` - Active/Legacy/Broken

### CarProfile (Mutable Gameplay)
- `CarDefinitionId` - Foreign key
- `BasePrice` - Calculated or manual
- `DealerPrecedence` - Spawn probability (0.0-1.0)
- `Source` - Generated/Manual

### GameState (Save Root)
- `CurrentDate` - Game time
- `Money` - Player balance
- `UsedCarMarket` - Active listings
- `Player.Cars` - Owned CarInstances
- `Opponents` - AI racers
- `ScheduledTasks` - Task state

## Repository Pattern

| Repository | Interface | Purpose |
|------------|-----------|---------|
| ContentCatalogRepository | IContentCatalogRepository | Car/track definitions |
| CarProfileRepository | ICarProfileRepository | Car profiles |
| GameStateRepository | IGameStateRepository | Save load/save |

## Rules
- Catalog IDs derived from folder names (stable, no GUIDs)
- Saves reference catalog by ID, not embedded data
- Legacy content stays in catalog (marked status)
- Profiles persist across AC content updates

## Files
- `Services/Catalog/ContentCatalogRepository.cs`
- `Services/Catalog/CarProfileRepository.cs`
- `Services/GameState/GameStateRepository.cs`
- `Models/Catalog/CarDefinition.cs`
- `Models/Catalog/CarProfile.cs`
- `Models/GameState/GameState.cs`
