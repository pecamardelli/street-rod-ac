# INI Modification System

## Core Principle
INI files are user/mod-owned. Declare intent, apply minimally, always restore.

## Target Files

### Global Config (`Documents\Assetto Corsa\cfg\`)
User-controlled, volatile.
- `race.ini` - Race session settings
- `showroom_start.ini` - Showroom settings

### Car Data (`content\cars\{car}\data\`)
Mod-controlled.
- `power.lut` - Power curve
- `engine.ini` - Engine parameters

## Three-Layer Model

### 1. Read (Observe)
Parse INI to in-memory representation.
Preserve sections, keys, comments, order.
Non-destructive.

### 2. Intent (Declare)
Semantic intent, not file operations:
- "Disable assists"
- "Set AI opponent"
- "Configure track"

### 3. Apply (Materialize)
Map intent to specific INI changes.
Apply minimal diff only.
Write atomically.

## Modification Rules

| Rule | Description |
|------|-------------|
| Minimal Diff | Only modify owned keys |
| Reversibility | Every change must be restorable |
| Idempotency | Same intent = same result |
| Atomic Write | Temp file + rename |

## Change Tracking
Track which keys were modified.
Support clean revert.
Never modify untouched keys.

## IniModificationService

| Member | Purpose |
|--------|---------|
| ApplyIntent(intent) | Keep the target file's original, then apply the modification |
| ReadIniFile(name) | The cfg file as an `IniText`, decoded as UTF-8 or Latin-1 |
| RestoreAll() | Put back every cfg file an intent changed, as it was before the first change |
| HasPendingRestore | Whether a changed cfg file is waiting to go back |

## Backup and Restore
The cfg files (`race.ini`, `showroom_start.ini`) are restored the way the cars' data is (`CarDataOverlay`):

- **Keep first** (`AcConfigBackup.Keep`): before the first byte of a file changes, its original bytes (or the fact
  that it did not exist) go to `%AppData%\StreetRodAC\AcRestore\~cfg`, with a manifest (`ini-manifest.json`)
  flushed to disk. Only files under `Documents\Assetto Corsa\cfg` are ever kept or written.
- **Once per race**: a file already kept (a second launch after a restore that failed) is not kept again, so what
  goes back is always the user's own file, never ours.
- **Then write**: through `SafeFile` (temp file, flushed, renamed over the old one). An edited file keeps its encoding
  and every line but the ones changed (`IniText`); race.ini, which a race writes whole, is UTF-8 without a BOM.
- **Restore** (`RestoreAll`), once AC has exited: in the launcher's `finally`, at start-up (a crash or a power cut
  leaves the manifest behind), when the game exits, and from the fatal-exception handler. Never while an AC process
  runs (`AcProcesses.AnyRunning`): the launcher's `finally`, exit and the fatal path then leave it for the next start,
  and a start with AC still running waits for it to close (`RestoreWhenAcExitsAsync`, holding the launch lock) before
  putting anything back. A file that did not exist
  is deleted; one that did gets its original bytes back. Idempotent; a file that cannot go back stays kept for the
  next call and is logged, and the others go back anyway. The `~cfg` folder has no car manifest, so the car-data
  restore never takes it for a car.

After a race the user's race.ini is what it was before: no `MODE=sr_race`, no `__CM_NEW_MODE_USED`, none of our cars,
and a normal AC or Content Manager session is not affected.

## Race Context Id
A race's race.ini carries its context id, which the Lua app copies into its result file:

```ini
[STREET_ROD]
CONTEXT_ID=<RaceContext.ContextId, "D" format>
```

`IniModificationService` writes it whenever the intent's `Metadata["RaceContext"]` holds a `RaceContext`. See
[CSP Lua Scripts](csp-lua-scripts.md) for how the result carries it back.

## Intent Types

| Intent | Target File | Purpose |
|--------|-------------|---------|
| ShowroomIntent | showroom_start.ini | Car/skin for showroom |
| DragRaceIntent | race.ini | Race config, AI params |

## Safety Rules
- One writer: every cfg INI write goes through `IniModificationService` (`IniText` + `SafeFile`); nothing else writes
  `race.ini` or `showroom_start.ini`
- Write to temp file first
- Validate before replacing
- Never leave partial writes
- Log all modifications

## Anti-Patterns
- Regex-only parsing
- Overwriting entire files
- Silent failures
- Mixing file I/O with gameplay logic

## Files
- `Services/Configuration/IniModificationService.cs`
- `Services/Configuration/IIniModificationService.cs`
- `Services/Configuration/AcConfigBackup.cs` (originals and manifest)
- `Services/Configuration/Models/ModificationIntent.cs`
- `Parts/Export/IniText.cs` (the one INI reader/writer, line-preserving)
- `Helpers/SafeFile.cs` (atomic writes)
