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
The race is over when no game process is left: Steam can close `acs.exe` and start it again, so after ours exits the
launcher watches a moment longer (`RelaunchGrace`, 3 s) and waits for any relaunched one.

### 6. Post-Execution
Read the results (races only), then, in the `finally`: restore the original configuration and release the lock.
Events (`ExecutionEnded`, `RaceCompleted`) are raised after the `finally`, once everything is back and unlocked,
each guarded so a failing subscriber spoils neither the result nor the others.

## Stopping a Race
`CancelRace()` kills the process started and its children, and any other AC race process (a relaunch). The pipeline
then carries on as after any exit: a race stopped before the finish has no result (a wager race is forfeited), and the
install goes back in the `finally`. If the processes do not die, the wait gives up after 15 s (`ResultPending`).
A stop that comes before AC is started (while the cars' data is still being prepared) calls the launch off instead:
AC never starts, what was already applied goes back, the pending race is released and no time passes (`Cancelled`).
The race is timed until the **last** AC process exits, so a Steam relaunch is one race, not a launch failure.
- The race loading screen has a **Stop Race** button (`StopRaceCommand`), behind a confirmation that says what is at
  stake.
- Closing the main window during a race asks first; yes stops the race and the app closes once the launcher is done.

## Race Outcome
`LaunchResult.Outcome` (`RaceOutcome`) says what came of a launch, and `LaunchResult.PlayerMessages` what to tell the
player (`PlayerMessage(Title, Text)`); the race loading screen shows them one dialog at a time after going back.

| Outcome | When |
|---------|------|
| NotARace | Showroom or free run: nothing to ingest |
| Processed | A result was read and applied |
| NoResult | AC closed and no result file of this race exists (the player quit or stopped it): a wager or pink-slip race is forfeited (a loss); otherwise "the race does not count" |
| ResultPending | This race's result file is there but could not be handled yet (locked, a failed check, move or save), or AC would not die: nothing is forfeited, the race stays pending in the save and counts when the save is next loaded or before the next race ("Race Result Waiting") |
| Quarantined | This race's result was written but could not be used; it is kept aside and the race is voided (nothing lost) |
| Cancelled | Stopped before AC started ("Race called off") |
| Failed | The launch or the ingestion failed |

### The pending race
The launcher puts the race on record in the save before anything is prepared (`BeginRaceAsync`), stamps it when AC
actually starts (`MarkRaceLaunched`, `RaceContext.LaunchedAt`) and ends it with the outcome above. An older race still
pending is settled first; if it cannot be settled yet the new race does not start ("Last Race Not Settled"). A pending
race is settled (on load, and before a race) by this rule, once no result file for it can be handled:

| Situation | What happens |
|-----------|--------------|
| Its file is there but can't be handled yet | Stays pending |
| Its file was quarantined | Released: void, nothing lost, with a message |
| AC is running (or it can't be told) | Stays pending; settled when AC exits (`AssettoCorsaExited`) |
| AC never started for it (`LaunchedAt` unset) | Released |
| Otherwise | Forfeited when something was at stake, released when not, with a message |

When the `finally` finds AC still alive it does not restore under it: the launcher keeps the execution lock, waits in
the background for AC to close, then puts the install back, releases the lock and raises `AssettoCorsaExited`. Every
change to the install (INI apply, car data, clones, the crash path's restore) goes through one lock, `AcInstallGate`;
the crash path only tries it for 5 s per step and leaves anything it can't do to the next start.

A race whose game closed less than 15 s after it started (and was not stopped by the player) never loaded: it is a
launch failure (`Failed`, `Success = false`), not a forfeit.

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

race.ini is backed up before it is changed and put back like the cars' data: in the launcher's `finally`, at start-up,
when the game exits and from the fatal-exception handler, but never while an AC process still runs (then the next
start does it, waiting for AC to close first; see [INI Modification](ini-modification.md), "Backup and restore"). A race writes `[STREET_ROD] CONTEXT_ID=<race context id>` into it, which the Lua app copies into its result
file, so a result is only ever applied to the race (and the save) it belongs to (see [CSP Lua Scripts](csp-lua-scripts.md)).

## The cars' own data
A race is driven on what the cars' parts make of them: `RaceCarDataService.Prepare(car)` turns the player's and the
opponent's parts into data files (see `docs/systems/parts-system.md`, "Assetto Corsa export"), they ride on the
`LaunchIntent.CarData`, and `CarDataOverlay` puts them into `content\cars\<car>\data` after the race config and
before `acs.exe`: originals kept under `%APPDATA%\StreetRodAC\AcRestore` with a manifest first, then the change.
A packed car (`data.acd` only) is unpacked for the race and the folder removed after. `RestoreAll` runs in the
launcher's `finally` and again at start-up (a crash leaves a manifest behind); a manifest that could not be restored is
put back before that car's data is changed again. The Assetto Corsa folder is a setting (`GameSettings.AssettoCorsaPath`,
the Settings screen; `C:\GAMES\Street Rod AC` when not set), and every manifest records the absolute car folder it
changed, so a restore goes back to the folder that was changed even after the setting moves.

Two cars of one model do not share a folder: an opponent in the player's model races in a marked copy of the car
folder, `<car>__sr_opponent` (`RaceSetupBuilder.RacesAs`, `CarDataOverlay.CreateClone`), with its own data and sound.
The launcher makes the copies first, from the untouched originals, then applies the player's data to the car itself;
race.ini names the copy as the opponent's `MODEL`. The marker file (`streetrod_clone.json`) goes in first and comes out
last, every scanner of `content\cars` skips marked folders (`AcCarFolder.InstalledCars`), and `RestoreAll` deletes
every copy after the race and at start-up. An opponent whose car cannot run races in a copy as its author made it
(details: `docs/systems/parts-system.md`, "Two cars of one model"). A player's car with a problem (no engine that runs,
a missing wheel) is stopped before the race.

## Key Services

| Service | Purpose |
|---------|---------|
| AssettoCorsaLauncher | Execute AC, manage lifecycle, apply and restore the cars' data, stop a race |
| AcProcesses | Finds and kills AC's processes (`AnyRunning`, `KillAll`) |
| IniModificationService | Apply/restore INI changes |
| CarDataOverlay | Cars' data files into the install for a race and back, with a manifest |
| RaceCarDataService | A car's parts as its data files |

## Files
- `Services/Assetto Corsa/AssettoCorsaLauncher.cs`
- `Services/Assetto Corsa/IAssettoCorsaLauncher.cs`
- `Services/Assetto Corsa/AcProcesses.cs`
- `Services/Assetto Corsa/CarDataOverlay.cs`
- `Services/Configuration/Models/LaunchResult.cs` (`RaceOutcome`)
- `Services/Configuration/IniModificationService.cs`
- `Services/Configuration/Models/LaunchIntent.cs`
- `Services/Configuration/Models/ModificationIntent.cs`
