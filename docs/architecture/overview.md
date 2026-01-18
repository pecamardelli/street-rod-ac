# Architecture Overview

## Project Goal
Street Rod-style career mode for Assetto Corsa. External WPF manager handles all game logic. AC is treated as physics/rendering engine only.

## Three-Layer Architecture

### 1. Meta-Game Layer (C#/WPF)
- Player progression, economy, car ownership
- Upgrade system, wear/damage persistence
- Save/load system
- All gameplay decisions

### 2. AC Integration Layer
- Launch AC via `acs.exe` (race) or `acShowroom.exe` (preview)
- Generate race configurations via INI modification
- Read race results from Python app output
- Never modify AC binaries or core files

### 3. Data/Persistence Layer
- LiteDB databases
- Catalog (static content definitions)
- Saves (dynamic player state)

## Key Principles

| Principle | Description |
|-----------|-------------|
| AC is external | Treat AC as unreliable content provider |
| Intent-based config | Declare what, let service handle how |
| Minimal modification | Change only owned keys, always restore |
| Deterministic imports | Same input = same catalog output |
| Engine-agnostic models | Game data never stores AC-specific values |

## Tech Stack
- Language: C# / .NET 10
- UI: WPF (native, no WebView)
- Pattern: MVVM (strict separation)
- Storage: LiteDB
- Target: Windows x64

## Non-Goals
- No AC binary patching
- No hot-reloading physics
- No open-world AI traffic
- No online multiplayer (initially)

## Files
- `App.xaml.cs` - Service composition, startup
- `MainWindow.xaml` - Single window host
- `Navigation/NavigationService.cs` - Screen management
