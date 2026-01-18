# Navigation System

## Core Concept
Single main window hosts interchangeable screens. Navigation is state transition, not window management.

## Screen Model
- Each screen = ViewModel + View pair
- Screens are self-contained, no knowledge of other screens
- Lifecycle hooks: `Enter()`, `Exit()`

## NavigationService Pattern

### Factory Methods (Mandatory)
Every screen has a typed factory method in `NavigationService`:

```
NavigateToGarage(gameState)
NavigateToUsedCarMarket(gameState)
NavigateToSettings()
NavigateToCarCatalogEditor()
```

### How It Works
1. Screen calls `_navigationService.NavigateTo[Screen](params)`
2. NavigationService creates ViewModel with dependencies
3. NavigationService calls `NavigateTo(screen)` internally
4. Previous screen's `Exit()` called
5. New screen's `Enter()` called
6. View resolved via DataTemplate in App.xaml

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
   - Constructor takes NavigationService + dependencies

2. Create `Screens/[Name]/[Name]ScreenView.xaml`
   - UserControl with UI

3. Create code-behind `.xaml.cs`
   - Just `InitializeComponent()`

4. Add to `NavigationService.cs`:
   ```csharp
   public void NavigateTo[Name](params)
   {
       var screen = new [Name]ScreenViewModel(this, ...);
       NavigateTo(screen);
   }
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
