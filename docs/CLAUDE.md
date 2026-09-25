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
3. **Data Layer** - Dual-database: catalog.db (static) + saves/*.db (dynamic), both under `%AppData%\StreetRodAC` (`Catalog\catalog.db`, `Saves\{name}.db`, next to `settings.json`; see `docs/architecture/data-storage.md`)

## Key Architectural Rules
- **Navigation**: Single `NavigationService` with typed factory methods. Screens never instantiate other screens. Screens get their services through NavigationService (constructor parameters), never from `(App)Application.Current`.
- **Dialogs**: Modal overlays, never nested. One dialog at a time: one requested while another is open is queued and shows after it, never replacing it. A dialog closes before it runs its callback.
- **INI Files**: Declare intent, apply minimally, always restore after AC exits. Every cfg INI write goes through `IniModificationService` (`IniText`, line- and encoding-preserving, written atomically by `SafeFile`); the original is kept first under `%AppData%\StreetRodAC\AcRestore\~cfg` (`AcConfigBackup`) and put back in the launcher's `finally`, at start-up, on exit and on the fatal path, never while an AC process runs.
- **Saves**: One open save database at a time (`SaveDatabase`), shared by the game state and race session repositories; the Load screen lists saves by header without switching it.
- **Errors**: Global handlers (dispatcher, AppDomain, unobserved tasks) in `App.xaml.cs`. A recoverable UI exception is logged and shown in a dialog; a fatal one runs the fatal path once (save the game, restore the AC install if AC is not running, shut FMOD down, close the save database, flush the log). Screens guard their own `async` paths; `AsyncRelayCommand` logs what escapes.
- **AC Content**: Import to catalog, never modify AC installation. Two exceptions. The game's own race mode (`apps\new-modes\sr_race`) is installed into `extension\lua\new-modes\sr_race` before every race (`SrRaceMode`: written when it differs, left there, the old `apps\lua\sr_race_manager` app removed). And a race: a car's data files and engine sound, as its parts make them, go in through `CarDataOverlay` (originals kept with a manifest, put back in the launcher's `finally` and at start-up, once no AC process runs), and an opponent in the player's model races in a marked copy of the car folder that the same cleanup deletes. Anything that scans `content\cars` skips folders with the copy marker (`AcCarFolder.IsClone`).
- **Logging**: Structured, via `IAppLogger`. Categories: App, Import, Market, Navigation, etc.

## Data Models

| Model | Location | Purpose |
|-------|----------|---------|
| CarDefinition | catalog.db | Imported AC car identity (immutable) |
| CarProfile | catalog.db | Gameplay properties: BasePrice, DealerPrecedence |
| UsedCarListing | saves/*.db | Car for sale in market |
| CarInstance | saves/*.db | Player-owned car |
| PartInstance | saves/*.db | A part somebody owns, with what is mounted on it: `Car.Parts` (engine on car slot 1), `Player.Parts` (shelf), `UsedCarListing.Parts`, `NewspaperAds.Parts` |
| Opponent | saves/*.db | AI racer with Skill/Aggression traits |
| GameState | saves/*.db | Player money, date, cars, market |

## Current Systems

| System | Service | Purpose |
|--------|---------|---------|
| Catalog | ContentCatalogRepository, CarProfileService | Car import, profiles |
| Market | UsedCarMarketService, CarValuation | Spawn listings, refresh, purchases; one formula for what a car is worth |
| Opponents | OpponentInitializationService, OpponentLifeService, EngineTuner, OpponentEvolutionService | Rivals on the street: daily review (buy, repair, tune, go broke, come back), the King (see `docs/systems/opponent-system.md`) |
| Time | GameTimeScheduler, IScheduledTask | Time-based task execution |
| Race | AssettoCorsaLauncher, IniModificationService, RaceResultIngestionService | Launch AC, modify and restore configs, stop a race, read results (`LaunchResult.Outcome`, `PlayerMessages`) |
| Dialogs | DialogService | Modal overlays |
| Parts | PartsCatalog, PartScriptRuntime, EngineDyno, AcEngineData, AcRunningGearData | SLRR parts: run their scripts on the player's build, dyno, AC data (see `docs/systems/parts-system.md`) |
| Cars' parts | CarPartsService, PartsShopService, Workbench, EngineFactory, RunningGearFactory | Factory engine and running gear per car, part trees on cars, plausibly tuned used cars, garage workbench (parts picked in 3D), parts shop |
| Race data | RaceCarDataService, CarDataOverlay | What the parts make of a car goes into its AC data for a race and comes out after (see `docs/systems/parts-system.md`, "Apply and restore") |
| Engine sounds | SoundLibrary, SoundMatcher, AcCarSound | A sound per engine block: `Assets\Sounds` folders plus every installed car's bank, swapped in for the race by `CarDataOverlay` |

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
NavigationService.NavigateTo[ScreenName](dependencies) : bool
  → SafeNavigate: creates ScreenViewModel with the services from NavigationService's fields
  → Calls NavigateTo(screen) internally
  → Screen.Enter() lifecycle hook (loading happens here, not in the constructor)
  → A constructor or Enter() that throws: logged, dialog shown, previous screen kept, returns false
```

## Opponent System (AC-Agnostic)
- `Opponent` model stores Skill (90-100; the floor of 90 is deliberate, commit 61ca613: below it AC's AI drives too badly to make a race. Never lower it) and Aggression (0-100)
- `OpponentAIAdapter` converts to AC AI parameters at runtime (never persisted)
- Evolution happens after races via `OpponentEvolutionService`
- The rivals live day to day (`OpponentLifeService`, the `opponent_review` task): `Cars[0]` is the car a rival drives,
  `Car.PowerHp` its dyno figure; what they do goes into `GameState.StreetTalk` (the diner's "word on the street")

## Market System
- `CarProfile.DealerPrecedence` controls spawn probability (0.0=rare, 1.0=common)
- Daily refresh: remove old listings, spawn new based on precedence
- Condition affects price (better condition = higher price)

## AC-Side Components (Outside C# Project)

| Component | Location | Purpose |
|-----------|----------|---------|
| Street Corsa race mode | `apps\new-modes\sr_race\` (installed to `extension\lua\new-modes\sr_race\` by `SrRaceMode`) | Auto-start, crash and false-start judging, race results JSON, auto-quit; physics API via `ALLOW_PHYSICS_ALTERATIONS` |
| Crash Penalty Mode | `C:\GAMES\Street Rod AC\extension\lua\new-modes\crash-penalty-tournament\` | Penalty tracking (unused) |
| FFB Limiter | `C:\GAMES\Street Rod AC\extension\lua\ffb-postprocess\upper-limit\` | Direct drive protection |
| SR Race Manager (Lua app, removed) | was `apps\lua\sr_race_manager\` | Became the race mode 2026-09-24 (an app gets no physics API); `SrRaceMode` deletes it from the install |
| Python Race App (removed) | was `apps\python\StreetRodRaceApp\` | Superseded by SR Race Manager; deleted 2026-09-24, in git history only |

**Key Integration Points**:
- Every race, drag races too, is a one-lap race session (`TYPE=3`, never AC's drag session, whose rules teleport the cars) in the `sr_race` CSP mode: `[RACE] __CM_CUSTOM_MODE=sr_race` and `[STREET_ROD] RACE_TYPE` in race.ini (`IniModificationService`), with AC's damage and tyre wear on in assists.ini for the race (kept and restored like race.ini)
- The mode writes results to `Documents/Assetto Corsa/out/sr_race_manager/*.json` (schema 1.3: `end_reason`, `false_start`, `disqualified`, the car's `condition`) and quits via `ac.shutdownAssettoCorsa()`
- Launcher reads JSON results after AC process exits

## Quick Reference - Adding Features

**New Screen:**
1. Create `Screens/[Name]/[Name]ScreenViewModel.cs` (inherit BaseScreenViewModel)
2. Create `Screens/[Name]/[Name]ScreenView.xaml` + `.xaml.cs`
3. Add `bool NavigateTo[Name]()` in NavigationService through `SafeNavigate`, passing services from its fields (add a constructor parameter for a new one)
4. Add DataTemplate in App.xaml

**New Scheduled Task:**
1. Implement `IScheduledTask` (TaskId, IntervalDays, ExecuteAsync)
2. Register in App.xaml.cs via `Scheduler.RegisterTask()`

**New Dialog:**
1. Create ViewModel in `Dialogs/[Name]/`
2. Create View XAML
3. Add DataTemplate in App.xaml
4. Show via `DialogService.ShowDialog(viewModel)` (queued if another dialog is open)

## Documentation Index
- [Roadmap](roadmap.md): next steps (damage, timeslips, economy, living opponents, police), decisions and research
- [Architecture Overview](architecture/overview.md)
- [Data Storage](architecture/data-storage.md)
- [Navigation System](architecture/navigation.md)
- [Catalog System](systems/catalog-system.md)
- [Market System](systems/market-system.md)
- [Opponent System](systems/opponent-system.md)
- [Parts System](systems/parts-system.md)
- [AC Launcher](ac-integration/launcher.md)
- [CSP Lua Scripts](ac-integration/csp-lua-scripts.md)
- [Python Race App (removed, for reference)](ac-integration/python-app.md)
- [Screens Index](screens/index.md)
