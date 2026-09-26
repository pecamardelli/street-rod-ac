# Screens Index

## Screen Inventory

| Screen | Path | Purpose |
|--------|------|---------|
| Init | `Screens/Init/` | App startup, catalog loading |
| MainMenu | `Screens/MainMenu/` | Main screen: cars filmed in the showrooms, the menu over them (see `main-screen.md`) |
| NewGame | `Screens/NewGame/` | Create new save (a card on the main screen) |
| LoadGame | `Screens/LoadGame/` | Load existing save (a card on the main screen) |
| Settings | `Screens/Settings/` | App settings (a card on the main screen) |
| CarCatalogEditor | `Screens/CarCatalogEditor/` | Edit car prices/precedence |
| Garage | `Screens/Garage/` | In-game hub: 3D garage with the selected car, overlay buttons to every other in-game screen |
| CarSelection | `Screens/CarSelection/` | Select car for activity |
| Diner | `Screens/Diner/` | Meet opponents, accept challenges |
| Cruise | `Screens/Cruise/` | Sit at the curb in your car; rivals pull up with an offer (see `cruise.md`) |
| Newspaper | `Screens/Newspaper/` | Street News (articles on the last 3 days' races, `NewsWriter`), race invitations, used car and parts ads, the player's ads |
| DealerMap | `Screens/DealerMap/` | The city, with a pin per dealer |
| DealerLot | `Screens/DealerLot/` | One dealer's cars parked in 3D; click one to look at it |
| UsedCarMarket | `Screens/UsedCarMarket/` | Browse/buy used cars as a flat list |
| UsedParts | `Screens/UsedParts/` | Browse/buy used parts |

## Navigation Flow

```
Init → MainMenu (NewGame, LoadGame and Settings are cards on it, not screens)
         ├── NewGame card → Garage
         ├── LoadGame card → Garage
         └── Settings card → CarCatalogEditor → MainMenu with the Settings card open

Garage (hub) ←→ CarSelection
  │
  ├──→ Diner (challenges)      [Hit the streets]
  │      └──→ Cruise ──→ RaceLoading ──→ Cruise   [Cruise the streets]
  ├──→ Career                   [Career stats]
  ├──→ DealerMap → DealerLot    [Car dealers]
  └──→ Newspaper → UsedCarMarket
                 → UsedParts

Garage Back button → MainMenu (with confirmation)
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
| NavigateToMainMenu(card) | MainMenu, optionally with a card open |
| CreateNewGameCard(back) | NewGame card (not a navigation) |
| CreateLoadGameCard(back) | LoadGame card (not a navigation) |
| CreateSettingsCard(back) | Settings card (not a navigation) |
| NavigateToCarCatalogEditor() | CarCatalogEditor |
| NavigateToGarage(gameState) | Garage |
| NavigateToCarSelection(gameState) | CarSelection |
| NavigateToDiner(gameState) | Diner |
| NavigateToCruise(gameState) | Cruise |
| NavigateToNewspaper(gameState) | Newspaper |
| NavigateToDealerMap(gameState) | DealerMap |
| NavigateToDealerLot(gameState, dealerId) | DealerLot |
| NavigateToUsedCarMarket(gameState) | UsedCarMarket |
| NavigateToUsedParts(gameState) | UsedParts |

## Adding a New Screen

1. Create folder: `Screens/[Name]/`
2. Create ViewModel inheriting `BaseScreenViewModel`
3. Create View (UserControl XAML)
4. Add navigation method to `NavigationService`
5. Add DataTemplate to `App.xaml`

See [Navigation System](../architecture/navigation.md) for details.
