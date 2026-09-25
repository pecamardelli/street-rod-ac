# Opponent System

## Core Principle
Opponent data = who the racer is (persisted).
AC AI parameters = how engine runs them (runtime only, never persisted).

## Two Layers

### Layer 1: Opponent Model (Engine-Agnostic)
Stored in save file, evolves over time.

| Property | Range | Purpose |
|----------|-------|---------|
| Skill | 90-100 | Overall driving ability (the floor of 90 is deliberate, commit 61ca613: below it AC's AI drives too badly to make a race. Generation, evolution clamping and the adapter all keep to it; never lower it) |
| Aggression | 0-100 | Risk appetite |
| Age, Gender | - | Personality modifiers |
| Name, Portrait | - | Identity |

### Layer 2: AI Adapter (Runtime Only)
Generated fresh for each race, never persisted.

| Input | Output |
|-------|--------|
| Skill → | AC AI_LEVEL (clamped to 90-100) |
| Aggression → | AC AI_AGGRESSION |

## Generation Modifiers
Age affects aggression:
- Young (18-25): +10 to +20
- Middle (26-45): 0 to +5
- Older (46+): -10 to -20

## Evolution (After Each Race Against the Player)

`RaceResultProcessor` applies it to the rival after every decided race (`OpponentEvolutionService`): a crash makes
them careful, a loss less bold, a win bolder. Rivals racing each other gain a little skill from a win
(`RaceSimulatorService`).

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

## Living Opponents (roadmap step 5)

The rivals live like the player: they buy cars off the same lots, answer the player's ads, repair, tune, go broke and
come back. The GameMaker version's daily review (`scr_review_racers`, `scr_get_car_upgrades`) on the part tree and the
real dyno.

**Pools** (`RacerCollection`): `ReadyToRace` (at the diner, racing each other), `Retired` (sitting it out: no car, or
a car that can't race and no money to fix it), `Inactive` (not on the street yet). A new career puts 6 racers on the
street and the rest in `Inactive`; `OpponentRules.MinActive` brings out 2 more each week (`6 + 2 × weeks`).

**The daily review** (`OpponentLifeService`, scheduled as `OpponentReviewTask`, before the race simulator), per racer,
retired ones first:
1. The best car to the front of `Cars` (`OpponentRules.CarScore`: a car that can race before any that can't, then
   dyno horsepower ×10, condition, worth). `Cars[0]` is the car every screen and the simulator use.
2. Spares: a wreck goes for scrap (`CarSaleService.ScrapFactor`), more than 2 cars and the weakest goes to a dealer
   at 60%, onto the trade-in lot.
3. No car: the best listing they can afford (`OpponentRules.PurchaseScore`: power, condition, a
   runner, power per dollar, money left over), bought exactly as the player would (`CarPurchaseService.CarFrom`).
4. A car that can't race (`CarCondition.WhyCannotRace`): the whole `RepairShop.Jobs` bill if they can pay it; else,
   when what the car fetches plus their money buys a runner, sold and replaced.
5. Broke (`OpponentRules.IsBankrupt`: no racing car, less than the cheapest car for sale): 10–60% of the cheapest
   car for sale scraped together ($50–500 when nothing is for sale).
6. Status: ready → `ReadyToRace`, not → `Retired`.

Then the newcomers come out (`Activate`), then tuning (below). Every car of every rival and every listing without a
figure goes on the dyno first (`Car.PowerHp`, `UsedCarListing.PowerHp`), on copies, off the UI thread.

**Prices drive it.** Every sum a rival weighs comes from the game's prices: the lots' asking prices, the repair bill,
the mail-order price of parts, what a dealer or the scrapyard pays, a car's worth. The only fixed sums are the
fallbacks for an empty market. Changing the prices changes what the rivals can do; the numbers below need no tuning
with them.

**Tuning** (`EngineTuner`): a few racers a day (`TuneChance` 15%, at most 3) keep a quarter of their car's worth back
and spend up to 30% of the rest on one upgrade: the one with the best `gain% / cost × priority` (block swap 10, blower 8,
carbs/injection 7, exhaust 6, manifold and camshaft 5, air 3), at least 3% more power on the dyno. A part the new one
leaves without a place is replaced by the cheapest that fits (depth 5). The block swap is a bigger engine of the same
family, used (`UsedEnginePrice`), at most 1.6× the power it replaces. There is no ceiling on a car's power: money is
the limit (the user's call, 2026-09-24). New parts at the mail-order price; the old ones traded in.

**Races between rivals** (`RaceSimulatorService`): by `Car.PowerHp`; the race's wear lands on the parts through
`CarCondition.ApplyRace` (`SimulatedCondition`: engine life, gearbox, tyres, knocks, a loser's hard blow now and then);
the loser of a road race crashes 3% of the time (drag 0.5%), which usually totals the car. A pink-slipped car stays
with the winner (the review sells it on); a racer without a car retires. The King never races in them.

**The King** (`Opponent.IsKing`, `"isKing": true` in `opponent_definitions.json`): set up by
`OpponentInitializationService.EnsureKing` (also into older saves), one of the 5 strongest factory cars tuned all the
way, $20,000, a record that makes his reputation 100. He is kept in `Inactive` until `KingVictory.IsUnlocked` (10
wins, 50 reputation), then sits at the diner. He only races for pink slips (a cash challenge is refused, the diner
switches to pink slips when he is picked) and accepts any pink slip. Beating him is the King victory.

**What the player sees:**
- "Word on the street" at the diner (`GameState.StreetTalk`, the last 7 days, newest first): purchases, tuning,
  repairs, going broke, coming back, new faces, pink slips and wrecks between rivals.
- A rival whose car can't race is not at the diner and not in events (`IOpponentChallengeService.CanRace`); rivals'
  cars race with their real damage.
- Some buyers who answer the player's newspaper ads are rivals (`CarSaleService.RivalBuyer`, 40% when one wants the
  car: no racing car, or a weaker one, and room for it); the car then races under them.

## Key Services

| Service | Purpose |
|---------|---------|
| OpponentInitializationService | New career: the rivals with used cars with real parts, 6 on the street; the King |
| OpponentLifeService | The daily review (buy, repair, sell, bankrupt, activate, tune, street talk) |
| OpponentRules | The numbers of a rival's life (pure) |
| EngineTuner | Dyno-checked tuning within a budget (Parts/Cars, pure) |
| OpponentGenerationService | Create new opponents (unused) |
| OpponentEvolutionService | Apply race outcome changes |
| OpponentAIAdapter | Convert to AC parameters (static) |

## Challenges (`OpponentChallengeService`)
An opponent can turn a challenge down (`ChallengeResponse.DeclineReason`):
- **Cash wager**: no money or not enough (`InsufficientFunds`), a stake over half their bankroll unless they are
  aggressive (`BetTooHigh`), a rookie challenger (`ReputationTooLow`, by chance), or plain not interested.
- **Pink slip**: both cars valued by `CarValuation` (with their engines when the parts service is there). A car worth
  nothing, or a player's car worth less than 60% of theirs, is `CarValueMismatch`. Otherwise the chance to accept
  starts at 50% and moves with reputation, aggression, age, skill, their pink-slip record and the value ratio, clamped
  to 5-95%.
- `AlwaysAccept` (default off) accepts everything, for testing races; it never ships on.

The Diner asks before it prepares the cars' data, so a refusal costs nothing.

## Race Workflow
1. Load `Opponent` from save
2. Convert via `OpponentAIAdapter.ToAssettoCorsaAI(opponent)` (Diner and Newspaper events both go through it, via
   `RaceSetupBuilder`)
3. Pass AI params to launch intent
4. After race, call evolution service
5. Save updated opponent

## Files
- `Models/GameState/Opponent.cs`
- `Services/Opponents/OpponentLifeService.cs`, `OpponentRules.cs`, `OpponentInitializationService.cs`
- `Services/Scheduler/Tasks/OpponentReviewTask.cs`
- `Parts/Cars/EngineTuner.cs`
- `Services/Opponents/OpponentGenerationService.cs`
- `Services/Opponents/OpponentEvolutionService.cs`
- `Services/Opponents/OpponentAIAdapter.cs`
- `Services/Opponents/OpponentChallengeService.cs`
