# WPF Styling

## Visual Philosophy
Dark, minimal, Assetto Corsa-like. Flat UI, no default Windows controls.

## Resource Dictionaries

| File | Purpose |
|------|---------|
| `Styles/Colors.xaml` | `Color` tokens: every colour the UI uses, by name |
| `Styles/Brushes.xaml` | `SolidColorBrush` for each colour used as a brush (`<Name>Brush`), plus the background and button art |
| `Styles/Typography.xaml` | Font size scale (`sys:Double`) |
| `Styles/Buttons.xaml` | Button styles and templates (merges Brushes and Typography itself) |

All four are merged in `App.xaml` and referenced with `{StaticResource …}`. Views don't use hex colours or
literal font sizes: a colour or a size that isn't in `Styles/` yet gets a token there first.

## Colour tokens
Every colour exists as a `Color` (for `GradientStop`, `DropShadowEffect`, `ColorAnimation`) and, when a view paints
with it, as a `<Name>Brush`. Name conventions:

- `GrayNN` is `#NNNNNN` (`Gray50` = `#505050`), so the name says the exact shade.
- A hex suffix is the alpha byte: `BgPanelCC` = `#CC1A1A1A`, `Cream70` = `#70F5E6C8`, `BlackB0` = `#B0000000`.

| Group | Tokens |
|-------|--------|
| Backgrounds | `BgDark` #1B1B1B, `BgMedium` #2A2A2A, `BgLight` #3A3A3A, `BgPanel` #1A1A1A |
| Text | `TextPrimary` #E0E0E0, `TextSecondary` #B0B0B0, `TextDisabled` #606060 |
| Accent | `AccentPrimary` #D32F2F, `AccentHover` #F44336, `AccentPressed` #B71C1C, `AccentSuccess` #90EE90 |
| Borders | `BorderDark` #101010, `BorderLight` #404040 |
| Greys | `Gray26`, `Gray30`, `Gray33`, `Gray50`, `Gray70`, `Gray80`, `GrayA0`, `White` #FFFFFF |
| Translucent panels | `BgPanelAA`, `BgPanelCC`, `BgPanelF2`, `BgDarkDF`, `Glass12C0`, `Glass12D0`, `Glass14D0`, `Glass14E0`, `Glass15E8` (`GlassNN` = `#NNNNNN` under the alpha) |
| Veils | `Overlay` #99000000 (behind dialogs), `BlackTransparent` #00000000, `BlackHitTest` #01000000, `Black77`, `BlackB0`, `BlackCC`, `BlackE6`, `White20`, `White30`, `WhiteB0` |
| Status | `Gold` #FFD700 (+`GoldCC`, `Gold33`), `Amber` #FFC107, `Warning` #FFA500, `WarningStrong` #FF8C00, `WarningBg` #3D2F1F, `Danger` #FF6B6B, `DangerStrong` #CC0000, `DangerBg` #3D1F1F, `SuccessBg` #2A3A2A, `GoldBg` #3A3A2A, `Salmon` #FF8C6B, `SuccessStrong` #00AA00, `SuccessHover` #7CCD7C, `Fit` #59FF73 (+`Fit30`), `PaleYellow` #FFE8A0, `Info` #4A90D9, `InfoBg` #1A2A3A, `RoyalBlue` #4169E1, `SpinnerOrange` #FF6B00, `RedZone` #60C02020 |
| Dealer map and lot | `Cream` #F5E6C8 (+`Cream50`, `Cream60`, `Cream70`), `MapSepia` #6B4A1E, `MapDotFar` #6E6A60, `MapDotFarBorder` #A09A8C, `MapFarText` #E8A15A |
| License input | `LicenseBrown` #2C1810 |

`BlackTransparent` is not WPF's `Transparent` (#00FFFFFF): a gradient fading to it stays black instead of greying
through white.

## Font size scale (`Styles/Typography.xaml`)

