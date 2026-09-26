# CSP Lua Scripts

## Overview
Custom Shaders Patch (CSP) Lua scripts extend Assetto Corsa's functionality. These scripts run inside AC and are located in `C:\GAMES\Street Rod AC\extension\lua\`.

**Important**: These scripts are in the AC installation, NOT the C# project. The one exception is the game's race
mode, which lives in the repo (`apps/new-modes/sr_race/`), is installed into the AC install before every race and
writes the race results the launcher reads (see "Integration with C# Launcher").

## Street Corsa Scripts

### 1. Street Corsa race mode (`apps/new-modes/sr_race/`, installed as `extension/lua/new-modes/sr_race/`)

**Purpose**: Runs every race: starts it, judges crashes and false starts, writes the result file, quits AC.
A mode and not an app since 2026-09-24: `ALLOW_PHYSICS_ALTERATIONS=1` in its manifest grants the `physics.*` API,
which an app only gets from a track that opts in through its `surfaces.ini` (none of ours do). As an app, the
control lock on a crash did nothing.

**Files**: `mode.lua`, `manifest.ini`, `siren.wav`. Edit them in the repo: `SrRaceMode` writes the repo's copy (shipped next to
the game under `AcModes\sr_race`) over the installed one before every race.

**Selected by** `[RACE] __CM_CUSTOM_MODE=sr_race` in race.ini (the key CSP reads; `MODE=` and
`__CM_NEW_MODE_USED` are not). The manifest has **no `BASE_MODE`**, so race.ini's session stands. Checked in the
game 2026-09-24: `physics.allowed()` true, `physics.setCarBodyDamage` and `setCarEngineLife` take and read back.

**Every race is a one-lap race session (`TYPE=3`), drag races too**, with `JUMP_START_PENALTY=0`: AC's drag session
(`TYPE=7`) disqualifies for crossing lanes, resets jump starts and runs matches, all by teleporting the cars, and the
mode owns those calls. On `ks_drag` both cars start side by side, one per lane (10.8 m apart), AC's start lights
give the green (the rival leaves on it), and the lap ends at the layout's finish line. The mode also blocks pit
teleports (`physics.blockTeleportingToPits`) and switches car recovery off (`ac.disableCarRecovery`). race.ini
`[STREET_ROD] RACE_TYPE=DRAG|ROAD` tells the mode which kind of race it is. The rival AI drifts up to about 2 m off
its lane on the strip, well inside the 10.8 m between lanes.

**Drag race contact**: crossing lanes is fine (nobody judges lanes on the street), but when the two cars touch
between the green and the line (`ac.onCarCollision` with `collidedWith` non-zero), the car further out of its own
lane (measured sideways from where it stood on the line) is disqualified. The player's disqualification ends the race
(`DISQUALIFIED`); a disqualified rival is held where it is and the player finishes to win, even when the hit crashed
the player.

**Bracket races** (step 11): race.ini `[STREET_ROD] DIAL_IN=player,rival` makes a drag race a bracket race.
- **The trees:** each lane gets its own, a sportsman's tree: three ambers 0.5 s apart, the green 0.5 s after the
  last. The slower dial-in's tree starts 2 s after AC's start, and the other lane's comes later by the difference in
  dial-ins. CSP can't hide AC's own start lights, so the mode tells the player to wait for their own tree, drawn on
  the right of the screen with the dial-in under it.
- **The rival** is held (`setAIThrottleLimit`, `setAIStopCounter`) until its green, then let go a moment after it:
  released, woken, put in first gear. `BRACKET_RIVAL=reaction,margin` sets how long it takes to leave (0.10 s for AI
  level 100, up to 0.40 s) and how far over its dial-in it aims (0.02 to 0.14 s).
- **The rival takes the stripe:** from half way down the quarter, a rival going faster than the pace that gets it to
  the quarter on its dial-in plus its margin is held to that pace (`physics.setAITopSpeed`), so AC's AI brakes to it.
  It is never made faster.
