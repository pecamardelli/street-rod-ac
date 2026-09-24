--[[
  SR Race Manager
  Street Rod race session management for Assetto Corsa.

  Features:
  - Auto-starts the race (bypasses pits menu)
  - Crash detection via G-force monitoring
  - Race finish detection with win/lose determination
  - Session data output to JSON (one file per race, written once)
  - Auto-quit via ac.shutdownAssettoCorsa()
]]

-- Version info
local SCRIPT_VERSION = "2.2.0"
-- 1.1: session.context_id, participants[].car_index and participants[].is_player
local SCHEMA_VERSION = "1.1"

-- Crash detection parameters
local HARD_CRASH_THRESHOLD_G = 25.0
local CRASH_COOLDOWN_SECONDS = 8.0

-- Sampling interval (seconds)
local SAMPLING_INTERVAL = 0.1

-- The longest step one tick may account for: a hitch or a pause must not turn into distance or race time
local MAX_TICK_SECONDS = 1.0

-- Messages
local MSG_WIN = "You won a few bucks, not bad!"
local MSG_LOSE = "You lost, sucker!"
local MSG_CRASH = "Lucky you weren't killed!\nBetter luck next time!"

-- State
local sim = ac.getSim()
local playerCar = ac.getCar(0)

-- Session state
local sessionId = nil
local sessionActive = false
-- Latched once the result is written: nothing may start a second session (and a second result file)
-- in the seconds before AC quits
local sessionEnded = false
local sessionStartTime = nil
local sessionEndTime = nil
local sessionDuration = 0.0
local contextId = nil
local lastSimTimeMs = nil

-- Race state
local autoStartAttempted = false
local playerCrashed = false
local playerFinished = false
local raceEnded = false

-- Overlay state
local showResultOverlay = false
local resultMessage = ""

-- Car data storage, keyed by car index 0..carCount-1 (always walked in that order)
local carData = {}
local carCount = 0

-- Forward declaration: endSession writes the output, which is defined further down
local writeSessionOutput

-- Car data class
local function createCarData(carIndex)
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
    prevGForceTotal = 0.0,
    lastCrashCheckTime = -999.0,
    crashIntensities = {},
    crashed = false,
    crashTimestamp = nil,

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
  if not ok then ac.log('[SR Race Manager] Could not read CONTEXT_ID from race.ini: ' .. tostring(value)) end
  return nil
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

-- Generate UUID
local function generateUUID()
  local template = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'
  return (string.gsub(template, '[xy]', function(c)
    local v = (c == 'x') and math.random(0, 15) or math.random(8, 11)
    return string.format('%x', v)
  end))
end

-- Get ISO timestamp
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

  -- Initialize car data for all cars
  carCount = sim.carsCount
  for i = 0, carCount - 1 do
    carData[i] = createCarData(i)
  end

  ac.log('[SR Race Manager] Session started: ' .. sessionId .. ' with ' .. carCount .. ' cars, context ' .. tostring(contextId))
end

local function scheduleQuit(delay, why)
  setTimeout(function()
    ac.log('[SR Race Manager] Quitting AC ' .. why .. '...')
    ac.shutdownAssettoCorsa()
  end, delay)
end

-- End session: write results, show overlay, and schedule quit
local function endSession(result)
  if not sessionActive then return end
  sessionActive = false
  sessionEnded = true

  ac.log('[SR Race Manager] Ending session: ' .. result)

  -- Write session results. Whatever goes wrong there, AC must still quit: the launcher waits for it.
  sessionEndTime = getISOTimestamp()
  local ok, err = pcall(writeSessionOutput)
  if not ok then
    ac.log('[SR Race Manager] ERROR: Writing the session data failed: ' .. tostring(err))
  end

  -- Set overlay message and schedule quit
  if result == "WIN" then
    resultMessage = MSG_WIN
  elseif result == "LOSE" then
    resultMessage = MSG_LOSE
  elseif result == "CRASH" then
    resultMessage = MSG_CRASH
    -- Lock controls and force brakes
    pcall(physics.lockUserControlsFor, 20)
    pcall(physics.forceUserBrakesFor, 20, 1.0)
    showResultOverlay = true
    scheduleQuit(5.0, 'after crash')
    return
  else
    -- TELEPORTED - no overlay, quit immediately
    ac.log('[SR Race Manager] Quitting AC...')
    ac.shutdownAssettoCorsa()
    return
  end

  showResultOverlay = true

  -- Schedule quit after showing result (for road races without teleport)
  -- Drag races will quit earlier via onCarJumped
  scheduleQuit(5.0, 'after result timeout')
end

