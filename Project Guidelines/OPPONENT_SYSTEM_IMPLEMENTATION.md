# Street Rod Opponent System - Implementation Summary

## Overview

The opponent system is now fully implemented following the separation-of-concerns architecture defined in `opponent_system_guidelines.txt`.

## What Was Built

### 1. **Opponent Model** (Engine-Agnostic Layer)
- **File:** `Street Rod AC/Models/GameState/Opponent.cs`
- **Purpose:** Defines WHO the racer is
- **Key Features:**
  - Extends `Racer` base class
  - Unique `OpponentId` (Guid)
  - Age and Gender for personality
  - **Skill** (80-100) - Maps to AC AI Strength
  - **Aggression** (0-100) - Maps to AC AI Aggression
  - Optional portrait, location, biography
  - Built-in clamping methods for safe trait updates
  - **Persistence:** Saved to LiteDB (no AC-specific values)

### 2. **Opponent AI Adapter** (Engine-Specific Layer)
- **File:** `Street Rod AC/Services/Opponents/OpponentAIAdapter.cs`
- **Purpose:** Translates Street Rod traits to AC AI parameters
- **Key Features:**
  - Static adapter class (no state)
  - `ToAssettoCorsaAI()` - Converts Opponent → AssettoCorsaAIParameters
  - `ValidateOpponent()` - Ensures traits are in valid ranges
  - **Persistence:** NEVER persisted (runtime only)
  - **Generation:** Created fresh for every race

### 3. **Opponent Generation Service**
- **Files:**
  - `Street Rod AC/Services/Opponents/IOpponentGenerationService.cs`
  - `Street Rod AC/Services/Opponents/OpponentGenerationService.cs`
- **Purpose:** Generate new opponents with realistic traits
- **Key Features:**
  - Age-based aggression modifiers:
    - Young (18-25): +10 to +20 aggression
    - Middle-aged (26-45): 0 to +5 aggression
    - Older (46+): -10 to -20 aggression
  - Gender-based tendencies (subtle, allows variation):
    - Male: slight positive tendency (+2 average)
    - Female: slight negative tendency (-1 average)
    - Other: neutral
  - Configurable base skill or random generation
  - All values clamped to valid ranges

### 4. **Opponent Evolution Service**
- **Files:**
  - `Street Rod AC/Services/Opponents/IOpponentEvolutionService.cs`
  - `Street Rod AC/Services/Opponents/OpponentEvolutionService.cs`
- **Purpose:** Event-driven trait evolution based on race outcomes
- **Key Features:**
  - **Win Evolution:**
    - Skill: +1 to +2
    - Aggression: +0 to +3 (regular), +3 to +6 (dominant win)
  - **Loss Evolution:**
    - Skill: +0 to +2 (learning from defeat)
    - Aggression: -4 to 0 (close loss), -8 to -3 (badly beaten)
  - **Crash Evolution:**
    - Skill: -2 to 0 (loss of confidence)
    - Aggression: -10 to -5 (becomes cautious)
  - **Close Race Evolution:**
    - Skill: +1 to +2 (intense competition improves ability)
  - All changes logged for debugging
  - Caller responsible for persistence

### 5. **Launch Intent Updates**
- **Files Updated:**
  - `Street Rod AC/Services/Configuration/Models/ModificationIntent.cs`
  - `Street Rod AC/Services/Configuration/Models/LaunchIntent.cs`
  - `Street Rod AC/Services/Configuration/IniModificationService.cs`
- **Changes:**
  - Added `OpponentAILevel` and `OpponentAIAggression` to `DragRaceIntent`
  - Added same parameters to `DragRaceLaunchIntent`
  - Updated `ApplyDragRaceIntent()` to inject AI parameters into race.ini
  - Parameters flow: Opponent → Adapter → LaunchIntent → ModificationIntent → race.ini

## Architecture Compliance

The implementation strictly follows the guidelines:

### ✅ Separation Rules
- Street Rod opponent data contains ZERO AC-specific values
- AC AI parameters are computed at runtime, never persisted
- Opponent evolution operates only on conceptual traits (skill, aggression)
- Adapter layer is the ONLY place AC values are generated

