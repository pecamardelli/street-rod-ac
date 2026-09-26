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

Tested by the user in the game (2026-09-25): collisions work.

Not yet tested by the user in a race: a clean drag race to the finish, a disqualification for contact either way, a
crashed rival being held, a false start on a road race, `assists.ini` coming back after the race.

**Done: step 2, damage, timeslips and the repair shop** (PR #18, merged as `d1106a2`; built
on 2026-09-24 as one PR, the user's choice). See `docs/ac-integration/csp-lua-scripts.md` "Damage" and
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

**Done: step 4, the economy** (PR #19, merged as `a04eaf3`). See `docs/systems/market-system.md` "Selling Cars".
- **The user decided:**
  - the difficulty belongs to the save, picked on the New Game screen (Easy, Normal, Hard, each figure adjustable, which
    makes it Custom), not app-wide settings;
  - selling through the paper is an ad with buyers calling over the days;
  - a dealer pays 60% of what the car is worth.
- **`GameRules`** (`GameState.Rules`; saves from before load as Normal) replaced the unused economy and difficulty fields
  of `GameSettings`, which now only holds the AC folder and the last free-run track. It drives:
  - `CarPriceMultiplier`: what the lots ask (new listings, relisted pink slips and trade-ins);
  - `PartPriceMultiplier`: the parts shop, the parts ads and the repair bay;
  - `RacePrizeMultiplier`: event cash, shown and paid (`EventReward.ScaledBy`);
  - `OpponentSkill/AggressionModifier`: on top of each rival's AI in `OpponentAIAdapter`, never under AI level 85;
  - `CarWearMultiplier`: AC's `DAMAGE` (`RaceDamagePercent`, at most 100) and the parts' mileage wear, and the simulated
    rivals' wear;
  - `PinkSlipFrequency`: rival-vs-rival pink slips (10% of road races on Medium), pink-slip events turning up, and how
    readily a rival takes a pink slip on;
  - `RaceSimulationEnabled`, `SeasonalRacingEnabled`, `MarketRefreshEnabled`: the daily rival races, their summer and
    winter rhythm, the dealers restocking.
  Presets: Easy 0.85/0.85/1.25, skill −3, aggression −15, wear 0.6, pink slips Low; Hard 1.15/1.15/0.85, +3, +15,
  1.3, High. The starting money stays $1M on every difficulty until a first release.
- **Selling** (`CarSaleService`, the garage's Sell button): the dealer on the spot, the scrapyard for a totaled car
  (15% ±20%), or an ad in the paper whose buyers show in the Sell dialog and under "Your Ads" in the newspaper.
  `CarsSold` and `CarsOwned` (on a purchase) are now counted.
- **Bigger bets** (`MatchupCalculator.StakesFactor`): the house limit grows by one for every 25 points of the rival's
  reputation over 50 (three times at 100) and doubles from 20:00, still capped by the poorer racer's money. The diner
  says why the stakes are up. Races still run in daylight: a night race (`SUN_ANGLE`) belongs with step 6.

Tested by the user in the game. Known issue: the newspaper's "Your Ads" panel runs off the bottom of the screen and
can't be scrolled; the newspaper screen is due a refactor.

## Step 5, living opponents (done, PR #20, merged as `70161ad`)

See `docs/systems/opponent-system.md` "Living Opponents" for what was built.

Checked with a scratch console harness: a new career on the real catalog, parts and lots, run for 60–90 game days
(review, rival races, market refresh; nothing saved). Rivals buy, sell, repair, go broke and come back, and newcomers
arrive on schedule. The review takes 0.1–0.8 s a day. The first cut tuned far too fast (every rival on 740 hp within
weeks), so it was slowed: 15% a day, one upgrade, 30% of the money over a reserve. The user then asked that the
rivals behave by the prices they find (prices get tuned later): every sum is price-relative, and the 1.8×-factory
power cap added meanwhile was dropped. Money is the limit. Many rivals end up in Chevelle SS 454
LS5s, because the dyno rates that engine at 555 hp against a cheap price (the known over-rating of some SLRR engines).

Tested by the user in the game (2026-09-24): the street talk panel, The King, a rival answering an ad, damaged rival cars.

Left for later: new racers generated when the pool runs dry (`OpponentGenerationService` is still unused); rivals
putting their own cars in the paper; rivals buying used parts out of the ads (they order new); the King's own
portrait; dyno horsepower in the diner's matchup panel (not picked by the user).

**The user decided (2026-09-24):**
- **Ramp up:** 6 rivals active at the start and 2 more each week, pulled from the inactive pool (the GameMaker rule).
  Rivals who lose their last car or go broke retire and come back through the daily review.
- **First cars are made, with real parts:** a used engine (sometimes tuned) and running gear on New Game, so the dyno
  and the repair bill are real from day one. After that they buy off the lots and answer the player's ads.
- **The King in this PR:** a fixed boss, strong well-tuned car, top skill, pink slips only, shown once the King victory
  unlocks (10 wins, 50 reputation). He lives through the same daily review.
- **Visible:** "word on the street" at the diner (purchases, tuning, going broke, coming back, pink slips) and rivals
  among the buyers who answer the player's ads (the car then races under them).
- Not picked: dyno horsepower in the diner's matchup panel (it keeps the catalog figures).

**Design:**
- `Car.PowerHp` / `UsedCarListing.PowerHp`: the dyno figure, cached; the simulator and the rivals' choices use it.
- `OpponentLifeService` + `OpponentReviewTask` (daily, before the race simulator): retired racers are fixed first,
  then the ready ones are checked; best car to the front (`Cars[0]`, the convention everywhere), buy when carless,
  repair when the whole bill is affordable (`RepairShop.Jobs`), sell the wreck and buy again when that gets them
  racing, keep at most 2 cars (extras to the trade-in lot at the dealer's 60%, wrecks for scrap), bankrupt = cash
  injection $50–500; then activation up to `6 + 2 × weeks`; then tuning.
- `EngineTuner` (Parts/Cars, dyno-checked): bolt-on swaps scored `gain% / cost × priority` (block swap 10, blower 8,
  carbs/injection 7, exhaust 6, manifold/camshaft 5, air 3), at least 3% gain, parts that stop fitting replaced to a
  depth of 5, a same-family bigger engine as the block swap; new parts at the mail-order price, replaced parts traded
  in. Run off the UI thread on clones, a few racers a day.
- The simulator's wear goes onto the parts (`CarCondition.ApplyRace` with a made-up condition), crashes can total a
  car, a pink-slipped car stays with the winner (the review sells it on), a carless loser retires.
- Rivals' cars race with their real damage (no more `CarCondition.Runnable`); a rival whose car can't race isn't at the
  diner.

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

## Step 4: economy (done)

The plan as it stood; what was built is under "Where things stand".

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

## Step 6: police chases (done, PR #21, merged as `fc450b8`)

**The user decided (2026-09-24):**
- real AI cops (design (c) below), not a ghost or a text roll;
- the 1974 Dodge Monaco mod as the police car, which the player installs;
- busted is a fine and the car in the impound, and the race counts as a loss;
- getting away earns reputation;
- the rival can be busted too, and both busted is no contest;
- races after 20:00 run at night, the police chance rolled from the time, the reputation and the stakes, and the
  diner shows the risk.

**What was built** (see `docs/ac-integration/csp-lua-scripts.md`, "The police chase"):
- **The career side.**
  - The diner rolls the patrol when a road race is agreed (`PoliceCars.Patrol`, `PoliceRules`):
    - the chance: 8% by day, 30% from 20:00, up to 15% more for the better known racer, 5% more each for a pink slip
      and a wager of $1,000 or more, at most 50%;
    - the cars: two, one for each racer, on a track with four pit boxes or more.
  - The police car is the installed car with liveries marked `"street_corsa_police": true` in `ui_skin.json`, a
    Monaco first (`PoliceCars.Find`). Without one, no police come and the diner shows no risk.
  - race.ini gets the cops as `[CAR_2]`… and `[STREET_ROD] POLICE`/`POLICE_SPOT`, and `SUN_ANGLE` from the game's
    clock for every race.
- **The race mode** runs the chase and writes `pursuit` (schema 1.5):
  - speed traps or a patrol;
  - a cop racing each racer, a racer it gets past busted;
  - one roadblock per cop;
  - lights and a synthesized siren.
- **The result:**
  - busted: the fine ($750, $500 more for each earlier bust, at most $5,000);
  - busted: the impound (2 days, one more for each earlier bust, at most 7, $100 a day; an unpaid fine goes on the
    bill), collected with the garage's Collect button;
  - getting away: +2 reputation each time, up to +10.
- **Rivals** pay to collect their cars, sit out meanwhile, and lose a car left unpaid 14 days past its date.
- **Organised events** (the newspaper) never draw police, but run at the game's hour too.
- **Drag races** have no police: the strip has two pit boxes and nowhere to run.

**The police car:** the 1974 Dodge Monaco Police (Stereo) is installed and sanitized to factory spec (275 hp net 440, 2.94
pursuit axle). Its liveries carry `"street_corsa_police": true` in `ui_skin.json`, which is how the game knows a police
livery (`PoliceCars`). Marked liveries never reach the catalog, and a car with nothing else stays out of it (no police
car on a lot or in a rival's garage). The civilian Monaco of the same mod is an ordinary car of the set.

**Tested in the game** (2026-09-24, unattended runs, see `docs/ac-integration/csp-lua-scripts.md`): the busted and got-away
paths, roadblocks, a stuck cop put back, night at 21:00. The game found five things the stub couldn't:
- the INI list bug, which also fixes step 2's body and suspension carry-over;
- cops ramming a stopped player;
- a roadblock square across the road with no way through;
- AI racers stopping behind a roadblock instead of going round it;
- a roadblock "left" in the frame it went up.

Tested by the user in the game (2026-09-25): a police chase works.

Not yet seen by the user: the lights and the siren (the screenshots caught the terminal over the AC window); the
diner's police note; the garage's impound panel.

**Speed traps** (added after the first review): two patrols in three are cops parked on the verge at random spots on
straights round the track, set off by whoever goes past (see the race mode doc). Only a racer a cop has been after has
a chase to win or lose. A cop that catches the rival stays with them. Checked in the game. The police chance was kept
as it was (the user thought 30–50% of races would be too many).

**The police drive like racers** (the user's rules, 2026-09-25):
- **One cop for each racer.** Each races its prey at AI level 150% with full aggression, and tries to get past.
- **Getting past is the bust.** The player's car is taken over by an autopilot and braked to a stop; the rival is
  held.
- **A dodged roadblock** sends the cop after its prey at full throttle.
- **The finish line is home:** a racer over it is out of the police's reach.
- **The police Monaco has the 426 Hemi.** The factory 440 couldn't keep up with a 450 hp Chevelle.
- **Dropped:** the boxing-in, the PIT, the pinned bust and a pushed rubber band, tried along the way.
- **The chance:** the police chance is unchanged, and it always sends two cars.

Left for later: Test Drive mode (race-explorer); rivals busted in their own races with each other; the difficulty's say
in the police chance; hiding the cops from AC's HUD leaderboard. Any other car can be a police car by marking a skin.

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

## Step 7: career gaps (done, PR #22, merged as `6aa3d39`)

The known gaps the analysis found, closed in one PR. **The user decided (2026-09-25):** only the picked victory
path wins, and the player keeps racing after the win or goes to the main menu; about half the events become road
races; the prize camshaft is the best one that fits. See `docs/career-system.md` ("Default Events", "Winning the
Game") and `docs/systems/time-system.md`.

- **End Day.** The garage's calendar panel has an End Day button: after asking, the rest of the day goes by, the
  night's tasks run and the game is saved.
- **Events:**
  - five are road races (Classic Showdown, Fifties Fever, Sixties Showdown, European Invasion, Chevy Challenge),
    raced on an installed circuit; the other five stay at the strip;
  - at most five invitations are open at once;
  - the career's reputation and cars-owned counters follow the player (`CareerState.SyncStanding`), so events that
    ask for reputation show from the first day;
  - the prize camshaft is real: the dearest camshaft that fits the winning engine and beats its own, on the shelf,
    or $500 when none does;
  - a pink-slip event only draws a racer of the pool, who has a garage for the player's car.
- **Victory:** only the picked path wins (any, with none picked); picking a path already reached wins at once. The
  victory screen shows the career sheet, with Keep Racing and Main Menu.
- **Cleared away:** `GameState.UsedCars`, `GameState.UsedParts`, `NewspaperAds.Cars` (never filled; old saves still
  load) and the unbound `RefreshMarketCommand`. `Player.Stats.CarsOwned` already went up on purchases.
- **Docs brought up to date:** `career-system.md`, `systems/time-system.md`, `diner-refactoring-plan.md`.

Not yet seen by the user: the End Day button, the victory screen, a road-race event. The end of the game (victory)
takes too long to reach by playing; the user will test it in the first release.

## Steps 8–15: what comes next (planned 2026-09-25)

Everything left over from steps 1–7, plus `docs/ideas.txt`, grouped into vertical slices. **The user decided:** the steps
are taken one at a time, in this order. The rename comes last because it touches every file and would clash with any
open branch.

### Step 8: playtest and fix
The user plays what nobody has seen yet, from a checklist, and one PR fixes what breaks. Already tested: collisions and
a police chase. Left out: the end of the game, kept for the first release.
- **Race rules:** a clean drag race to the finish, a contact disqualification either way, a crashed rival being held,
  a false start on a road race, `assists.ini` coming back.
- **Damage:** the dents redrawing at the start, breakdowns both ways, the timeslip marks on `ks_drag`, how the toe and
  gearbox data drive, the tow to the garage.
- **Police:** the lights and the siren, the diner's police note, the garage's impound panel.
- **Career:** the End Day button, a road-race event.
- **Economy:** a price balance check after the $5k price fix (audit 2026-09-25). **The user decided (2026-09-25):**
  they review the prices of every car and part themselves first, then the economy.

**Started before step 8 was done (2026-09-25):** the user plays the checklist while step 9 is built.

### Step 9: a new main screen (done, PR #23, merged as `7ff1fda`)
From `docs/ideas.txt`. Owned or catalog cars picked at random stand in a showroom, filmed with slow camera moves (like
Gran Turismo's menus). Over it, a fade to black rises from the bottom to about half the screen, and New Game, Load Game
and Settings are semi-transparent cards that fade in and out; the screen never navigates away to show them. It builds
on the garage renderer (`GarageRenderer`) and the engine preview.

**The user decided (2026-09-25):** catalog cars only (no save is loaded yet); the rooms rotate (the garage plus the
installed AC showrooms); no sound; the menu stays as buttons, and a click opens that button's card, one at a time, with
fades and a Back button, the cars always behind. **As built:** see `docs/screens/main-screen.md`. The cars fill the
top 72% of the window in a wide frame, with the fade from 40% to solid black; six GT-style shot kinds with dissolves
between them and a fade through black between cars; three shots a car, three cars a room. Esc closes a card. The old
picture shows until the first frame, and whenever there is nothing to show.

**The user saw it (2026-09-25): "I love it!"** Their tweaks, done: the fade and the menu moved to the top (cars in the
bottom 80%); several cars in each room (a lineup of 3–4, the camera staying with one at a time and keeping clear of
the others); the screen opens black, and the old picture only comes up when there is nothing to show. Also found:
encrypted car mods (scrambled normals) are now left out of the catalog, see `docs/systems/catalog-system.md`.

**Reviewed before the merge (2026-09-25):** an xhigh code review found 15 issues, all fixed in the PR. The worst was the
garage's navigation tiles throwing on hover. The others: Esc closing every card, cards opened as safely as screens, a
broken room skipped rather than ending the showroom, the central car in the main slot. Build and tests pass; not yet
tried in the game.

### Step 10: the world remembers (done, PR #24, merged as `2db9eb1`)
Three features, one PR:
- **car history:** every car keeps its odometer (`Car.OdometerKM` exists), previous owners and wins, and its price
  reflects them (`CarValuation`);
- **grudges:** a rival who lost a pink slip asks for a rematch, with talk lines to match (`StaticTalkService`);
- **newspaper articles** written from the race history.

**The user decided (2026-09-25):** history moves the price modestly (wins up to +15%, owners and mileage down to
−15%, condition still leads); the rematch is a diner offer (marked, grudge lines, pink slips accepted whatever the odds,
two weeks, in the street talk); the paper's articles cover the player's notable races and the rivals' big ones.

**As built:** see `docs/systems/market-system.md` ("Car History", "Pricing"), `docs/systems/opponent-system.md`
("Grudges") and `NewsWriter`.
- The history lives on the car (`CarHistory`), not in the race sessions: the rivals' races were never saved. The
  history is copied onto the listing and back, and a relisted car keeps its id (review fix).
- Articles (`GameState.News`, two weeks kept) are written when a race is settled, only when there is a story: the
  player's pink slips, the King, rematches, the police, wrecks, event wins, upsets, cash races of $1,000 and more, the
  first win; from the rivals' own races, pink slips and wrecks. The paper prints the last 3 days, 5 pieces, newest
  day first and the weightiest first within it, the lead in bigger type.
- The newspaper got a third column for them, **Street News**; the market buttons are two thirds their size side by
  side, and "Your Ads" scrolls: the overflow from step 4 is fixed. The invitations panel stays up with a line when
  there are none.
- The race session record now keeps the game date, both names and car ids, the race type and the event.
- Found on the way: the diner never told the talk service a pink slip was on the table, so the pink-slip lines were
  never said; fixed.
- The race snapshot now covers the street talk and the news too, so a race that fails to save leaves none behind.

Not yet seen by the user in the game: the new newspaper (checked in a render at 1920×1080), a rematch at the diner,
history on the lots and in the garage, the price effect on the lots.

**Reviewed before the merge (2026-09-25):** an xhigh code review found 14 issues, all fixed in the PR. The worst: picking
a rival who wanted a rematch left pink slips on the table for the next rival, and a rematch took a car worth nothing.
The others: a grudge kept after the rival bought the car back, headlines naming a racer twice, event races losing
their story to a wreck, older saves showing used cars as first-owner cars, a car won back counting its owner twice,
the King's car priced before its record. Build and tests pass; not yet tried in the game.

### Step 11: strip extras (done, PR #25, merged as `fdac6cc`)
- **Test-and-tune:** paid time at the strip for a timeslip with nothing at stake, building on the free run.
- **Bracket racing** with a dial-in.
- **Each car's best elapsed time.**
- **The part that failed:** name the one part that broke, not the whole rotating assembly (see step 2).

**The user decided (2026-09-25), on `feature/strip-extras`:**
- test-and-tune is free and costs only game time, like the free run; each pass gives a timeslip, with nothing at stake
  beyond the usual wear;
- bracket races come from the diner (a rival offers a bracket race as a race type), from bracket events, and the
  dial-in comes from the test-and-tune slips and the car's best ET;
- real bracket rules: each racer sets a dial-in, the slower car gets the green first by the difference, and breaking
  out (running quicker than the dial-in) loses; the player picks theirs, suggested from the best ET, and rivals dial in
  from their car's figures;
- when the engine fails, the weakest (most worn) part of the rotating assembly breaks and goes to 0%, the others take
  a smaller share, and the damage report and the repair shop name it.
Also chosen (not asked): the best ET shows in the garage and in the car's history, and it suggests the dial-in.

**As built** (see `docs/ac-integration/csp-lua-scripts.md` "Bracket races" and "Test-and-tune", schema 1.6):
- **Test-and-tune:** the garage's Test & Tune button, an hour at the strip (`GameAction.TestAndTune`). It picks
  ks_drag drag1000 when installed, else the longest strip that runs the quarter (`RaceSetupBuilder.PickStrip`).
  - It goes through the race pipeline with the player alone (`RACE_TYPE=TUNE`, 6 passes). Each pass has its own
    tree and timeslip, and the car is put back on the line after each.
  - The result is written after every pass. Afterwards the slips show side by side in the timeslip dialog, with the
    day's best.
  - The car wears and can break as in a race. Nothing else changes: no stats, no money.
- **Bracket races:**
  - At the diner, a "Run it as a bracket race" box on any strip that runs the quarter (400 m and up). There are two
    new bracket events, Bracket Night and Dial-In Shootout.
  - The player picks a dial-in in a dialog (±0.01 and ±0.1). It is suggested from the car's best rounded up to 0.05,
    or estimated from power and weight.
  - The rival dials in from its car's best, else Hale's formula for a street car (`BracketRules.StreetEtFactor`,
    not yet checked against AC's AI).
  - The mode runs a tree for each lane and holds the rival to its green. The rival takes the stripe by capping its
    AI top speed near the end.
  - Red light = false start (no contest, the existing rule). A breakout loses unless the other broke out by more.
  - The career decides again from the slips (`BracketRules.Decide`, `WinCondition.BracketFinish`, `PlayerBrokeOut`,
    `OpponentBrokeOut`).
- **Best ET:** `CarHistory.BestQuarterSeconds`/`Mph`/`Date`, from any quarter the car runs, whoever drives it. It
  shows with the history, and the timeslip says when it is a new best.
- **The part that failed:** the weakest rotating part takes the loss and the others 35% of it. The report says "a
  connecting rod let go", and the repair shop names the part.
- **Timeslip dialog:** any number of columns (lanes or passes), a DIAL row in a bracket race, "RL" on a red light,
  and a note line.
- **Tests:**
  - `tools/sr_race_harness/test_strip.py`: the chase stub laid out as a straight strip, 8 scenarios, and golden
    files for a bracket race and a test-and-tune.
  - The C# contract test checks that the career and the mode pick the same bracket winner.

Not yet tried in the game. **The user decided (2026-09-25):** they test these after the first release; the
checklist is `docs/playtest-step-8.md`, "6. Strip extras (after the first release)".
- the tree overlay and AC's own start lights together;
- the rival held and let go at its green;
- the stripe-taking with AC's real AI;
- `setCarPosition` putting the car back on the line between passes, and going to the pits to end a test-and-tune;
- whether the rivals' estimated dial-ins are near what AC's AI runs;
- the diner's bracket box and the dial-in dialog.

**Reviewed before the merge (2026-09-25):** an xhigh code review found 15 issues, all fixed in the PR. The worst: on a
test-and-tune every write after the first pass failed (CSP's `io.move` does not overwrite by default), so a crash on a
later pass was never applied. The others: a pass under way when the session ended was dropped, a car put back past its
spot never staged, the mode and the career could disagree on a breakout at the dial-in, bracket events could go to a
strip short of the quarter, strip lengths misread ("1,000 m", feet), small engine wear lost, an engine without rotating
parts keeping no damage, and a TUNE file settling a race. Build and tests pass; not yet tried in the game.

### Step 12: a bigger opponent pool (in progress on `feature/opponent-pool`)
After step 10, since both touch the rivals and the newspaper.
- New racers when the pool runs dry (`OpponentGenerationService` is written but unused).
- Rivals put their own cars in the paper and buy used parts out of the ads.
- The King's own portrait.

**The user decided (2026-09-25):**
- new racers come from a second batch of 30, drawn with the ComfyUI script like the first 30;
- racers leave the scene for good now and then;
- a full private market: rivals advertise spare cars, which the player and other rivals buy;
- used parts both ways: what a rival takes off goes into the parts ads under their name, and rivals buy used parts
  there when they ask less.
Also chosen (not asked): once the new faces run out, racers who left 90 days ago or more come back; rivals trade up
now and then, which is what fills the paper.

**As built** (see `docs/systems/opponent-system.md`, "A bigger pool"):
- 60 racers and the King (`drv_031`–`drv_060` with prompts). Older saves get the newcomers and any missing portrait
  (`EnsureNewcomers`).
- Leaving (`RacerStatus.Departed`): 0.2% a day; 5% a day after 45 days sitting out; always on going broke a third
  time. Never the King, nor a racer with a race pending, a grudge or an offer on the player's car.
- Rival car ads (`RivalCarAds`): 75–90% of the lot price, 10% off after a week, to a dealer after 14 days. They show
  on the paper's Used Cars page as "(private)". Trade-ups: 5% of days, 20% more power, a quarter of the price left over.
- Rival part ads (`RivalPartAds`): parts taken off go into the paper; unsold ones get the shop's trade-in. The tuner
  buys used loose parts and whole engines (`UsedPartOffer`).
- The Life harness (180 days) came out at about 30 private sales, 18 leaving and 7 back. A first run had 22 leaving at
  0.4% a day, so the chance was halved.

**Portraits:** the 30 new racers and the King were drawn with `generate-portraits.js` (ComfyUI, juggernautXL) on
2026-09-26; the King's definition points at `king.png`.

### Step 13: the street encounter
From `docs/ideas.txt`, after step 9 because it reuses its renderer work. Not SLRR's Valo City: the player's car seen
from the driver's seat, looking left; a rival drives up and the race dialog pops up, as in Street Rod. Both cars have
their engine sound and body movement under braking and throttle (the engine preview has both). **Decide first:** the
street scene. The showroom research (`reports/AC showrooms for dealer and garage.md`) found nothing licensed to ship,
so it is likely one we build.

### Step 14: deeper simulation and police extras
Research first: CSP limits much of it (it fills oil figures only for scripted cars).
- Overheating and oil starvation modelled by the game, fuel carried between races, body dirt.
- Test Drive mode (race-explorer), rivals busted in their own races, the difficulty's say in the police chance,
  hiding the cops from AC's HUD leaderboard.

### Step 15: the rename to Street Corsa
Namespaces (`Street_Rod_AC`), the AppData folder (`StreetRodAC`, with a migration), ids like `sr_race`, and the repo.
Before a release, check the licence of every shipped asset: the icons in `Assets/Icons` are of unknown origin, and the
garage showroom is not licensed for the public repo.

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
