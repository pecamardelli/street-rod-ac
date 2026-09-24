# Data Storage

## Where Things Live
Everything the game writes is under `%AppData%\StreetRodAC`, never next to the exe (a build folder, or Program Files):

| Path | What |
|------|------|
| `Catalog\catalog.db` | The catalog database (`CatalogDatabase.DefaultPath`) |
| `Saves\{name}.db` | One save per file (`SaveDatabase.DefaultSavesDirectory`) |
| `settings.json` | `GameSettings` (`GameSettingsService`), written atomically |
| `AcRestore\` | Originals of what a race changes in the AC install and cfg, until they are put back (see `docs/ac-integration/ini-modification.md`, `CarDataOverlay`) |

Older versions kept `catalog.db` among the saves and `settings.json` next to the exe. Each is moved over once, the
first time the new version needs it: `catalog.db` from `Saves\` (under the catalog lock), `settings.json` from
`AppDomain.BaseDirectory`. `catalog` is a reserved save name, and the save list ignores it and LiteDB's side files
(`*-log`, `*-tmp`), so the catalog can never be loaded, overwritten or deleted as a save.

## Dual-Database Strategy

### Catalog Database (`%AppData%\StreetRodAC\Catalog\catalog.db`)
**Purpose:** Static game rules and content definitions
**Access:** Read-only during gameplay; one opener at a time (`CatalogDatabase.Open`)

| Collection | Model | Purpose |
|------------|-------|---------|
| cars | CarDefinition | Imported AC car identity |
| carprofiles | CarProfile | Gameplay properties (price, precedence) |
| tracks | TrackDefinition | Imported AC track identity |

### Save Database (`%AppData%\StreetRodAC\Saves\{name}.db`)
**Purpose:** Dynamic player progress
**Access:** Read/Write, through one open database (`SaveDatabase`)

`SaveDatabase` holds one long-lived `LiteDatabase`: the save in use. `GameStateRepository` and the race session
repository share it, so a race's session record and the state it changes are written in one transaction
(`InTransaction`), and every use holds a lock (the UI saves while the race pipeline reads on a worker). Opening another
save closes the one before it. The Load screen lists saves with `ListSaveHeaders()`, which projects only the player's
name, money, game date and last-played time: through the open instance for the save in use, and through a short
read-only open for the others (`SaveDatabase.Peek`), so listing never closes the game being played. `Save` keeps three
rotating backups (`{name}.db.backup1..3`). The repository is disposed on exit and on the fatal path, after the last
save.

| Collection | Model | Purpose |
|------------|-------|---------|
| gamestate | GameState | Root save object |
| - | UsedCarMarket | List of cars for sale |
| - | Player.Cars | Owned car instances |
| - | Racers | AI racers |
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
- `Date` - Game time
- `Player.Money` - Player balance
- `UsedCarMarket` - Active listings
- `Player.Cars` - Owned CarInstances
- `Racers` - AI racers
- `ScheduledTasks` - Task state
- `Career` - Career progress and event states
- `PendingRace` - The race AC was started for and has not come back from; `LaunchedAt` is set once AC actually started, which decides whether a race with no result is forfeited or just released (see `docs/ac-integration/launcher.md`, "The pending race")

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
- `Services/Catalog/CatalogDatabase.cs`
- `Services/Catalog/ContentCatalogRepository.cs`
- `Services/Catalog/CarProfileRepository.cs`
- `Services/Storage/GameStateRepository.cs`
- `Services/Storage/SaveDatabase.cs`
- `Services/Settings/GameSettingsService.cs`
- `Models/Catalog/CarDefinition.cs`
- `Models/Catalog/CarProfile.cs`
- `Models/GameState/GameState.cs`
