--[[
  SR Race Manager
  Street Rod race session management for Assetto Corsa.

  Features:
  - Auto-starts the race (bypasses pits menu)
  - Crash detection via G-force monitoring
  - Race finish detection with win/lose determination
  - Session data output to JSON
  - Auto-quit via ac.shutdownAssettoCorsa()
]]

-- Version info
local SCRIPT_VERSION = "2.1.0"
local SCHEMA_VERSION = "1.0"

-- Crash detection parameters
local HARD_CRASH_THRESHOLD_G = 50.0
local CRASH_COOLDOWN_SECONDS = 5.0

-- Sampling interval (seconds)
local SAMPLING_INTERVAL = 0.1

-- Messages
local MSG_WIN = "You won a few bucks, not bad!"
local MSG_LOSE = "You lost, sucker!"
local MSG_CRASH = "Lucky you weren't killed!\nBetter luck next time!"

-- State
local sim = ac.getSim()
local playerCar = ac.getCar(0)

-- Seed random number generator for UUID generation
math.randomseed(os.time() + (os.clock() * 1000))

-- Session state
local sessionId = nil
local sessionActive = false
local sessionStartTime = nil
local sessionEndTime = nil
local sessionDuration = 0.0
local accumulatedDelta = 0.0

-- Race state
local autoStartAttempted = false
local playerCrashed = false
local playerFinished = false
local raceEnded = false

-- Overlay state
local showResultOverlay = false
local resultMessage = ""

-- Car data storage
local carData = {}

-- Car data class
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
    prevGForceTotal = 0.0,
    lastCrashCheckTime = -999.0,
    crashIntensities = {},
    crashed = false,
    crashTimestamp = nil,

    -- Final result
    finalPosition = nil
  }
end

-- Generate UUID
local function generateUUID()
  local template = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'
  return string.gsub(template, '[xy]', function(c)
    local v = (c == 'x') and math.random(0, 15) or math.random(8, 11)
    return string.format('%x', v)
  end)
end

-- Get ISO timestamp
local function getISOTimestamp()
  return os.date("!%Y-%m-%dT%H:%M:%SZ")
end

-- Initialize session
local function initializeSession()
  sessionId = generateUUID()
  sessionStartTime = getISOTimestamp()
  sessionActive = true

  -- Initialize car data for all cars
  local totalCars = sim.carsCount
  for i = 0, totalCars - 1 do
    carData[i] = createCarData(i)
  end

  ac.log('[SR Race Manager] Session started: ' .. sessionId .. ' with ' .. totalCars .. ' cars')
end

-- End session: write results and show overlay (quit handled by jump detector)
local function endSession(result)
  if not sessionActive then return end
  sessionActive = false

  ac.log('[SR Race Manager] Ending session: ' .. result)

  -- Write session results
  sessionEndTime = getISOTimestamp()
  writeSessionOutput()

  -- Set overlay message (jump detector will quit when car is teleported)
  if result == "WIN" then
    resultMessage = MSG_WIN
  elseif result == "LOSE" then
    resultMessage = MSG_LOSE
  elseif result == "CRASH" then
    resultMessage = MSG_CRASH
  else
    -- TELEPORTED - no overlay, quit immediately
    ac.log('[SR Race Manager] Quitting AC...')
    ac.shutdownAssettoCorsa()
    return
  end
  showResultOverlay = true
end

-- Detect crash for a car
local function detectCrashForCar(carIndex, data)
  local car = ac.getCar(carIndex)
  if not car then return end

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
  for carIndex, data in pairs(carData) do
    local car = ac.getCar(carIndex)
    if not car then goto continue end

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
      detectCrashForCar(carIndex, data)
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
function writeSessionOutput()
  local trackId = ac.getTrackID()
  local trackLayout = ac.getTrackLayout()
  if trackLayout == "" then trackLayout = nil end

  -- Capture final positions
  for carIndex, data in pairs(carData) do
    local pos = ac.getCarLeaderboardPosition(carIndex)
    if pos > 0 then
      data.finalPosition = pos
    end
  end

  -- Build participants array
  local participants = {}
  for _, data in pairs(carData) do
    table.insert(participants, carDataToDict(data))
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
      start_timestamp = sessionStartTime,
      end_timestamp = sessionEndTime,
      duration_seconds = math.floor(sessionDuration * 100 + 0.5) / 100,
      track_id = trackId,
      track_layout = trackLayout
    },
    participants = participants
  }

  -- Output directory
  local documentsPath = os.getenv("USERPROFILE") .. "\\Documents"
  local outputDir = documentsPath .. "\\Assetto Corsa\\out\\sr_race_manager"

  -- Create directory if needed
  io.createDir(outputDir)

  -- Write JSON file
  local filename = outputDir .. "\\" .. sessionId .. ".json"
  local json = JSON.stringify(outputData)

  local success = io.save(filename, json)
  if success then
    ac.log('[SR Race Manager] Session data written to ' .. filename)
  else
    ac.log('[SR Race Manager] ERROR: Failed to write session data')
  end
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

-- Main update loop
setInterval(function()
  -- Refresh sim state
  sim = ac.getSim()
  playerCar = ac.getCar(0)

  -- Try auto-start first
  if not autoStartAttempted then
    tryAutoStart()
    return
  end

  -- Initialize session once auto-start is done
  if not sessionActive then
    initializeSession()
  end

  -- Track session duration (approximate using interval)
  sessionDuration = sessionDuration + SAMPLING_INTERVAL

  -- Update telemetry
  updateAllTelemetry(SAMPLING_INTERVAL)

  -- Check for race finish
  if not raceEnded then
    checkRaceFinish()
  end

end, SAMPLING_INTERVAL)

-- Register HUD callback for drawing result overlay
ui.onExclusiveHUD(function(mode)
  if not showResultOverlay then return end
  if mode ~= 'game' then return end

  local uiState = ac.getUI()
  local screenSize = vec2(uiState.windowSize.x, uiState.windowSize.y)

  -- Draw fullscreen dim overlay
  ui.beginTransparentWindow('srDimOverlay', vec2(0, 0), screenSize)
  ui.drawRectFilled(vec2(0, 0), screenSize, rgbm(0, 0, 0, 0.5))
  ui.endTransparentWindow()

  -- Draw dialog box
  local boxSize = vec2(600, 200)
  local boxPos = vec2((screenSize.x - boxSize.x) / 2, (screenSize.y - boxSize.y) / 2 - 50)

  ui.beginTransparentWindow('srResultOverlay', boxPos, boxSize)

  -- Draw background
  ui.drawRectFilled(vec2(0, 0), boxSize, rgbm(0, 0, 0, 0.85), 10)
  ui.drawRect(vec2(0, 0), boxSize, rgbm(1, 1, 1, 0.3), 10, 2)

  -- Title
  ui.dwriteTextAligned("Race Over", 40, ui.Alignment.Center, ui.Alignment.Center, vec2(boxSize.x, 80), false, rgbm.colors.white)

  -- Message
  ui.setCursor(vec2(0, 90))
  ui.dwriteTextAligned(resultMessage, 24, ui.Alignment.Center, ui.Alignment.Center, vec2(boxSize.x, 90), true, rgbm.colors.white)

  ui.endTransparentWindow()
end)

-- Detect car teleport (jump start, lane violation, or post-race reset)
ac.onCarJumped(0, function()
  -- Race ended normally - just quit now
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
