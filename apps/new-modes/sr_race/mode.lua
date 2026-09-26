--[[
  Street Corsa race mode

  One street race against one rival, for the Street Corsa career:
  - Starts the race by itself (no pits menu)
  - Judges a crash: a collision with a change of velocity of CRASH_G or more. A crashed player is stopped and
    the race is over; a crashed rival is stopped where it lies and the player still has to finish
  - Judges a false start: the player's car moving before the green, or AC putting it back before it got anywhere
  - A drag race: whoever hits the other car out of their own lane is disqualified (crossing lanes alone is fine:
    nobody judges lanes on the street). AC's own start lights give the green. Each car gets a timeslip.
  - Puts each car into AC as its earlier races left it (race.ini [STREET_ROD] CAR_n_*): the body's damage, which
    brings the scratches and dents with it, and the engine's life. AC cannot be told about a worn gearbox or a bent
    corner; the career puts those into the car's data, and this mode adds them to what the race does.
  - Judges a breakdown: a blown engine, a gearbox or a corner that gives out, a blown tyre, an empty tank. A player's
    breakdown ends the race; a rival's is held where it stopped, and the player still has to finish
  - Runs each racer's engine heat and oil (engineHeat): a car cooled too little for what its engine makes runs hot,
    goes flat, cooks and can boil over; a stock sump surges under g held too long. The career rates the car, and
    sends its fuel and dirt from its earlier races
  - Runs the police chase when the career sends cops (race.ini [STREET_ROD] POLICE): they wait hidden on the grid
    and come after the racers part way round the lap, with lights and a siren. A driver who stops with a cop on
    top of them is busted; the player who stays far enough ahead for long enough gets away. The line is home: a
    player who crosses it with cops still on their tail got away (the harness pins this: the_line_is_home)
  - A bracket race (race.ini [STREET_ROD] DIAL_IN=player,rival): each lane gets its own tree, the slower dial-in
    first by the difference, and the race is to the quarter mile. Leaving before your green is a red light (a false
    start), and running quicker than your dial-in is a breakout, which loses unless the other car broke out by more.
    The rival takes the stripe: near the end it lifts when its pace would beat its dial-in.
  - Test-and-tune (RACE_TYPE=TUNE): the player alone on the strip, pass after pass, each with its own tree and
    timeslip. After a pass the car is put back on the line. The session ends after TUNE_PASSES passes, a crash or a
    breakdown, or when the player goes to the pits; the result is written after every pass, so closing AC keeps them.
  - Reports the race to the career, one JSON file per race, written once, and quits Assetto Corsa

  Full control: every race, drag races too, is a one-lap race session (Street Corsa never asks AC for its drag
  session, whose lane disqualifications, jump-start resets and match resets teleport the cars), with jump-start
  penalties off, pit teleports blocked and car recovery off. What happens to a car is this mode's call.

  A mode rather than an app since 2026-09-24: its manifest grants the physics API (ALLOW_PHYSICS_ALTERATIONS),
  which an app only gets from a track that opts in, and none of ours do. Without it the old app's control lock
  on a crash did nothing.

  Crash detection is the one Test Drive settled on (race-explorer's new-modes/test-drive): the change of
  velocity over one frame, counted only while CSP reports the car in a collision. Hard braking, kerbs and
  landings never report a collision, and a teleport moves a car without touching anything, so neither can pass
  for a crash. Test Drive uses 10 g; a street race uses 15, so a car that takes a knock and can still run is left
  to finish.
]]

-- manifest.ini [ABOUT] VERSION goes with it
local SCRIPT_VERSION = "3.7.0"
-- 1.1: session.context_id, participants[].car_index and participants[].is_player
-- 1.2: session.end_reason, participants[].false_start, participants[].condition
-- 1.3: participants[].disqualified, session.race_type
-- 1.4: session.end_reason BROKE_DOWN, participants[].broke_down, participants[].breakdown, participants[].timeslip
-- 1.5: pursuit, session.end_reason BUSTED; the police are never participants
-- 1.6: session.race_type TUNE with one participant and its participants[].passes; timeslip.green_s,
--      timeslip.red_light; participants[].dial_in_s and participants[].breakout in a bracket race
-- 1.7: condition.max_fuel_litres, condition.dirt and condition.heat; participants[].breakdown FUEL, OVERHEAT and OIL
local SCHEMA_VERSION = "1.7"

-- How far AC bends a steering rod at most, in metres (suspensions.ini MAX_DAMAGE, 0.05 on every car the game has):
-- a corner bent this far, with what it carried in, has given out
local MAX_SUSPENSION_BEND = 0.05

-- A drag strip's marks, in metres from the line. The trap speed is the average over the last 66 ft before a mark,
-- as the strips time it.
local FEET = 0.3048
local STAGE_METRES = 0.2
local TRAP_METRES = 66 * FEET
local MARKS = {
  { key = 'sixty_ft_s', metres = 60 * FEET },
  { key = 'three_thirty_ft_s', metres = 330 * FEET },
  { key = 'eighth_trap', metres = 660 * FEET - TRAP_METRES, hidden = true },
  { key = 'eighth_mile_s', metres = 660 * FEET, trap = 'eighth_trap', speed = 'eighth_mile_mph' },
  { key = 'thousand_ft_s', metres = 1000 * FEET },
  { key = 'quarter_trap', metres = 1320 * FEET - TRAP_METRES, hidden = true },
  { key = 'quarter_mile_s', metres = 1320 * FEET, trap = 'quarter_trap', speed = 'quarter_mile_mph' }
}

-- The strip's own rules (a table: the mode is near Lua's 200 locals):
-- QUARTER: the quarter mile, in metres
-- TREE_STEP, TREE_AMBERS: the tree, a sportsman's: three ambers half a second apart, the green half a second after
--   the last
-- BRACKET_LEAD, BRACKET_WAIT: a bracket race's slower lane's tree starts this long after AC's start, and the race is
--   over once both cars have crossed the quarter, one of them is out, or this long after the player crossed it
-- STRIPE_FROM: the rival takes the stripe from this share of the quarter on
-- TUNE_PASSES: the passes a test-and-tune has unless race.ini says otherwise; TUNE_SLIP: how long the slip shows before
--   the car is put back on the line; TUNE_STAGE: how long it is held there before the tree comes on. A pass is over
--   once the car is past the quarter and down to TUNE_DONE_KMH, has stood still for TUNE_STOPPED seconds after
--   leaving, or after TUNE_PASS_SECONDS.
local STRIP = {
  QUARTER = 1320 * FEET,
  TREE_STEP = 0.5, TREE_AMBERS = 3,
  BRACKET_LEAD = 2.0, BRACKET_WAIT = 15,
  STRIPE_FROM = 0.5,
  TUNE_PASSES = 6, TUNE_SLIP = 6, TUNE_STAGE = 3, TUNE_DONE_KMH = 20, TUNE_STOPPED = 3, TUNE_PASS_SECONDS = 60
}

-- A crash: a collision with a change of velocity of at least this, in g over one frame
local CRASH_G = 15.0

-- How long after CSP reports a collision the car still counts as in one: AC's collisions last a few frames and
-- the callback can land on the frame after the one that carried the change of velocity
local COLLISION_WINDOW_SECONDS = 0.25

-- After a teleport the jump in velocity is not a crash
local TELEPORT_GRACE_SECONDS = 1.0

-- A car that moves further in one frame than its speed allows, by this much, was teleported. AC does not always
-- say so: the drag race's reset after a disqualification moves both cars back to the line without onCarJumped,
-- and the jump read as two crashes (1440 g and 17.5 g, 2026-09-24).
local TELEPORT_SLACK_METRES = 2.0

-- A false start: the player's car this far from where it was put on the grid before the green
local FALSE_START_METRES = 1.0

