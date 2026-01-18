# Street Rod AC - AI Context

## Project Summary
Street Rod-style career mode manager for Assetto Corsa. WPF app manages game logic, economy, progression. AC is physics engine only - never modified directly.

## Tech Stack
- .NET 10, WPF, MVVM pattern
- LiteDB for persistence
- Single main window, screen-based navigation

## Architecture Layers
1. **Meta-Game Layer** - Player progression, economy, car ownership, saves
2. **AC Integration Layer** - Launch via acs.exe, INI modification, result ingestion
3. **Data Layer** - Dual-database: catalog.db (static) + saves/*.db (dynamic)

## Key Architectural Rules
- **Navigation**: Single `NavigationService` with typed factory methods. Screens never instantiate other screens.
- **Dialogs**: Modal overlays, never nested. One dialog at a time.
- **INI Files**: Declare intent, apply minimally, always restore after AC exits.
- **AC Content**: Import to catalog, never modify AC installation.
- **Logging**: Structured, via `IAppLogger`. Categories: App, Import, Market, Navigation, etc.

## Data Models

| Model | Location | Purpose |
|-------|----------|---------|
| CarDefinition | catalog.db | Imported AC car identity (immutable) |
| CarProfile | catalog.db | Gameplay properties: BasePrice, DealerPrecedence |
| UsedCarListing | saves/*.db | Car for sale in market |
| CarInstance | saves/*.db | Player-owned car |
| Opponent | saves/*.db | AI racer with Skill/Aggression traits |
| GameState | saves/*.db | Player money, date, cars, market |

## Current Systems

| System | Service | Purpose |
|--------|---------|---------|
| Catalog | ContentCatalogRepository, CarProfileService | Car import, profiles |
| Market | UsedCarMarketService | Spawn listings, refresh, purchases |
| Opponents | OpponentGenerationService, OpponentEvolutionService | Create/evolve racers |
| Time | GameTimeScheduler, IScheduledTask | Time-based task execution |
| Race | AssettoCorsaLauncher, IniModificationService | Launch AC, modify configs |
| Dialogs | DialogService | Modal overlays |

## File Structure
```
Street Rod AC/
├── Models/           # Data models (Catalog/, GameState/)
├── Services/         # Business logic (Catalog/, Market/, Scheduler/, etc.)
├── Screens/          # UI screens (ViewModel + View per screen)
├── Navigation/       # NavigationService, BaseScreenViewModel
├── Dialogs/          # Dialog system (Confirmation, Information, etc.)
├── Styles/           # WPF ResourceDictionaries
└── Logging/          # IAppLogger infrastructure
```

## Screen Navigation Pattern
```
NavigationService.NavigateTo[ScreenName](dependencies)
  → Creates ScreenViewModel with injected services
  → Calls NavigateTo(screen) internally
  → Screen.Enter() lifecycle hook
```

## Opponent System (AC-Agnostic)
- `Opponent` model stores Skill (80-100) and Aggression (0-100)
- `OpponentAIAdapter` converts to AC AI parameters at runtime (never persisted)
- Evolution happens after races via `OpponentEvolutionService`

## Market System
- `CarProfile.DealerPrecedence` controls spawn probability (0.0=rare, 1.0=common)
- Daily refresh: remove old listings, spawn new based on precedence
- Condition affects price (better condition = higher price)

## Quick Reference - Adding Features

**New Screen:**
1. Create `Screens/[Name]/[Name]ScreenViewModel.cs` (inherit BaseScreenViewModel)
2. Create `Screens/[Name]/[Name]ScreenView.xaml` + `.xaml.cs`
3. Add `NavigateTo[Name]()` method in NavigationService
4. Add DataTemplate in App.xaml

**New Scheduled Task:**
1. Implement `IScheduledTask` (TaskId, IntervalDays, ExecuteAsync)
2. Register in App.xaml.cs via `Scheduler.RegisterTask()`

**New Dialog:**
1. Create ViewModel in `Dialogs/[Name]/`
2. Create View XAML
3. Add DataTemplate in App.xaml
4. Show via `DialogService.ShowDialog(viewModel)`

## Documentation Index
- [Architecture Overview](architecture/overview.md)
- [Data Storage](architecture/data-storage.md)
- [Navigation System](architecture/navigation.md)
- [Catalog System](systems/catalog-system.md)
- [Market System](systems/market-system.md)
- [Opponent System](systems/opponent-system.md)
- [AC Launcher](ac-integration/launcher.md)
- [Screens Index](screens/index.md)
