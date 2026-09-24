# CSP Lua Scripts

## Overview
Custom Shaders Patch (CSP) Lua scripts extend Assetto Corsa's functionality. These scripts run inside AC and are located in `C:\GAMES\Street Rod AC\extension\lua\`.

**Important**: These scripts are in the AC installation, NOT the C# project. They communicate with the launcher via file signals or CSP APIs.

## Street Rod Custom Scripts

### 1. SR Race Mode (`new-modes/sr_race/`)

**Purpose**: Auto-start races and auto-quit after finish. Designed for seamless drag race flow.

**Files**:
- `mode.lua` - Main script
- `manifest.ini` - Mode registration

**Key Features**:
| Feature | Implementation |
|---------|----------------|
| Auto-start | `script.prepare()` returns `true` after 0.5s delay |
| Finish detection | Monitors `car.lapCount` changes |
| Auto-quit | Calls `ac.shutdownAssettoCorsa()` after 3s delay |
| UI overlay | Shows "Quitting in X..." countdown |

**No key bindings required** - uses native CSP APIs.

**Configuration Constants**:
```lua
AUTO_START_DELAY = 0.5      -- Seconds before auto-start
QUIT_DELAY_SECONDS = 3.0    -- Wait after finish before quitting
```

**Manifest Settings**:
```ini
[RULES]
BASE_MODE=RACE
PENALTIES=0

[TWEAKS]
IMMEDIATE_START=1
DISABLE_FLAGS=YELLOW, BLUE
```

**State Machine**:
```
PREPARE → (0.5s) → RACE_STARTED → (lap complete) → PLAYER_FINISHED → (3s) → AC_SHUTDOWN
```

**CSP APIs Used**:
- `ac.setStartMessage(msg, icon)` - Enable prepare mode (REQUIRED at module level)
- `script.prepare(dt)` - Called during prepare phase, return `true` to start
- `ac.shutdownAssettoCorsa()` - Quit AC completely
- `ac.getCar(0)` - Player car reference
- `ac.getSim()` - Session state

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
The `sr_race` mode handles auto-start and auto-quit internally using CSP APIs. The C# launcher:
1. Launches AC with sr_race mode enabled
2. Waits for AC process to exit
3. Reads race results from the SR Race Manager Lua app (`apps/lua/sr_race_manager`), written to `Documents/Assetto Corsa/out/sr_race_manager/*.json`

### Communication Methods

| Method | Status | Notes |
|--------|--------|-------|
| `ac.endSession()` | Working | Clean exit, returns to AC menu then AC closes |
| Signal files | Removed | Was in Python app, didn't work reliably |
| `ac.ext_quitAC()` | Untested | CSP extension, may not exist in all versions |
| race.ini `[STREET_ROD] CONTEXT_ID` | Working | Launcher to Lua app: which race this is (see below) |
| Result JSON | Working | Lua app to launcher, one file per race |

### Race Result File (SR Race Manager)
The app writes one file per race to `Documents\Assetto Corsa\out\sr_race_manager\<session_id>.json`: AC's own
Documents folder (`ac.getFolder(ac.FolderID.ACDocuments)`), which is the known Documents folder the launcher reads even
where Documents is redirected (OneDrive). The file is written as `<name>.tmp` and then renamed, so a file under the
final name is always whole; the launcher ignores names that are not a UUID.

Schema 1.1 added three fields to what 1.0 had (the launcher still reads 1.0 files, without them):

| Field | Type | Meaning |
|-------|------|---------|
| `session.context_id` | string, may be absent | The race's `RaceContext.ContextId`, copied from race.ini `[STREET_ROD] CONTEXT_ID` (read with `ac.INIConfig.raceConfig()`); absent when race.ini had none |
| `participants[].car_index` | int | AC's index of the car; 0 is the player's |
| `participants[].is_player` | bool | True for the player's car |

```json
{
  "metadata": { "schema_version": "1.1", "script_version": "...", "source": "sr_race_manager", "generated_at": "ISO8601" },
  "session": { "session_id": "UUID", "context_id": "UUID of the race context", "track_id": "...", "duration_seconds": 12.3 },
  "participants": [
    { "driver_name": "...", "car_name": "...", "car_index": 0, "is_player": true, "performance": { }, "crash": { } }
  ]
}
```

How the launcher uses them (`RaceResultIngestionService`, `RaceResultProcessor`):
- The player is the participant with `is_player`, never "the first in the list" (the list's order is not defined).
- A file whose `context_id` does not match the race in hand is never applied with that race's context.
- The race about to be driven is saved as `GameState.PendingRace` before AC starts. A result left over from a race
  that ended badly is applied when a save is loaded, only if its `context_id` is that save's `PendingRace.ContextId`
  (or it has no `context_id` and the save has a pending race); anything else is quarantined with the reason, never
  applied to the wrong save. Processing a result, or the forfeit of a wager or pink-slip race that left no result,
  clears `PendingRace`.

### Enabling SR Race Mode

The mode must be enabled in AC/CSP settings or via INI modification before launch. Check `IniModificationService` for how modes are configured.

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
1. Edit Lua file in `C:\GAMES\Street Rod AC\extension\lua\...`
2. Restart AC session (scripts reload on session start)
3. Check AC logs for errors