-- A car AC puts back before it has gone this far jumped the start (a drag race's own jump-start rule does this);
-- one put back after is out of the race (a lane violation, the pits)
local FALSE_START_TELEPORT_METRES = 20.0

-- How long the result shows before AC quits
local RESULT_SECONDS = 5.0

-- The longest step one tick may account for: a hitch or a pause must not turn into distance or race time
local MAX_TICK_SECONDS = 1.0

-- The police chase (docs/roadmap.md, step 6). Two cops, one for each racer: each sticks to its prey, races it like a
-- racer (AI level 150%, full aggression, AC's own overtaking) and tries to get past. A racer the police get past is
-- busted: the message shows, an autopilot takes the car and brakes it to a stop (the user's rules, 2026-09-24,
-- replacing a boxing-in, a PIT and a pushed rubber band that tried to make a slow Monaco catch a fast Chevelle: the
-- police Monaco has the 426 Hemi now).
-- How far behind its prey a patrol cop joins, in metres
local POLICE_JOIN_METRES = 200
-- Overtaken: the cop this far ahead of its prey along the road, for this long, after having been behind it
local OVERTAKE_METRES = 3
local OVERTAKE_SECONDS = 0.5
-- Busted anyway: a cop this close, and the driver this slow, for this long (a car stopped off the road, which the cop
-- on its line may never get past)
local BUSTED_METRES = 8
local BUSTED_KMH = 15
local BUSTED_SECONDS = 3
-- A busted player is pulled over: an autopilot brakes the car to a stop, and the race ends once it has, or after this
local PULL_OVER_SECONDS = 12
-- Got away: every cop after them this far back along the road, for this long
local ESCAPE_METRES = 600
local ESCAPE_SECONDS = 20
-- The police call it off after this long
local CHASE_GIVE_UP_SECONDS = 240
-- The cops' AI, set again every POLICE_AI_EVERY: a level of 1.5 is 150% (CSP takes 0 to 2; race.ini stops at 100),
-- and an aggression of 0.95 is AC's 100% (the launcher's value is multiplied by 0.95)
local POLICE_AI_LEVEL = 1.5
local POLICE_AI_AGGRESSION = 0.95
-- A mild rubber band: the cops' extra grip, AC's own 1.2 on their prey's bumper up to this far back of it
local POLICE_MIN_GRIP = 1.2
local POLICE_MAX_GRIP = 1.6
-- A cop this far behind for this long sets up a roadblock ahead instead, once a chase
local ROADBLOCK_BEHIND_METRES = 700
local ROADBLOCK_AFTER_SECONDS = 8
local ROADBLOCK_AHEAD_METRES = 450
-- Speed traps (race.ini [STREET_ROD] POLICE_MODE=TRAPS): each cop waits parked on the verge of a straight somewhere
-- between these shares of the lap (clear of the grid, before the line), and pulls out after the first racer to go
-- past it who has no cop after them yet. A spot is a straight (the road turns less than TRAP_MAX_TURN over
-- TRAP_STRAIGHT_METRES either way) with TRAP_MIN_ROOM metres at one side; the cop parks TRAP_EDGE_METRES in from that
-- edge, at most TRAP_MAX_OFFSET out.
local TRAP_FROM, TRAP_TO = 0.12, 0.92
local TRAP_STEP_METRES = 10
local TRAP_STRAIGHT_METRES = 60
local TRAP_MAX_TURN = math.rad(12)
local TRAP_MIN_ROOM = 4.5
local TRAP_EDGE_METRES = 1.3
local TRAP_MAX_OFFSET = 5
-- A racer this far past a trap has been seen
local TRAP_ENGAGE_METRES = 25
-- The push a cop pulls out of a trap or a roadblock with, m/s
local PULL_OUT_SPEED = 6

-- A roadblock is a car parked at an angle on one side of the road, the other lane left open: square across a
-- two-lane road a 5.6 m Monaco left no way through, and every roadblock was a bust (2026-09-24). It goes on the wider
-- side, its middle this far out from the line (and this far in from the edge), turned this far across.
local ROADBLOCK_OFFSET_METRES = 3.0
local ROADBLOCK_EDGE_METRES = 1.2
local ROADBLOCK_ANGLE = math.rad(50)
-- A racer this far past a roadblock has dodged it: the cop goes after them at full throttle
local ROADBLOCK_PASSED_METRES = 15
-- A cop going nowhere for this long, away from its target, is put back on the road behind it
local STUCK_KMH = 5
local STUCK_SECONDS = 6
local REJOIN_METRES = 250
-- A car set down on the road: this high over it, and pushed along at up to this speed (m/s). Test Drive stops a
-- car before it moves it: a car carried into a placement at speed froze AC's physics there.
local SET_DOWN_CLEARANCE = 0.25
local SET_DOWN_MAX_SPEED = 25
-- How often the cops' AI level and aggression are set again: CSP resets the aggression
local POLICE_AI_EVERY = 0.5

-- Messages
local MSG_WIN = "You won a few bucks, not bad!"
local MSG_LOSE = "You lost, sucker!"
local MSG_CRASH = "Lucky you weren't killed!\nBetter luck next time!"
local MSG_FALSE_START = "You jumped the gun!\nNo contest, and everybody saw it."
local MSG_DISQUALIFIED = "You hit him in his own lane!\nThat's a DQ, you lose."
local MSG_BROKE_DOWN = {
  ENGINE = "The engine let go!\nYou're out of the race.",
  GEARBOX = "The gearbox gave out!\nYou're out of the race.",
  SUSPENSION = "The suspension gave out!\nYou're out of the race.",
  TYRE = "A tyre blew!\nYou're out of the race.",
  OVERHEAT = "The engine boiled over and cooked itself!\nYou're out of the race.",
  OIL = "The oil pressure went, and the bottom end with it!\nYou're out of the race.",
  FUEL = "You ran out of gas!\nYou're out of the race."
}
local MSG_BUSTED = "Busted!\nThe cops have you and your car."
local MSG_ESCAPED = "You lost the cops!"
local MSG_RED_LIGHT = "Red light! You left before your green.\nNo contest, and everybody saw it."

local sim = ac.getSim()

-- Session state
local sessionId = nil
local sessionActive = false
-- Latched once the result is written: nothing may start a second session (and a second result file)
-- in the seconds before AC quits
local sessionEnded = false
local sessionStartTime = nil
local sessionEndTime = nil
local sessionDuration = 0.0
local endReason = nil
local contextId = nil
local lastSimTimeMs = nil

-- Race state
local raceEnded = false
local playerCrashed = false

-- Where the player's car stood before the green, for the false start
local gridPosition = nil

-- DRAG, ROAD or TUNE, from race.ini [STREET_ROD] RACE_TYPE
local raceType = nil

-- A bracket race, nil in any other: dialIn by car index; decided, playerWon once it is; playerCrossedAt, the race
-- clock when the player crossed the quarter; rivalReaction, rivalMargin: how the rival leaves and takes the stripe
local bracket = nil

-- Test-and-tune, nil in any other race: passes (the slips so far), maxPasses; state WAIT (for AC's start), STAGE
-- (held on the line), TREE, RUN (the pass), SLIP (the slip shows) or OVER (the session ended); since, the race clock
-- when the state began; stoppedFor, seconds standing still in a pass; teleportedAt, the session time the car was last
-- put back
local tune = nil

-- A drag race's lanes: the sideways axis of the strip, from the player's car on the line. Each car's own lane is
-- where it stood (data.laneStart).
local stripSide = nil

-- ...and the axis down the strip, for the timeslips
local stripForward = nil

-- Seconds since AC's green, on the game's clock; nil before it
local raceClock = nil

-- Overlay state
local showResultOverlay = false
local resultTitle = "Race Over"
local resultMessage = ""

-- Car data storage, keyed by car index 0..carCount-1 (always walked in that order)
local carData = {}
local carCount = 0

-- Seconds each car still counts as in a collision
local touching = {}

-- The police, in the order race.ini lists them, and by car index. A cop's state: WAITING (hidden on the grid),
-- CHASING, ROADBLOCK, OUT (wrecked) or GONE (the chase is over).
local police = {}
local policeOf = {}

-- The chase, nil in a race without police:
-- spotMetres: how far into the race the patrol shows up; started, startedAt: when it did (session seconds);
-- player, rival: ESCAPED or BUSTED once decided; escapeFor: seconds the player has been far enough ahead
local chase = nil

-- The racers (never the police) in the order they crossed the line, by car index
local finishOrder = {}

-- Forward declaration: endSession writes the output, which is defined further down
local writeSessionOutput

local function createCarData(carIndex)
  local car = ac.getCar(carIndex)
  return {
    carIndex = carIndex,
    driverName = ac.getDriverName(carIndex) or "Unknown",
    carName = ac.getCarName(carIndex) or "Unknown",

    -- Performance tracking
    lapsCompleted = 0,
    prevLapCount = 0,
    bestLapTimeMs = nil,
    totalRaceTimeMs = 0.0,
    maxSpeedKmh = 0.0,
    distanceKm = 0.0,

    -- Crash tracking
    velocity = car and vec3():set(car.velocity) or vec3(),
    position = car and vec3():set(car.position) or vec3(),
    grace = 0.0,
    crashIntensities = {},
    crashed = false,
    crashTimestamp = nil,

    falseStart = false,
    disqualified = false,
    laneStart = nil,

    -- Breakdown: what the car carried in from its earlier races (race.ini), and what gave out
    startGearbox = 0,
    startSuspension = { 0, 0, 0, 0 },
    brokeDown = false,
    breakdown = nil,

    -- Timeslip (drag races): the race clock when the car got its green (AC's start but in a bracket race and on a
    -- test-and-tune pass) and when it left the line, how far down the strip it was last frame, and the time at each
    -- mark. stagedAt: where a test-and-tune pass starts, metres past laneStart (the car is not always put back exactly
    -- on it). released: a bracket race's rival is held until its green.
    greenAt = 0,
    leftAt = nil,
    stagedAt = 0,
    lastMetres = 0,
    slip = {},
    released = true,

    -- Final result
    finalPosition = nil
  }
end

-- The launcher's id for this race, from race.ini [STREET_ROD] CONTEXT_ID; nil when it is not there
local function readContextId()
  local ok, value = pcall(function()
    return ac.INIConfig.raceConfig():get('STREET_ROD', 'CONTEXT_ID', '')
  end)
  if ok and type(value) == 'string' and value ~= '' then return value end
  if not ok then ac.log('[Street Corsa] Could not read CONTEXT_ID from race.ini: ' .. tostring(value)) end
  return nil
end

-- DRAG or ROAD, from race.ini [STREET_ROD] RACE_TYPE; nil when it is not there (a launcher older than the mode)
local function readRaceType()
  local ok, value = pcall(function()
    return ac.INIConfig.raceConfig():get('STREET_ROD', 'RACE_TYPE', '')
  end)
  if ok and (value == 'DRAG' or value == 'ROAD' or value == 'TUNE') then return value end
  return nil
end

-- Numbers from race.ini [STREET_ROD] KEY=a,b,c; nil when the key is not there or is not all numbers. Asked for with
-- no default: CSP splits a value at its commas, and a string default gets only the first item (so "2,3" read as "2",
-- and a car's four body zones as one, found in the game 2026-09-24).
local function readNumbers(key, count)
  local ok, value = pcall(function()
    return ac.INIConfig.raceConfig():get('STREET_ROD', key)
  end)
  if not ok or value == nil then return nil end
  local parts = {}
  if type(value) == 'table' then
    for _, item in ipairs(value) do
      for part in tostring(item):gmatch('[^,]+') do parts[#parts + 1] = part end
    end
  else
    for part in tostring(value):gmatch('[^,]+') do parts[#parts + 1] = part end
  end
  local numbers = {}
  for _, part in ipairs(parts) do
    local n = tonumber(part)
    if not n or n ~= n or n == math.huge or n == -math.huge then return nil end
    numbers[#numbers + 1] = n
  end
  if #numbers < count then return nil end
  return numbers
end

-- Heat, oil, fuel and dirt (docs/roadmap.md, step 14). AC's water temperature is an estimate that changes nothing, and
-- stock cars have no oil to speak of, so the mode runs its own engine heat and oil supply and makes them count: the
-- water gauge shows it, a hot engine goes flat at the top (AC's restrictor, which takes power off at high rpm: on the
-- Nova at 5000 rpm, 100 took 9% and 200 took 28%), a cooking engine loses its life, and one that boils blows its head
-- gasket. Oil surges off the pickup under g held too long (long hard corners, hard braking, a hard launch); a sump
-- that runs dry under throttle wears the bottom end. The career rates each car (race.ini [STREET_ROD] CAR_n_COOLING=cooling,fan,oil):
--   cooling: what the radiator and water pump carry off, over what the engine makes (1: a factory engine at full
--            throttle, carried off at speed with a little to spare)
--   fan:     the share of that the fan keeps up at a standstill (moving air does the rest by FULL_AIR_KMH)
--   oil:     the g the sump holds its oil to (a stock pan surges first)
-- The fuel the car goes in with (CAR_n_FUEL, litres; below 0 fills the tank) and its dirt (CAR_n_DIRT, 0 to 1) are
-- the career's too. A car that runs its tank dry is out of the race. One local for all of it: the chunk is at Lua's
-- limit of 200.
local engineHeat = (function()
  local M = {}

  local THERMOSTAT = 88     -- °C the water sits at while the radiator keeps up
  local OPENS = 0.8         -- share of the radiator's work the thermostat covers before the water climbs
  local PER_EXCESS = 50     -- °C the water settles higher for every unit of heat past that
  local MAX_WATER = 160
  local RISE_S = 60         -- time constants: an engine takes a minute to heat through, half that to cool
  local FALL_S = 30
  local FULL_AIR_KMH = 60   -- moving air carries the radiator's full load from here
  local WARM = 105          -- the player hears about it
  local HOT = 110           -- power fades from here...
  local BOIL = 135          -- ...to MAX_FADE here, where the water boils and the head gasket goes
  local MAX_FADE = 250
  local COOKING = 118       -- engine life goes from here, LIFE_PER_C each second for every degree past it
  local LIFE_PER_C = 2
  local SURGE_HOLD_S = 3    -- a sump holds its oil this long past its g before the pickup sucks air (the cars pull
                            -- 1 to 1.4 g in corners: a stock pan starves only in a long sweeper at the limit)
  local STARVE_S = 0.5      -- and runs dry this fast
  local REFILL_S = 0.3
  local OIL_LIFE = 10       -- life a dry sump costs each second at the limiter under throttle (off it, the bearings
                            -- carry little)
  local DRY_TANK = 0.02     -- litres: the engine dies

  local function clamp01(x) return math.max(0, math.min(1, x)) end
  local function rounded(x, places) local k = 10 ^ places; return math.floor(x * k + 0.5) / k end

  -- A car's heat and oil, with what the career rated it (a factory car's figures when race.ini has none)
  local function state(data)
    if not data.heat then
      data.heat = { cooling = 1, fan = 0.35, oilG = 1.05, water = THERMOSTAT, supply = 1, surgeFor = 0, peak = THERMOSTAT,
        hotFor = 0, cookedLife = 0, starvedFor = 0, oilLife = 0, lowestSupply = 1, fade = 0, peakFade = 0, told = {}, life = nil }
    end
    return data.heat
  end

  -- At the start, from race.ini: the car's rating, fuel and dirt
  function M.start(carIndex, data)
    local h = state(data)
    local prefix = 'CAR_' .. carIndex .. '_'
    local rating = readNumbers(prefix .. 'COOLING', 3)
    if rating then
      h.cooling = math.max(0.02, rating[1])
      h.fan = clamp01(rating[2])
      h.oilG = math.max(0.1, rating[3])
    end
    local dirt = readNumbers(prefix .. 'DIRT', 1)
    if dirt then pcall(ac.setBodyDirt, carIndex, clamp01(dirt[1])) end

    local fuel = readNumbers(prefix .. 'FUEL', 1)
    if physics.allowed() then
      pcall(physics.setWaterTemperature, carIndex, h.water)
      local car = ac.getCar(carIndex)
      if fuel and car then
        local litres = fuel[1] < 0 and car.maxFuel or math.min(fuel[1], car.maxFuel)
        pcall(physics.setCarFuel, carIndex, math.max(0, litres))
      end
    end
    ac.log(string.format('[Street Corsa] Car %d: cooling %.2f, fan %.2f, sump %.2f g, fuel %s, dirt %s', carIndex, h.cooling,
      h.fan, h.oilG, fuel and string.format('%.1f', fuel[1]) or 'as AC has it', dirt and string.format('%.2f', dirt[1]) or 'as AC has it'))
  end

  local function tell(carIndex, h, key, title, text)
    if carIndex ~= 0 or h.told[key] then return end
    h.told[key] = true
    pcall(ac.setMessage, title, text)
  end

  -- Takes life off the engine, and remembers what took the last of it. AC reads the life back a frame late, so the
  -- mode goes on from what it set last, or from AC's own when that is less (an over-rev)
  local function wear(carIndex, car, h, amount, cause)
    if amount <= 0 or not physics.allowed() then return end
    local life = math.min(car.engineLifeLeft, h.life or math.huge) - amount
    if life <= 0 then h.cause = h.cause or cause end
    h.life = math.max(0, life)
    pcall(physics.setCarEngineLife, carIndex, h.life)
  end

  -- One car, one frame, from the green to the line
  local function step(carIndex, car, h, dt)
    local limiter = car.rpmLimiter and car.rpmLimiter > 0 and car.rpmLimiter or 6500
    local revs = clamp01(car.rpm / limiter)
    local load = clamp01(car.gas) * revs

    -- The water heads for where the radiator can hold it, as fast as the engine's mass lets it
    local air = h.fan + (1 - h.fan) * math.min(1, car.speedKmh / FULL_AIR_KMH)
    local excess = load / math.max(0.02, h.cooling * air)
    local ambient = sim.ambientTemperature or 25
    local target = math.min(MAX_WATER, math.max(THERMOSTAT, THERMOSTAT + (excess - OPENS) * PER_EXCESS + (ambient - 25) * 0.4))
    h.water = h.water + (target - h.water) * (1 - math.exp(-dt / (target > h.water and RISE_S or FALL_S)))
    h.peak = math.max(h.peak, h.water)

    -- The oil: the pickup keeps it while the car pulls less g than the sump holds, or for a moment more; boiling water
    -- thins it
    local g = math.max(math.abs(car.acceleration.x), math.abs(car.acceleration.z))
    if g > h.oilG then h.surgeFor = h.surgeFor + dt else h.surgeFor = math.max(0, h.surgeFor - dt * 2) end
    local thin = 1 - clamp01((h.water - 120) / 40) * 0.5
    if h.surgeFor > SURGE_HOLD_S then
      h.supply = math.max(0, h.supply - dt / STARVE_S)
    else
      h.supply = h.supply + dt / REFILL_S
    end
    h.supply = math.min(h.supply, thin)
    h.lowestSupply = math.min(h.lowestSupply, h.supply)

    if physics.allowed() then
      pcall(physics.setWaterTemperature, carIndex, h.water)
      local fade = clamp01((h.water - HOT) / (BOIL - HOT)) * MAX_FADE
      if math.abs(fade - h.fade) >= 2 or (fade == 0 and h.fade ~= 0) then
        h.fade = fade
        h.peakFade = math.max(h.peakFade, fade)
        pcall(physics.setCarRestrictor, carIndex, fade)
      end
    end

    if h.water >= HOT then h.hotFor = h.hotFor + dt end
    if h.water >= BOIL then
      local left = math.min(car.engineLifeLeft, h.life or math.huge)
      if left > 0 then
        h.cookedLife = h.cookedLife + left
        wear(carIndex, car, h, left + 1, 'OVERHEAT')
        ac.log(string.format('[Street Corsa] Car %d boiled over at %.0f C', carIndex, h.water))
      end
    elseif h.water > COOKING then
      local amount = (h.water - COOKING) * LIFE_PER_C * dt
      h.cookedLife = h.cookedLife + amount
      wear(carIndex, car, h, amount, 'OVERHEAT')
      tell(carIndex, h, 'cooking', 'Overheating!', 'Steam from under the hood. Back off or it lets go.')
    elseif h.water > WARM then
      tell(carIndex, h, 'warm', 'Running hot', 'The temperature gauge is climbing. Ease off!')
    end

    if h.supply < 0.5 and revs > 0.4 and car.gas > 0.2 then
      h.starvedFor = h.starvedFor + dt
      local amount = (1 - h.supply) * revs * OIL_LIFE * dt
      h.oilLife = h.oilLife + amount
      wear(carIndex, car, h, amount, 'OIL')
      tell(carIndex, h, 'oil', 'Oil pressure!', 'The oil light flickers: the sump is surging.')
    end
  end

  -- Every frame: the racers still running (running(data) says who), from the green; the police are left alone
  function M.update(dt, running)
    if not sim.isSessionStarted or dt <= 0 then return end
    for carIndex = 0, carCount - 1 do
      local data = carData[carIndex]
      local car = ac.getCar(carIndex)
      if data and car and not policeOf[carIndex] and running(data) then
        local ok, err = pcall(step, carIndex, car, state(data), dt)
        if not ok and not data.heatFault then
          data.heatFault = true
          ac.log('[Street Corsa] The heat model failed for car ' .. carIndex .. ': ' .. tostring(err))
        end
      end
    end
  end

  -- What took the last of the engine's life, when its heat or its oil did: 'OVERHEAT' or 'OIL'
  function M.cause(data) return data.heat and data.heat.cause end

  -- A car whose tank has run dry
  function M.outOfFuel(car) return car.fuel <= DRY_TANK end

  -- What the result says of it: the tank, the dirt, and how its heat and oil went
  function M.report(carIndex, data, condition)
    local car = ac.getCar(carIndex)
    if car then
      pcall(function() condition.max_fuel_litres = rounded(car.maxFuel, 2) end)
      pcall(function() condition.dirt = rounded(clamp01(car.dirt), 3) end)
    end
    local h = data and data.heat
    if not h then return end
    condition.heat = {
      peak_water_c = rounded(h.peak, 1),
      overheated_s = rounded(h.hotFor, 1),
      peak_fade = rounded(h.peakFade, 0),
      heat_life_lost = rounded(h.cookedLife, 1),
      oil_starved_s = rounded(h.starvedFor, 1),
      oil_life_lost = rounded(h.oilLife, 1),
      lowest_oil_supply = rounded(h.lowestSupply, 2)
    }
  end

  return M
end)()

-- The shape a car goes into the race in, from the career: body and engine into AC, the gearbox and the corners
-- remembered for the breakdowns (AC starts those new; the car's data already carries what they do)
local function applyStartState(carIndex, data)
  local prefix = 'CAR_' .. carIndex .. '_'
  local heatOk, heatErr = pcall(engineHeat.start, carIndex, data)
  if not heatOk then ac.log('[Street Corsa] Could not rate car ' .. carIndex .. ' for heat, fuel and dirt: ' .. tostring(heatErr)) end
  local gearbox = readNumbers(prefix .. 'GEARBOX', 1)
  if gearbox then data.startGearbox = math.max(0, gearbox[1]) end
  local suspension = readNumbers(prefix .. 'SUSPENSION', 4)
  if suspension then
    for w = 1, 4 do data.startSuspension[w] = math.max(0, suspension[w]) end
  end

  if not physics.allowed() then return end
  local body = readNumbers(prefix .. 'BODY', 4)
  if body then
    pcall(physics.setCarBodyDamage, carIndex, vec4(body[1], body[2], body[3], body[4]))
  end
  local life = readNumbers(prefix .. 'ENGINE_LIFE', 1)
  if life then
    pcall(physics.setCarEngineLife, carIndex, math.max(1, life[1]))
  end

  local car = ac.getCar(carIndex)
  if car and (body or life) then
    ac.log(string.format('[Street Corsa] Car %d starts with body %.0f/%.0f/%.0f/%.0f km/h, engine life %.0f, gearbox %.2f, corners %.2f/%.2f/%.2f/%.2f',
      carIndex, car.damage[0], car.damage[1], car.damage[2], car.damage[3], car.engineLifeLeft, data.startGearbox,
      data.startSuspension[1], data.startSuspension[2], data.startSuspension[3], data.startSuspension[4]))
  end
end

-- Forward declaration: parks the cops in their speed traps before the green, defined with the chase further down
local setUpTraps

-- The police from race.ini [STREET_ROD]: POLICE lists their car indices (after the two racers), POLICE_SPOT how
-- far round the lap, as a share of it, the patrol shows up. They wait out of sight: hidden, and nothing hits them.
local function readPolice()
  local indices = readNumbers('POLICE', 1)
  if not indices then return end
  for _, value in ipairs(indices) do
    local index = math.floor(value)
    if index >= 2 and index < carCount and not policeOf[index] then
      local cop = { index = index, number = #police + 1, state = 'WAITING', target = 0, behindFor = 0,
        stuckFor = 0, roadblocked = false, steering = false, aiTimer = 0 }
      police[#police + 1] = cop
      policeOf[index] = cop
      pcall(ac.setCarActive, index, false)
      pcall(physics.disableCarCollisions, index, true)
    end
  end
  if #police == 0 then return end

  local spot = readNumbers('POLICE_SPOT', 1)
  local share = math.min(0.9, math.max(0.1, spot and spot[1] or 0.4))
  local ok, mode = pcall(function() return ac.INIConfig.raceConfig():get('STREET_ROD', 'POLICE_MODE', '') end)
  -- playerChased, rivalChased: a cop has been after them; only then is their chase judged. playerSince: from when.
  chase = { spotMetres = share * sim.trackLengthM, traps = ok and mode == 'TRAPS', started = false, startedAt = nil,
    player = nil, rival = nil, playerChased = false, rivalChased = false, playerSince = nil, escapeFor = 0, caughtFor = {} }
  if chase.traps then
    ac.log(string.format('[Street Corsa] %d cop(s) waiting in speed traps', #police))
  else
    ac.log(string.format('[Street Corsa] %d cop(s) waiting, the patrol shows up %.0f m into the race', #police, chase.spotMetres))
  end
end

-- A bracket race, from race.ini [STREET_ROD] DIAL_IN=player,rival: each lane's green on the race clock, the slower
-- dial-in's tree first by the difference, and the rival held until its own. BRACKET_RIVAL=reaction,margin: how long
-- the rival takes to leave after its green, and how far over its dial-in it aims when it takes the stripe.
local function readBracket()
  local dials = readNumbers('DIAL_IN', 2)
  if not dials or carCount < 2 then return end
  local player, rival = dials[1], dials[2]
  if player <= 0 or rival <= 0 then return end
  local slower = math.max(player, rival)
  local lead = STRIP.BRACKET_LEAD + STRIP.TREE_AMBERS * STRIP.TREE_STEP
  local rivalWay = readNumbers('BRACKET_RIVAL', 2)
  bracket = { dialIn = { [0] = player, [1] = rival }, decided = false, playerWon = nil, playerCrossedAt = nil,
    rivalReaction = rivalWay and math.max(0, math.min(1, rivalWay[1])) or 0.15,
    rivalMargin = rivalWay and math.max(0, math.min(0.5, rivalWay[2])) or 0.05 }
  carData[0].greenAt = lead + slower - player
  carData[1].greenAt = lead + slower - rival
  carData[1].released = false
  ac.log(string.format('[Street Corsa] Bracket race: dial-ins %.2f and %.2f, greens at %.2f and %.2f s, the rival leaves %.2f s after its own',
    player, rival, carData[0].greenAt, carData[1].greenAt, bracket.rivalReaction))
end

-- Test-and-tune, from race.ini [STREET_ROD] TUNE_PASSES: how many passes the session has
local function readTune()
  local passes = readNumbers('TUNE_PASSES', 1)
  local count = passes and math.floor(passes[1]) or STRIP.TUNE_PASSES
  tune = { passes = {}, maxPasses = math.max(1, math.min(20, count)), state = 'WAIT', since = 0, stoppedFor = 0,
    teleportedAt = nil }
  ac.log(string.format('[Street Corsa] Test-and-tune: up to %d passes', tune.maxPasses))
end

-- Seed the generator from several sources: os.time() alone changes once a second. The script clock, the
-- game clock, a heap address (randomized per run) and the race's context id each add their own bits.
local function seedRandom()
  local seed = os.time()
  seed = seed * 1000003 + math.floor((os.preciseClock() % 1) * 1e6)
  seed = seed + math.floor(sim.time or 0)
  local address = tonumber(tostring({}):match('0x(%x+)') or '0', 16) or 0
  seed = seed + address % 1000000007
  for i = 1, #(contextId or '') do
    seed = (seed * 31 + contextId:byte(i)) % 2147483647
  end
  math.randomseed(seed % 2147483647)
  -- LuaJIT's first draws after seeding are poorly mixed
  for _ = 1, 8 do math.random() end
end

local function generateUUID()
  local template = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'
  return (string.gsub(template, '[xy]', function(c)
    local v = (c == 'x') and math.random(0, 15) or math.random(8, 11)
    return string.format('%x', v)
  end))
end

local function getISOTimestamp()
  return os.date("!%Y-%m-%dT%H:%M:%SZ")
end

-- Initialize session (once per AC run)
local function initializeSession()
  contextId = readContextId()
  seedRandom()

  sessionId = generateUUID()
  sessionStartTime = getISOTimestamp()
  sessionActive = true
  lastSimTimeMs = sim.time

  carCount = sim.carsCount
  for i = 0, carCount - 1 do
    carData[i] = createCarData(i)
    local ok, err = pcall(applyStartState, i, carData[i])
    if not ok then ac.log('[Street Corsa] Could not put car ' .. i .. ' into its shape: ' .. tostring(err)) end
  end

  local player = ac.getCar(0)
  if player and not sim.isSessionStarted then gridPosition = vec3():set(player.position) end

  raceType = readRaceType()
  if raceType ~= 'DRAG' then
    local ok, err = pcall(readPolice)
    if not ok then ac.log('[Street Corsa] Could not read the police: ' .. tostring(err)) end
    if chase and chase.traps and setUpTraps then
      local set, why = pcall(setUpTraps)
      if not set then
        chase.traps = false
        ac.log('[Street Corsa] Speed traps could not be set up: ' .. tostring(why))
      end
    end
  end
  if (raceType == 'DRAG' or raceType == 'TUNE') and player and not sim.isSessionStarted then
    -- Each car's lane is the line it stands on
    for i = 0, carCount - 1 do
      local car = ac.getCar(i)
      if car then carData[i].laneStart = vec3():set(car.position) end
    end
    local look = vec3(player.look.x, 0, player.look.z):normalize()
    stripSide = vec3(-look.z, 0, look.x)
    stripForward = look
  end
  if raceType == 'DRAG' and stripForward then
    local ok, err = pcall(readBracket)
    if not ok then ac.log('[Street Corsa] Could not read the bracket: ' .. tostring(err)) end
  elseif raceType == 'TUNE' and stripForward then
    local ok, err = pcall(readTune)
    if not ok then ac.log('[Street Corsa] Could not read the test-and-tune: ' .. tostring(err)) end
  end

  ac.log(string.format('[Street Corsa] Session started: %s with %d cars, context %s, physics allowed %s, session type %s, race %s',
    sessionId, carCount, tostring(contextId), tostring(physics.allowed()), tostring(sim.raceSessionType), tostring(raceType)))
end

local function scheduleQuit(delay, why)
  setTimeout(function()
    ac.log('[Street Corsa] Quitting AC ' .. why .. '...')
    ac.shutdownAssettoCorsa()
  end, delay)
end

-- Stops the player's car and keeps it stopped until AC quits. All three, as Test Drive found: the input lock
-- alone lets the car coast, and the brakes alone let the player drive against them.
local function holdPlayer(seconds)
  pcall(physics.setCarNoInput, true)
  pcall(physics.lockUserControlsFor, seconds)
  pcall(physics.forceUserBrakesFor, seconds, 1.0)
end

-- Stops an AI car where it is. Held every frame from updateCrashes: the AI's own driving rewrites the throttle.
local function holdAI(carIndex)
  pcall(physics.setAIThrottleLimit, carIndex, 0)
  pcall(physics.setAIStopCounter, carIndex, 1)
end

-- The patrol has shown up and the player's chase is not decided yet
local function chaseOn()
  return chase ~= nil and chase.started and chase.playerChased and chase.player == nil
end

-- A cop has been after the rival and the rival's chase is not decided yet
local function rivalHunted()
  return chase ~= nil and chase.started and chase.rivalChased and chase.rival == nil
end

-- The racer has a cop after them from now: their chase counts
local function markChased(racer)
  if racer == 0 and not chase.playerChased then
    chase.playerChased = true
    chase.playerSince = sessionDuration
  elseif racer == 1 and not chase.rivalChased then
    chase.rivalChased = true
    chase.rivalSince = sessionDuration
  end
end

-- A racer's chase decided, 'player' or 'rival': ESCAPED or BUSTED, once
local function decideChase(who, outcome)
  if not chase or not chase.started or chase[who] then return end
  chase[who] = outcome
  ac.log(string.format('[Street Corsa] Chase: %s %s after %.0f s', who, outcome, sessionDuration - (chase.startedAt or sessionDuration)))
end

-- Forward declaration: puts the police away, lights and sirens too; defined with the chase further down
local shutDownPolice

local function round(value, places)
  if type(value) ~= 'number' then return nil end
  local k = 10 ^ (places or 0)
  return math.floor(value * k + 0.5) / k
end

-- A car's quarter-mile crossing on the race clock and its elapsed time, or nil before it crossed
local function quarterOf(data)
  local et = data.slip.quarter_mile_s
  if not et or not data.leftAt then return nil end
  return data.leftAt + et, et
end

-- The same as the slip has it (timeslipOf), to the millisecond: green, plus reaction, plus elapsed time. A bracket
-- race is decided on these, the numbers the career decides it on again (BracketRules.Decide, which adds them up in
-- the same order), so the two never disagree over a car a fraction of a millisecond either side of its dial-in.
local function slipQuarterOf(data)
  local _, et = quarterOf(data)
  if not et then return nil end
  local green = data.greenAt ~= 0 and round(data.greenAt, 3) or 0
  local elapsed = round(et, 3)
  return green + round(data.leftAt - data.greenAt, 3) + elapsed, elapsed
end

-- Past the line, where nothing counts any more: AC's line, the quarter in a bracket race, never on a test-and-tune
local function pastTheLine(data)
  if tune then return false end
  if bracket then return quarterOf(data) ~= nil end
  return data.lapsCompleted >= 1
end

-- The best pass of a test-and-tune so far (the quickest quarter), or nil
local function bestPass()
  local best = nil
  for _, pass in ipairs(tune and tune.passes or {}) do
    if pass.quarter_mile_s and (not best or pass.quarter_mile_s < best.quarter_mile_s) then best = pass end
  end
  return best
end

-- What the player hears about the bracket race's result: their ET against their dial-in
local function bracketLine()
  local data = carData[0]
  local _, et = slipQuarterOf(data)
  if not bracket or not et then return '' end
  local dial = bracket.dialIn[0]
  return string.format('\nYour %.3f on a %.2f dial-in%s', et, dial, et < dial and ': BREAKOUT' or '')
end

-- The timeslip as the result has it, defined with the output further down
local timeslipOf

-- A test-and-tune pass that is over, added to the passes; false when the car never left the line
local function recordPass(data)
  local slip = timeslipOf(data)
  if not slip then return false end
  slip.pass = #tune.passes + 1
  tune.passes[#tune.passes + 1] = slip
  tune.last = slip
  ac.log(string.format('[Street Corsa] Pass %d: R/T %s, 1/4 %s', slip.pass, tostring(slip.reaction_s), tostring(slip.quarter_mile_s)))
  return true
end

-- End session: write results, show the result, and quit
-- reason: FINISHED (WIN or LOSE), CRASH, FALSE_START, DISQUALIFIED, BROKE_DOWN, BUSTED or ABANDONED
local function endSession(reason, won)
  if not sessionActive then return end
  sessionActive = false
  sessionEnded = true
  raceEnded = true
  endReason = reason

  ac.log('[Street Corsa] Ending session: ' .. reason .. (reason == 'FINISHED' and (won and ' (WIN)' or ' (LOSE)') or ''))

  -- A chase the race ended in the middle of. A player who stops for good with the cops behind them is theirs:
  -- wrecked, broken down, or back in the pits. The police stop there, so a rival not caught by then got away.
  if chase and chase.started then
    local stopped = reason == 'CRASH' or reason == 'BROKE_DOWN' or reason == 'ABANDONED' or reason == 'BUSTED'
    if chase.playerChased then decideChase('player', stopped and 'BUSTED' or 'ESCAPED') end
    if chase.rivalChased then decideChase('rival', 'ESCAPED') end
    chase.duration = sessionDuration - chase.startedAt
  end
  if shutDownPolice then pcall(shutDownPolice) end

  -- A test-and-tune pass under way is one of the passes: the player may leave the strip still rolling past the quarter
  if tune then
    if (tune.state == 'TREE' or tune.state == 'RUN') and carData[0] then recordPass(carData[0]) end
    tune.state = 'OVER'
  end

  -- Write session results. Whatever goes wrong there, AC must still quit: the launcher waits for it.
  sessionEndTime = getISOTimestamp()
  local ok, err = pcall(writeSessionOutput)
  if not ok then
    ac.log('[Street Corsa] ERROR: Writing the session data failed: ' .. tostring(err))
  end

  if reason == 'ABANDONED' then
    -- Put back in the pits or off the strip: nothing to show
    ac.shutdownAssettoCorsa()
    return
  end

  local busted = chase and chase.player == 'BUSTED'
  if reason == 'BUSTED' then
    resultTitle, resultMessage = "Busted", MSG_BUSTED
    holdPlayer(30)
  elseif reason == 'CRASH' then
    resultTitle, resultMessage = busted and "Busted" or "Race Over", busted and MSG_CRASH .. "\n" .. MSG_BUSTED or MSG_CRASH
    holdPlayer(30)
  elseif reason == 'FALSE_START' then
    local red = carData[0] and carData[0].redLight
    resultTitle, resultMessage = red and "Red Light" or "False Start", red and MSG_RED_LIGHT or MSG_FALSE_START
    holdPlayer(30)
  elseif reason == 'DISQUALIFIED' then
    resultTitle, resultMessage = "Disqualified", MSG_DISQUALIFIED
    holdPlayer(30)
  elseif reason == 'BROKE_DOWN' then
    resultTitle, resultMessage = "Broke Down", MSG_BROKE_DOWN[carData[0] and carData[0].breakdown or 'ENGINE'] or MSG_BROKE_DOWN.ENGINE
    holdPlayer(30)
  elseif tune then
    local best = bestPass()
    resultTitle = "Strip Closed"
    resultMessage = string.format('%d pass%s on the strip.', #tune.passes, #tune.passes == 1 and '' or 'es')
    if best then
      resultMessage = resultMessage .. string.format('\nBest: %.3f @ %.2f mph', best.quarter_mile_s, best.quarter_mile_mph or 0)
    end
    holdPlayer(30)
  else
    resultTitle, resultMessage = "Race Over", (won and MSG_WIN or MSG_LOSE) .. bracketLine()
    if busted then
      resultTitle, resultMessage = "Busted", resultMessage .. "\n" .. MSG_BUSTED
      holdPlayer(30)
    elseif chase and chase.player == 'ESCAPED' then
      resultMessage = resultMessage .. "\n" .. MSG_ESCAPED
    end
  end

  showResultOverlay = true
  -- A drag race quits earlier: AC puts the cars back after the run, see onCarJumped
  scheduleQuit(RESULT_SECONDS, 'after the result')
end

-- A crash for a car: counted, and the car stopped. The player's crash ends the race.
local function crash(carIndex, data, g)
  data.crashed = true
  table.insert(data.crashIntensities, g)
  data.crashTimestamp = getISOTimestamp()

  if carIndex == 0 then
    playerCrashed = true
    ac.log(string.format('[Street Corsa] PLAYER CRASHED! Intensity: %.1fG', g))
    endSession('CRASH')
  else
    ac.log(string.format('[Street Corsa] Car %d crashed - Intensity: %.1fG', carIndex, g))
    holdAI(carIndex)
    -- A rival wrecked with the cops out is theirs
    if rivalHunted() then decideChase('rival', 'BUSTED') end
  end
end

local _dv = vec3()

-- Every frame: the change of velocity of each car, a crash when it comes with a collision
local function updateCrashes(dt)
  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and car then
      if touching[carIndex] then
        touching[carIndex] = touching[carIndex] - dt
        if touching[carIndex] <= 0 then touching[carIndex] = nil end
      end

      -- Put somewhere else without a word from AC: not a crash, and a moment's grace to land
      local reach = math.max(data.velocity:length(), car.velocity:length()) * math.max(dt, 1 / 240)
      local moved = car.position:distance(data.position)
      data.position:set(car.position)
      if moved > reach + TELEPORT_SLACK_METRES then
        ac.log(string.format('[Street Corsa] Car %d moved %.0f m in one frame: teleported, not crashed', carIndex, moved))
        data.grace = TELEPORT_GRACE_SECONDS
      end

      _dv:set(car.velocity):sub(data.velocity)
      local g = _dv:length() / math.max(dt, 1 / 240) / 9.81
      data.velocity:set(car.velocity)

      local cop = policeOf[carIndex]
      if cop then
        -- A cop that wrecks itself is out of the chase; it never races, so nothing else applies
        if data.grace > 0 then
          data.grace = data.grace - dt
        elseif cop.state == 'CHASING' and touching[carIndex] and g >= CRASH_G then
          cop.state = 'OUT'
          ac.log(string.format('[Street Corsa] Cop %d wrecked (%.1fG): out of the chase', carIndex, g))
        end
      elseif data.crashed or data.brokeDown or data.busted then
        -- A crashed, broken-down or busted rival stays where it is (not retired to the pits by AC)
        if carIndex ~= 0 then
          holdAI(carIndex)
          pcall(physics.preventAIFromRetiring, carIndex)
        end
      elseif data.disqualified and carIndex ~= 0 and data.grace <= 0 and touching[carIndex] and g >= CRASH_G then
        -- A disqualified rival that crashed doing it: both on its record
        crash(carIndex, data, g)
      elseif data.disqualified and carIndex ~= 0 then
        -- A disqualified rival stays where it is
        holdAI(carIndex)
      elseif data.grace > 0 then
        data.grace = data.grace - dt
      elseif pastTheLine(data) then
        -- Past the line nothing counts: a rival that finishes first and wrecks itself at the end of the strip
        -- (205 km/h, 2026-09-24) has still won, and the player still gets to finish. The police have no hold on a
        -- racer past the line either.
      elseif touching[carIndex] and g >= CRASH_G and sim.isSessionStarted then
        crash(carIndex, data, g)
        if sessionEnded then return end
      end
    end
  end
end

-- What gave out on a car, or nil: the engine's life run out (OVERHEAT or OIL when its heat or its oil took the last of
-- it), the gearbox or a corner past what it could take with what it carried in, a blown tyre, an empty tank
local function findBreakdown(car, data)
  if car.engineLifeLeft <= 0 then return engineHeat.cause(data) or 'ENGINE' end
  if engineHeat.outOfFuel(car) then return 'FUEL' end
  if data.startGearbox + math.max(0, car.gearboxDamage) >= 1 then return 'GEARBOX' end
  for w = 0, 3 do
    local wheel = car.wheels[w]
    if wheel.isBlown then return 'TYRE' end
    if data.startSuspension[w + 1] + math.max(0, wheel.suspensionDamage) / MAX_SUSPENSION_BEND >= 1 then return 'SUSPENSION' end
  end
  return nil
end

-- Every frame between the green and the line: a car that breaks down is out. The player's breakdown ends the race;
-- a rival's is held where it stopped, and the player finishes to win. Both broken is a draw, which the career calls.
local function updateBreakdowns()
  if not sim.isSessionStarted then return end
  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and car and not policeOf[carIndex] and not data.crashed and not data.disqualified and not data.brokeDown and not pastTheLine(data) then
      local ok, what = pcall(findBreakdown, car, data)
      if ok and what then
        data.brokeDown = true
        data.breakdown = what
        ac.log(string.format('[Street Corsa] Car %d BROKE DOWN: %s', carIndex, what))
        if carIndex == 0 then
          endSession('BROKE_DOWN')
          return
        end
        holdAI(carIndex)
        pcall(ac.setMessage, 'Broke down', data.driverName .. "'s car gave out. Finish the race and it is yours!")
        if rivalHunted() then decideChase('rival', 'BUSTED') end
      end
    end
  end
end

-- A drag race's timeslips: each car's time at every mark down the strip, from the moment it left the line. The
-- moment a mark is passed is found between two frames, from where the car was on each side of it. The race clock
-- (raceClock) runs from AC's start; previousClock is where it stood a tick ago. A car that leaves before its own green
-- (a bracket race, a test-and-tune pass) has red-lit.
local function updateTimeslips(previousClock, tick)
  if (raceType ~= 'DRAG' and raceType ~= 'TUNE') or not stripForward or not sim.isSessionStarted then return end
  if tick <= 0 then return end

  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and car and data.laneStart then
      local metres = (car.position - data.laneStart):dot(stripForward) - data.stagedAt
      local timing = not tune or tune.state == 'TREE' or tune.state == 'RUN'
      local function crossed(mark)
        if data.lastMetres >= mark or metres < mark then return nil end
        local share = (mark - data.lastMetres) / math.max(metres - data.lastMetres, 1e-6)
        return previousClock + share * tick
      end

      if timing and not data.leftAt then
        data.leftAt = crossed(STAGE_METRES)
        if data.leftAt and data.leftAt < data.greenAt then
          data.redLight = true
          ac.log(string.format('[Street Corsa] Car %d RED LIGHT: left %.3f s before its green', carIndex, data.greenAt - data.leftAt))
        end
      end
      if timing and data.leftAt then
        for _, mark in ipairs(MARKS) do
          if data.slip[mark.key] == nil then
            local at = crossed(mark.metres)
            if at then
              data.slip[mark.key] = at - data.leftAt
              if mark.trap and data.slip[mark.trap] then
                local seconds = data.slip[mark.key] - data.slip[mark.trap]
                -- m/s to mph
                if seconds > 0 then data.slip[mark.speed] = TRAP_METRES / seconds * 2.2369363 end
              end
            end
          end
        end
      end

      data.lastMetres = metres
    end
  end
end

-- Whether the player wins a bracket race, from each car's quarter (at: crossed on the race clock, et) and dial-in;
-- the rival's quarter is nil when it never got there. A breakout loses, unless the other car broke out by more; with
-- neither, the first car to the quarter wins. The career decides the same way from the slips (BracketRules).
local function bracketPlayerWins(playerAt, playerEt, rivalAt, rivalEt)
  local playerOver = playerEt < bracket.dialIn[0]
  if not rivalAt then return not playerOver end
  local rivalOver = rivalEt < bracket.dialIn[1]
  if playerOver and rivalOver then return bracket.dialIn[0] - playerEt < bracket.dialIn[1] - rivalEt end
  if playerOver ~= rivalOver then return rivalOver end
  return playerAt < rivalAt
end

-- Every frame of a bracket race: the rival held until its green and let go a moment after it; near the end it lifts
-- when its pace would beat its dial-in; a player who left before their green has red-lit; the race is over once
-- both cars have crossed the quarter, the rival is out, or a while after the player crossed it.
local function updateBracket()
  if not bracket then return end
  local player, rival = carData[0], carData[1]
  if not player or not rival then return end
  if not sim.isSessionStarted or not raceClock then
    if not rival.released then holdAI(1) end
    return
  end

  if player.redLight then
    player.falseStart = true
    endSession('FALSE_START')
    return
  end

  -- AC's own lights go green for both lanes at its start: the player's green is their own tree's (CSP has no way to
  -- hide AC's lights)
  if not bracket.told then
    bracket.told = true
    pcall(ac.setMessage, 'Bracket race', 'Not yet! Wait for your own tree, on the right.')
  end

  if not rival.released then
    if raceClock >= rival.greenAt + bracket.rivalReaction and not rival.crashed and not rival.brokeDown then
      rival.released = true
      pcall(physics.setAIThrottleLimit, 1, 1)
      pcall(physics.setAIStopCounter, 1, 0)
      pcall(physics.awakeCar, 1)
      pcall(physics.engageGear, 1, 1)
      ac.log(string.format('[Street Corsa] Green for car 1 at %.2f s, away at %.2f s', rival.greenAt, raceClock))
    else
      holdAI(1)
    end
  elseif rival.leftAt and not quarterOf(rival) and not rival.crashed and not rival.brokeDown and not rival.disqualified then
    -- Taking the stripe: from part way down, a car going faster than the pace that gets it to the quarter on its
    -- dial-in (and the rival's margin) is held to that pace, AC's AI braking down to it. Only ever slower: a car that
    -- can't make its dial-in goes flat out.
    local car = ac.getCar(1)
    local speed = car and car.speedKmh / 3.6 or 0
    local top = 1e9
    if car and rival.lastMetres > STRIP.STRIPE_FROM * STRIP.QUARTER then
      local timeLeft = bracket.dialIn[1] + bracket.rivalMargin - (raceClock - rival.leftAt)
      local pace = timeLeft > 0 and (STRIP.QUARTER - rival.lastMetres) / timeLeft or math.huge
      if pace < speed then top = math.max(30, pace * 3.6) end
    end
    pcall(physics.setAITopSpeed, 1, top)
  end

  local playerAt, playerEt = slipQuarterOf(player)
  if not playerAt then return end
  bracket.playerCrossedAt = bracket.playerCrossedAt or raceClock
  local rivalAt, rivalEt = slipQuarterOf(rival)
  local rivalOut = rival.crashed or rival.brokeDown or rival.disqualified
  if not rivalAt and not rivalOut and raceClock - bracket.playerCrossedAt < STRIP.BRACKET_WAIT then return end

  bracket.decided = true
  bracket.playerWon = rivalOut or bracketPlayerWins(playerAt, playerEt, rivalAt, rivalEt)
  ac.log(string.format('[Street Corsa] Bracket race over: player %.3f on %.2f, rival %s on %.2f: %s', playerEt,
    bracket.dialIn[0], rivalEt and string.format('%.3f', rivalEt) or 'no time', bracket.dialIn[1],
    bracket.playerWon and 'WON' or 'LOST'))
  endSession('FINISHED', bracket.playerWon)
end

-- Puts the player's car back on its line, stopped, facing down the strip
local function backToTheLine()
  local data = carData[0]
  -- Marked first: AC may call onCarJumped from inside setCarPosition, and this jump is the mode's own
  tune.teleportedAt = sessionDuration
  pcall(physics.setCarVelocity, 0, vec3())
  -- setCarPosition takes the opposite of the car's look (Test Drive's parkPlayer)
  local ok, err = pcall(physics.setCarPosition, 0, data.laneStart, -stripForward)
  if not ok then ac.log('[Street Corsa] Could not put the car back on the line: ' .. tostring(err)) end
  data.grace = TELEPORT_GRACE_SECONDS
end

-- Every frame of a test-and-tune: the pass's states one after the other, and the result written after each pass
local function updateTune(tick)
  if not tune then return end
  local data = carData[0]
  local car = ac.getCar(0)
  if not data or not car then return end

  if not sim.isSessionStarted then
    -- Held until AC's start: the first tree is the mode's own
    pcall(physics.forceUserBrakesFor, 0.25, 1.0)
    return
  end
  local now = raceClock or 0
  local state = tune.state

  if state == 'WAIT' or state == 'STAGE' then
    pcall(physics.forceUserBrakesFor, 0.25, 1.0)
    if state == 'WAIT' then
      tune.state, tune.since = 'STAGE', now
      pcall(ac.setMessage, 'Test and tune', string.format('Pass %d of %d: stage up', #tune.passes + 1, tune.maxPasses))
    elseif now - tune.since >= STRIP.TUNE_STAGE then
      -- The brakes come off at the first amber: from here the player holds the car
      -- The pass is measured from where the car stands: put back short of or past its line, it still stages
      data.leftAt, data.slip, data.redLight = nil, {}, false
      data.stagedAt = (car.position - data.laneStart):dot(stripForward)
      data.lastMetres = 0
      data.greenAt = now + STRIP.TREE_AMBERS * STRIP.TREE_STEP
      tune.state, tune.since, tune.stoppedFor = 'TREE', now, 0
      pcall(physics.forceUserBrakesFor, 0, 0)
      ac.log(string.format('[Street Corsa] Pass %d: tree on, green at %.2f s', #tune.passes + 1, data.greenAt))
    end
    return
  end

  if state == 'TREE' or state == 'RUN' then
    if state == 'TREE' and now >= data.greenAt then tune.state = 'RUN' end
    if data.leftAt and car.speedKmh < 3 then tune.stoppedFor = tune.stoppedFor + tick else tune.stoppedFor = 0 end
    local done = (quarterOf(data) and car.speedKmh < STRIP.TUNE_DONE_KMH)
      or (data.leftAt and tune.stoppedFor >= STRIP.TUNE_STOPPED)
      or now - data.greenAt >= STRIP.TUNE_PASS_SECONDS
    if not done then return end

    if recordPass(data) then
      -- Written after every pass: closing AC keeps what was run
      endReason, sessionEndTime = 'FINISHED', getISOTimestamp()
      local ok, err = pcall(writeSessionOutput)
      if not ok then ac.log('[Street Corsa] ERROR: Writing the passes failed: ' .. tostring(err)) end
    else
      tune.last = nil
    end
    if #tune.passes >= tune.maxPasses then
      tune.state = 'OVER'
      endSession('FINISHED')
      return
    end
    tune.state, tune.since = 'SLIP', now
    return
  end

  if state == 'SLIP' and now - tune.since >= STRIP.TUNE_SLIP then
    backToTheLine()
    tune.state = 'WAIT'
  end
end

-- Update telemetry for all cars
local function updateAllTelemetry(deltaT)
  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and car then
      data.totalRaceTimeMs = data.totalRaceTimeMs + (deltaT * 1000.0)

      local currentLapCount = car.lapCount
      if currentLapCount > data.prevLapCount then
        if data.lapsCompleted < 1 and currentLapCount >= 1 and not policeOf[carIndex] then
          finishOrder[#finishOrder + 1] = carIndex
        end
        data.lapsCompleted = currentLapCount

        local lastLapTimeMs = car.previousLapTimeMs
        if lastLapTimeMs and lastLapTimeMs > 0 then
          if data.bestLapTimeMs == nil or lastLapTimeMs < data.bestLapTimeMs then
            data.bestLapTimeMs = lastLapTimeMs
          end
        end
      end
      data.prevLapCount = currentLapCount

      local speedKmh = car.speedKmh
      if speedKmh > data.maxSpeedKmh then
        data.maxSpeedKmh = speedKmh
      end

      data.distanceKm = data.distanceKm + ((speedKmh / 3.6) * deltaT) / 1000.0
    end
  end
end

-- Before the green: the player's car may not leave its spot
local function checkFalseStart()
  if sim.isSessionStarted or not gridPosition then return end
  local player = ac.getCar(0)
  if player and player.position:distance(gridPosition) > FALSE_START_METRES then
    carData[0].falseStart = true
    ac.log('[Street Corsa] FALSE START: the car left its spot before the green')
    endSession('FALSE_START')
  end
end

-- The race is over when the player crosses the line, whoever got there first: the player always gets to finish.
-- The session ends there too, chase or not: a cop still on the player's tail at the line has lost him.
local function checkRaceFinish()
  if raceEnded then return end

  local playerData = carData[0]
  if not playerData then return end

  if playerData.lapsCompleted >= 1 then
    local won
    if #police > 0 then
      -- The police are in AC's race too, and one put down ahead can lead it: the racers' own order decides
      won = finishOrder[1] == 0
      ac.log(string.format('[Street Corsa] Race finished - %s', won and 'WON' or 'LOST'))
    else
      local playerPosition = ac.getCarLeaderboardPosition(0)
      if playerPosition <= 0 then
        playerPosition = ac.getCar(0).racePosition
      end
      won = playerPosition == 1
      ac.log(string.format('[Street Corsa] Race finished - Position: %d', playerPosition))
    end

    -- The line is home: a cop still after the player has lost him (endSession calls it a getaway)
    endSession('FINISHED', won)
  end
end

-- What the race left of the car, as AC tracks it: the career puts it on the car's parts. Every field is read on its
-- own, so one AC does not have leaves the rest.
local function carCondition(carIndex)
  local car = ac.getCar(carIndex)
  if not car then return nil end
  local condition = {}

  pcall(function()
    condition.body_damage_kmh = { round(car.damage[0], 1), round(car.damage[1], 1), round(car.damage[2], 1), round(car.damage[3], 1) }
  end)
  pcall(function() condition.engine_life = round(car.engineLifeLeft, 1) end)
  pcall(function() condition.gearbox_damage = round(car.gearboxDamage, 3) end)
  pcall(function() condition.water_temperature_c = round(car.waterTemperature, 1) end)
  pcall(function() condition.oil_temperature_c = round(car.oilTemperature, 1) end)
  pcall(function() condition.oil_pressure = round(car.oilPressure, 2) end)
  pcall(function() condition.fuel_litres = round(car.fuel, 2) end)
  engineHeat.report(carIndex, carData[carIndex], condition)

  -- One entry per corner, in AC's order, whatever fails to read: a wheel that cannot be read keeps its place, never
  -- leaves a gap that moves the later wheels into the wrong corners. Each entry names its corner, which also keeps an
  -- unread one an object in the JSON (an empty table is written as []).
  local wheels = {}
  for w = 0, 3 do
    local entry = { wheel = w }
    wheels[w + 1] = entry
    pcall(function()
      local wheel = car.wheels[w]
      entry.tyre_wear = round(wheel.tyreWear, 4)
      entry.tyre_virtual_km = round(wheel.tyreVirtualKM, 3)
      entry.tyre_blown = wheel.isBlown
      entry.suspension_damage = round(wheel.suspensionDamage, 4)
    end)
  end
  condition.wheels = wheels
  return condition
end

-- The timeslip as the result has it; nil for a car that never left the line, and in a road race. The reaction is
-- from the car's own green; green_s says when that was on the race clock, where it was not AC's start.
timeslipOf = function(data)
  if (raceType ~= 'DRAG' and raceType ~= 'TUNE') or not data.leftAt then return nil end
  local slip = { reaction_s = round(data.leftAt - data.greenAt, 3) }
  if data.greenAt ~= 0 then slip.green_s = round(data.greenAt, 3) end
  if data.redLight then slip.red_light = true end
  for _, mark in ipairs(MARKS) do
    if not mark.hidden then slip[mark.key] = round(data.slip[mark.key], 3) end
    if mark.speed then slip[mark.speed] = round(data.slip[mark.speed], 2) end
  end
  return slip
end

-- Convert car data to output format
local function carDataToDict(data)
  local maxCrash = 0
  for _, g in ipairs(data.crashIntensities) do
    if g > maxCrash then maxCrash = g end
  end

  local intensities = {}
  for i, g in ipairs(data.crashIntensities) do intensities[i] = round(g, 2) end

  -- A bracket race: the car's dial-in, and whether it ran under it
  local dialIn, breakout = nil, nil
  if bracket and bracket.dialIn[data.carIndex] then
    dialIn = round(bracket.dialIn[data.carIndex], 3)
    local _, et = slipQuarterOf(data)
    breakout = et ~= nil and et < bracket.dialIn[data.carIndex]
  end

  -- Test-and-tune: every pass, and the best of them as the car's timeslip
  local passes = nil
  local slip = timeslipOf(data)
  if tune and data.carIndex == 0 then
    passes = tune.passes
    slip = bestPass()
  end

  return {
    driver_name = data.driverName,
    car_name = data.carName,
    car_index = data.carIndex,
    is_player = data.carIndex == 0,
    false_start = data.falseStart,
    disqualified = data.disqualified,
    broke_down = data.brokeDown,
    breakdown = data.breakdown,
    timeslip = slip,
    passes = passes,
    dial_in_s = dialIn,
    breakout = breakout,
    performance = {
      final_position = data.finalPosition,
      laps_completed = data.lapsCompleted,
      best_lap_time_ms = data.bestLapTimeMs and round(data.bestLapTimeMs, 2) or nil,
      total_race_time_ms = round(data.totalRaceTimeMs, 2),
      max_speed_kmh = round(data.maxSpeedKmh, 2),
      distance_km = round(data.distanceKm, 3)
    },
    crash = {
      crashed = data.crashed,
      crash_intensities_g = intensities,
      max_crash_intensity_g = round(maxCrash, 2),
      crash_timestamp = data.crashTimestamp
    },
    condition = carCondition(data.carIndex)
  }
end

-- Write session output to JSON file
writeSessionOutput = function()
  local trackId = ac.getTrackID()
  local trackLayout = ac.getTrackLayout()
  if trackLayout == "" then trackLayout = nil end

  -- Capture final positions, and build the participants in car index order (the player, car 0, first). The police
  -- are not racers: they are in AC's race, not in the result, and the racers' positions are their own order at
  -- the line, whoever did not get there after.
  local participants = {}
  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    if data and not policeOf[carIndex] then
      if bracket and bracket.decided then
        -- A bracket race is won at the quarter by its own rules, whoever led AC's race
        data.finalPosition = (carIndex == 0) == bracket.playerWon and 1 or 2
      elseif #police > 0 then
        data.finalPosition = #finishOrder + 1
        for position, index in ipairs(finishOrder) do
          if index == carIndex then data.finalPosition = position end
        end
      else
        local pos = ac.getCarLeaderboardPosition(carIndex)
        if pos > 0 then
          data.finalPosition = pos
        end
      end
      participants[#participants + 1] = carDataToDict(data)
    end
  end

  -- The chase, when the career sent the police: whether the patrol showed up, and how it went for each racer
  local pursuit = nil
  if chase then
    pursuit = {
      police = #police,
      started = chase.started,
      started_at_s = chase.started and round(chase.startedAt, 1) or nil,
      duration_s = chase.started and round(chase.duration or (sessionDuration - chase.startedAt), 1) or nil,
      player = chase.player,
      rival = chase.rival
    }
  end

  local outputData = {
    metadata = {
      schema_version = SCHEMA_VERSION,
      script_version = SCRIPT_VERSION,
      -- The race manager's id since the first schema: the launcher checks it
      source = "sr_race_manager",
      generated_at = getISOTimestamp()
    },
    session = {
      session_id = sessionId,
      context_id = contextId,
      start_timestamp = sessionStartTime,
      end_timestamp = sessionEndTime,
      duration_seconds = round(sessionDuration, 2),
      track_id = trackId,
      track_layout = trackLayout,
      end_reason = endReason,
      race_type = raceType
    },
    participants = participants,
    pursuit = pursuit
  }

  -- Output directory: AC's own documents folder, the shell's Documents (which OneDrive may have moved),
  -- the same one the launcher reads
  local outputDir = ac.getFolder(ac.FolderID.ACDocuments) .. "\\out\\sr_race_manager"
  io.createDir(outputDir)

  -- Written under a name the launcher ignores, then renamed: a file cut short by a kill is never
  -- taken for a result
  local filename = outputDir .. "\\" .. sessionId .. ".json"
  local tempFilename = filename .. ".tmp"
  local json = JSON.stringify(outputData)

  if not io.save(tempFilename, json) then
    error('Failed to write ' .. tempFilename)
  end
  -- A test-and-tune writes after every pass: each write replaces the last (io.move fails onto a file by default)
  if not io.move(tempFilename, filename, false) then
    io.deleteFile(tempFilename)
    error('Failed to rename ' .. tempFilename .. ' to ' .. filename)
  end
  ac.log('[Street Corsa] Session data written to ' .. filename)
end

-- Seconds of game time since the last tick: nothing moves while the game is paused, and a hitch counts for
-- no more than MAX_TICK_SECONDS
local function measureTick()
  local now = sim.time
  local last = lastSimTimeMs
  lastSimTimeMs = now
  if sim.isPaused or not now or not last then return 0 end
  local dt = (now - last) / 1000.0
  if dt < 0 then return 0 end
  if dt > MAX_TICK_SECONDS then return MAX_TICK_SECONDS end
  return dt
end

-- How far a car has moved out of its own lane towards the other car's, in metres (negative: away from it)
local function intrusion(data, otherData, car)
  local towards = (otherData.laneStart - data.laneStart):dot(stripSide)
  local drift = (car.position - data.laneStart):dot(stripSide)
  return towards >= 0 and drift or -drift
end

-- A drag race, between the green and the line: the two cars touched. The one further out of its own lane hit
-- the other and is disqualified. The player's disqualification ends the race; a disqualified rival is stopped
-- where it is, and the player finishes to win.
local function judgeContact(carIndex, otherIndex)
  local data, otherData = carData[carIndex], carData[otherIndex]
  if not data or not otherData or not data.laneStart or not otherData.laneStart then return end
  if data.disqualified or otherData.disqualified then return end
  if pastTheLine(data) or pastTheLine(otherData) then return end

  local car, other = ac.getCar(carIndex), ac.getCar(otherIndex)
  if not car or not other then return end
  local mine, theirs = intrusion(data, otherData, car), intrusion(otherData, data, other)
  local guiltyIndex = mine >= theirs and carIndex or otherIndex
  local guilty = carData[guiltyIndex]
  guilty.disqualified = true
  ac.log(string.format('[Street Corsa] Contact between cars %d and %d (%.1f m and %.1f m out of their lanes): car %d DISQUALIFIED',
    carIndex, otherIndex, mine, theirs, guiltyIndex))

  if guiltyIndex == 0 then
    endSession('DISQUALIFIED')
  else
    holdAI(guiltyIndex)
    pcall(ac.setMessage, 'Disqualified', guilty.driverName .. ' hit you. Finish the race and it is yours!')
  end
end

--------------------------------------------------------------------------------
-- The police chase
--------------------------------------------------------------------------------

local _roadAt, _roadOn, _roadDir, _placeAt, _facing, _push, _probe = vec3(), vec3(), vec3(), vec3(), vec3(), vec3(), vec3()
local _down = vec3(0, -1, 0)

-- A share of the lap in [0, 1)
local function lapShare(t)
  return t - math.floor(t)
end

-- The road's direction at lap share t, flat and of unit length (into dir); false where the line has none
local function roadDirection(t, dir)
  local step = 5 / math.max(sim.trackLengthM, 1)
  ac.trackProgressToWorldCoordinateTo(lapShare(t), _roadAt)
  ac.trackProgressToWorldCoordinateTo(lapShare(t + step), _roadOn)
  dir:set(_roadOn.x - _roadAt.x, 0, _roadOn.z - _roadAt.z)
  if dir:length() < 1e-6 then return false end
  dir:normalize()
  return true
end

-- Where a position is on the road: lap share and metres to the right of the AI line (Traffic Race's road frame:
-- right of a heading is (-z, x) in AC's handedness, and setAISplineAbsoluteOffset takes right as positive)
local function roadFrame(pos)
  local t = ac.worldCoordinateToTrackProgress(pos)
  if t < 0 or not roadDirection(t, _roadDir) then return nil end
  ac.trackProgressToWorldCoordinateTo(t, _roadAt)
  local lateral = (pos.x - _roadAt.x) * -_roadDir.z + (pos.z - _roadAt.z) * _roadDir.x
  return t, lateral
end

-- Metres along the road from a to b, the short way round: positive when b is ahead
local function gapAlong(a, b)
  local d = b - a
  d = d - math.floor(d + 0.5)
  return d * sim.trackLengthM
end

-- The height of the road under pos, or nil
local function groundUnder(pos)
  _probe:set(pos.x, pos.y + 3, pos.z)
  local distance = physics.raycastTrack(_probe, _down, 10)
  if distance == nil or distance < 0 then return nil end
  return _probe.y - distance
end

-- Puts a cop on the road at lap share t, a hand's breadth over it, facing along it (or parked at an angle on one side
-- for a roadblock, ROADBLOCK_*), and sends it off at speed m/s. The way Test Drive puts a car back: stopped first, then placed (the
-- AI setter faces the opposite of where the car will look), woken, engine running, and pushed.
local function placeCop(cop, t, across, speed, lateral)
  local i = cop.index
  local car = ac.getCar(i)
  if not car or not roadDirection(t, _facing) then return false end
  ac.trackProgressToWorldCoordinateTo(lapShare(t), _placeAt)
  local ground = groundUnder(_placeAt)
  _placeAt.y = (ground or _placeAt.y) + SET_DOWN_CLEARANCE

  _push:set(_facing):scale(math.min(speed, SET_DOWN_MAX_SPEED))
  if lateral and not across then
    -- Off the line (a speed trap on the verge): right of the heading is (-z, x)
    _placeAt.x, _placeAt.z = _placeAt.x - _facing.z * lateral, _placeAt.z + _facing.x * lateral
    local ground3 = groundUnder(_placeAt)
    if ground3 then _placeAt.y = ground3 + SET_DOWN_CLEARANCE end
  end
  if across then
    -- Right of the heading is (-z, x); the nose turned in towards the line
    local sides = ac.getTrackAISplineSides(lapShare(t))
    local side = sides.y >= sides.x and 1 or -1
    local room = side > 0 and sides.y or sides.x
    local lateral = side * math.max(0, math.min(room - ROADBLOCK_EDGE_METRES, ROADBLOCK_OFFSET_METRES))
    local rx, rz = -_facing.z, _facing.x
    _placeAt.x, _placeAt.z = _placeAt.x + rx * lateral, _placeAt.z + rz * lateral
    local ground2 = groundUnder(_placeAt)
    if ground2 then _placeAt.y = ground2 + SET_DOWN_CLEARANCE end
    local c, sn = math.cos(ROADBLOCK_ANGLE), math.sin(ROADBLOCK_ANGLE)
    _facing:set(_facing.x * c - side * rx * sn, 0, _facing.z * c - side * rz * sn):normalize()
    cop.blockT, cop.blockSide = lapShare(t), side
  end

  physics.setCarVelocity(i, vec3())
  physics.setAICarPosition(i, _placeAt, -_facing)
  physics.awakeCar(i)
  pcall(physics.setEngineStallEnabled, i, false)
  pcall(physics.setEngineRPM, i, 3000)
  if not across then
    physics.setCarVelocity(i, _push)
    physics.setAIThrottleLimit(i, 1)
    physics.setAIStopCounter(i, 0)
    physics.setAITopSpeed(i, 1e9)
    pcall(physics.setAIPitStopRequest, i, false)
  end
  cop.stuckFor, cop.behindFor = 0, 0
  if carData[i] then carData[i].grace = TELEPORT_GRACE_SECONDS end
  return true
end

-- A cop's lights: a red and a blue lamp on the roof, flashing in turn, lighting up the road around it
local function lightsOn(cop)
  if cop.lamps then return end
  cop.lamps = {}
  for n = 1, 2 do
    local lamp = ac.LightSource(ac.LightType.Regular)
    lamp.range = 25
    lamp.fadeAt = 400
    lamp.fadeSmooth = 100
    lamp.color = rgb(0, 0, 0)
    cop.lamps[n] = lamp
  end
  local ok, siren = pcall(ac.AudioEvent.fromFile, {
    filename = ac.getFolder(ac.FolderID.ScriptOrigin) .. '\\siren.wav',
    use3D = true, loop = true, minDistance = 10, maxDistance = 900, dopplerEffect = 1
  }, false)
  if ok and siren then
    siren.volume = 1
    siren.cameraInteriorMultiplier = 0.5
    -- Each car's siren starts somewhere else in the wail, so two never sing in unison
    pcall(function() siren:start(); siren:seek(cop.number * 1.7) end)
    cop.siren = siren
  else
    ac.log('[Street Corsa] No siren: ' .. tostring(siren))
  end
end

local function lightsOff(cop)
  if cop.lamps then
    for _, lamp in ipairs(cop.lamps) do pcall(lamp.dispose, lamp) end
    cop.lamps = nil
  end
  if cop.siren then
    pcall(function() cop.siren:stop(); cop.siren:dispose() end)
    cop.siren = nil
  end
end

local RED, BLUE, DARK = rgb(30, 0, 0), rgb(0, 4, 40), rgb(0, 0, 0)

-- Where a cop's lamps are, off its roof, in world space (into out); n = 1 left, 2 right
local function lampPosition(car, n, out)
  local height = (car.aabbSize and car.aabbSize.y > 0.5) and car.aabbSize.y * 0.5 + 0.1 or 1.4
  out:set(car.position):addScaled(car.up, height):addScaled(car.side, n == 1 and -0.45 or 0.45)
  if car.aabbCenter then out:addScaled(car.up, car.aabbCenter.y) end
  return out
end

-- Which lamp is lit: each car flashes about twice a second, its own beat
local function lampLit(cop, n)
  return (math.floor(sessionDuration * 4 + cop.number) % 2 == 0) == (n == 1)
end

local _lamp = vec3()
local function updateLights(cop, car)
  if not cop.lamps then return end
  for n, lamp in ipairs(cop.lamps) do
    lamp.position = lampPosition(car, n, _lamp)
    lamp.color = lampLit(cop, n) and (n == 1 and RED or BLUE) or DARK
  end
  if cop.siren then pcall(cop.siren.setPosition, cop.siren, car.position, car.look, car.up, car.velocity) end
end

-- Puts a cop out of sight for good: the chase is over for it
local function putAway(cop)
  lightsOff(cop)
  cop.state = 'GONE'
  pcall(physics.setAISplineAbsoluteOffset, cop.index, 0, false)
  holdAI(cop.index)
  pcall(ac.setCarActive, cop.index, false)
  pcall(physics.disableCarCollisions, cop.index, true)
end

-- Forward declaration: steers AI racers past roadblocks, defined with the chase below
local steerPastRoadblocks

shutDownPolice = function()
  for _, cop in ipairs(police) do
    -- A trap nobody set off stays parked where it is: a car vanishing off the verge would be seen
    if cop.state ~= 'GONE' and cop.state ~= 'PARKED' then putAway(cop) end
  end
  if steerPastRoadblocks then pcall(steerPastRoadblocks, true) end
end

-- Is the rival still running, for a cop to go after it
local function rivalRunning()
  local data = carData[1]
  return data ~= nil and not policeOf[1] and not data.crashed and not data.brokeDown and not data.disqualified
    and chase.rival == nil
end

-- The patrol shows up: each cop joins behind its target, the first nearest. With more than one, the last goes
-- after the rival.
local function startChase()
  chase.started = true
  chase.startedAt = sessionDuration
  -- The patrol saw them both: each one's chase counts
  markChased(0)
  markChased(1)
  for n, cop in ipairs(police) do
    -- One for each racer: the first after the player, the second after the rival
    cop.target = (n == 2 and rivalRunning()) and 1 or 0
    local target = ac.getCar(cop.target)
    local metres = POLICE_JOIN_METRES
    local ok, placed = pcall(placeCop, cop, target.splinePosition - metres / sim.trackLengthM, false, target.speedKmh / 3.6 * 0.8)
    if ok and placed then
      cop.state = 'CHASING'
      pcall(ac.setCarActive, cop.index, true)
      pcall(physics.disableCarCollisions, cop.index, false)
      lightsOn(cop)
      ac.log(string.format('[Street Corsa] Cop %d after car %d from %.0f m back', cop.index, cop.target, metres))
    else
      cop.state = 'OUT'
      ac.log(string.format('[Street Corsa] Cop %d could not be put on the road: %s', cop.index, tostring(placed)))
    end
  end
  pcall(ac.setMessage, 'Cops!', 'The police are on to you. Lose them!')
end

local _toTarget = vec3()
local _boost, _boostAt = vec3(), vec3()

-- Gets a cop that has been standing (in a trap, at a roadblock) going after its prey at full throttle: the way Test
-- Drive gets a still car going again. Released, woken (a still body is put to sleep and ignores its engine), engine
-- revving in first gear, and a push along the road at t. Released alone, a cop sat on the verge at 0 km/h (2026-09-24).
local function pullOut(cop, t)
  local i = cop.index
  physics.setAIThrottleLimit(i, 1)
  physics.setAIStopCounter(i, 0)
  physics.setAITopSpeed(i, 1e9)
  pcall(physics.awakeCar, i)
  pcall(physics.setEngineStallEnabled, i, false)
  pcall(physics.setEngineRPM, i, 2500)
  pcall(physics.engageGear, i, 1)
  pcall(physics.setCarAutoShifter, i, true)
  if roadDirection(t, _push) then
    pcall(physics.setCarVelocity, i, _push:scale(PULL_OUT_SPEED))
  end
  cop.stuckFor, cop.behindFor = 0, 0
end

-- One cop, every frame of the chase: racing its prey, and past it if it can
local function driveCop(cop, dt)
  local i = cop.index
  local car = ac.getCar(i)
  if not car then return end

  updateLights(cop, car)

  -- The rival is out of it (caught, wrecked, broken down): this cop stays with the arrest, lights on
  if cop.target == 1 and not rivalRunning() then
    cop.state = 'ARRESTING'
    holdAI(i)
    ac.log(string.format('[Street Corsa] Cop %d stays with the rival', i))
    return
  end
  local target = ac.getCar(cop.target)
  if not target then return end

  -- AC's own retirement puts a car that stops in the pits
  pcall(physics.preventAIFromRetiring, i)

  local copT = ac.worldCoordinateToTrackProgress(car.position)
  local targetT = ac.worldCoordinateToTrackProgress(target.position)
  if copT < 0 or targetT < 0 then return end
  local gap = gapAlong(copT, targetT)

  if cop.state == 'ROADBLOCK' then
    holdAI(i)
    -- Dodged: after them at full throttle. Judged from where the roadblock was put: the car reads where it was until
    -- the physics has run, and a roadblock once "left" in the frame it went up.
    if gapAlong(cop.blockT or copT, targetT) > ROADBLOCK_PASSED_METRES then
      pullOut(cop, cop.blockT or copT)
      cop.state = 'CHASING'
      ac.log(string.format('[Street Corsa] Cop %d: car %d got past its roadblock, after it', i, cop.target))
    end
    return
  end

  cop.aiTimer = cop.aiTimer - dt
  if cop.aiTimer <= 0 then
    cop.aiTimer = POLICE_AI_EVERY
    pcall(physics.setAILevel, i, POLICE_AI_LEVEL)
    pcall(physics.setAIAggression, i, POLICE_AI_AGGRESSION)
  end

  -- The mild rubber band: AC's own grip on their bumper, a little more the further back
  pcall(physics.setExtraAIGrip, i, math.min(POLICE_MAX_GRIP, math.max(POLICE_MIN_GRIP, POLICE_MIN_GRIP + (gap - 50) / 1000)))

  -- Past them: busted. Only a cop that has been behind its prey can get past it (a roadblock or a trap starts ahead).
  if gap > 0 then cop.behind = true end
  cop.pastFor = (cop.behind and gap < -OVERTAKE_METRES) and (cop.pastFor or 0) + dt or 0
  if cop.pastFor >= OVERTAKE_SECONDS then
    cop.overtook = true
    return
  end

  -- Losing ground: a roadblock up the road, once
  cop.behindFor = gap > ROADBLOCK_BEHIND_METRES and cop.behindFor + dt or 0
  if cop.behindFor >= ROADBLOCK_AFTER_SECONDS and not cop.roadblocked then
    if placeCop(cop, targetT + ROADBLOCK_AHEAD_METRES / sim.trackLengthM, true, 0) then
      cop.state = 'ROADBLOCK'
      cop.roadblocked = true
      cop.behind = false
      holdAI(i)
      ac.log(string.format('[Street Corsa] Cop %d sets up a roadblock %.0f m ahead of car %d', i, ROADBLOCK_AHEAD_METRES, cop.target))
    end
    return
  end

  -- Stuck somewhere: back on the road behind them
  _toTarget:set(target.position):sub(car.position)
  cop.stuckFor = (car.speedKmh < STUCK_KMH and _toTarget:length() > 30) and cop.stuckFor + dt or 0
  if cop.stuckFor >= STUCK_SECONDS then
    ac.log(string.format('[Street Corsa] Cop %d stuck: back on the road %.0f m behind car %d', i, REJOIN_METRES, cop.target))
    placeCop(cop, targetT - REJOIN_METRES / sim.trackLengthM, false, target.speedKmh / 3.6 * 0.8)
  end
end

-- Busted anyway: a cop right there and the car all but stopped, for long enough (a car stopped off the road)
local function caught(targetIndex, dt)
  local target = ac.getCar(targetIndex)
  if not target then return false end
  local near = false
  for _, cop in ipairs(police) do
    local car = ac.getCar(cop.index)
    if car and (cop.state == 'CHASING' or cop.state == 'ROADBLOCK') and car.position:distance(target.position) < BUSTED_METRES then
      near = true
    end
  end
  local held = (chase.caughtFor[targetIndex] or 0)
  held = (near and target.speedKmh < BUSTED_KMH) and held + dt or 0
  chase.caughtFor[targetIndex] = held
  return held >= BUSTED_SECONDS
end

-- The player got away: every cop after them far back along the road, for long enough, or none left after them.
-- A cop after the rival doesn't count (and stays with the rival once it has caught them).
local function gotAway(dt)
  local player = ac.getCar(0)
  local playerT = player and ac.worldCoordinateToTrackProgress(player.position) or -1
  if playerT < 0 then return false end
  local left, far = 0, true
  for _, cop in ipairs(police) do
    local after = cop.target == 0
    if after and (cop.state == 'CHASING' or cop.state == 'ROADBLOCK') then
      left = left + 1
      local car = ac.getCar(cop.index)
      local copT = car and ac.worldCoordinateToTrackProgress(car.position) or -1
      if copT < 0 or math.abs(gapAlong(copT, playerT)) < ESCAPE_METRES then far = false end
    end
  end
  if left == 0 then return true end
  chase.escapeFor = far and chase.escapeFor + dt or 0
  return chase.escapeFor >= ESCAPE_SECONDS
end

-- AI-driven racers (the rival; the player under an autopilot) are steered into the open lane past a roadblock: AC's AI
-- brakes to a stop behind a parked car on its line rather than going round it, and was busted there every time
-- (2026-09-24). Traffic Race's way of steering round traffic.
local ROADBLOCK_SEEN_METRES = 150
local dodging = {}

steerPastRoadblocks = function(release)
  for racer = 0, math.min(1, carCount - 1) do
    local car, data = ac.getCar(racer), carData[racer]
    local want = nil
    if not release and car and data and car.isAIControlled and not data.crashed and not data.brokeDown and not data.busted then
      local t = ac.worldCoordinateToTrackProgress(car.position)
      for _, cop in ipairs(police) do
        if t >= 0 and cop.state == 'ROADBLOCK' and cop.blockT then
          local ahead = gapAlong(t, cop.blockT)
          if ahead > -10 and ahead < ROADBLOCK_SEEN_METRES then want = -cop.blockSide * ROADBLOCK_OFFSET_METRES end
        end
      end
    end
    if want then
      pcall(physics.setAISplineAbsoluteOffset, racer, want, true)
      dodging[racer] = true
    elseif dodging[racer] then
      pcall(physics.setAISplineAbsoluteOffset, racer, 0, false)
      dodging[racer] = nil
    end
  end
end

-- The road's heading at lap share t, in radians
local _headingDir = vec3()
local atan2 = math.atan2 or math.atan
local function headingAt(t)
  if not roadDirection(t, _headingDir) then return nil end
  return atan2(_headingDir.z, _headingDir.x)
end

-- Where speed traps can go: straights with room at one side, as { t, side, room }
local function trapCandidates()
  local candidates = {}
  local length = sim.trackLengthM
  local window = TRAP_STRAIGHT_METRES / length
  for metres = TRAP_FROM * length, TRAP_TO * length, TRAP_STEP_METRES do
    local t = metres / length
    local before, after = headingAt(t - window), headingAt(t + window)
    if before and after then
      local turn = after - before
      turn = turn - math.floor(turn / (2 * math.pi) + 0.5) * 2 * math.pi
      local sides = ac.getTrackAISplineSides(lapShare(t))
      local room = math.max(sides.x, sides.y)
      if math.abs(turn) < TRAP_MAX_TURN and room >= TRAP_MIN_ROOM then
        candidates[#candidates + 1] = { t = t, side = sides.y >= sides.x and 1 or -1, room = room }
      end
    end
  end
  return candidates
end

-- Before the green: each cop parked in a speed trap, one per stretch of the lap, the spots random. A cop left
-- without a spot stays hidden; no spot at all, and the police come as a patrol instead.
setUpTraps = function()
  local candidates = trapCandidates()
  local stretch = (TRAP_TO - TRAP_FROM) / #police
  local parked = 0
  for n, cop in ipairs(police) do
    local from, to = TRAP_FROM + (n - 1) * stretch, TRAP_FROM + n * stretch
    local inStretch = {}
    for _, c in ipairs(candidates) do
      if c.t >= from and c.t < to then inStretch[#inStretch + 1] = c end
    end
    local spot = #inStretch > 0 and inStretch[math.random(#inStretch)] or nil
    if spot then
      local lateral = spot.side * math.min(spot.room - TRAP_EDGE_METRES, TRAP_MAX_OFFSET)
      if placeCop(cop, spot.t, false, 0, lateral) then
        holdAI(cop.index)
        cop.state = 'PARKED'
        cop.spotT = lapShare(spot.t)
        pcall(ac.setCarActive, cop.index, true)
        pcall(physics.disableCarCollisions, cop.index, false)
        parked = parked + 1
        ac.log(string.format('[Street Corsa] Cop %d parked in a speed trap at %.3f of the lap, %.1f m off the line', cop.index, cop.spotT, lateral))
      end
    end
  end
  if parked == 0 then
    chase.traps = false
    ac.log(string.format('[Street Corsa] No spot for a speed trap (%d straight(s) with room): the police come as a patrol', #candidates))
  end
end

-- A racer is going past a parked cop: it lights up and goes after them. The first one starts the chase.
local function engage(cop, racer)
  local i = cop.index
  cop.target = racer
  cop.state = 'CHASING'
  pullOut(cop, cop.spotT)
  lightsOn(cop)
  if not chase.started then
    chase.started = true
    chase.startedAt = sessionDuration
  end
  markChased(racer)
  ac.log(string.format('[Street Corsa] Cop %d in its speed trap saw car %d go by: after it', i, racer))
  pcall(ac.setMessage, 'Cops!', racer == 0 and 'A cop saw you go by. Lose him!'
    or (carData[1] and carData[1].driverName or 'Your rival') .. ' just went past a cop!')
end

-- A cop is after the racer already
local function hasCop(racer)
  for _, cop in ipairs(police) do
    if cop.target == racer and (cop.state == 'CHASING' or cop.state == 'ROADBLOCK' or cop.state == 'ARRESTING') then return true end
  end
  return false
end

local function bustRival()
  decideChase('rival', 'BUSTED')
  carData[1].busted = true
  holdAI(1)
  pcall(ac.setMessage, 'Busted', (carData[1] and carData[1].driverName or 'Your rival') .. ' got caught by the police!')
end

-- The player is busted: the message, and an autopilot takes the car and brakes it to a stop; the race ends then
local function pullOver()
  decideChase('player', 'BUSTED')
  chase.pullOverAt = sessionDuration
  pcall(physics.setCarAutopilot, true)
  pcall(physics.setGentleStop, 0, true)
  pcall(ac.setMessage, 'Busted!', 'The police got you. Pull over.')
end

local function checkTraps()
  for _, cop in ipairs(police) do
    if cop.state == 'PARKED' and cop.spotT then
      for racer = 0, math.min(1, carCount - 1) do
        local free = ((racer == 0 and chase.player == nil) or (racer == 1 and rivalRunning())) and not hasCop(racer)
          and carData[racer] and carData[racer].lapsCompleted < 1
        local car = free and ac.getCar(racer)
        if car then
          local t = ac.worldCoordinateToTrackProgress(car.position)
          local past = t >= 0 and gapAlong(cop.spotT, t) or -1
          if past > 0 and past < TRAP_ENGAGE_METRES then
            engage(cop, racer)
            break
          end
        end
      end
    end
  end
end

-- Every frame after the green: the patrol shows up once the player is far enough in, or the traps wait for racers;
-- then each racer's chase runs on its own, until the police get past them or they get away
local function updateChase(dt)
  if not chase or not sim.isSessionStarted or sessionEnded then return end

  -- Hidden on the grid or parked in a trap until called, and never retired for standing still, whatever the chase is doing
  for _, cop in ipairs(police) do
    if cop.state == 'WAITING' or cop.state == 'PARKED' then
      holdAI(cop.index)
      pcall(physics.preventAIFromRetiring, cop.index)
    end
  end

  -- A busted player being pulled over: the race ends once the car has stopped
  if chase.pullOverAt then
    local player = ac.getCar(0)
    if (player and player.speedKmh < 3) or sessionDuration - chase.pullOverAt >= PULL_OVER_SECONDS then
      chase.pullOverAt = nil
      endSession('BUSTED')
    end
    return
  end

  if chase.traps then
    local ok, err = pcall(checkTraps)
    if not ok then ac.log('[Street Corsa] Speed traps: ' .. tostring(err)) end
    if not chase.started then return end
  elseif not chase.started then
    if carData[0].distanceKm * 1000 >= chase.spotMetres then
      local ok, err = pcall(startChase)
      if not ok then
        ac.log('[Street Corsa] The chase could not start: ' .. tostring(err))
        chase.started = true
        chase.startedAt = sessionDuration
        chase.player = 'ESCAPED'
        chase.rival = 'ESCAPED'
        shutDownPolice()
      end
    end
    return
  end

  pcall(steerPastRoadblocks, false)

  for _, cop in ipairs(police) do
    if cop.state == 'CHASING' or cop.state == 'ROADBLOCK' then
      local ok, err = pcall(driveCop, cop, dt)
      if not ok then
        ac.log(string.format('[Street Corsa] Cop %d: %s', cop.index, tostring(err)))
      end
    elseif cop.state == 'OUT' then
      lightsOff(cop)
      holdAI(cop.index)
    elseif cop.state == 'ARRESTING' then
      holdAI(cop.index)
      local car = ac.getCar(cop.index)
      if car then updateLights(cop, car) end
    end
  end

  -- A cop that got past its prey has them
  for _, cop in ipairs(police) do
    if cop.state == 'CHASING' and cop.overtook then
      cop.overtook = false
      cop.state = 'ARRESTING'
      holdAI(cop.index)
      ac.log(string.format('[Street Corsa] Cop %d got past car %d', cop.index, cop.target))
      if cop.target == 1 and rivalHunted() then
        bustRival()
      elseif cop.target == 0 and chase.player == nil then
        pullOver()
        return
      end
    end
  end

  -- The rival over the line is home: the cop after them is called off
  if rivalHunted() and carData[1] and carData[1].lapsCompleted >= 1 then
    decideChase('rival', 'ESCAPED')
    for _, cop in ipairs(police) do
      if cop.target == 1 and (cop.state == 'CHASING' or cop.state == 'ROADBLOCK') then putAway(cop) end
    end
  end

  if rivalHunted() and rivalRunning() and caught(1, dt) then bustRival() end

  -- The player's chase: caught, or away from the cops after him (the rival's chase goes on without it)
  if chase.playerChased and chase.player == nil then
    if caught(0, dt) then
      pullOver()
      return
    end

    local giveUp = sessionDuration - chase.playerSince >= CHASE_GIVE_UP_SECONDS
    if gotAway(dt) or giveUp then
      decideChase('player', 'ESCAPED')
      chase.duration = sessionDuration - chase.startedAt
      for _, cop in ipairs(police) do
        if cop.target == 0 and (cop.state == 'CHASING' or cop.state == 'ROADBLOCK') then putAway(cop) end
      end
      pcall(ac.setMessage, 'Got away', giveUp and 'The cops gave up on you. Now win the race!' or 'You lost the cops. Now win the race!')
    end
  end

  -- The rival's chase: the police give up on them after a while
  if rivalHunted() and sessionDuration - (chase.rivalSince or chase.startedAt) >= CHASE_GIVE_UP_SECONDS then
    decideChase('rival', 'ESCAPED')
    for _, cop in ipairs(police) do
      if cop.target == 1 and (cop.state == 'CHASING' or cop.state == 'ROADBLOCK') then putAway(cop) end
    end
  end
end

local _glowTo = vec3()

-- The lamps as the eye sees them: a bright dot on each side of the roof, the lit one glowing
function script.draw3D()
  if not chase or not chase.started then return end
  local camera = ac.getCameraPosition()
  for _, cop in ipairs(police) do
    local car = cop.lamps and ac.getCar(cop.index)
    if car then
      for n = 1, 2 do
        local at = lampPosition(car, n, _lamp)
        _glowTo:set(camera):sub(at)
        local lit = lampLit(cop, n)
        local colour = n == 1 and rgbm(lit and 40 or 0.4, 0, 0, 1) or rgbm(0, lit and 6 or 0.1, lit and 60 or 0.6, 1)
        render.circle(at, _glowTo, lit and 0.16 or 0.08, colour)
      end
    end
  end
end

ac.onCarCollision(-1, function(carIndex)
  touching[carIndex] = COLLISION_WINDOW_SECONDS

  -- Car against car in a drag race: somebody left their lane
  if raceType ~= 'DRAG' or not sessionActive or not sim.isSessionStarted then return end
  local car = ac.getCar(carIndex)
  if not car or car.collidedWith == 0 then return end
  -- collidedWith is 0 for the track and non-zero for a car; with two cars the other one is the only one there is
  if carCount ~= 2 then return end
  judgeContact(carIndex, 1 - carIndex)
end)

-- Everything AC would do to a car on its own: pit teleports and recovery. The block is a disposable; AC quits
-- with the mode, which releases it.
if physics.allowed() then
  local block = physics.blockTeleportingToPits()
  local recoveryOff = pcall(ac.disableCarRecovery, true)
  ac.log(string.format('[Street Corsa] Pit teleports blocked %s, car recovery off %s', tostring(block ~= nil), tostring(recoveryOff)))
else
  ac.log('[Street Corsa] ERROR: no physics API - is ALLOW_PHYSICS_ALTERATIONS in the manifest?')
end

-- The race starts by itself, in two steps. The game loads onto the pits menu, where neither prepare() nor
-- update() is called: a timer presses Drive (the old app did this; without it the game waits on the menu).
-- Then a start message puts the mode in its preparation stage, and prepare() ends it.
local startTimer
startTimer = setInterval(function()
  local s = ac.getSim()
  if not s.isInMainMenu then
    clearInterval(startTimer)
    return
  end
  if ac.tryToStart(true) then
    ac.log('[Street Corsa] Race auto-started!')
  end
end, 0.25)

ac.setStartMessage('Street Corsa')

local PREPARE_SECONDS = 0.5
local preparedFor = 0

function script.prepare(dt)
  preparedFor = preparedFor + dt
  if preparedFor < PREPARE_SECONDS then return false end
  ac.log('[Street Corsa] Ready to race')
  return true
end

function script.update(dt)
  -- The result is written: nothing more to track until AC quits
  if sessionEnded then return end

  sim = ac.getSim()

  -- Back on the pits menu (the player pressed Escape): nothing to track there. On a test-and-tune it is leaving the
  -- strip, with the passes run so far.
  if sim.isInMainMenu then
    if tune and sessionActive and sim.isSessionStarted then endSession('FINISHED') end
    return
  end

  if sessionId == nil then
    initializeSession()
    return
  end

  if not tune then checkFalseStart() end
  if sessionEnded then return end

  updateCrashes(dt)
  if sessionEnded then return end

  updateBreakdowns()
  if sessionEnded then return end

  local tick = measureTick()
  sessionDuration = sessionDuration + tick
  updateAllTelemetry(tick)
  engineHeat.update(tick, function(data)
    return not data.crashed and not data.disqualified and not data.brokeDown and not data.busted and not pastTheLine(data)
  end)
  local previousClock = raceClock or 0
  if sim.isSessionStarted then raceClock = previousClock + tick end
  updateTimeslips(previousClock, tick)

  updateBracket()
  if sessionEnded then return end

  updateTune(tick)
  if sessionEnded then return end

  updateChase(tick)
  if sessionEnded then return end

  -- A bracket race is over at the quarter, a test-and-tune when the player is done
  if not bracket and not tune then checkRaceFinish() end
end

-- AC puts a car back: after a drag run, on a jump start, a lane violation, or the player going to the pits
ac.onCarJumped(-1, function(carIndex)
  local data = carData[carIndex]
  if data then
    data.grace = TELEPORT_GRACE_SECONDS
    local car = ac.getCar(carIndex)
    if car then
      data.velocity:set(car.velocity)
      data.position:set(car.position)
    end
  end

  if carIndex ~= 0 then return end

  -- A crash has its own quit timer
  if playerCrashed then return end

  -- Race over (a drag race puts the cars back after the run): quit now
  if raceEnded then
    ac.log('[Street Corsa] Car put back after the race - Quitting AC')
    ac.shutdownAssettoCorsa()
    return
  end

  -- Test-and-tune: the mode puts the car back on the line after each pass; anything else is the player going to
  -- the pits, done with the strip
  if tune then
    if tune.teleportedAt and sessionDuration - tune.teleportedAt < TELEPORT_GRACE_SECONDS then return end
    if sessionActive then
      ac.log('[Street Corsa] Put back to the pits: the strip is done')
      endSession('FINISHED')
    end
    return
  end

  if not sessionActive or not data then return end

  -- Before it got anywhere, the car jumped the start; after, it is out of the race
  if data.distanceKm * 1000 < FALSE_START_TELEPORT_METRES then
    data.falseStart = true
    ac.log(string.format('[Street Corsa] FALSE START: put back after %.1f m', data.distanceKm * 1000))
    endSession('FALSE_START')
  else
    ac.log(string.format('[Street Corsa] Put back after %.0f m - out of the race', data.distanceKm * 1000))
    endSession('ABANDONED')
  end
end)

local COLOR_DIM = rgbm(0, 0, 0, 0.5)
local COLOR_BOX = rgbm(0, 0, 0, 0.85)
local COLOR_BORDER = rgbm(1, 1, 1, 0.3)
local COLOR_TEXT = rgbm(1, 1, 1, 1)
local BOX_SIZE = vec2(600, 200)

local LAMP = { OFF = rgbm(0.15, 0.15, 0.15, 1), AMBER = rgbm(1, 0.65, 0, 1), GREEN = rgbm(0.1, 1, 0.2, 1),
  RED = rgbm(1, 0.1, 0.1, 1), RADIUS = 16 }

-- The player's own tree, when the mode gives the green (a bracket race, a test-and-tune pass): three ambers coming on
-- half a second apart, then the green, or the red of a car that left early. Shown from a second before the first
-- amber until two seconds after the green.
local function drawTree(size)
  local data = carData[0]
  if not data or not raceClock or not (bracket or (tune and (tune.state == 'TREE' or tune.state == 'RUN'))) then return end
  local firstAmber = data.greenAt - STRIP.TREE_AMBERS * STRIP.TREE_STEP
  if raceClock < firstAmber - 1 or raceClock > data.greenAt + 2 then return end

  local x = size.x - 70
  local y = size.y * 0.3
  ui.drawRectFilled(vec2(x - 30, y - 30), vec2(x + 30, y + (STRIP.TREE_AMBERS + 1) * 44 + 4), COLOR_BOX, 8)
  for n = 1, STRIP.TREE_AMBERS do
    local lit = raceClock >= firstAmber + (n - 1) * STRIP.TREE_STEP and raceClock < data.greenAt
    ui.drawCircleFilled(vec2(x, y + (n - 1) * 44), LAMP.RADIUS, lit and LAMP.AMBER or LAMP.OFF, 24)
  end
  local bottom = vec2(x, y + STRIP.TREE_AMBERS * 44)
  if data.redLight then
    ui.drawCircleFilled(bottom, LAMP.RADIUS, LAMP.RED, 24)
  else
    ui.drawCircleFilled(bottom, LAMP.RADIUS, raceClock >= data.greenAt and LAMP.GREEN or LAMP.OFF, 24)
  end
  if bracket then
    ui.pushFont(ui.Font.Main)
    ui.drawTextClipped(string.format('Dial-in\n%.2f', bracket.dialIn[0]), vec2(x - 60, y + (STRIP.TREE_AMBERS + 1) * 44 + 8),
      vec2(x + 30, y + (STRIP.TREE_AMBERS + 1) * 44 + 60), COLOR_TEXT, vec2(0.5, 0))
    ui.popFont()
  end
end

-- A test-and-tune pass's slip, while the car waits to go back to the line
local function drawPassSlip(size)
  if not tune or tune.state ~= 'SLIP' then return end
  local slip = tune.last
  local lines = {}
  if slip then
    lines[#lines + 1] = string.format('Pass %d of %d%s', slip.pass, tune.maxPasses, slip.red_light and '   RED LIGHT' or '')
    local function mark(label, value, format) lines[#lines + 1] = string.format('%-8s %s', label, value and string.format(format, value) or '-') end
    mark('R/T', slip.reaction_s, '%.3f')
    mark("60'", slip.sixty_ft_s, '%.3f')
    mark("330'", slip.three_thirty_ft_s, '%.3f')
    mark('1/8 ET', slip.eighth_mile_s, '%.3f')
    mark('1/8 MPH', slip.eighth_mile_mph, '%.2f')
    mark('1/4 ET', slip.quarter_mile_s, '%.3f')
    mark('1/4 MPH', slip.quarter_mile_mph, '%.2f')
  else
    lines[#lines + 1] = 'No time: the car never left the line'
  end
  local left = math.max(0, math.ceil(STRIP.TUNE_SLIP - ((raceClock or 0) - tune.since)))
  lines[#lines + 1] = ''
  lines[#lines + 1] = string.format('Back to the line in %d s. Go to the pits to leave the strip.', left)

  local box = vec2(460, 40 + #lines * 22)
  local topLeft = vec2((size.x - box.x) / 2, size.y * 0.15)
  ui.drawRectFilled(topLeft, topLeft + box, COLOR_BOX, 10)
  ui.drawRect(topLeft, topLeft + box, COLOR_BORDER, 10, nil, 2)
  ui.pushFont(ui.Font.Monospace)
  ui.drawTextClipped(table.concat(lines, '\n'), topLeft + vec2(20, 20), topLeft + box - vec2(20, 20), COLOR_TEXT, vec2(0, 0))
  ui.popFont()
end

function script.drawUI()
  local size = ui.windowSize()
  if not showResultOverlay then
    pcall(drawTree, size)
    pcall(drawPassSlip, size)
    return
  end

  ui.drawRectFilled(vec2(0, 0), size, COLOR_DIM)

  local topLeft = vec2((size.x - BOX_SIZE.x) / 2, (size.y - BOX_SIZE.y) / 2 - 50)
  local bottomRight = topLeft + BOX_SIZE
  ui.drawRectFilled(topLeft, bottomRight, COLOR_BOX, 10)
  ui.drawRect(topLeft, bottomRight, COLOR_BORDER, 10, nil, 2)

  ui.pushFont(ui.Font.Title)
  ui.drawTextClipped(resultTitle, topLeft, vec2(bottomRight.x, topLeft.y + 80), COLOR_TEXT, vec2(0.5, 0.5))
  ui.popFont()
  ui.pushFont(ui.Font.Main)
  ui.drawTextClipped(resultMessage, vec2(topLeft.x, topLeft.y + 90), vec2(bottomRight.x, bottomRight.y - 10), COLOR_TEXT, vec2(0.5, 0.5))
  ui.popFont()
end

ac.log('[Street Corsa] Race mode loaded')