-- Detect crash for a car
local function detectCrashForCar(car, carIndex, data)
  -- Get G-force (acceleration vector)
  local acc = car.acceleration
  local totalG = math.sqrt(acc.x^2 + acc.y^2 + acc.z^2)

  -- Calculate change in G-force
  local gChange = math.abs(totalG - data.prevGForceTotal)

  -- Check cooldown
  local timeSinceLastCheck = sessionDuration - data.lastCrashCheckTime

  -- Detect hard crash
  if gChange >= HARD_CRASH_THRESHOLD_G and timeSinceLastCheck >= CRASH_COOLDOWN_SECONDS then
    data.crashed = true
    table.insert(data.crashIntensities, gChange)
    data.crashTimestamp = getISOTimestamp()
    data.lastCrashCheckTime = sessionDuration

    -- If player crashed
    if carIndex == 0 then
      playerCrashed = true
      raceEnded = true
      ac.log(string.format('[SR Race Manager] PLAYER CRASHED! Intensity: %.1fG', gChange))
      endSession("CRASH")
    else
      ac.log(string.format('[SR Race Manager] Car %d crashed - Intensity: %.1fG', carIndex, gChange))
    end
  end

  data.prevGForceTotal = totalG
end

-- Update telemetry for all cars
local function updateAllTelemetry(deltaT)
  for carIndex = 0, carCount - 1 do
    local data = carData[carIndex]
    local car = ac.getCar(carIndex)
    if not data or not car then goto continue end

    -- Update race time
    data.totalRaceTimeMs = data.totalRaceTimeMs + (deltaT * 1000.0)

    -- Track laps
    local currentLapCount = car.lapCount
    if currentLapCount > data.prevLapCount then
      data.lapsCompleted = currentLapCount

      -- Record lap time
      local lastLapTimeMs = car.previousLapTimeMs
      if lastLapTimeMs and lastLapTimeMs > 0 then
        if data.bestLapTimeMs == nil or lastLapTimeMs < data.bestLapTimeMs then
          data.bestLapTimeMs = lastLapTimeMs
        end
      end
    end
    data.prevLapCount = currentLapCount

    -- Track max speed
    local speedKmh = car.speedKmh
    if speedKmh > data.maxSpeedKmh then
      data.maxSpeedKmh = speedKmh
    end

    -- Track distance
    data.distanceKm = data.distanceKm + ((speedKmh / 3.6) * deltaT) / 1000.0

    -- Crash detection (only if not already crashed)
    if not data.crashed then
      detectCrashForCar(car, carIndex, data)
      -- A player crash ends the session: the other cars' figures stay as they were at that moment
      if sessionEnded then return end
    end

    ::continue::
  end
end

-- Check if player finished the race
local function checkRaceFinish()
  if raceEnded or playerFinished then return end

  local playerData = carData[0]
  if not playerData then return end

  -- Check if player completed a lap
  if playerData.lapsCompleted >= 1 then
    playerFinished = true
    raceEnded = true

    -- Get position
    local playerPosition = ac.getCarLeaderboardPosition(0)
    if playerPosition <= 0 then
      playerPosition = playerCar.racePosition
    end

    -- Determine win/lose
    local result = playerPosition == 1 and "WIN" or "LOSE"

    ac.log(string.format('[SR Race Manager] Race finished - Position: %d (%s)', playerPosition, result))
    endSession(result)
  end
end