- **Red light:** leaving the stage beam (0.2 m) before your green. The player's red light is a `FALSE_START`, the
  existing no-contest rule.
- **The finish:** the race is to the quarter, not AC's line (on `drag400` the quarter is 2.3 m past it). It ends
  once both cars have crossed the quarter, the rival is out, or 15 s after the player crossed. The winner: a car
  that broke out (ran quicker than its dial-in) loses, unless the other broke out by more; otherwise the first to
  the quarter. "Past the line" means the quarter for crashes, breakdowns and contact.
- **The result:** each slip has `green_s`, its `reaction_s` counted from that green, and the positions are the
  bracket's.

The career decides again from the slips with the same rules (`BracketRules.Decide`). The mode decides on the slips'
own numbers (rounded to the millisecond, added up in the same order), not its unrounded clocks, so a car a fraction
of a millisecond either side of its dial-in is the same breakout to both. The contract test checks that both agree
on the harness's bracket race.

**Test-and-tune** (step 11): `RACE_TYPE=TUNE` puts the player alone on the strip (`CARS=1`, `LAPS=50` so AC never
ends the session) for `TUNE_PASSES` passes (the garage sends 6).
- **Staging:** the car is held with forced brakes until AC's start. Then it is held 3 s on the line before each pass.
- **Each pass:** the brakes come off at the first amber, and from there the player holds the car. The pass gets its
  own tree and timeslip. A red light is only marked (`red_light`) and the pass goes on.
- **End of a pass:** past the quarter and under 20 km/h, standing still 3 s after leaving, or 60 s after the green.
  The slip then shows for 6 s.
- **Back to the line:** `setCarVelocity` to 0, then `setCarPosition` on the car's spot, facing down the strip. The
  mode's own teleport is not a trip to the pits. Each pass is measured from where the car stands when the tree comes
  on (`stagedAt`), so a car put back short of or past its spot still stages.
- **Writing the result:** the file is written after every pass, so closing AC keeps what was run. Each write
  replaces the last: `io.move(tmp, json, false)`, since CSP's `io.move` fails onto an existing file by default.
- **Ending the session:** the last pass, a crash or a breakdown ends it, and so does the player going to the pits
  (the pits menu, or AC putting the car back). A pass under way then is kept as one of the passes.
- **The result:** one participant with `passes` (each slip with its `pass` number); its `timeslip` is the best of
  them.

**Starting the race**: the game loads onto the pits menu, where a mode's `prepare()` and `update()` are not
called. A module-level `setInterval` presses Drive (`ac.tryToStart(true)`); then `ac.setStartMessage` puts the mode
in its preparation stage and `prepare()` ends it after 0.5 s.

**Crash**: the change of velocity over one frame, `|dv| / dt / 9.81 >= 15 g`, counted only within 0.25 s of an
`ac.onCarCollision` event (the method race-explorer's Test Drive mode settled on, which uses 10 g; a street race
leaves a car that takes a knock to finish). A teleport gives the car 1 s of grace: one AC reports (`ac.onCarJumped`),
or a car that moves more than 2 m further in one frame than its speed allows (the drag race's reset after a
disqualification reports nothing, and read as two crashes). A car past the line never crashes: the race is the
player's to finish, whoever finished first. A crashed player is stopped
(`physics.setCarNoInput`, `lockUserControlsFor`, `forceUserBrakesFor`, all three: one alone lets the car coast or be
driven) and the race ends; a crashed rival is held where it lies (`setAIThrottleLimit(i, 0)`, `setAIStopCounter`,
every frame) and the player still has to finish.

**False start**: the player's car more than 1 m from its grid spot before the green (`sim.isSessionStarted`), or AC
putting the car back (`ac.onCarJumped`) before it has gone 20 m. With AC's own penalties and teleports off, the first
is the rule; the second stays for a car AC moves anyway. Put back after 20 m is `ABANDONED`.