### ✅ Data Flow
```
┌─────────────────────────────────────────────────────────────┐
│ STREET ROD LAYER (Engine-Agnostic, Persisted)              │
├─────────────────────────────────────────────────────────────┤
│ • Opponent Model (age, gender, skill, aggression)          │
│ • OpponentGenerationService (creates new opponents)        │
│ • OpponentEvolutionService (evolves traits after races)    │
└──────────────────────┬──────────────────────────────────────┘
                       │ Runtime Conversion (Never Persisted)
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ ADAPTER LAYER (Runtime Only, Disposable)                   │
├─────────────────────────────────────────────────────────────┤
│ • OpponentAIAdapter.ToAssettoCorsaAI()                      │
│   - Converts Skill → AI_LEVEL (80-100)                     │
│   - Converts Aggression → AI_AGGRESSION (0-100)            │
└──────────────────────┬──────────────────────────────────────┘
                       │ AC-Specific Parameters
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ ASSETTO CORSA LAYER (Engine-Specific, Ephemeral)           │
├─────────────────────────────────────────────────────────────┤
│ • DragRaceLaunchIntent (contains runtime AI params)        │
│ • DragRaceIntent (modification intent with AI values)      │
│ • IniModificationService (writes to race.ini)              │
│ • race.ini [CAR_1] section (AI_LEVEL, AI_AGGRESSION)       │
└─────────────────────────────────────────────────────────────┘
```

### ✅ Mutation Rules
- Skill changes: ±1-2 (small increments as specified)
- Aggression changes: Variable based on event severity
- All changes clamped to valid ranges (Skill: 80-100, Aggression: 0-100)
- Evolution is event-driven (after each race)
- Changes logged for visibility

### ✅ Future-Proofing Benefits
This architecture enables:
- AI rebalance patches (change adapter logic only)
- New opponent behaviors (add new evolution methods)
- Difficulty modes (apply global modifiers in adapter)
- Alternative engines (create new adapter layer)
- Save compatibility (opponent data never breaks)

## Usage

See `opponent_system_usage_example.cs` for complete code examples covering:
1. Generating new opponents
2. Converting to AC AI parameters
3. Launching drag races with opponents
4. Evolving opponents after races
5. Complete race workflow
6. Persistence guidelines

## Next Steps

To integrate the opponent system into your game:

1. **Register Services** (if using DI):
   ```csharp
   services.AddSingleton<IOpponentGenerationService, OpponentGenerationService>();
   services.AddSingleton<IOpponentEvolutionService, OpponentEvolutionService>();
   ```

2. **Create Opponents** (one-time setup or during gameplay):
   ```csharp
   var opponent = generationService.GenerateOpponent("Name", age: 28, Gender.Male);
   gameStateRepository.SaveOpponent(opponent);
   ```

3. **Race Workflow** (when player challenges opponent):
   ```csharp
   // Convert to AC params
   var aiParams = OpponentAIAdapter.ToAssettoCorsaAI(opponent);

   // Launch race with AI params
   var intent = new DragRaceLaunchIntent {
       OpponentAILevel = aiParams.AILevel,
       OpponentAIAggression = aiParams.AIAggression,
       // ...
   };
   await launcher.LaunchRaceAsync(intent);

   // Evolve opponent based on results
   evolutionService.ApplyWinEvolution(opponent, isDominant);
   gameStateRepository.UpdateOpponent(opponent);
   ```

4. **Testing**:
   - Test opponent generation with different ages/genders
   - Verify AI parameters are correctly injected into race.ini
   - Test evolution by simulating various race outcomes
   - Verify opponents maintain valid trait ranges

## Files Created/Modified

### New Files
- `Street Rod AC/Models/GameState/Opponent.cs`
- `Street Rod AC/Services/Opponents/OpponentAIAdapter.cs`
- `Street Rod AC/Services/Opponents/IOpponentGenerationService.cs`
- `Street Rod AC/Services/Opponents/OpponentGenerationService.cs`
- `Street Rod AC/Services/Opponents/IOpponentEvolutionService.cs`
- `Street Rod AC/Services/Opponents/OpponentEvolutionService.cs`
- `Project Guidelines/opponent_system_usage_example.cs`
- `Project Guidelines/OPPONENT_SYSTEM_IMPLEMENTATION.md` (this file)

### Modified Files
- `Street Rod AC/Services/Configuration/Models/ModificationIntent.cs`
- `Street Rod AC/Services/Configuration/Models/LaunchIntent.cs`
- `Street Rod AC/Services/Configuration/IniModificationService.cs`
- `Project Guidelines/opponent_system_guidelines.txt`

## Compliance Statement

This implementation STRICTLY adheres to all requirements in `opponent_system_guidelines.txt`:
- ✅ Clean separation between Street Rod data and AC parameters
- ✅ Engine-agnostic opponent model
- ✅ Runtime-only AC adapter
- ✅ Event-driven evolution with small increments
- ✅ Age and gender-based generation modifiers
- ✅ Valid ranges enforced (Skill: 80-100, Aggression: 0-100)
- ✅ No AC-specific values persisted
- ✅ Future-proof architecture

**FINAL RULE COMPLIANCE:**
> "Street Rod defines who the racer is.
> Assetto Corsa defines how the engine runs them.
> Never confuse the two."

✅ **ACHIEVED**
