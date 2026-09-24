# Street Corsa roadmap

The game's official name is **Street Corsa** (decided 2026-09-24). The repo, namespaces (`Street_Rod_AC`), the
AppData folder (`StreetRodAC`) and ids like `sr_race` still carry the old name. The full rename is planned for later.
Until then, use the new name in player-facing text and docs, and leave ids and paths alone.

Written 2026-09-24 after an analysis of the project and the user's answers (their original notes:
`docs/next_steps.txt`, untracked). Each step below lists what the user decided, what the research found, and the
design. Steps are meant to be vertical slices, one PR each, to `dev` (remote `github`; PRs are squash-merged).

## Where things stand

**Done: step 1, the race mode** (PR #17, merged as `a83683e`). See `docs/ac-integration/csp-lua-scripts.md`.
- **The race script is a CSP mode.** `apps/new-modes/sr_race` (was the Lua app `apps/lua/sr_race_manager`) runs with `ALLOW_PHYSICS_ALTERATIONS=1`, which grants the physics API. `SrRaceMode` installs it into the AC install before every race.
- **`race.ini` selects it** with `[RACE] __CM_CUSTOM_MODE=sr_race`. `assists.ini` gets `DAMAGE=100` and `TYRE_WEAR=1` for the race, and both files are restored afterwards.
- **Every race, drag races too, is a one-lap race session (`TYPE=3`).**
  - `JUMP_START_PENALTY=0`, pit teleports blocked, car recovery off: AC never moves a car.
  - `[STREET_ROD] RACE_TYPE=DRAG|ROAD` tells the mode which kind of race it is.
- **Crash:** a collision with a change of velocity of 15 g or more over one frame.
  - A crashed player is stopped and the race ends.
  - A crashed rival is held where it stopped.
  - Past the finish line nothing counts.
  - A teleport is not a crash.
- **False start:** moving more than 1 m before the green. No contest, nothing changes hands, and each one costs 3 reputation (up to 15).
- **Drag race contact:** crossing lanes is fine. When the cars touch, the car further out of its own lane is disqualified: the player's disqualification is a loss, the rival's a win.
- **The player always gets to finish.** Quitting AC is a forfeit.
- **Result schema 1.3:** `end_reason`, `race_type`, `false_start`, `disqualified`, and `condition`. `condition` is the car as AC left it: body damage by zone, engine life, gearbox, oil and water, fuel, and per wheel tyre wear, virtual km, blown and suspension damage. Step 2 puts `condition` onto the parts (schema 1.4 since).

Not yet tested by the user in a race: a clean drag race to the finish, a disqualification for contact either way, a
crashed rival being held, a false start on a road race, `assists.ini` coming back after the race.

**Done: step 2, damage, timeslips and the repair shop** (PR #18, `feature/damage`, open against `dev` on
2026-09-24; one PR, the user's choice). See `docs/ac-integration/csp-lua-scripts.md` "Damage" and
`docs/systems/parts-system.md` "Damage and repairs".
- **The user decided:**
  - AC's damage at 100%;
  - a car with a blown engine, a wrecked gearbox or a totaled body (200 km/h of hits, all zones together) does not
    race or free-run until repaired, and neither does one with a broken corner or a blown tyre;
  - after a crash the player is towed to the garage and sees the damage report there.
- **Damage lands on the parts** (`CarCondition`):
  - engine life on the `Tear` of the rotating parts (crankshaft, rods, pistons, camshafts);
  - gearbox damage on the transmission;
  - a bent steering rod on that corner's spring and shock;
  - tyre wear on the tyres' `Wear`, a blown tyre's `Tear` to 0;
  - body damage on the car, in km/h per zone (`Car.BodyDamageKmh`);
  - a little mileage `Wear` on the engine.
  `Car.EngineHealth` and the other car figures are worked out from the parts after every race and repair. Old result
  files without `condition` still get the flat wear of before.
- **The next race starts where the last left off.** `race.ini [STREET_ROD]` gets `CAR_n_BODY`, `ENGINE_LIFE`,
  `GEARBOX` and `SUSPENSION`. The mode sets body and engine life. The car's data carries the rest (`AcDamageData`):
  slower shifts for a worn gearbox, toe for a bent axle. AC's tyre km are deliberately not carried: the tyre's wear
  already lowers its grip in `tyres.ini`.
- **Breakdowns** (schema 1.4, `BROKE_DOWN`): a blown engine, a gearbox or a corner past what it can take, a blown
  tyre. The player's is a loss, the rival's a win, both cars out a draw (`WinCondition.BothOut`).
- **Timeslips** for drag races (R/T, 60', 330', ⅛, 1000', ¼, trap speeds over the last 66 ft), shown in a timeslip
  dialog after the race.
- **The garage's Repairs button** (`RepairShop`): engine and gearbox rebuilds, straightened corners, new tyres, body
  work, for money and garage time (`GarageWorkMinor` 30 min, `GarageWorkMajor` 2 h).
- **Opponents' cars** carry their damage too, but race with just enough to leave the line (`CarCondition.Runnable`)
  until step 5 has them repair their cars.

Not yet tested by the user in the game:
- the scratches and dents redrawing when the body damage is set at the start;
- breakdowns, both ways;
- the timeslip marks on `ks_drag` (its layout is 1000 m, so the ¼ mile falls inside the lap);
- how the toe and gearbox data drive;
- the tow to the garage after a crash.

Left for later: overheating and oil starvation modelled by the game (CSP only fills oil figures for scripted cars),
fuel carried between races, body dirt, each car's best elapsed time, bracket racing with a dial-in, and whether AC's
full damage wrecks 1970 street cars too fast (the user chose 100% to start).

## Next: step 4, economy

Step 3 is done (see below) and step 2b went in with step 2, so the economy is next. Before starting: check that PR #18
is merged into `dev`, and branch from `dev`.

## Step 2: damage, as real as AC allows (done, PR #18)

The research and the design sketch, kept as the record. What was built differs in places: see "Where
things stand".

**User:**
- "Everything that AC takes into consideration should be ported to the game": map everything AC tracks.
- Visual damage wanted ("even scratches in the body will work").
- A broken engine, suspension or gearbox during the race puts that racer out. If both break, it's a draw (agreed).

**Facts (CSP 0.2.11 SDK, checked in the game 2026-09-24):**
- **Reading** needs no permission. Everything above is already in `condition`.
  - Oil temperature and oil pressure read about 0 on stock cars: CSP only fills them for cars with a script. The game should model overheating and oil starvation itself, from the parts and how long the engine sat near the limiter.
  - Brake temperatures exist only on the 46 cars that have `[TEMPS_*]` in `brakes.ini`.
- **Writing at the start**, with the mode's physics API:
  - `physics.setCarBodyDamage(i, vec4)` and `physics.setCarEngineLife(i, v)`: both take, and read back, in the game.
  - `physics.setTyresVirtualKM` and `physics.setCarFuel`.
  - `ac.setBodyDirt(i, v)`, which needs no permission.
- **No setter for gearbox or suspension damage.** Fake them in the car data the game already writes for each race:
  - a bent corner as toe and camber offsets in `suspensions.ini`;
  - a worn gearbox as a tighter `RPM_WINDOW_K` or slower shifts in `drivetrain.ini`.
- **Visual damage follows `damage[]`.** 102 of the 165 cars have a `damage.ini` with paint scratches, cracked glass and hanging bumpers or bonnets, all driven by the body damage zones. Setting the body damage at the start therefore brings the visuals back too (still to confirm in the game that the scratches redraw at once). CSP can also crumple the bonnet on some cars (`[DEFORMING_HOOD]`).
- **What AC already does by itself:**
  - it kills an engine whose `engineLifeLeft` reaches 0;
  - over-revving wears the engine, since `AcEngineData.cs:155` already sets `RPM_THRESHOLD` from the weakest engine part;
  - missed shifts raise `gearboxDamage`.
- **Breakdowns the mode can force** from part wear (all need the physics API): `setCarEngineLife(i, 0)`, `blowTyres`, `lockUserGearboxFor`, `setGripDecrease`.

**The disconnect to fix first:**
- The race's wear currently lands on car-level fields (`Car.EngineHealth`, `TransmissionHealth`, `BodyCondition`, `TireCondition`) in `RaceResultProcessor.ApplyCarDegradation`. Once a car has its part tree, those only change its price.
- The part `Wear` that actually changes grip, brakes and the engine script (`AcRunningGearData.cs:87`, `PartScriptRuntime`) is set when a part is made (`EngineFactory.cs:101`) and **never goes down**.
- Step 2 moves the race's wear onto the parts, and works the car-level values out from the parts or retires them.

**Design sketch:**
1. **C#:** read `condition` into `RaceResultJson`, then map it to parts:
   - body zones go to the body panels' `Tear`/`Wear`, kept as km/h per zone so they can go back exactly;
   - `engine_life` goes to the engine internals;
   - `gearbox_damage` to the transmission;
   - `suspension_damage` to that corner's suspension parts;
   - tyre wear to the tyres.
2. **The car keeps its AC state for the next race:** body zones, engine life and tyre km, stored on the car or its parts.
3. **`RaceCarData`/`race.ini` carry the start state to the mode.** A new `[STREET_ROD]` key or a small JSON file next to `race.ini`. At the start, the mode calls `setCarBodyDamage`, `setCarEngineLife`, `setTyresVirtualKM` and `setBodyDirt`. The rival's car carries its own state the same way.
4. **Breakdowns:** the mode checks every frame for a dead engine (`engineLifeLeft <= 0`), a gearbox near 1, or a blown tyre. That car is out:
   - a player breakdown ends the race, `end_reason` `BROKE_DOWN`;
   - a rival's breakdown holds it where it stopped;
   - both broken is a draw.
5. **Repair shop:** time and money per part, using `GameAction.GarageWorkMinor/Major`, which exist but are unused. A "totaled" state for a car whose crash was hard enough.
6. **Open:** how much of AC's damage to scale (the full rate may wreck cars too fast for 1970 street cars), and whether a car too damaged to run can race at all (`RaceSetupBuilder` already refuses a player car that won't run).

## Step 2b: timeslips (done with step 2, PR #18)

**User:** yes.

The mode records reaction time (green to the car moving), 60 ft, ⅛ mile, ¼ mile elapsed time, and trap speed for
both cars, measured from AC's green (`sim.isSessionStarted` turning true; in a race session that is AC's start
lights, and the rival leaves on it). The distances come from progress along the track (`car.splinePosition` times the
track length, or distance from the line along the strip's axis the mode already has). They go into the result, and
the app shows a timeslip after a drag race. Built: the elapsed times run from the car leaving the line (0.2 m), the
reaction time from the green to that, the distance is along the strip's axis. Later: bracket races with a dial-in,
and each car's best elapsed time.

## Step 3: crash and start rules (done)

**Done:**
- a crash is 15 g during a collision (Test Drive uses 10 g in `C:\GAMES\Assetto Corsa\extension\lua\new-modes\test-drive`);
- quitting AC is a forfeit;
- a false start is no contest plus a reputation drain;
- drag race contact disqualifies the car further out of its lane.

**Dismissed:** a flagger in the street. The user thought AC had one; building one (a model and an animation, or a picture standing in the road) was dismissed, so AC's start lights stay.

## Step 4: economy

**User:** yes to all, except that the starting money stays at the $1M placeholder until a first release (they are testing).
- **Selling cars:** to the dealer and in the newspaper. `GameAction.SellCar` exists but nothing uses it, and `CarsSold` is never written.
- **Make `GameSettings` do something.** None of its economy or difficulty fields is read anywhere (`CarPriceMultiplier`, `PartPriceMultiplier`, `RacePrizeMultiplier`, `OpponentSkill/AggressionModifier`, `CarWearMultiplier`, `PinkSlipFrequency`, `RaceSimulationEnabled`, `SeasonalRacingEnabled`, `MarketRefreshEnabled`). The New Game screen could offer a difficulty. `CarWearMultiplier` could scale AC's damage (`IniModificationService.RaceDamage`, now 100) and the mileage wear (`CarCondition.MileageWearPerKm`).
- **The repair shop:** done in step 2 (`RepairShop`). Its prices are constants in `RepairShop` (60% of the damaged
  parts' new price, $15 labour, $8 per km/h of body damage): `PartPriceMultiplier` or a labour setting could scale them.
- **Selling a wreck:** a totaled car should sell for scrap (the GameMaker version pays 15% ±20% of its value).
  `CarValuation.ConditionOf` already sees the damage, since the car's figures come from its parts.
- **Bigger bets:** they are $10–100 on drag and $25–250 on road today (`MatchupCalculator.cs:17-32`), and could grow at night and against higher-reputation rivals.

## Step 5: living opponents

**User:**
- "Opponents should be fully alive".
- They buy cars from the dealers and the newspaper, like the player.
- They tune their cars within their budget.
- When broke, they can come back with new money.

Source: the user's GameMaker remake, `C:\Users\Administrator\GameMakerProjects\street-rod`.
`RaceSimulatorService.cs` already ports its race simulation (`scr_simulate_races`) almost line for line. What was never
ported is the daily life cycle:

- **The daily review** (`scripts/scr_review_racers`), a new scheduled task:
  - **Pools:** `inactive`, `readyToRace` and `retired`. The minimum number of active racers is `6 + 2 × weeks elapsed`, pulled from `inactive`.
  - **Pick a car:** the best owned car, scored as `hp×10 + condition×5 + 1000 if it runs + value×0.1`.
  - **No car:** buy the best affordable one from the same listings the player sees; it leaves the market. It needs at least $100. Purchase score:
    - `hp×2 + cond×3 + 500 if it runs + (hp/price)×1000`;
    - plus `(1 − price/budget)×200` when the car costs under 70% of the budget, so money is left for repairs.
  - **Car doesn't run:** repair only if the whole bill is affordable (`StructCar.repair`, `StructCar.gml:541`). Otherwise the racer retires until they can. Since step 2 this is real: an opponent's car keeps its damage, and `CarCondition.WhyCannotRace` plus `RepairShop.Jobs` give the bill. Once opponents repair, `RaceSetupBuilder` can stop making their cars `Runnable`.
  - **Bankrupt** (no running car, and less money than the cheapest used car, or $200 if the market is empty): a cash injection of $50–500, then back to racing.
  - Retired racers are checked daily and return once they're ready.
- **Tuning within budget** (`StructCar.tuneUp` + `scr_get_car_upgrades`), to run every day in the review, not only when a car is generated:
  - each upgrade is scored by `improvement% / cost × slot priority`: block 10, supercharger 8, fuel injection 7, exhaust 6, intake 5, transmission 4, fuel and air delivery 3, differential 2, others 1;
  - an upgrade must gain at least 3%;
  - parts that stop fitting (block → intake/exhaust → carb → air cleaner) are replaced, searching to a depth of 5;
  - upgrades are picked greedily within budget, one per slot.

  Build it on the part tree's slot fittings (`FindFittingParts`), paid from the opponent's money.
- **Changes this needs in Street Corsa:**
  - `CarPurchaseService.PurchaseAsync` and `PartsShopService.BuyNew/BuyUsed` are wired to `gameState.Player` and spend game time: they need a buyer and a path that costs no time.
  - `RaceSimulatorService` fakes horsepower as `100 + PurchasePrice/50` (line ~501) and only uses `Cars[0]`. It needs the real dyno figure cached on the `Car` and refreshed when parts change, or tuning won't change who wins.
  - `OpponentInitializationService.GenerateOpponentCar` invents cars instead of buying listings. Opponents start with $2000–5000 here, against $200–800 in the GameMaker version.
  - `OpponentEvolutionService` and `OpponentGenerationService` are never instantiated. The docs say evolution runs after each race; it doesn't. The pool only shrinks.
  - A crash could total the car as in the GameMaker version: 15% ±20% of its value as scrap, which goes to the winner in a pink-slip race.
- **Also:**
  - `MatchupCalculator` compares catalog horsepower, not what's under the hood: use the dyno.
  - Add "The King" as a boss at the top of a reputation ladder. The King victory can't be won today, because no racer is called "The King" (`KingVictory.cs:13, 84`).

## Step 6: police chases

**User:** loves it, and wants it for Test Drive mode too.

**Research (2026-09-24):**
- **No free single-player police-chase script exists.** Cops-and-robbers only exists on online servers with human cops.
- **CSP AI only drives along the track's AI spline.** It can be:
  - shifted sideways with `physics.setAISplineOffset` / `setAISplineAbsoluteOffset` (clamped to the spline's width; `true` blinds it to other cars);
  - swapped for another spline with `physics.setAISpline`, which is experimental; the user's `new-modes/spline-spike` was built to measure it and has no recorded results;
  - taken over with `ac.overrideCarControls(i)` for steering (pedals combine by maximum, so hold the throttle limit at 0 and drive it yourself).
- **Pace controls:** `setAITopSpeed`, `setAIThrottleLimit`, `setExtraAIGrip` (the cheap rubber band), `setAIStopCounter`, `setAILevel`/`Aggression`. CSP resets aggression, so re-set it about every 0.5 s.
- **Placing and hiding cars:** `setAICarPosition`, `setCarVelocity`, `ac.setCarActive(i, false)` to hide, `physics.disableCarCollisions`.
- **Lights and siren:** AI cars' own light bars can't be switched from Lua. Draw them: an `ac.LightSource` on the roof, alternating red and blue, and/or `setMaterialProperty('ksEmissive')`. Siren: `ac.AudioEvent.fromFile({use3D = true, loop = true})` with `:setPosition` every frame, which gives a Doppler effect.
- **Cars:**
  - the [1974 Dodge Monaco police](https://www.overtake.gg/downloads/1974-dodge-monaco.22283/) is the best period car (rated 4.91, Bluesmobile-style liveries, no working light bar);
  - or a black-and-white skin on a Galaxie 500 four-door, Impala 1962, Belvedere 1965, Fury 1958, Coronet 1967 or Montego 1970, all already installed;
  - no police car is installed today.
- **Tracks:**
  - in the Street Rod AC install: Black Cat County (rural US) and Highlands. Both are loops with one AI line.
  - in the main AC install, tracks with `traffic.json`: `new_plymouth` (a real town grid), `tripoli`, `mecheria` and others.
  - chases don't suit the drag strip.

**Design, simplest first:**
- (a) **Text only:** after the race, work out the outcome from risk and the player's margin.
- (b) **A ghost cop:** lights and siren that follow the player's progress with no collisions, with busted or escaped decided from the gap.
- (c) **A full AI chase on loop tracks:**
  - 1–3 police as extra `race.ini` entries, hidden and parked, then placed 150–300 m behind the player and released at full pace;
  - pace adjusted from the gap, and shifted sideways to box the player in within about 30 m;
  - if a cop falls too far behind, it jumps ahead as a roadblock.
- (d) **Later:** custom driving code for town tracks such as `new_plymouth`.

**Outcomes:**
- **busted:** a cop within 8 m and the player under 15 km/h for 3 s;
- **escaped:** more than 600 m ahead for 20 s;
- the result carries a `pursuit` field, and the app applies a fine, impound or reputation;
- the police are left out of the race result.

**Setting:** a night street race (`SUN_ANGLE`/time in `race.ini`; races run in daytime today), the risk rolled from the neighbourhood, time and reputation.

## New ideas the user liked

- **Test-and-tune:** paid time at the strip for a timeslip with nothing at stake, building on the free run.
- **Rivals remember you:** a grudge rematch after a pink-slip loss, with talk lines to match. `StaticTalkService` today; the LLM talk planned in `docs/ai-integration.md` later.
- **Newspaper articles written from race history** (the processed race sessions are already saved).
- **Car history:** every car keeps its odometer, previous owners and wins, and its price reflects them.
- **Parts that fail in the race:** an over-revved engine throws a rod in AC, and you find the damaged part on the workbench afterwards. Since step 2 the whole rotating assembly takes the damage together (the parts view shows it); singling out the one part that failed is still open.

## Known gaps found in the analysis (not scheduled yet)

- **No end-day button.** The day only ends when spending time goes past 22:00 (`GameTimeService`); `EndDayAsync` has no UI.
- **Events:**
  - all 10 events are drag races, with no track set;
  - nothing caps how many events are open at once (the docs say 5);
  - events that need reputation are hidden until the first decided race, because the `ReputationReached` counter starts at 0 while reputation starts at 50;
  - the "rare_camshaft" special item is only logged (TODO in `RaceResultProcessor`);
  - a pink-slip event can't take the player's car, because the event opponent has no garage.
- **Victory:** any achieved condition wins whatever the player picked (`ActiveVictoryType` is only displayed), and winning sets `HasWonGame` without ending anything.
- **Stats:** `Player.Stats.CarsOwned` only goes up on pink-slip wins, not on purchases.
- **Unused data:** `GameState.UsedCars`, `GameState.UsedParts` and `NewspaperAds.Cars` are never filled.
- **Unused screen code:** `UsedCarMarketScreenViewModel.RefreshMarketCommand` is never bound.
- **Out-of-date docs:** `docs/career-system.md` "Not Yet Implemented" (all of it is built), `docs/systems/time-system.md`, the checkboxes in `docs/diner-refactoring-plan.md`.

## Testing in AC without driving

See `docs/ac-integration/csp-lua-scripts.md`, "Testing the race mode without driving". In short:
1. back up `Documents\Assetto Corsa\cfg\race.ini` and `assists.ini`;
2. write a `race.ini` with `__CM_CUSTOM_MODE=sr_race`;
3. append a shim to the installed `mode.lua` (logging, autopilot, quit after N seconds);
4. run `C:\GAMES\Street Rod AC\acs.exe`;
5. read `Documents\Assetto Corsa\logs\custom_shaders_patch.log` (the lines tagged `[Street Corsa]`) and `log.txt`;
6. screenshot the windowed AC from PowerShell (`Graphics.CopyFromScreen`) while it runs;
7. afterwards, restore the cfg files, delete the result from `out\sr_race_manager`, and copy the repo's `mode.lua` back.

**Hard-won facts:**
- CSP reads `[RACE] __CM_CUSTOM_MODE`; `MODE=` and `__CM_NEW_MODE_USED` do nothing.
- A mode's `prepare()` and `update()` aren't called on the pits menu: a module-level `setInterval` has to call `ac.tryToStart(true)`.
- AC's drag session starts about 14 s before the tree goes green, and its jump-start rule put the car back after about 17 m. It is no longer used.
- On `ks_drag` as a race session, the autopilot steers into the wall. Once it stops, AC retires it as an AI and ends the race, so a finish can't be tested unattended.
- AC's drag reset teleported the cars without firing `ac.onCarJumped`.
- The cfg folder is shared by the Street Rod AC install (`C:\GAMES\Street Rod AC`) and the main AC install (`C:\GAMES\Assetto Corsa`). Test Drive, race-explorer's CSP mode, is deployed in the main install.