**The police chase** (road races only, since step 6). race.ini lists the police as `[CAR_2]` and `[CAR_3]`, after the
two racers. `[STREET_ROD]` tells the mode:
- `POLICE=2,3`: which cars they are;
- `POLICE_MODE`: `TRAPS` (two patrols in three) or `PATROL`;
- `POLICE_SPOT`: for a patrol, how far round the lap it shows up.

The career rolls whether the police come at all (`PoliceRules`, `PoliceCars.Patrol`); the mode runs the chase. The
rules are the user's (2026-09-24):
- **One cop for each racer.** Each sticks to its prey and races it like a racer, with AC's own AI and its overtaking:
  - AI level 150% (`physics.setAILevel(i, 1.5)`: CSP takes 0 to 2, race.ini stops at 100);
  - full aggression (0.95, which is AC's 100%);
  - both set again every 0.5 s, since CSP resets the aggression;
  - a mild rubber band: `setExtraAIGrip` from AC's own 1.2 up to 1.6 the further back the cop is.
- **The police car is the Monaco with the 426 Hemi:** 425 hp gross, 3.23 axle, its own engine sound. With the factory
  440 it couldn't keep up with a 450 hp Chevelle, and boxing it in, a PIT and a pushed rubber band were tried and
  dropped for this.
- **Waiting:** hidden (`ac.setCarActive(i, false)`) with nothing colliding with them (`physics.disableCarCollisions`).
  Held on the grid with the throttle limit and the stop counter every frame, and kept from retiring
  (`physics.preventAIFromRetiring`). Not `setAINoInput`: Test Drive found a car parked with it never drives again.
- **Speed traps:** before the green each cop is parked on the verge of a straight.
  - The spots are picked at random from the AI line, sampled every 10 m between 12% and 92% of the lap.
  - A spot turns less than 12° over 120 m and has 4.5 m of room at one side. There's one per stretch of the lap.
  - The cop parks 1.3 m in from the edge, at most 5 m off the line, visible with its lights off.
  - The first racer to go 0–25 m past a trap who has no cop after them yet sets it off: lights on, and it pulls out.
    A racer with a cop already leaves it for the other one.
  - A trap nobody set off stays parked. With no room for a trap anywhere, the police come as a patrol instead.
- **Pulling out** (a trap, a dodged roadblock): the Test Drive restart. Released, woken, revving in first gear, and
  pushed along the road at 6 m/s. Released alone, a cop sat on the verge.
- **The patrol** shows up when the player has driven `POLICE_SPOT` of the lap. Each cop is put on the AI line 200 m
  behind its own prey, shown, and sent off at 80% of the prey's speed. The way Test Drive puts a car back:
  - stopped;
  - placed a hand's breadth over the road (`physics.raycastTrack`);
  - facing the opposite of where it will look (`setAICarPosition` takes it that way);
  - woken, with the engine running and stalling off;
  - then pushed.
- **Busted = overtaken.** A cop 3 m ahead of its prey along the road for 0.5 s, after having been behind it, has them.
  - The player: "Busted!", `physics.setCarAutopilot` and `physics.setGentleStop` brake the car to a stop, and the race
    ends `BUSTED` once it has stopped (12 s at most).
  - The rival: held where it stops, and the player races on.
  - The cop stops by them, lights on.
  - A car stopped off the road, which a cop on its line may never get past, is busted with a cop within 8 m and
    under 15 km/h for 3 s.
  - Crashing or breaking down while chased is busted too.
- **Roadblock:** a cop more than 700 m back for 8 s is parked 450 m ahead of its prey, once a chase.
  - It sits at 50° on the wider side of the road, 3 m out from the line, with the other lane open. Square across a
    county road the Monaco left no way through.
  - AC's AI brakes to a stop behind a parked car rather than going round it. So an AI-driven racer (the rival, or the
    player under an autopilot) is steered into the open lane when a roadblock is within 150 m ahead.
  - Dodged (the prey 15 m past it, judged from where it was put, since the car reads its old place until the physics
    runs): the cop goes after them at full throttle.
  - A roadblock ahead of its prey never counts as getting past it.
