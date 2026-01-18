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

| Method | Purpose |
|--------|---------|
| ApplyIntent(intent) | Apply modification |
| Backup() | Save original state |
| Restore() | Revert to backup |

## Intent Types

| Intent | Target File | Purpose |
|--------|-------------|---------|
| ShowroomIntent | showroom_start.ini | Car/skin for showroom |
| DragRaceIntent | race.ini | Race config, AI params |

## Safety Rules
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
- `Services/Configuration/Models/ModificationIntent.cs`
