# WPF Styling

## Visual Philosophy
Dark, minimal, Assetto Corsa-like. Flat UI, no default Windows controls.

## Resource Dictionaries

| File | Purpose |
|------|---------|
| `Styles/Colors.xaml` | Color definitions |
| `Styles/Brushes.xaml` | SolidColorBrush resources |
| `Styles/Buttons.xaml` | Button styles and templates |

## Key Brushes
- `MainBackgroundBrush` - Primary background
- `TextPrimaryBrush` - Main text color
- `TextSecondaryBrush` - Muted text
- `AccentPrimaryBrush` - Action buttons
- `BorderDarkBrush` - Subtle borders

## Button Styles
- `MenuButtonStyle` - Large menu buttons (300x300)
- `ExitButtonStyle` - Back/exit buttons (64x64)
- `DialogPrimaryButtonStyle` - Dialog confirm buttons
- `DialogSecondaryButtonStyle` - Dialog cancel buttons
- `GameButtonStyle` - Game screen buttons with hover labels

## Rules

| Do | Don't |
|----|-------|
| Define styles in XAML | Inline styling |
| Use ResourceDictionaries | Hardcode colors |
| Replace default control templates | Default Windows look |
| Subtle hover states | Heavy animations |
| Dark neutral palette | Bright accent colors |

## Window Configuration
- `WindowStyle="None"` - No system chrome
- Custom title bar with close/minimize
- `AllowsTransparency="True"` if needed

## MVVM Compliance
- Views contain NO logic
- No event handlers in code-behind (except UI glue)
- All visuals from styles/templates
- Commands for all actions

## Files
- `Styles/Colors.xaml`
- `Styles/Brushes.xaml`
- `Styles/Buttons.xaml`
- `App.xaml` - Merged dictionaries