- **Stuck:** a cop going nowhere for 6 s is put back 250 m behind.
- **Lights and siren:**
  - two `ac.LightSource`s on the roof, red and blue in turn about twice a second, with a glow drawn over each in
    `script.draw3D` (`render.circle`). AI cars' own light bars can't be switched from Lua.
  - The siren is `siren.wav` in the mode's folder, a 3D looping `ac.AudioEvent` placed on the car every frame. It is
    synthesized by `tools/siren/make_siren.py`, so the game ships no recording.
- **Got away:** the cop after the player more than 600 m back along the road for 20 s, no cop left after them, or 4
  minutes gone. That cop is put away; the rival's chase goes on on its own (the police give up on the rival after 4
  minutes too).
- **The finish line is home.** A racer over the line is out of the police's reach.
  - The player crossing it ends the race, and a player still chased got away.
  - The rival crossing it gets away, and their cop is called off.
  - Past the line nothing counts: no crash, no bust.
  - The race itself is decided by the racers' own order at the line (the police are in AC's race too, and one put
    down ahead can lead it).
- **Whose chase counts:** only a racer a cop has been after has a chase to win or lose (`playerChased`,
  `rivalChased`).
- A wrecked cop (15 g) is out of the chase.

The police are never in `participants`: the result has a `pursuit` block instead (schema 1.5). AC's own HUD
leaderboard still lists them ("POL").

**Reading lists from race.ini**: `ac.INIConfig:get` splits a value at its commas, and with a string default returns
only the first item. `readNumbers` asks with no default and gets the list: before that, `POLICE=2,3` read as one cop,
and the damage keys of four values (`CAR_n_BODY`, `CAR_n_SUSPENSION`) never reached AC at all (fixed 2026-09-24,
checked in the game: a car sent in with `CAR_0_BODY=25,0,0,0` finished with 25 km/h on the front).

**Tested in the game** (2026-09-24, unattended, Black Cat County at 21:00, the Monaco Police, the player on
`physics.setCarAutopilot`): the cops appear behind at speed and drive; a player who stops is busted with the cop
braked to a stop 5 m behind; with the cops held back, both roadblocks are passed, a stuck cop is put back, the player
gets away mid-race and still wins at the line. 21:00 is full night. Speed traps: parked on the verge, set off by the
player going past, pulled out and chased, a bust; the light bar lights the player's cockpit blue at night. With the
Hemi and one cop for each racer (2026-09-25): a 140 hp Bel Air and a 130 hp Packard were both overtaken and busted,
the Bel Air pulled over by the autopilot; a 450 hp Chevelle kept its lead to the line and the race ended there. The chase logic runs
outside the game in `tools/sr_race_harness/test_chase.py` (lupa, a stub round track). The bracket races and the
test-and-tune run in `tools/sr_race_harness/test_strip.py`, on the same stub laid out as a straight strip.
Neither has been driven in the game yet.

**Time of day**: race.ini's `[LIGHTING] SUN_ANGLE` follows the game's clock when the race starts (Content Manager's
formula, 0 at 13:00 and 16 degrees an hour; CSP takes angles past 80, so a race after 20:00 is run in the dark). A
free run is at noon.

**End reasons** (`session.end_reason`): `FINISHED` (the player crossed the line, `WIN` or `LOSE` by position),
`CRASH`, `FALSE_START`, `DISQUALIFIED`, `BROKE_DOWN`, `BUSTED` (caught by the police before the line), `ABANDONED`. The race ends only when the player crosses the line, whoever
finished first. Quitting AC before any of them writes nothing: the launcher settles a race with
stakes that brings back no result as a forfeit.

**Other rules kept from the app**: written once (`sessionEnded` latches), AC always quits (the write runs under
`pcall`), one tick counts for at most 1 s of distance or race time, the file is written as `.tmp` and renamed.

---