| Token | Size | Token | Size |
|-------|------|-------|------|
| `FontSizeMicro` | 9 | `FontSizeCardTitle` | 21 |
| `FontSizeTiny` | 10 | `FontSizeSubtitle` | 22 |
| `FontSizeCaption` | 11 | `FontSizeTitleSmall` | 24 |
| `FontSizeSmall` | 12 | `FontSizeTitle` | 28 |
| `FontSizeCompact` | 13 | `FontSizeTitleLarge` | 30 |
| `FontSizeBody` | 14 | `FontSizeScreenTitle` | 32 |
| `FontSizeBodyLarge` | 15 | `FontSizeDisplaySmall` | 36 |
| `FontSizeSubheading` | 16 | `FontSizeDisplay` | 48 |
| `FontSizeHeading` | 18 | `FontSizeHero` | 72 |
| `FontSizeHeadingLarge` | 20 | | |

## Images
Backgrounds (`MainBackgroundBrush`, `InitBackgroundBrush`, `NewspaperBackgroundBrush`, `DinerBackgroundBrush`,
`SettingsBackgroundBrush`) and button art (`…ButtonBrush`) are `ImageBrush`es over a `BitmapImage` resource
(`…BackgroundImage`, `…ButtonImage`). The `BitmapImage` sets `DecodePixelWidth` near the size it is drawn at
(backgrounds 1920, button art 300; the ~290 px back/exit/garage icons load as they are), `CacheOption="OnLoad"`,
and both are frozen with `PresentationOptions:Freeze="True"`. The source PNGs are 2560×1440 and up to 1024², so
decoding them full size would keep ~15 MB per background alive in the application resources. Add new art the same
way.

## Converters
Declared in `App.xaml` unless noted.

- `BooleanToVisibilityConverter`, `InverseBooleanToVisibilityConverter`, `InverseBooleanConverter`,
  `StringToVisibilityConverter`, `GaragePanelToVisibilityConverter`, `PercentageToWidthConverter`.
- `DifficultyToBrushConverter` — an `OpponentDifficulty` as a brush. The brushes are set where it is declared
  (`Easy`, `Matched`, `Hard`, `Other`), so the colours stay in the view. `DinerScreenView.xaml` declares two:
  `DifficultyTextBrushConverter` (AccentSuccess / Gold / Danger / White) and `DifficultyBorderBrushConverter`
  (SuccessStrong / Warning / DangerStrong / BorderLight).

View models never return colours. They expose the state (an enum, a bool, an int) and the view picks the brush with
a converter like the one above or with `DataTrigger`s, e.g. the matchup advantage (`Advantage` → AccentSuccess /
Danger / Gray80) in the Diner and the invitation expiry/eligibility (`IsExpiringSoon`, `HasEligibleCars`) in the
Newspaper.

## Button Styles
- `MenuButtonStyle` - Large menu buttons (300x300)
- `ExitButtonStyle` - Back/exit buttons (64x64)
- `DialogPrimaryButtonStyle` - Dialog confirm buttons
- `DialogSecondaryButtonStyle` - Dialog cancel buttons
- `GameButtonStyle` - Large image buttons with hover labels (Newspaper screen)
- `OverlayTileButtonStyle` - Image tiles floating over the 3D garage, animated hover (scale, glow, slide-up label) via VisualStateManager

## Rules

| Do | Don't |
|----|-------|
| Define styles in XAML | Inline styling |
| Use ResourceDictionaries and tokens | Hardcode colors or font sizes |
| Replace default control templates | Default Windows look |
| Subtle hover states | Heavy animations |
| Dark neutral palette | Bright accent colors |
| Expose state from view models | Return colours from view models |

## Window Configuration
There is one window, `MainWindow.xaml`:
- `WindowStyle="None"`, `AllowsTransparency="True"`, maximized - no system chrome.
- Screens are swapped in its content host; dialogs show in its overlay (`OverlayBrush`) inside a
  `ScrollViewer`, so a long dialog scrolls instead of being cut off.

## MVVM Compliance
- Views contain NO logic
- No event handlers in code-behind (except UI glue)
- All visuals from styles/templates
- Commands for all actions

## Files
- `Styles/Colors.xaml`
- `Styles/Brushes.xaml`
- `Styles/Typography.xaml`
- `Styles/Buttons.xaml`
- `App.xaml` - Merged dictionaries and converters
