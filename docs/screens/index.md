# Screens Index

## Screen Inventory

| Screen | Path | Purpose |
|--------|------|---------|
| Init | `Screens/Init/` | App startup, catalog loading |
| MainMenu | `Screens/MainMenu/` | Main menu (New/Load/Settings) |
| NewGame | `Screens/NewGame/` | Create new save |
| LoadGame | `Screens/LoadGame/` | Load existing save |
| Settings | `Screens/Settings/` | App settings |
| CarCatalogEditor | `Screens/CarCatalogEditor/` | Edit car prices/precedence |
| Game | `Screens/Game/` | In-game hub (calendar view) |
| Garage | `Screens/Garage/` | Player's car collection |
| CarSelection | `Screens/CarSelection/` | Select car for activity |
| Diner | `Screens/Diner/` | Meet opponents, accept challenges |
| Newspaper | `Screens/Newspaper/` | Used car ads, news |
| UsedCarMarket | `Screens/UsedCarMarket/` | Browse/buy used cars |
| UsedParts | `Screens/UsedParts/` | Browse/buy used parts |

## Navigation Flow

```
Init → MainMenu
         ├── NewGame → Game
         ├── LoadGame → Game
         └── Settings → CarCatalogEditor

Game ←→ Garage ←→ CarSelection
  │
  ├──→ Diner (challenges)
  │
  └──→ Newspaper → UsedCarMarket
                 → UsedParts
```

## Screen Structure
Each screen folder contains:
- `[Name]ScreenViewModel.cs` - Logic, commands, state
- `[Name]ScreenView.xaml` - UI layout
- `[Name]ScreenView.xaml.cs` - Code-behind (InitializeComponent only)

## NavigationService Methods

| Method | Target Screen |
|--------|---------------|
| NavigateToInit() | Init |
| NavigateToMainMenu() | MainMenu |
| NavigateToNewGame() | NewGame |
| NavigateToLoadGame() | LoadGame |
| NavigateToSettings() | Settings |
| NavigateToCarCatalogEditor() | CarCatalogEditor |
| NavigateToGame(gameState) | Game |
| NavigateToGarage(gameState) | Garage |
| NavigateToCarSelection(gameState) | CarSelection |
| NavigateToDiner(gameState) | Diner |
| NavigateToNewspaper(gameState) | Newspaper |
| NavigateToUsedCarMarket(gameState) | UsedCarMarket |
| NavigateToUsedParts(gameState) | UsedParts |

## Adding a New Screen

1. Create folder: `Screens/[Name]/`
2. Create ViewModel inheriting `BaseScreenViewModel`
3. Create View (UserControl XAML)
4. Add navigation method to `NavigationService`
5. Add DataTemplate to `App.xaml`

See [Navigation System](../architecture/navigation.md) for details.
