# Cruise Screen

Roadmap step 13, "the street encounter". Not SLRR's open city: the player sits in their own car at the curb, engine
running, looking out of the driver's window at the lane beside them. Now and then a rival pulls up alongside, blips
the throttle, and makes an offer: a race, where, and for what. The player takes it, changes the stakes, or waves them
off, and they pull away. It is the way Street Rod did it, in 3D.

```
Diner ──[CRUISE THE STREETS]──▶ Cruise ──[DINER] / [GARAGE]──▶
                                  │
                                  ├── a rival pulls up ── YOU'RE ON! ──▶ RaceLoading ──▶ back to Cruise
                                  │                    └─ WAVE OFF ──▶ they drive away, the wait goes on
                                  └── 22:00, the day is over ──▶ Garage
```

**The user decided (2026-09-26):**
- the street is built by the game itself (a CC0 panorama projected on a ground and a dome, written as a KN5), not cut
  out of an installed track nor borrowed from a showroom;
- the encounter is a place of its own, **Cruise**, reached from the diner; the diner's challenge panel stays as it was;
- the light follows the game's clock (day, dusk, night), and the street is busier after 20:00.

## The street

`Assets/Streets/pretville/`: `street_day.kn5`, `street_dusk.kn5`, `street_night.kn5` and `street.json`. It is Poly
Haven's **Pretville Street** (Dimitrios Savva and Jarod Guest, CC0: free to ship, no credit asked; it is given in
`street.json` all the same), a 1950s American film-set street.

**How the scene is made** (`tools/StreetScene`):
1. `prepare_street.py <panorama.hdr> <out> [capture height] [floor half-size]` (numpy and opencv-python) tone-maps the
   8k HDR into `sky_<light>.jpg` (the whole panorama, 4096 × 2048) and `floor_<light>.jpg` (the ground seen from
   above, 40 m square, 4096²), for day, dusk and night. Dusk and night are graded from the day's photo: dimmer, tinted,
   and the sky (a mask of the bright blue above the horizon) swapped for a sky of that hour. It also finds the sun
   (`light.json`).
2. `StreetSceneBuilder <prepared> <out>` writes the three KN5s: a **floor disc** (20 m, lit, takes the cars' shadows,
   drawn with the floor picture), a **ground ring** out to 35 m and a **hemisphere** on it, both drawn with the
   panorama and self-lit (`ksEmissive` 1, `ksAmbient`/`ksDiffuse` 0). Every UV is the panorama seen **from the point
   the photo was taken, 1.7 m over the middle**, so from there the three meet without a seam and the street looks as
   shot; from the driver's seat, a little lower, near enough. The mapping is `u = 0.5 − atan2(x, z)/2π`,
   `v = 0.5 − asin(y)/π`, the same in both tools.

To use another panorama: run both, then write its `street.json` (where the player parks, the lane, the lights) and
check it with the viewport harness (below).

**`street.json`** (read by `Services/Street/StreetScene.cs`): `player` (where the player's car is parked, x/z in metres
from the middle, heading in degrees about the vertical, 0 facing +z), `laneOffset` (the rival's lane, centre to
centre, to the player's left), `rivalStop` (where the rival stops, metres ahead of the player's centre), `approachFrom`
/ `leaveTo`, `sun`, and `lights` per light of the day: key light brightness and colour, ambient, `cubemapAmbient`, and
at night `lamps` (point lights). In Pretville the player faces down the long street (+z) with the pink building's curb
on their right; the rival comes up from the plaza behind and stops by the barbershop.

**The light of the day** (`StreetLights`): the sun sets at about 16:25 at the turn of the year and 20:10 at midsummer
(a cosine through the year); dusk is the hour and a half before it, night from half an hour after. A change of light
while nobody is about loads the street again in the new light.

## The viewport (`Controls/StreetViewport3D.cs`)

A `D3DViewportBase` like the garage's, the lot's and the main screen's:
- **The player's car** stands in the main slot where `street.json` parks it. The camera sits at **the car's own
  driver eyes** (`Kn5RenderableCar.GetDriverCamera()`, from its data) and looks 72° to the left, out of the side
  window, through the car's own interior. Dragging looks round (−35° to 160°, ±20° up and down); a double click looks
  back out. The player's engine runs at idle (`RunningEngine`, as in the garage) and rocks the car; the view rocks with
  it (`CarLean`).
- **The rival's car** takes a second slot and drives the lane (`Controls/Street/RivalDrive.cs`, a function of time):
  cruising in second gear at about 27 mph, braking at 4.2 m/s² to a stop, the clutch in under 3.5 m/s; leaving, a
  launch through a four-speed, a quarter of a second off the throttle at each shift. The engine is turned by the
  wheels while the car rolls (`EngineRunner.Drive`, `EngineSim.TickCoupled`) and idles on its own when stopped. The
  wheels turn (`CarBodyRock` spins the `WHEEL_*` nodes), the body dives on the brakes and squats on the launch
  (`ChassisPitch`, a spring and damper, 0.6° per m/s²), and the brake lights come on. At night both cars have their
  headlights on.
