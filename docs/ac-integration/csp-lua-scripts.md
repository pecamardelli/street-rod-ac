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

**Files**: `mode.lua`, `manifest.ini`. Edit them in the repo: `SrRaceMode` writes the repo's copy (shipped next to
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

**End reasons** (`session.end_reason`): `FINISHED` (the player crossed the line, `WIN` or `LOSE` by position),
`CRASH`, `FALSE_START`, `DISQUALIFIED`, `ABANDONED`. The race ends only when the player crosses the line, whoever
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

### Communication Methods

| Method | Status | Notes |
|--------|--------|-------|
| `ac.shutdownAssettoCorsa()` | Working | The mode quits AC once the result is written |
| Signal files | Removed | Was in Python app, didn't work reliably |
| race.ini `[STREET_ROD] CONTEXT_ID` | Working | Launcher to the mode: which race this is (see below) |
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
| `participants[].condition` | object | What the race left of the car, as AC tracks it: `body_damage_kmh[4]`, `engine_life` (1000 new, 0 dead), `gearbox_damage`, `water_temperature_c`, `oil_temperature_c`, `oil_pressure`, `fuel_litres`, `wheels[4]` (`tyre_wear`, `tyre_virtual_km`, `tyre_blown`, `suspension_damage`). Each field read on its own; not read by the launcher yet (next: onto the car's parts). Oil temperature and pressure read near 0 on a stock car: CSP fills them only for cars with a script |

Schema 1.3 added:

| Field | Type | Meaning |
|-------|------|---------|
| `session.end_reason` | string | Now also `DISQUALIFIED` |
| `session.race_type` | string | `DRAG` or `ROAD`, from race.ini (not read by the launcher, which has the race's context) |
| `participants[].disqualified` | bool | This car hit the other one out of its own lane in a drag race |

```json
{
  "metadata": { "schema_version": "1.3", "script_version": "3.2.0", "source": "sr_race_manager", "generated_at": "ISO8601" },
  "session": { "session_id": "UUID", "context_id": "UUID of the race context", "track_id": "...", "duration_seconds": 12.3, "end_reason": "FINISHED" },
  "participants": [
    { "driver_name": "...", "car_name": "...", "car_index": 0, "is_player": true, "false_start": false,
      "performance": { }, "crash": { }, "condition": { } }
  ]
}
```

How the launcher uses them (`RaceResultIngestionService`, `RaceResultProcessor`):
- The player is the participant with `is_player`, never "the first in the list" (the list's order is not defined).
- A false start (`end_reason` `FALSE_START` or the player's `false_start`) is no contest: nothing changes hands, no
  win or loss, and `RacerStats.FalseStarts` costs reputation (3 each, up to 15). `ABANDONED` is a loss.
- A disqualification decides the race before any crash does: the player's (`DISQUALIFIED` or the player's
  `disqualified`) is a loss, the rival's a win.
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
