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
  - Judges a breakdown: a blown engine, a gearbox or a corner that gives out, a blown tyre. A player's breakdown ends
    the race; a rival's is held where it stopped, and the player still has to finish
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

local SCRIPT_VERSION = "3.3.0"
-- 1.1: session.context_id, participants[].car_index and participants[].is_player
-- 1.2: session.end_reason, participants[].false_start, participants[].condition
-- 1.3: participants[].disqualified, session.race_type
-- 1.4: session.end_reason BROKE_DOWN, participants[].broke_down, participants[].breakdown, participants[].timeslip
local SCHEMA_VERSION = "1.4"

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
  TYRE = "A tyre blew!\nYou're out of the race."
}

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

-- DRAG or ROAD, from race.ini [STREET_ROD] RACE_TYPE
local raceType = nil

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

    -- Timeslip (drag races): the race clock when the car left the line, how far down the strip it was last frame,
    -- and the time at each mark
    leftAt = nil,
    lastMetres = 0,
    slip = {},

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
  if ok and (value == 'DRAG' or value == 'ROAD') then return value end
  return nil
end

-- Numbers from race.ini [STREET_ROD] KEY=a,b,c; nil when the key is not there or is not all numbers
local function readNumbers(key, count)
  local ok, value = pcall(function()
    return ac.INIConfig.raceConfig():get('STREET_ROD', key, '')
  end)
  if not ok or type(value) ~= 'string' or value == '' then return nil end
  local numbers = {}
  for part in value:gmatch('[^,]+') do
    local n = tonumber(part)
    if not n or n ~= n or n == math.huge or n == -math.huge then return nil end
    numbers[#numbers + 1] = n
  end
  if #numbers < count then return nil end
  return numbers
end

-- The shape a car goes into the race in, from the career: body and engine into AC, the gearbox and the corners
-- remembered for the breakdowns (AC starts those new; the car's data already carries what they do)
local function applyStartState(carIndex, data)
  local prefix = 'CAR_' .. carIndex .. '_'
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
  if raceType == 'DRAG' and player and not sim.isSessionStarted then
    -- Each car's lane is the line it stands on
    for i = 0, carCount - 1 do
      local car = ac.getCar(i)
      if car then carData[i].laneStart = vec3():set(car.position) end
    end
    local look = vec3(player.look.x, 0, player.look.z):normalize()
    stripSide = vec3(-look.z, 0, look.x)
    stripForward = look
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

-- End session: write results, show the result, and quit
-- reason: FINISHED (WIN or LOSE), CRASH, FALSE_START, DISQUALIFIED, BROKE_DOWN or ABANDONED
local function endSession(reason, won)
  if not sessionActive then return end
  sessionActive = false
  sessionEnded = true
  raceEnded = true
  endReason = reason

  ac.log('[Street Corsa] Ending session: ' .. reason .. (reason == 'FINISHED' and (won and ' (WIN)' or ' (LOSE)') or ''))

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

  if reason == 'CRASH' then
    resultTitle, resultMessage = "Race Over", MSG_CRASH
    holdPlayer(30)
  elseif reason == 'FALSE_START' then
    resultTitle, resultMessage = "False Start", MSG_FALSE_START
    holdPlayer(30)
  elseif reason == 'DISQUALIFIED' then
    resultTitle, resultMessage = "Disqualified", MSG_DISQUALIFIED
    holdPlayer(30)
  elseif reason == 'BROKE_DOWN' then
    resultTitle, resultMessage = "Broke Down", MSG_BROKE_DOWN[carData[0] and carData[0].breakdown or 'ENGINE'] or MSG_BROKE_DOWN.ENGINE
    holdPlayer(30)
  else
    resultTitle, resultMessage = "Race Over", won and MSG_WIN or MSG_LOSE
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

      if data.crashed or data.brokeDown then
        -- A crashed or broken-down rival stays where it is
        if carIndex ~= 0 then holdAI(carIndex) end
      elseif data.disqualified and carIndex ~= 0 and data.grace <= 0 and touching[carIndex] and g >= CRASH_G then
        -- A disqualified rival that crashed doing it: both on its record
        crash(carIndex, data, g)
      elseif data.disqualified and carIndex ~= 0 then
        -- A disqualified rival stays where it is
        holdAI(carIndex)
      elseif data.grace > 0 then
        data.grace = data.grace - dt
      elseif data.lapsCompleted >= 1 then
        -- Past the line nothing counts: a rival that finishes first and wrecks itself at the end of the strip
        -- (205 km/h, 2026-09-24) has still won, and the player still gets to finish
      elseif touching[carIndex] and g >= CRASH_G and sim.isSessionStarted then
        crash(carIndex, data, g)
        if sessionEnded then return end
      end
    end
  end
end

-- What gave out on a car, or nil: the engine's life run out, the gearbox or a corner past what it could take with
-- what it carried in, a blown tyre
local function findBreakdown(car, data)
  if car.engineLifeLeft <= 0 then return 'ENGINE' end
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
    if data and car and not data.crashed and not data.disqualified and not data.brokeDown and data.lapsCompleted < 1 then
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
      end
    end
  end
end

-- A drag race's timeslips: each car's time at every mark down the strip, from the moment it left the line. The
-- moment a mark is passed is found between two frames, from where the car was on each side of it.
local function updateTimeslips(tick)
  if raceType ~= 'DRAG' or not stripForward or not sim.isSessionStarted then return end
  local previousClock = raceClock or 0
  raceClock = previousClock + tick
  if tick <= 0 then return end

  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and car and data.laneStart then
      local metres = (car.position - data.laneStart):dot(stripForward)
      local function crossed(mark)
        if data.lastMetres >= mark or metres < mark then return nil end
        local share = (mark - data.lastMetres) / math.max(metres - data.lastMetres, 1e-6)
        return previousClock + share * tick
      end

      if not data.leftAt then
        data.leftAt = crossed(STAGE_METRES)
      end
      if data.leftAt then
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

-- Update telemetry for all cars
local function updateAllTelemetry(deltaT)
  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and car then
      data.totalRaceTimeMs = data.totalRaceTimeMs + (deltaT * 1000.0)

      local currentLapCount = car.lapCount
      if currentLapCount > data.prevLapCount then
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

-- The race is over when the player crosses the line, whoever got there first: the player always gets to finish
local function checkRaceFinish()
  if raceEnded then return end

  local playerData = carData[0]
  if not playerData then return end

  if playerData.lapsCompleted >= 1 then
    local playerPosition = ac.getCarLeaderboardPosition(0)
    if playerPosition <= 0 then
      playerPosition = ac.getCar(0).racePosition
    end

    ac.log(string.format('[Street Corsa] Race finished - Position: %d', playerPosition))
    endSession('FINISHED', playerPosition == 1)
  end
end

local function round(value, places)
  if type(value) ~= 'number' then return nil end
  local k = 10 ^ (places or 0)
  return math.floor(value * k + 0.5) / k
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

  local wheels = {}
  for w = 0, 3 do
    pcall(function()
      local wheel = car.wheels[w]
      wheels[#wheels + 1] = {
        tyre_wear = round(wheel.tyreWear, 4),
        tyre_virtual_km = round(wheel.tyreVirtualKM, 3),
        tyre_blown = wheel.isBlown,
        suspension_damage = round(wheel.suspensionDamage, 4)
      }
    end)
  end
  condition.wheels = wheels
  return condition
end

-- The timeslip as the result has it; nil for a car that never left the line, and in a road race
local function timeslipOf(data)
  if raceType ~= 'DRAG' or not data.leftAt then return nil end
  local slip = { reaction_s = round(data.leftAt, 3) }
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

  return {
    driver_name = data.driverName,
    car_name = data.carName,
    car_index = data.carIndex,
    is_player = data.carIndex == 0,
    false_start = data.falseStart,
    disqualified = data.disqualified,
    broke_down = data.brokeDown,
    breakdown = data.breakdown,
    timeslip = timeslipOf(data),
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

  -- Capture final positions, and build the participants in car index order (the player, car 0, first)
  local participants = {}
  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    if data then
      local pos = ac.getCarLeaderboardPosition(carIndex)
      if pos > 0 then
        data.finalPosition = pos
      end
      participants[#participants + 1] = carDataToDict(data)
    end
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
    participants = participants
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
  if not io.move(tempFilename, filename) then
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
  if data.lapsCompleted >= 1 or otherData.lapsCompleted >= 1 then return end

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

  -- Back on the pits menu (the player pressed Escape): nothing to track there
  if sim.isInMainMenu then return end

  if sessionId == nil then
    initializeSession()
    return
  end

  checkFalseStart()
  if sessionEnded then return end

  updateCrashes(dt)
  if sessionEnded then return end

  updateBreakdowns()
  if sessionEnded then return end

  local tick = measureTick()
  sessionDuration = sessionDuration + tick
  updateAllTelemetry(tick)
  updateTimeslips(tick)

  checkRaceFinish()
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

function script.drawUI()
  if not showResultOverlay then return end

  local size = ui.windowSize()
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
