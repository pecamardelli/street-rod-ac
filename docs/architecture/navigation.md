# Navigation System

## Core Concept
Single main window hosts interchangeable screens. Navigation is state transition, not window management.

## Screen Model
- Each screen = ViewModel + View pair
- Screens are self-contained, no knowledge of other screens
- Lifecycle hooks: `Enter()`, `Exit()`

## NavigationService Pattern

### Factory Methods (Mandatory)
Every screen has a typed factory method in `NavigationService`. Each returns `bool`: false when the player did not get
there (callers that don't care use it as a statement).

```
NavigateToGarage(gameState)
NavigateToUsedCarMarket(gameState)
NavigateToMainMenu(card)
NavigateToCarCatalogEditor()
```

The main screen's cards (New Game, Load Game, Settings) are the one exception: they open over the main screen instead of
replacing it, so `NavigationService` only builds them (`CreateNewGameCard(back)` and the rest) and the main menu hosts
them, calling their `Enter()`/`Exit()` itself. See `docs/screens/main-screen.md`.

### How It Works
1. Screen calls `_navigationService.NavigateTo[Screen](params)`
2. NavigationService creates the ViewModel, passing the services it needs from its own fields
3. It goes through `SafeNavigate`, which calls `NavigateTo(screen)`
4. Previous screen's `Exit()` called
5. New screen's `Enter()` called
6. View resolved via DataTemplate in App.xaml

### Services
NavigationService is constructed once in `App.xaml.cs` with every service a screen needs (plus a callback that sets
`App.CurrentGameState`), and hands them to the screens' constructors. Screens never look services up through
`(App)Application.Current`: a screen's dependencies are all in its constructor.

### Failure Handling (`SafeNavigate`)
- A screen whose constructor throws is never shown.
- A screen whose `Enter()` throws is exited again and the previous screen is re-entered.
- Either way the error is logged, an InformationDialog tells the player, and the factory returns false.
- An `async void Enter()` is only covered up to its first `await`, so each one guards its own body. Loading work
  (lists, files) runs in `Enter()`, not in the constructor.

## Rules

| Rule | Reason |
|------|--------|
| Screens never instantiate other screens | Decoupling |
| No `new ScreenViewModel()` in screen code | Use factory methods |
| All navigation paths in NavigationService | Single source of truth |
| Pass required data as parameters | Compile-time safety |

## Adding a New Screen

1. Create `Screens/[Name]/[Name]ScreenViewModel.cs`
   - Inherit `BaseScreenViewModel`
   - Constructor takes NavigationService + dependencies (services, never `App` lookups)

2. Create `Screens/[Name]/[Name]ScreenView.xaml`
   - UserControl with UI

3. Create code-behind `.xaml.cs`
   - Just `InitializeComponent()`

4. Add to `NavigationService.cs` (add a constructor parameter and field for any service it needs that is not there yet):
   ```csharp
   public bool NavigateTo[Name](params) => SafeNavigate("[name] screen", () =>
       new [Name]ScreenViewModel(this, _dialogService, ...));
   ```

5. Add DataTemplate in `App.xaml`:
   ```xml
   <DataTemplate DataType="{x:Type ns:[Name]ScreenViewModel}">
       <ns:[Name]ScreenView/>
   </DataTemplate>
   ```

## Anti-Patterns
- Multiple windows for navigation
- WPF Frame/Page navigation
- Code-behind navigation logic
- Generic factories or service locators
- UI driving state instead of ViewModel

## Files
- `Navigation/NavigationService.cs`
- `Navigation/BaseScreenViewModel.cs`
- `Navigation/IScreen.cs`
