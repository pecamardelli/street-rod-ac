# Main Screen

The first screen after start-up (roadmap step 9). A few installed cars stand together in a showroom and are filmed in
slow camera moves, the way Gran Turismo's menus do it, the camera staying with one of them at a time. The menu is laid over them, and New Game, Load Game and
Settings open as cards in the menu's place: the screen never navigates away to show them.

```
MainMenu ──▶ card: New Game ──▶ Garage
         ├─▶ card: Load Game ──▶ Garage
         └─▶ card: Settings ──▶ CarCatalogEditor ──▶ back to MainMenu, Settings card open
```

## Layout

- **The cars** fill the bottom 80% of the window (`ShowcaseViewport3D`, rows 1–2 of the view's grid). The frame is
  wide, about 2.2:1 on a 16:9 screen, so the car on show sits in the lower middle of the screen, clear of the menu.
- **The fade to black** is at the top (user's change, 2026-09-25): solid black down to 20% of the height, then fading
  out by 46%. The menu and the cards stand in it.
- **The menu** is the three round badges (Load Game, New Game, Settings) in a row, each labelled under it
  (`MenuButtonStyle` in `Styles/Buttons.xaml`). A badge lifts and glows gold under the pointer.
- **A card** (`MenuCardStyle`) is dark glass with a thin edge. It hangs from the top, over the fade, and a tall card
  (New Game) reaches down over the cars. The menu fades out and lifts away, then the card drops in. Back or Esc reverses it (Esc is caught at the
  window, since the clicked badge is hidden and takes the focus out of the screen; not while a dialog is up). Only one
  is ever open, and a click can't land on either while they swap. The code is in `MainMenuScreenView.xaml.cs`. A card
  that fails to build or open is reported like a screen (`NavigationService.SafeOpenCard`). If starting or loading a
  game fails, the main screen is resumed with the card the player was using open again.
- **The name of the car the camera is on** shows small, in italics, at the bottom right, with a shadow.
- **The screen opens black** and the first scene fades up in it (user's change: the old picture used to hold the
  screen while it loaded). The old picture (`Backgrounds/main.png`) only comes up when there is nothing to show: no
  Assetto Corsa folder, no installed catalog cars or no scene found (`MainMenuScreenViewModel.NothingToShow`), or a
  viewport that failed (no GPU in a remote session, three rooms in a row whose cars would not load, or no room whose
  model would load). A room whose model will not load is dropped for the rest of the visit (`ShowcasePlaylist.Drop`).

## What it shows

- **Cars:** the catalog's `Active` cars that are installed (`InstalledCars.Only`, then `ShowcaseContent.Cars`). A car shows in a random skin of
  the catalog's `AvailableSkins`, or its default skin when that folder is missing. It is titled "Year Name" unless
  the name already has the year. The player's own cars are not used, since no save is loaded at this point (the
  user's decision).
- **Rooms:** `Assets/Showcase/scenes.json` lists them with the radius of their nearest wall. Each id is looked for as
  the game's own garage first (`Assets/Garages/<id>`), then as an installed AC showroom. The list: `garage` (Rob
  Taylor's, see the showroom note in the memory), `Hangar`, `showroom`, `industrial` and `beach`. `at_previews` (a
  black stage with stray white panels) and `studio_white` (bare) are left out.
- **Lineups** (`ShowcasePlaylist`, `ShowcaseLineup`): each room gets three cars, or four where the nearest wall is
  20 m or more away, all different. Every car comes round once, in a shuffled order, before any comes round again,
  and the room is never the one just left. The cars stand in one of three layouts: side by side (3.4 m apart), an
  echelon of angled bays, or loosely parked. The layout is turned any way, centred, and kept 1.2 m inside the walls,
  with fewer cars when it won't fit. They all load behind the black before the scene fades up (about 0.6–1.4 s),
  within a 900 MB geometry budget (`CarModelFiles.EstimateBytes`, counted only for cars that load). The cars that
  loaded are then stood again in a layout for as many as there are, so a car that failed leaves no gap, and the car in
  the renderer's main slot (which shadows and reflections are worked out from) takes the middle.
- **The camera** stays with up to three of them in turn, three shots each, then fades to the next room.

## The film

All the math is pure, in `Controls/Showcase` (`ShowcaseShots`, `ShowcaseLineup`), and unit-tested in `MainScreenTests`.

`Controls/Showcase/ShowcaseShots.cs` holds the moves, as pure math over an orbit pose (target, radius, alpha,
beta: what AcTools' `CameraOrbit` takes). The car stands at the origin with its nose to +Z. Alpha 0 is off its left
side and π/2 is in front.

| Shot | What it does |
|---|---|
| FrontPushIn | low off the front three-quarter, drifting in |
| SideTrack | alongside and level with the car, running its length |
| RearLowSweep | down by the rear three-quarter, turning round the tail |
| HighOrbit | from higher up, going slowly round |
| WheelCloseUp | close on a front wheel |
| NoseLow | right down at the nose, sweeping across the grille |

- Each shot lasts about 7 s (±0.8 s). The move is mostly steady, with a little easing at either end. Half the shots
  play in reverse, and now and then a shot is taken from the car's other side. The same kind never plays twice
  running.
- **Framing** comes from the four `WHEEL_*` dummies (`CarFrame.FromWheels`). The renderer's own box for a car takes in
  its shadow planes and comes out a metre or more too long (a '69 Camaro measures ±3.5 m). A model with no sensible
  wheels gets `CarFrame.Nominal`.
- **Walls:** every pose, including those between the ends of a move, is pulled in along its own line of sight until
  the camera is 1.5 m inside the wall, and lifted to at least 0.18 m over the floor (`KeepInside`). A camera through a
  wall renders the back of it.
- **Other cars:** a shot on one car keeps clear of the others all the way through (`ShowcaseLineup.Choose`, sampled at
  nine points). The camera stays 1.4 m from any other car, since nearer, the lens clips into bodywork and a car beside
  it fills a corner of the wide frame. It stays 0.6 m from its own car. And it never looks at its car through another
  (a camera high enough sees over a roof). A car's footprint is its wheels plus 1.0 m ahead, 1.3 m behind and 0.32 m
  each side. A shot the walls pull in below 75% of its framed distance counts as spoilt. Kinds not used lately are
  tried first, from both sides. A simulation of 4,200 lineups (every room size) found a clear shot for every car.
- **Placement:** a car's `CarPlacement` (X, Z, heading in radians) goes on its slot as `RotationY(heading) *
  Translation`, and a shot framed in car space is taken to the room by the same rotation (world alpha = alpha −
  heading). A node's `Matrix` in AcTools is world (`LocalMatrix * ParentMatrix`, updated eagerly), so the wheel hubs
  are taken back into car space through the inverse of the car's own matrix. Without that, every car but one standing
  in the middle was framed somewhere it wasn't, and the camera ended up inside cars.
- **Transitions:** three shots per car.
  - Between two shots of one car, and from one car to the next in the same room, the last frame dissolves into the
    next shot over 1 s. The still comes from
    `D3DImage.CopyBackBuffer` (`SharedTextureBridge.CopyFrame`), about 2 ms at 1080p.
  - Between rooms, the picture fades out over 0.8 s (started before the last shot ends, so the camera is still moving
    as it goes). The renderer is rebuilt behind the black, keeping the D3D9 device, the cars load, and after four
    frames of settling it all fades up over 1.2 s.
- A car that won't load is left out of its room; a room with none goes on to the next, and after three in a row the
  screen shows its picture.
- There's no sound, by the user's choice.

## Numbers (2026-09-25, off-screen harness)

- A room with its three or four cars loads in 0.6–1.4 s. The first scene is up about 2 s after the screen opens.
- Over 3 minutes of swapping rooms and lineups, the process stayed at 450–650 MB working set with no growth.

## Known issues

- Encrypted car models (normals scrambled, which only CSP undoes) draw as shattered glass in every viewer of the
  game. The catalog import now leaves them out (`Services/Catalog/EncryptedCars.cs`), so they never reach this
  screen, the dealers or the rivals. They stay in AC's cars folder. On 2026-09-25 there were eight: `buick_gsx`,
  `mv_plymouth_fury_1958_enc`, `lm_camaro_ss`, `lm_galaxie_500`, `rpm_falcon_sprint`, `dske_dodge_charger`,
  `exmods_dodge_charg_daytona` and `exmods_dodge_zeder_z250`.
- The Hangar is dim: a dark car there is hard to make out.