- **`StreetStage`** (`Controls/Street/StreetStage.cs`) is what the screen and the viewport share: the screen sends a
  rival up the lane (`Arrive`) or away (`Leave`); the viewport says when it stopped alongside and when it has gone.
  With no viewport up (no 3D), the rival is there at once and gone at once, so the screen works without the picture.

**Hard-won facts:**
- **The panorama came out mirrored** with `u = 0.5 + atan2/2π` (the street signs read backwards): the renderer's
  space is the other way round from what the maths assumed. A car's own **+x is its left**, the driver's side.
- **The Dark renderer's `CubemapAmbient`** (default 0.5) is not a strength: in its default white mode any value over 0
  lights the cars with a white fill of about the same brightness whatever the value, from the reflections'
  spherical harmonics normalised to their own brightness (`DarkMaterial.Reflection.fx`, `GetAmbient`). At night the
  cars glowed bright orange on a dark street. Dusk and night use 0, which lights them with the ambient colours alone.
- **Its point lights are strong**: a street lamp at brightness 6 flooded a car 4 m away; 0.5–0.6 is a lamp.
- `CarSlot.LocalMatrix` *is* the car node's matrix: a car that moves and rocks cannot be placed by one and rocked by
  the other. `CarBodyRock.Apply` takes the placement and puts the lean on it.
- The first frame comes before the player's car has loaded: a rival sent up before `StreetStage.IsShown` would be
  there at once. The screen's first rival is never due before 10 game minutes (a few seconds).

## Two engines at once

`EngineAudio` has two channels (`EngineChannel.Main`, `Second`): the player's car and the rival's. Each loads its own
bank; two cars on one sound share the bank, as FMOD cannot load one twice. `ReleaseAsync(Second)` lets the rival's go
when the screen does; a race still releases everything (`UnloadAllAsync`). The rival is heard from where the car is:
`EngineVoice.SetDirection` puts the event a metre off in that direction (FMOD's 3D attributes, the listener left at
its default), which pans it without FMOD's own distance falloff, and the volume falls off with distance
(`StreetViewport3D.HearRival`). The player's own engine plays at 55%, heard from inside.

## The encounter (`Services/Street/StreetEncounters.cs`)

- **Waiting:** the clock runs while the player sits there, 5 game minutes every 1.5 s, and stops while a rival is
  about. The wait for the next rival is exponential with a mean of 50 minutes by day, 35 at dusk and 18 from 20:00,
  between 5 and 120.
- **Who:** the racers on the scene with a car that can race and that is installed. A rival with a grudge is four times
  likelier (they come looking), one within 15 reputation of the player 1.5 times, the King a quarter as likely. At
  night the bold are out (weight 0.5 + aggression), by day it hardly matters. A rival met is not met again that night,
  race or not (`MetTonight`, kept for the game day across the race and back).
- **The race:** a drag race 65% of the time, 80% at night, on an installed track of that kind; a drag strip's quarter
  mile when it has one.
- **The offer:** the King and a rival with a grudge want pink slips, and the bet cannot be changed. Anybody else offers
  pink slips now and then (6% on Normal, times 0.5 + aggression, times the difficulty's pink-slip figure), otherwise
  cash: somewhere between the street's minimum and maximum for the hour and the two wallets
  (`MatchupCalculator.WagerLimits`), higher with aggression. A rival nobody can bet with drives on by.
- **Taking it:** the offer as made is taken without asking again; changed terms (the other kind of bet, another sum)
  are put to the rival like a diner challenge ("HOW ABOUT THIS?"), and a no is said through the window with the offer
  still standing. The race goes through **`Screens/Shared/ChallengeLauncher.cs`**, which the diner now uses too: the
  answer, the dial-in for a bracket race, the police roll, both cars on their parts, and the loading screen. After the
  race the player comes back to the street (`ReturnToCruise` on the launch intent), unless towed home.
- **The rival talks** (`TalkTrigger.PulledUp`: lines about the player's car by its short name, `CarNames.Short`; the
  King's and a grudge's own) and blips the throttle every few seconds. The player revs back with the REV button or the
  space bar.
- **The night ends** at 22:00 like any day: the player goes home to the garage, the game is saved.

A player whose car cannot race (or is impounded), or who has none, is sent back to the garage on the way in.

## Checking it without playing

Two throwaway harnesses (in a session's scratchpad, not the repo) did the checking on 2026-09-26:
- **viewport:** a WPF window off screen hosting `StreetViewport3D` with a `StreetStage`, sending a rival up and away and
  capturing frames with `RenderTargetBitmap` (it draws the D3DImage too). Wait for `Stage.IsShown` before `Arrive`.
- **screen:** the real `CruiseScreenViewModel` through `NavigationService.NavigateToCruise`, the services put together
  as `App` does (the ones it never touches passed as null), on a **copy** of a save (`new SaveDatabase(copyFolder)`,
  `SaveName` cleared so nothing is written), with the styles merged from `pack://application:,,,/Street Rod AC;component/Styles/*.xaml`
  and `ContentService.LoadTracksAsync()` called first (the app does that at start-up). It sat through two rivals,
  waved the first off and captured the offer card.

Tested by the user in the game on 2026-09-26, sound included: "It's perfect!"