### 2. Crash Penalty Tournament (`new-modes/crash-penalty-tournament/`)

**Purpose**: Track crash penalties for tournament-style races.

**Files**:
- `mode.lua` - Main script
- `manifest.ini` - Mode registration

**Key Features**:
| Feature | Implementation |
|---------|----------------|
| Crash detection | `car.damage` threshold monitoring |
| Penalty tracking | Per-car penalty accumulation |
| UI display | Transparent window showing penalties |
| Collision tracking | `car.collidedWith` + speed threshold |

**Configuration Constants**:
```lua
PENALTY_PER_CRASH = 5.0          -- Seconds added per crash
DAMAGE_THRESHOLD = 0.15          -- Damage delta to trigger (0-1)
COLLISION_SPEED_THRESHOLD = 30   -- km/h minimum for crash
```

**Data Structures**:
```lua
carPenalties[carIndex] = totalPenaltySeconds
carDamageStates[carIndex] = { lastDamage, crashes }
carCollisionStates[carIndex] = { lastCollision, lastSpeed }
```

**Note**: AC doesn't allow modifying final race times. Penalties are displayed/logged only.

---

### 3. FFB Upper Limit (`ffb-postprocess/upper-limit/`)

**Purpose**: Limit force feedback for direct drive wheels with collision dampening.

**Files**:
- `ffb.lua` - Active version (collision-aware)
- `ffb.bak.lua` - Simple limit version (backup)

**Active Version Behavior**:
- Unlocks FFB limits for direct drive wheels
- Zeros FFB for 1 second after high G-forces (acceleration > 2G)
- Zeros FFB for 1 second after collision (collision depth > 0, position Y > 0.1)

**Simple Version** (backup):
- Hard limit at 200% (`limit = 2`)
- No collision/G-force logic

**CSP API**:
```lua
ac.unlockFFBLimits(true)  -- Allow FFB > 100%

function script.update(ffbValue, ffbDamper, steerInput, steerInputSpeed)
  return modifiedFFB, ffbDamper
end
```

---

## Standard CSP Content (Not Custom)

Located in the extension folder but NOT Street Rod specific:

| Folder | Purpose |
|--------|---------|
| `internal/lua-shared/` | LuaSocket networking library |
| `lua/tools/csp-traffic-tool/` | AI traffic editor/simulation |
| `lua/tools/csp-railworks-tool/` | Train/railroad simulation |
| `lua/cars/android_auto/` | Android Auto in-car display |
| `lua/fireworks/` | Holiday fireworks effects |
| `lua/pp-filters/` | Post-processing filters (VHS, vintage, etc.) |
| `lua/joypad-assist/` | Gamepad steering assist modes |
| `lua/chaser-camera/` | Camera modes (drone, arcade) |

---

## Integration with C# Launcher

### Current State
The `sr_race` mode runs the race and reports it. The C# launcher:
1. Installs the mode (`SrRaceMode`) and writes race.ini with `__CM_CUSTOM_MODE=sr_race`, and assists.ini with damage on
2. Launches AC and waits for the process to exit
3. Reads the result the mode wrote to `Documents/Assetto Corsa/out/sr_race_manager/*.json`

### Damage

Every race runs with AC's damage at 100% and tyre wear on (assists.ini). What AC reports in `condition` goes onto the
car (`CarCondition.ApplyRace`), and back into AC at the start of the next race:

| AC | After the race | Next race |
|----|----------------|-----------|
| `body_damage_kmh[4]` | `Car.BodyDamageKmh` (a car's parts have no panels); the worse of the car's and AC's per zone | `CAR_n_BODY`, set with `physics.setCarBodyDamage`: the scratches and dents of `damage.ini` come with it |
| `engine_life` | The weakest rotating part (crankshaft, rods, pistons, camshafts; the rods first when two are as worn) is the one that gave: its `Tear` goes down to life/1000, and the others lose 35% of that loss. The report and the repair shop name it ("a connecting rod let go") | `CAR_n_ENGINE_LIFE` = 1000 x the weakest one's `Tear`, set with `physics.setCarEngineLife` |
| `gearbox_damage` (AC starts at 0) | subtracted from the transmission's `Tear` | No setter: drivetrain.ini shifts slower and engages in a narrower window (`AcDamageData`); `CAR_n_GEARBOX` tells the mode, which adds it to the race's |
| `suspension_damage` (metres of steering rod, AC's MAX_DAMAGE 0.05) | /0.05 subtracted from that corner's spring and shock `Tear` | No setter: suspensions.ini `TOE_OUT` of the axle gains the mean bend of its corners in metres; `CAR_n_SUSPENSION` tells the mode |
| `tyre_wear` (AC starts at 0) | subtracted from the tyre's `Wear`; `tyre_blown` sets its `Tear` to 0 | Nothing: the tyre's wear already lowers its grip in tyres.ini. AC's tyre km would count it twice |
| distance | a little `Wear` on every engine part (1/40000 per km) | |

The mode puts a car out of the race (a breakdown) when its engine life reaches 0, its gearbox's carried wear plus
AC's gearbox damage reaches 1, a corner's carried bend plus AC's reaches 1, or a tyre blows. A car the last races
left blown, with a gearbox or a corner under 10%, a blown tyre, or a body of 200 km/h or more (totaled) does not race
or go on a free run until it is repaired (`CarCondition.WhyCannotRace`). An opponent's car races whatever shape it is
in, with just enough to leave the line (`CarCondition.Runnable`), until opponents look after their cars.

The garage's Repairs button (`RepairShop`) takes `Tear` off: an engine rebuild, a gearbox rebuild, a straightened
corner, a new tyre for a blown one, body work. A part repair costs 60% of the damaged parts' new price as far as the
damage goes, plus $15; body work $8 per km/h. Small jobs take `GarageWorkMinor` (30 min), big ones
`GarageWorkMajor` (2 h). Mileage (`Wear`) is not repaired: a worn part is replaced.

Oil temperature, oil pressure and water temperature are reported but not used: CSP fills oil figures only for cars
with a script, and overheating is for the game to model.

### Communication Methods

| Method | Status | Notes |
|--------|--------|-------|
| `ac.shutdownAssettoCorsa()` | Working | The mode quits AC once the result is written |
| Signal files | Removed | Was in Python app, didn't work reliably |
| race.ini `[STREET_ROD] CONTEXT_ID` | Working | Launcher to the mode: which race this is (see below) |
| race.ini `[STREET_ROD] CAR_n_*` | Working | Launcher to the mode: the damage car n carries into the race (see below) |
| race.ini `[STREET_ROD] POLICE`, `POLICE_SPOT` | Working | Launcher to the mode: which cars are the police, and how far round the lap they show up |
| race.ini `[STREET_ROD] DIAL_IN`, `BRACKET_RIVAL` | Working (harness) | Launcher to the mode: a bracket race's dial-ins, and how the rival leaves and takes the stripe |
| race.ini `[STREET_ROD] RACE_TYPE=TUNE`, `TUNE_PASSES` | Working (harness) | Launcher to the mode: a test-and-tune, and how many passes |
| Result JSON | Working | The mode to the launcher, one file per race |

### Race Result File
The mode writes one file per race to `Documents\Assetto Corsa\out\sr_race_manager\<session_id>.json`: AC's own
Documents folder (`ac.getFolder(ac.FolderID.ACDocuments)`), which is the known Documents folder the launcher reads even
where Documents is redirected (OneDrive). The file is written as `<name>.tmp` and then renamed, so a file under the
final name is always whole; the launcher ignores names that are not a UUID. `metadata.source` is still
`sr_race_manager`, the id the launcher checks. Script version 3.0.0.

Schema 1.1 added three fields to what 1.0 had (the launcher still reads 1.0 files, without them):

| Field | Type | Meaning |
|-------|------|---------|
| `session.context_id` | string, may be absent | The race's `RaceContext.ContextId`, copied from race.ini `[STREET_ROD] CONTEXT_ID` (read with `ac.INIConfig.raceConfig()`); absent when race.ini had none |
| `participants[].car_index` | int | AC's index of the car; 0 is the player's |
| `participants[].is_player` | bool | True for the player's car (car index 0 in AC) |

Schema 1.2 (the race mode) added:

| Field | Type | Meaning |
|-------|------|---------|
| `session.end_reason` | string | `FINISHED`, `CRASH`, `FALSE_START` or `ABANDONED` (`EndReasons` in C#) |
| `participants[].false_start` | bool | This car jumped the start |
| `participants[].condition` | object | What the race left of the car, as AC tracks it: `body_damage_kmh[4]`, `engine_life` (1000 new, 0 dead), `gearbox_damage`, `water_temperature_c`, `oil_temperature_c`, `oil_pressure`, `fuel_litres`, `wheels[4]` (`wheel` 0..3 as FL, FR, RL, RR, so a wheel that could not be read never moves the others, `tyre_wear`, `tyre_virtual_km`, `tyre_blown`, `suspension_damage`). Each field read on its own; the launcher puts it on the car's parts (see "Damage" below). Oil temperature and pressure read near 0 on a stock car: CSP fills them only for cars with a script |

Schema 1.3 added:

| Field | Type | Meaning |
|-------|------|---------|
| `session.end_reason` | string | Now also `DISQUALIFIED` |
| `session.race_type` | string | `DRAG` or `ROAD`, from race.ini. The launcher checks it against the race's context and logs a mismatch (the file is still applied) |
| `participants[].disqualified` | bool | This car hit the other one out of its own lane in a drag race |

Schema 1.4 added:

| Field | Type | Meaning |
|-------|------|---------|
| `session.end_reason` | string | Now also `BROKE_DOWN`: the player's car broke down |
| `participants[].broke_down` | bool | This car broke down before the line and was out of the race |
| `participants[].breakdown` | string or absent | What gave out: `ENGINE`, `GEARBOX`, `SUSPENSION` or `TYRE` |
| `participants[].timeslip` | object or absent | A drag race only: `reaction_s` (AC's green to the car leaving the line, 0.2 m), then from leaving the line `sixty_ft_s`, `three_thirty_ft_s`, `eighth_mile_s`, `eighth_mile_mph`, `thousand_ft_s`, `quarter_mile_s`, `quarter_mile_mph`. A mark the car never reached is absent. The speeds are trap speeds, the average over the last 66 ft |

Schema 1.5 added:

| Field | Type | Meaning |
|-------|------|---------|
| `session.end_reason` | string | Now also `BUSTED`: the police caught the player before the line |
| `pursuit` | object or absent | Only when race.ini sent police: `police` (how many), `started` (the patrol showed up), `started_at_s`, `duration_s`, `player` and `rival`: `ESCAPED` or `BUSTED`. The police cars are never participants |

Schema 1.6 added (step 11):

| Field | Type | Meaning |
|-------|------|---------|
| `session.race_type` | string | Now also `TUNE`: a test-and-tune, with the player as the only participant |
| `participants[].passes` | array or absent | A test-and-tune only: every pass's timeslip, in order, each with its `pass` number |
| `participants[].timeslip.green_s` | number or absent | When the car got its own green, in seconds from AC's start (a bracket race, a test-and-tune pass); `reaction_s` is then from that green. Absent when the green was AC's |
| `participants[].timeslip.red_light` | bool or absent | The car left before its green |
| `participants[].dial_in_s` | number or absent | A bracket race only: the car's dial-in |
| `participants[].breakout` | bool or absent | A bracket race only: the car ran quicker than its dial-in |

```json
{
  "metadata": { "schema_version": "1.6", "script_version": "3.6.0", "source": "sr_race_manager", "generated_at": "ISO8601" },
  "session": { "session_id": "UUID", "context_id": "UUID of the race context", "track_id": "...", "duration_seconds": 12.3, "end_reason": "FINISHED" },
  "participants": [
    { "driver_name": "...", "car_name": "...", "car_index": 0, "is_player": true, "false_start": false,
      "performance": { }, "crash": { }, "condition": { } }
  ],
  "pursuit": { "police": 2, "started": true, "started_at_s": 41.2, "duration_s": 96.5, "player": "ESCAPED", "rival": "BUSTED" }
}
```

How the launcher uses them (`RaceResultIngestionService`, `RaceResultProcessor`):
- The player is the participant with `is_player`, never "the first in the list" (the list's order is not defined).
- A false start (`end_reason` `FALSE_START` or the player's `false_start`) is no contest: nothing changes hands, no
  win or loss, and `RacerStats.FalseStarts` costs reputation (3 each, up to 15). `ABANDONED` is a loss.
- A disqualification decides the race before any crash does: the player's (`DISQUALIFIED` or the player's
  `disqualified`) is a loss, the rival's a win.
- A breakdown puts a car out like a crash: the player's (`BROKE_DOWN` or the player's `broke_down`) is a loss, the
  rival's a win. Both cars out, whatever put each one out, is a draw (`BothOut`, or `BothCrashed` when both crashed).
- The police decide the race before anything but a false start: a busted player loses (`PlayerBusted`), even after
  winning at the line; a busted rival loses (`OpponentBusted`); both busted is no contest (`BothBusted`). A busted
  racer pays a fine (`PoliceRules.Fine`, more for each earlier bust) and, if the car is still theirs after the stakes,
  it goes to the impound for a few days (`Car.ImpoundedUntil`, `ImpoundFee`; a fine they couldn't pay goes on the
  bill). The player who got away earns `RacerStats.PoliceEscapes`, 2 reputation each, up to 10.
- A file whose `context_id` does not match the race in hand is never applied with that race's context.
- The race about to be driven is saved as `GameState.PendingRace` before AC starts. A result left over from a race
  that ended badly is applied when a save is loaded, only if its `context_id` is that save's `PendingRace.ContextId`
  (or it has no `context_id` and the save has a pending race); anything else is quarantined with the reason, never
  applied to the wrong save. Processing a result, or the forfeit of a wager or pink-slip race that left no result,
  clears `PendingRace`.

### Testing the race mode without driving
A copy of `mode.lua` in the install with a few lines appended that call `physics.setCarAutopilot(true)` once the rival
moves (the rival AI waits for the green; an autopilot switched on earlier jumps the start) drives the player's car
unattended. On `ks_drag` as a race session the autopilot steers into the wall by the stands and crawls, and AC then
retires it as an AI and ends the race: fine for checking the start, the rival and the log, not for a finish.
Screenshots of a windowed AC can be taken from PowerShell (`Graphics.CopyFromScreen`) while it runs. Back up `cfg\race.ini` and `cfg\assists.ini`, write a drag race.ini with `__CM_CUSTOM_MODE=sr_race`, run
`acs.exe` from the install, then restore the cfg files, delete the result file from `out\sr_race_manager` and put the
repo's `mode.lua` back. `physics.allowed()` in the log line "Session started" says whether the physics API is there.

---

## Development Notes

### Script Lifecycle Hooks
```lua
function script.start()      -- Session initialization
function script.prepare(dt)  -- Pre-race countdown (return true to start)
function script.update(dt)   -- Main loop (every frame)
function script.drawUI()     -- UI rendering
function script.sessionEnd() -- Cleanup
```

### Debugging
- Logs go to AC log: `ac.log('message')`
- View in `Documents/Assetto Corsa/logs/`

### Testing Changes
1. Edit Lua file in `C:\GAMES\Street Rod AC\extension\lua\...` (the race mode: in the repo, `apps\new-modes\sr_race`; the next race installs it)
2. Restart AC session (scripts reload on session start)
3. Check AC logs for errors
