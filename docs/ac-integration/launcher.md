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

### Modification Intent (Low-Level)
Specific INI changes:
- ShowroomIntent
- DragRaceIntent

Launch Intent → generates → Modification Intent(s)

## Restoration Rule (Mandatory)
ALWAYS restore original config after AC exits:
- On success: restore
- On error: restore
- On crash: restore

## Key Services

| Service | Purpose |
|---------|---------|
| AssettoCorsaLauncher | Execute AC, manage lifecycle |
| IniModificationService | Apply/restore INI changes |

## Files
- `Services/Launcher/AssettoCorsaLauncher.cs`
- `Services/Launcher/IAssettoCorsaLauncher.cs`
- `Services/Configuration/IniModificationService.cs`
- `Services/Configuration/Models/LaunchIntent.cs`
- `Services/Configuration/Models/ModificationIntent.cs`
