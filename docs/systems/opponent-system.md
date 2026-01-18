# Opponent System

## Core Principle
Opponent data = who the racer is (persisted).
AC AI parameters = how engine runs them (runtime only, never persisted).

## Two Layers

### Layer 1: Opponent Model (Engine-Agnostic)
Stored in save file, evolves over time.

| Property | Range | Purpose |
|----------|-------|---------|
| Skill | 80-100 | Overall driving ability |
| Aggression | 0-100 | Risk appetite |
| Age, Gender | - | Personality modifiers |
| Name, Portrait | - | Identity |

### Layer 2: AI Adapter (Runtime Only)
Generated fresh for each race, never persisted.

| Input | Output |
|-------|--------|
| Skill → | AC AI_LEVEL |
| Aggression → | AC AI_AGGRESSION |

## Generation Modifiers
Age affects aggression:
- Young (18-25): +10 to +20
- Middle (26-45): 0 to +5
- Older (46+): -10 to -20

## Evolution (After Each Race)

| Event | Skill Change | Aggression Change |
|-------|--------------|-------------------|
| Win | +1 to +2 | +0 to +3 |
| Dominant Win | +1 to +2 | +3 to +6 |
| Loss | +0 to +2 | -4 to 0 |
| Bad Loss | +0 to +2 | -8 to -3 |
| Crash | -2 to 0 | -10 to -5 |

## Strict Rules
- Never store AC AI values in Opponent model
- AI parameters recomputed every race
- Evolution operates only on Skill/Aggression
- Adapter layer is the only AC-specific code

## Key Services

| Service | Purpose |
|---------|---------|
| OpponentGenerationService | Create new opponents |
| OpponentEvolutionService | Apply race outcome changes |
| OpponentAIAdapter | Convert to AC parameters (static) |

## Race Workflow
1. Load `Opponent` from save
2. Convert via `OpponentAIAdapter.ToAssettoCorsaAI(opponent)`
3. Pass AI params to launch intent
4. After race, call evolution service
5. Save updated opponent

## Files
- `Models/GameState/Opponent.cs`
- `Services/Opponents/OpponentGenerationService.cs`
- `Services/Opponents/OpponentEvolutionService.cs`
- `Services/Opponents/OpponentAIAdapter.cs`
