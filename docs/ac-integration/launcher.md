# AC Launcher System

## Core Principle
Launching AC is the final step of a controlled pipeline. All preparation completes before execution.

## Executables
- `acs.exe` - Race mode
- `acShowroom.exe` - Showroom mode

No command-line arguments. All config via INI files in `Documents\Assetto Corsa\cfg\`.

## Execution Pipeline

### 1. Declare Intent
Higher-level system declares what to execute (race, showroom).
No files modified yet.

### 2. Prepare Configuration
Apply modification intents to INI files.
Validate resulting state.
Abort if preparation fails.

### 3. Lock State
Configuration becomes read-only.
No UI/background system may modify INI files.

### 4. Execute
Launch AC executable.
Control handed to game.

### 5. Monitor (Passive)
Observe process lifecycle.
Do not modify anything.

### 6. Post-Execution
Always restore original configuration.
Persist gameplay results.
Release lock.

## Intent Architecture

### Launch Intent (High-Level)
What user wants to do:
- ShowroomLaunchIntent
- DragRaceLaunchIntent
- FreeRunLaunchIntent: the selected car alone on a track of the player's choosing (garage "Free Run" button, track
  remembered in `GameSettings.FreeRunTrack`). A practice session without the `sr_race` mode: nothing auto-starts or
  auto-quits and no results are read; the player leaves when done and an hour of game time goes by. The car's parts
  data is applied and restored like a race's.

### Modification Intent (Low-Level)
Specific INI changes:
- ShowroomIntent
- DragRaceIntent
- FreeRunIntent (one car, `[SESSION_0] TYPE=1`, `SPAWN_SET=PIT`; shares the common race.ini sections with the drag race)

Launch Intent → generates → Modification Intent(s)

## Restoration Rule (Mandatory)
ALWAYS restore original config after AC exits:
- On success: restore
- On error: restore
- On crash: restore

## The cars' own data
A race is driven on what the cars' parts make of them: `RaceCarDataService.Prepare(car)` turns the player's and the
opponent's parts into data files (see `docs/systems/parts-system.md`, "Assetto Corsa export"), they ride on the
`LaunchIntent.CarData`, and `CarDataOverlay` puts them into `content\cars\<car>\data` after the race config and
before `acs.exe`: originals kept under `%APPDATA%\StreetRodAC\AcRestore` with a manifest first, then the change.
A packed car (`data.acd` only) is unpacked for the race and the folder removed after. `RestoreAll` runs in the
launcher's `finally` and again at start-up (a crash leaves a manifest behind); a manifest that could not be restored is
put back before that car's data is changed again. Two cars of one model share a folder: the diner prepares only the
player's and the opponent drives it. A player's car with a problem (no engine that runs, a missing wheel) is stopped in
the diner.

## Key Services

| Service | Purpose |
|---------|---------|
| AssettoCorsaLauncher | Execute AC, manage lifecycle, apply and restore the cars' data |
| IniModificationService | Apply/restore INI changes |
| CarDataOverlay | Cars' data files into the install for a race and back, with a manifest |
| RaceCarDataService | A car's parts as its data files |

## Files
- `Services/Launcher/AssettoCorsaLauncher.cs`
- `Services/Launcher/IAssettoCorsaLauncher.cs`
- `Services/Configuration/IniModificationService.cs`
- `Services/Configuration/Models/LaunchIntent.cs`
- `Services/Configuration/Models/ModificationIntent.cs`
