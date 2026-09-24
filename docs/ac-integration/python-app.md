# AC Python Race App

> **Removed** (2026-09-24): this app was replaced by the CSP Lua app in `apps/lua/sr_race_manager` (see [CSP Lua Scripts](csp-lua-scripts.md)), and its folder `apps/python/StreetRodRaceApp` has been deleted from the repo; git history before that date still has it. Nothing in the game referenced it. The launcher ingests results only from `Documents/Assetto Corsa/out/sr_race_manager/`. This page describes the old app for reference; the Lua app's result schema has moved on since (see the Lua page).

## Purpose
Lightweight telemetry observer inside AC. Enforces Street Rod crash consequences. Outputs session results.

## Responsibilities
- Sample telemetry at low, fixed frequency
- Track race metrics (laps, time, speed)
- Detect hard crashes via G-force delta
- Enforce crash mode (lock player controls)
- Write single output file at session end

## Non-Responsibilities (Strict)
- No INI modification
- No physics changes
- No session restart/end
- No database access
- No networking

## Crash Detection
Uses AC telemetry API (`ac.getCarState()`, `acsys.CS.AccG`).

Algorithm:
1. Read G-forces [lateral, vertical, longitudinal]
2. Calculate magnitude: `sqrt(gx² + gy² + gz²)`
3. Track delta between frames
4. If delta > threshold (100G): hard crash

## Crash Mode Enforcement
When hard crash detected:
- Force throttle to zero
- Force brake to maximum
- Override continuously
- Control never restored

Message: "You crashed hard. The race is over."

## Output File

### Location
`Documents\Assetto Corsa\out\StreetRodRaceApp\{uuid}.json`

### Structure
```json
{
  "metadata": {
    "schema_version": "1.0",
    "source": "StreetRodRaceApp",
    "generated_at": "ISO8601"
  },
  "session": {
    "session_id": "UUID",
    "track_id": "spa",
    "duration_seconds": 296
  },
  "participants": [
    {
      "driver_name": "Player",
      "car_name": "ks_porsche_911",
      "performance": {
        "final_position": 1,
        "laps_completed": 3,
        "best_lap_time_ms": 92500
      },
      "crash": {
        "crashed": false,
        "max_crash_intensity_g": 0
      }
    }
  ]
}
```

## Output Rules
- Exactly one file per session
- Written only at session end
- UUID as filename
- Immutable after creation

## Street Rod Manager Integration
1. After AC exits, scan output folder
2. Parse JSON by session_id
3. Apply game consequences
4. Delete processed file

## Files
- `apps/python/StreetRodRaceApp/StreetRodRaceApp.py` (removed; in git history only)
- `Services/Race/RaceResultProcessor.cs` (ingestion, now of the Lua app's files)