-- Convert car data to output format
local function carDataToDict(data)
  local maxCrash = 0
  for _, g in ipairs(data.crashIntensities) do
    if g > maxCrash then maxCrash = g end
  end

  return {
    driver_name = data.driverName,
    car_name = data.carName,
    car_index = data.carIndex,
    is_player = data.carIndex == 0,
    performance = {
      final_position = data.finalPosition,
      laps_completed = data.lapsCompleted,
      best_lap_time_ms = data.bestLapTimeMs and math.floor(data.bestLapTimeMs * 100 + 0.5) / 100 or nil,
      total_race_time_ms = math.floor(data.totalRaceTimeMs * 100 + 0.5) / 100,
      max_speed_kmh = math.floor(data.maxSpeedKmh * 100 + 0.5) / 100,
      distance_km = math.floor(data.distanceKm * 1000 + 0.5) / 1000
    },
    crash = {
      crashed = data.crashed,
      crash_intensities_g = data.crashIntensities,
      max_crash_intensity_g = math.floor(maxCrash * 100 + 0.5) / 100,
      crash_timestamp = data.crashTimestamp
    }
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

  -- Build output data
  local outputData = {
    metadata = {
      schema_version = SCHEMA_VERSION,
      script_version = SCRIPT_VERSION,
      source = "sr_race_manager",
      generated_at = getISOTimestamp()
    },
    session = {
      session_id = sessionId,
      context_id = contextId,
      start_timestamp = sessionStartTime,
      end_timestamp = sessionEndTime,
      duration_seconds = math.floor(sessionDuration * 100 + 0.5) / 100,
      track_id = trackId,
      track_layout = trackLayout
    },
    participants = participants
  }

  -- Output directory: AC's own documents folder, the shell's Documents (which OneDrive may have moved),
  -- the same one the launcher reads
  local outputDir = ac.getFolder(ac.FolderID.ACDocuments) .. "\\out\\sr_race_manager"

  -- Create directory if needed
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
  ac.log('[SR Race Manager] Session data written to ' .. filename)
end

-- Auto-start logic
local function tryAutoStart()
  if autoStartAttempted then return end

  sim = ac.getSim()

  if sim.isInMainMenu then
    local success = ac.tryToStart(true)
    if success then
      ac.log('[SR Race Manager] Race auto-started!')
      autoStartAttempted = true
    end
  else
    ac.log('[SR Race Manager] Race already active.')
    autoStartAttempted = true
  end
end

-- Seconds of game time since the last tick: the interval is only nominal (callbacks run once per frame
-- at best, later at low FPS), and nothing moves while the game is paused
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

-- Main update loop
setInterval(function()
  -- The result is written: nothing more to track until AC quits
  if sessionEnded then return end

  -- Refresh sim state
  sim = ac.getSim()
  playerCar = ac.getCar(0)

  -- Try auto-start first
  if not autoStartAttempted then
    tryAutoStart()
    return
  end

  -- Initialize session once auto-start is done
  if sessionId == nil then
    initializeSession()
    return
  end

  local dt = measureTick()

  -- Track session duration
  sessionDuration = sessionDuration + dt

  -- Update telemetry
  updateAllTelemetry(dt)

  -- Check for race finish
  if not raceEnded then
    checkRaceFinish()
  end

end, SAMPLING_INTERVAL)

-- Overlay geometry and colours, made once: the HUD callback runs every frame while it shows
local OVERLAY_ORIGIN = vec2(0, 0)
local OVERLAY_BOX_SIZE = vec2(600, 200)
local OVERLAY_TITLE_SIZE = vec2(600, 80)
local OVERLAY_MESSAGE_POS = vec2(0, 90)
local OVERLAY_MESSAGE_SIZE = vec2(600, 90)
local OVERLAY_DIM = rgbm(0, 0, 0, 0.5)
local OVERLAY_BACKGROUND = rgbm(0, 0, 0, 0.85)
local OVERLAY_BORDER = rgbm(1, 1, 1, 0.3)
local overlayScreenSize = vec2()
local overlayBoxPos = vec2()

-- Register HUD callback for drawing result overlay
ui.onExclusiveHUD(function(mode)
  if not showResultOverlay then return end
  if mode ~= 'game' then return end

  local uiState = ac.getUI()
  overlayScreenSize:set(uiState.windowSize.x, uiState.windowSize.y)

  -- Draw fullscreen dim overlay
  ui.beginTransparentWindow('srDimOverlay', OVERLAY_ORIGIN, overlayScreenSize)
  ui.drawRectFilled(OVERLAY_ORIGIN, overlayScreenSize, OVERLAY_DIM)
  ui.endTransparentWindow()

  -- Draw dialog box
  overlayBoxPos:set((overlayScreenSize.x - OVERLAY_BOX_SIZE.x) / 2, (overlayScreenSize.y - OVERLAY_BOX_SIZE.y) / 2 - 50)

  ui.beginTransparentWindow('srResultOverlay', overlayBoxPos, OVERLAY_BOX_SIZE)

  -- Draw background
  ui.drawRectFilled(OVERLAY_ORIGIN, OVERLAY_BOX_SIZE, OVERLAY_BACKGROUND, 10)
  ui.drawRect(OVERLAY_ORIGIN, OVERLAY_BOX_SIZE, OVERLAY_BORDER, 10, 2)

  -- Title
  ui.dwriteTextAligned("Race Over", 40, ui.Alignment.Center, ui.Alignment.Center, OVERLAY_TITLE_SIZE, false, rgbm.colors.white)

  -- Message
  ui.setCursor(OVERLAY_MESSAGE_POS)
  ui.dwriteTextAligned(resultMessage, 24, ui.Alignment.Center, ui.Alignment.Center, OVERLAY_MESSAGE_SIZE, true, rgbm.colors.white)

  ui.endTransparentWindow()
end)

-- Detect car teleport (jump start, lane violation, or post-race reset)
ac.onCarJumped(0, function()
  -- Crash already being handled with its own quit timer
  if playerCrashed then return end

  -- Race ended normally (WIN/LOSE) - just quit now
  if raceEnded then
    ac.log('[SR Race Manager] Car teleported after race end - Quitting AC')
    ac.shutdownAssettoCorsa()
    return
  end

  -- Race hasn't ended - this is a jump start or lane violation
  if not sessionActive then return end

  ac.log('[SR Race Manager] Car was teleported to pits - Quitting race')
  raceEnded = true
  endSession("TELEPORTED")
end)

ac.log('[SR Race Manager] Script loaded')
